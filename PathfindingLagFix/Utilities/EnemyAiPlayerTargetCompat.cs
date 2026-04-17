using System;
using System.Reflection;
using GameNetcodeStuff;
using UnityEngine;

namespace PathfindingLagFix.Utilities;

/// <summary>
/// Game v81+ may ship without <c>EnemyAI.PlayerIsTargetable</c> (or with a different signature), which causes
/// <see cref="MissingMethodException"/> when older builds call it directly. This resolves the method at runtime
/// when present; otherwise uses a minimal filter so async pathfinding can still run.
/// </summary>
internal static class EnemyAiPlayerTargetCompat
{
    private const string PlayerIsTargetableName = "PlayerIsTargetable";

    private static readonly Func<EnemyAI, PlayerControllerB, bool> Invoke = CreateInvoker();

    private static bool _loggedFallback;

    private static Func<EnemyAI, PlayerControllerB, bool> CreateInvoker()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in typeof(EnemyAI).GetMethods(flags))
        {
            if (method.Name != PlayerIsTargetableName)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(PlayerControllerB))
                continue;

            if (method.ReturnType != typeof(bool))
                continue;

            if (parameters.Length == 1)
                return (enemy, player) => (bool)method.Invoke(enemy, new object[] { player })!;

            for (var i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != typeof(bool))
                    goto NextMethod;
            }

            return (enemy, player) =>
            {
                var args = new object[parameters.Length];
                args[0] = player;
                for (var i = 1; i < parameters.Length; i++)
                {
                    var pi = parameters[i];
                    if (pi.HasDefaultValue && pi.DefaultValue is bool db)
                        args[i] = db;
                    else
                        args[i] = true;
                }

                return (bool)method.Invoke(enemy, args)!;
            };

            NextMethod:
            ;
        }

        return Fallback;
    }

    private static bool Fallback(EnemyAI _, PlayerControllerB player)
    {
        if (!_loggedFallback)
        {
            _loggedFallback = true;
            Debug.LogWarning(
                "[PathfindingLagFix] EnemyAI.PlayerIsTargetable was not found on this game version; " +
                "using a minimal fallback (alive players only) for async player pathfinding.");
        }

        if (player == null || player.isPlayerDead)
            return false;

        return true;
    }

    internal static bool IsPlayerTargetable(EnemyAI enemy, PlayerControllerB player) => Invoke(enemy, player);
}
