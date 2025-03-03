using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

using PathfindingLagFix.Patches;
using PathfindingLib;

namespace PathfindingLagFix;

[BepInPlugin(MOD_UNIQUE_NAME, MOD_NAME, MOD_VERSION)]
[BepInDependency(PathfindingLibPlugin.PluginGUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string MOD_NAME = "PathfindingLagFix";
    public const string MOD_UNIQUE_NAME = "Zaggy1024." + MOD_NAME;
    public const string MOD_VERSION = "2.1.1";

    private readonly Harmony harmony = new(MOD_UNIQUE_NAME);

    public static Plugin Instance { get; private set; }
    public new ManualLogSource Logger => base.Logger;

    public void Awake()
    {
        Instance = this;

        ConfigOptions.BindAllOptions(Config);

#if BENCHMARKING
        PatchAddProfilerMarkers.ApplyPatches(harmony);
#endif

        harmony.PatchAll(typeof(PatchEnemyAI));
        harmony.PatchAll(typeof(PatchFlowermanAI));
        harmony.PatchAll(typeof(PatchCentipedeAI));
        harmony.PatchAll(typeof(PatchPufferAI));
        harmony.PatchAll(typeof(PatchDoublewingAI));
        harmony.PatchAll(typeof(PatchFlowerSnakeEnemy));
        harmony.PatchAll(typeof(PatchSpringManAI));
        harmony.PatchAll(typeof(PatchBlobAI));
        harmony.PatchAll(typeof(PatchCaveDwellerAI));

        PatchFindMainEntrance.ApplyPatches(harmony);

#if BENCHMARKING
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.None);
#endif
    }
}
