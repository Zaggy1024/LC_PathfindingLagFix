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
    [HarmonyPatch(typeof(CentipedeAI))]
    public class PatchCentipedeAI
    {
        static readonly FieldInfo f_CentipedeAI_mainEntrancePosition = AccessTools.Field(typeof(CentipedeAI), "mainEntrancePosition");
        static readonly FieldInfo f_CentipedeAI_choseHidingSpotNoPlayersNearby = AccessTools.Field(typeof(CentipedeAI), "choseHidingSpotNoPlayersNearby");

        public const string PATCH_NAME = "Snare Flea lag patch";

        public static void FinishChoosingFarthestNodeFromEntrance(CentipedeAI centipede, Transform node)
        {
            throw Common.StubError(nameof(FinishChoosingFarthestNodeFromEntrance), PATCH_NAME);
        }

        static IEnumerator ChooseFarthestNodeFromEntrance(CentipedeAI centipede, Vector3 position, bool avoidLineOfSight, int offset)
        {
            var farthestNodeCoroutine = Coroutines.ChooseFarthestNodeFromPosition(centipede, position, avoidLineOfSight, offset);
            Transform lastTransform = null;
            while (farthestNodeCoroutine.MoveNext())
            {
                if (farthestNodeCoroutine.Current != null)
                    lastTransform = farthestNodeCoroutine.Current;
                yield return null;
            }

            centipede.searchCoroutine = null;

            if (farthestNodeCoroutine.Current == null)
                yield break;
            FinishChoosingFarthestNodeFromEntrance(centipede, lastTransform);
        }

        public static int NoPlayerToTargetNodeVar = -1;
        public static List<CodeInstruction> NoPlayerToTargetInstructions = null;

        [HarmonyPatch(nameof(EnemyAI.DoAIInterval))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsList = instructions.ToList();

            // Find the branch where no player can be targeted.
            var targetPlayer = instructionsList.FindIndexOfSequence([
                insn => insn.Calls(Reflection.m_EnemyAI_TargetClosestPlayer),
                insn => insn.opcode == OpCodes.Brtrue || insn.opcode == OpCodes.Brtrue_S,
            ]);

            // Find the start and end of the branch where a hiding spot has not yet been chosen.
            // that branch contains a call to ChooseFarthestNodeFromPosition() that we want to patch.
            var checkHasChosenHidingSpot = instructionsList.FindIndexOfSequence([
                insn => insn.IsLdarg(0),
                insn => insn.LoadsField(f_CentipedeAI_choseHidingSpotNoPlayersNearby),
                insn => insn.opcode == OpCodes.Brtrue || insn.opcode == OpCodes.Brtrue_S,
            ]);
            var hasChosenHidingSpotLabel = (Label)instructionsList[checkHasChosenHidingSpot.End - 1].operand;
            var hasChosenHidingSpot = instructionsList.FindIndex(checkHasChosenHidingSpot.End, insn => insn.labels.Contains(hasChosenHidingSpotLabel)) - 1;

            var chooseFarthestNodeCall = instructionsList.FindIndex(checkHasChosenHidingSpot.End, insn => insn.Calls(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition));
            if (chooseFarthestNodeCall >= hasChosenHidingSpot)
                throw new Exception("Unexpected ordering of instructions in CentipedeAI.DoAIInterval().");
            var chooseFarthestNodeParameters = instructionsList.InstructionRangeForStackItems(chooseFarthestNodeCall, 1, 4);

            // Extract all instructions in the branch other than the direct call to ChooseFarthestNodeFromPosition().
            // We will use these to fill the stub that is called after a node has been chosen.
            // The call to ChooseFarthestNodeFromPosition() and all its parameters are replaced with a load of argument
            // 1, which corresponds to the node passed to the stub method.
            NoPlayerToTargetInstructions =
                instructionsList.IndexRangeView(checkHasChosenHidingSpot.End, chooseFarthestNodeParameters.Start)
                .Append(new CodeInstruction(OpCodes.Ldarg_1))
                .Concat(instructionsList.IndexRangeView(chooseFarthestNodeCall + 1, hasChosenHidingSpot))
                .ToList();

            // Now, grab all the instructions that are used to put the parameters for ChooseFarthestNodeFromPosition() on the stack, not including the `log`
            // parameter, which is unused. We will pass all these parameters to our own function instead.
            var chooseFarthestNodeParametersInstructions = instructionsList.IndexRangeView(chooseFarthestNodeParameters.Start, chooseFarthestNodeParameters.End).ToList();
            instructionsList.RemoveIndexRange(checkHasChosenHidingSpot.End, hasChosenHidingSpot);

            // if (searchCoroutine == null)
            //   searchCoroutine = StartCoroutine(ChooseFarthestNodeFromEntrance(...));
            var instructionsToInsert = new CodeInstruction[] {
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldfld, Reflection.f_EnemyAI_searchCoroutine),
                new(OpCodes.Brtrue_S, hasChosenHidingSpotLabel),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldarg_0),
            }.Concat(chooseFarthestNodeParametersInstructions)
            .Concat([
                CodeInstruction.Call(typeof(PatchCentipedeAI), nameof(ChooseFarthestNodeFromEntrance), [ typeof(CentipedeAI), typeof(Vector3), typeof(bool), typeof(int) ]),
                new(OpCodes.Call, Reflection.m_MonoBehaviour_StartCoroutine),
                new(OpCodes.Stfld, Reflection.f_EnemyAI_searchCoroutine),
            ]);

            instructionsList.InsertRange(checkHasChosenHidingSpot.End, instructionsToInsert);
            return instructionsList;
        }
    }

    [HarmonyPatch(typeof(PatchCentipedeAI))]
    internal class PatchCopyVanillaCentipedeCode
    {
        [HarmonyPatch(nameof(PatchCentipedeAI.FinishChoosingFarthestNodeFromEntrance))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> PatchCentipedeAI_FinishChoosingFarthestNodeFromEntranceTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var newInstructions = PatchCentipedeAI.NoPlayerToTargetInstructions;
            newInstructions.TransferLabelsAndVariables(generator);
            return newInstructions.Append(new CodeInstruction(OpCodes.Ret));
        }
    }
}
