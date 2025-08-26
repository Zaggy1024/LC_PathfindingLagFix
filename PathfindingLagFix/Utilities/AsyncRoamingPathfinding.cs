using Unity.Jobs;
using UnityEngine;

using PathfindingLib.Utilities;

#if BENCHMARKING
using Unity.Profiling;
#endif

namespace PathfindingLagFix.Utilities;

internal static class AsyncRoamingPathfinding
{
    private static readonly IDMap<EnemyRoamingPathfindingStatus> Statuses = new(() => new EnemyRoamingPathfindingStatus(), 1);

    internal static void RemoveStatus(EnemyAI enemy)
    {
        Statuses[enemy.thisEnemyIndex] = new EnemyRoamingPathfindingStatus();
    }

#if BENCHMARKING
    private static readonly ProfilerMarker StartJobsMarker = new("Start Jobs");
    private static readonly ProfilerMarker GetNodePositionsMarker = new("Get Node Positions");
    private static readonly ProfilerMarker InitAndScheduleJobsMarker = new("Initialize and Schedule Jobs");
#endif

    internal class EnemyRoamingPathfindingStatus
    {
        internal FindPathsToNodesJob PathsFromEnemyJob;
        internal JobHandle PathsFromEnemyJobHandle;

        internal FindPathsToNodesJob PathsFromSearchStartJob;
        internal JobHandle PathsFromSearchStartJobHandle;

        private Vector3[] nodePositions = [];
        private int nodeCount = 0;

        internal void StartJobs(EnemyAI enemy)
        {
#if BENCHMARKING
            using var startJobsMarkerAuto = StartJobsMarker.Auto();
#endif

            var search = enemy.currentSearch;
            var agent = enemy.agent;

#if BENCHMARKING
            using var getNodePositionsMarkerAuto = new TogglableProfilerAuto(GetNodePositionsMarker);
#endif
            var nodes = search.unsearchedNodes;
            nodeCount = nodes.Count;

            if (nodeCount > nodePositions.Length)
                nodePositions = new Vector3[nodeCount];

            for (var i = 0; i < nodes.Count; i++)
                nodePositions[GetJobIndex(i)] = nodes[i].transform.position;

#if BENCHMARKING
            getNodePositionsMarkerAuto.Pause();
            using var initAndScheduleJobsMarkerAuto = InitAndScheduleJobsMarker.Auto();
#endif

            PathsFromEnemyJob.Initialize(agent, nodePositions, nodeCount, calculateDistance: search.startedSearchAtSelf);
            PathsFromEnemyJobHandle = PathsFromEnemyJob.ScheduleByRef(nodes.Count, default);

            if (!search.startedSearchAtSelf)
            {
                PathsFromSearchStartJob.Initialize(-1, agent.areaMask, costs: default, search.currentSearchStartPosition, nodePositions, nodeCount, calculateDistance: true);
                PathsFromSearchStartJobHandle = PathsFromSearchStartJob.ScheduleByRef(nodes.Count, default);
            }
        }

        internal int GetJobIndex(int index)
        {
            return nodeCount - 1 - index;
        }

        ~EnemyRoamingPathfindingStatus()
        {
            PathsFromEnemyJob.FreeAllResources();
            PathsFromSearchStartJob.FreeAllResources();
        }
    }

    internal static EnemyRoamingPathfindingStatus GetStatus(EnemyAI enemy)
    {
        return Statuses[enemy.thisEnemyIndex];
    }
}
