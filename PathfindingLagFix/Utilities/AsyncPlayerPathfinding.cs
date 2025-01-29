using System.Collections.Generic;

using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLib.Utilities;
using System;

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
        internal enum JobsStatus
        {
            NotStarted,
            NotRetrieved,
            RetrievedJobsDone,
            RetrievedJobsIncomplete,
        }

        internal FindPathsToNodesJob PathsToPlayersJob;
        internal JobHandle PathsToPlayersJobHandle;

        private int[] playerJobIndices = [];
        private readonly List<Vector3> validPlayerPositions = [];
        private float inFlightJobsTime = float.NegativeInfinity;
        private float currentJobsTime = float.NegativeInfinity;
        private bool[] playersPathable = [];

        private void StartJobs(EnemyAI enemy)
        {
            var agent = enemy.agent;
            var position = enemy.agent.GetPathOrigin();

            var allPlayers = StartOfRound.Instance.allPlayerScripts;
            if (playerJobIndices.Length != allPlayers.Length)
                playerJobIndices = new int[allPlayers.Length];

            validPlayerPositions.Clear();

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

            inFlightJobsTime = Time.time;
        }

        internal bool AllJobsAreDone()
        {
            for (var i = 0; i < PathsToPlayersJob.Statuses.Length; i++)
            {
                if (PathsToPlayersJob.Statuses[i].GetResult() == PathQueryStatus.InProgress)
                    return false;
            }
            return true;
        }

        internal float UpdatePathsAndGetCalculationTime(EnemyAI enemy)
        {
            if (!AllJobsAreDone())
                return currentJobsTime;

            if (playersPathable.Length != playerJobIndices.Length)
                Array.Resize(ref playersPathable, playerJobIndices.Length);

            for (var i = 0; i < playerJobIndices.Length; i++)
            {
                var jobIndex = playerJobIndices[i];
                if (jobIndex == -1)
                {
                    playersPathable[i] = false;
                    continue;
                }

                var status = PathsToPlayersJob.Statuses[jobIndex].GetResult();
                playersPathable[i] = status == PathQueryStatus.Success;
            }

            currentJobsTime = inFlightJobsTime;

            StartJobs(enemy);
            return currentJobsTime;
        }

        internal bool CanReachPlayer(int playerIndex)
        {
            return playersPathable[playerIndex];
        }

        internal void Clear()
        {
            playerJobIndices = [];
            validPlayerPositions.Clear();
            playersPathable = [];
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
