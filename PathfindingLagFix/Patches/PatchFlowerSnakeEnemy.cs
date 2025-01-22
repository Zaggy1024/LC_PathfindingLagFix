using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(FlowerSnakeEnemy))]
internal static class PatchFlowerSnakeEnemy
{
    private static bool useAsync = true;

    private const int EVADE_AFTER_DISMOUNT_ID = 0;

    // Returns whether to skip the synchronous node selection.
    private static bool ChooseDismountedFarawayNodeAsync(FlowerSnakeEnemy flowerSnake)
    {
        if (!useAsync)
            return false;

        var status = AsyncDistancePathfinding.StartChoosingFarthestNodeFromPosition(flowerSnake, EVADE_AFTER_DISMOUNT_ID, flowerSnake.transform.position);
        var node = status.RetrieveChosenNode(out flowerSnake.mostOptimalDistance);
        if (node != null)
        {
            flowerSnake.SetDestinationToPosition(node.transform.position);
            flowerSnake.choseFarawayNode = true;
        }

        return true;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // - if (!choseFarawayNode) {
        // + if (!choseFarawayNode && !ChooseDismountedFarawayNodeAsync(this)) {
        //     if (SetDestinationToPosition(ChooseFarthestNodeFromPosition(base.transform.position).transform.position, checkForPath: true))
        //       choseFarawayNode = true;
        //     else
        //       base.transform.position = ChooseClosestNodeToPosition(base.transform.position).position;
        //   }
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(FlowerSnakeEnemy).GetField(nameof(FlowerSnakeEnemy.choseFarawayNode), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Opcode(OpCodes.Brtrue),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the check for a chosen faraway node in {nameof(FlowerSnakeEnemy)}.{nameof(FlowerSnakeEnemy.DoAIInterval)}().");
            return instructions;
        }

        var skipVanillaCodeLabel = (Label)injector.LastMatchedInstruction.operand;
        return injector
            .GoToMatchEnd()
            .Insert([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchFlowerSnakeEnemy).GetMethod(nameof(ChooseDismountedFarawayNodeAsync), BindingFlags.NonPublic | BindingFlags.Static, [typeof(FlowerSnakeEnemy)])),
                new(OpCodes.Brtrue_S, skipVanillaCodeLabel),
            ])
            .ReleaseInstructions();
    }
}
