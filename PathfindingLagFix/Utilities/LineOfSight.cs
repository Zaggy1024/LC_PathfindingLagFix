using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace PathfindingLagFix.Utilities;

internal static class LineOfSight
{
    private const int LINE_OF_SIGHT_LAYER_MASK = 0x40000;

    /// <summary>
    /// Checks line of sight along a path equivalently to how <see cref="EnemyAI.PathIsIntersectedByLineOfSight"/> does
    /// after calling CalculatePath(), optionally also checking line of sight to a target position for each segment.
    /// </summary>
    /// <param name="path">An array of navmesh positions that make up the path.</param>
    /// <param name="checkLOSToPosition">A position to check line of sight to for each segment, allowing target player
    /// LOS checks equivalent to <see cref="EnemyAI.PathIsIntersectedByLineOfSight"/>.</param>
    /// <returns>Whether the path is blocked.</returns>
    public static bool PathIsBlockedByLineOfSight(NativeArray<Vector3> path, Vector3? checkLOSToPosition = null)
    {
        if (path.Length <= 1)
            return false;

        // Check if any segment of the path enters a player's line of sight.
        for (int segment = 1; segment < path.Length && segment < 16; segment++)
        {
            var segmentStart = path[segment - 1];
            var segmentEnd = path[segment];

            if (checkLOSToPosition.HasValue)
            {
                if (!Physics.Linecast(segmentStart, checkLOSToPosition.Value + Vector3.up * 0.3f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    return true;
            }

            if (Physics.Linecast(segmentStart, segmentEnd, LINE_OF_SIGHT_LAYER_MASK))
                return true;
        }

        return false;
    }
}
