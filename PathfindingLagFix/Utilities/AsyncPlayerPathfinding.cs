using System.Collections.Generic;

using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLib.Utilities;

namespace PathfindingLagFix.Utilities;

internal static class AsyncPlayerPathfinding
{
    private static readonly IDMap<EnemyToPlayerPathfindingStatus> Statuses = new(() => new EnemyToPlayerPathfindingStatus(), 1);

    internal static void RemoveStatus(EnemyAI enemy)
    {
        Statuses[enemy.thisEnemyIndex] = new EnemyToPlayerPathfindingStatus();
    }

    internal class EnemyToPlayerPathfindingStatus
    {
        internal FindPathsToNodesJob PathsToPlayersJob;
        internal JobHandle PathsToPlayersJobHandle;

        internal bool hasStarted = false;
        private int[] playerJobIndices = [];
        private readonly List<Vector3> validPlayerPositions = [];
        private bool lastRetrievedStatusWasInProgress = false;

        internal void StartJobs(EnemyAI enemy)
        {
            hasStarted = true;

            var agent = enemy.agent;
            var position = enemy.agent.GetPathOrigin();

            var allPlayers = StartOfRound.Instance.allPlayerScripts;
            if (playerJobIndices.Length != allPlayers.Length)
                playerJobIndices = new int[allPlayers.Length];

            validPlayerPositions.Clear();
            lastRetrievedStatusWasInProgress = false;

            var jobIndex = 0;
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (!enemy.PlayerIsTargetable(player))
                {
                    playerJobIndices[i] = -1;
                    continue;
                }

                playerJobIndices[i] = jobIndex++;
                validPlayerPositions.Add(player.transform.position);
            }

            PathsToPlayersJob.Initialize(agent.agentTypeID, agent.areaMask, position, validPlayerPositions);

            PathsToPlayersJobHandle = PathsToPlayersJob.ScheduleByRef(validPlayerPositions.Count, default);
        }

        internal bool IsPathValid(int playerIndex)
        {
            if (!hasStarted)
                return false;

            if (playerIndex >= playerJobIndices.Length)
                return false;

            var index = playerJobIndices[playerIndex];

            if (index < 0)
                return false;

            var status = PathsToPlayersJob.Statuses[index].GetResult();
            if (status == PathQueryStatus.InProgress)
            {
                lastRetrievedStatusWasInProgress = true;
                return false;
            }

            return status == PathQueryStatus.Success;
        }

        internal void ResetIfResultsHaveBeenUsed()
        {
            if (!hasStarted)
                return;

            if (lastRetrievedStatusWasInProgress && validPlayerPositions.Count > 0)
                return;

            hasStarted = false;
        }

        ~EnemyToPlayerPathfindingStatus()
        {
            PathsToPlayersJob.FreeAllResources();
        }
    }

    internal static EnemyToPlayerPathfindingStatus GetStatus(EnemyAI enemy)
    {
        return Statuses[enemy.thisEnemyIndex];
    }
}
