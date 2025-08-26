using System;
using System.Collections.Generic;

using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLib.Utilities;

#if BENCHMARKING
using Unity.Profiling;
#endif

namespace PathfindingLagFix.Utilities;

internal static class AsyncPlayerPathfinding
{
    private static readonly IDMap<EnemyToPlayerPathfindingStatus> Statuses = new(() => new EnemyToPlayerPathfindingStatus(), 1);

    internal static void RemoveStatus(EnemyAI enemy)
    {
        Statuses[enemy.thisEnemyIndex] = new EnemyToPlayerPathfindingStatus();
    }

#if BENCHMARKING
    private static readonly ProfilerMarker StartJobsMarker = new("Start Jobs");
    private static readonly ProfilerMarker CollectMarker = new("Collect Players");
    private static readonly ProfilerMarker ScheduleMarker = new("Schedule Job");
#endif

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
#if BENCHMARKING
            using var startJobsMarkerAuto = StartJobsMarker.Auto();
#endif

            var agent = enemy.agent;

#if BENCHMARKING
            using var collectMarkerAuto = new TogglableProfilerAuto(CollectMarker);
#endif
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
#if BENCHMARKING
            collectMarkerAuto.Pause();
#endif

            PathsToPlayersJob.Initialize(agent, validPlayerPositions);

#if BENCHMARKING
            using var scheduleMarkerAuto = new TogglableProfilerAuto(ScheduleMarker);
#endif
            PathsToPlayersJobHandle = PathsToPlayersJob.ScheduleByRef(validPlayerPositions.Count, default);
#if BENCHMARKING
            scheduleMarkerAuto.Pause();
#endif

            inFlightJobsTime = Time.time;
        }

        internal bool AllJobsAreDone()
        {
            for (var i = 0; i < validPlayerPositions.Count; i++)
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
