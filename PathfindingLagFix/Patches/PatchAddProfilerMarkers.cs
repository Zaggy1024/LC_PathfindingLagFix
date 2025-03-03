using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;

using HarmonyLib;
using HarmonyLib.Public.Patching;
using Mono.Cecil;
using Unity.Profiling;

using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(EnemyAI))]
internal class PatchAddProfilerMarkers
{
    private static readonly Dictionary<MethodInfo, List<int>> callOffsets = [];
    private static readonly Dictionary<string, ProfilerMarker> markers = [];

    private static string currentMarkerName = "";

    internal static void ApplyPatches(Harmony harmony)
    {
        foreach (var type in typeof(EnemyAI).Assembly.GetTypes())
        {
            if (type != typeof(EnemyAI) && !type.IsSubclassOf(typeof(EnemyAI)))
                continue;

            foreach (var method in type.GetRuntimeMethods())
            {
                if (method.DeclaringType != type)
                    continue;

                if (!method.HasMethodBody())
                    continue;

                var instructions = method.GetMethodPatcher().CopyOriginal().Definition.Body.Instructions;
                var isMatch = false;
                foreach (var instruction in instructions)
                {
                    if (instruction.OpCode != Mono.Cecil.Cil.OpCodes.Call && instruction.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt)
                        continue;
                    if (instruction.Operand is not MethodReference calledMethod)
                        continue;
                    if (calledMethod.DeclaringType.Name != "EnemyAI")
                        continue;
                    switch (calledMethod.Name)
                    {
                        case "PathIsIntersectedByLineOfSight":
                        case "ChooseFarthestNodeFromPosition":
                        case "ChooseClosestNodeToPosition":
                            isMatch = true;
                            GetCallOffsets(method).Add(instruction.Offset);
                            break;
                    }
                }

                if (!isMatch)
                    continue;

                new PatchProcessor(harmony, method)
                    .AddTranspiler(
                        new HarmonyMethod(typeof(PatchAddProfilerMarkers).GetMethod(nameof(CallerTranspiler), BindingFlags.NonPublic | BindingFlags.Static, [typeof(IEnumerable<CodeInstruction>), typeof(ILGenerator), typeof(MethodBase)]))
                        {
                            priority = Priority.First,
                        })
                    .Patch();
            }
        }

        harmony.PatchAll(typeof(PatchAddProfilerMarkers));
    }

    private static List<int> GetCallOffsets(MethodInfo method)
    {
        if (callOffsets.TryGetValue(method, out var offsets))
            return offsets;
        offsets = [];
        callOffsets[method] = offsets;
        return offsets;
    }

    private static IEnumerable<CodeInstruction> CallerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase method)
    {
        if (method is not MethodInfo methodInfo)
            return instructions;

        Plugin.Instance.Logger.LogInfo($"Begin caller transpiler for {method.DeclaringType.FullName}::{method.Name}");

        var injector = new ILInjector(instructions);
        var callIndex = 0;

        while (true)
        {
            // + PatchAddProfilerMarkers.currentCallingMethod = $"{method.Name}:IL_{injector.Index:X}";
            //   PathIsIntersectedByLineOfSight(...)
            // or
            //   ChooseFarthestNodeFromPosition(...)
            // or
            //   ChooseClosestNodeToPosition(...)
            injector
                .Find([
                    ILMatcher.Predicate(insn => insn.Calls(Reflection.m_EnemyAI_PathIsIntersectedByLineOfSight) || insn.Calls(Reflection.m_EnemyAI_ChooseFarthestNodeFromPosition) || insn.Calls(Reflection.m_EnemyAI_ChooseClosestNodeToPosition)),
                ]);
            var index = injector.Index;

            if (!injector.IsValid)
                break;

            var calledMethod = (MethodBase)injector.Instruction.operand;
            injector
                .GoToPush(calledMethod.GetParameters().Length + 1)
                .InsertAfterBranch([
                    new(OpCodes.Ldstr, $"{method.Name}:IL_{GetCallOffsets(methodInfo)[callIndex]:X} -> {calledMethod.Name}"),
                    new(OpCodes.Stsfld, typeof(PatchAddProfilerMarkers).GetField(nameof(currentMarkerName), BindingFlags.NonPublic | BindingFlags.Static)),
                ]);
            injector.Index = index + 3;
            callIndex++;
        }
        
        return injector
            .ReleaseInstructions();
    }

    private static ProfilerMarker GetMarker(string name)
    {
        if (markers.TryGetValue(name, out ProfilerMarker marker))
            return marker;

        Plugin.Instance.Logger.LogInfo($"Adding a marker named '{name}', up to {markers.Count + 1} total");
        marker = new ProfilerMarker(name);
        markers[name] = marker;
        return marker;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(EnemyAI.PathIsIntersectedByLineOfSight))]
    [HarmonyPatch(nameof(EnemyAI.ChooseFarthestNodeFromPosition))]
    [HarmonyPatch(nameof(EnemyAI.ChooseClosestNodeToPosition))]
    private static IEnumerable<CodeInstruction> CalleeTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase method)
    {
        var markerLocal = generator.DeclareLocal(typeof(ProfilerMarker));
        var injector = new ILInjector(instructions)
            .Insert([
                new(OpCodes.Ldsfld, typeof(PatchAddProfilerMarkers).GetField(nameof(currentMarkerName), BindingFlags.NonPublic | BindingFlags.Static)),
                new(OpCodes.Call, typeof(PatchAddProfilerMarkers).GetMethod(nameof(GetMarker), BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)])),
                new(OpCodes.Stloc, markerLocal),
                new(OpCodes.Ldloca, markerLocal),
                new(OpCodes.Call, typeof(ProfilerMarker).GetMethod(nameof(ProfilerMarker.Begin), [])),
            ]);

        while (true)
        {
            injector
                .Find([
                    ILMatcher.Opcode(OpCodes.Ret),
                ]);

            if (!injector.IsValid)
                break;

            injector
                .Insert([
                    new(OpCodes.Ldloca, markerLocal),
                    new(OpCodes.Call, typeof(ProfilerMarker).GetMethod(nameof(ProfilerMarker.End))),
                ])
                .GoToMatchEnd();
        }

        return injector
            .ReleaseInstructions();
    }
}
