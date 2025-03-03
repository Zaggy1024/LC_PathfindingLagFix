using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLagFix.Utilities;
using PathfindingLib.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(CaveDwellerAI))]
internal static class PatchCaveDwellerAI
{
    private static bool useAsync = true;

    private const int ADULT_SNEAKING_TO_PLAYER_ID = 0;

    private class StuckCheckStatus()
    {
        internal FindPathsToNodesJob job = new();
        internal JobHandle jobHandle = default;
        internal Vector3[] nodePositions = null;
        internal bool jobStarted = false;

        internal bool IsStuck()
        {
            if (!jobStarted)
            {
                Plugin.Instance.Logger.LogError($"Cave dweller stuck check jobs have not been started before checking their results.");
                return false;
            }

            for (var i = 0; i < nodePositions.Length; i++)
            {
                var pathStatus = job.Statuses[i].GetResult();
                if (pathStatus == PathQueryStatus.InProgress)
                {
                    Plugin.Instance.Logger.LogError($"Cave dweller stuck check jobs have not finished before checking their results.");
                    return false;
                }

                if (pathStatus == PathQueryStatus.Success)
                    return false;
            }

            return true;
        }
    }

    private static IDMap<StuckCheckStatus> stuckCheckStatuses = new(() => new(), 1);

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

    private static bool IsStuckCheckJobComplete(CaveDwellerAI caveDweller)
    {
        if (!useAsync)
            return true;

        var status = stuckCheckStatuses[caveDweller.thisEnemyIndex];
        ref var job = ref status.job;

        if (status.nodePositions == null)
        {
            status.nodePositions = new Vector3[caveDweller.allAINodes.Length];
            for (var i = 0; i < status.nodePositions.Length; i++)
                status.nodePositions[i] = caveDweller.allAINodes[i].transform.position;
        }

        if (!status.jobStarted)
        {
            var agent = caveDweller.agent;
            job.Initialize(agent.agentTypeID, agent.areaMask, caveDweller.transform.position, status.nodePositions);
            status.jobHandle = job.ScheduleByRef(status.nodePositions.Length, default);
            status.jobStarted = true;
        }

        return status.jobHandle.IsCompleted;
    }

    private static bool RetrieveWhetherCaveDwellerIsStuck(CaveDwellerAI caveDweller)
    {
        var status = stuckCheckStatuses[caveDweller.thisEnemyIndex];
        var result = status.IsStuck();
        status.jobStarted = false;
        return result;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(CaveDwellerAI.DoNonBabyUpdateLogic))]
    private static IEnumerable<CodeInstruction> DoNonBabyUpdateLogicTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // - if (base.IsOwner && Time.realtimeSinceStartup - checkIfTrappedInterval > 15) {
        // - if (base.IsOwner && Time.realtimeSinceStartup - checkIfTrappedInterval > 15 && PatchCaveDwellerAI.IsStuckCheckJobComplete()) {
        //     checkIfTrappedInterval = Time.realtimeSinceStartup;
        // -   if (ChooseClosestNodeToPosition(base.transform.position) == nodesTempArray[0].transform && PathIsIntersectedByLineOfSight(nodesTempArray[0].transform.position, calculatePathDistance: false, avoidLineOfSight: false))
        // +   if (PatchCaveDwellerAI.useAsync ? PatchCaveDwellerAI.RetrieveWhetherCaveDwellerIsStuck(this) : ChooseClosestNodeToPosition(base.transform.position) == nodesTempArray[0].transform && PathIsIntersectedByLineOfSight(nodesTempArray[0].transform.position, calculatePathDistance: false, avoidLineOfSight: false))
        var injector = new ILInjector(instructions, generator)
            .Find([
                ILMatcher.Call(typeof(Time).GetProperty(nameof(Time.realtimeSinceStartup)).GetMethod),
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(CaveDwellerAI).GetField(nameof(CaveDwellerAI.checkIfTrappedInterval), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Opcode(OpCodes.Sub),
                ILMatcher.LdcF32(15),
                ILMatcher.Opcode(OpCodes.Ble_Un).CaptureOperandAs(out Label skipStuckCheckLabel),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find trapped interval check in {nameof(CaveDwellerAI)}.{nameof(CaveDwellerAI.DoNonBabyUpdateLogic)}.");
            return instructions;
        }

        injector
            .GoToMatchEnd()
            .Insert([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchCaveDwellerAI).GetMethod(nameof(IsStuckCheckJobComplete), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Brfalse, skipStuckCheckLabel),
            ])
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseClosestNodeToPosition),
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(Reflection.f_EnemyAI_nodesTempArray),
                ILMatcher.Ldc(0),
                ILMatcher.Opcode(OpCodes.Ldelem_Ref),
                ILMatcher.Callvirt(Reflection.m_GameObject_get_transform),
                ILMatcher.Call(Reflection.m_Object_op_Equality),
                ILMatcher.Opcode(OpCodes.Brfalse).CaptureOperandAs(out Label skipUnstickLabel),
            ])
            .GoToMatchEnd()
            .Back(1)
            .GoToPush(1);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the trapped check pathfinding in {nameof(CaveDwellerAI)}.{nameof(CaveDwellerAI.DoNonBabyUpdateLogic)}.");
            return instructions;
        }

        injector
            .AddLabel(out var doVanillaStuckCheckLabel)
            .DefineLabel(out var unstickLabel)
            .Insert([
                new(OpCodes.Ldsfld, typeof(PatchCaveDwellerAI).GetField(nameof(useAsync), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Brfalse_S, doVanillaStuckCheckLabel),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchCaveDwellerAI).GetMethod(nameof(RetrieveWhetherCaveDwellerIsStuck), BindingFlags.NonPublic | BindingFlags.Static, [typeof(CaveDwellerAI)])),
                new(OpCodes.Brtrue_S, unstickLabel),
                new(OpCodes.Br, skipUnstickLabel),
            ])
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_PathIsIntersectedByLineOfSight),
                ILMatcher.Opcode(OpCodes.Brfalse),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the trapped check pathfinding re-check in {nameof(CaveDwellerAI)}.{nameof(CaveDwellerAI.DoNonBabyUpdateLogic)}.");
            return instructions;
        }

        return injector
            .GoToMatchEnd()
            .AddLabel(unstickLabel)
            .ReleaseInstructions();
    }

    internal static void RemoveStatus(EnemyAI enemy)
    {
        stuckCheckStatuses[enemy.thisEnemyIndex] = new();
    }
}
