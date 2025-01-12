using Unity.Jobs;
using UnityEngine;

namespace PathfindingLagFix.Utilities;

internal static class AsyncRoamingPathfinding
{
    private static readonly IDMap<EnemyRoamingPathfindingStatus> Statuses = new(() => new EnemyRoamingPathfindingStatus(), 1);

    internal static void RemoveStatus(EnemyAI enemy)
    {
        Statuses[enemy.thisEnemyIndex] = new EnemyRoamingPathfindingStatus();
    }

    internal class EnemyRoamingPathfindingStatus
    {
        internal FindPathsToNodesJob PathsFromEnemyJob;
        internal JobHandle PathsFromEnemyJobHandle;

        internal FindPathsToNodesJob PathsFromSearchStartJob;
        internal JobHandle PathsFromSearchStartJobHandle;

        private int nodeCount;

        internal void StartJobs(EnemyAI enemy)
        {
            var search = enemy.currentSearch;
            var agent = enemy.agent;
            var position = enemy.agent.GetAgentPosition();

            var nodes = search.unsearchedNodes;
            nodeCount = nodes.Count;
            var nodePositions = new Vector3[nodeCount];

            for (var i = 0; i < nodes.Count; i++)
                nodePositions[GetJobIndex(i)] = nodes[i].transform.position;

            PathsFromEnemyJob.Initialize(agent.agentTypeID, agent.areaMask, position, nodePositions, calculateDistance: search.startedSearchAtSelf);
            PathsFromEnemyJobHandle = PathsFromEnemyJob.ScheduleByRef(nodes.Count, default);

            if (!search.startedSearchAtSelf)
            {
                PathsFromSearchStartJob.Initialize(agent.agentTypeID, agent.areaMask, search.currentSearchStartPosition, nodePositions, calculateDistance: true);
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
