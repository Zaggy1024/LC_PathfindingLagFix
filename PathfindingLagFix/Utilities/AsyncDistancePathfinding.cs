using System.Collections;

using Unity.Jobs;
#if BENCHMARKING
using Unity.Profiling;
#endif
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

#if BENCHMARKING
    static double create = 0;
    static double sort = 0;
#endif

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
#if BENCHMARKING
            create = Time.realtimeSinceStartupAsDouble;
#endif

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

#if BENCHMARKING
            create = Time.realtimeSinceStartupAsDouble - create;
            Plugin.Instance.Logger.LogInfo($"Creating arrays took {create * 1_000_000} microseconds");
#endif

            static void Swap<T>(ref T a, ref T b)
            {
                (a, b) = (b, a);
            }

#if BENCHMARKING
            sort = Time.realtimeSinceStartupAsDouble;
#endif

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

#if BENCHMARKING
            sort = Time.realtimeSinceStartupAsDouble - sort;
            Plugin.Instance.Logger.LogInfo($"Sorting took {sort * 1_000_000} microseconds");
#endif
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
        var position = enemy.transform.position;

        // NavMeshAgent.CalculatePath() starts from the current off-mesh link's end position to allow agents
        // passing through off-mesh links to continue calculating paths. Without this, we get a little pause
        // when exiting an off-mesh link.
        if (agent.isOnOffMeshLink)
            position = agent.currentOffMeshLinkData.endPos;

        status.SortNodes(enemy, target, farthestFirst);

#if BENCHMARKING
        var initialize = Time.realtimeSinceStartupAsDouble;
#endif

        ref var job = ref status.Job;
        job.Initialize(agent.agentTypeID, agent.areaMask, position, status.SortedPositions);

#if BENCHMARKING
        initialize = Time.realtimeSinceStartupAsDouble - initialize;
        Plugin.Instance.Logger.LogInfo($"Initializing took {initialize * 1_000_000} microseconds");

        var schedule = Time.realtimeSinceStartupAsDouble;
#endif

        status.JobHandle = job.ScheduleByRef(count, default);

#if BENCHMARKING
        schedule = Time.realtimeSinceStartupAsDouble - schedule;
        Plugin.Instance.Logger.LogInfo($"Scheduling took {schedule * 1_000_000} microseconds");

        Plugin.Instance.Logger.LogInfo($"Total: {(create + sort + initialize + schedule) * 1_000_000}");
#endif

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

#if BENCHMARKING
    private static readonly ProfilerMarker startJobsProfilerMarker = new("StartJobs");
#endif

    internal static IEnumerator ChooseFarthestNodeFromPosition(EnemyAI enemy, EnemyDistancePathfindingStatus status, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        if (!enemy.agent.isOnNavMesh)
            yield break;

        var candidateCount = enemy.allAINodes.Length;
#if BENCHMARKING
        using (var startJobsProfilerMarkerAuto = startJobsProfilerMarker.Auto())
#endif
        StartJobs(enemy, status, target, candidateCount, farthestFirst);
        var job = status.Job;
        var jobHandle = status.JobHandle;

        int result = -1;

#if BENCHMARKING
        int failedCount = 0;
        var totalTime = 0d;
#endif

        var capDistanceSqr = capDistance * capDistance;

        while (result == -1)
        {
            yield return null;
            var startTime = Time.realtimeSinceStartupAsDouble;
            bool complete = true;
            var pathsLeft = offset;

#if BENCHMARKING
            failedCount = 0;
#endif

            var enemyPosition = enemy.transform.position;

            for (int i = 0; i < candidateCount; i++)
            {
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
                            if (capDistanceSqr > 0 && (segmentEndPos - enemyPosition).sqrMagnitude > capDistanceSqr)
                                break;

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

#if BENCHMARKING
                failedCount++;
#endif
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

#if BENCHMARKING
            totalTime += Time.realtimeSinceStartupAsDouble - startTime;
#endif
        }

        job.Canceled[0] = true;

#if BENCHMARKING
        Plugin.Instance.Logger.LogInfo($"Finding final path took {totalTime * 1_000_000} microseconds");
        if (result != -1)
            Plugin.Instance.Logger.LogInfo($"Chose path {result}/{candidateCount} with {failedCount} failures: {status.SortedPositions[result]} ({status.SortedNodes[result].transform.position})");
        else
            Plugin.Instance.Logger.LogInfo($"Failed to choose path out of {candidateCount} candidates with {failedCount} failures");
#endif
        if (result == -1 && status.SortedNodes.Length > 0)
        {
            Plugin.Instance.Logger.LogInfo($"Defaulting to result = 0");
            result = 0;
        }

        if (result >= 0)
        {
            status.ChosenNode = status.SortedNodes[result];
            status.MostOptimalDistance = Vector3.Distance(target, status.SortedPositions[result]);
        }

        while (!jobHandle.IsCompleted)
            yield return null;

#if BENCHMARKING
        Plugin.Instance.Logger.LogInfo($"Job completed fully. Disposing.");
#endif

        status.Coroutine = null;
    }
}
