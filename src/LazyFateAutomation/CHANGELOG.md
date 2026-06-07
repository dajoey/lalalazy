# Changelog - Lazy Fate Automation

## [0.0.1.26] - 2026-06-07
### Added
- Added combat stuck detection and mitigation in `HandleCombatStuckDetection()`. If BossMod's straight-line movement gets the player stuck on trees/obstacles in combat for 1.5 seconds, the bot disables BossMod movement and uses `vnavmesh` to pathfind around the obstacle to the target.
- Added auto-skipping for NPC dialogue SelectString option lists in `TaskBase.WaitUntilSkipping()`.
### Fixed
- Gated teleporting and mounting on `!Svc.Condition[ConditionFlag.InCombat]` in `TaskBase.cs` to prevent getting stuck in combat.
- Prevented rapid mounting and dismounting loops during chain FATEs while waiting for the next FATE to spawn.

## [0.0.1.23] - 2026-06-06
### Changed
- File logging now suppresses DBG/TRC scope tracing by default; only WRN/ERR are written to LazyFateAutomation.log. Set VerboseFileLogging to true in the plugin config (LazyFateAutomation.json) to restore full debug logging for troubleshooting. (Svc.cs LogToFile gate, Configuration.cs flag.)
- Fixes ~28 MB log growth observed 2026-06-06 from FATE-grind DebugContext scope enter/exit tracing.

## [0.0.1.19] - 2026-05-31
### Added
- Added a 30-second cooldown on empty-zone teleports to prevent rapid infinite teleporting loops when all selected/mode zones are empty.
### Changed
- Moved the checkable swap zones list exclusively to the Settings panel (hidden by default) rather than showing on the main tracker window to keep the UI clean.

## [0.0.1.18] - 2026-05-31
### Added
- Added ability to list and selectively restrict/exclude teleport zones directly in the main UI and settings panel.
- Added Select All and Clear All quick-configuration controls.
- Added custom filtering to relic item target zone swapping to strictly honor user restrictions.

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
