using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

using HarmonyLib;

namespace PathfindingLagFix.Patches
{
    internal class PatchCopyVanillaCode
    {
        [HarmonyPatch(typeof(PatchFlowermanAI))]
        [HarmonyPatch(nameof(PatchFlowermanAI.FinishChoosingPlayerEvasionLocation))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> FinishChoosingPlayerEvasionLocationTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            if (PatchFlowermanAI.AvoidClosestPlayerInstructions == null)
            {
                Plugin.Instance.Logger.LogError("Code was not copied from AvoidPlayerTarget().");
                return null;
            }

            var instructionsList = instructions.ToList();

            var vanillaInstructions = PatchFlowermanAI.AvoidClosestPlayerInstructions.ToArray();
            if (!vanillaInstructions[0].IsLdloc())
            {
                Plugin.Instance.Logger.LogError("Copied code from AvoidPlayerTarget() does not begin with an ldloc.");
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
        static IEnumerable<CodeInstruction> DoFinishChoosingFarthestNodeFromEntranceTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
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

        static IEnumerable<CodeInstruction> FinishChoosingAvoidPlayerTargetTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            Plugin.Instance.Logger.LogWarning("Transpiling FinishChoosingFarthestNodeFromEntrance");
            foreach (var instruction in DoFinishChoosingFarthestNodeFromEntranceTranspiler(instructions, generator))
            {
                Plugin.Instance.Logger.LogInfo(instruction);
                yield return instruction;
            }
        }
    }
}
