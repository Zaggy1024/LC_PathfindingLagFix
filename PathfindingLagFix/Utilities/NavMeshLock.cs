using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

using HarmonyLib;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

using PathfindingLagFix.Patches;
using PathfindingLagFix.Utilities.IL;

namespace PathfindingLagFix.Utilities;

internal static class NavMeshLock
{
    internal static ReaderWriterLockSlim BlockingLock = new(LockRecursionPolicy.SupportsRecursion);
    internal static ManualResetEvent JobRunCondition = new(true);

    private class LockJobs { }

    private class AIUpdateGroup { }

    private class BeforeAIUpdate { }

    private class AfterAIUpdate { }

    internal static bool Initialize(Harmony harmony)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();

        if (!SearchAndInjectAIUpdatePrefixAndPostfix(ref loop))
            return false;

        var lockJobs = new PlayerLoopSystem()
        {
            type = typeof(LockJobs),
            updateDelegate = PauseJobsImpl,
        };
        loop.subSystemList = [lockJobs, .. loop.subSystemList];
        PlayerLoop.SetPlayerLoop(loop);

        harmony.PatchAll(typeof(NavMeshLock));

        return true;
    }

    private static bool SearchAndInjectAIUpdatePrefixAndPostfix(ref PlayerLoopSystem currentSubSystem)
    {
        if (currentSubSystem.subSystemList == null)
            return false;

        var index = -1;
        for (var i = 0; i < currentSubSystem.subSystemList.Length; i++)
        {
            if (currentSubSystem.subSystemList[i].type == typeof(PreUpdate.AIUpdate))
            {
                index = i;
                break;
            }
        }

        if (index == -1)
        {
            for (var i = 0; i < currentSubSystem.subSystemList.Length; i++)
            {
                if (SearchAndInjectAIUpdatePrefixAndPostfix(ref currentSubSystem.subSystemList[i]))
                    return true;
            }

            return false;
        }

        ref var subSystems = ref currentSubSystem.subSystemList;

        var prefixSubSystem = new PlayerLoopSystem()
        {
            type = typeof(BeforeAIUpdate),
            updateDelegate = BeforeAIUpdateImpl,
        };
        var postfixSubSystem = new PlayerLoopSystem()
        {
            type = typeof(AfterAIUpdate),
            updateDelegate = AfterAIUpdateImpl,
        };
        var nestedSystem = new PlayerLoopSystem()
        {
            type = typeof(AIUpdateGroup),
            subSystemList = [prefixSubSystem, subSystems[index], postfixSubSystem],
        };

        subSystems[index] = nestedSystem;
        return true;
    }

    private static void PauseJobsImpl()
    {
        JobRunCondition.Reset();
    }

    private static void BeforeAIUpdateImpl()
    {
        BeginNavMeshWrite();
    }

    private static void AfterAIUpdateImpl()
    {
        EndNavMeshWrite();
        JobRunCondition.Set();
    }

    public static void BeginNavMeshWrite()
    {
        BlockingLock.EnterWriteLock();
    }

    public static void EndNavMeshWrite()
    {
        BlockingLock.ExitWriteLock();
    }

    public static void BeginNavMeshRead()
    {
        JobRunCondition.WaitOne();
        BlockingLock.EnterReadLock();
    }

    public static void EndNavMeshRead()
    {
        BlockingLock.ExitReadLock();
    }

    private static readonly MethodInfo m_BeginNavMeshWrite = typeof(NavMeshLock).GetMethod(nameof(BeginNavMeshWrite), BindingFlags.Public | BindingFlags.Static);
    private static readonly MethodInfo m_EndNavMeshWrite = typeof(NavMeshLock).GetMethod(nameof(EndNavMeshWrite), BindingFlags.Public | BindingFlags.Static);

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NavMeshSurface), nameof(NavMeshSurface.BuildNavMesh))]
    [HarmonyPatch(typeof(NavMeshSurface), nameof(NavMeshSurface.UpdateActive))]
    [HarmonyPatch(typeof(NavMeshSurface), nameof(NavMeshSurface.AddData))]
    [HarmonyPatch(typeof(NavMeshSurface), nameof(NavMeshSurface.RemoveData))]
    private static IEnumerable<CodeInstruction> LockWriteForDurationOfMethodTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return new ILInjector(instructions)
            .Insert([new(OpCodes.Call, m_BeginNavMeshWrite)])
            .GoToEnd()
            .ReverseFind([
                ILMatcher.Opcode(OpCodes.Ret)
            ])
            .Insert([new(OpCodes.Call, m_EndNavMeshWrite)])
            .ReleaseInstructions();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NavMeshSurface), nameof(NavMeshSurface.UpdateDataIfTransformChanged))]
    private static IEnumerable<CodeInstruction> UpdateDataIfTransformChangedTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var injector = new ILInjector(instructions)
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Call(typeof(NavMeshSurface).GetMethod(nameof(NavMeshSurface.RemoveData))),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to {nameof(NavMeshSurface.RemoveData)} in {nameof(NavMeshSurface)}.{nameof(NavMeshSurface.UpdateDataIfTransformChanged)}.");
            return instructions;
        }

        injector
            .Insert([
                new(OpCodes.Call, m_BeginNavMeshWrite),
            ])
            .Find([
                ILMatcher.Ldarg(0),
                ILMatcher.Call(typeof(NavMeshSurface).GetMethod(nameof(NavMeshSurface.AddData))),
            ]);
        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the call to {nameof(NavMeshSurface.AddData)} in {nameof(NavMeshSurface)}.{nameof(NavMeshSurface.UpdateDataIfTransformChanged)}.");
            return instructions;
        }

        return injector
            .Insert([
                new(OpCodes.Call, m_EndNavMeshWrite),
            ])
            .ReleaseInstructions();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NavMeshSurface), nameof(NavMeshSurface.UpdateNavMesh))]
    private static IEnumerable<CodeInstruction> UpdateNavMeshTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        return new ILInjector(instructions)
            .Insert([
                new(OpCodes.Call, m_BeginNavMeshWrite),
            ])
            .Find(ILMatcher.Opcode(OpCodes.Ret))
            .Insert([
                new(OpCodes.Dup),
                new(OpCodes.Call, typeof(NavMeshLock).GetMethod(nameof(EndNavMeshWriteAtEndOfAsyncOperation), BindingFlags.NonPublic | BindingFlags.Static, [typeof(AsyncOperation)])),
            ])
            .ReleaseInstructions();
    }

    private static void EndNavMeshWriteAtEndOfAsyncOperation(AsyncOperation operation)
    {
        operation.completed += _ => EndNavMeshWrite();
    }
}
