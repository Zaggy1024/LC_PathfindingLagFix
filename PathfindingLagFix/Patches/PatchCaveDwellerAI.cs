using System;
using System.Collections;

using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLagFix.Utilities;
using PathfindingLib.Utilities;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(CaveDwellerAI))]
internal static class PatchCaveDwellerAI
{
    private static bool useAsync = true;

    private const int ADULT_SNEAKING_TO_PLAYER_ID = 0;

    private static bool IsPathInvalidWhileBodyIsVisible(CaveDwellerAI caveDweller, NativeArray<NavMeshLocation> path)
    {
        if (path.Length <= 1)
            return false;

        var targetPos = caveDweller.targetPlayer.transform.position;
        var targetDist = Vector3.Distance(caveDweller.transform.position, targetPos);
        var segmentStart = path[0].position;

        for (int segment = 1; segment < path.Length; segment++)
        {
            var segmentEnd = path[segment].position;
            var segmentCenter = (segmentStart + segmentEnd) * 0.5f;

            if (Vector3.Distance(segmentCenter, targetPos) < 8)
                return true;
            if (Vector3.Distance(segmentEnd, targetPos) < 8)
                return true;
            if (targetDist - Vector3.Distance(segmentEnd, targetPos) > 4)
                return true;

            segmentStart = segmentEnd;
        }

        return false;
    }

    private static IEnumerator ChooseClosestNodeToPositionNoLOSCoroutine(CaveDwellerAI caveDweller, AsyncDistancePathfinding.EnemyDistancePathfindingStatus status, Vector3 target, int offset = 0)
    {
        if (!caveDweller.agent.isOnNavMesh)
            yield break;

        var candidateCount = caveDweller.allAINodes.Length;
        AsyncDistancePathfinding.StartJobs(caveDweller, status, target, candidateCount, farthestFirst: false, calculateDistance: true);
        var job = status.Job;
        var jobHandle = status.JobHandle;

        int result = -1;

        while (result == -1)
        {
            yield return null;
            bool complete = true;
            var pathsLeft = Math.Min(offset, candidateCount - 1);

            var currentPosition = caveDweller.transform.position;

            var hidingSpot = caveDweller.caveHidingSpot;
            var searchWidthSqr = caveDweller.searchRoutine.searchWidth * caveDweller.searchRoutine.searchWidth;

            for (int i = 0; i < candidateCount; i++)
            {
                if (caveDweller.ignoredNodes.Contains(status.SortedNodes[i].transform))
                    continue;
                var nodePosition = status.SortedPositions[i];
                if ((hidingSpot - nodePosition).sqrMagnitude > searchWidthSqr)
                    continue;
                if ((currentPosition - nodePosition).sqrMagnitude > 40 * 40)
                    continue;
                if (!Physics.Linecast(nodePosition + Vector3.up, target, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    continue;

                var nodeStatus = job.Statuses[i];

                if (nodeStatus.GetResult() == PathQueryStatus.InProgress)
                {
                    complete = false;
                    break;
                }

                if (nodeStatus.GetResult() == PathQueryStatus.Success)
                {
                    var path = job.GetPath(i);
                    if (path[0].polygon.IsNull())
                    {
                        Plugin.Instance.Logger.LogWarning($"{i}: Path is null");
                        continue;
                    }

                    if (caveDweller.wasBodyVisible)
                    {
                        if (IsPathInvalidWhileBodyIsVisible(caveDweller, path))
                            continue;
                    }
                    else
                    {
                        if (job.PathDistances[i] > Vector3.Distance(currentPosition, caveDweller.targetPlayer.transform.position) + 16)
                            continue;
                        if (LineOfSight.PathIsBlockedByLineOfSight(path, checkLOSToPosition: caveDweller.targetPlayer.transform.position))
                            continue;
                    }

                    if (pathsLeft-- == 0)
                    {
                        result = i;
                        break;
                    }

                    continue;
                }
            }
            if (complete)
                break;
        }

        job.Cancel();

        if (result == -1)
        {
            caveDweller.ignoredNodes.Clear();
            result = Math.Min(UnityEngine.Random.Range(6, 15), candidateCount);
        }

        status.ChosenNode = status.SortedNodes[result];
        status.MostOptimalDistance = Vector3.Distance(target, status.SortedPositions[result]);

        while (!jobHandle.IsCompleted)
            yield return null;

        status.Coroutine = null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CaveDwellerAI.ChooseClosestNodeToPositionNoLOS))]
    private static bool ChooseClosestNodeToPositionNoLOSPrefix(CaveDwellerAI __instance, Vector3 pos, int offset, ref Transform __result)
    {
        if (!useAsync)
            return true;
        var status = AsyncDistancePathfinding.StartChoosingNode(__instance, ADULT_SNEAKING_TO_PLAYER_ID, status => ChooseClosestNodeToPositionNoLOSCoroutine(__instance, status, pos, offset));
        __result = status.RetrieveChosenNode(out __instance.mostOptimalDistance);
        return false;
    }
}
