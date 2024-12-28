using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;

using PathfindingLagFix.Utilities.IL;
using PathfindingLagFix.Utilities;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(FlowermanAI))]
public class PatchFlowermanAI
{
    private static readonly FieldInfo f_FlowermanAI_mainEntrancePosition = AccessTools.Field(typeof(FlowermanAI), "mainEntrancePosition");
    private static readonly MethodInfo m_FlowermanAI_ChooseClosestNodeToPlayer = typeof(FlowermanAI).GetMethod(nameof(FlowermanAI.ChooseClosestNodeToPlayer), []);

    private static readonly FieldInfo f_PatchFlowermanAI_useAsync = typeof(PatchFlowermanAI).GetField(nameof(useAsync));

    private static bool useAsync = true;

    private const int FAR_FROM_MAIN_ID = 0;
    private const int EVADE_PLAYER_ID = 1;
    private const int SNEAK_TO_PLAYER_ID = 2;

    // Returns whether to skip the vanilla code.
    private static bool ChooseFarthestNodeFromMainEntrance(FlowermanAI flowerman)
    {
        if (useAsync)
        {
            var status = AsyncPathfinding.StartChoosingFarthestNodeFromPosition(flowerman, FAR_FROM_MAIN_ID, flowerman.mainEntrancePosition);
            var node = status.RetrieveChosenNode(out flowerman.mostOptimalDistance);
            if (node != null)
            {
                if (flowerman.favoriteSpot == null)
                    flowerman.favoriteSpot = node;
                flowerman.targetNode = node;
                flowerman.SetDestinationToPosition(node.position);
            }
            return true;
        }

        return false;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FlowermanAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // Grab jump label for the else of:
        // if (TargetClosestPlayer())
        //   ..
        // else
        //   ..
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Call(m_FlowermanAI_ChooseClosestNodeToPlayer),
                ILMatcher.Branch()
            ])
            .GoToMatchEnd();
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find call to {nameof(FlowermanAI.ChooseClosestNodeToPlayer)} in {nameof(FlowermanAI)}.{nameof(FlowermanAI.DoAIInterval)}().");
            return instructions;
        }

        // + if (!PatchFlowermanAI.ChooseFarthestNodeFromMainEntrance(this)) {
        //     var node = ChooseFarthestNodeFromPosition(mainEntrancePosition);
        //     if (favoriteSpot == null)
        //       favoriteSpot = node;
        //     targetNode = node;
        //     SetDestinationToPosition(node.position, checkForPath: true);
        // + }
        var endLabel = (Label)injector.GetRelativeInstruction(-1).operand;
        injector
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(FlowermanAI).GetField(nameof(FlowermanAI.mainEntrancePosition), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(50),
                ILMatcher.Ldc(0),
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find call to {nameof(EnemyAI.ChooseFarthestNodeFromPosition)} in {nameof(FlowermanAI)}.{nameof(FlowermanAI.DoAIInterval)}().");
            return instructions;
        }
        return injector
            .InsertInPlaceAfterBranch([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchFlowermanAI).GetMethod(nameof(ChooseFarthestNodeFromMainEntrance), BindingFlags.NonPublic | BindingFlags.Static, [typeof(FlowermanAI)])),
                new(OpCodes.Brtrue_S, endLabel),
            ])
            .PrintContext("Injected DoAIInterval")
            .ReleaseInstructions();
    }

    // Returns whether to skip the vanilla behavior.
    private static bool AvoidClosestPlayer(FlowermanAI flowerman)
    {
        if (!useAsync)
            return false;

        var status = AsyncPathfinding.StartChoosingFarthestNodeFromPosition(flowerman, EVADE_PLAYER_ID, flowerman.targetPlayer.transform.position, avoidLineOfSight: true, offset: 0, capDistance: 60);
        var node = status.RetrieveChosenNode(out flowerman.mostOptimalDistance);
        if (node != null)
        {
            flowerman.farthestNodeFromTargetPlayer = node;
            return false;
        }
        return true;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FlowermanAI.AvoidClosestPlayer))]
    private static IEnumerable<CodeInstruction> AvoidClosestPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + if (!PatchFlowermanAI.AvoidClosestPlayer(this))
        // +   return;
        //   if (farthestNodeFromTargetPlayer == null) {
        //     gettingFarthestNodeFromPlayerAsync = true;
        //     return;
        //   }
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(FlowermanAI).GetField(nameof(FlowermanAI.farthestNodeFromTargetPlayer), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Opcode(OpCodes.Ldnull),
                ILMatcher.Call(Reflection.m_Object_op_Equality),
                ILMatcher.Opcode(OpCodes.Brfalse),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find check for the farthest node from the target player in {nameof(FlowermanAI)}.{nameof(FlowermanAI.AvoidClosestPlayer)}().");
            return instructions;
        }

        var label = generator.DefineLabel();
        return injector
            .AddLabel(label)
            .InsertInPlace([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchFlowermanAI).GetMethod(nameof(AvoidClosestPlayer), BindingFlags.NonPublic | BindingFlags.Static, [typeof(FlowermanAI)])),
                new(OpCodes.Brfalse_S, label),
                new(OpCodes.Ret),
            ])
            .PrintContext("Injected AvoidClosestPlayer")
            .ReleaseInstructions();
    }

    private static bool ChooseClosestNodeToPlayer(FlowermanAI flowerman)
    {
        var status = AsyncPathfinding.StartChoosingClosestNodeToPosition(flowerman, SNEAK_TO_PLAYER_ID, flowerman.targetPlayer.transform.position, avoidLineOfSight: true);
        var node = status.RetrieveChosenNode(out flowerman.mostOptimalDistance);
        if (node == null)
            return true;

        flowerman.targetNode = node;
        return false;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FlowermanAI.ChooseClosestNodeToPlayer))]
    private static IEnumerable<CodeInstruction> ChooseClosestNodeToPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + if (!PatchFlowermanAI.useAsync) {
        //     if (targetNode == null)
        //       targetNode = allAINodes[0].transform;
        //     var node = ChooseClosestNodeToPosition(targetPlayer.transform.position, avoidLineOfSight: true);
        //     if (node != null)
        //       targetNode = node;
        // + } else if (PatchFlowermanAI.ChooseClosestNodeToPlayer(this)) {
        // +   return;
        // + }
        var skipVanillaLabel = generator.DefineLabel();
        var injector = new ILInjector(instructions)
            .Insert([
                new(OpCodes.Ldsfld, typeof(PatchFlowermanAI).GetField(nameof(useAsync), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Brtrue_S, skipVanillaLabel),
            ])
            .Find([
                ILMatcher.Ldarg(),
                ILMatcher.Ldloc(),
                ILMatcher.Stfld(Reflection.f_EnemyAI_targetNode),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find storing of the target node in {nameof(FlowermanAI)}.{nameof(FlowermanAI.ChooseClosestNodeToPlayer)}().");
            return instructions;
        }

        var skipEarlyReturnLabel = generator.DefineLabel();
        return injector
            .GoToMatchEnd()
            .InsertAfterBranch([
                new CodeInstruction(OpCodes.Br_S, skipEarlyReturnLabel),
                new CodeInstruction(OpCodes.Ldarg_0).WithLabels(skipVanillaLabel),
                new CodeInstruction(OpCodes.Call, typeof(PatchFlowermanAI).GetMethod(nameof(ChooseClosestNodeToPlayer), BindingFlags.NonPublic | BindingFlags.Static, [typeof(FlowermanAI)])),
                new CodeInstruction(OpCodes.Brfalse_S, skipEarlyReturnLabel),
                new CodeInstruction(OpCodes.Ret),
            ])
            .AddLabel(skipEarlyReturnLabel)
            .PrintContext("Injected ChooseClosestNodeToPlayer")
            .ReleaseInstructions();
    }
}
