using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(PufferAI))]
internal static class PatchPufferAI
{
    private static bool useAsync = true;

    private const int EVADE_PLAYER_ID = 0;

    private static bool ChoosePlayerEvasionNodeAsync(PufferAI puffer)
    {
        if (!useAsync)
            return false;

        var status = AsyncDistancePathfinding.StartChoosingFarthestNodeFromPosition(puffer, EVADE_PLAYER_ID, puffer.closestSeenPlayer.transform.position, avoidLineOfSight: true, offset: 0, capDistance: AsyncDistancePathfinding.DEFAULT_CAP_DISTANCE);
        var node = status.RetrieveChosenNode(out var mostOptimalDistance);

        if (ConfigOptions.CurrentOptions.AsyncDistancePathfindingMostOptimalDistanceBehavior == AsyncDistancePathfindingMostOptimalDistanceBehaviorType.Set)
            puffer.mostOptimalDistance = mostOptimalDistance;

        if (node == null)
            return true;

        puffer.farthestNodeFromTargetPlayer = node;
        return false;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(PufferAI.AvoidClosestPlayer))]
    private static IEnumerable<CodeInstruction> AvoidClosestPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + if (PatchPufferAI.AvoidClosestPlayer(this))
        // +   return;
        //   if (farthestNodeFromTargetPlayer == null)
        //   {
        //     gettingFarthestNodeFromPlayerAsync = true;
        //     return;
        //   }
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(PufferAI).GetField(nameof(PufferAI.farthestNodeFromTargetPlayer), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Opcode(OpCodes.Ldnull),
                ILMatcher.Call(Reflection.m_Object_op_Equality),
                ILMatcher.Opcode(OpCodes.Brfalse),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find check for the farthest node from the target player in {nameof(FlowermanAI)}.{nameof(FlowermanAI.AvoidClosestPlayer)}().");
            return instructions;
        }

        var skipReturnLabel = generator.DefineLabel();
        return injector
            .Insert([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchPufferAI).GetMethod(nameof(ChoosePlayerEvasionNodeAsync), BindingFlags.NonPublic | BindingFlags.Static, [typeof(PufferAI)])),
                new(OpCodes.Brfalse_S, skipReturnLabel),
                new(OpCodes.Ret),
            ])
            .AddLabel(skipReturnLabel)
            .ReleaseInstructions();
    }
}
