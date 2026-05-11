using UnityEngine;

namespace PathfindingLagFix.Utilities;

internal static class CoroutineWaiters
{
    public static readonly WaitForEndOfFrame WaitForEndOfFrame = new();
}
