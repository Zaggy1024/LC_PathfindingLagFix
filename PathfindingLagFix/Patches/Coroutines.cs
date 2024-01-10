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

            var pathsRemaining = offset;
            for (var i = 0; i < candidateNodes.Length; i++)
            {
                var transform = candidateNodes[i].transform;
                if (!enemy.PathIsIntersectedByLineOfSight(transform.position, calculatePathDistance: false, avoidLineOfSight))
                {
                    enemy.mostOptimalDistance = Vector3.Distance(pos, transform.position);
                    yield return transform;
                    if (pathsRemaining == 0)
                        yield break;
                    pathsRemaining--;
                }
                else
                {
                    yield return null;
                }
            }

            // If all line of sight checks fail, we will find the furthest reachable path to allow the pathfinding to succeed once we set the target.
            // The vanilla version of this function may return an unreachable location, so the NavMeshAgent will reset it to a reachable location and
            // cause a delay in the enemy's movement.
            if (avoidLineOfSight)
            {
                Plugin.Instance.Logger.LogWarning("Failed to find a far location without LOS, resorting to furthest accessible path.");
                foreach (var node in candidateNodes)
                {
                    if (!enemy.PathIsIntersectedByLineOfSight(node.transform.position, calculatePathDistance: false, avoidLineOfSight: false))
                    {
                        yield return node.transform;
                        yield break;
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }
        }
    }
}
