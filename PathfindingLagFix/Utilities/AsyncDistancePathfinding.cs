using System;
using System.Collections;

using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLib.Utilities;

#if BENCHMARKING
using Unity.Profiling;
#endif

namespace PathfindingLagFix.Utilities;

internal static class AsyncDistancePathfinding
{
    internal const float DEFAULT_CAP_DISTANCE = 60;

    private static readonly IDMap<EnemyDistancePathfindingStatus> Statuses = new(() => new EnemyDistancePathfindingStatus(), 1);

    internal static void RemoveStatus(EnemyAI enemy)
    {
        Statuses[enemy.thisEnemyIndex] = new EnemyDistancePathfindingStatus();
    }

    internal class EnemyDistancePathfindingStatus
    {
        public Coroutine Coroutine;
        public int CurrentSearchTypeID = -1;

        public GameObject[] AINodes;
        public Transform[] SortedNodes;
        public Vector3[] SortedPositions;

        public FindPathsToNodesJob Job;
        public JobHandle JobHandle;

        public Transform ChosenNode;
        public float MostOptimalDistance = float.PositiveInfinity;

        public void SortNodes(EnemyAI enemy, Vector3 target, bool furthestFirst)
        {
            var count = enemy.allAINodes.Length;
            if (enemy.allAINodes != AINodes)
            {
                AINodes = enemy.allAINodes;
                SortedNodes = new Transform[count];
                SortedPositions = new Vector3[count];

                for (int i = 0; i < count; i++)
                {
                    var transform = AINodes[i].transform;
                    SortedNodes[i] = transform;
                    SortedPositions[i] = transform.position;
                }
            }

            static void Swap<T>(ref T a, ref T b)
            {
                (a, b) = (b, a);
            }

            // Use an insertion sort to reorder the nodes since they will be mostly sorted.
            for (int i = 1; i < count; i++)
            {
                var current = (SortedPositions[i] - target).sqrMagnitude;
                for (int j = i; j > 0; j--)
                {
                    var neighbor = (SortedPositions[j - 1] - target).sqrMagnitude;
                    if ((neighbor <= current) ^ furthestFirst)
                        break;
                    Swap(ref SortedNodes[j - 1], ref SortedNodes[j]);
                    Swap(ref SortedPositions[j - 1], ref SortedPositions[j]);
                }
            }
        }

        public Transform RetrieveChosenNode(out float mostOptimalDistance)
        {
            var chosenNode = ChosenNode;
            mostOptimalDistance = MostOptimalDistance;

            if (chosenNode == null)
                return null;

            ChosenNode = null;
            MostOptimalDistance = float.PositiveInfinity;
            CurrentSearchTypeID = -1;

            return chosenNode;
        }

        ~EnemyDistancePathfindingStatus()
        {
            Job.FreeAllResources();
        }
    }

#if BENCHMARKING
    private static readonly ProfilerMarker StartJobsMarker = new("Start Jobs");
    private static readonly ProfilerMarker SortMarker = new("Sort Nodes");
    private static readonly ProfilerMarker ScheduleMarker = new("Schedule Job");
#endif

    internal static EnemyDistancePathfindingStatus StartJobs(EnemyAI enemy, EnemyDistancePathfindingStatus status, Vector3 target, int count, bool farthestFirst, bool calculateDistance = false)
    {
#if BENCHMARKING
        using var startJobsMarkerAuto = StartJobsMarker.Auto();
#endif

        var agent = enemy.agent;
        var position = agent.GetPathOrigin();

#if BENCHMARKING
        using var sortMarkerAuto = new TogglableProfilerAuto(SortMarker);
#endif
        status.SortNodes(enemy, target, farthestFirst);
#if BENCHMARKING
        sortMarkerAuto.Pause();
#endif

        ref var job = ref status.Job;
        job.Initialize(agent.agentTypeID, agent.areaMask, position, status.SortedPositions, calculateDistance);

#if BENCHMARKING
        using var scheduleMarkerAuto = new TogglableProfilerAuto(ScheduleMarker);
#endif
        status.JobHandle = job.ScheduleByRef(count, default);
#if BENCHMARKING
        scheduleMarkerAuto.Pause();
#endif

        return status;
    }

    internal delegate IEnumerator NodeSelectionCoroutine(EnemyDistancePathfindingStatus status);

    internal static EnemyDistancePathfindingStatus StartChoosingNode(EnemyAI enemy, int searchTypeID, NodeSelectionCoroutine coroutine)
    {
        var status = Statuses[enemy.thisEnemyIndex];
        if (status.CurrentSearchTypeID == searchTypeID)
            return status;
        if (status.Coroutine != null)
        {
            status.Job.Cancel();
            return status;
        }

        status.RetrieveChosenNode(out _);
        status.CurrentSearchTypeID = searchTypeID;
        status.Coroutine = enemy.StartCoroutine(coroutine(status));
        return status;
    }

    private static EnemyDistancePathfindingStatus StartChoosingNode(EnemyAI enemy, int searchTypeID, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        return StartChoosingNode(enemy, searchTypeID, status => ChooseNodeCoroutine(enemy, status, target, farthestFirst, avoidLineOfSight, offset, capDistance));
    }

    internal static EnemyDistancePathfindingStatus StartChoosingFarthestNodeFromPosition(EnemyAI enemy, int searchTypeID, Vector3 target, bool avoidLineOfSight = false, int offset = 0, float capDistance = 0)
    {
        return StartChoosingNode(enemy, searchTypeID, target, farthestFirst: true, avoidLineOfSight, offset, capDistance);
    }

    internal static EnemyDistancePathfindingStatus StartChoosingClosestNodeToPosition(EnemyAI enemy, int searchTypeID, Vector3 target, bool avoidLineOfSight = false, int offset = 0, float capDistance = 0)
    {
        return StartChoosingNode(enemy, searchTypeID, target, farthestFirst: false, avoidLineOfSight, offset, capDistance);
    }

    internal static IEnumerator ChooseNodeCoroutine(EnemyAI enemy, EnemyDistancePathfindingStatus status, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        if (enemy.allAINodes.Length == 0 || !enemy.agent.isOnNavMesh)
        {
            status.ChosenNode = enemy.transform;
            status.MostOptimalDistance = 0;
            yield break;
        }

        var candidateCount = enemy.allAINodes.Length;
        StartJobs(enemy, status, target, candidateCount, farthestFirst);
        var job = status.Job;
        var jobHandle = status.JobHandle;

        int result = -1;

        var capDistanceSqr = capDistance * capDistance;

        while (result == -1)
        {
            yield return null;
            bool complete = true;
            var pathsLeft = Math.Min(offset, candidateCount - 1);

            var enemyPosition = enemy.transform.position;

            for (int i = 0; i < candidateCount; i++)
            {
                if (capDistanceSqr > 0 && (status.SortedPositions[i] - enemyPosition).sqrMagnitude > capDistanceSqr)
                    continue;

                var nodeStatus = job.Statuses[i];
                if (nodeStatus.GetResult() == PathQueryStatus.InProgress)
                {
                    complete = false;
                    break;
                }

                if (nodeStatus.GetResult() == PathQueryStatus.Success)
                {
                    var path = job.GetPath(i);
                    if (path[0].polygon.IsNull())
                    {
                        Plugin.Instance.Logger.LogWarning($"{i}: Path is null");
                        continue;
                    }

                    if (avoidLineOfSight && LineOfSight.PathIsBlockedByLineOfSight(path))
                        continue;

                    if (pathsLeft-- == 0)
                    {
                        result = i;
                        break;
                    }

                    continue;
                }
            }
            if (complete)
                break;
        }

        job.Cancel();

        if (result == -1)
        {
            switch (ConfigOptions.CurrentOptions.DistancePathfindingFallbackNodeSelection)
            {
                case DistancePathfindingFallbackNodeSelectionType.BestPathable:
                    for (var i = 0; i < candidateCount; i++)
                    {
                        if (job.Statuses[i].GetResult() == PathQueryStatus.Success)
                        {
                            result = i;
                            break;
                        }
                    }
                    break;
                case DistancePathfindingFallbackNodeSelectionType.Vanilla:
                    result = 0;
                    break;
                case DistancePathfindingFallbackNodeSelectionType.DontMove:
                    break;
            }
        }

        if (result == -1)
        {
            status.ChosenNode = enemy.transform;
            status.MostOptimalDistance = 0;
        }
        else
        {
            status.ChosenNode = status.SortedNodes[result];
            status.MostOptimalDistance = Vector3.Distance(target, status.SortedPositions[result]);
        }

        while (!jobHandle.IsCompleted)
            yield return null;

        status.Coroutine = null;
    }
}
