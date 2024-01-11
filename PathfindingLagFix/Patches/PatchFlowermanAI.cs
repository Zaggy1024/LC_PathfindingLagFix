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
    [HarmonyPatch(typeof(FlowermanAI))]
    public class PatchFlowermanAI
    {
        static readonly FieldInfo f_FlowermanAI_mainEntrancePosition = AccessTools.Field(typeof(FlowermanAI), "mainEntrancePosition");

        public static void FinishChoosingFarthestNodeFromEntrance(FlowermanAI flowerman, Transform node)
        {
            new NotSupportedException("FinishChoosingFarthestNodeFromEntrance stub was called");
        }

        static IEnumerator ChooseFarthestNodeFromEntrance(FlowermanAI flowerman, Vector3 mainEntrancePosition)
        {
            var farthestNodeCoroutine = Coroutines.ChooseFarthestNodeFromPosition(flowerman, mainEntrancePosition);
            Transform lastTransform = null;
            while (farthestNodeCoroutine.MoveNext())
            {
                if (farthestNodeCoroutine.Current != null)
                    lastTransform = farthestNodeCoroutine.Current;
                yield return null;
            }

            flowerman.searchCoroutine = null;

            if (farthestNodeCoroutine.Current == null)
                yield break;
            FinishChoosingFarthestNodeFromEntrance(flowerman, lastTransform);
        }

        public static int NoPlayerToTargetNodeVar = -1;
        public static List<CodeInstruction> NoPlayerToTargetInstructions = null;

        [HarmonyPatch(nameof(EnemyAI.DoAIInterval))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsList = instructions.ToList();

            Label? noPlayerTargetLabel = null;
            var targetPlayer = instructionsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.Calls(Reflection.m_EnemyAI_TargetClosestPlayer),
                insn => insn.Branches(out noPlayerTargetLabel),
            });

            var noPlayerTarget = instructionsList.FindIndex(insn => insn.labels.Contains(noPlayerTargetLabel.Value));

            var afterNoPlayerTargetLabel = (Label)instructionsList[noPlayerTarget - 1].operand;
            var afterNoPlayerTarget = instructionsList.FindIndex(noPlayerTarget, insn => insn.labels.Contains(afterNoPlayerTargetLabel));

            var chooseFarTarget = instructionsList.FindIndexOfSequence(noPlayerTarget, new Predicate<CodeInstruction>[]
            {
                // Transform transform = ChooseFarthestNodeFromPosition(mainEntrancePosition);
                insn => insn.IsLdarg(0),
                insn => insn.IsLdarg(0),
                insn => insn.LoadsField(f_FlowermanAI_mainEntrancePosition),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(0),
                insn => insn.Calls(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
                insn => insn.IsStloc(),
            });

            NoPlayerToTargetNodeVar = instructionsList[chooseFarTarget.End - 1].GetLocalIndex();
            NoPlayerToTargetInstructions = instructionsList.GetRange(chooseFarTarget.End, afterNoPlayerTarget - chooseFarTarget.End);

            var chooseFarTargetLabels = instructionsList[chooseFarTarget.Start].labels.ToArray();
            instructionsList.RemoveRange(chooseFarTarget.Start, afterNoPlayerTarget - chooseFarTarget.Start);

            var skipSearchCoroutineLabel = generator.DefineLabel();
            instructionsList[chooseFarTarget.Start].labels.Add(skipSearchCoroutineLabel);

            instructionsList.InsertRange(chooseFarTarget.Start, new CodeInstruction[]
            {
                // if (searchCoroutine == null)
                //   StartCoroutine(PatchFlowermanAI.ChooseFarNodeWhenNoTarget(this, mainEntrancePosition));
                new CodeInstruction(OpCodes.Ldarg_0).WithLabels(chooseFarTargetLabels),
                new CodeInstruction(OpCodes.Ldfld, Reflection.f_EnemyAI_searchCoroutine),
                new CodeInstruction(OpCodes.Brtrue_S, skipSearchCoroutineLabel),

                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, f_FlowermanAI_mainEntrancePosition),
                CodeInstruction.Call(typeof(PatchFlowermanAI), nameof(ChooseFarthestNodeFromEntrance), new Type[] { typeof(FlowermanAI), typeof(Vector3) }),
                new CodeInstruction(OpCodes.Callvirt, Reflection.m_MonoBehaviour_StartCoroutine),
                new CodeInstruction(OpCodes.Stfld, Reflection.f_EnemyAI_searchCoroutine),
            });

            return instructionsList;
        }

        public static void FinishChoosingPlayerEvasionLocation(FlowermanAI flowerman, Transform node)
        {
            new NotSupportedException("FinishChoosingPlayerEvasionLocation stub was called");
        }

        public static IEnumerator ChoosePlayerEvasionLocation(FlowermanAI flowerman)
        {
            var farthestNodeCoroutine = Coroutines.ChooseFarthestNodeFromPosition(flowerman, flowerman.targetPlayer.transform.position, avoidLineOfSight: true);
            Transform lastTransform = null;
            while (farthestNodeCoroutine.MoveNext())
            {
                if (farthestNodeCoroutine.Current != null)
                    lastTransform = farthestNodeCoroutine.Current;
                yield return null;
            }

            flowerman.searchCoroutine = null;

            if (farthestNodeCoroutine.Current == null)
            {
                Plugin.Instance.Logger.LogWarning($"Failed to find avoid player target.");
                yield break;
            }

            FinishChoosingPlayerEvasionLocation(flowerman, lastTransform);
        }

        public static List<CodeInstruction> AvoidClosestPlayerInstructions = null;

        [HarmonyPatch(nameof(FlowermanAI.AvoidClosestPlayer))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DoAvoidClosestPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsList = instructions.ToList();

            var chooseFarTarget = instructionsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.IsLdarg(0),
                insn => insn.IsLdarg(0),
                insn => insn.LoadsField(Reflection.f_EnemyAI_targetPlayer),
                insn => insn.Calls(Reflection.m_Component_get_transform),
                insn => insn.Calls(Reflection.m_Transform_get_position),
                insn => insn.LoadsConstant(1),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(1),
                insn => insn.Calls(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
                insn => insn.IsStloc(),
            });
            var returnInstruction = instructionsList.FindLastIndex(insn => insn.opcode == OpCodes.Ret);
            AvoidClosestPlayerInstructions = instructionsList.GetRange(chooseFarTarget.End, returnInstruction - chooseFarTarget.End);

            var skipSearchCoroutineLabel = generator.DefineLabel();
            return new CodeInstruction[]
            {
                // if (searchCoroutine == null)
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, Reflection.f_EnemyAI_searchCoroutine),
                new CodeInstruction(OpCodes.Brtrue_S, skipSearchCoroutineLabel),

                //   searchCoroutine = StartCoroutine(PatchFlowermanAI.ChoosePlayerEvasionLocation(this));
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.Call(typeof(PatchFlowermanAI), nameof(ChoosePlayerEvasionLocation), new Type[] { typeof(FlowermanAI) }),
                new CodeInstruction(OpCodes.Callvirt, Reflection.m_MonoBehaviour_StartCoroutine),
                new CodeInstruction(OpCodes.Stfld, Reflection.f_EnemyAI_searchCoroutine),

                new CodeInstruction(OpCodes.Ret).WithLabels(skipSearchCoroutineLabel),
            };
        }
    }

    internal class PatchCopyVanillaFlowermanCode
    {
        [HarmonyPatch(typeof(PatchFlowermanAI))]
        [HarmonyPatch(nameof(PatchFlowermanAI.FinishChoosingPlayerEvasionLocation))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> PatchFlowermanAI_FinishChoosingPlayerEvasionLocationTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            if (PatchFlowermanAI.AvoidClosestPlayerInstructions == null)
            {
                Plugin.Instance.Logger.LogError("Code was not copied from FlowermanAI.AvoidPlayerTarget().");
                return null;
            }

            var instructionsList = instructions.ToList();

            var vanillaInstructions = PatchFlowermanAI.AvoidClosestPlayerInstructions.ToArray();
            if (!vanillaInstructions[0].IsLdloc())
            {
                Plugin.Instance.Logger.LogError("Copied code from FlowermanAI.AvoidPlayerTarget() does not begin with an ldloc.");
                return null;
            }
            var vanillaNodeVar = (LocalBuilder)vanillaInstructions[0].operand;

            for (var i = 0; i < vanillaInstructions.Length; i++)
            {
                var instruction = vanillaInstructions[i];

                if (instruction.IsLdloc(vanillaNodeVar))
                    vanillaInstructions[i] = new CodeInstruction(OpCodes.Ldarg_1).WithLabels(instruction.labels);
            }

            vanillaInstructions.TransferLabelsAndVariables(generator);

            var returnInstruction = instructionsList.FindIndex(insn => insn.opcode == OpCodes.Ret);
            instructionsList.RemoveRange(0, returnInstruction);
            instructionsList.InsertRange(0, vanillaInstructions);

            return instructionsList;
        }

        [HarmonyPatch(typeof(PatchFlowermanAI))]
        [HarmonyPatch(nameof(PatchFlowermanAI.FinishChoosingFarthestNodeFromEntrance))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> PatchFlowermanAI_FinishChoosingFarthestNodeFromEntranceTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            if (PatchFlowermanAI.NoPlayerToTargetNodeVar == -1)
            {
                Plugin.Instance.Logger.LogError("Target node variable was not found in DoAIInterval().");
                return null;
            }
            if (PatchFlowermanAI.NoPlayerToTargetInstructions == null)
            {
                Plugin.Instance.Logger.LogError("Code was not copied from DoAIInterval().");
                return null;
            }

            var instructionsList = instructions.ToList();

            var vanillaInstructions = PatchFlowermanAI.NoPlayerToTargetInstructions;
            var nodeVar = PatchFlowermanAI.NoPlayerToTargetNodeVar;
            PatchFlowermanAI.NoPlayerToTargetInstructions = null;
            PatchFlowermanAI.NoPlayerToTargetNodeVar = -1;

            for (var i = 0; i < vanillaInstructions.Count(); i++)
            {
                var instruction = vanillaInstructions[i];

                if (instruction.IsLdloc() && instruction.GetLocalIndex() == nodeVar)
                    vanillaInstructions[i] = new CodeInstruction(OpCodes.Ldarg_1).WithLabels(instruction.labels);
            }

            vanillaInstructions.TransferLabelsAndVariables(generator);

            var returnInstruction = instructionsList.FindIndex(insn => insn.opcode == OpCodes.Ret);
            instructionsList.RemoveRange(0, returnInstruction);
            instructionsList.InsertRange(0, vanillaInstructions);

            return instructionsList;
        }
    }
}
