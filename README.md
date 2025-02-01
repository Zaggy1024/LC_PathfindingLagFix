# PathfindingLagFix
Moves many AI pathfinding tasks off the main thread to improve frame rates significantly, especially for hosts.

Most patches must be tailored to each AI, so this will not affect modded enemies in most cases.

The asynchronous pathfinding jobs are rendered safe from crashing through [PathfindingLib](https://thunderstore.io/c/lethal-company/p/Zaggy1024/PathfindingLib/).

### Frametime graph comparisons:
Before/after on March with late night spawns:

![Before](https://raw.githubusercontent.com/Zaggy1024/LC_PathfindingLagFix/refs/heads/master/Media/march_night_before.png) ![After](https://raw.githubusercontent.com/Zaggy1024/LC_PathfindingLagFix/refs/heads/master/Media/march_night_after.png)

### Support my work:
If you wish to support my continued work, feel free to donate to me at my ko-fi: https://ko-fi.com/zaggy1024

Thanks!

## Patches
The enemy behaviors currently patched to run their pathfinding off the main thread include:
- General enemies:
  - The roaming search patterns used by many enemies (thumpers, hoarding bugs, jesters, etc), including any modded enemies that use this vanilla functionality
  - The checks for valid paths to players to be targeted
- Bracken:
  - The search for a hiding spot away from the main entrance when no players are targetable
  - The search for a hiding spot away from a player when it is spotted
  - The pathing towards the player out of line of sight when it is hunting
- Snare flea:
  - The search for a hiding spot at a certain number of paths away from main when no players are present
  - The search for a hiding spot near a player when they are targetable
- Spore lizard:
  - The search for a hiding spot away from a player when it is spotted
- Tulip snake:
  - Selection of a node to run away to after dismounting the player
- Manticoil:
  - The search for a node to fly to when it is disturbed by a player at close range

Other changes include:
- Bracken:
  - A vanilla bug that causes the bracken to stand in place if spotted within 5 units will no longer occur with the async pathfinding solution.
- Tulip snake:
  - A patch to prevent small stutters due to calls to `Object.FindObjectsByType<FlowerSnakeEnemy>()`.

## Thanks to
- Lunxara - Tested the early beta versions that were crashing due to Unity silliness.
- mattymatty - Brainstormed, assisted in reverse engineering Unity, and helped implementing synchronization to prevent crashes.
- XuXiaolan - Helped test asynchronous pathfinding by becoming an early adopter of [PathfindingLib](https://thunderstore.io/c/lethal-company/p/Zaggy1024/PathfindingLib/) in [CodeRebirth](https://thunderstore.io/c/lethal-company/p/XuXiaolan/CodeRebirth/).
- Zehs - Pointed out the calls to `FindObjectsByType<FlowerSnakeEnemy>()` that are patched since v2.0.0.
- qwertyrodrigo - Made this meme:
![A meme](https://raw.githubusercontent.com/Zaggy1024/LC_PathfindingLagFix/refs/heads/master/Media/meme.png)
