using System.Collections.Generic;
using System.Reflection.Emit;

using HarmonyLib;

using PathfindingLagFix.Utilities.IL;

using Object = UnityEngine.Object;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(MaskedPlayerEnemy))]
internal static class PatchMaskedPlayerEnemy
{
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(MaskedPlayerEnemy.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Call(typeof(Object).GetGenericMethod(nameof(Object.FindObjectOfType), [], [typeof(MineshaftElevatorController)])),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to get the elevator controller in {nameof(MaskedPlayerEnemy)}.{nameof(MaskedPlayerEnemy.DoAIInterval)}().");
            return instructions;
        }

        return injector
            .ReplaceLastMatch([
                new(OpCodes.Call, Reflection.m_RoundManager_get_Instance),
                new(OpCodes.Ldfld, typeof(RoundManager).GetField(nameof(RoundManager.currentMineshaftElevator))),
            ])
            .ReleaseInstructions();
    }
}
