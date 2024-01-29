# PathfindingLagFix
This modifies the Bracken, Snare Flea and Spore Lizard AI to prevent large stutters that can occur every 200ms in some situations, by spreading the load of checking hundreds of paths over multiple frames.

## Details
- The EnemyAI.ChooseFarthestNodeFromPosition() function could end up checking 180+ nav mesh nodes for accessibility, if most or none of them were accessible. This could happen if the enemy was not on the nav mesh, or if the function call required line of sight and players were blocking most of the map from access. The following cases have been patched:
    - This would occur for the Bracken if it spawned outside, since it has no outside nodes, or if the bracken was in a dead end with a player watching the exit.
    - The Spore Lizard also uses the same function to check for a path away from the player out of line of sight.
    - The Snare Flea searches for a location far from the main door if no players are targetable. This check could take some time even under normal circumstances due to trying to find a node midway through the accessible nodes. It would always occur if the Snare Flea fell out of the playable area.
    - There may be other cases where this type of issue could occur. Please report any cases to investigate with steps to reproduce the lag.

## Notes

### FixCentipedeLag Compatibility
PathfindingLagFix is an _alternative_ to FixCentipedeLag, which aims to remove the lag caused by Snare Fleas. PathfindingLagFix also prevents the stutters that FixCentipedeLag avoids, but does so in such a way that the AI's behavior should be unchanged, whereas FixCentipedeLag will kill Snare Fleas almost instantly if no players are inside the building where Snare Fleas can target them.
