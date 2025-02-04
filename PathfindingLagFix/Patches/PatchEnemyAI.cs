using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.AI;

using PathfindingLagFix.Utilities;
using PathfindingLagFix.Utilities.IL;
using PathfindingLib.Utilities;

namespace PathfindingLagFix.Patches;

[HarmonyPatch(typeof(EnemyAI))]
internal static class PatchEnemyAI
{
    internal readonly static MethodInfo m_CheckIfPlayerPathsAreStaleAndStartJobs = typeof(PatchEnemyAI).GetMethod(nameof(CheckIfPlayerPathsAreStaleAndStartJobs), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI)]);
    internal readonly static MethodInfo m_IsAsyncPathToPlayerInvalid = typeof(PatchEnemyAI).GetMethod(nameof(IsAsyncPathToPlayerInvalid), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI), typeof(int)]);

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

        var injector = new ILInjector(instructions)
            .Find(ILMatcher.Opcode(OpCodes.Switch));

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the switch in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        var switchLabels = (Label[])injector.Instruction.operand;

        injector
            .FindLabel(switchLabels[0]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the switch case 0 in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        // + while (StopPreviousJobAndStartNewOne(this))
        // +   yield return null;
        //   yield return null;
        var skipYieldReturnNullOnJobNotStartedLabel = generator.DefineLabel();
        injector
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

        //   if (i % 5 == 0)
        //     yield return null;
        // + PathQueryStatus pathState;
        // + while ((pathState = GetPathStatus(this, i)) == PathQueryStatus.InProgress)
        // +   yield return null;
        // + if (pathState == PathQueryStatus.Failure) {
        // +   EliminateNodeFromSearch(i);
        // +   continue;
        // + }
        //   if (Vector3.Distance(currentSearch.currentSearchStartPosition, currentSearch.unsearchedNodes[i].transform.position) > currentSearch.searchWidth) {
        //     EliminateNodeFromSearch(i);
        //     continue;
        //   }
        injector
            .FindLabel(switchLabels[2])
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Ldc(-1),
                ILMatcher.Stfld(state),
            ])
            .GoToMatchEnd();

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the switch case 2 in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        var pathStateLocal = generator.DeclareLocal(typeof(PathQueryStatus));
        var skipYieldReturnNullOnPathInProgressLabel = generator.DefineLabel();
        var skipEliminateNodeFromAsyncLabel = generator.DefineLabel();
        var continueLabel = generator.DefineLabel();
        injector
            .InsertAfterBranch([
                // while ((pathState = GetPathStatus(this, i)) == PathQueryStatus.InProgress)
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, index),
                new CodeInstruction(OpCodes.Call, typeof(PatchEnemyAI).GetMethod(nameof(GetPathStatus), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI), typeof(int)])),
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Stloc, pathStateLocal),
                new CodeInstruction(OpCodes.Ldc_I4, (int)PathQueryStatus.InProgress),
                new CodeInstruction(OpCodes.Bne_Un_S, skipYieldReturnNullOnPathInProgressLabel),

                // yield return null;
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldc_I4_2),
                new CodeInstruction(OpCodes.Stfld, state),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Stfld, current),
                new CodeInstruction(OpCodes.Ldc_I4_1),
                new CodeInstruction(OpCodes.Ret),

                // if (pathState == PathQueryStatus.Failure)
                new CodeInstruction(OpCodes.Ldloc, pathStateLocal).WithLabels(skipYieldReturnNullOnPathInProgressLabel),
                new CodeInstruction(OpCodes.Ldc_I4, (int)PathQueryStatus.Failure),
                new CodeInstruction(OpCodes.Bne_Un_S, skipEliminateNodeFromAsyncLabel),

                // EliminateNodeFromSearch(i);
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, index),
                new CodeInstruction(OpCodes.Call, Reflection.m_EnemyAI_EliminateNodeFromSearch),

                // continue;
                new CodeInstruction(OpCodes.Br, continueLabel),
            ])
            .AddLabel(skipEliminateNodeFromAsyncLabel);

        injector
            .Find(ILMatcher.Call(Reflection.m_EnemyAI_PathIsIntersectedByLineOfSight))
            .Find([
                ILMatcher.Call(Reflection.m_EnemyAI_EliminateNodeFromSearch),
                ILMatcher.Opcode(OpCodes.Br),
            ]);

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find where AI nodes are eliminated for failing to find a path in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        // - else if (agent.isOnNavMesh && PathIsIntersectedByLineOfSight(currentSearch.unsearchedNodes[i].transform.position, currentSearch.startedSearchAtSelf, avoidLineOfSight: false))
        // + else if (!PatchEnemyAI.useAsyncRoaming && agent.isOnNavMesh && PathIsIntersectedByLineOfSight(currentSearch.unsearchedNodes[i].transform.position, currentSearch.startedSearchAtSelf, avoidLineOfSight: false))
        //     EliminateNodeFromSearch(i);
        var skipEliminateNodeFromSyncLabel = generator.DefineLabel();
        var existingContinueLabel = (Label)injector.LastMatchedInstruction.operand;
        injector
            .GoToMatchEnd()
            .AddLabel(skipEliminateNodeFromSyncLabel)
            .Back(2)
            .ReverseFind([
                ILMatcher.Call(Reflection.m_EnemyAI_EliminateNodeFromSearch),
                ILMatcher.Opcode(OpCodes.Br),
            ])
            .GoToMatchEnd();

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the instruction before a path is tested in the enumerator for {nameof(EnemyAI)}.{nameof(EnemyAI.ChooseNextNodeInSearchRoutine)}().");
            return instructions;
        }

        var useAsyncRoamingField = typeof(PatchEnemyAI).GetField(nameof(useAsyncRoaming), BindingFlags.NonPublic | BindingFlags.Static);

        injector
            .InsertAfterBranch([
                new(OpCodes.Ldsfld, useAsyncRoamingField),
                new(OpCodes.Brtrue_S, skipEliminateNodeFromSyncLabel),
            ]);

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

        // Add label to skip an iteration after eliminating a node.
        injector
            .FindLabel(existingContinueLabel)
            .AddLabel(continueLabel);

        return injector
            .GoToEnd()
            .ReverseFind(ILMatcher.Opcode(OpCodes.Ret))
            .Insert([
                new(OpCodes.Ldloc_1),
                new(OpCodes.Call, typeof(PatchEnemyAI).GetMethod(nameof(CancelJobs), BindingFlags.NonPublic | BindingFlags.Static, [typeof(EnemyAI)])),
            ])
            .ReleaseInstructions();
    }

    private static bool CheckIfPlayerPathsAreStaleAndStartJobs(EnemyAI enemy)
    {
        if (!useAsyncPlayerPaths)
            return false;

        var status = AsyncPlayerPathfinding.GetStatus(enemy);
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

    private static bool IsAsyncPathToPlayerInvalid(EnemyAI enemy, int index)
    {
        return !AsyncPlayerPathfinding.GetStatus(enemy).CanReachPlayer(index);
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(EnemyAI.TargetClosestPlayer))]
    private static IEnumerable<CodeInstruction> TargetClosestPlayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // + var canUseAsync = PatchEnemyAI.CheckIfPlayerPathsAreStaleAndStartJobs(this);
        //   
        //   for (var i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++) {
        //     if (!PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]))
        //       continue;
        // -   if (!PathIsIntersectedByLineOfSight(StartOfRound.Instance.allPlayerScripts[i].transform.position, calculatePathDistance: false, avoidLineOfSight: false))
        // +   if (canUseAsync ? PatchEnemyAI.IsAsyncPathToPlayerInvalid(this, i) : !PathIsIntersectedByLineOfSight(StartOfRound.Instance.allPlayerScripts[i].transform.position, calculatePathDistance: false, avoidLineOfSight: false))
        //       continue;
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
                new(OpCodes.Call, m_CheckIfPlayerPathsAreStaleAndStartJobs),
                new(OpCodes.Stloc, canUseAsyncLocal),
            ])
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
            Plugin.Instance.Logger.LogError($"Failed to find the call to check if the enemy can path to a player in {nameof(EnemyAI)}.{nameof(EnemyAI.TargetClosestPlayer)}().");
            return instructions;
        }

        var loadIndexInstruction = injector.GetRelativeInstruction(3);
        var skipAsyncLabel = generator.DefineLabel();
        var skipSyncLabel = generator.DefineLabel();
        return injector
            .Insert([
                new(OpCodes.Ldloc, canUseAsyncLocal),
                new(OpCodes.Brfalse_S, skipAsyncLabel),
                new(OpCodes.Ldarg_0),
                loadIndexInstruction,
                new(OpCodes.Call, m_IsAsyncPathToPlayerInvalid),
                new(OpCodes.Br_S, skipSyncLabel),
            ])
            .AddLabel(skipAsyncLabel)
            .GoToMatchEnd()
            .AddLabel(skipSyncLabel)
            .ReleaseInstructions();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EnemyAI.OnDestroy))]
    private static void OnDestroyPostfix(EnemyAI __instance)
    {
        AsyncDistancePathfinding.RemoveStatus(__instance);
        AsyncPlayerPathfinding.RemoveStatus(__instance);
        AsyncRoamingPathfinding.RemoveStatus(__instance);
    }
}
