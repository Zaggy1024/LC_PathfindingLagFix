using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

using PathfindingLagFix.Patches;

namespace PathfindingLagFix
{
    [BepInPlugin(MOD_UNIQUE_NAME, MOD_NAME, MOD_VERSION)]
    [BepInDependency("Zaggy1024.PathfindingLib", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string MOD_NAME = "PathfindingLagFix";
        public const string MOD_UNIQUE_NAME = "Zaggy1024." + MOD_NAME;
        public const string MOD_VERSION = "2.0.5";

        private readonly Harmony harmony = new(MOD_UNIQUE_NAME);

        public static Plugin Instance { get; private set; }
        public new ManualLogSource Logger => base.Logger;

        public void Awake()
        {
            Instance = this;

            harmony.PatchAll(typeof(PatchEnemyAI));
            harmony.PatchAll(typeof(PatchFlowermanAI));
            harmony.PatchAll(typeof(PatchCentipedeAI));
            harmony.PatchAll(typeof(PatchPufferAI));
            harmony.PatchAll(typeof(PatchDoublewingAI));
            harmony.PatchAll(typeof(PatchFlowerSnakeEnemy));
        }
    }
}
