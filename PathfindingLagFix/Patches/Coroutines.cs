//#define DRAW_LINES

using System.Collections.Generic;

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLagFix.Utilities;

namespace PathfindingLagFix.Patches
{
    public static class Coroutines
    {
        private const int LINE_OF_SIGHT_LAYER_MASK = 0x40000;
        private const float MAX_ORIGIN_DISTANCE = 5;
        private const float MAX_ENDPOINT_DISTANCE = 1.5f;
        private const float MAX_ENDPOINT_DISTANCE_SQR = MAX_ENDPOINT_DISTANCE * MAX_ENDPOINT_DISTANCE;

        private static readonly NavMeshQueryPool QueryPool = new(256);
        private static readonly IDMap<EnemyPathfindingStatus> EnemyPathfindingStatuses = new();

        static double create = 0;
        static double distances = 0;
        static double sort = 0;

        public struct EnemyPathfindingStatus()
        {
            public GameObject[] AINodes;
            public Transform[] SortedNodes;
            public Vector3[] SortedPositions;
            //public float[] SortedSqrDistances;

            public FindPathToNodeJob Job;
            public JobHandle JobHandle;

            public void SortNodes(EnemyAI enemy, Vector3 target, bool furthestFirst)
            {
                create = Time.realtimeSinceStartupAsDouble;

                var count = enemy.allAINodes.Length;
                if (enemy.allAINodes != AINodes)
                {
                    AINodes = enemy.allAINodes;
                    SortedNodes = new Transform[count];
                    SortedPositions = new Vector3[count];
                    //SortedSqrDistances = new float[count];

                    for (int i = 0; i < count; i++)
                    {
                        var transform = AINodes[i].transform;
                        SortedNodes[i] = transform;
                        SortedPositions[i] = transform.position;
                    }
                }
                create = Time.realtimeSinceStartupAsDouble - create;
                Plugin.Instance.Logger.LogInfo($"Creating arrays took {create * 1_000_000} microseconds");

                static void Swap<T>(ref T a, ref T b)
                {
                    (a, b) = (b, a);
                }

                sort = Time.realtimeSinceStartupAsDouble;
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
                sort = Time.realtimeSinceStartupAsDouble - sort;
                Plugin.Instance.Logger.LogInfo($"Sorting took {sort * 1_000_000} microseconds");
            }
        }

        //[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        public struct FindPathToNodeJob : IJobParallelFor
        {
            [ReadOnly] internal int AgentTypeID;
            [ReadOnly] internal int AreaMask;
            [ReadOnly] internal Vector3 Origin;
            [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<Vector3> Destinations;

            [ReadOnly, NativeDisableContainerSafetyRestriction] internal NativeArray<NavMeshQuery> Queries;

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

            public NativeArray<NavMeshLocation> GetPath(int index)
            {
                return Paths.GetSubArray(index * Pathfinding.MAX_STRAIGHT_PATH, Pathfinding.MAX_STRAIGHT_PATH);
            }

            public void Execute(int index)
            {
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
                var endPosition = GetPath(index)[pathSize - 1].position;
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

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(Coroutines), nameof(ChooseFarthestNodeFromPosition))]
        public static IEnumerator<Transform> Profiler(IEnumerator<Transform> __result, EnemyAI enemy)
        {
            int i = 0;
            while (true)
            {
                var startTime = Time.realtimeSinceStartupAsDouble;
                var hasNext = __result.MoveNext();
                var runTime = Time.realtimeSinceStartupAsDouble - startTime;
                Plugin.Instance.Logger.LogInfo($"Iteration {i} of {enemy.name}: {runTime * 1_000_000} microseconds");
                if (!hasNext)
                    yield break;
                yield return __result.Current;
                i++;
            }
        }*/

        private static EnemyPathfindingStatus StartJobs(EnemyAI enemy, Vector3 target, int count)
        {
            var agent = enemy.agent;
            var position = enemy.transform.position;

            ref var status = ref EnemyPathfindingStatuses[enemy.thisEnemyIndex];

            status.SortNodes(enemy, target, true);

            ref var job = ref status.Job;
            var initialize = Time.realtimeSinceStartupAsDouble;
            job.Initialize(agent.agentTypeID, agent.areaMask, position, status.SortedPositions);
            initialize = Time.realtimeSinceStartupAsDouble - initialize;
            Plugin.Instance.Logger.LogInfo($"Initializing took {initialize * 1_000_000} microseconds");

            var schedule = Time.realtimeSinceStartupAsDouble;
            status.JobHandle = job.Schedule(count, 1);
            schedule = Time.realtimeSinceStartupAsDouble - schedule;
            Plugin.Instance.Logger.LogInfo($"Scheduling took {schedule * 1_000_000} microseconds");

            Plugin.Instance.Logger.LogInfo($"Total: {(create + distances + sort + initialize + schedule) * 1_000_000}");

            return status;
        }

        public static IEnumerator<Transform> ChooseFarthestNodeFromPosition(EnemyAI enemy, Vector3 target, bool avoidLineOfSight = false, int offset = 0)
        {
            if (!enemy.agent.isOnNavMesh)
                yield break;

            var startFrame = Time.frameCount;

#if DRAW_LINES
            var colorIndex = 0;
            foreach (var node in enemy.allAINodes)
                UnityEngine.Debug.DrawLine(node.transform.position, node.transform.position + Vector3.up, ColorRotation[colorIndex++ % ColorRotation.Length], 0.15f);
#endif

            var candidateCount = enemy.allAINodes.Length;
            var status = StartJobs(enemy, target, candidateCount);
            var job = status.Job;
            var jobHandle = status.JobHandle;

            int result = -1;
            var totalTime = 0d;

            while (result == -1)
            {
                yield return null;
                var startTime = Time.realtimeSinceStartupAsDouble;
                bool complete = true;
                var pathsLeft = offset;
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
                            Plugin.Instance.Logger.LogWarning($"{i}: Path is null");

                        if (avoidLineOfSight)
                        {
                            // Check if any segment of the path enters a player's line of sight.
                            bool pathObstructed = false;
                            for (int segment = 1; segment < path.Length && !pathObstructed; segment++)
                            {
                                if (Physics.Linecast(path[segment - 1].position, path[segment].position, LINE_OF_SIGHT_LAYER_MASK))
                                    pathObstructed = true;
                            }
                            if (pathObstructed)
                                continue;
                        }

                        if (pathsLeft-- == 0)
                        {
                            result = i;
                            break;
                        }
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
                totalTime += Time.realtimeSinceStartupAsDouble - startTime;
            }
            Plugin.Instance.Logger.LogInfo($"Finding final path took {totalTime * 1_000_000} microseconds, path {(result >= 0 ? "exists" : "doesn't exist")}");

            if (result >= 0)
            {
                enemy.mostOptimalDistance = Vector3.Distance(target, status.SortedPositions[result]);
                yield return status.SortedNodes[result];
            }

            while (!jobHandle.IsCompleted)
                yield return null;

#if DRAW_LINES
            {
                for (int i = 0; i < candidateCount; i++)
                {
                    if (job.Statuses[i].GetStatus() == PathQueryStatus.Success)
                    {
                        var path = job.GetPath(i);
                        var pathSize = job.PathSizes[i];
                        for (int segment = 1; segment < pathSize; segment++)
                            UnityEngine.Debug.DrawLine(path[segment - 1].position, path[segment].position, ColorRotation[i % ColorRotation.Length], i == result ? 0.2f : 0.1f);
                    }
                }
            }
#endif

            job.Dispose();
        }
        private static readonly Color[] ColorRotation = [Color.red, Color.yellow, Color.green, Color.blue, Color.cyan, Color.grey];
    }
}
