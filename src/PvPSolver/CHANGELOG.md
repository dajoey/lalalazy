# Changelog - PvP Solver

## v0.1.1.1 (2026-09-05)

- Fixed a log line that repeated forever instead of once. The startup line "MajorUpdater: first valid cycle" sat behind a check that only asked whether rotations had loaded yet, but the flag that answers that is only ever set while actually in a PvP-valid state - so out of PvP it never flipped and the line printed on every single update cycle, including at the title screen with no character logged in. It now sits inside the block that really does load the rotations, so it prints once each time rotations are loaded and describes the state it claims to describe. (`PvPSolver/Updaters/MajorUpdater.cs`, `RSRGateUpdate`)
- Impact: over one 5.5-hour sample this single line was 55,497 of the 106,116 log lines collected across every installed plugin - 52.3% of the whole log - and 29,152 of those were written at the title screen by a PvP plugin. The same window would now produce 91 of them, a 99.8% reduction, with no loss of information: the line still states what condition the plugin came up in.
- No gameplay change. Rotation loading, targeting and the PvP rotations themselves are untouched; the only edit is where the log statement sits.

### Notes
- The paired "triggering rotation load..." line already fired 91 times in that sample rather than once, because `MajorUpdater.IsValid` resets `_rotationsLoaded` on every zone transition, logout and `Player.Available == false`. That reload behaviour is unchanged here and is tracked separately; it is why the line is "once per rotation load" rather than literally once per session.
- Second log-spam fix in this plugin (v0.1.0.11 throttled the per-frame "no rotation found" warnings). Every other `PluginLog.Information` under `src/PvPSolver/` was audited this release: the 53 in `PvPSolver.Basic/Helpers/ObjectHelper.cs` are all gated behind `Service.Config.InDebug`, and nothing else logs at Information on a per-cycle path.

## v0.1.1.0 (2026-09-05)

- Added the in-game "What's new" popup. After PvP Solver updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/pvpsolver changelog` (or `/pvs changelog`) any time to reopen it.
- No change to rotations or targeting: the sticky-target and team-pressure work from 0.1.0.13 is untouched, and the seen-version is stored in its own small file so upstream RotationSolverReborn syncs stay clean.

## [0.1.0.13] - 2026-08-25
### Added
- **`PvPHighestPressure` targeting mode** (issue #4, requested by @Petra105). Selects the hostile the most members of the player's own side are currently targeting, breaking ties toward the lowest HP percentage. Party and alliance lists are both counted, so it behaves in Frontline (where the rest of the player's side arrives as alliance members) as well as in Crystalline Conflict. The player's current target is excluded from the count, otherwise the mode would reinforce whatever was already targeted. Appended to the end of `TargetingType`, so existing saved `TargetingTypes` indices are unaffected, and it is **not** added to the default list - it is opt-in from Target settings. (`PvPSolver.Basic/Data/TargetType.cs`, `PvPSolver.Basic/Actions/ActionTargetInfo.cs`)

### Fixed
- **Auto-target abandoned an enemy mid damage-window** (issue #4, same report). `FindHostileRaw` re-ranked every hostile from scratch on each action and returned the top of the list, with no reference to the target already selected; `RSCommands.DoAction` then hard-assigned `Svc.Targets.Target` to it. Under an HP-based targeting mode this flips target the instant another enemy's HP crosses the player's, so a just-applied Wildfire is left on a target no longer being hit. `FindHostileRaw` now consults `FindStickyTarget` before returning: if the current target is still a legal candidate **for the action being used** and carries at least one status sourced from the player with `0 < RemainingTime <= StickyTargetMaxRemaining`, it is kept. (`PvPSolver.Basic/Actions/ActionTargetInfo.cs`)

### Changed
- Two new options under Target settings: **`StickyTarget`** (default **on**) and **`StickyTargetMaxRemaining`** (default 30s). The second exists so an effectively permanent aura cannot pin the target forever - `RemainingTime == 0` is this codebase's convention for a permanent status and is already excluded, and the cap covers long ones.

### Notes
- **Trade-off, by design:** while the debuff is ticking the rotation will stay on that target even if another enemy drops lower. That is the requested behavior, and turning `StickyTarget` off restores the previous snap-to-lowest-HP handling exactly.
- **Scoped to PvP.** `FindStickyTarget` returns early unless `DataCenter.IsPvP`, so PvE target selection is byte-for-byte unchanged.
- Sticky selection reads `Svc.Targets.Target`, i.e. the target the previous action settled on, which is what makes this hysteresis rather than a second independent pick.
- `CountTeamPressure` scores every candidate once before sorting rather than inside the comparator; a comparator that re-reads live target data can throw `IComparer.Compare() returns inconsistent results` if a teammate switches target mid-sort.
- **Not yet verified in-game** - shipped to the testing channel only.

## [0.1.0.12] - 2026-08-06
### Changed
- Synced upstream RotationSolverReborn `35ceb6e82` ("Guard status check for Doom to ensure valid object"): the PvP-rotation portion of that commit is a `GameVersion` attribute bump from `7.5` to `7.55` across all 21 PvP rotations.

### Notes
- **No gameplay change.** The Doom guard itself lives in `RotationSolver.Basic/Helpers/StatusHelper.cs` and upstream's PvE rotations - base classes this fork intentionally does not track (RUNBOOK 3.2). The fork's PvP rotations contain no Doom status checks, so there is nothing to port.
- Synced via `tools/sync-upstream-rotations.sh`; all 21 PvP rotation files verified byte-identical to upstream `6c064f910` (CR-normalised).
- **Promoted to production 2026-08-09.** `AssemblyVersion` 0.1.0.9 -> 0.1.0.12; both channels now ship this build. In-game gate waived by Joey: the 0.1.0.11 fix executes only outside PvP (guard on `DataCenter.IsPvP`) and this sync is attribute-only, so PvP play cannot exercise what changed; the user-facing check is the absence of the PvE `no rotation found` warning in `dalamud.log` (issue #3).

## [0.1.0.11] - 2026-08-03
### Fixed
- **Per-frame log spam in all PvE content** (issue #3, reported by @TheWalkingDude19). `UpdateCustomRotation` is driven from the framework tick via `MajorUpdater.RSRRotationAndStateUpdate`, and emitted `WRN ... no rotation found for job=<job> combatType=PvE` roughly 50 times per second for as long as the player was outside PvP. PvPSolver is PvP-only and ships no PvE rotations, so the lookup could never succeed; worse, the miss path set `DataCenter.CurrentRotation = null` immediately before logging, which permanently defeated the unchanged-state guard at the top of the method and forced a full re-resolve every tick. (Updaters/RotationUpdater.cs)

### Changed
- `UpdateCustomRotation` now returns early when `DataCenter.IsPvP` is false, clearing `CurrentRotation`/`CurrentRotationActions` once on the PvP-to-PvE transition instead of every frame. `curCombatType` is consequently always `CombatType.PvP` below that guard.
- Removed the ungated `PluginLog.Information($"UpdateCustomRotation: job=..., IsPvP=..., groups=...")` call. It ran unconditionally on every invocation, ahead of every early return - invisible to users filtering at Warning+, but hammering `dalamud.log` in PvP and PvE alike.
- Both remaining "no rotation" warnings (`no rotation found for job=...` and `No valid rotations found for ...`) are now throttled by a `_lastNoRotationLogged` (job, combat type) tuple, so a persistent miss reports once rather than once per frame. Reset in `ChangeRotation` so a later successful load re-arms the warning.

### Notes
- **Shipped to production with 0.1.0.12 (2026-08-09).** In-game gate waived by Joey: every changed statement executes only when `DataCenter.IsPvP` is false, so in-PvP behavior is provably unchanged; the empirical check is that the PvE `no rotation found` warning stops firing (issue #3).
- Both log lines date to the initial vendored commit `9b3652dae`, so this affected every release to date.
- No rotation, targeting or combat behavior changes - the end state in PvE is identical (`CurrentRotation == null`), it is just no longer recomputed and re-logged every tick.

## [0.1.0.10] - 2026-07-31
### Changed
- Synced upstream RotationSolverReborn commit `2ac940563`: the `EmergencyGCD` hook now takes the queued next GCD as a parameter, bringing it in line with `EmergencyAbility`, interrupts, dispels and the heal hooks, which have all taken it for some time. Applied to the base engine (`PvPSolver.Basic/Rotations/CustomRotation_GCD.cs` declaration + both call sites, `PvPSolver.Basic/Rotations/Duties/DutyRotation.cs` declaration) and to the two rotations that override it (`RebornRotations/PVPRotations/Healer/WHM_Default.PVP.cs`, `RebornRotations/PVPRotations/Ranged/BRD_Default.PVP.cs`).

### Notes
- **No gameplay change.** The new `nextGCD` parameter is not read by any implementation of the hook — not in this fork, and not anywhere in upstream `641bd792e`. It is plumbing for future upstream use. Purify (stun / heavy / bind / silence / deep freeze / Miracle of Nature), Guard, Recuperate and Standard-issue Elixir priority are unchanged, as are the WHM Aquaveil and BRD The Warden's Paean cleanse paths.
- `WHM_Default.PVP.cs` and `BRD_Default.PVP.cs` are now byte-identical to upstream `641bd792e` (verified by diff, CR-normalised).
- First release on the **reinstated testing channel**. The channel was removed 2026-06-07; its previous entry never worked because `TestingDalamudApiLevel` was unset, so Dalamud hid the build. Now set to 15 alongside `TestingAssemblyVersion`. Production remains 0.1.0.9 until this build is verified in-game.
- Escalated by the nightly upstream-merge on 2026-07-31 under RUNBOOK 3.2 ("never pull base classes"). RUNBOOK amended the same day with a signature-only carve-out so equivalent changes sync unattended.

## [0.1.0.9] - 2026-07-02
### Changed
- Hidden the three PvE-only config tabs inherited from upstream: Duty Rotation, Duty, and AutoDuty (incl. the AutoDuty helper-plugin installer list). Tabs are `[TabSkip]`-hidden, not deleted, so nightly subtree merges stay clean. (UI/RotationConfigWindowTab.cs)
- Removed upstream Combat Reborn donation/community links: Ko-fi title-bar button, About-tab Ko-fi button and Discord banner, and the "Thanks to Supporters" About section with its random supporter shout-out hints. (UI/RotationConfigWindow.cs)
- Easter egg window retitled "PvP Solver Lab" (was "RSR Lab"). (UI/EasterEggWindow.cs)

### Notes
- Part of the 2026-07-02 fork-branding cleanup pass across lalalazy forks. No rotation/behavior changes.
- The Actions-tab "Intercepted" option is PvE-only by nature and remains visible; candidate for a later pass.

## [0.1.0.8] - 2026-06-18
### Changed
- Skip loading PvE duty rotations (Bozja, Emanation, MonsterHunter, Orbonne, Phantom, Variant) entirely. PvPSolver is PvP-only, so the duty rotation set had zero relevance; not loading it reduces memory use and startup time. (Updaters/RotationUpdater.cs)

## [0.1.0.7] - 2026-06-07
### Added
- Auto-targeting for Forlorn / Maiden targets: checks the global `Svc.Objects` list to target any targetable, alive Forlorn/Maiden immediately upon rendering, regardless of whether they are in the hostile list.
- Increased `sipRange` in `ObjectHelper.cs` to 48f (from 25f) to ensure they are targeted immediately upon entering the player's hostile radar range.

## [0.1.0.5] - 2026-06-04
### Changed
- Rebranded and updated all remaining user-facing references to `RSR` and `/rotation` (such as in settings tooltips, enum descriptions, and compatibility diagnostics) to `PvP Solver` and `/pvpsolver`.

## [0.1.0.4] - 2026-06-04
### Changed
- Rebranded and updated all leftover RSR and `/rotation` command references in the UI, settings, and first-start tutorial to PvP Solver and `/pvpsolver` to avoid user confusion.
- Disabled PVE incompatible-plugins download task, preventing the 404 orange warning banner in the UI.
- Updated EzIPC prefix to `PvPSolver` and `PvPSolver.ActionUpdater`, preventing side-by-side IPC collisions.

## [0.1.0.3] - 2026-05-24
### Added
- Wholly Original Rebrand: Renamed core slash commands to `/pvpsolver` and `/pvs` to prevent command collisions.
- Built-in out-of-combat/no-enemy cast protection in core action checking logic (blocks all combat abilities in PvP if no enemies are within 40 yalms).
