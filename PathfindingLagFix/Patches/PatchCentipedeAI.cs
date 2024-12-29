using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(CentipedeAI))]
internal static class PatchCentipedeAI
{
    private static bool useAsync = true;

    private const int HIDE_AWAY_FROM_MAIN_ID = 0;
    private const int HIDE_NEAR_PLAYER_ID = 1;

    // Returns whether to skip setting the destination.
    private static bool ChooseHidingSpotRelativeToMainEntranceAsync(CentipedeAI centipede)
    {
        if (!useAsync)
            return false;

        var nodeCount = centipede.allAINodes.Length;
        var status = AsyncPathfinding.StartChoosingFarthestNodeFromPosition(centipede, HIDE_AWAY_FROM_MAIN_ID, centipede.mainEntrancePosition, avoidLineOfSight: false, (nodeCount / 2 + centipede.thisEnemyIndex) % nodeCount);
        var node = status.RetrieveChosenNode(out centipede.mostOptimalDistance);
        if (node != null)
        {
            centipede.choseHidingSpotNoPlayersNearby = true;
            centipede.SetDestinationToNode(node);
        }
        return true;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(CentipedeAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // - if (!choseHidingSpotNoPlayersNearby)
        // + if (!choseHidingSpotNoPlayersNearby && !PatchCentipedeAI.ChooseHidingSpotRelativeToMainEntranceAsync(this))
        //   {
        //       choseHidingSpotNoPlayersNearby = true;
        //       SetDestinationToNode(ChooseFarthestNodeFromPosition(mainEntrancePosition, avoidLineOfSight: false, (allAINodes.Length / 2 + thisEnemyIndex) % allAINodes.Length));
        //   }
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(CentipedeAI).GetField(nameof(CentipedeAI.choseHidingSpotNoPlayersNearby), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Opcode(OpCodes.Brtrue),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find instructions to check if a hiding spot is chosen in {nameof(CentipedeAI)}.{nameof(CentipedeAI.DoAIInterval)}().");
            return instructions;
        }

        var skipVanillaLabel = (Label)injector.LastMatchedInstruction.operand;
        return injector
            .GoToMatchEnd()
            .InsertInPlace([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchCentipedeAI).GetMethod(nameof(ChooseHidingSpotRelativeToMainEntranceAsync), BindingFlags.NonPublic | BindingFlags.Static, [typeof(CentipedeAI)])),
                new(OpCodes.Brtrue_S, skipVanillaLabel),
            ])
            .ReleaseInstructions();
    }

    private static bool ChooseHidingSpotNearPlayer(CentipedeAI centipede, Vector3 targetPos)
    {
        if (!useAsync)
            return false;

        var status = AsyncPathfinding.StartChoosingClosestNodeToPosition(centipede, HIDE_NEAR_PLAYER_ID, targetPos, avoidLineOfSight: true, centipede.offsetNodeAmount);
        var node = status.RetrieveChosenNode(out centipede.mostOptimalDistance);

        if (node != null)
            centipede.SetDestinationToNode(node);
        return true;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(CentipedeAI.ChooseHidingSpotNearPlayer))]
    private static IEnumerable<CodeInstruction> ChooseHidingSpotNearPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + if (PatchCentipedeAI.ChooseHidingSpotNearPlayer(this))
        // +   return;
        //   Transform transform = ChooseClosestNodeToPosition(targetPos, avoidLineOfSight: true, offsetNodeAmount);
        //
        // Note: This skips the logic that runs if ChooseClosestNodeToPosition returns null when async is enabled.
        //       However, that should be an impossibility, as ChooseClosestNodeToPosition() returns the closest node
        //       to the target position if all paths are blocked by line of sight.
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseClosestNodeToPosition),
                ILMatcher.Stloc(),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find call to {nameof(EnemyAI.ChooseClosestNodeToPosition)} in {nameof(CentipedeAI)}.{nameof(CentipedeAI.ChooseHidingSpotNearPlayer)}().");
            return instructions;
        }

        var skipEarlyReturn = generator.DefineLabel();
        return injector
            .GoToPush(3)
            .InsertAfterBranch([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldarg_1),
                new(OpCodes.Call, typeof(PatchCentipedeAI).GetMethod(nameof(ChooseHidingSpotNearPlayer), BindingFlags.NonPublic | BindingFlags.Static, [typeof(CentipedeAI), typeof(Vector3)])),
                new(OpCodes.Brfalse_S, skipEarlyReturn),
                new(OpCodes.Ret),
            ])
            .AddLabel(skipEarlyReturn)
            .ReleaseInstructions();
    }
}
