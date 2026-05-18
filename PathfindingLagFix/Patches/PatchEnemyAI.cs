using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.AI;
using GameNetcodeStuff;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;
using PathfindingLib.Utilities;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(EnemyAI))]
internal static class PatchEnemyAI
{
    internal readonly static MethodInfo m_CheckIfPlayerPathsAreStaleAndStartJobs = typeof(PatchEnemyAI).GetMethod(nameof(CheckIfPlayerPathsAreStaleAndStartJobs), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI), typeof(bool), typeof(bool)]);
    internal readonly static MethodInfo m_IsAsyncPathToPlayerInvalid = typeof(PatchEnemyAI).GetMethod(nameof(IsAsyncPathToPlayerInvalid), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI), typeof(bool), typeof(bool), typeof(int)]);

    private static bool useAsyncRoaming = true;
    private static bool useAsyncPlayerPaths = true;

    // Returns whether to insert another iteration of the enumerator to wait for the jobs to complete.
    private static bool StopPreviousJobAndStartNewOne(EnemyAI enemy)
    {
        if (!useAsyncRoaming)
            return false;

        var status = AsyncRoamingPathfinding.GetStatus(enemy);
        if (!status.PathsFromEnemyJobHandle.IsCompleted || !status.PathsFromSearchStartJobHandle.IsCompleted)
        {
            status.PathsFromEnemyJob.Cancel();
            status.PathsFromSearchStartJob.Cancel();
            return true;
        }
        status.StartJobs(enemy);
        return false;
    }

    private static PathQueryStatus GetPathStatus(EnemyAI enemy, int index)
    {
        if (!useAsyncRoaming)
            return PathQueryStatus.Success;

        var status = AsyncRoamingPathfinding.GetStatus(enemy);
        if (!status.PathsFromEnemyJob.Statuses.IsCreated)
            StopPreviousJobAndStartNewOne(enemy);

        var pathIndex = status.GetJobIndex(index);
        var pathStatus = status.PathsFromEnemyJob.Statuses[pathIndex].GetResult();
        if (!enemy.currentSearch.startedSearchAtSelf && status.PathsFromSearchStartJob.Statuses[pathIndex].GetResult() == PathQueryStatus.InProgress)
            pathStatus = PathQueryStatus.InProgress;
        return pathStatus;
    }

    private static void SetPathDistance(EnemyAI enemy, int index)
    {
        var status = AsyncRoamingPathfinding.GetStatus(enemy);
        var job = enemy.currentSearch.startedSearchAtSelf ? status.PathsFromEnemyJob : status.PathsFromSearchStartJob;

        if (job.PathDistances.IsCreated)
            enemy.pathDistance = job.PathDistances[status.GetJobIndex(index)];
        else
            enemy.pathDistance = 0;
    }

    private static void CancelJobs(EnemyAI enemy)
    {
        var status = AsyncRoamingPathfinding.GetStatus(enemy);
        status.PathsFromEnemyJob.Cancel();
        status.PathsFromSearchStartJob.Cancel();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(EnemyAI.ChooseNextNodeInSearchRoutine), MethodType.Enumerator)]
    private static IEnumerable<CodeInstruction> ChooseNextNodeInSearchRoutineTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        var useAsyncRoamingField = typeof(PatchEnemyAI).GetField(nameof(useAsyncRoaming), BindingFlags.NonPublic | BindingFlags.Static);

        FieldInfo state = null;
        FieldInfo current = null;
        FieldInfo index = null;

        foreach (var field in method.DeclaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (state == null && field.Name.EndsWith("state"))
                state = field;
            else if (current == null && field.Name.EndsWith("current"))
                current = field;
            else if (index == null && field.Name.StartsWith("<i>"))
                index = field;
        }

        if (state == null)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the 'state' field in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }
        if (current == null)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the 'current' field in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }
        if (index == null)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the 'i' field in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        var injector = new ILInjector(instructions, generator)
            .Find(ILMatcher.Opcode(OpCodes.Switch));

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the switch in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        //   if (!currentSearch.calculatingNodeInSearch) {
        //     yield return null;
        //     continue;
        //   }
        // + while (StopPreviousJobAndStartNewOne(this))
        // +   yield return null;
        //   yield return null;
        injector
            .Find([
                ILMatcher.Ldloc(1),
                ILMatcher.Ldfld(Reflection.f_EnemyAI_currentSearch),
                ILMatcher.Ldfld(typeof(AISearchRoutine).GetField(nameof(AISearchRoutine.calculatingNodeInSearch))),
                ILMatcher.Opcodes(OpCodes.Brtrue).CaptureOperandAs(out Label beginSearchLabel),
            ])
            .FindLabel(beginSearchLabel);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the start of node path checks in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        injector
            .DefineLabel(out var skipYieldReturnNullOnJobNotStartedLabel)
            .InsertAfterBranch([
                new(OpCodes.Ldloc_1),
                new(OpCodes.Call, typeof(PatchEnemyAI).GetMethod(nameof(StopPreviousJobAndStartNewOne), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI)])),
                new(OpCodes.Brfalse_S, skipYieldReturnNullOnJobNotStartedLabel),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Stfld, state),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldnull),
                new(OpCodes.Stfld, current),
                new(OpCodes.Ldc_I4_1),
                new(OpCodes.Ret),
            ])
            .AddLabel(skipYieldReturnNullOnJobNotStartedLabel);

        // + if (useAsync) {
        // +   PathQueryStatus pathState;
        // +   while ((pathState = GetPathStatus(this, i)) == PathQueryStatus.InProgress)
        // +     yield return null;
        // +   if (pathState == PathQueryStatus.Failure) {
        // +     EliminateNodeFromSearch(i);
        // +     continue;
        // +   }
        // + } else
        //   if (PathIsIntersectedByLineOfSight(currentSearch.unsearchedNodes[i].transform.position, currentSearch.startedSearchAtSelf, avoidLineOfSight: false)) {
        //     EliminateNodeFromSearch(i);
        //     continue;
        //   }
        injector
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_EliminateNodeFromSearch),
                ILMatcher.Opcode(OpCodes.Br).CaptureOperandAs(out Label continueLabel),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the continue jump in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        injector
            .GoToStart()
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_PathIsIntersectedByLineOfSight),
                ILMatcher.Opcode(OpCodes.Brfalse).CaptureOperandAs(out Label skipVanillaPathCheckLabel),
            ])
            .GoToPush(5);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the check for a path to a node in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        var pathStateLocal = generator.DeclareLocal(typeof(PathQueryStatus));
        injector
            .DefineLabel(out var skipAsyncPathCheckLabel)
            .DefineLabel(out var pathReadyLabel)
            .InsertAfterBranch([
                // if (PatchEnemyAI.useAsyncRoaming) {
                new CodeInstruction(OpCodes.Ldsfld, useAsyncRoamingField),
                new CodeInstruction(OpCodes.Brfalse_S, skipAsyncPathCheckLabel),

                // while ((pathState = GetPathStatus(this, i)) == PathQueryStatus.InProgress) {
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, index),
                new CodeInstruction(OpCodes.Call, typeof(PatchEnemyAI).GetMethod(nameof(GetPathStatus), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI), typeof(int)])),
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Stloc, pathStateLocal),
                new CodeInstruction(OpCodes.Ldc_I4, (int)PathQueryStatus.InProgress),
                new CodeInstruction(OpCodes.Bne_Un_S, pathReadyLabel),

                // yield return null;
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldc_I4_2),
                new CodeInstruction(OpCodes.Stfld, state),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Stfld, current),
                new CodeInstruction(OpCodes.Ldc_I4_1),
                new CodeInstruction(OpCodes.Ret),

                // }

                // if (pathState == PathQueryStatus.Failure) {
                new CodeInstruction(OpCodes.Ldloc, pathStateLocal).WithLabels(pathReadyLabel),
                new CodeInstruction(OpCodes.Ldc_I4, (int)PathQueryStatus.Failure),
                new CodeInstruction(OpCodes.Bne_Un_S, skipVanillaPathCheckLabel),

                // EliminateNodeFromSearch(i);
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, index),
                new CodeInstruction(OpCodes.Call, Reflection.m_EnemyAI_EliminateNodeFromSearch),

                // continue;
                new CodeInstruction(OpCodes.Br, continueLabel),

                // }

                // } else
            ])
            .AddLabel(skipAsyncPathCheckLabel);

        // + if (PatchEnemyAI.useAsyncRoaming) {
        // +   PatchEnemyAI.SetPathDistance(this, i);
        // + } else
        //   if (!currentSearch.startedSearchAtSelf) {
        //     GetPathDistance(currentSearch.unsearchedNodes[i].transform.position, currentSearch.currentSearchStartPosition);
        //   }
        injector
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_GetPathDistance),
                ILMatcher.Opcode(OpCodes.Pop),
            ])
            .ReverseFind([
                ILMatcher.Ldloc(1),
                ILMatcher.Ldfld(Reflection.f_EnemyAI_currentSearch),
                ILMatcher.Ldfld(typeof(AISearchRoutine).GetField(nameof(AISearchRoutine.startedSearchAtSelf))),
                ILMatcher.Opcode(OpCodes.Brtrue),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to get the path distance in {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        var skipSyncPathDistanceLabel = (Label)injector.LastMatchedInstruction.operand;
        var doSyncPathDistanceLabel = generator.DefineLabel();
        injector
            .InsertAfterBranch([
                new(OpCodes.Ldsfld, useAsyncRoamingField),
                new(OpCodes.Brfalse_S, doSyncPathDistanceLabel),
                new(OpCodes.Ldloc_1),
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldfld, index),
                new(OpCodes.Call, typeof(PatchEnemyAI).GetMethod(nameof(SetPathDistance), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI), typeof(int)])),
                new(OpCodes.Br, skipSyncPathDistanceLabel),
            ])
            .AddLabel(doSyncPathDistanceLabel);

        return injector
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(index),
                ILMatcher.Ldc(0),
                ILMatcher.Opcodes(OpCodes.Bge),
            ])
            .GoToMatchEnd()
            .InsertAfterBranch([
                new(OpCodes.Ldloc_1),
                new(OpCodes.Call, typeof(PatchEnemyAI).GetMethod(nameof(CancelJobs), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI)])),
            ])
            .ReleaseInstructions();
    }

    private static AsyncPlayerPathfinding.PathOptions GetPathOptions(bool doGroundCast, bool requirePath)
    {
        var result = AsyncPlayerPathfinding.PathOptions.None;
        if (doGroundCast)
            result |= AsyncPlayerPathfinding.PathOptions.GroundCast;
        if (requirePath)
            result |= AsyncPlayerPathfinding.PathOptions.RequirePath;
        return result;
    }

    private static bool CheckIfPlayerPathsAreStaleAndStartJobs(EnemyAI enemy, bool doGroundCast, bool requirePath)
    {
        if (!useAsyncPlayerPaths)
            return false;
        if (!doGroundCast && !requirePath)
            return true;
        var status = AsyncPlayerPathfinding.GetStatus(enemy, GetPathOptions(doGroundCast, requirePath));
        var pathsTime = status.UpdatePathsAndGetCalculationTime(enemy);

        var age = Time.time - pathsTime;
        if (age > enemy.AIIntervalTime * 2)
        {
            if (age < float.PositiveInfinity)
                Plugin.Instance.Logger.LogDebug($"Player paths from {enemy.name} ({enemy.GetType().Name}, {enemy.GetInstanceID()}) are {age * 1000}ms old, which is more than twice {enemy.AIIntervalTime * 1000}ms, using synchronous paths.");
            return false;
        }

        return true;
    }

    private static bool IsAsyncPathToPlayerInvalid(EnemyAI enemy, bool doGroundCast, bool requirePath, int index)
    {
        if (!doGroundCast && !requirePath)
            return false;

        return !AsyncPlayerPathfinding.GetStatus(enemy, GetPathOptions(doGroundCast, requirePath)).CanReachPlayer(index);
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(EnemyAI.TargetClosestPlayer))]
    private static IEnumerable<CodeInstruction> TargetClosestPlayerTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
    {
        var doGroundCastParameterName = "doGroundCast";
        var doGroundCastArg = -1;
        var requirePathParameterName = "requirePath";
        var requirePathArg = -1;

        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            var name = parameters[i].Name;
            if (name == doGroundCastParameterName)
                doGroundCastArg = i + 1;
            else if (name == requirePathParameterName)
                requirePathArg = i + 1;
        }

        if (doGroundCastArg == 0)
        {
            Plugin.Instance.Logger.LogError($"{nameof(EnemyAI)}.{method.Name}() has no parameter named {doGroundCastParameterName}.");
            return instructions;
        }

        if (requirePathArg == 0)
        {
            Plugin.Instance.Logger.LogError($"{nameof(EnemyAI)}.{method.Name}() has no parameter named {requirePathParameterName}.");
            return instructions;
        }

        var loadParameters = new CodeInstruction[] {
            new(OpCodes.Ldarg, doGroundCastArg),
            new(OpCodes.Ldarg, requirePathArg),
        };

        // + var canUseAsync = PatchEnemyAI.CheckIfPlayerPathsAreStaleAndStartJobs(this);
        //   
        //   for (var i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++) {
        //     if (!PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]))
        //       continue;
        // +   if (canUseAsync) {
        // +     if (PatchEnemyAI.IsAsyncPathToPlayerInvalid(this, doGroundCast, requirePath, i))
        // +       continue;
        // +   } else
        //     if (doGroundCast) {
        //       if (!Physics.Raycast(StartOfRound.Instance.allPlayerScripts[i].transform.position, Vector3.down, out raycastHit, 5f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
        //         continue;
        //       if (requirePath && PathIsIntersectedByLineOfSight(raycastHit.point, calculatePathDistance: false, avoidLineOfSight: false))
        //         continue;
        //     } else if (requirePath && PathIsIntersectedByLineOfSight(StartOfRound.Instance.allPlayerScripts[i].transform.position, calculatePathDistance: false, avoidLineOfSight: false)) {
        //       continue;
        //     }
        //     if (requireLineOfSight && !CheckLineOfSightForPosition(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, viewWidth, 40))
        //       continue;
        //     tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
        //     if (tempDist < mostOptimalDistance) {
        //       mostOptimalDistance = tempDist;
        //       targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
        //     }
        //   }
        var canUseAsyncLocal = generator.DeclareLocal(typeof(bool));
        var injector = new ILInjector(instructions)
            .Insert([
                new(OpCodes.Ldarg_0),
                ..loadParameters,
                new(OpCodes.Call, m_CheckIfPlayerPathsAreStaleAndStartJobs),
                new(OpCodes.Stloc, canUseAsyncLocal),
            ])
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Call(Reflection.m_StartOfRound_get_Instance),
                ILMatcher.Ldfld(Reflection.f_StartOfRound_allPlayerScripts),
                ILMatcher.Ldloc().CaptureAs(out var loadIndexInstruction),
                ILMatcher.Opcode(OpCodes.Ldelem_Ref),
                ILMatcher.Push(1),
                ILMatcher.Push(1),
                ILMatcher.Push(1),
                ILMatcher.Callvirt(typeof(EnemyAI).GetMethod(nameof(EnemyAI.PlayerIsTargetable), [typeof(PlayerControllerB), typeof(bool), typeof(bool), typeof(bool)])),
                ILMatcher.Opcode(OpCodes.Brfalse).CaptureOperandAs(out Label continueLabel),
            ])
            .Find([
                ILMatcher.Ldarg(requirePathArg),
                ILMatcher.Opcode(OpCodes.Brfalse).CaptureOperandAs(out Label pathIsValidLabel),
            ])
            .ReverseFind([
                ILMatcher.Ldarg(doGroundCastArg),
                ILMatcher.Opcode(OpCodes.Brfalse),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to check if the enemy can path to a player in {nameof(EnemyAI)}.{nameof(EnemyAI.TargetClosestPlayer)}().");
            return instructions;
        }

        var skipAsyncLabel = generator.DefineLabel();
        return injector
            .Insert([
                new(OpCodes.Ldloc, canUseAsyncLocal),
                new(OpCodes.Brfalse_S, skipAsyncLabel),
                new(OpCodes.Ldarg_0),
                ..loadParameters,
                loadIndexInstruction,
                new(OpCodes.Call, m_IsAsyncPathToPlayerInvalid),
                new(OpCodes.Brfalse_S, pathIsValidLabel),
                new(OpCodes.Br_S, continueLabel),
            ])
            .AddLabel(skipAsyncLabel)
            .ReleaseInstructions();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(EnemyAI.ChooseFarthestNodeFromPosition))]
    private static IEnumerable<CodeInstruction> ChooseFarthestNodeFromPositionTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method)
    {
        var capDistanceArg = Array.FindIndex(method.GetParameters(), parameter => parameter.Name == "capDistance") + 1;
        if (capDistanceArg > 0)
        {
            var capDistanceUsages = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.GetLdargIndex() == capDistanceArg)
                    capDistanceUsages++;
            }
            if (capDistanceUsages == 1)
                return instructions;
        }

        Plugin.Instance.Logger.LogError($"{nameof(EnemyAI)}.{method.Name}()'s capDistance argument usages have changed, patches need updating.");
        return instructions;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EnemyAI.OnDestroy))]
    private static void OnDestroyPostfix(EnemyAI __instance)
    {
        AsyncDistancePathfinding.RemoveStatus(__instance);
        AsyncPlayerPathfinding.RemoveStatus(__instance);
        AsyncRoamingPathfinding.RemoveStatus(__instance);
        PatchCaveDwellerAI.RemoveStatus(__instance);
        PatchBlobAI.RemoveStatus(__instance);
        PatchStingrayAI.RemoveStatus(__instance);
    }
}
