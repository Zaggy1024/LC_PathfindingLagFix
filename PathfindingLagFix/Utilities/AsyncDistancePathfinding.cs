using System.Collections;

using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities;

internal static class AsyncDistancePathfinding
{
    private const int LINE_OF_SIGHT_LAYER_MASK = 0x40000;

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

    private static EnemyDistancePathfindingStatus StartJobs(EnemyAI enemy, EnemyDistancePathfindingStatus status, Vector3 target, int count, bool farthestFirst)
    {
        var agent = enemy.agent;
        var position = agent.GetAgentPosition();
        status.SortNodes(enemy, target, farthestFirst);

        ref var job = ref status.Job;
        job.Initialize(agent.agentTypeID, agent.areaMask, position, status.SortedPositions);

        status.JobHandle = job.ScheduleByRef(count, default);

        return status;
    }

    internal static EnemyDistancePathfindingStatus StartChoosingNode(EnemyAI enemy, int searchTypeID, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        var status = Statuses[enemy.thisEnemyIndex];
        if (status.CurrentSearchTypeID == searchTypeID)
            return status;
        if (status.Coroutine != null)
        {
            status.Job.Cancel();
            return status;
        }

        status.CurrentSearchTypeID = searchTypeID;
        status.Coroutine = enemy.StartCoroutine(ChooseFarthestNodeFromPosition(enemy, status, target, farthestFirst, avoidLineOfSight, offset, capDistance));
        return status;
    }

    internal static EnemyDistancePathfindingStatus StartChoosingFarthestNodeFromPosition(EnemyAI enemy, int searchTypeID, Vector3 target, bool avoidLineOfSight = false, int offset = 0, float capDistance = 0)
    {
        return StartChoosingNode(enemy, searchTypeID, target, farthestFirst: true, avoidLineOfSight, offset, capDistance);
    }

    internal static EnemyDistancePathfindingStatus StartChoosingClosestNodeToPosition(EnemyAI enemy, int searchTypeID, Vector3 target, bool avoidLineOfSight = false, int offset = 0, float capDistance = 0)
    {
        return StartChoosingNode(enemy, searchTypeID, target, farthestFirst: false, avoidLineOfSight, offset, capDistance);
    }

    internal static IEnumerator ChooseFarthestNodeFromPosition(EnemyAI enemy, EnemyDistancePathfindingStatus status, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        if (!enemy.agent.isOnNavMesh)
            yield break;

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
            var pathsLeft = offset;

            var enemyPosition = enemy.transform.position;

            for (int i = 0; i < candidateCount; i++)
            {
                if (capDistanceSqr > 0 && (status.SortedPositions[i] - enemyPosition).sqrMagnitude > capDistanceSqr)
                    break;

                var nodeStatus = job.Statuses[i];
                if (nodeStatus.GetStatus() == PathQueryStatus.InProgress)
                {
                    complete = false;
                    break;
                }

                if (nodeStatus.GetStatus() == PathQueryStatus.Success)
                {
                    var path = job.GetPath(i);
                    if (path[0].polygon.IsNull())
                    {
                        Plugin.Instance.Logger.LogWarning($"{i}: Path is null");
                        continue;
                    }

                    if (avoidLineOfSight && path.Length > 1)
                    {
                        // Check if any segment of the path enters a player's line of sight.
                        bool pathObstructed = false;
                        var segmentStartPos = path[0].position;
                        
                        for (int segment = 1; segment < path.Length && segment < 16 && !pathObstructed; segment++)
                        {
                            var segmentEndPos = path[segment].position;
                            if (Physics.Linecast(segmentStartPos, segmentEndPos, LINE_OF_SIGHT_LAYER_MASK))
                                pathObstructed = true;
                            segmentStartPos = segmentEndPos;
                        }
                        if (pathObstructed)
                            continue;
                    }

                    if (pathsLeft-- == 0)
                    {
                        result = i;
                        break;
                    }

                    continue;
                }
            }
            // If all line of sight checks fail, we will find the furthest reachable path to allow the pathfinding to succeed once we set the target.
            // The vanilla version of this function may return an unreachable location, so the NavMeshAgent will reset it to a reachable location and
            // cause a delay in the enemy's movement.
            if (complete && result == -1)
            {
                for (int i = 0; i < candidateCount; i++)
                {
                    if (job.Statuses[i].GetStatus() == PathQueryStatus.Success)
                    {
                        result = i;
                        break;
                    }
                }
                break;
            }
        }

        job.Canceled[0] = true;

        if (result == -1 && status.SortedNodes.Length > 0)
            result = 0;

        if (result >= 0)
        {
            status.ChosenNode = status.SortedNodes[result];
            status.MostOptimalDistance = Vector3.Distance(target, status.SortedPositions[result]);
        }

        while (!jobHandle.IsCompleted)
            yield return null;

        status.Coroutine = null;
    }
}
