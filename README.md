# PathfindingLagFix

## WARNING: BETA VERSION! MAY CAUSE CRASHES!

Moves long-running pathfinding tasks off the main thread to improve frame rates significantly, especially for hosts.

Most patches must be tailored to each AI, so this will not affect modded enemies in most cases.

### Frametime graph comparisons:
Before/After on March with late night spawns:

![Before](https://raw.githubusercontent.com/Zaggy1024/LC_PathfindingLagFix/refs/heads/master/Media/march_night_before.png) ![After](https://raw.githubusercontent.com/Zaggy1024/LC_PathfindingLagFix/refs/heads/master/Media/march_night_after.png)

## Patches
The enemy behaviors currently patched include:
- All roaming enemies. This includes thumpers, hoarding bugs, jesters, and many more of the vanilla enemies. This is a single patch that applies to all enemies that use the vanilla search routine, and is the one exception in which modded enemies may be affected.
- Bracken patches:
    - The search for a hiding spot away from the main entrance when no players are targetable
    - The search for a hiding spot away from a player when it is spotted
    - The pathing towards the player out of line of sight when it is hunting
- Snare flea:
    - The search for a hiding spot at a certain number of paths away from main when no players are present
    - The search for a hiding spot near a player when they are targetable
- Spore lizard:
    - The search for a hiding spot away from a player when it is spotted
- Manticoil:
    - The search for a node to fly to when it is disturbed by a player at close range
- More to come!
