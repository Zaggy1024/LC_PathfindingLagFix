## Version 2.0.4 (Beta)
- Added a patch to prevent stutters when enemies are trying to target players in large lobbies.
- Removed the crash warning from the readme, as 2.0.3 appears to have remained stable.

## Version 2.0.3 (Beta)
- Added a workaround to prevent crashes that would occur when the navmesh is modified while pathfinding jobs are running.

## Version 2.0.2 (Beta)
- Added a warning to the readme and description about potential crashes.

## Version 2.0.1 (Beta)
- Modified threaded code in an effort to prevent crashes. The crashes are extremely rare, so these fixes are unconfirmed, and crashes may still be present.
- Fixed some issues that may cause unintended inconsistencies in pathfinding versus the vanilla code.

## Version 2.0.0 (Beta)
- Rewrote the mod to run any pathfinding patches off the main thread via Unity Jobs, reducing the performance impact to near zero.
- Patches have been completely rewritten and now include:
  - All roaming AI
  - Bracken hunting, evasion, and hiding spot pathfinding
  - Snare flea hiding spot pathfinding
  - Spore lizard evasion pathfinding
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
