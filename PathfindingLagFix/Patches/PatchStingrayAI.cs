using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using UnityEngine;
using UnityEngine.Experimental.AI;
using HarmonyLib;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;
using PathfindingLib.Utilities;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(StingrayAI))]
internal static class PatchStingrayAI
{
    private static bool useAsync = true;

    private class StingrayData
    {
        internal readonly SequentialMap<NodeOrdering> orderings = new(() => new NodeOrdering(), 4);
    }

    private static readonly EnemyMap<StingrayData> instanceDatas = new(() => new StingrayData());
    private static bool currentIntervalFoundTargetPlayer = false;

    internal static void RemoveStatus(EnemyAI enemy)
    {
        instanceDatas.Remove(enemy);
    }

    private static void UpdateTargetPlayer(StingrayAI stingray)
    {
        currentIntervalFoundTargetPlayer = stingray.TargetClosestPlayer(4f);
    }

    private static bool NeedPathsForThisInterval(StingrayAI stingray)
    {
        var hidingSpot = stingray.hidingSpot;
        if (!hidingSpot.choseTemporarySpot)
            return true;
        if (currentIntervalFoundTargetPlayer)
        {
            if (stingray.hasSpit)
                return hidingSpot.type != HidingSpotType.AvoidPlayer;
            else
                return hidingSpot.type != HidingSpotType.NearPlayer;
        }
        return false;
    }

    private static bool ShouldSkipNodeSelection(StingrayAI stingray)
    {
        if (!useAsync)
            return false;

        if (!NeedPathsForThisInterval(stingray))
            return false;

        var status = AsyncDistancePathfinding.GetStatus(stingray);
        return status.CurrentSearchTypeID != 0 || status.Coroutine != null;
    }

    private static void StartJobs(StingrayAI stingray)
    {
        if (!useAsync)
            return;

        var status = AsyncDistancePathfinding.GetStatus(stingray);
        if (status.Coroutine != null)
            return;

        if (NeedPathsForThisInterval(stingray) && stingray.agent.isOnNavMesh)
        {
            status.Coroutine = stingray.StartCoroutine(WaitForPathsCoroutine(stingray));
            status.CurrentSearchTypeID = 0;
        }
        else
        {
            status.CurrentSearchTypeID = -1;
        }
    }

    private static IEnumerator WaitForPathsCoroutine(StingrayAI stingray)
    {
        var status = AsyncDistancePathfinding.GetStatus(stingray);

        var candidateCount = stingray.allAINodes.Length;
        AsyncDistancePathfinding.StartJobs(stingray, status, default, candidateCount, NodeSortOrder.None, calculateDistance: true);
        var job = status.Job;
        var jobHandle = status.JobHandle;

        while (true)
        {
            yield return null;
            var allDone = true;
            for (int i = 0; i < candidateCount; i++)
            {
                if (job.Statuses[i].GetResult() == PathQueryStatus.InProgress)
                {
                    allDone = false;
                    break;
                }
            }
            if (allDone)
                break;
        }

        while (!jobHandle.IsCompleted)
            yield return null;

        jobHandle.Complete();

        status.Coroutine = null;
    }

    internal static Transform ChooseFarthestNodeFromPositionAsync(StingrayAI stingray, Vector3 pos, bool avoidLineOfSight, int offset, bool doAsync, int maxAsyncIterations, int capDistance, int nodeSearchID)
    {
        if (!useAsync)
            return stingray.ChooseFarthestNodeFromPosition(pos, avoidLineOfSight, offset, doAsync, maxAsyncIterations, capDistance);

        var status = AsyncDistancePathfinding.GetStatus(stingray);
        var ordering = instanceDatas[stingray].orderings[nodeSearchID];

        var candidateCount = status.AINodes.Length;
        ordering.Sort(status.SortedPositions, candidateCount, pos, NodeSortOrder.FarthestFirst);
        ref var job = ref status.Job;

        Transform result = status.SortedNodes[ordering[0]];
        var pathsLeft = Math.Min(offset, candidateCount - 1);

        var stingrayPosition = stingray.transform.position;

        for (int i = 0; i < candidateCount; i++)
        {
            var sourceIndex = ordering[i];
            var nodePosition = status.SortedPositions[sourceIndex];

            if (capDistance != -1)
            {
                if (Vector3.Distance(stingrayPosition, nodePosition) > 40f)
                    continue;
                var dungeonIndex = RoundManager.Instance.currentDungeonType;
                if (dungeonIndex >= 0)
                {
                    if (Math.Abs(nodePosition.y - stingrayPosition.y) > 12.5f * RoundManager.Instance.dungeonFlowTypes[dungeonIndex].MapTileSize)
                        continue;
                }
            }

            if (job.Statuses[sourceIndex].GetResult() == PathQueryStatus.Success)
            {
                var path = job.GetPath(sourceIndex);
                if (avoidLineOfSight && LineOfSight.PathIsBlockedByLineOfSight(path))
                    continue;

                stingray.mostOptimalDistance = Vector3.Distance(pos, nodePosition);
                result = status.SortedNodes[sourceIndex];

                if (pathsLeft-- == 0)
                    break;

                continue;
            }
        }

        return result;
    }

    internal static Transform ChooseClosestNodeToPositionStingrayAsync(StingrayAI stingray, Vector3 pos, int nodeSearchID)
    {
        if (!useAsync)
            return stingray.ChooseClosestNodeToPositionStingray(pos);

        var status = AsyncDistancePathfinding.GetStatus(stingray);
        var ordering = instanceDatas[stingray].orderings[nodeSearchID];

        var candidateCount = status.AINodes.Length;
        ordering.Sort(status.SortedPositions, candidateCount, pos, NodeSortOrder.ClosestFirst);
        ref var job = ref status.Job;

        // In vanilla, if there are no nodes within 32 units, the original pathDistance value may be used in the loop.
        // Otherwise, the pathDistance is overwritten until the loop reaches >= 32 units, then that value is reused.
        var initialPathDistance = stingray.pathDistance;

        int result = 0;

        var stingrayPosition = stingray.transform.position;
        var mostOptimalDistance = 1000f;
        var foundPath = false;
        var pathDistance = initialPathDistance;
        var pathsLeft = 5 * stingray.stingrayNumber % candidateCount;
        var successfulPaths = 0;

        for (int i = 14; i < candidateCount; i++)
        {
            var sourceIndex = ordering[i];
            var nodePosition = status.SortedPositions[sourceIndex];

            if (!foundPath && successfulPaths > 24)
                break;

            if ((nodePosition - pos).sqrMagnitude < 64)
                continue;

            if (pathsLeft > 0 && foundPath)
            {
                pathsLeft--;
                continue;
            }

            var dungeonIndex = RoundManager.Instance.currentDungeonType;
            if (dungeonIndex >= 0)
            {
                if (Math.Abs(nodePosition.y - stingrayPosition.y) > 12.5f * RoundManager.Instance.dungeonFlowTypes[dungeonIndex].MapTileSize)
                    continue;
            }

            if (Vector3.Distance(stingrayPosition, nodePosition) < 32)
            {
                var nodeResult = job.Statuses[sourceIndex].GetResult();
                if (nodeResult != PathQueryStatus.Success)
                    continue;
                var nodePath = job.GetPath(sourceIndex);
                if (LineOfSight.PathIsBlockedByLineOfSight(nodePath, out pathDistance))
                    continue;
            }

            successfulPaths++;

            if (pathDistance < mostOptimalDistance)
            {
                mostOptimalDistance = pathDistance;
                result = sourceIndex;
                foundPath = true;
            }

            if (pathsLeft == 0 || i == candidateCount - 1)
                break;

            pathsLeft--;
        }

        return status.SortedNodes[result];
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(StingrayAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var f_StingrayAI_hidingSpot = typeof(StingrayAI).GetField(nameof(StingrayAI.hidingSpot), BindingFlags.Instance | BindingFlags.NonPublic);
        var f_StingrayHidingSpot_choseTemporarySpot = typeof(StingrayHidingSpot).GetField(nameof(StingrayHidingSpot.choseTemporarySpot));

        var f_PatchStingrayAI_currentIntervalFoundTargetPlayer = typeof(PatchStingrayAI).GetField(nameof(currentIntervalFoundTargetPlayer), BindingFlags.Static | BindingFlags.NonPublic);

        var injector = new ILInjector(instructions, generator);

        // + PatchStingrayAI.currentIntervalFoundTargetPlayer = TargetClosestPlayer(4f);
        //   if (hidingSpot.choseTemporarySpot) {
        injector
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(f_StingrayAI_hidingSpot),
                ILMatcher.Ldfld(f_StingrayHidingSpot_choseTemporarySpot),
                ILMatcher.Opcode(OpCodes.Brfalse),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the hiding spot validation in {nameof(StingrayAI)}.{nameof(StingrayAI.DoAIInterval)}().");
            return instructions;
        };
        injector
            .InsertAfterBranch([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(UpdateTargetPlayer), BindingFlags.Static | BindingFlags.NonPublic, [typeof(StingrayAI)])),
            ]);

        // - TargetClosestPlayer(4f)
        // + targetClosestPlayerResult
        while (true)
        {
            injector.Find([
                ILMatcher.Ldarg(0),
                ILMatcher.LdcF32(4f),
                ILMatcher.Ldc(0),
                ILMatcher.LdcF32(70f),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(1),
                ILMatcher.Call(Reflection.m_EnemyAI_TargetClosestPlayer),
            ]);
            if (!injector.IsValid)
                break;
            injector
                .ReplaceLastMatch([
                    new(OpCodes.Ldsfld, f_PatchStingrayAI_currentIntervalFoundTargetPlayer),
                ])
                .GoToMatchEnd();
        }

        // + if (PatchStingrayAI.ShouldSkipNodeSelection(this))
        // +     return;
        //   if (!hidingSpot.choseTemporarySpot) {
        injector
            .GoToStart()
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(f_StingrayAI_hidingSpot),
                ILMatcher.Ldfld(f_StingrayHidingSpot_choseTemporarySpot),
                ILMatcher.Opcode(OpCodes.Brtrue),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the modification block start in {nameof(StingrayAI)}.{nameof(StingrayAI.DoAIInterval)}().");
            return instructions;
        }
        injector
            .DefineLabel(out var skipEarlyReturnLabel)
            .InsertAfterBranch(
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(ShouldSkipNodeSelection), BindingFlags.Static | BindingFlags.NonPublic)),
                new(OpCodes.Brfalse_S, skipEarlyReturnLabel),
                new(OpCodes.Ret))
            .AddLabel(skipEarlyReturnLabel);

        // - ChooseFarthestNodeFromPosition([...])
        // + PatchStingrayAI.ChooseFarthestNodeFromPositionAsync([...], [nodeSelectionID])
        var nodeSearchID = 0;
        injector.GoToStart();
        while (true)
        {
            injector.Find([
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
            ]);
            if (!injector.IsValid)
                break;
            injector
                .ReplaceLastMatch(
                    new(OpCodes.Ldc_I4, nodeSearchID++),
                    new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(ChooseFarthestNodeFromPositionAsync), BindingFlags.Static | BindingFlags.NonPublic)))
                .GoToMatchEnd();
        }

        // - ChooseClosestNodeToPositionStingray([...])
        // + PatchStingrayAI.ChooseClosestNodeToPositionStingrayAsync([...], [nodeSelectionID])
        injector.GoToStart();
        while (true)
        {
            injector.Find([
                ILMatcher.Call(typeof(StingrayAI).GetMethod(nameof(StingrayAI.ChooseClosestNodeToPositionStingray), [typeof(Vector3)])),
            ]);
            if (!injector.IsValid)
                break;
            injector
                .ReplaceLastMatch(
                    new(OpCodes.Ldc_I4, nodeSearchID++),
                    new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(ChooseClosestNodeToPositionStingrayAsync), BindingFlags.Static | BindingFlags.NonPublic)))
                .GoToMatchEnd();
        }

        return injector.ReleaseInstructions();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(StingrayAI.DoAIInterval))]
    private static void DoAIIntervalPostfix(StingrayAI __instance)
    {
        StartJobs(__instance);
    }
}
