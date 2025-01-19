using System.Collections.Generic;

using GameNetcodeStuff;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

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

        internal void StartJobs(EnemyAI enemy)
        {
            hasStarted = true;

            var agent = enemy.agent;
            var position = enemy.agent.GetAgentPosition();

            var allPlayers = StartOfRound.Instance.allPlayerScripts;
            if (playerJobIndices.Length != allPlayers.Length)
                playerJobIndices = new int[allPlayers.Length];

            validPlayerPositions.Clear();
            var jobIndex = 0;
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (enemy.PlayerIsTargetable(player))
                {
                    playerJobIndices[i] = jobIndex++;
                    validPlayerPositions.Add(player.transform.position);
                }
                else
                {
                    playerJobIndices[i] = -1;
                }
            }

            PathsToPlayersJob.Initialize(agent.agentTypeID, agent.areaMask, position, validPlayerPositions);

            PathsToPlayersJobHandle = PathsToPlayersJob.ScheduleByRef(validPlayerPositions.Count, default);
        }

        internal void ResetIfJobsHaveCompleted()
        {
            if (!hasStarted)
                return;

            for (var i = 0; i < validPlayerPositions.Count; i++)
            {
                if (PathsToPlayersJob.Statuses[i] == PathQueryStatus.InProgress)
                    return;
            }

            hasStarted = false;
        }

        internal int GetJobIndexForPlayerIndex(int playerIndex)
        {
            if (playerIndex >= playerJobIndices.Length)
                return -1;
            return playerJobIndices[playerIndex];
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
