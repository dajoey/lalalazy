# Changelog - Lazy Fate Automation

## v0.0.3.7 (2026-10-05) [testing]
### Fixed
- **The boss FATE icon on the server info bar entry now actually renders.** The previous testing build showed no icon at all because the glyph reference pointed at an empty slot instead of the boss FATE icon; the icon value is now pinned so it stays the boss glyph.
### Notes
- Testing channel only; the production channel is unchanged.
- Click behavior, the confirming second click and every other part of the server bar entry are unchanged.

## v0.0.3.6 (2026-10-05) [testing]
### Changed
- **The server info bar entry shows the boss FATE icon instead of the sword glyphs.** The same icon is used in every state; the bot's on/off state stays in the text (": On", ": Off" and the rest), exactly as before. No other entry is affected.
### Notes
- Testing channel only; the production channel is unchanged.
- The click behavior, the confirming second click and everything else in the previous build are unchanged.

## v0.0.3.5 (2026-10-05) [testing]
### Added
- **Server info bar (Umbra) toggle for the FATE bot.** The plugin adds a "Lazy Fate Automation" entry to the game's server info bar, which Umbra renders as a button. It shows the bot state with the sibling entries' sword icon: On, Off, "(stopping)" while a soft stop is pending, or the live state such as "Paused (in instance)". Clicking it stops the bot (Ctrl+click waits for the current FATE to finish, like the window's soft stop); when stopped, the first click arms the start and a second click within 10 seconds confirms it, so a stray click cannot start the automation. Starting still requires a logged-in character, exactly like the Lazy Hub switch.
- New display setting "Show server info bar entry" (on by default) hides or shows the entry; it can also be hidden per-plugin in Umbra or under /xlsettings -> Server Info Bar.
### Notes
- Testing channel only; the production channel is unchanged.
- The Lazy Hub quick controls, the snapshot endpoint and all other behavior are unchanged.

## v0.0.3.4 (2026-10-05) [testing]
### Fixed
- **The currency-focus fallback setting now takes effect.** In the previous version both fallback options kept rotating the focused zones identically. Zone swaps now first pass through every focused zone; once a full pass finds no FATE to run, "Continue normally" (default) resumes the usual zone rotation (achievement-guided / same expansion), while "Idle in zone" stays put and waits for focused-currency FATEs instead of grinding other FATEs.
- Excluding every focused zone now falls back the same way instead of waiting in place.
- The fallback setting's in-game help describes the new behavior.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.0.3.3 (2026-10-05) [testing]
### Added
- **All FATE zones are supported and selectable.** The zone list is now built from the game's own data: every A Realm Reborn through Dawntrail zone with FATEs and a teleport aetheryte (47 zones), grouped by expansion in the settings, so future zones appear with no plugin update. Zone swaps no longer pick zones the aetheryte network cannot reach; automation inside foray zones (Bozja, Eureka, Occult Crescent, cosmic exploration) is unchanged when the character is already there.
- **FATE type selection.** The settings offer a switch per FATE type: kill & boss, item collection (turn-in), escort, defend, seasonal event, chase, and the two Firmament types. Unchecked types are never started. Defaults unchanged: every type runs.
- **Currency focus.** A new setting focuses the automation on a FATE reward currency: Company Seals (A Realm Reborn through Stormblood zones), Bicolor Gemstones (Shadowbringers and later) or Yo-kai Medals (the Yo-kai Watch event zones). While a focus is set, zone swaps prefer zones whose FATEs reward that currency, and a fallback setting chooses between continuing normally (default) and idling until a focused FATE is up. A grind mode with its own zone list still wins over the focus.
- **Settings reorganized into collapsible sections** (FATE selection, currency focus, swap zones, sorting & display). The zone list is grouped by expansion with a text filter, two-column checkboxes and per-expansion all/none buttons instead of one long checklist.
### Notes
- Existing configs load unchanged: every new setting defaults to the previous behavior.
- Lazy Hub quick controls and the local dashboard snapshot (port 10505) are unchanged.
- Testing channel only; the production channel is unchanged.

## v0.0.3.2 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers the FATE bot switch (starting asks for a confirmation), "Stop when safe", "Prioritize Forlorn Maidens", "Swap zones when empty", the three FATE filters (longest duration, least time left, furthest progress) and the grind mode to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
- The zone swap, the FATE filters and the grind mode can only be changed while the bot is stopped. The filters have limited ranges that still let FATEs through.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v0.0.3.1 (2026-09-12)

### Added

- Combat now prioritizes a Forlorn Maiden or the Forlorn (the rare bonus mob that can spawn mid-FATE and despawns quickly if not killed) over the FATE's normal targets. When one is present it becomes the automation's hard target until it is defeated or disappears; normal nearest-target behaviour resumes automatically afterward. New setting "Prioritize Forlorn Maidens" (on by default).
- Debug log line when a Forlorn is detected or clears, for troubleshooting.

### Fixed

- The fate-priority sort order list could grow duplicate entries over repeated updates (harmless to ordering, just clutter in the settings window). Existing installs are cleaned up automatically on the next load.

## v0.0.3.0 (2026-09-07)

### Added

- Dashboard snapshot endpoint: the plugin now serves a read-only JSON snapshot of FATE and hunt state at http://127.0.0.1:10505/fates on the game host (new FateSnapshot service types). Loopback-only, no settings. The home dashboard relay polls it; nothing in the plugin's FATE automation behaviour changes.
- The snapshot lists each active FATE with progress, seconds remaining, bonus flag, and whether the player is inside it, capped at the 12 most relevant (joined FATE first, soonest to expire next).
- The snapshot carries a session counter of FATEs completed (a joined FATE that reaches 100% and then ends counts once) and the number of unlocked hunt bills with their total monster kills so far.
- The snapshot names one live elite hunt mark present in the current zone (the game's own hunt-target check, MobHunt.IsHuntTarget); no name when none is up.
- New debug command: "/lazyfate snapshot" prints the current snapshot state to the plugin log for troubleshooting.

### Notes

- The snapshot endpoint answers 503 only before the first character loads (title screen); afterwards it always serves the latest snapshot, matching LazyRetainerLive's behaviour.

## v0.0.2.0 (2026-09-05)

- Added the in-game "What's new" popup. After Lazy Fate Automation updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/lazyfate changelog` any time to reopen it.
- No change to FATE grinding: zone swapping, the duty pause, the Gluttony lease and every sort/blacklist setting are unchanged.

## [0.0.1.45] - 2026-08-17
### Fixed
- **Pauses inside instanced content and RELEASES the Gluttony lease.** `FateGrind` had no duty gate: queuing
 into a dungeon/trial/raid while the grind was running left the Gluttony lease live for the whole duty
 (FATE config overlay - `DPSAlwaysHardTarget`, `DPSRotationMode=Nearest`, `InCombatOnly=false`,
 `OnlyAttackInCombat=false` - plus every job combo forced into auto-mode), and every out-of-combat loop
 iteration cleared the player's target. In an 8-man raid on WHM that snapped the hard target back to the
 boss on every DPS GCD and every heal fell through the heal stack to Self. The loop now detects
 `BoundByDuty && !PublicEvent.IsFateTerritory` (forays / Bozja / Occult Crescent / Cosmic still run), tears
 down the BossMod preset, stops vnavmesh, calls `GluttonyComboIPC.Release` (Auto-Rotation falls back to
 the user's own settings for the duty), does NOT touch the target, and idles with status
 `Paused (in instance)` until back in a FATE zone, where a fresh lease is acquired on the next engage.
- **Auto-Rotation control actually works now.** `GluttonyComboIPC.Enable` called `SetAutoRotationConfigState`
 x7 before the first `SetAutoRotationState`; Gluttony's `AddRegistrationForAutoRotation` (through 1.0.4.141)
 then threw `KeyNotFoundException` on every `SetAutoRotationState`, so the plugin never turned Auto-Rotation
 on/off (the user had to toggle it by hand, and it stayed on afterwards) and logged
 `Gluttony Combo: Enable failed: Exception has been thrown by the target of an invocation` every frame
 (70 MB LazyFateAutomation.log). `SetAutoRotationState` is now sent first, which works against both the
 fixed Gluttony 1.0.4.142 and older builds.
- **`InvalidLease` handled on every IPC call** (`SetAutoRotationState`, `SetAutoRotationConfigState`,
 `SetCurrentJobAutoRotationReady`), not just the last one - a lease suspended by Gluttony (job change) is
 forgotten immediately and re-acquired (still throttled to one registration per 10s).
- IPC warnings are throttled to one per 10s.

## [0.0.1.44] - 2026-06-27
### Fixed
- **Stop now halts vnavmesh immediately.** Hitting Stop (or `/lazyfate stop`) while the bot was pathfinding/flying to a FATE previously left vnavmesh navigating to the destination on its own - cancelling the plugin's task does not stop vnav's in-flight movement. `FateToolKit` Running=false and the `FateGrind` task teardown now call `Svc.Navmesh.PathfindCancelAll` + `Svc.Navmesh.Stop` (cancel any in-progress pathfind AND stop following the current path), so the character stops the moment Stop is pressed.

## [0.0.1.43] - 2026-06-27
### Fixed
- **Stop now fully releases the Gluttony Combo lease** instead of only disabling auto-rotation. Previously, Stop (and `/lazyfate stop`, and auto-complete) left Gluttony "controlled by Lazy Fate Automation" with the lease still held, so manual/macro control of Gluttony stayed locked out. `FateToolKit` Running=false now calls `GluttonyComboIPC.Release` (which calls `ReleaseControl`) instead of `Disable`; a fresh lease is acquired on the next grind start. Between-FATE pauses still use `Disable` (keep the lease, just stop the rotation).

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
- IPC subscriber for Gluttony Combo's lease-based Auto-Rotation (prefix `GluttonyCombo`) with lease lifecycle (register/enable/disable/release) and FATE-grinding config.
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
- Added robust mounting verification and retry loops inside `MoveTo`. If the player is in combat, the bot stands still and waits for combat to end before attempting to mount.
- Added mid-travel dismount checking inside `MoveTo` pathfinding loops. If the player is dismounted mid-travel (e.g. from getting aggroed/hit), the bot halts movement, waits for combat to end, mounts up, and resumes pathfinding rather than walking on foot.
- Rewrote combat/engage stuck detection in `HandleCombatStuckDetection`. Instead of depending on `InCombat` (which is false when running between FATE mobs) or `IsMoving` (which returns false when stuck against a wall), it now tracks position changes relative to the current target and activates `vnavmesh` pathing fallback if progress towards a distant target stops for 1.5 seconds.
- Added explicit landing and dismounting calls at the beginning of `TeleportTo`, and wait for `!Player.IsBusy` to prevent teleport casts from immediately failing when mounted/flying.
### Fixed
- Fixed task crashing on teleport failures by replacing `ErrorIf(!ActionManager.Teleport)` with a robust 3-attempt retry loop that falls back to the `/return` recovery gracefully.

## [0.0.1.26] - 2026-06-07
### Added
- Added combat stuck detection and mitigation in `HandleCombatStuckDetection`. If BossMod's straight-line movement gets the player stuck on trees/obstacles in combat for 1.5 seconds, the bot disables BossMod movement and uses `vnavmesh` to pathfind around the obstacle to the target.
- Added auto-skipping for NPC dialogue SelectString option lists in `TaskBase.WaitUntilSkipping`.
### Fixed
- Gated teleporting and mounting on `!Svc.Condition[ConditionFlag.InCombat]` in to prevent getting stuck in combat.
- Prevented rapid mounting and dismounting loops during chain FATEs while waiting for the next FATE to spawn.

## [0.0.1.23] - 2026-06-06
### Changed
- File logging now suppresses DBG/TRC scope tracing by default; only WRN/ERR are written to the plugin log file. Set VerboseFileLogging to true in the plugin config (LazyFateAutomation.json) to restore full debug logging for troubleshooting.
- Fixes ~28 MB log growth observed from FATE-grind DebugContext scope enter/exit tracing.

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
