using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(FlowerSnakeEnemy))]
internal static class PatchFlowerSnakeEnemy
{
    private static bool useAsync = true;
    private static bool fixFindObjects = true;

    private const int EVADE_AFTER_DISMOUNT_ID = 0;

    private static List<FlowerSnakeEnemy> allFlowerSnakes = [];

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

    private static Transform GetClosestNodeToWarpTo(FlowerSnakeEnemy flowerSnake)
    {
        var allNodes = flowerSnake.allAINodes;
        if (allNodes.Length == 0)
            return flowerSnake.transform;

        var position = flowerSnake.transform.position;
        var closestNode = allNodes[0].transform;
        var closestDistanceSqr = (closestNode.position - position).sqrMagnitude;

        for (var i = 1; i < allNodes.Length; i++)
        {
            var node = allNodes[i].transform;
            var distanceSqr = (node.position - position).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestNode = node;
                closestDistanceSqr = distanceSqr;
            }
        }

        return closestNode;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.StopClingingOnLocalClient))]
    private static IEnumerable<CodeInstruction> StopClingingOnLocalClientTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        //   if (!RoundManager.Instance.GotNavMeshPositionResult)
        // -   navMeshPosition = ChooseClosestNodeToPosition(transform.position).transform.position;
        // +   navMeshPosition = (PatchFlowerSnakeEnemy.useAsync ? PatchFlowerSnakeEnemy.GetClosestNodeToWarpTo(this) : ChooseClosestNodeToPosition(transform.position)).transform.position;
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Call(typeof(RoundManager).GetMethod($"get_{nameof(RoundManager.Instance)}")),
                ILMatcher.Ldfld(typeof(RoundManager).GetField(nameof(RoundManager.GotNavMeshPositionResult))),
                ILMatcher.Opcode(OpCodes.Brtrue),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the check for a valid navmesh position to warp to in {nameof(FlowerSnakeEnemy)}.{nameof(FlowerSnakeEnemy.StopClingingOnLocalClient)}().");
            return instructions;
        }

        injector
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_ChooseClosestNodeToPosition),
                ILMatcher.Callvirt(Reflection.m_Component_get_transform),
                ILMatcher.Callvirt(Reflection.m_Transform_get_position),
                ILMatcher.Stloc(),
            ])
            .GoToPush(3);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to choose the closest node to warp to in {nameof(FlowerSnakeEnemy)}.{nameof(FlowerSnakeEnemy.StopClingingOnLocalClient)}().");
            return instructions;
        }

        var skipVanillaLabel = generator.DefineLabel();
        var skipAsyncLabel = generator.DefineLabel();
        return injector
            .Insert([
                new(OpCodes.Ldsfld, typeof(PatchFlowerSnakeEnemy).GetField(nameof(useAsync), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Brfalse_S, skipAsyncLabel),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchFlowerSnakeEnemy).GetMethod(nameof(GetClosestNodeToWarpTo), BindingFlags.NonPublic | BindingFlags.Static, [typeof(FlowerSnakeEnemy)])),
                new(OpCodes.Br, skipVanillaLabel),
            ])
            .AddLabel(skipAsyncLabel)
            .GoToMatchEnd()
            .Forward(1)
            .AddLabel(skipVanillaLabel)
            .ReleaseInstructions();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.OnEnable))]
    private static void OnEnablePostfix(FlowerSnakeEnemy __instance)
    {
        allFlowerSnakes.Add(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.OnDisable))]
    private static void OnDisablePostfix(FlowerSnakeEnemy __instance)
    {
        allFlowerSnakes.Remove(__instance);
    }

    private static FlowerSnakeEnemy[] GetAllFlowerSnakesArray()
    {
        return NoAllocHelpers.ExtractArrayFromListT(allFlowerSnakes);
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.StopClingingOnLocalClient))]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.SetFlappingLocalClient))]
    [HarmonyPatch(nameof(FlowerSnakeEnemy.FSHitPlayerServerRpc))]
    private static IEnumerable<CodeInstruction> ReplaceFindObjectsByTypeCallsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase method)
    {
        // - var flowerSnakes = FindObjectsByType<FlowerSnakeEnemy>(FindObjectsSortMode.None);
        // + var flowerSnakes = PatchFlowerSnakeEnemy.fixFindObjects ? PatchFlowerSnakeEnemy.GetAllFlowerSnakesArray() : FindObjectsByType<FlowerSnakeEnemy>(FindObjectsSortMode.None);
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldc(),
                ILMatcher.Call(typeof(Object).GetGenericMethod(nameof(Object.FindObjectsByType), [typeof(FindObjectsSortMode)], [typeof(FlowerSnakeEnemy)])),
                ILMatcher.Stloc(),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the check for a chosen faraway node in {method.DeclaringType.Name}.{method.Name}().");
            return instructions;
        }

        var fixFindObjectsField = typeof(PatchFlowerSnakeEnemy).GetField(nameof(fixFindObjects), BindingFlags.NonPublic | BindingFlags.Static);

        var storeFlowerSnakesArrayInstruction = injector.LastMatchedInstruction;
        var skipArrayFixLabel = generator.DefineLabel();
        var skipFindObjectsLabel = generator.DefineLabel();
        injector
            .Insert([
                new(OpCodes.Ldsfld, fixFindObjectsField),
                new(OpCodes.Brfalse_S, skipArrayFixLabel),
                new(OpCodes.Call, typeof(PatchFlowerSnakeEnemy).GetMethod(nameof(GetAllFlowerSnakesArray), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Br, skipFindObjectsLabel),
            ])
            .AddLabel(skipArrayFixLabel)
            .GoToMatchEnd()
            .Back(1)
            .AddLabel(skipFindObjectsLabel);

        int patchCount = 0;

        while (true)
        {
            // - flowerSnakes.Length
            // + PatchFlowerSnakeEnemy.fixFindObjects ? PatchFlowerSnakeEnemy.allFlowerSnakes.Count : flowerSnakes.Length
            injector
                .Find([
                    ILMatcher.Ldloc(storeFlowerSnakesArrayInstruction.GetLdlocIndex()),
                    ILMatcher.Opcode(OpCodes.Ldlen),
                ]);

            if (!injector.IsValid)
                break;

            var skipCountFixLabel = generator.DefineLabel();
            var skipArrayLengthLabel = generator.DefineLabel();
            injector
                .Insert([
                    new(OpCodes.Ldsfld, fixFindObjectsField),
                    new(OpCodes.Brfalse_S, skipCountFixLabel),
                    new(OpCodes.Ldsfld, typeof(PatchFlowerSnakeEnemy).GetField(nameof(allFlowerSnakes), BindingFlags.NonPublic | BindingFlags.Static)),
                    new(OpCodes.Call, typeof(List<FlowerSnakeEnemy>).GetMethod($"get_{nameof(List<FlowerSnakeEnemy>.Count)}")),
                    new(OpCodes.Br, skipArrayLengthLabel),
                ])
                .AddLabel(skipCountFixLabel)
                .GoToMatchEnd()
                .AddLabel(skipArrayLengthLabel);
            patchCount++;
        }

        return injector
            .ReleaseInstructions();
    }
}
