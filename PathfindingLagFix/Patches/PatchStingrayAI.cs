using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using UnityEngine;
using HarmonyLib;

using PathfindingLagFix.Utilities.IL;
using PathfindingLagFix.Utilities;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(StingrayAI))]
internal static class PatchStingrayAI
{
    private static readonly FieldInfo f_StingrayAI_hidingSpot = typeof(StingrayAI).GetField(nameof(StingrayAI.hidingSpot), BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool useAsync = true;

    private const int TEMPORARY_SPOT_ID = 0;
    private const int AVOID_PLAYER_ID = 1;

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
}
