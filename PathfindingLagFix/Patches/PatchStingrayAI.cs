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
    private static readonly FieldInfo f_StingrayAI_hidingSpot = typeof(StingrayAI).GetField(nameof(StingrayAI.hidingSpot), BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool useAsync = true;

    private const int TEMPORARY_SPOT_ID = 0;
    private const int AVOID_PLAYER_ID = 1;
    private const int NEAR_PLAYER_ID = 2;

    private static bool ChooseTemporarySpot(StingrayAI stingray)
    {
        if (useAsync)
        {
            var aiNodeCount = stingray.allAINodes.Length;
            var nodeOffset = (aiNodeCount / 3 + 3 * stingray.stingrayNumber) % aiNodeCount;
            var status = AsyncDistancePathfinding.StartChoosingFarthestNodeFromPosition(stingray, TEMPORARY_SPOT_ID, stingray.mainEntrancePosition, offset: nodeOffset);
            var node = status.RetrieveChosenNode(out stingray.mostOptimalDistance);
            if (node != null)
            {
                var hidingSpot = stingray.hidingSpot;
                hidingSpot.choseTemporarySpot = true;
                hidingSpot.position = node.position;
                hidingSpot.type = HidingSpotType.Temporary;
            }
            return true;
        }

        return false;
    }

    private static Transform ChooseAvoidPlayerNode(StingrayAI stingray)
    {
        if (!stingray.hidingSpot.choseTemporarySpot)
            return null;

        var status = AsyncDistancePathfinding.StartChoosingFarthestNodeFromPosition(stingray, TEMPORARY_SPOT_ID, stingray.targetPlayer.transform.position);
        var node = status.RetrieveChosenNode(out stingray.mostOptimalDistance);
        return node;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(StingrayAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // - if (!hidingSpot.choseTemporarySpot) {
        // + if (!hidingSpot.choseTemporarySpot && PatchStingrayAI.ChooseTemporarySpot(this)) {
        //       hidingSpot.choseTemporarySpot = true;
        //       SetDestinationToNode(ChooseFarthestNodeFromPosition(mainEntrancePosition, avoidLineOfSight: false, (allAINodes.Length / 3 + 3 * stingrayNumber) % allAINodes.Length));
        //       hidingSpot.position = destination;
        //       hidingSpot.type = HidingSpotType.Temporary;
        //   }
        var injector = new ILInjector(instructions, generator)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(f_StingrayAI_hidingSpot),
                ILMatcher.Ldfld(typeof(StingrayHidingSpot).GetField(nameof(StingrayHidingSpot.choseTemporarySpot))),
                ILMatcher.Opcode(OpCodes.Brtrue).CaptureOperandAs(out Label skipTemporarySpotAssignmentLabel),
            ])
            .GoToMatchEnd();
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the branch to select the temporary spot in {nameof(StingrayAI)}.{nameof(StingrayAI.DoAIInterval)}().");
            return instructions;
        }

        injector
            .Insert([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(ChooseTemporarySpot), BindingFlags.Static | BindingFlags.NonPublic, [typeof(StingrayAI)])),
                new(OpCodes.Brtrue_S, skipTemporarySpotAssignmentLabel),
            ]);

        //   if (hidingSpot.type != HidingSpotType.AvoidPlayer) {
        // -     var chosenNode = ChooseFarthestNodeFromPosition(targetPlayer.transform.position, avoidLineOfSight: false, 0, doAsync: false, 50, 40);
        // +     var chosenNode;
        // +     if (PatchStingrayAI.useAsync)
        // +         chosenNode = PatchStingrayAI.ChooseAvoidPlayerNode(this);
        // +     else
        // +         chosenNode = ChooseFarthestNodeFromPosition(targetPlayer.transform.position, avoidLineOfSight: false, 0, doAsync: false, 50, 40);
        //       if (chosenNode != null) {
        //           hidingSpot.position = chosenNode.transform.position;
        //           hidingSpot.gotHidingSpot = true;
        //           hidingSpot.type = HidingSpotType.AvoidPlayer;
        //       }
        //   }
        injector
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(Reflection.f_EnemyAI_targetPlayer),
                ILMatcher.Callvirt(Reflection.m_Component_get_transform),
                ILMatcher.Callvirt(Reflection.m_Transform_get_position),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(50),
                ILMatcher.Ldc(40),
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
                ILMatcher.Stloc().CaptureAs(out var storeAvoidPlayerTargetNode),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find call to {nameof(EnemyAI.ChooseFarthestNodeFromPosition)} in {nameof(StingrayAI)}.{nameof(StingrayAI.DoAIInterval)}().");
            return instructions;
        }
        return injector
            .DefineLabel(out var skipVanillaAvoidPlayerLabel)
            .AddLabel(out var skipAsyncAvoidPlayerLabel)
            .Insert([
                new(OpCodes.Ldsfld, typeof(PatchStingrayAI).GetField(nameof(useAsync), BindingFlags.Static | BindingFlags.NonPublic)),
                new(OpCodes.Brfalse_S, skipAsyncAvoidPlayerLabel),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(ChooseAvoidPlayerNode), BindingFlags.Static | BindingFlags.NonPublic, [typeof(StingrayAI)])),
                storeAvoidPlayerTargetNode,
                new(OpCodes.Br_S, skipVanillaAvoidPlayerLabel),
            ])
            .GoToMatchEnd()
            .AddLabel(skipVanillaAvoidPlayerLabel)
            .ReleaseInstructions();
    }

    private static IEnumerator ChooseClosestNodeToPositionStingrayCoroutine(StingrayAI stingray, AsyncDistancePathfinding.EnemyDistancePathfindingStatus status, Vector3 target)
    {
        if (!stingray.agent.isOnNavMesh)
        {
            status.ChosenNode = stingray.transform;
            status.MostOptimalDistance = 0;
            yield return CoroutineWaiters.WaitForEndOfFrame;
            status.Coroutine = null;
            yield break;
        }

        var candidateCount = stingray.allAINodes.Length;
        AsyncDistancePathfinding.StartJobs(stingray, status, target, candidateCount, farthestFirst: false, calculateDistance: true);
        var job = status.Job;
        var jobHandle = status.JobHandle;

        // Vanilla doesn't set nodesTempArray to allAINodes after sorting, and nodesTempArray is used for
        // per-node verticality conditions.
        GameObject[] nodesTempArray = [.. stingray.nodesTempArray];

        // In vanilla, if there are no nodes within 32 units, the original pathDistance value may be used in the loop.
        // Otherwise, the pathDistance is overwritten until the loop reaches >= 32 units, then that value is reused.
        var initialPathDistance = stingray.pathDistance;

        int result;
        while (true)
        {
            yield return null;
            bool complete = true;
            result = 0;

            var stingrayPosition = stingray.transform.position;
            var mostOptimalDistance = 1000f;
            var foundPath = false;
            var pathDistance = initialPathDistance;
            var pathsLeft = 5 * stingray.stingrayNumber % candidateCount;
            var successfulPaths = 0;

            for (int i = 14; i < candidateCount; i++)
            {
                var nodePosition = status.SortedPositions[i];

                if (!foundPath && successfulPaths > 24)
                    break;

                if ((nodePosition - target).sqrMagnitude < 64)
                    continue;

                if (pathsLeft > 0 && foundPath)
                {
                    pathsLeft--;
                    continue;
                }

                if (RoundManager.Instance.currentDungeonType >= 0)
                {
                    var comparePosition = nodesTempArray[i].transform.position;
                    var dungeonInfo = RoundManager.Instance.dungeonFlowTypes[RoundManager.Instance.currentDungeonType];
                    if (Math.Abs(comparePosition.y - stingrayPosition.y) > 12.5f * dungeonInfo.MapTileSize)
                        continue;
                }

                if (Vector3.Distance(stingrayPosition, nodePosition) < 32)
                {
                    var nodeResult = job.Statuses[i].GetResult();
                    if (nodeResult == PathQueryStatus.InProgress)
                    {
                        complete = false;
                        break;
                    }
                    if (nodeResult != PathQueryStatus.Success)
                        continue;
                    var nodePath = job.GetPath(i);
                    if (LineOfSight.PathIsBlockedByLineOfSight(nodePath, out pathDistance))
                        continue;
                }

                successfulPaths++;

                if (pathDistance < mostOptimalDistance)
                {
                    mostOptimalDistance = pathDistance;
                    result = i;
                    foundPath = true;
                }

                if (pathsLeft == 0 || i == candidateCount - 1)
                    break;

                pathsLeft--;
            }
            if (complete)
                break;
        }

        job.Cancel();

        status.ChosenNode = status.SortedNodes[result];

        while (!jobHandle.IsCompleted)
            yield return null;

        status.Coroutine = null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(StingrayAI.ChooseClosestNodeToPositionStingray))]
    private static bool ChooseClosestNodeToPositionStingrayPrefix(StingrayAI __instance, Vector3 pos, ref Transform __result)
    {
        if (!useAsync)
            return true;
        var status = AsyncDistancePathfinding.StartChoosingNode(__instance, NEAR_PLAYER_ID, status => ChooseClosestNodeToPositionStingrayCoroutine(__instance, status, pos));
        __result = status.RetrieveChosenNode(out _);
        return false;
    }
}
