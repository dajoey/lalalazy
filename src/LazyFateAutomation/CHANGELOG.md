# Changelog - Lazy Fate Automation

## [0.0.1.17] - 2026-05-31
### Fixed
- Fixed same-zone teleport race conditions where the bot would immediately proceed and start moving before the teleport cast actually began or completed.
- Added a robust 2-second timeout safeguard to detect if the teleport cast failed to start.
- Added an automatic recovery mechanism that executes the `/return` command to reset the client state if it detects the client is stuck in the FFXIV "another teleport is underway" bugged state.

## [0.0.1.15] - 2026-05-30
### Fixed
- Fixed an issue where the bot would mount, dismount, and then get stuck saying "Automation: Dismounting" during zone swaps or same-zone teleports.
- Added a wait condition to ensure the dismount animation lock has fully decayed and player movement momentum has stopped before executing the teleport cast.
- Corrected task status reporting to reset back to "Teleporting" after a dismount completes.

## [0.0.1.12] - 2026-05-24
### Added
- Added proactive landing and dismounting checks before initiating any teleportation action. If the player is mounted/flying in the air, the bot will safely descend, land, and dismount before casting Teleport, preventing silent casting blocks and infinite zone-change hangs.

## [0.0.1.10] - 2026-05-18
### Fixed
- Wrapped all BossMod/BMR and TextAdvance IPC calls in robust try-catch blocks to prevent unhandled IpcNotReadyError exceptions from crashing the plugin during start/stop state changes.

## [0.0.1.9] - 2026-05-11
### Added
- Routed all internal Automation task state machine events, errors, and cancellations through Svc.LogToFile.
- Improved Svc.LogToFile path resolution to dynamically detect Proton/Wine home directory structure under Linux/Bazzite (`Z:\home\<username>\.xlcore\logs\`) for live diagnostics.
