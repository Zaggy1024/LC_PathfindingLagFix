# PathfindingLagFix
This modifies the Bracken AI (and others soon) to prevent it from causing a large stutter every 200ms in some situations, by spreading the load of checking hundreds of paths over multiple frames.

## Details
- The EnemyAI.ChooseFarthestNodeFromPosition() function could end up checking 180+ nav mesh nodes for accessibility, if most or none of them were accessible. This could happen if the enemy was not on the nav mesh, or if the function call required line of sight and players were blocking most of the map from access. The following cases have been patched:
    - This would occur for the Bracken if it spawned outside, since it has no outside nodes, or if the bracken was in a dead end with a player watching the exit.
    - The Spore Lizard also uses the same function to check for a path away from the player out of line of sight.
    - There may be other cases where this type of issue could occur. Please report any cases to investigate with steps to reproduce the lag.
