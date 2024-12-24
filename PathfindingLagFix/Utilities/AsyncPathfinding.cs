using System.Collections;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities;

public static class AsyncPathfinding
{
    private const int LINE_OF_SIGHT_LAYER_MASK = 0x40000;
    private const float MAX_ORIGIN_DISTANCE = 5;
    private const float MAX_ENDPOINT_DISTANCE = 1.5f;
    private const float MAX_ENDPOINT_DISTANCE_SQR = MAX_ENDPOINT_DISTANCE * MAX_ENDPOINT_DISTANCE;

    internal const float DEFAULT_CAP_DISTANCE = 60;

    private static readonly NavMeshQueryPool QueryPool = new(256);
    private static readonly IDMap<EnemyPathfindingStatus> EnemyPathfindingStatuses = new(() => new EnemyPathfindingStatus(), 1);

#if BENCHMARKING
    static double create = 0;
    static double sort = 0;
#endif

    internal class EnemyPathfindingStatus
    {
        public Coroutine Coroutine;
        public int CurrentSearchTypeID = -1;

        public GameObject[] AINodes;
        public Transform[] SortedNodes;
        public Vector3[] SortedPositions;

        public FindPathToNodeJob Job;
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
    }

    //[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct FindPathToNodeJob : IJobFor
    {
        [ReadOnly] internal int AgentTypeID;
        [ReadOnly] internal int AreaMask;
        [ReadOnly] internal Vector3 Origin;
        [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<Vector3> Destinations;

        [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<NavMeshQuery> Queries;

        [ReadOnly] internal NativeArray<bool> Canceled;

        [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<PathQueryStatus> Statuses;
        [WriteOnly, NativeDisableContainerSafetyRestriction, NativeDisableParallelForRestriction] internal NativeArray<NavMeshLocation> Paths;
        [WriteOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<int> PathSizes;

        public void Initialize(int agentTypeID, int areaMask, Vector3 origin, Vector3[] candidates)
        {
            AgentTypeID = agentTypeID;
            AreaMask = areaMask;
            Origin = origin;

            var count = candidates.Length;
            EnsureCount(count);

            if (Canceled == default)
                Canceled = new(1, Allocator.Persistent);
            Canceled[0] = false;

            for (var i = 0; i < count; i++)
                Statuses[i] = PathQueryStatus.InProgress;

            QueryPool.Take(Queries, count);
            Destinations.CopyFrom(candidates);
        }

        private void EnsureCount(int count)
        {
            if (Destinations.Length >= count)
                return;

            if (Destinations.IsCreated)
            {
                Destinations.Dispose();
                Queries.Dispose();

                Statuses.Dispose();
                Paths.Dispose();
                PathSizes.Dispose();
            }
            Destinations = new(count, Allocator.Persistent);
            Queries = new(count, Allocator.Persistent);

            Statuses = new(count, Allocator.Persistent);
            Paths = new(count * Pathfinding.MAX_STRAIGHT_PATH, Allocator.Persistent);
            PathSizes = new(count, Allocator.Persistent);
        }

        public NativeArray<NavMeshLocation> GetPathBuffer(int index)
        {
            return Paths.GetSubArray(index * Pathfinding.MAX_STRAIGHT_PATH, Pathfinding.MAX_STRAIGHT_PATH);
        }

        public NativeArray<NavMeshLocation> GetPath(int index)
        {
            return Paths.GetSubArray(index * Pathfinding.MAX_STRAIGHT_PATH, PathSizes[index]);
        }

        public void Execute(int index)
        {
            if (Canceled[0])
            {
                Statuses[index] = PathQueryStatus.Failure;
                return;
            }

            var query = Queries[index];
            var originExtents = new Vector3(MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE, MAX_ORIGIN_DISTANCE);
            var origin = query.MapLocation(Origin, originExtents, AgentTypeID, AreaMask);

            if (!query.IsValid(origin.polygon))
            {
                Statuses[index] = PathQueryStatus.Failure;
                return;
            }

            var destination = Destinations[index];
            var destinationExtents = new Vector3(MAX_ENDPOINT_DISTANCE, MAX_ENDPOINT_DISTANCE, MAX_ENDPOINT_DISTANCE);
            var destinationLocation = query.MapLocation(destination, destinationExtents, AgentTypeID, AreaMask);
            if (!query.IsValid(destinationLocation))
            {
                Statuses[index] = PathQueryStatus.Failure;
                return;
            }
            
            query.BeginFindPath(origin, destinationLocation, AreaMask);

            PathQueryStatus status = PathQueryStatus.InProgress;
            while (status.GetStatus() == PathQueryStatus.InProgress)
                status = query.UpdateFindPath(int.MaxValue, out int _);

            if (status.GetStatus() != PathQueryStatus.Success)
            {
                Statuses[index] = status;
                return;
            }

            var pathNodes = new NativeArray<PolygonId>(Pathfinding.MAX_PATH_SIZE, Allocator.Temp);
            status = query.EndFindPath(out var pathNodesSize);
            query.GetPathResult(pathNodes);

            var straightPathStatus = Pathfinding.FindStraightPath(query, Origin, destination, pathNodes, pathNodesSize, GetPath(index), out var pathSize);
            PathSizes[index] = pathSize;
            pathNodes.Dispose();

            if (straightPathStatus.GetStatus() != PathQueryStatus.Success)
            {
                Statuses[index] = status;
                return;
            }

            // Check if the end of the path is close enough to the target.
            var endPosition = GetPathBuffer(index)[pathSize - 1].position;
            var distance = (endPosition - destination).sqrMagnitude;
            if (distance > MAX_ENDPOINT_DISTANCE_SQR)
            {
                Statuses[index] = PathQueryStatus.Failure;
                return;
            }

            Statuses[index] = PathQueryStatus.Success;
        }

        internal void Dispose()
        {
            QueryPool.Free(Queries);
        }
    }

    private static EnemyPathfindingStatus StartJobs(EnemyAI enemy, EnemyPathfindingStatus status, Vector3 target, int count, bool farthestFirst)
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

    internal static EnemyPathfindingStatus StartChoosingNode(EnemyAI enemy, int searchTypeID, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        var status = EnemyPathfindingStatuses[enemy.thisEnemyIndex];
        if (status.CurrentSearchTypeID == searchTypeID)
            return status;
        if (status.Coroutine != null)
        {
            status.Job.Canceled[0] = true;
            return status;
        }

        status.CurrentSearchTypeID = searchTypeID;
        status.Coroutine = enemy.StartCoroutine(ChooseFarthestNodeFromPosition(enemy, status, target, farthestFirst, avoidLineOfSight, offset, capDistance));
        return status;
    }

    internal static EnemyPathfindingStatus StartChoosingFarthestNodeFromPosition(EnemyAI enemy, int searchTypeID, Vector3 target, bool avoidLineOfSight = false, int offset = 0, float capDistance = 0)
    {
        return StartChoosingNode(enemy, searchTypeID, target, farthestFirst: true, avoidLineOfSight, offset, capDistance);
    }

    internal static EnemyPathfindingStatus StartChoosingClosestNodeToPosition(EnemyAI enemy, int searchTypeID, Vector3 target, bool avoidLineOfSight = false, int offset = 0, float capDistance = 0)
    {
        return StartChoosingNode(enemy, searchTypeID, target, farthestFirst: false, avoidLineOfSight, offset, capDistance);
    }

    private static readonly ProfilerMarker startJobsProfilerMarker = new("StartJobs");

    internal static IEnumerator ChooseFarthestNodeFromPosition(EnemyAI enemy, EnemyPathfindingStatus status, Vector3 target, bool farthestFirst, bool avoidLineOfSight, int offset, float capDistance)
    {
        if (!enemy.agent.isOnNavMesh)
            yield break;

        var candidateCount = enemy.allAINodes.Length;
        using (var startJobsProfilerMarkerAuto = startJobsProfilerMarker.Auto())
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

        job.Dispose();
        status.Coroutine = null;
    }
}
