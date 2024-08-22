# PathfindingLagFix
This modifies the Snare Flea AI to prevent large stutters that can occur every 200ms in some situations, by spreading the load of checking hundreds of paths over multiple frames.

Originally, it was designed to address lag that the Bracken could cause, which has since been addressed by Zeekers.

## Details
- The EnemyAI.ChooseFarthestNodeFromPosition() function could end up checking 180+ nav mesh nodes for accessibility, if most or none of them were accessible. This could happen if the enemy was not on the nav mesh, or if the function call required line of sight and players were blocking most of the map from access. The following cases have been patched:
    - When the Bracken cannot target a player, either due to them being outside or being mid-air making a jump in the interior, it will try to path to the farthest position from the entrance. This could cause a large stutter on maps with a lot of inaccessible nodes.
    - The Snare Flea searches for a location far from the main door if no players are targetable. This check could take some time even under normal circumstances due to trying to find a node midway through the accessible nodes. It would always occur if the Snare Flea fell out of the playable area.
    - There may be other cases where this type of issue could occur. Please report any cases to investigate with steps to reproduce the lag.

Historically, this mod also contained patches to prevent Brackens and Spore Lizards from causing lag when retreating from a player while blocked from most positions by players' lines of sight. Those issues have since been patched in the vanilla game.

## Notes

### FixCentipedeLag Compatibility
PathfindingLagFix is an _alternative_ to FixCentipedeLag, which aims to remove the lag caused by Snare Fleas. PathfindingLagFix also prevents the stutters that FixCentipedeLag avoids, but does so in such a way that the AI's behavior should be unchanged, whereas FixCentipedeLag will kill Snare Fleas almost instantly if no players are inside the building where Snare Fleas can target them.
