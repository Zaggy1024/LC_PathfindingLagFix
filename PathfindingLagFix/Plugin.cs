using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

using PathfindingLagFix.Patches;
using PathfindingLagFix.Utilities;

namespace PathfindingLagFix
{
    [BepInPlugin(MOD_UNIQUE_NAME, MOD_NAME, MOD_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private const string MOD_NAME = "PathfindingLagFix";
        private const string MOD_UNIQUE_NAME = "Zaggy1024." + MOD_NAME;
        private const string MOD_VERSION = "2.0.4";

        private readonly Harmony harmony = new Harmony(MOD_UNIQUE_NAME);

        public static Plugin Instance { get; private set; }
        public new ManualLogSource Logger => base.Logger;

        public void Awake()
        {
            Instance = this;

            if (!NavMeshLock.Initialize(harmony))
            {
                Logger.LogInfo($"Failed to initialize navmesh concurrency safeties, disabling all patches.");
                return;
            }

            harmony.PatchAll(typeof(PatchEnemyAI));
            harmony.PatchAll(typeof(PatchFlowermanAI));
            harmony.PatchAll(typeof(PatchCentipedeAI));
            harmony.PatchAll(typeof(PatchPufferAI));
            harmony.PatchAll(typeof(PatchDoublewingAI));
        }
    }
}
