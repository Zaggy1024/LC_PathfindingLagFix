using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(DoublewingAI))]
internal static class PatchDoublewingAI
{
    private const int EVADE_PLAYER_ID = 0;

    private static bool useAsync = true;

    private static PlayerControllerB GetPlayerToEvade(DoublewingAI doublewing)
    {
        return doublewing.CheckLineOfSightForPlayer(80, 8, 4);
    }

    private static AsyncDistancePathfinding.EnemyDistancePathfindingStatus StartEvasionPathfindingJob(DoublewingAI doublewing, PlayerControllerB player)
    {
        return AsyncDistancePathfinding.StartChoosingFarthestNodeFromPosition(doublewing, EVADE_PLAYER_ID, player.transform.position, avoidLineOfSight: false, Random.Range(0, doublewing.allAINodes.Length / 2));
    }

    private static Transform ChoosePlayerEvasionNodeAsync(DoublewingAI doublewing, PlayerControllerB player)
    {
        var status = StartEvasionPathfindingJob(doublewing, player);
        return status.RetrieveChosenNode(out doublewing.mostOptimalDistance);
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(DoublewingAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_CheckLineOfSightForPlayer),
                ILMatcher.Stloc(),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find instructions to find the player to evade from in {nameof(DoublewingAI)}.{nameof(DoublewingAI.DoAIInterval)}().");
            return instructions;
        }

        var playerLoadInstruction = injector.LastMatchedInstruction.StlocToLdloc();
        injector
            .Find([
                ILMatcher.Ldloc(playerLoadInstruction.GetLdlocIndex()),
                ILMatcher.Call(Reflection.m_Object_op_Implicit),
                ILMatcher.Opcode(OpCodes.Brfalse),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find instructions to check if a player is in line of sight in {nameof(DoublewingAI)}.{nameof(DoublewingAI.DoAIInterval)}().");
            return instructions;
        }

        var skipSynchronousNodeSelectionLabel = (Label)injector.LastMatchedInstruction.operand;
        injector
            .GoToMatchEnd()
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
                ILMatcher.Stloc(),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find instructions to choose player evasion node in {nameof(DoublewingAI)}.{nameof(DoublewingAI.DoAIInterval)}().");
            return instructions;
        }

        var targetNodeStoreInstruction = injector.LastMatchedInstruction;

        //   var playerInLineOfSight = CheckLineOfSightForPlayer(80f, 10, 8);
        //   if (oddInterval && playerInLineOfSight)
        //   {
        // -   Transform targetNode = ChooseFarthestNodeFromPosition(playerInLineOfSight.transform.position, avoidLineOfSight: false, UnityEngine.Random.Range(0, allAINodes.Length / 2));
        // +   Transform targetNode;
        // +   if (PatchDoublewingAI.useAsync) {
        // +     targetNode = PatchDoublewingAI.ChoosePlayerEvasionNodeAsync(this, playerInLineOfSight)
        // +     if (targetNode != null)
        // +       goto SkipAssigningDestination;
        // +     goto SkipSynchronousNodeSelection;
        // +   }
        // +
        // +   targetNode = ChooseFarthestNodeFromPosition(playerInLineOfSight.transform.position, avoidLineOfSight: false, UnityEngine.Random.Range(0, allAINodes.Length / 2));
        // +   SkipSynchronousNodeSelection:
        //
        //     if (SetDestinationToPosition(targetNode.position))
        //     {
        //       avoidingPlayer = UnityEngine.Random.Range(10, 20);
        //       StopSearch(roamGlide);
        //     }
        // +   SkipAssigningDestination:
        //   }
        var skipChoosingNodeLabel = generator.DefineLabel();
        injector.GetRelativeInstruction(2).labels.Add(skipChoosingNodeLabel);

        var skipAsyncLabel = generator.DefineLabel();
        return injector
            .GoToPush(6)
            .Insert([
                new(OpCodes.Ldsfld, typeof(PatchDoublewingAI).GetField(nameof(useAsync), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Brfalse_S, skipAsyncLabel),
                new(OpCodes.Ldarg_0),
                playerLoadInstruction,
                new(OpCodes.Call, typeof(PatchDoublewingAI).GetMethod(nameof(ChoosePlayerEvasionNodeAsync), BindingFlags.NonPublic | BindingFlags.Static, [typeof(DoublewingAI), typeof(PlayerControllerB)])),
                new(OpCodes.Dup),
                targetNodeStoreInstruction,
                new(OpCodes.Ldnull),
                new(OpCodes.Call, Reflection.m_Object_op_Equality),
                new(OpCodes.Brtrue_S, skipSynchronousNodeSelectionLabel),
                new(OpCodes.Br, skipChoosingNodeLabel),
            ])
            .AddLabel(skipAsyncLabel)
            .ReleaseInstructions();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(DoublewingAI.AlertBird))]
    [HarmonyPatch(nameof(DoublewingAI.AlertBirdByOther))]
    private static void AlertBirdPostfix(DoublewingAI __instance)
    {
        // This should be called on from DoAIInterval (or soon after) when the behavior state changes to 1.
        // By calling this here, we can pre-calculate the path so it is done on the next iteration.
        if (useAsync && __instance.IsOwner && GetPlayerToEvade(__instance) is PlayerControllerB player)
            StartEvasionPathfindingJob(__instance, player);
    }
}
