using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

namespace PathfindingLagFix.Patches
{
    public class SequenceMatch
    {
        public int Start;
        public int End;

        public SequenceMatch(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    public static class Common
    {
        public static SequenceMatch FindIndexOfSequence<T>(this List<T> list, int startIndex, int count, IEnumerable<Predicate<T>> predicates)
        {
            var index = startIndex;
            while (index < list.Count())
            {
                var predicateEnumerator = predicates.GetEnumerator();
                if (!predicateEnumerator.MoveNext())
                    return null;
                index = list.FindIndex(index, predicateEnumerator.Current);

                if (index < 0)
                    break;

                bool matches = true;
                var sequenceIndex = 1;
                while (predicateEnumerator.MoveNext())
                {
                    if (sequenceIndex >= list.Count() - index
                        || !predicateEnumerator.Current(list[index + sequenceIndex]))
                    {
                        matches = false;
                        break;
                    }
                    sequenceIndex++;
                }

                if (matches)
                    return new SequenceMatch(index, index + predicates.Count());
                index++;
            }

            return null;
        }

        public static SequenceMatch FindIndexOfSequence<T>(this List<T> list, int startIndex, IEnumerable<Predicate<T>> predicates)
        {
            return FindIndexOfSequence(list, startIndex, -1, predicates);
        }

        public static SequenceMatch FindIndexOfSequence<T>(this List<T> list, IEnumerable<Predicate<T>> predicates)
        {
            return FindIndexOfSequence(list, 0, -1, predicates);
        }

        public static IEnumerable<T> IndexRangeView<T>(this List<T> list, int start, int end)
        {
            for (int i = start; i < end; i++)
                yield return list[i];
        }

        public static IEnumerable<T> IndexRangeView<T>(this List<T> list, SequenceMatch range)
        {
            return list.IndexRangeView(range.Start, range.End);
        }

        public static void RemoveIndexRange<T>(this List<T> list, int start, int end)
        {
            list.RemoveRange(start, end - start);
        }

        public static void RemoveIndicesRange<T>(this List<T> list, SequenceMatch range)
        {
            list.RemoveIndexRange(range.Start, range.End);
        }

        public static void TransferLabelsAndVariables(this IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var localDict = new Dictionary<LocalBuilder, LocalBuilder>();
            LocalBuilder GetLocal(LocalBuilder local)
            {
                if (!localDict.ContainsKey(local))
                    localDict[local] = generator.DeclareLocal(local.LocalType);
                return localDict[local];
            }

            var labelDict = new Dictionary<Label, Label>();
            Label GetLabel(Label label)
            {
                if (!labelDict.ContainsKey(label))
                    labelDict[label] = generator.DefineLabel();
                return labelDict[label];
            }

            foreach (var instruction in instructions)
            {
                if (instruction.operand is LocalBuilder local)
                    instruction.operand = GetLocal(local);

                for (var labelIndex = 0; labelIndex < instruction.labels.Count(); labelIndex++)
                    instruction.labels[labelIndex] = GetLabel(instruction.labels[labelIndex]);
                if (instruction.operand is Label label)
                    instruction.operand = GetLabel(label);
            }
        }

        public static int GetLocalIndex(this CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Stloc_0)
                return 0;
            if (instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Stloc_1)
                return 1;
            if (instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Stloc_2)
                return 2;
            if (instruction.opcode == OpCodes.Ldloc_3 || instruction.opcode == OpCodes.Stloc_3)
                return 3;

            if (instruction.opcode != OpCodes.Ldloc || instruction.opcode != OpCodes.Ldloc_S
                || instruction.opcode != OpCodes.Ldloca || instruction.opcode != OpCodes.Ldloca_S
                || instruction.opcode != OpCodes.Stloc || instruction.opcode != OpCodes.Stloc_S)
                return -1;

            return ((LocalBuilder)instruction.operand).LocalIndex;
        }

        public static int PopCount(this CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                return ((MethodInfo)instruction.operand).GetParameters().Length;

            switch (instruction.opcode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                    return 0;
                case StackBehaviour.Pop1:
                    return 1;
                case StackBehaviour.Pop1_pop1:
                    return 2;
                case StackBehaviour.Popi:
                    return 1;
                case StackBehaviour.Popi_pop1:
                    return 2;
                case StackBehaviour.Popi_popi:
                    return 2;
                case StackBehaviour.Popi_popi8:
                    return 2;
                case StackBehaviour.Popi_popi_popi:
                    return 3;
                case StackBehaviour.Popi_popr4:
                    return 2;
                case StackBehaviour.Popi_popr8:
                    return 2;
                case StackBehaviour.Popref:
                    return 1;
                case StackBehaviour.Popref_pop1:
                    return 2;
                case StackBehaviour.Popref_popi:
                    return 2;
                case StackBehaviour.Popref_popi_popi:
                    return 3;
                case StackBehaviour.Popref_popi_popi8:
                    return 3;
                case StackBehaviour.Popref_popi_popr4:
                    return 3;
                case StackBehaviour.Popref_popi_popr8:
                    return 3;
                case StackBehaviour.Popref_popi_popref:
                    return 3;
                case StackBehaviour.Varpop:
                    throw new NotImplementedException("Variable pop on non-call instruction");
                case StackBehaviour.Popref_popi_pop1:
                    return 3;
                default:
                    throw new NotSupportedException($"StackBehaviourPop of {instruction.opcode.StackBehaviourPop} was not a pop for instruction '{instruction}'");
            }
        }

        public static int PushCount(this CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                return 1;

            switch (instruction.opcode.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    return 1;
                case StackBehaviour.Push1:
                    return 1;
                case StackBehaviour.Push1_push1:
                    return 2;
                case StackBehaviour.Pushi:
                    return 1;
                case StackBehaviour.Pushi8:
                    return 1;
                case StackBehaviour.Pushr4:
                    return 1;
                case StackBehaviour.Pushr8:
                    return 1;
                case StackBehaviour.Pushref:
                    return 1;
                case StackBehaviour.Varpush:
                    throw new NotImplementedException("Variable push on non-call instruction");
                default:
                    throw new NotSupportedException($"StackBehaviourPush of {instruction.opcode.StackBehaviourPush} was not a push for instruction '{instruction}'");
            }
        }

        public static SequenceMatch InstructionRangeForStackItems(this List<CodeInstruction> instructions, int instructionIndex, int startIndex, int endIndex)
        {
            int start = -1;
            int end = -1;

            instructionIndex--;
            int stackPosition = 0;
            while (instructionIndex >= 0)
            {
                var instruction = instructions[instructionIndex];
                stackPosition += instruction.PushCount();
                stackPosition -= instruction.PopCount();

                if (stackPosition == startIndex && end == -1)
                {
                    end = instructionIndex;
                }
                else if (stackPosition == endIndex + 1)
                {
                    start = instructionIndex;
                    break;
                }

                instructionIndex--;
            }

            if (start == -1 || end == -1)
                return null;

            return new SequenceMatch(start, end);
        }

        public static Exception PatchError(string message, string patchName)
        {
            return new Exception($"{message}, {patchName} may not be supported on this game version");
        }

        public static Exception StubError(string name, string patchName)
        {
            return new NotSupportedException($"{name} stub was called, {patchName} may not be supported on this game version");
        }
    }
}
