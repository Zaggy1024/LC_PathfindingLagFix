using System;

using Unity.Collections;
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
    [Flags]
    internal enum PathOptions : byte
    {
        None = 0,
        GroundCast = 1,
        RequirePath = 2,
    }

    const byte PlayerPathFlagsMax = (byte)(PathOptions.GroundCast | PathOptions.RequirePath);

    private static EnemyMap<EnemyToPlayerPathfindingStatus>[] CreateStatusMaps()
    {
        var result = new EnemyMap<EnemyToPlayerPathfindingStatus>[PlayerPathFlagsMax];
        for (byte i = 0; i < PlayerPathFlagsMax; i++)
        {
            var flags = (PathOptions)i + 1;
            result[i] = new(() => new EnemyToPlayerPathfindingStatus(flags));
        }
        return result;
    }

    private static readonly EnemyMap<EnemyToPlayerPathfindingStatus>[] Statuses = CreateStatusMaps();

    internal static void RemoveStatus(EnemyAI enemy)
    {
        for (byte i = 0; i < PlayerPathFlagsMax; i++)
            Statuses[i][enemy].Invalidate();
    }

#if BENCHMARKING
    private static readonly ProfilerMarker StartJobsMarker = new("Start Jobs");
    private static readonly ProfilerMarker CollectMarker = new("Collect Players");
    private static readonly ProfilerMarker ScheduleMarker = new("Schedule Job");
#endif

    internal class EnemyToPlayerPathfindingStatus(PathOptions pathFlags)
    {
        internal enum JobsStatus
        {
            NotStarted,
            NotRetrieved,
            RetrievedJobsDone,
            RetrievedJobsIncomplete,
        }

        internal FindPathsToNodesJob PathsToPlayersJob;
        private AssignGroundCastPointsAsDestinationsJob AssignDestinationsJob;
        internal JobHandle JobHandle;

        private NativeArray<RaycastCommand> raycastCommands;
        private NativeArray<RaycastHit> raycastHits;
        private NativeArray<Vector3> playerPositions;
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
            if (playerPositions.Length != allPlayers.Length)
            {
                Clear();

                playerPositions = new(allPlayers.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                if (pathFlags.HasFlag(PathOptions.GroundCast))
                {
                    raycastCommands = new(allPlayers.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    raycastHits = new(allPlayers.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
            }

            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];

                if (!enemy.PlayerIsTargetable(player))
                {
                    playerPositions[i] = FindPathsToNodesJob.INVALID_DESTINATION;
                    if (pathFlags.HasFlag(PathOptions.GroundCast))
                    {
                        raycastCommands[i] = default;
                        raycastHits[i] = default;
                    }
                    continue;
                }

                var playerPosition = player.transform.position;
                playerPositions[i] = playerPosition;
                if (pathFlags.HasFlag(PathOptions.GroundCast))
                {
                    var queryParameters = QueryParameters.Default with
                    {
                        layerMask = StartOfRound.Instance.collidersAndRoomMaskAndDefault,
                        hitTriggers = QueryTriggerInteraction.Ignore,
                    };
                    raycastCommands[i] = new RaycastCommand(playerPosition, Vector3.down, queryParameters, 5f);
                    raycastHits[i] = default;
                }
            }
#if BENCHMARKING
            collectMarkerAuto.Pause();
            using var scheduleMarkerAuto = new TogglableProfilerAuto(ScheduleMarker);
#endif

            JobHandle prerequisiteJobHandle = default;

            if (pathFlags.HasFlag(PathOptions.GroundCast))
            {
                prerequisiteJobHandle = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, 1, prerequisiteJobHandle);
            }

            if (pathFlags.HasFlag(PathOptions.GroundCast) && pathFlags.HasFlag(PathOptions.RequirePath))
            {
                AssignDestinationsJob.Initialize(raycastHits, playerPositions);
                prerequisiteJobHandle = AssignDestinationsJob.ScheduleByRef(playerPositions.Length, prerequisiteJobHandle);
            }

            if (pathFlags.HasFlag(PathOptions.RequirePath))
            {
                PathsToPlayersJob.Initialize(agent, playerPositions.Length);
                PathsToPlayersJob.SetDestinations(playerPositions);
                prerequisiteJobHandle = PathsToPlayersJob.ScheduleByRef(playerPositions.Length, prerequisiteJobHandle);
            }

            JobHandle = prerequisiteJobHandle;
#if BENCHMARKING
            scheduleMarkerAuto.Pause();
#endif

            inFlightJobsTime = Time.time;
        }

        internal bool AllJobsAreDone()
        {
            return JobHandle.IsCompleted;
        }

        internal float UpdatePathsAndGetCalculationTime(EnemyAI enemy)
        {
            if (!AllJobsAreDone())
                return currentJobsTime;

            if (playersPathable.Length != playerPositions.Length)
                Array.Resize(ref playersPathable, playerPositions.Length);

            if (pathFlags.HasFlag(PathOptions.RequirePath))
            {
                for (var i = 0; i < playerPositions.Length; i++)
                {
                    var status = PathsToPlayersJob.Statuses[i].GetResult();
                    playersPathable[i] = status == PathQueryStatus.Success;
                }
            }
            else if (pathFlags.HasFlag(PathOptions.GroundCast))
            {
                for (var i = 0; i < playerPositions.Length; i++)
                    playersPathable[i] = raycastHits[i].colliderInstanceID != 0;
            }
            else
            {
                throw new InvalidOperationException("Bad path flags");
            }

            currentJobsTime = inFlightJobsTime;

            StartJobs(enemy);
            return currentJobsTime;
        }

        internal bool CanReachPlayer(int playerIndex)
        {
            return playersPathable[playerIndex];
        }

        internal void Invalidate()
        {
            currentJobsTime = float.NegativeInfinity;
            inFlightJobsTime = float.NegativeInfinity;
        }

        internal void Clear()
        {
            raycastCommands.Dispose();
            raycastHits.Dispose();
            playerPositions.Dispose();

            playersPathable = [];
        }

        ~EnemyToPlayerPathfindingStatus()
        {
            PathsToPlayersJob.FreeAllResources(disposeDestinations: false);
            Clear();
        }
    }

    internal static EnemyToPlayerPathfindingStatus GetStatus(EnemyAI enemy, PathOptions flags)
    {
        return Statuses[(int)flags - 1][enemy];
    }
}
