using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace PathfindingLagFix.Patches
{
    public static class Coroutines
    {
        public static IEnumerator<Transform> ChooseFarthestNodeFromPosition(EnemyAI enemy, Vector3 pos, bool avoidLineOfSight = false, int offset = 0)
        {
            var candidateNodes = enemy.allAINodes.OrderByDescending(node => Vector3.Distance(pos, node.transform.position)).ToArray();

            yield return null;

            Transform firstAccessibleNode = null;

            var pathsRemaining = offset;
            for (var i = 0; i < candidateNodes.Length; i++)
            {
                var transform = candidateNodes[i].transform;
                var position = transform.position;

                // Check if the path is accessible.
                if (!enemy.agent.CalculatePath(position, enemy.path1))
                {
                    yield return null;
                    continue;
                }
                if (firstAccessibleNode == null)
                    firstAccessibleNode = transform;

                // Check if the end of the path is close enough to the target.
                if (Vector3.Distance(enemy.path1.corners[enemy.path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
                {
                    yield return null;
                    continue;
                }

                if (avoidLineOfSight)
                {
                    // Check if any segment of the path enters a player's line of sight.
                    bool pathObstructed = false;
                    for (int segment = 1; segment < enemy.path1.corners.Length && !pathObstructed; segment++)
                        pathObstructed = Physics.Linecast(enemy.path1.corners[segment - 1], enemy.path1.corners[segment], 0x40000);
                    if (pathObstructed)
                    {
                        yield return null;
                        continue;
                    }
                }

                enemy.mostOptimalDistance = Vector3.Distance(pos, transform.position);
                yield return transform;
                if (pathsRemaining == 0)
                    yield break;
                pathsRemaining--;
            }

            // If all line of sight checks fail, we will find the furthest reachable path to allow the pathfinding to succeed once we set the target.
            // The vanilla version of this function may return an unreachable location, so the NavMeshAgent will reset it to a reachable location and
            // cause a delay in the enemy's movement.
            if (avoidLineOfSight)
                yield return firstAccessibleNode;
        }
    }
}
