using System.Collections.Generic;
using System.Reflection.Emit;

using HarmonyLib;

using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(SpringManAI))]
internal class PatchSpringManAI
{
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(SpringManAI.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + var canUseAsync = PatchEnemyAI.CheckIfPlayerPathsAreStaleAndStartJobs(this);
        //   
        //   for (int j = 0; j < StartOfRound.Instance.allPlayerScripts.Length; j++) {
        //     if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[j])) {
        //       ..
        // -     if (PathIsIntersectedByLineOfSight(StartOfRound.Instance.allPlayerScripts[j].transform.position, calculatePathDistance: false, avoidLineOfSight: false))
        // +     if (canUseAsync ? PatchEnemyAI.IsAsyncPathToPlayerInvalid(this, j) : PathIsIntersectedByLineOfSight(StartOfRound.Instance.allPlayerScripts[j].transform.position, calculatePathDistance: false, avoidLineOfSight: false))
        //         continue;
        //       if (Physics.Linecast(base.transform.position + Vector3.up * 0.5f, StartOfRound.Instance.allPlayerScripts[j].gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
        //         continue;
        //       if (Vector3.Distance(base.transform.position, StartOfRound.Instance.allPlayerScripts[j].transform.position) >= 30)
        //         continue;
        //       SwitchToBehaviourState(1);
        //       return;
        //     }
        //   }
        var canUseAsyncLocal = generator.DeclareLocal(typeof(bool));
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Call(Reflection.m_StartOfRound_get_Instance),
                ILMatcher.Ldfld(Reflection.f_StartOfRound_allPlayerScripts),
                ILMatcher.Ldloc(),
                ILMatcher.Opcode(OpCodes.Ldelem_Ref),
                ILMatcher.Callvirt(Reflection.m_Component_get_transform),
                ILMatcher.Callvirt(Reflection.m_Transform_get_position),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Ldc(0),
                ILMatcher.Call(Reflection.m_EnemyAI_PathIsIntersectedByLineOfSight),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to check if the coilhead can path to a player in {nameof(SpringManAI)}.{nameof(SpringManAI.DoAIInterval)}().");
            return instructions;
        }

        var loadIndexInstruction = injector.GetRelativeInstruction(3);
        var skipAsyncLabel = generator.DefineLabel();
        var skipSyncLabel = generator.DefineLabel();
        injector
            .Insert([
                new(OpCodes.Ldloc, canUseAsyncLocal),
                new(OpCodes.Brfalse_S, skipAsyncLabel),
                new(OpCodes.Ldarg_0),
                loadIndexInstruction,
                new(OpCodes.Call, PatchEnemyAI.m_IsAsyncPathToPlayerInvalid),
                new(OpCodes.Br_S, skipSyncLabel),
            ])
            .AddLabel(skipAsyncLabel)
            .GoToMatchEnd()
            .AddLabel(skipSyncLabel)
            .ReverseFind([
                ILMatcher.Ldc(0),
                ILMatcher.Stloc(),
                ILMatcher.Opcode(OpCodes.Br),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the start of the loop over all players in in {nameof(SpringManAI)}.{nameof(SpringManAI.DoAIInterval)}().");
            return instructions;
        }

        return injector
            .Insert([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, PatchEnemyAI.m_CheckIfPlayerPathsAreStaleAndStartJobs),
                new(OpCodes.Stloc, canUseAsyncLocal),
            ])
            .ReleaseInstructions();
    }
}
