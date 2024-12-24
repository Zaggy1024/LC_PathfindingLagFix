using System;
using System.Collections.Generic;
using System.Diagnostics;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLagFix.Utilities;

namespace PathfindingLagFix.Patches
{
    public static class Coroutines
    {
        private const bool DEBUG_JOB = false;
        private const int LINE_OF_SIGHT_LAYER_MASK = 0x40000;
        private const bool USE_POOL = true;
        private const bool USE_CACHE = true;
        private const float MAX_ENDPOINT_DISTANCE = 1.5f;
        private const float MAX_ENDPOINT_DISTANCE_SQR = MAX_ENDPOINT_DISTANCE * MAX_ENDPOINT_DISTANCE;

        private static readonly Vector3 NodeSearchRange = new Vector3(5, 5, 5);
        private static readonly NavMeshQueryPool QueryPool = new(256);

        private static readonly IDMap<JobCache> EnemyJobCaches = new(5);

        struct JobCache
        {
            private NavMeshQuery[] queries;
            private FindPathToNodeJob[] jobs;
            private JobHandle[] jobHandles;

            public (NavMeshQuery[], FindPathToNodeJob[], JobHandle[]) Get(int count)
            {
                if (queries is null || queries.Length < count)
                {
                    queries = new NavMeshQuery[count];
                    jobs = new FindPathToNodeJob[count];
                    jobHandles = new JobHandle[count];
                }
                return (queries, jobs, jobHandles);
            }
        }

        //[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct FindPathToNodeJob : IJob
        {
            [ReadOnly] internal int Index;
            [ReadOnly] internal Vector3 Origin;
            [ReadOnly] internal Vector3 Destination;

            internal NavMeshQuery Query;

            internal NativeArray<PathQueryStatus> Status;
            internal NativeArray<NavMeshLocation> Path;
            internal NativeArray<int> PathSize;

            internal FindPathToNodeJob(bool valid, int index, NavMeshQuery query, Vector3 origin, Vector3 destination)
            {
                Index = index;

                Origin = origin;
                Destination = destination;
                Query = query;

                Status = new(new PathQueryStatus[] { valid ? PathQueryStatus.InProgress : PathQueryStatus.Failure }, Allocator.TempJob);
                Path = new NativeArray<NavMeshLocation>(Pathfinding.MAX_PATH_SIZE, Allocator.TempJob);
                PathSize = new(new int[] { 0 }, Allocator.TempJob);
            }

            public void Execute()
            {
                if (Status[0].GetStatus() != PathQueryStatus.InProgress)
                    return;

                PathQueryStatus status = PathQueryStatus.InProgress;
                while (status.GetStatus() == PathQueryStatus.InProgress)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    status = Query.UpdateFindPath(int.MaxValue, out int _);
                    stopwatch.Stop();
                }

                if (status.GetStatus() != PathQueryStatus.Success)
                {
                    if (DEBUG_JOB)
                        Plugin.Instance.Logger.LogInfo($"{Index} ({status}): Failed FindPath.");
                    Status[0] = status;
                    return;
                }

                var pathNodes = new NativeArray<PolygonId>(Pathfinding.MAX_PATH_SIZE, Allocator.Temp);
                status = Query.EndFindPath(out var pathNodesSize);
                Query.GetPathResult(pathNodes);

                var straightPathStatus = Pathfinding.FindStraightPath(Query, Origin, Destination, pathNodes, pathNodesSize, Path, PathSize);
                pathNodes.Dispose();

                if (straightPathStatus.GetStatus() != PathQueryStatus.Success)
                {
                    if (DEBUG_JOB)
                        Plugin.Instance.Logger.LogInfo($"{Index} ({status}, {straightPathStatus}): Failed FindStraightPath.");
                    Status[0] = status;
                    return;
                }

                // Check if the end of the path is close enough to the target.
                var distance = (Path[PathSize[0] - 1].position - Destination).sqrMagnitude;
                if (distance > MAX_ENDPOINT_DISTANCE_SQR)
                {
                    if (DEBUG_JOB)
                        Plugin.Instance.Logger.LogInfo($"{Index} ({status}, {straightPathStatus}): Destination is too far away at {distance}m.");
                    Status[0] = PathQueryStatus.Failure;
                    return;
                }

                if (DEBUG_JOB)
                    Plugin.Instance.Logger.LogInfo($"{Index} ({status}, {straightPathStatus}): Success with {(Path[0].polygon.IsNull() ? "null" : "created path")}.");
                Status[0] = PathQueryStatus.Success;
            }
        }

        static void Dispose(this FindPathToNodeJob job)
        {
            job.Status.Dispose();
            job.Path.Dispose();
            job.PathSize.Dispose();
        }

        private static (NavMeshQuery[], FindPathToNodeJob[], JobHandle[]) CreatePathfindingJobs(EnemyAI enemy, Transform[] candidates)
        {
            var agent = enemy.agent;

            if (!agent.isOnNavMesh || !agent.FindClosestEdge(out var agentNavMeshHit))
            {
                Plugin.Instance.Logger.LogInfo($"Agent is not on the navmesh.");
                return default;
            }

            var agentTypeID = agent.agentTypeID;
            var areaMask = agent.areaMask;

            var count = candidates.Length;

            NavMeshQuery[] queries;
            FindPathToNodeJob[] jobs;
            JobHandle[] jobHandles;

            if (USE_CACHE)
            {
                (queries, jobs, jobHandles) = EnemyJobCaches[enemy.thisEnemyIndex].Get(count);
            }
            else
            {
                queries = new NavMeshQuery[count];
                jobs = new FindPathToNodeJob[count];
                jobHandles = new JobHandle[count];
            }


            if (USE_POOL)
            {
                QueryPool.Take(queries);
            }
            else
            {
                var world = NavMeshWorld.GetDefaultWorld();
                for (int i = 0; i < count; i++)
                    queries[i] = new NavMeshQuery(world, Allocator.TempJob, Pathfinding.MAX_PATH_SIZE);
            }

            var origin = queries[0].MapLocation(agentNavMeshHit.position, NodeSearchRange, agentTypeID, areaMask);

            if (!queries[0].IsValid(origin.polygon))
            {
                Plugin.Instance.Logger.LogError($"Path origin at {agentNavMeshHit.position} was not found in the nav mesh.");
                QueryPool.Free(queries);
                return default;
            }

            var allocationStopwatch = new Stopwatch();

            for (int i = 0; i < count; i++)
            {
                var query = queries[i];
                var destination = candidates[i].position;
                var destinationLocation = query.MapLocation(destination, NodeSearchRange, agentTypeID, areaMask);
                var valid = query.IsValid(destinationLocation);
                if (valid)
                    query.BeginFindPath(origin, destinationLocation, areaMask);
                jobs[i] = new FindPathToNodeJob(valid, i, query, agent.transform.position, destination);
                allocationStopwatch.Start();
                jobHandles[i] = jobs[i].Schedule();
                allocationStopwatch.Stop();
            }

            Plugin.Instance.Logger.LogInfo($"allocation took {allocationStopwatch.Elapsed.TotalMilliseconds * 1000:0.###} microseconds");
            //Plugin.Instance.Logger.LogInfo($"Agent navmesh hit was {agentNavMeshHit.position}, chose map location at {origin.position}.");
            return (queries, jobs, jobHandles);
        }

        private static int lastFramePrinted = 0;
        private const int printInterval = 165;
        private static bool print = false;

        public static IEnumerator<Transform> ChooseFarthestNodeFromPosition(EnemyAI enemy, Vector3 pos, bool avoidLineOfSight = false, int offset = 0)
        {
            if (!enemy.agent.isOnNavMesh)
                yield break;

            var startFrame = Time.frameCount;

            if (DEBUG_JOB)
            {
                var colorIndex = 0;
                foreach (var node in enemy.allAINodes)
                    UnityEngine.Debug.DrawLine(node.transform.position, node.transform.position + Vector3.up, ColorRotation[colorIndex++ % ColorRotation.Length], 0.15f);
            }

            var candidateCount = enemy.allAINodes.Length;
            var candidateTransforms = new Transform[candidateCount];
            var candidateDistances = new float[candidateCount];
            for (int i = 0; i < candidateCount; i++)
            {
                candidateTransforms[i] = enemy.allAINodes[i].transform;
                candidateDistances[i] = (pos - candidateTransforms[i].position).sqrMagnitude;
            }
            Array.Sort(candidateDistances, candidateTransforms, Comparer<float>.Create((a, b) => b.CompareTo(a)));

            /*print = startFrame - lastFramePrinted > printInterval;
            if (print)
            {
                lastFramePrinted = startFrame;
                Plugin.Instance.Logger.LogInfo($"Begin ChooseFarthestNodeFromPosition by {enemy.name} with {candidateCount} jobs.");
            }*/

            var (queries, jobs, jobHandles) = CreatePathfindingJobs(enemy, candidateTransforms);
            if (jobs == null)
                yield break;
            //Plugin.Instance.Logger.LogInfo($"Allocated jobs and job handles.");

            int result = -1;

            while (result == -1)
            {
                yield return null;
                bool complete = true;
                var pathsLeft = offset;
                for (int i = 0; i < candidateCount; i++)
                {
                    if (!jobHandles[i].IsCompleted)
                    {
                        complete = false;
                        break;
                    }
                    jobHandles[i].Complete();
                    var job = jobs[i];

                    if (job.Status[0].GetStatus() == PathQueryStatus.Success)
                    {
                        var path = job.Path;
                        //if (path[0].polygon.IsNull())
                        //    Plugin.Instance.Logger.LogWarning($"{i}: Path is null");

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
                        if (jobs[i].Status[0].GetStatus() == PathQueryStatus.Success)
                        {
                            result = i;
                            break;
                        }
                    }
                    break;
                }
            }

            if (result >= 0)
            {
                var resultTransform = candidateTransforms[result];
                enemy.mostOptimalDistance = Vector3.Distance(pos, resultTransform.position);
                //if (print)
                //    Plugin.Instance.Logger.LogInfo($"Found position {resultTransform.position} with {jobs[result].PathSize[0]} path nodes in {Time.frameCount - startFrame} frames.");
                yield return resultTransform;
            }

            {
                int i = 0;
                while (i < candidateCount)
                {
                    yield return null;
                    for (; i < candidateCount; i++)
                    {
                        var job = jobs[i];
                        var jobHandle = jobHandles[i];
                        if (!jobHandle.IsCompleted)
                            break;
                        jobHandles[i].Complete();

                        if (DEBUG_JOB && job.Status[0].GetStatus() == PathQueryStatus.Success)
                        {
                            var path = job.Path;
                            var pathSize = job.PathSize[0];
                            for (int segment = 1; segment < pathSize; segment++)
                                UnityEngine.Debug.DrawLine(path[segment - 1].position, path[segment].position, ColorRotation[i % ColorRotation.Length], i == result ? 0.2f : 0.1f);
                        }

                        job.Dispose();
                    }

                    if (i == candidateCount)
                        break;
                }
            }

            if (USE_POOL)
            {
                QueryPool.Free(queries);
            }
            else
            {
                for (int queryIndex = 0; queryIndex < candidateCount; queryIndex++)
                    queries[queryIndex].Dispose();
            }
            //Plugin.Instance.Logger.LogInfo($"Deallocated jobs and job handles in {Time.frameCount - startFrame} frames.");
        }
        private static readonly Color[] ColorRotation = [Color.red, Color.yellow, Color.green, Color.blue, Color.cyan, Color.grey];
    }
}
