## Version 2.2.1
- Fixed blobs often being stretched to the origin for a while after they spawn.

## Version 2.2.0
- Patched masked to use the cached elevator controller instead of finding it fresh every AI interval.
- Patched the method to find the main entrance to use a cache and avoid a `FindObjectsOfType` call when masked are pathing to it.
- Patched hygroderes to reduce the physics casts per frame from 8 to 1.
- Patched the maneater AI to avoid synchronous pathfinding when the adult is sneaking and when it checks if it is stuck.
- Patched coilheads' player targeting pathfinding to run asynchronously.

## Version 2.1.1
- Fixed a bug that caused distance pathfinding to get incorrect results when the jobs take a long time. This fixes a bug that could cause bracken to path in strange ways, especially on large interiors like Titan.

## Version 2.1.0
- Fixed a bug that would cause the bracken not to path out of line of sight in some situations.
- Added presets and options to configure the bug fixes that slightly change behavior from vanilla.

## Version 2.0.1
- Updated the manifest description to no longer state that it is a beta version.

## Version 2.0.0
> Note that this has the same mod ID as PathfindingLagFix Beta, but a lower version, so that will need to be disabled in order to run the stable version.

- Rewrote the mod to run all pathfinding patches off the main thread using [PathfindingLib](https://thunderstore.io/c/lethal-company/p/Zaggy1024/PathfindingLib/), reducing the performance impact of many forms of vanilla enemy pathfinding to near zero. This provides a general framerate improvement when many enemies are spawned, especially for hosts.
- All patches have been completely rewritten.
- Patched behaviors now include:
  - All roaming AI (i.e. thumpers, lootbugs, coilheads, and many more)
  - All omniscient player targeting (i.e. blobs, jesters, brackens, and more)
  - Bracken hunting, evasion, and hiding spot pathfinding
  - Snare flea hiding spot pathfinding
  - Spore lizard evasion pathfinding
  - Tulip snake dismounting pathfinding, and calls to `FindObjectsByType<FlowerSnakeEnemy>()`
  - Manticoil evasion pathfinding

## Version 1.4.0
- Brought back the patch for lag caused when the Bracken has no player target and is therefore pathing to the furthest position from the interior entrance. This would often occur when the player jumps.

## Version 1.3.1
- Updated mod icon and Thunderstore summary.

## Version 1.3.0
- Updated to support v60/v61, removing patches for the Bracken and Spore Lizard now that vanilla uses an async method for those AI.

## Version 1.2.1
- Removed some debug spam that would happen while a Snare Flea is retreating with no target players.

## Version 1.2.0
- Prevent stutters caused by Snare Fleas finding a far location when there are no players to target.
  - This pathfinding would always happen if a Snare Flea spawned while no players are within the building.
  - More often in normal gameplay, it would occur if the Snare Flea fell off the playable area, since this would make all players untargetable, as well as make the search for a far location eventually fail.
  - NOTE: This may not cover all cases of lag caused by Snare Fleas. When they are running from a kill, they look for the nearest location that is accessible out of line of sight. If all line of sight checks fail, there may be stutters.

## Version 1.1.0
- Fixed stutters that would happen when Spore Lizards run from the player.

## Version 1.0.1
- Removed printing of the FlowermanAI.DoAIInterval() bytecode.

## Version 1.0.0
- Fixed stutters that could occur when Brackens are retreating.
- Fixed stutters that always occur when Brackens spawn outside or are not on the nav mesh.
