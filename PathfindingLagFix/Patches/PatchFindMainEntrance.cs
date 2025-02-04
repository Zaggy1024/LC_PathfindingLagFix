using System.Collections.Generic;

using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

using Object = UnityEngine.Object;

namespace PathfindingLagFix.Patches;

internal static class PatchFindMainEntrance
{
    private static bool usePatch = true;

    private readonly static List<EntranceTeleport> allTeleports = [];

    internal static void ApplyPatches(Harmony harmony)
    {
        harmony.PatchAll(typeof(PatchFindMainEntrance));

        SceneManager.sceneUnloaded += (_) => GetAllTeleports();
    }

    internal static void GetAllTeleports()
    {
        allTeleports.Clear();
        allTeleports.AddRange(Object.FindObjectsOfType<EntranceTeleport>(includeInactive: true));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntranceTeleport), nameof(EntranceTeleport.Awake))]
    private static void EntranceTeleportAwakePostfix(EntranceTeleport __instance)
    {
        allTeleports.Add(__instance);
    }

    private static EntranceTeleport GetMainEntrance(bool getOutsideEntrance)
    {
        foreach (var entrance in allTeleports)
        {
            if (entrance == null)
                continue;
            if (!entrance.isActiveAndEnabled)
                continue;
            if (entrance.entranceId != 0)
                continue;
            if (entrance.isEntranceToBuilding != getOutsideEntrance)
                continue;
            return entrance;
        }
        return null;
    }

    private static EntranceTeleport GetFirstNonNullActiveTeleport()
    {
        foreach (var entrance in allTeleports)
        {
            if (entrance == null)
                continue;
            if (!entrance.isActiveAndEnabled)
                continue;
            return entrance;
        }
        return null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.FindMainEntranceScript))]
    private static bool FindMainEntranceScriptPrefix(bool getOutsideEntrance, ref EntranceTeleport __result)
    {
        if (!usePatch)
            return true;

        __result = GetMainEntrance(getOutsideEntrance);
        __result ??= GetFirstNonNullActiveTeleport();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.FindMainEntrancePosition))]
    private static bool FindMainEntrancePositionPrefix(bool getTeleportPosition, bool getOutsideEntrance, ref Vector3 __result)
    {
        if (!usePatch)
            return true;

        var teleport = GetMainEntrance(getOutsideEntrance);
        __result = getTeleportPosition ? teleport.entrancePoint.position : teleport.transform.position;
        return false;
    }
}
