using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

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

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(StingrayAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        // - if (!hidingSpot.choseTemporarySpot) {
        // + if (!hidingSpot.choseTemporarySpot && PatchStingrayAI.ChooseTemporarySpot(this)) {
        //       hidingSpot.choseTemporarySpot = true;
        //       SetDestinationToNode(ChooseFarthestNodeFromPosition(mainEntrancePosition, avoidLineOfSight: false, (allAINodes.Length / 3 + 3 * stingrayNumber) % allAINodes.Length));
        //       hidingSpot.position = destination;
        //       hidingSpot.type = HidingSpotType.Temporary;
        //   }
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(f_StingrayAI_hidingSpot),
                ILMatcher.Ldfld(typeof(StingrayHidingSpot).GetField(nameof(StingrayHidingSpot.choseTemporarySpot))),
                ILMatcher.Opcode(OpCodes.Brtrue).CaptureOperandAs(out Label skipTemporarySpotAssignmentLabel),
            ])
            .GoToMatchEnd();
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find call to {nameof(EnemyAI.ChooseFarthestNodeFromPosition)} in {nameof(StingrayAI)}.{nameof(StingrayAI.DoAIInterval)}().");
            return instructions;
        }

        return injector
            .Insert([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchStingrayAI).GetMethod(nameof(ChooseTemporarySpot), BindingFlags.Static | BindingFlags.NonPublic, [typeof(StingrayAI)])),
                new(OpCodes.Brtrue_S, skipTemporarySpotAssignmentLabel),
            ])
            .ReleaseInstructions();
    }
}
