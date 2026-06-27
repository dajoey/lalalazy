# Changelog - Lazy Fate Automation

## [0.0.1.44] - 2026-06-27
### Fixed
- **Stop now halts vnavmesh immediately.** Hitting Stop (or `/lazyfate stop`) while the bot was pathfinding/flying to a FATE previously left vnavmesh navigating to the destination on its own - cancelling the plugin's task does not stop vnav's in-flight movement. `FateToolKit` Running=false and the `FateGrind` task teardown now call `Svc.Navmesh.PathfindCancelAll()` + `Svc.Navmesh.Stop()` (cancel any in-progress pathfind AND stop following the current path), so the character stops the moment you hit Stop.

## [0.0.1.43] - 2026-06-27
### Fixed
- **Stop now fully releases the Gluttony Combo lease** instead of only disabling auto-rotation. Previously, Stop (and `/lazyfate stop`, and auto-complete) left Gluttony "controlled by Lazy Fate Automation" with the lease still held, so your manual/macro control of Gluttony stayed locked out. `FateToolKit` Running=false now calls `GluttonyComboIPC.Release()` (which calls `ReleaseControl`) instead of `Disable()`; a fresh lease is acquired on the next grind start. Between-FATE pauses still use `Disable()` (keep the lease, just stop the rotation).

## [0.0.1.42] - 2026-06-27
### Changed
- Renamed the BossMod combat preset `CBT - Gluttony` -> `Gluttony` (dropped the leftover "CBT -" prefix that came from the original "CBT - DwD" community preset). The status bar now reads just "Gluttony".

## [0.0.1.41] - 2026-06-27
### Fixed
- **Critical: Gluttony Combo lease churn that crashed Gluttony and tanked FPS.** `GluttonyComboIPC` re-registered a new lease on every transient IPC hiccup; because Gluttony's `CreateRegistration` dedups on `PluginName == internalPluginName` (and `PluginName` stores the *display* name), the dedup never matched and duplicate "Lazy Fate Automation" registrations piled up. Two or more registrations make Gluttony's `Search.AllJobsControlled` `ToDictionary` (keyed by plugin name) throw on every UI render and rotation tick - dead framerate, an error dialog in Gluttony's settings window, and a non-functional toggle macro. The lease is now acquired exactly once (throttled, only when none is held) and is never dropped on a transient error - only when Gluttony itself reports the lease invalid (by then it is already removed, so re-acquiring cannot duplicate).
### Changed
- Renamed the BossMod combat preset `CBT - DwD` -> `CBT - Gluttony` so the status bar reflects the new combat engine. Preset JSON re-brotli-compressed in `FateGrind._presetCompressed`; `_presetName` updated to match.
### Notes
- After updating, **reload Gluttony Combo** (or restart the game) once to clear any orphaned duplicate registrations left behind by v0.0.1.40.

## [0.0.1.40] - 2026-06-27
### Changed
- Combat is now driven by **Gluttony Combo** instead of BossMod's "DwD" autorotation. BossMod is kept only for movement and danger avoidance; Gluttony Combo's lease-based Auto-Rotation now owns the combat rotation **and** target selection.
- `FateGrind.HandleIntegrations` enables Gluttony Combo Auto-Rotation on FATE engage via the `GluttonyCombo` IPC (RegisterForLease -> SetAutoRotationState -> SetCurrentJobAutoRotationReady), configured for FATE grinding: DPSRotationMode=Nearest, FATEPriority=on, DPSAlwaysHardTarget=on (so BossMod movement follows Gluttony's hard target), InCombatOnly=off, BypassFATE=on, DPSAoETargets=3.
- BossMod `MiscAI.AutoTarget` now yields target authority to Gluttony: `Retarget=NoTarget` (only auto-targets when the player has nothing targeted) plus `FATE=Enabled` for the bootstrap case. The `MaxTargets` pull cap is retained.
- `FateGrind.DeactivateIntegrations` and the run-stop path disable Gluttony Combo Auto-Rotation so it never fires while travelling between FATEs.
### Added
- `Helpers/IPC/GluttonyComboIPC.cs` - IPC subscriber for Gluttony Combo's lease-based Auto-Rotation (prefix `GluttonyCombo`) with lease lifecycle (register/enable/disable/release) and FATE-grinding config. Modeled on GluttonyCombo/docs/IPCExample.cs.
- `Ipc.GluttonyCombo` flag; `Service.Gluttony` instance wired into plugin start/dispose.
### Notes
- Requires Gluttony Combo installed; the current job's Single-Target + AoE combos are enabled in Auto-Mode automatically. If Gluttony Combo is not loaded, the IsLoaded guard skips the integration and combat falls back to BossMod's prior behavior.
- Version jumped 0.0.1.39 -> 0.0.1.40 (no 0.0.1.39 CHANGELOG entry existed; this entry covers the combat-engine switch).

## [0.0.1.38] - 2026-06-09
### Fixed
- Prevented mounting and dismounting loops after FATE completion by keeping the bot in Engaging state (clearing remaining combat) before transitioning to BetweenFates or deactivating integrations.

## [0.0.1.35] - 2026-06-07
### Changed
- Stop movement and remove dismount/landing logic from `TeleportTo` pre-cast checks. Teleport casts will now initiate directly while remaining mounted.
- Exclude city hubs (where mounting is disabled) from random zone swapping, gemstone allowed zones, relic allowed zones, and the UI zone selector.

## [0.0.1.34] - 2026-06-07
### Added
- Added stuck check mitigation for combat/engage pathfinding fallbacks.
- Wait for combat to end before mounting.

## [0.0.1.33] - 2026-06-07
### Added
- Only dismount before teleporting if the player is flying (`Player.InFlight` is true). If the player is mounted on the ground, they will now remain mounted while teleporting, which makes travels faster and more natural.
- Added robust mounting verification and retry loops inside `MoveTo()`. If the player is in combat, the bot stands still and waits for combat to end before attempting to mount.
- Added mid-travel dismount checking inside `MoveTo()` pathfinding loops. If the player is dismounted mid-travel (e.g. from getting aggroed/hit), the bot halts movement, waits for combat to end, mounts up, and resumes pathfinding rather than walking on foot.
- Rewrote combat/engage stuck detection in `HandleCombatStuckDetection()`. Instead of depending on `InCombat` (which is false when running between FATE mobs) or `IsMoving` (which returns false when stuck against a wall), it now tracks position changes relative to the current target and activates `vnavmesh` pathing fallback if progress towards a distant target stops for 1.5 seconds.
- Added explicit landing and dismounting calls at the beginning of `TeleportTo()`, and wait for `!Player.IsBusy` to prevent teleport casts from immediately failing when mounted/flying.
### Fixed
- Fixed task crashing on teleport failures by replacing `ErrorIf(!ActionManager.Teleport())` with a robust 3-attempt retry loop that falls back to the `/return` recovery gracefully.

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
