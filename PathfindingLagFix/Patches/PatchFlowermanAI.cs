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

        public const string PATCH_NAME = "Bracken lag patch";

        public static void FinishChoosingFarthestNodeFromEntrance(FlowermanAI flowerman, Transform node)
        {
            throw Common.StubError(nameof(FinishChoosingFarthestNodeFromEntrance), PATCH_NAME);
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
            var targetPlayer = instructionsList.FindIndexOfSequence([
                insn => insn.Calls(Reflection.m_EnemyAI_TargetClosestPlayer),
                insn => insn.Branches(out noPlayerTargetLabel),
            ]);

            var noPlayerTarget = instructionsList.FindIndex(insn => insn.labels.Contains(noPlayerTargetLabel.Value));

            var afterNoPlayerTargetLabel = (Label)instructionsList[noPlayerTarget - 1].operand;
            var afterNoPlayerTarget = instructionsList.FindIndex(noPlayerTarget, insn => insn.labels.Contains(afterNoPlayerTargetLabel));

            var chooseFarTarget = instructionsList.FindIndexOfSequence(noPlayerTarget, [
                // Transform transform = ChooseFarthestNodeFromPosition(mainEntrancePosition);
                insn => insn.IsLdarg(0),
                insn => insn.IsLdarg(0),
                insn => insn.LoadsField(f_FlowermanAI_mainEntrancePosition),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(0),
                insn => insn.LoadsConstant(50),
                insn => insn.LoadsConstant(0),
                insn => insn.Calls(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition),
                insn => insn.IsStloc(),
            ]);

            NoPlayerToTargetNodeVar = instructionsList[chooseFarTarget.End - 1].GetLocalIndex();
            NoPlayerToTargetInstructions = instructionsList.IndexRangeView(chooseFarTarget.End, afterNoPlayerTarget).ToList();

            var chooseFarTargetLabels = instructionsList[chooseFarTarget.Start].labels.ToArray();
            instructionsList.RemoveIndexRange(chooseFarTarget.Start, afterNoPlayerTarget);

            var skipSearchCoroutineLabel = generator.DefineLabel();
            instructionsList[chooseFarTarget.Start].labels.Add(skipSearchCoroutineLabel);

            instructionsList.InsertRange(chooseFarTarget.Start, [
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
                CodeInstruction.Call(typeof(PatchFlowermanAI), nameof(ChooseFarthestNodeFromEntrance), [ typeof(FlowermanAI), typeof(Vector3) ]),
                new CodeInstruction(OpCodes.Callvirt, Reflection.m_MonoBehaviour_StartCoroutine),
                new CodeInstruction(OpCodes.Stfld, Reflection.f_EnemyAI_searchCoroutine),
            ]);

            return instructionsList;
        }
    }

    [HarmonyPatch(typeof(PatchFlowermanAI))]
    internal class PatchCopyVanillaFlowermanCode
    {
        [HarmonyPatch(nameof(PatchFlowermanAI.FinishChoosingFarthestNodeFromEntrance))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> PatchFlowermanAI_FinishChoosingFarthestNodeFromEntranceTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            if (PatchFlowermanAI.NoPlayerToTargetNodeVar == -1)
                throw Common.PatchError("Target node variable was not found in DoAIInterval()", PatchFlowermanAI.PATCH_NAME);
            if (PatchFlowermanAI.NoPlayerToTargetInstructions == null)
                throw Common.PatchError("Code was not copied from DoAIInterval()", PatchFlowermanAI.PATCH_NAME);

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

            return vanillaInstructions.Append(new CodeInstruction(OpCodes.Ret));
        }
    }
}
