using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;

namespace PathfindingLagFix.Patches
{
    [HarmonyPatch(typeof(PufferAI))]
    public class PatchPufferAI
    {
        static readonly FieldInfo f_PufferAI_closestSeenPlayer = AccessTools.Field(typeof(PufferAI), "closestSeenPlayer");

        public const string PATCH_NAME = "Spore Lizard lag patch";

        public static void FinishChoosingPlayerEvasionLocation(PufferAI puffer, Transform node)
        {
            throw Common.StubError("FinishChoosingPlayerEvasionLocation stub was called", PATCH_NAME);
        }

        public static IEnumerator ChoosePlayerEvasionLocation(PufferAI puffer, Vector3 origin)
        {
            var farthestNodeCoroutine = Coroutines.ChooseFarthestNodeFromPosition(puffer, origin, avoidLineOfSight: true);
            Transform lastTransform = null;
            while (farthestNodeCoroutine.MoveNext())
            {
                if (farthestNodeCoroutine.Current != null)
                    lastTransform = farthestNodeCoroutine.Current;
                yield return null;
            }

            // Set the current search back to roaming when we have finished our search so that
            // AvoidClosestPlayer() can start a new search.
            puffer.currentSearch = puffer.roamMap;

            if (farthestNodeCoroutine.Current == null)
            {
                Plugin.Instance.Logger.LogWarning($"Failed to find avoid player target.");
                yield break;
            }

            FinishChoosingPlayerEvasionLocation(puffer, lastTransform);
        }

        public static List<CodeInstruction> AvoidClosestPlayerInstructions = null;

        [HarmonyPatch(nameof(PufferAI.AvoidClosestPlayer))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DoAvoidClosestPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsList = instructions.ToList();

            var chooseFarthestNode = instructionsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.IsLdarg(0),
                insn => insn.IsLdarg(0),
                insn => insn.LoadsField(f_PufferAI_closestSeenPlayer),
                insn => insn.Calls(Reflection.m_Component_get_transform),
                insn => insn.Calls(Reflection.m_Transform_get_position),
                insn => insn.LoadsConstant(1),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(0),
                insn => insn.Calls(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
                insn => insn.IsStloc(),
            });
            AvoidClosestPlayerInstructions = instructionsList.GetRange(chooseFarthestNode.End, instructionsList.Count() - chooseFarthestNode.End);

            var noCurrentSearchRoutineLabel = generator.DefineLabel();
            var alreadyRunningCoroutineLabel = generator.DefineLabel();
            return new CodeInstruction[]
            {
                // if (currentSearch != null) {
                new CodeInstruction(OpCodes.Ldarg_0).WithLabels(noCurrentSearchRoutineLabel),
                new CodeInstruction(OpCodes.Ldfld, Reflection.f_EnemyAI_currentSearch),
                new CodeInstruction(OpCodes.Brfalse_S, alreadyRunningCoroutineLabel),

                //   searchCoroutine = StartCoroutine(PatchPufferAI.ChoosePlayerEvasionLocation(this));
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),

                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, f_PufferAI_closestSeenPlayer),
                new CodeInstruction(OpCodes.Call, Reflection.m_Component_get_transform),
                new CodeInstruction(OpCodes.Call, Reflection.m_Transform_get_position),
                CodeInstruction.Call(typeof(PatchPufferAI), nameof(ChoosePlayerEvasionLocation), new Type[] { typeof(PufferAI), typeof(Vector3) }),

                new CodeInstruction(OpCodes.Callvirt, Reflection.m_MonoBehaviour_StartCoroutine),
                new CodeInstruction(OpCodes.Stfld, Reflection.f_EnemyAI_searchCoroutine),

                //   currentSearch = null;
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Stfld, Reflection.f_EnemyAI_currentSearch),
                // }

                new CodeInstruction(OpCodes.Ret).WithLabels(alreadyRunningCoroutineLabel),
            };
        }
    }

    internal class PatchCopyVanillaPufferCode
    {
        [HarmonyPatch(typeof(PatchPufferAI))]
        [HarmonyPatch(nameof(PatchPufferAI.FinishChoosingPlayerEvasionLocation))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> PatchPufferAI_FinishChoosingPlayerEvasionLocationTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            if (PatchPufferAI.AvoidClosestPlayerInstructions == null)
                throw Common.PatchError("Code was not copied from PufferAI.AvoidPlayerTarget()", PatchPufferAI.PATCH_NAME);

            var instructionsList = instructions.ToList();

            var vanillaInstructions = PatchPufferAI.AvoidClosestPlayerInstructions.ToArray();
            if (!vanillaInstructions[0].IsLdloc())
                throw Common.PatchError("Copied code from PufferAI.AvoidPlayerTarget() does not begin with an ldloc", PatchPufferAI.PATCH_NAME);
            var vanillaNodeVar = (LocalBuilder)vanillaInstructions[0].operand;

            for (var i = 0; i < vanillaInstructions.Length; i++)
            {
                var instruction = vanillaInstructions[i];

                if (instruction.IsLdloc(vanillaNodeVar))
                    vanillaInstructions[i] = new CodeInstruction(OpCodes.Ldarg_1).WithLabels(instruction.labels);
            }

            vanillaInstructions.TransferLabelsAndVariables(generator);

            var returnInstruction = instructionsList.FindIndex(insn => insn.opcode == OpCodes.Ret);

            return vanillaInstructions.Append(new CodeInstruction(OpCodes.Ret));
        }
    }
}
