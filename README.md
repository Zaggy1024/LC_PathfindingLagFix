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

## Options

The mod is designed to change the behavior of enemies as minimally as possible, but there are some bugs that are incidental to the included patches and cannot be fixed by another mod unless it maintains two different versions of the patch. Therefore, the fixes are included in PathfindingLagFix.

In order to allow easy configuration of these fixes, two presets are included to define the general effect of options that can change behavior:
- OnlyFixes: The default, this will select only options that make what are deemed to be fixes to bugs in the vanilla behavior of enemies.
- Vanilla: This will attempt to retain the behavior of the vanilla game as much as possible.

They control two options that can be overridden individually:
- DistancePathfindingFallbackNodeSelection: Sets how distance pathfinding, i.e. bracken evasion, behaves when it fails to find a valid path out of line of sight, or any other criteria. The default behavior is to only return reachable nodes. This can be changed to behave the same as vanilla, where the path may not be valid, resulting in the enemy stutter stepping towards the evasion node.
- AsyncDistancePathfindingMostOptimalDistanceBehavior: Determines whether mostOptimalDistance is set to the distance to the chosen node for AI when they are making use of the vanilla async pathfinding solution. Not setting it causes the bracken to stop in front of the player if they spot it within 5 units.

## Thanks to
- Lunxara - Tested the early beta versions that were crashing due to Unity silliness.
- mattymatty - Brainstormed, assisted in reverse engineering Unity, and helped implementing synchronization to prevent crashes.
- XuXiaolan - Helped test asynchronous pathfinding by becoming an early adopter of [PathfindingLib](https://thunderstore.io/c/lethal-company/p/Zaggy1024/PathfindingLib/) in [CodeRebirth](https://thunderstore.io/c/lethal-company/p/XuXiaolan/CodeRebirth/).
- Zehs - Pointed out the calls to `FindObjectsByType<FlowerSnakeEnemy>()` that are patched since v2.0.0.
- qwertyrodrigo - Made this meme:
![A meme](https://raw.githubusercontent.com/Zaggy1024/LC_PathfindingLagFix/refs/heads/master/Media/meme.png)
