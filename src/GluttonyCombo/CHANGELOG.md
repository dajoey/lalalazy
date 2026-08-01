## v1.0.4.100 (2026-08-01) [testing]

### Added
- **Phantom Job support for all eight jobs added in patch 7.55** — Ninja, White Mage,
  Black Mage, Dragoon, Summoner, Blue Mage, Red Mage and Necromancer are now driven by
  autorotation inside Occult Crescent. New files
  `Combos/PvE/Content/OccultCrescent/OccultCrescent_755.cs` (rotations, action IDs, status
  IDs) and `OccultCrescent_755_Weakness.cs` (elemental weakness support). 47 new presets
  in the reserved range 110090-110136.
- **Elemental weakness gating.** Several 7.55 actions only pay off against a target
  carrying the matching weakness debuff. `TargetWeakTo()` checks the live debuff first
  (as revealed by Phantom Red Mage's Occult Libra, action 49094) and falls back to a
  112-entry mob-nameId table. New `Phantom755_RequireWeakness` preset (default on) lets
  users disable the gate and fire elemental actions on cooldown instead.
  The weakness table is adapted from FFXIV-CombatReborn/RotationSolverReborn
  (`StatusHelper.OccultWeaknessByNameId`, commits `2ac940563`..`443f4e0be`).

### Fixed
- **`OccultCrescent.JobIDs` enum corrected for 7.55.** The pre-7.55 placeholder ordering
  was a guess and was wrong. Verified against the game's own `MKDSupportJob` sheet, where
  the row id *is* the `SupportJob` index: Ninja 16, White Mage 17, Black Mage 18,
  Dragoon 19, Summoner 20, Blue Mage 21, Red Mage 22, Necromancer 23. Previously the file
  claimed Summoner 17 / Black Mage 18 / Red Mage 19 / Blue Mage 20 / White Mage 21 /
  Dragoon 22. Removed `BeastMaster` and `Mime` entirely — the sheet has exactly 24 rows
  (0-23) and neither job exists. This affected `CurrentJobLevel`, which indexes
  `State.SupportJobLevels[State.CurrentSupportJob]` through this enum, and the job icons.

### Notes
- **This is a deliberate stopgap.** Upstream Wrath Combo had shipped no 7.55 phantom job
  support as of 2026-08-01 (`wrathcombo/main` @ `96feb63e7`). When it does, this work
  should be dropped and we realign on upstream. Rip-out procedure is documented in the
  header of `OccultCrescent_755.cs`: delete two files, delete preset range 110090-110136,
  delete one dispatch call, delete the config sliders, revert the enum.
- All action and status IDs were datamined from the live 7.55 sqpack on 2026-08-01, not
  copied from another plugin. Action block is contiguous at 49062-49101; Phantom Job
  statuses at 5328-5335; elemental weakness statuses at 5322-5325.
- **Action names are not unique.** `Occult Cure II` is action 49067 on White Mage and
  49093 on Red Mage. Everything here is keyed by explicit ID; do not refactor to
  name-based lookup.
- Phantom Summoner's Thunderstorm is gated on **Wind** weakness, not Lightning, despite
  the name. RotationSolverReborn shipped it as Lightning and hotfixed it in `731446871`.
- Necromancer's Deep Freeze is deliberately not wired to status 4150 ("Deep Freeze"),
  which predates 7.55 and is unrelated. Left unlinked pending in-game confirmation.
- Blue Mage's Occult Aero (49085) auto-upgrades to Aero II (49089) / Aero III (49091) by
  trait and is resolved through `OriginalHook`. Neither upgrade is separately equippable.

## v1.0.4.99 (2026-07-30)

### Changed
- **`/gluttony buff` promoted to production** (`Combos/PvE/Content/OccultCrescent/OccultCrystalBuffs.cs`): removed the per-attempt `[CrystalBuffs]` Dalamud-log diagnostics (GetActionStatus probes + UseAction return logging) that v1.0.4.98 carried for the cast-path investigation, now that the rework is confirmed working in-game. Behavior is otherwise unchanged: hook-bypassed dual cast path, verified buff application, strict phantom-status job confirm, skip-if-fresh, and the chat progress/summary messages all remain.

## v1.0.4.98 (2026-07-29)

### Fixed
- **`/gluttony buff` cast path fully reworked** (`Combos/PvE/Content/OccultCrescent/OccultCrystalBuffs.cs`, `Data/ActionWatching.cs`) — v1.0.4.86-96 could cycle all four Phantom Jobs and restore the original without a single buff landing. Root-cause hardening, in order of suspicion:
  - **Casts now bypass GluttonyCombo's own `UseAction` detour.** New `ActionWatching.UseActionRaw()` invokes the game's `UseAction` via `UseActionHook.Original`, so the plugin's combat gating (`PlayerHasActionPenalty` hard-block, retargeting, queue handling in `UseActionDetour`) can never silently swallow the out-of-combat crystal casts. `ChangeSupportJob` is a native call that never passed through the hook — which is exactly why jobs kept switching while casts died.
  - **Dual cast path per buff.** Primary: `ActionType.GeneralAction` phantom slot (Knight/Pray 32, Monk/Counterstance 33, Bard/Romeo's Ballad 32, Dancer/Quickstep 32) — verified against BOCCHI's working Buff module and the live 7.5x GeneralAction sheet (rows 31-35 remain "Phantom Action I-V"). Fallback: `ActionType.Action` with the real Action-sheet ids (Pray 41589, Counterstance 41597, Romeo's Ballad 41609, Quickstep 46603), explicit self-target — how RotationSolverReborn and our own AutoRotation cast phantom actions. 3 attempts each, 800ms apart, 10s per-job cap.
  - **Success is verified, not assumed.** A cast counts only when the buff status appears/refreshes past the pre-cast snapshot (+60s), replacing the brittle ">=1780s fresh" check. Removed the `GetRecastTime - Elapsed <= 0` gate that could suppress every attempt; the client rejects unusable actions itself and the retry ladder handles it.
  - **Strict job-change confirm.** Phantom-job status first (PhantomKnight 4358 / Monk 4360 / Bard 4363 / Dancer 4805); the `CurrentSupportJob` state byte only counts after holding 1.5s (it can lead the server). 600ms post-confirm settle before casting.
  - **No more force-targeting the crystal** (BOCCHI parity; buffs are self/party casts and an EventObj hard target is at best useless). Jobs whose buff already has >=25min left are skipped without swapping. End-of-cycle summary reports N/M buffs applied.
  - **Full diagnostics.** Every attempt logs `GetActionStatus` + the `UseAction` return to the Dalamud log under `[CrystalBuffs]` — if a cast still fails, `/xllog` now states the client's exact rejection code instead of requiring another blind test cycle.

### Notes
- The v1.0.4.96 claim that `ActionType.Action` 41xxx phantom casts are "silently rejected by the client" did not survive source review — RotationSolverReborn and Wrath AutoRotation cast phantom actions that way in-game. Both mechanisms are retained; whichever lands first wins.

## v1.0.4.97 (2026-07-29)

### Changed
- **Merged upstream Wrath Combo through `1b984ff00` (upstream 1.0.4.19).** Net delta 35 files, +626/-753. Reconciled via per-file 3-way against base `2072ad38d` with the WrathCombo->GluttonyCombo rename transform: 19 pure-rename (took upstream), 14 clean 3-way, 1 new upstream file (`Extensions/ObjectTableExtensions.cs`), 1 conflicted file (`CustomCombo/Functions/Action.cs`).
- Upstream job-rotation updates across SAM, NIN, MNK, MCH, RPR, DRG, BLM, VPR, SGE, WHM (SAM_Helper/MNK_Helper largest reworks: Throwing Daggers, Fleeting Raiju melee-range gating, opener fixes; BattleData updates).

### Fixed
- **Action-history refactor absorbed** (`Data/ActionWatching.cs`, `CustomCombo/Functions/Action.cs`): upstream changed `CombatActions` to store `CombatAction` objects; item usage now attributes to the acting player and weave/`ActionCount`/`WasLastAction` read `.ActionID`. Our tree already carried the object form, so the 2 conflicts in `Action.cs` resolved to ours for compile-consistency with the surrounding `action.ActionID` sites; upstream's new `ActionSheet.TryGetValue` null-guard merged in cleanly.

### Preserved (local divergences carried across the merge)
- Amnesia/Pacification/Silence gating (`AmnesiaStatusIds`/`HasAmnesia`), 15s raidwide-mitigation gate + `RaidwideTimeRemaining()` cast-bar timer, WHM Divine Caress ground-heal targeting, SMN Aegis Uptime, BLU autorotation engine, OccultCrescent phantom-job buff automation.

### Notes
- Nightly upstream-merge run 2026-07-29; build clean (0 errors). Safe forks (Dagobert, LazyWTMath, PvPSolver) were no-ops this run.

## v1.0.4.96 (2026-07-28)

### Fixed
- **`/gluttony buff` ran its waits but never cast anything** (`Combos/PvE/Content/OccultCrescent/OccultCrystalBuffs.cs`): buff actions were invoked via `ActionManager.UseAction(ActionType.Action, <41xxx Action-sheet ID>)`, which the client silently rejects for phantom job abilities — the state machine waited its delays and moved on with no cast ever firing. Phantom hotbar abilities must be cast via `ActionType.GeneralAction` with per-slot GeneralAction row IDs (31-34), exactly like pressing the phantom hotbar buttons. Slot map: Knight Pray = 32, Monk Counterstance = 33, Bard Romeo's Ballad = 32, Dancer Quickstep = 32. Mechanism verified against BOCCHI's Buff module (github.com/OhKannaDuh/BOCCHI v2.1.2), which performs this same cycle in-game.
- **Job-change confirmation** (`OccultCrystalBuffs.cs`, `WaitForJobChange`): now waits for the Phantom Job status (PhantomKnight 4358 / PhantomMonk 4360 / PhantomBard 4363 / PhantomDancer 4805) in addition to the `CurrentSupportJob` state byte, matching how the server signals a completed support-job change; 400ms post-change settle retained.
- **Buff confirmation** (`OccultCrystalBuffs.cs`, `CastBuff`): advancing now requires the buff status present AND freshly applied (`RemainingTime >= 1780` of 1800s). Casts retry every 500ms but only when the GeneralAction is off recast (`GetRecastTime - GetRecastTimeElapsed <= 0`), replacing the blind 400ms re-spam. Per-job DuoLog progress lines added so each applied buff is visible in chat.

### Changed
- Per-job timing retuned: 5s job-change timeout (was 3s), 10s per-job cast cap (was 2s blind window), 800ms inter-job settle (was 1800ms); overall sequence timeout raised 60s -> 120s to fit worst-case retries across 4 jobs.

### Notes
- Status IDs unchanged and re-verified against BOCCHI `Data/PlayerStatus.cs`: EnduringFortitude 4233, Fleetfooted 4239, RomeosBallad 4244, QuickerStep 4799.
- Diagnosis source: Antigravity conversation `84b8c16b-8957-41bc-8137-1eacfa4a5ec1` (brain artifacts + 685-step trajectory) plus decompilation/source review of BOCCHI 2.1.2.

## v1.0.4.85 (2026-07-28)

### Changed
- **Synced upstream WrathCombo `b4e7f972f` -> `2072ad38d`** (1 commit, "Fix issue when using items with number of GCDs used" by Taurenkey; upstream csproj stays 1.0.4.18). Fork lineage 1.0.4.84 -> 1.0.4.85.
- **Action tracking rework** (`Data/ActionWatching.cs`): `CombatActions` changed from `List<uint>` to `List<(uint ActionID, ActionType ActionType)>`. `LastAction` now only updates for `ActionType.Action` (items no longer overwrite it); the use-counter and `NumberOfGcdsUsed` compare on the tuple so item usage no longer inflates the GCDs-since-combat count openers depend on; the Spell/Weaponskill/Ability category switch + timestamp/heal-throttle bookkeeping is gated under `ActionType.Action`. `OutputLog()` is now argument-less and switches on the recorded `ActionType`.
- **Null-safe attack-type lookup** (`Extensions/UIntExtensions.cs`): `ActionAttackType(this uint)` now uses `ActionSheet.TryGetValue(...)`, returning `0` for unknown IDs instead of indexing (avoids a throw on unmapped action IDs).
- **Debug tab** (`Window/Tabs/Debug.cs`): `DrawStatuses` is now `unsafe` and prints target status Count / NumValid / StatusCapped diagnostics (developer view only; no gameplay effect).

### Merge method
- Per-file 3-way (`git merge-file`, RUNBOOK 3.3) vs WrathCombo-namespace base/theirs blobs, LF-normalized, token-protected forward-rename. **3 files, 0 conflicts.** Diff vs current repo == upstream delta exactly.

### Preserved (fork divergences, token-count verified post-merge)
- `ActionWatching.cs`: PlayerHasActionPenalty 2 (Pyretic/Bomb hard-block), WouldLikeToGroundTarget 2 (WHM ground-heal tank-centering), GluttonyCombo.P 2, WrathOpener 3 - all unchanged; upstream tuple refactor landed with our divergences untouched (they sit clear of the changed regions).
- `Debug.cs`: "Gluttony IPC" / "Gluttony Leased" branding + WrathIPCCallback intact.
- BLU engine, SMN Aegis Uptime, WHM raidwide/ground-heal, 15s raidwide gate, Amnesia/Pacification/Silence untouched this range.

### Notes
- BOM-less LF output (RUNBOOK 9). No `.resx` touched. 0 residual bare-`WrathCombo` tokens in output. Verified `CustomComboFunctions.TargetIsStatusCapped` / `SafeStatusList` / `StatusManager.NumValidStatuses` already present in the fork before merging the new Debug references.

## v1.0.4.84 (2026-07-27)

### Changed
- **Synced upstream WrathCombo `aede233c6` -> `b4e7f972f`** (7 commits; upstream csproj stays 1.0.4.18). Fork lineage 1.0.4.83 -> 1.0.4.84. Commits: `92a7e0751` countdown check, `c1c8561b1` DNC prepull delays, `65b150d17` DNC opener updates (pot-on-cooldown fix), `3f68c0a6a` DNC refinements (delay -> float), `b455e8147` NIN adjusted-action update, `3621ebf27` back-to-back skip handling, `b4e7f972f` more skip safety.
- **DNC** (`Combos/PvE/DNC/DNC_Helper.cs`, `DNC_Config.cs`): opener/prepull delay refinements; prepull delay type changed to float for tighter accuracy; fixed the opener consuming a step when the potion is on cooldown.
- **NIN** (`Data/ActionWatching.cs`): mudra anti-rabbit replacement lookup switched from manual `LastActionInvokeFor` dictionary probing to `actionManager->GetAdjustedActionId(...)`.
- **Openers** (`CustomCombo/WrathOpener.cs`): better handling of back-to-back skipped steps + additional skip safety.
- **MCH / RDM / SAM** helper refinements taken from upstream (`MCH_Helper.cs`, `RDM_Helper.cs`, `SAM_Helper.cs`).
- **Items** (`AutoRotation/AutoRotationController.cs`): added a `Svc.Log.Debug` line when an item is used; `Combos/PvE/ALL/Items.cs` minor trim; `CustomCombo/Functions/Timer.cs` tweak.

### Merge method
- Per-file 3-way (`git merge-file --diff3`, RUNBOOK 3.3) vs WrathCombo-namespace base/theirs blobs, LF-normalized, token-protected forward-rename. **10 files, 0 conflicts.** Diffstat matches upstream delta exactly (+58/-55).

### Preserved (fork divergences, token-count verified post-merge)
- `AutoRotationController.cs`: Pacif 2, Silence 2, Amnesia 2, Pyretic 5, Reflect 4, PlayerHasActionPenalty 3, Raidwide 70, DivineCaress 4, WouldLikeToGroundTarget 13 - all unchanged.
- `ActionWatching.cs`: Pyretic 2, PlayerHasActionPenalty 2, Raidwide 3, WouldLikeToGroundTarget 2, GluttonyCombo.P 2 - all unchanged.
- BLU engine, SMN Aegis Uptime, WHM raidwide/ground-heal, 15s raidwide gate untouched this range.

### Notes
- BOM-less LF output throughout (RUNBOOK 9). No `.resx` touched this range. 0 residual `WrathCombo` tokens in output.

## v1.0.4.83 (2026-07-26)

### Changed
- **Synced upstream WrathCombo `8f3924ee5` -> `aede233c6`** (1 commit, "Make NIN anti-rabbit optional"; upstream csproj stays 1.0.4.18). Fork lineage 1.0.4.82 -> 1.0.4.83.
- **NIN "Anti-Rabbit" mudra protection is now opt-in.** New preset `NIN_Anti_Rabbit` (id 10056, "Anti-Rabbit Option"). The `InMudra` rabbit-guard in `Combos/PvE/NIN/NIN_Helper.cs` and the mudra queue-clear guard in `Data/ActionWatching.cs` are now gated behind `IsEnabled(Preset.NIN_Anti_Rabbit)` instead of firing unconditionally.
- Removed a dead commented-out mudra guard in `ActionWatching.CanQueueActionDetour`.

### Added
- Localization `NIN_Anti_Rabbit_Name` / `NIN_Anti_Rabbit_Desc` (resx + Designer accessors).

### Merge method
- Per-file 3-way (`git merge-file`, RUNBOOK 3.3) vs WrathCombo-namespace base/theirs blobs, LF-normalized, plain forward-rename (0 protected / `.API` / `.JobID` tokens in the 5 touched files). **5 files, 0 conflicts.** `.resx` handled by additive `<data>` injection with XML validation (kept all fork entries).

### Preserved (fork divergences, verified post-merge)
- `ActionWatching.cs`: Pyretic hard-block + `PlayerHasActionPenalty` (2), ground-heal `WouldLikeToGroundTarget` (2), `GluttonyCombo.P` IPC qualifier (2). `CustomComboPreset.cs`: BLU_AutoRotation (2), SGE_TankShield, SMN RadiantMaintain (2), WHM_Raidwide_Medica. `.resx`: "In Gluttony Settings" branding (2) + all SMN / BLU / WHM / SGE entries. Untouched this range: Amnesia / Pacification / Silence / Reflect / Divine Caress / SMN Aegis / 15s raidwide gate / BLU engine.

### Notes
- BOM-less LF output throughout (RUNBOOK 9).

## v1.0.4.82 (2026-07-25)

### Changed
- **Synced upstream WrathCombo `cb50b6040` -> `8f3924ee5`** (12 commits, upstream csproj 1.0.4.16 -> 1.0.4.18). 11 code files merged, 0 added, 0 deleted. Fork lineage bumps 1.0.4.81 -> 1.0.4.82.
- **NIN:** Ten Chi Jin now bypasses simple-mudra remapping in both mudra paths (upstream "simple mudras fix" + "More NIN refinements and fix IPC").
- **VPR:** out-of-range handling - `CanVicewinderCombo` gains `preferRangedWhenOor`; Vicewinder ST combo prefers ranged uptime when Uncoiled Fury / Ranged Uptime enabled; Writhing Snap gated on melee-range only; opener + one-button-checker flow tidied ("VPR gonna VPR", "fix VPR OOR", "range checks").
- **MNK:** chakra usage in the opener fixed ("fix chakra in opener").
- Minor upstream cleanup to `Data/ActionWatching.cs`, `Services/IPC/Leasing.cs`, `Services/IPC/Search.cs`, `Window/Tabs/Debug.cs`.

### Merge method
- Per-file 3-way (`git merge-file`, RUNBOOK 3.3) against WrathCombo-namespace base/theirs blobs, LF-normalized, token-protected forward-rename. **7 passthrough, 4 clean 3-way, 0 conflicts.**
- Upstream `WrathCombo.csproj` (version/branding = ours) NOT pulled.

### Preserved (fork divergences, token-count verified vs pre-merge)
- Diverged touched files preserved every local token: `ActionWatching.cs` Pyretic (2) + PlayerHasActionPenalty (2); `Leasing.cs` SuspendLeases (1); `Debug.cs` BattleData (7) + SuspendLeases (1). `"WrathCombo.json"` config-path literal intact. Untouched this range: Amnesia / Pacification / Silence / Reflect / Divine Caress / SMN Aegis / 15s raidwide gate / SetMaxDistanceToTarget / BLU engine.

### Notes
- BOM-less LF output throughout (RUNBOOK 9). No `.resx` touched this range.

## v1.0.4.81 (2026-07-23)

### Changed
- **Synced upstream WrathCombo `ad2493662` -> `cb50b6040`** (63 commits, upstream csproj 1.0.4.14 -> 1.0.4.16). 84 files merged, 1 added (`Combos/PvE/ALL/Items.cs`), 1 deleted (`Native/CustomActionWindow.cs`). Fork lineage bumps 1.0.4.80 -> 1.0.4.81.
- **Upstream job-rotation tuning** across BRD (standard-opener delayed-weave/skip fix), DRK (opener fix + large `DRK_Config` expansion + `DRK_ActionLogic`), MCH, NIN (TCJ queue), RPR, DNC, PCT, VPR, SAM, PLD, AST (class->job for cards), MNK, SGE, SCH, SMN.
- **New item/potion system.** `Combos/PvE/ALL/Items.cs` added; potion configs wired up; `ALL.cs` updated. "AoE manual ignore" option added to the AutoRotation UI.
- **Custom Action reliability.** Upstream retired `Native/CustomActionWindow.cs` (folded into `CustomActionManager`), added reload/hover crash guards, and fixed queueing the wrong action on overwrite. Pronoun service gutted upstream.

### Merge method
- Per-file 3-way (`git merge-file`, RUNBOOK 3.3) against WrathCombo-namespace base/theirs blobs, LF-normalized, token-protected forward-rename. 63 passthrough, 21 real 3-way, 1 add, 1 delete.
- **3 conflicts, all hand-resolved:**
  - `Data/ActionWatching.cs` (2): (a) `OnActionUsedProvider.SendMessage` -> took upstream's cast removal (`actionType` is already `ActionType`), dropping a spurious `GluttonyCombo.P`-vs-`P` qualifier; (b) preserved our Pyretic / `PlayerHasActionPenalty(true)` hard-block at the top of the send detour while adopting upstream's restructured `ActionType.Action` CustomActions handling (`GetAdjustedActionId` + return-false-on-click), dropping our stale pre-restructure copy.
  - `Window/Tabs/AutoRotationTab.cs` (1): kept our in-place `UnTargetAndDisableForPenalty` checkbox and literal "Pause when no target" label; dropped upstream's relocated IPC-controlled duplicate.
- `docs/`, upstream `WrathCombo.csproj` (version/branding = ours) and `*.DotSettings.user` intentionally NOT pulled.

### Preserved (fork divergences, token-count verified vs pre-merge main)
- Amnesia (15), Pacification (4), Silence (11), Pyretic (18), Reflect (56), Divine Caress ground-heal (14), SMN "Aegis Uptime" (3), SetMaxDistanceToTarget (6), SuspendLeases (5), EnteringInstancedContent (3), RaidwideCasting (5), IsRaidwide (2), BattleData (36), PlayerHasActionPenalty (7). IPC-contract tokens intact (`WrathComboCallback` x4, `###WrathCombo` x2, `"WrathCombo.json"`). `GluttonyCombo.P` 133 -> 132 by design (one spurious qualifier resolved to bare `P`). BLU engine untouched (no upstream counterpart).

### Notes
- 8 merged `.resx` files validated as well-formed XML post-merge (RUNBOOK 3.3 split-`<data>` hazard). BOM-less LF output throughout.
- Build clean (0 `error CS`); embedded zip manifest, pluginmaster, and template json all set to 1.0.4.81 with this changelog.

## v1.0.4.80 (2026-07-22)

### Changed
- **Synced upstream WrathCombo `0519de6d5` -> `ad2493662`** (5 commits: PR #1235 `DbgStatuses`, PR #1224 `June`, `SafeStatusList`, "Added Status reads as part of reading target info", CODEOWNERS). Upstream csproj version unchanged at 1.0.4.14; our fork lineage bumps 1.0.4.79 -> 1.0.4.80. Only one code file lands in the fork: `Window/Tabs/Debug.cs`.
- **Debug tab status-display refactor.** The inline Player Statuses and Target Statuses draw loops were extracted into a shared `private static void DrawStatuses(IGameObject?)` helper that iterates `SafeStatusList` (null-safe), and a new "Statuses" `TreeNode` was added under the target debug tree. Upstream also dropped the old inline Target-Statuses `ICD Tracker` sub-header. Developer-facing diagnostic window only - no autorotation, targeting, or gameplay behavior change.

### Merge method
- Per-file 3-way (`git merge-file -p ours base theirs`, RUNBOOK 3.3) against the WrathCombo-namespace base/theirs blobs, LF-normalized. 0 conflicts. The three upstream hunks (base lines 240-338, 1469-1477, 1527-1532) carry no rename tokens and sit clear of every local divergence, so no forward-rename was required and all fork edits survived verbatim.
- `docs/CODEOWNERS` (upstream repo governance) intentionally NOT pulled - out of scope for the fork.

### Preserved (fork divergences carried through unchanged)
- `Debug.cs` local edits verified present post-merge: "Gluttony IPC" / "Gluttony Leased:" UI branding, `GluttonyCombo.P` qualification (22 sites), no-BOM + LF file conventions.
- All autorotation divergences untouched (this merge touches no rotation code): Amnesia / Pacification / Silence, Pyretic / Reflect penalties, 15s raidwide-mitigation gate, WHM Divine Caress ground-heal, SMN "Aegis Uptime", BattleData, BossMod IPC, BLU engine.

### Notes
- `SafeStatusList` confirmed pre-existing in `Extensions/GameObjectExtensions.cs` (not a new upstream symbol) - build-safe.
- Build clean (0 `error CS`); embedded zip manifest, pluginmaster, and template json all set to 1.0.4.80 with this changelog.

## v1.0.4.79 (2026-07-20)

### Changed
- **Synced upstream WrathCombo 1.0.4.14 (`93559998d`) -> `0519de6d5` (autorotperf, PR #1234)** - 13 commits, 14 files, +174/-110. Method: per-file 3-way in WrathCombo namespace + forward Wrath->Gluttony rename (RUNBOOK 3.3), token-protected `fwd` guarding the `WrathCombo.json` literal. `git merge-file`: 11 clean, 2 conflicts, 1 hand file.
- **Autorotation caching (`autorotperf`).** `Window/Functions/Presets.cs` `GetJobAutorots` now caches the computed job->autorotation dictionary (`field`-backed) and only rebuilds when `UpdateDue`, cutting per-frame recompute. Invalidation wired through `Core/Presets.cs` / `Core/ConfigurationChanges.cs`.
- **`Core/Presets.cs` `TogglePreset` converged to upstream** (delegates to `DisablePreset` instead of inline disable). Our only local delta here was `GluttonyCombo.P` qualification (no behavioral divergence) - took upstream.

### Fixed
- **Opener no longer resets on area transition** (upstream `c8db6f681`). The `WrathOpener.CurrentOpener` reset moved out of the unconditional top of `UpdateCaches` into the `if (onJobChange || firstRun)` guard - reconciled onto our fork's restructured `UpdateCaches` (keeps the early `SelectOpener()` and the role-based `SetMaxDistanceToTarget` block).
- **NIN** (`NIN.cs` / `NIN_Helper.cs`): better prevents queueing duplicate mudras on bad ping.
- **Ghimlyt Dark battle data** fix (`BattleData_5.0_ShB.cs`).

### Added
- **Vauthry invulnerability** entries; Custom Actions window resized (`Native/CustomActionManager.cs`).
- Debug tab party info now sourced from the group manager (`Window/Tabs/Debug.cs`).

### Preserved (fork divergences carried through unchanged, token counts verified >= pre-merge)
- Amnesia / Pacification / Silence handling, Pyretic / Reflect penalties (`EnemyHasReflectPenalty`), 15s raidwide-mitigation gate, `IsRaidwide` / `IgnoreRaidwide`, WHM Divine Caress ground-heal targeting, SMN "Aegis Uptime" preset, BattleData subsystem, BossMod IPC (`SetMaxDistanceToTarget` / `SuspendLeases`), `EnteringInstancedContent` tracking. BLU autorotation engine untouched this merge (upstream has none).

### Notes
- Build: 0 errors, 11 pre-existing warnings, 12.6s. LF output per RUNBOOK §9. `"WrathCombo.json"` config literal preserved via token-protected rename.

## v1.0.4.78 (2026-07-17)

### Added
- **Upstream BattleData subsystem** (`Data/BattleData/BattleData.cs` + per-expansion
  `BattleData_2.0_ARR` through `BattleData_7.0_DT`). Curated per-encounter action-ID tables for
  tankbusters, raidwides, ignore-raidwides (gazes) and invulnerability, exposed via
  `PauseActions()` / `IsRaidwide()` / `IgnoreRaidwide()` / `IsTankbuster()` / `IsInvincible()`.
  Loaded on territory change.

### Changed
- **Synced upstream WrathCombo 1.0.4.13 (`efe5d828b`) to 1.0.4.14 (`93559998d`)** — 68 commits,
  61 files, +2794/-1942. Method: per-file 3-way in WrathCombo namespace + forward Wrath->Gluttony
  rename (RUNBOOK 3.3). git merge-file reported 0 conflicts; the two escalation-flagged files
  converged cleanly (see Notes).
- **`RaidwideCasting` (`CustomCombo/Functions/Action.cs`) converged with upstream.** Upstream's
  1.0.4.14 `RaidwideCasting` already ORs our cast-bar heuristic (`CastType 2/5 && EffectRange >= 30`)
  with `BattleData.IsRaidwide(id)` and adds a `BattleData.IgnoreRaidwide(id)` gaze filter. Our 15s
  raidwide-mit gate and `RaidwideTimeRemaining()` are unchanged.
- **`PlayerHasActionPenalty` (`CustomCombo/Functions/Status.cs`) rearchitected onto BattleData.**
  Adopted upstream's new signature `PlayerHasActionPenalty(bool fromAutorot)`; encounter-specific
  detection (e.g. Clyteum motion-scanner) now lives in `BattleData.PauseActions()`, with the
  AccelerationBomb / Pyretic / Misc status scan retained as the fallback branch. Our divergent call
  sites in `AutoRotation/AutoRotationController.cs` (x2) and `Data/ActionWatching.cs` now pass
  `fromAutorot: true` (matches upstream's sole call site).

### Preserved (fork divergences carried through unchanged, token counts verified vs pre-merge)
- Amnesia self-lockout (`AmnesiaStatusIds [5,1092,4210]`), Pacification / Silence handling,
  Pyretic / Reflect penalties (`EnemyHasReflectPenalty`), WHM Divine Caress ground-heal targeting,
  SMN "Aegis Uptime" preset, BossMod IPC (`IsAIActive` / `SetMaxDistanceToTarget`), 15s raidwide gate.

### Notes
- BLU taken-theirs (unprotected per Joey 2026-07-02; BLU autorotation is known-broken).
- Build: 0 errors, 11 warnings (all pre-existing). Resolves the 2026-07-15 nightly-upstream-merge
  escalation (upstream BattleData penalty rearchitecture vs. our divergences) — merged cleanly with
  every standing divergence intact.

## v1.0.4.77 (2026-07-11)

### Changed
- **Auto Positionals now skips when your target is targeting you.** When the
  "Auto Positionals (Melee DPS)" option is enabled, `PositionalMover.MoveToPositional`
  now returns early if the current target has the local player as its target
  (`battleTarget.TargetObjectId == Player.Object.GameObjectId`). A mob focused on you
  rotates to face you as you reposition, so the flank/rear can never be reached and the
  mover would otherwise just circle-strafe it. It now holds position and lets you attack
  from the front. Complements the existing guards (True North, omnidirectional targets,
  BossMod AI, active player movement input). `AutoRotation/PositionalMover.cs`.

## v1.0.4.76 (2026-07-05)

### Added
- **Amnesia handling (statuses 5, 1092, 4210 — "unable to use abilities").** Eureka Orthos /
  deep-dungeon floor enchantments and traps apply Amnesia (1092), disabling all oGCD
  abilities; both rotation modes previously kept trying to use them and stalled.
  - Auto-rotation: `ProcessAutoActions` skips `ActionAttackType.Ability` actions while any
    Amnesia status is present. `AutoRotation/AutoRotationController.cs`.
  - Manual (button-press) combos: `CanWeave`/`CanDelayedWeave` return false and `ActionReady`
    rejects ability-type actions under Amnesia, so combos fall through to GCDs globally. New
    `HasAmnesia` helper + `AmnesiaStatusIds` in `CustomCombo/Functions/Action.cs`;
    `Amnesia = 5` added to `ALL.Debuffs` (`Combos/PvE/ALL/ALL.cs`).

### Fixed
- **Pacification semantics were crossed since v1.0.4.23.** In-game, Pacification (status 6)
  blocks *weaponskills*; the v1.0.4.23 code skipped *abilities* under Pacification (Amnesia's
  rule keyed on the wrong status). Auto-rotation now skips weaponskill-type actions under
  Pacification. `AutoRotation/AutoRotationController.cs`.

### Notes
- Combos that return oGCDs without going through `CanWeave`/`ActionReady` are not covered by
  the global gates. Report any job that still stalls on an Amnesia floor.

## v1.0.4.75 (2026-07-02)

### Changed
- **Fork-branding cleanup (user-facing only).** All user-visible "Wrath" references now say
  "Gluttony": Settings tab strings (`SettingsCfgUI*.resx`, en/ja/ko/zh), conflict notices
  ("Gluttony cannot work in this state", "Conflicting Gluttony" header in
  `Data/Conflicts/ConflictingPlugins.cs` + `Conflicts.cs`), MainWindow conflict tooltip
  (`MainWindowUI*.resx`), "(In Gluttony Settings)" retarget hints (PvP job files +
  `CustomComboPresets*.resx`), Debug tab "Gluttony IPC"/"Gluttony Leased" labels.
  Internal identifiers (WrathOpener, `###WrathCombo` ImGui IDs, WrathCombo.API project) are
  intentionally untouched to keep nightly upstream merges clean. "Primal Wrath" (WAR action)
  untouched.
- **Login MOTD no longer fetched from upstream.** `PrintMotD` previously pulled and printed
  `PunishXIV/WrathCombo/main/res/motd.txt` (Wrath's news feed) to chat; now prints a local
  "Welcome to GluttonyCombo vX" line only. `GluttonyCombo.cs`.
- **IPC kill-switch repointed to this repo.** `Services/IPC/Helper.cs` `IPCStatusEndpoint`
  previously read `ipc_status.txt` from the upstream PunishXIV repo, meaning upstream could
  remotely disable Gluttony's IPC (which LazyFateAutomation depends on). Now reads
  `dajoey/lalalazy/main/res/ipc_status.txt` (new file, contents `enabled`). Fetch failure
  still defaults to enabled.
- **About tab de-Punished.** Replaced ECommons `PunishGui.AboutTab` (Punish branding/links)
  with a fork credit line + GitHub repo button. Kept the alexisoffline custom-action icon
  credit. `Window/ConfigWindow.cs`.
- **Debug dump renamed.** `WrathDebug.txt` -> `GluttonyDebug.txt` (`DebugFile.cs`,
  `Commands.cs`).

### Notes
- Part of the 2026-07-02 fork-branding cleanup pass across all lalalazy forks.
- No rotation/behavior changes.

## v1.0.4.74 (2026-07-02)

### Added
- **Custom Actions** (upstream 1.0.4.10-1.0.4.13): native hotbar action UI with drag/drop slots, per-action icon overrides, and the `/gluttony customactions` command. New `Native/CustomActionManager.cs`, `Native/CustomActionWindow.cs`, `Window/Tabs/CustomActions.cs`, plus 4 targeting-mode icons shipped in the package.
- **OpCode-based health-tick detection** (`Core/OpCodeConfig.cs`) - DoT logic no longer drops targets at 0 HP from natural regen ticks.
- **BLU broken warning**: prominent red banner on the BLU job page (`Window/Messages/Messages.cs`) and `*** CURRENTLY BROKEN - DO NOT USE ***` prefixes on both BLU Auto-Rotation preset descriptions. BLU auto-rotation is known non-functional in this release.

### Changed
- **Merged upstream WrathCombo 1.0.4.9 -> 1.0.4.13** (~180 commits, 78 files): full DRG rewrite, VPR rewire, MNK Perfect Balance/burst rework + opener, MCH hypercharge/tools/hotshot splits, RPR fixes (soul overcap, Soul of Death refresh, custom-action brick), NIN Buff Rush opener, healer retargeting fixes (WHM/SGE/SCH/AST), plus BLM/SAM/BRD/DNC/DRK/GNB/PLD/PCT/RDM/SMN/WAR updates and BossMod/BMR autorotation-conflict checks (`Data/Conflicts/*`, `Services/IPC_Subscriber/BossMod.cs`).
- **WHM Liturgy of the Bell** retarget now uses the replaced action (upstream fix) while keeping our RaidwideMedica timing hooks (`Combos/PvE/WHM/WHM.cs`).

### Notes
- All fork divergences preserved: healer raidwide shield/regen system (v1.0.4.55-.73), Pacification/Silence handling, Pyretic/action-penalty hard-block, HP-scaled raidwide gate, SMN Aegis Uptime, Gluttony IPC lease API (`SetMaxDistanceToTarget`/`IsAIActive` retained alongside upstream's reworked BossMod IPC).
- motd URL restored to upstream `PunishXIV/WrathCombo` (previous fork rename had pointed it at a nonexistent repo).

## v1.0.4.73 (2026-07-01)

### Changed
- **STABLE PROMOTION of the healer raidwide rework (testing v1.0.4.55-.72).** Everything validated by Joey in live play: SGE shield-first Eukrasian Prognosis + one mit per raidwide (hard intention-lock, v65), SGE tank-shield upkeep, SCH Succor commit-latch through the hard cast (v68), WHM Medica II/III and AST Aspected Helios timed AoE regens - controller-owned, arm-at-detect + fire-by-clock, aimed to complete ~1.2s after the raidwide cast bar so the heal lands on post-hit HP (v69-72).

### Removed
- **All `[RWS]` diagnostic logging stripped** (SGE/SCH/WHM/AST locks in `AutoRotationController.cs`, combo-fire log in `SGE_Helper.cs`) - clean production build.

## v1.0.4.72 (2026-07-01)

### Fixed
- **AST timed regen now actually fires: arm-at-detect + fire-by-clock (WHM too).** The dajoeybaz log proved the v71 mechanism worked when it triggered (one perfect `rem=0.27 castS=1.48` Helios) but almost never triggered: the trigger gates were only sampled while `remaining bar <= castS - 1.2s`, a window just ~0.3s wide for AST's 1.5s Helios cast (vs ~1.1s for WHM's 2s Medica - why WHM felt fine and AST didn't). Any mid-GCD moment inside that sliver = total miss. The locks now ARM as soon as the raidwide bar appears (gates evaluated with the whole bar of leeway), schedule an absolute fire time (`bar end + RegenLandDelaySeconds - own cast time`), and fire by the clock. Armed state disarms if the bar vanishes early or the party picks up the HoT another way; movement delays the fire instead of cancelling it. `AutoRotation/AutoRotationController.cs`.
- Log also showed rotation-cast Heliae coinciding with a detection window being counted as the raidwide regen (bare `COMPLETE` lines burning the 10s gate); with arming now happening at bar start this dedupe only engages in the actual fire window.

### Notes
- New `[RWS] WHM/AST ARM rem= castS= fireIn=` log lines record the scheduled timing for tuning.

## v1.0.4.71 (2026-07-01)

### Fixed
- **WHM/AST timed regen: aim at damage APPLICATION, not the cast bar; measured logging.** v1.0.4.70 aimed the regen to complete 0.5s after the boss cast bar - but raidwide damage applies ~0.6-1.5s AFTER the bar (effect-packet delay, per-spell, not present in the game sheets), so the heal still landed in the gap before the hit. The aim point is now `RegenLandDelaySeconds = 1.2s` after the bar. New `RaidwideTimeRemaining()` (`CustomCombo/Functions/Action.cs`) exposes the actual remaining bar time; the locks use it directly and only apply timed logic when a cast bar exists - VFX/stack-marker detections (which carry no timing) fire immediately instead of pretending to be timed. `[RWS]` issue logs now record `rem=` (measured bar remaining, or "VFX") and `castS=` (our adjusted cast time) so the delay constant can be tuned from live data. `AutoRotation/AutoRotationController.cs`.

## v1.0.4.70 (2026-07-01)

### Fixed
- **WHM/AST timed regen now completes just AFTER the raidwide hits, not before.** v1.0.4.69 used fixed trigger windows (WHM 2.5s / AST 1.5s) that were wider than the regen's own cast time, so the heal finished ~0.5s before the damage landed (Joey's live test). The trigger window is now computed per-cast: `GetAdjustedCastTime(regen) - RegenLandOffsetSeconds (0.5s)`, i.e. the cast starts late enough that it completes ~0.5s after the boss cast bar resolves, landing the heal + HoT on post-hit HP. Floor of 0.5s (covers Swiftcast/instant edge). `AutoRotation/AutoRotationController.cs`.

## v1.0.4.69 (2026-07-01)

### Fixed
- **WHM & AST timed AoE regens now fire reliably under auto-rotation (controller-owned locks).** `WHM_Raidwide_Medica` and `AST_Raidwide_AspectedHelios` previously lived only in the per-job combo-replacement path (`WHM_Helper.RaidwideMedica` / `AST_Helper.RaidwideAspectedHelios`), which under autorot only runs when the DPS combo happens to be invoked inside the short timing window - the same architecture flaw that made the SGE/SCH shields hit-or-miss before v1.0.4.57/.65. Ported both into `AutoRotationController` as `WhmRaidwideRegenLock()` / `AstRaidwideRegenLock()`, dispatched from `HealerRaidwideShieldLock()`, using the proven SCH commit-latch pattern: claim the next GCD via direct `UseAction` (base action via `OriginalHook` + self `GameObjectId`), HOLD the lock through the entire hard cast, and mark the 10s shield-slot gate only on cast COMPLETION. WHM starts Medica II/III at ~2.5s before impact; AST starts Aspected Helios / Helios Conjunction at ~1.5s (fires with or without Neutral Sect). Movement releases an uncommitted lock instead of dead-locking; 4s safety expiry. `AutoRotation/AutoRotationController.cs`.

### Changed
- **Raidwide mit list no longer burns the timed regens early.** When `WHM_Raidwide_Medica` / `AST_Raidwide_AspectedHelios` are enabled, `HandleRaidwide` skips `Medica2/Medica3` / `AspectedHelios/HeliosConjuction` in `RaidwideActions` - firing them as generic mits at detect time applied the HoT too soon and made the timed cast skip itself on the party-already-has-the-HoT check.

### Notes
- Combo-path helpers unchanged (manual play still works); `[RWS]` diagnostic logging retained pending healer validation.

## v1.0.4.68 (2026-06-27)

### Fixed
- **SCH raidwide shield: added a commit-latch so the hard cast survives `GroupDamageIncoming()` flipping false.** The dajoeybaz log showed `SchRaidwideShieldLock` issuing Succor once (`cast=True`) then never holding or completing - because `wanted` was gated solely on `GroupDamageIncoming()`, which is only true for a brief detection window. The instant it flipped false the lock released mid-cast, the rotation resumed and cancelled the half-started ~2s Succor. Added `_schShieldPending` (mirrors SGE's `_shieldEukrasiaPending`): once Succor is issued the lock stays engaged until the cast COMPLETES (or a 4s safety expiry), so the rotation can't interrupt it. `AutoRotation/AutoRotationController.cs`.

## v1.0.4.67 (2026-06-27)

### Fixed
- **SCH raidwide shield: hold the lock through the whole hard cast, and mark it done only on completion.** Succor/Concitation is a ~2s hard cast with no instant version (Recitation only removes cost + guarantees a crit; it does NOT grant instant cast), so v1.0.4.66's mark-used-on-cast-start released the lock mid-cast and let the rotation resume while Succor was still casting. `SchRaidwideShieldLock` now HOLDS the lock for the entire cast and calls `MarkRaidwideShieldUsed` only when the cast actually COMPLETES (watched cast-state transition + `JustUsed`), never on start. Also dropped the `GetPartyBuffPercent(Galvanize) <= 50` gate so a Galvanize already on the party (e.g. a tank's Adloquium) can't suppress the raidwide Succor. `AutoRotation/AutoRotationController.cs`.

## v1.0.4.66 (2026-06-27)

### Fixed
- **SCH raidwide shield now uses the same hard intention-lock as SGE.** SCH's Succor/Concitation was still cast through the per-job combo path (the controller only *held* mitigation for it), so on a tight window it could be skipped entirely - it missed on the very first raidwide in testing. Generalized `SgeRaidwideShieldLock` into `HealerRaidwideShieldLock`; the new `SchRaidwideShieldLock` claims the next GCD for the AoE shield (single hard cast, no Eukrasia two-step) and LOCKS the rest of the rotation until the Galvanize shield is up, then a mitigation weaves in during the cast. `AutoRotation/AutoRotationController.cs`.

## v1.0.4.65 (2026-06-27)

### Fixed
- **SGE raidwide shield: hard intention-lock so Eukrasian Prognosis ALWAYS follows the Eukrasia.** When the auto-rotation casts Eukrasia for the raidwide shield it now sets a lock and the entire rest of the rotation is suppressed until Eukrasian Prognosis is out - so the Eukrasia can never be spent on Eukrasian Dosis (or anything else) first. The lock waits out any in-progress cast, lets the GCD free up, casts Eukrasia, then casts Eukrasian Prognosis, then releases. `SgeRaidwideShieldLock` in `AutoRotation/AutoRotationController.cs`.

## v1.0.4.64 (2026-06-27)

### Changed
- Diagnostic build: `[RWS]` logging now records GCD/casting/moving state at raidwide detection and combo-fire, to see why the 2-GCD Eukrasian Prognosis sometimes can't slot into a tight raidwide window. No behavior change.

## v1.0.4.63 (2026-06-27)

### Fixed
- **SGE/SCH raidwide shield: handed the cast back to the combo path.** The dajoeybaz `[RWS]` trace showed the controller's direct shield cast (added in 1.0.4.57) spamming Eukrasia (rejected ~1s while the GCD rolled, then queued) and the 1.0.4.61 Run-hold STARVING the per-job combo - the path that actually casts Eukrasian Prognosis correctly (via the heal-cast routine, which prioritises the shield over Eukrasian Dosis so the Eukrasia is not stolen). Removed `TryRaidwideShield` and the Run-hold; the shield is cast solely by the combo (`RaidwideEprognosis` / `RaidwideSuccor`) and `RaidwideShieldPending` (preset-gated again) only holds the mitigation until the shield lands. Requires the sub-preset on (SGE: SGE_Raidwide -> Eukrasian Prognosis; SCH: SCH_Raidwide -> Succor). One `[RWS]` combo log kept for verification.

## v1.0.4.62 (2026-06-27)

### Changed
- **Diagnostic build.** Added temporary `[RWS]` logging to the SGE/SCH raidwide-shield path (entry state, Eukrasia/Prognosis/Succor cast attempts and their results) so the exact point of failure can be read from the Dalamud log (`/xllog`, filter `RWS`). No behavior change; logging will be removed once the shield sequence is confirmed.

## v1.0.4.61 (2026-06-27)

### Fixed
- **SGE raidwide shield: Eukrasian Prognosis now follows the Eukrasia even when Eukrasia was not already up.** The cast itself was fine - it worked whenever Eukrasia happened to already be up - but when the auto-rotation had to put Eukrasia up first, the DPS rotation immediately spent it on Eukrasian Dosis before the Prognosis follow-up. (That is also why the tank-shield always worked: it fires in 3+ enemy AoE where the rotation uses Dyskrasia, not Eukrasian Dosis.) Fix: while SGE/SCH still owe the AoE shield this raidwide, the rest of the rotation is held for that tick so nothing consumes the Eukrasia between it and Eukrasian Prognosis. `Run()` in `AutoRotation/AutoRotationController.cs`.

## v1.0.4.60 (2026-06-27)

### Fixed
- **SGE Eukrasian Prognosis now casts under auto-rotation - the self target id was missing.** The working AoE-heal/mit combos cast it via `UseAction(OriginalHook(Prognosis), player.GameObjectId)`: the BASE Prognosis WITH a self target id, which the game transforms into Eukrasian Prognosis. v1.0.4.58 used the base Prognosis but with NO target id (did not fire); v1.0.4.59 added a Retarget/target but on the explicit Eukrasian id - and Prognosis takes no selectable target, so that was wrong. Now mirrors the proven combo cast exactly. SCH Succor also passes the self target id now. `TryRaidwideShield` in `AutoRotation/AutoRotationController.cs`.

## v1.0.4.59 (2026-06-27)

### Fixed
- **SGE raidwide shield now casts the Eukrasian Prognosis follow-up.** Eukrasia fired but the second GCD did nothing. The fix mirrors the working tank-shield path (`UpdateSgeTankShield`, which casts Eukrasian Diagnosis): use the explicit Eukrasian Prognosis action id with `Retarget(Self)` and an explicit self target id. Casting the base Prognosis (v1.0.4.58) or the Eukrasian id with no target (v1.0.4.57) did not fire. `TryRaidwideShield` in `AutoRotation/AutoRotationController.cs`.

## v1.0.4.58 (2026-06-27)

### Fixed
- **SGE Eukrasian Prognosis now actually casts under auto-rotation.** v1.0.4.57 issued the Eukrasian* action id directly (`UseAction(EukrasianPrognosis)`), which the game won't cast, so the SGE shield never went out. With Eukrasia up the controller now uses the BASE `Prognosis` and lets the game transform it into Eukrasian Prognosis - the same proven pattern the DPS rotation uses for Eukrasian Dosis. The shield also only marks its cooldown when `UseAction` actually succeeds, so a missed cast retries instead of gating itself off. `TryRaidwideShield` in `AutoRotation/AutoRotationController.cs`.

## v1.0.4.57 (2026-06-27)

### Fixed
- **SGE/SCH raidwide shield now fires reliably under auto-rotation.** It previously came only from the per-job combo path (needs the `SGE_Raidwide_EPrognosis` / `SCH_Raidwide_Succor` sub-preset AND the right combo invoked that tick), while the mitigations fire from the controller's preset-independent list - so the mit reliably went out but the shield often didn't ("felt like upstream"). The AoE shield (SGE Eukrasia -> Eukrasian Prognosis, SCH Succor) is now cast directly in the auto-rotation controller, FIRST, the same way the mits fire, gated only on raidwide-handling being on (no separate sub-preset needed for auto-rotation). New `TryRaidwideShield` + preset-independent `RaidwideShieldPending` in `AutoRotation/AutoRotationController.cs`. The manual heal-combo raidwide feature (still uses the sub-presets) is unchanged.

## v1.0.4.56 (2026-06-27)

### Changed
- **Raidwide shield: removed the cast-interrupt.** SGE/SCH no longer cancel an in-progress hard-cast to force the AoE shield out - cancelling the cast could leave the shield unable to slot in (GCD/animation-lock thrash: "stops casting but doesn't start the shield"). The AoE shield (Eukrasian Prognosis / Succor) is still the highest-priority raidwide action and now slots in cleanly on the next available GCD. Removed `IsHardCastingDamage` + the per-job damage-cast tables and the `Hotbar.CancelCast()` call in `AutoRotation/AutoRotationController.cs`; the shield-first mitigation deferral (`RaidwideShieldPending`) is unchanged.

## v1.0.4.55 (2026-06-27)

### Added
- **SGE/SCH raidwide "shield-first" reaction.** On an incoming raidwide OR stack, SGE/SCH now fire ONE AoE shield FIRST (Eukrasian Prognosis / Succor) then ONE mitigation, instead of only a single mit. New `RaidwideShieldOnCooldown` (10s) gate in `AutoRotation/AutoRotationController.cs`, separate from the 15s mit gate, lets both land on the same raidwide; the shield helpers (`RaidwideEprognosis`, `RaidwideSuccor`) were moved off the mit gate and the combos mark the shield gate only on the actual shield step. Auto-rotation also cancels an in-progress damage hard-cast (Dosis/Broil, scoped via `IsHardCastingDamage`) so the instant shield fires immediately. Applies to auto-rotation and the manual heal combos (`SGE.cs`, `SGE_Helper.cs`, `SCH.cs`, `SCH_Helper.cs`).
- **WHM `WHM_Raidwide_Medica` (new toggle).** Times Medica II / Medica III so the regen lands as a raidwide/stack resolves (fires at <= 2.5s left on the incoming cast), skipped if the party already has the HoT or while moving. `WHM.cs`, `WHM_Helper.cs`.
- **SGE `SGE_TankShield` (new toggle, auto-rotation).** While MORE THAN 2 enemies are on a tank, keeps Eukrasian Diagnosis up on that tank (Eukrasia -> Diagnosis, flag-driven so it never hijacks an Eukrasian Dosis), and spends Addersting with Toxikon at cap so the breaking shields don't waste the gauge. `AutoRotationController.cs`.

### Changed
- **AST raidwide regen timing.** `AST_Raidwide_AspectedHelios` now fires only once the incoming damage is <= 1.5s from landing so the Aspected Helios / Helios Conjunction HoT recovers the hit, and now also applies the regen without Neutral Sect when the party lacks the HoT. `AST_Helper.cs`.

### Notes
- The SGE/SCH AoE-shield helpers moved off the shared 15s mit gate onto their own 10s shield gate; other healers' raidwide mit behavior is unchanged.
- Hardcoded tunables: shield gate 10s, AST window 1.5s, WHM Medica window 2.5s, tank threshold > 2, Addersting cap 3.
- Testing build.

## v1.0.4.54 (2026-06-19)

### Changed
- **Upstream sync (WrathCombo `main` 1.0.4.9 tip, 3 commits `06877cca..1c7049c64`).** Per-file 3-way merge; all fork divergences preserved.
- **Autorotation override-target lifecycle.** `OverrideTarget` is now cleared automatically when it points at a dead/invalid object (self-wiping getter) and wiped when autorotation is disabled (`if (!cfg.Enabled) OverrideTarget = null;`), replacing the per-early-exit `OverrideTarget = null` cleanups in `AutoRotation/AutoRotationController.cs`. Invoke paths now use `OverrideTarget = target ?? OverrideTarget` and pass `OverrideTarget` into range/face/target-id checks.
- **Ability queue window.** oGCD queueing now uses `AnimationLock <= cfg.QueueWindow` instead of requiring `AnimationLock == 0`, so weaves fire more reliably. `AutoRotationController.cs`.
- **Target helpers.** `HasBattleTarget()` is now null-safe (`CurrentTarget?.IsHostile() == true`); `OverrideTarget` getter drops dead targets. `CustomCombo/Functions/Target.cs`, `Status.cs` (penalty path no longer force-nulls the override).

### Notes
- Preserved fork divergences: 15s raidwide-mit gate, Pyretic/`PlayerHasActionPenalty` + enemy-reflect gating, Pacification/Silence handling, WHM Divine Caress ground-heal, BLU autorotation engine, SMN Aegis Uptime.
- Removed unused `using ECommons.DalamudServices.Legacy;` per upstream.

# Gluttony Combo â€” Changelog

## v1.0.4.53 (2026-06-18)

### Fixed
- **BLU auto-rotation no longer idles when damage spells are available.** The terminal GCD filler
  was a hand-picked list of specific spells; if none matched it returned nothing. It now iterates
  your entire slotted spellbook (`ActiveBLUSpells`) and casts the first off-cooldown, in-range
  damage spell. Only an explicit exclusion set is skipped — buffs, heals, mitigation, hard CC,
  knockbacks/draws, suicides/self-damage, instant-KO/%HP gimmicks, and the cooldown-managed damage
  + DoTs the cascade already handles. Any damage spammable you slot is picked up automatically with
  no per-spell configuration.

## v1.0.4.52 (2026-06-18)

### Fixed
- **BLU filler: Bristle now snapshots onto DoTs.** Breath of Magic / Mortal Flame / Song of Torment
  cast Bristle first (when available and not already up) so the DoT is buffed.
- **BLU: Mortal Flame no longer double-applies.** Added a `JustUsed` guard so the permanent DoT is not
  re-cast during the status-application delay (applies to all three DoTs).
- **BLU: Surpanakha dumps all 4 charges.** Once charges cap at 4 it now fires the full chain
  consecutively (ready-flag pattern) instead of a single charge.
- **BLU: Winged Reprobation / Conviction Marcato combo no longer stalls at 2.** The filler returns
  `OriginalHook(WingedReprobation)` so the 3rd stack and the Conviction Marcato payoff resolve. This
  also fixes the rotation stalling after DoTs (it was returning an uncastable raw Winged Reprobation
  at the stack transition).
- **BLU: explicit Sonic Boom terminal filler** so the GCD keeps rolling when everything else is down.

## v1.0.4.51 (2026-06-18)

### Fixed
- **BLU auto-rotation reported "0 active" and never auto-cast.** The `BLU_AutoRotation_DPS`
  preset lacked the `[SimpleCombo]` tag that every other auto-rotation preset carries, so
  `PresetData.ComboType` resolved to `Feature` (a non-combo UI toggle) instead of `Simple`. Tagged
  it `[SimpleCombo]` so it registers as a real single-target auto-rotation combo. Also removed a
  stray `[ConflictingCombos(BLU_MeleeCombo)]` that was added in the previous build.

## v1.0.4.50 (2026-06-18)

### Added
- **BLU mimic-aware auto-rotation (Phases 1-3).** New opt-in single-target DPS auto-rotation preset
  `BLU_AutoRotation_DPS` (`[AutoAction(false,false)]`, replaces Sonic Boom) that reads the current
  Aetheric Mimicry stance and runs DPS / Tank / Healer lanes from one combo. Because the engine's
  heal/tank automation is hard-gated to `CombatRole.Healer`/`Tank` and never runs for BLU
  (magical-ranged DPS), the heal and tank lanes live inside the DPS `Invoke`. New file
  `Combos/PvE/BLU/BLU_AutoRotation.cs`; `BLU.cs` left untouched for upstream-merge friendliness.
- **124 per-ability toggles + tuning sliders** in `Combos/PvE/BLU/BLU_Config.cs` (was an empty stub).
  Every learnable BLU spell gets a `UserBool` allow-list toggle (rotation on by default; the suicides
  plus Diamondback / Basic Instinct off), grouped under collapsible headers. Sliders: Final Sting boss
  HP%, Cold Fog lead time, party / single-target / emergency heal HP% thresholds, prophylactic mit,
  Surpanakha hold-for-burst window, per-mimic BossMod Reborn distance (Tank/DPS/Healer), DoT refresh
  lead. A second config-only preset `BLU_AutoRotation_Heal` hosts the heal thresholds and gates the
  heal lane.
- **Mimic-aware behaviour.** Pushes BossMod Reborn `MaxDistanceToTarget` per stance on mimic change
  (`ConflictingPluginsChecks.BossModReborn.SetMaxDistanceToTarget`). Tank lane auto-ensures Mighty
  Guard is on and never auto-cancels it (player call); Healer lane heals generously (party-HP% gated),
  emergency-only under DPS/Tank mimic. Moon Flute burst reuses the proven `BLU_NewMoonFluteOpener`
  sequence; Cold Fog -> White Death pre-raidwide window via `GroupDamageIncoming`; Final Sting
  kill-range behind a default-off toggle + HP% slider.

### Notes
- Additive and opt-in - existing manual BLU button-replacement combos are untouched.
- ~18 utility/mitigation spells have toggles but no cascade predicate yet (dormant by design). Final
  Sting currently gates on HP% only (no boss-only check yet); heal triggers use party-average HP as a
  proxy. Tuning to follow after in-game testing.

## v1.0.4.49 (2026-06-18)

### Added
- **Healer raidwide mitigation gating for SCH / AST / SGE.** Raidwide mit oGCDs now respect the
  shared `AutoRotationController.RaidwideMitOnCooldown` window and call `MarkRaidwideMitUsed()`
  when they fire, mirroring the existing WHM implementation. Stops the autorotation from dumping
  multiple raidwide mitigations onto a single incoming hit. AST Aspected Helios is intentionally
  exempt (it is a reactive heal cast on need, not a pre-cast mitigation). Files:
  `Combos/PvE/SCH/SCH_Helper.cs`, `Combos/PvE/AST/AST_Helper.cs`, `Combos/PvE/SGE/SGE_Helper.cs`.

## v1.0.4.48 (2026-06-17)

### Changed
- **Upstream sync — WrathCombo `main` 1.0.4.8 → 1.0.4.9 (~44 commits, 30 files).** Merged the upstream range `0e6e5a9e…06877cca6` across job rotations, autorotation, and UI, preserving all Gluttony fork divergences.
  - **MCH:** fixed AoE tools firing incorrectly; Reassemble/Hypercharge handling; helper refactors (`Combos/PvE/MCH/MCH.cs`, `MCH_Helper.cs`).
  - **SAM:** adopted upstream's completed ST/AoE rotation rebalance (Getsu/Ka + Fugetsu/Fuka refresh guards on Mangetsu/Oka/Gekko/Kasha). Our fork carried an earlier, incomplete form of the same logic — converged to upstream to reduce future merge friction (`Combos/PvE/SAM/SAM.cs`, `SAM_Helper.cs`).
  - **SGE:** AoE simple-heal oGCD spread rebalance; autorotation shield check now optional (`Combos/PvE/SGE/SGE.cs`).
  - **BLM:** fixed level-90 Ice phase (`BLM.cs`, `BLM_Helper.cs`). **VPR:** early-buff opener (`VPR_Helper.cs`, `VPR_Config.cs`). **WAR:** Fell Cleave cleanup + helper tidy (`WAR.cs`, `WAR_Helper.cs`). **MNK PvP** update (`Combos/PvP/MNKPVP.cs`).

### Added
- **Encounter safety / Action Penalty Gaze & Motion handling.** New `Combos/PvE/Content/EncounterSafety.cs` plus content-specific action checks — Windurst Motion/Gaze VFX checks, content-specific fallbacks, Clytemnestra motion-scanner range check.
- **p3 invincible status** added to status handling and Pyretic check moved to post-pre-pull (`CustomCombo/Functions/Status.cs`).
- **Healer "Include Shields" autorotation setting** and **DTR bar updates while hidden** (`AutoRotation/*`, `Window/Tabs/AutoRotationTab.cs`).
- **Opener DTR bar is now click-to-toggle** the current opener preset (`GluttonyCombo.cs`).

### Notes
- Upstream WrathCombo `.csproj` advanced 1.0.4.8 → 1.0.4.9; merge base advanced `0e6e5a9e` → `06877cca6`.
- **Fork divergences preserved:** AutoRotation tab keeps the `UnTargetAndDisableForPenalty` plain-checkbox variant + the Auto Positionals (Melee DPS) feature at their existing location; upstream relocated that checkbox to the top of DPS settings, so the relocated duplicate was dropped to avoid a doubled control. Pacification/Silence handling, WHM Divine Caress ground-heal targeting, 15s raidwide-mit gate, HP-scaled raidwide `numberOfCasts`, SMN "Aegis Uptime", and the manual BLU combos all live in files outside this upstream range and are untouched.
- Build clean (0 errors, 9 pre-existing warnings).

## v1.0.4.44 (2026-06-08)

### Changed
- **Upstream sync — Auto-Rotation tab UI.** Merged WrathCombo `main` commits `27fcf666` (Update autorot UI) and `0e6e5a9e` (More rewords) into `Window/Tabs/AutoRotationTab.cs` and `Resources/Localization/UI/AutoRotation/AutoRotationUI.{resx,Designer.cs}`.
  - Label rewords: `Checkbox_OnlyInCombat` "Only in Combat" → "Restrict to Combat Only"; `Checkbox_BypassFATETargets`/`Checkbox_BypassQuestTargets` "Bypass Only in Combat for …" → "Bypass for …"; matching `HelpText_PreEmptiveHoT` update.
  - Tab reorganized: added "Combat Settings" and "Automatic Activation Settings" `ImGuiEx.TextUnderlined` headers; combat settings (InCombatOnly, bypass options, delay) render unconditionally instead of being gated behind `P.IPC.GetAutoRotationState()`.

### Notes
- Upstream WrathCombo `.csproj` is still 1.0.4.8 — these were UI-only commits with no upstream version bump. Merge base advanced cab2ae9e → 0e6e5a9e.
- Preserved fork divergences: `UnTargetAndDisableForPenalty` plain-checkbox variant and the `/gluttony ignore` command string. The tab's `GluttonyCombo.P.`-qualified `UIHelper`/`IPC` calls were collapsed to bare `P.` to converge with upstream (functionally identical; `P` resolves to `GluttonyCombo.P`, as already used in `Commands.cs`/`DebugFile.cs`).
- No autorotation engine, combo, ActionID, or StatusID changes.

## v1.0.4.43 (2026-06-07)

### Added
- **HP% threshold for the Radiant Aegis "Maintain Uptime" feature.** The maintain block now fires only when `PlayerHealthPercentageHp()` is at or below a per-mode configurable threshold, instead of unconditionally re-applying whenever the buff was down in combat.
  - `Combos/PvE/SMN/SMN_Config.cs`: new `SMN_ST_RadiantMaintainHP` / `SMN_AoE_RadiantMaintainHP` `UserInt`s (default 90); `DrawSliderInt(0, 100, ...)` cases for `Preset.SMN_ST_Advanced_Combo_RadiantMaintain` and `Preset.SMN_AoE_Advanced_Combo_RadiantMaintain`, labeled via `FormatAndCache(Generics.HPPercentageThreshold, RadiantAegis.ActionName())`.
  - `Combos/PvE/SMN/SMN_Helper.cs`: maintain block gated on `PlayerHealthPercentageHp() <= radiantAegisMaintainHP`; threshold = `flags.HasFlag(Combo.ST) ? SMN_ST_RadiantMaintainHP : SMN_AoE_RadiantMaintainHP` (mirrors the existing Lucid ST/AoE selector).

### Changed
- Default 90%: Radiant Aegis is kept up as a near-constant buffer and only stops topping up at full HP. Set the slider to 100 to restore the previous always-maintain-in-combat behavior.

### Notes
- The overcap block (fire at 2 charges to avoid wasting a charge) is intentionally left ungated by HP.
- Reuses the existing `Generics.HPPercentageThreshold` localization string and mirrors the BLM Manaward HP-threshold pattern (`BLM_ST_ManawardHPThreshold`).

## v1.0.4.42 (2026-06-06)

### Added
- **Enemy damage-reflect / "spikes" pause (Eureka).** Autorotation now stops and targets self when any nearby hostile (the rotation's `DPSTargeting.BaseSelection`) has a reflect / counter / elemental "spikes" status, and resumes once it clears on all mobs. Mirrors the player-side Pyretic handling but scans enemies instead of the player. Built for Eureka's **Gelid Charge** (action 1284 -> Ice Spikes) and **Static Charge** (action 1283 -> Shock Spikes), and also covers the elemental Counter stances used in deep dungeons.
  - `Data/StatusCache.cs`: new `PausingStatuses.EnemyReflects` FrozenSet, resolved by English status name so every ID variant is caught - Ice Spikes / Shock Spikes / Blaze Spikes + Shocking/Burning/Freezing/Cutting/Burying/Drowning/Unrelenting Counter (status IDs 948-954).
  - `AutoRotation/AutoRotationController.cs`: new `EnemyHasReflectPenalty()` - scans `DPSTargeting.BaseSelection`, and on a hit targets self (`Svc.Targets.Target = Player.Object`), clears `OverrideTarget`, and `UIState.Instance()->Hotbar.CancelCast()`. Called from `ShouldSkipAutorotation()` gated behind `cfg.DPSSettings.UnTargetAndDisableForPenalty`. Added `using FFXIVClientStructs.FFXIV.Client.Game.UI;`.

### Notes
- Reuses the existing "Un-target and stop actions for Pyretics" toggle (opt-in, off by default). Because it also matches the generic Spikes family, a boss with non-lethal elemental spikes could trip the pause while the toggle is on - acceptable for the Eureka / deep-dungeon farming use case. The enemy scan only runs when that toggle is enabled.
- Status IDs identified by datamining the live game Status/Action sheets via Lumina.

## v1.0.4.41 (2026-06-06)

### Fixed
- In-game changelog display: the packaged manifest's Changelog had been stuck at v1.0.4.35 (template was never updated on release), so 1.0.4.36-1.0.4.40 notes never showed in Dalamud. Template + embedded manifest + pluginmaster now synced.

## v1.0.4.40 (2026-06-06)

### Changed
- Synced upstream WrathCombo 1.0.4.6 -> 1.0.4.8 (80 commits, 44 files): job-combo updates (BRD/DRG/MCH/MNK/PLD/RDM/SGE/WAR/WHM), IPC (Leasing/Provider/Search), ActionWatching/StatusCache, localization, debug tab.
- **Raidwide cap converged to upstream:** dropped our hard `AutorotRaidwides >= 1` in favor of upstream's HP-scaled `numberOfCasts` (1 when party healthy, 2 at <=60%, 3 at <=30%). Less future merge friction.

### Preserved
- Our customizations kept: 15s raidwide-mit cooldown gate, WHM Divine Caress ground-heal targeting, Pacification/Silence handling, BLU autorotation engine, SMN 'Aegis Uptime' (Radiant Aegis maintain) preset.

### Notes
- TESTING build pending in-game validation; production stays 1.0.4.39 until promoted. Merge conflicts resolved: AutoRotationController.cs (cap->upstream), Debug.cs (took upstream debug UI), CustomComboPresets.resx (kept both SMN + MCH entries).

## v1.0.4.37 (2026-05-31)
### Removed
- **BLU autorotation ALPHA reverted.** Removed BLU_Helper.cs, BLU_ST_AdvancedMode, BLU_Heal_AdvancedMode, the ALPHA warning banner, and the ST Advanced Engine debug readout. Manual BLU combos (Moon Flute Opener, Final Sting, Primal Combo, etc.) are unchanged.
- **DismountOnAbility removed.** The auto-dismount-on-ability-press feature has been removed from ActionWatching and Configuration.

## v1.0.4.35 (2026-05-31)
### Added
- **File-based debug log** writes to `C:\temp\blu-debug.log` on each decision frame of `BLU_ST_AdvancedMode.Invoke()`. Controlled by `DebugLogEnabled` static bool (default: true).
- Each log line is a tab-separated record with: timestamp, chosen action + DPET, alternative DPET top-3, all ready oGCDs with potency, all ready GCDs with potency and DPET, DoT up/down status, buff states (MF/BR/WH/TI), Surpanakha dump state (charges, JustUsed), WR chain state (stacks, Winged Redemption status).
- **Cooldown blockers section** logs every slotted spell with CD > 0 that is NOT being cast, with the specific reason: CD remaining, not slotted, melee range required, moving with cast time, shared recast on CD, missing required status.
- File auto-rotates at 5 MB (current -> .bak, new file started). All logging is wrapped in try/catch so failures never crash the engine.## v1.0.4.33 (2026-05-31)

### Fixed
- **Surpanakha charge dump.** Added a dump guard in Invoke() and BestWeave() so that once the first Surpanakha charge fires, all remaining charges fire consecutively without the engine interleaving other actions. Consecutive uses gain +50% potency each (200/300/450/675 = 1625 total vs 800 spread out).
- **Winged Reprobation chain DPET.** The chain-start value was divided by GcdCost*5 (=12.5), making it too low to ever win priority. Corrected to 296 potency/GCD (total chain 1480 over 5 GCDs), so the chain competes fairly in BestGcd().
- **ReadyGcd cooldown gating.** Spells with a real cooldown (CooldownS > 0 in the catalog, e.g. Magic Hammer 90s, Devour 60s, Ruby Dynamics 30s) now use IsOffCooldown() instead of the generic CooldownTotal <= 3f check, preventing them from appearing ready when still on cooldown.
- **Heal preset placement.** Moved BLU_Heal_AdvancedMode (70031) to immediately follow BLU_ST_AdvancedMode (70030) in CustomComboPreset.cs, matching the layout convention used by other jobs (AST, WHM, SGE, SCH).

### Added
- **Flame Thrower** (11402) added to the spell catalog as a channel (220 potency, cone AoE, 10s channel duration). Previously missing despite being a damaging BLU ability.

## v1.0.4.30 (2026-05-31)

### Complete Rewrite
- **Complete BLU autorotation engine rewrite with full spell catalog.** Replaced the ~317-line partial catalog with a ~730-line engine covering the entire Blue Mage damage kit (100+ spells cataloged). Every candidate spell is now scored by damage-per-effective-time (DPET) with AoE-aware target counting, so the engine naturally transitions from ST to AoE priority based on how many enemies each spell will hit.

### Added
- **AoE-aware scoring.** Every AoE spell multiplies its effective potency by `NumberOfEnemiesInRange(spellId)` so multi-target situations automatically shift priority from ST fillers to AoE nukes without a separate AoE preset.
- **Conditional/gated spells.** White Death (needs Touch of Frost 2494), Divine Cataract (needs Auspicious Trance 2497), and Conviction Marcato (needs Winged Redemption 3641) fire instantly when their enabler buffs proc.
- **Full spell catalog.** All ST fillers (Sonic Boom, Water Cannon, Sharpened Knife, Goblin Punch, Glower, Mustard Bomb, Abyssal Transfixion, Reflux, Matra Magic, Triple Trident, Revenge Blast, Flying Sardine, Blood Drain), all AoE GCDs (40+ spells from Drill Cannons to Candy Cane), all oGCDs (Feather Rain through Being Mortal + Surpanakha), DoTs (Song of Torment, Breath of Magic, Mortal Flame, Aetherial Spark), channels (Phantom Flurry with finisher, Apokalypsis), and self-KO/percent-HP gimmick spells (cataloged but never auto-cast).
- **Movement awareness.** Cast-time GCDs and buff spells are skipped while moving; only instant-cast spells are selected.
- **Shared recast handling.** Being Mortal / Apokalypsis and Magic Hammer / Candy Cane correctly share cooldowns.
- **DPS Mimicry awareness.** Matra Magic scores at 800 potency when DPS Mimicry (status 2125) is active.
- **New BLU_Heal_AdvancedMode preset** (enum 70031). Heal mode raises dead party members (Angel Whisper), heals when anyone drops below 50%% HP (Gobskin, Angels Snack, Pom Cure, White Wind, Rehydration, Exuviation), then falls through to the full DPS engine when nobody needs healing.
- **Expanded debug readout.** Shows buff states, movement, melee range, and per-spell active/ready/AoE/channel flags with enemy counts.

### Notes
- Still ALPHA. Greedy priority (fires on cooldown), no Moon Flute burst window optimization yet.
- Self-KO spells (Final Sting, Self-destruct, Wild Rage) and percent-HP gimmick spells (Missile, Tail Screw, Dimensional Shift, Launcher) are cataloged but never auto-cast.


## v1.0.4.29 (2026-05-31)

### Fixed
- **The Ram's Voice spam + cooldowns never firing (same root cause).** GCD nukes were gated on `IsOffCooldown`, which for an instant GCD just reflects the rolling 2.5s global GCD, so at the decision moment every standard nuke read 'on cooldown' and the engine fell to a 2s-cast filler. Those hardcasts consumed the whole GCD, leaving no weave window -- which is why oGCD cooldowns stopped firing. GCD spells are now gated by `ReadyGcd` (only a REAL cooldown > the GCD blocks them), so instant nukes are selected normally and weave windows return. `Combos/PvE/BLU/BLU_Helper.cs`.
- **Surpanakha now fires.** Dropped the max-stack latch (which never triggered if charges were not full); it now fires on any available charge.
- **Mortal Flame double-cast.** Suppression window widened for permanent DoTs so it is not re-applied after the status readback lapses.

### Added
- **Channels implemented (Phantom Flurry, Apokalypsis), not excluded.** Cast on cooldown when stationary and in range, then HELD with a no-op (All.SavageBlade) so no other action cancels the channel; Phantom Flurry fires its 600 finisher just before expiry. Apokalypsis yields to the instant Being Mortal when both are slotted (shared recast).
- **Revenge Blast modeled by its synergy** -- valued at 500 only when your HP < 20% (never self-harms to set it up), otherwise treated as the ~50 it is.
- **Debug readout.** Settings -> Debug -> Blue Mage Data -> 'ST Advanced Engine' shows CanWeave, the chosen weave/GCD action, and a per-spell active/ready/charge table for diagnosing what the engine sees.

### Notes
- Still ALPHA, greedy single-target priority.

## v1.0.4.28 (2026-05-31)

### Fixed
- **Breath of Magic (and DoT) spam fixed.** DoT up-detection used the per-TARGET `JustUsedOn`, but Breath of Magic is a cone with no single target, so its cast was recorded against target 0/self and the per-target lookup always missed -- once the debuff readback also lagged, the DoT (worth ~960 DPET vs ~160 for a nuke) won every GCD and monopolized them. Now uses the per-ACTION `JustUsed` timestamp (reliably recorded on every cast) for cadence, with the debuff readback as secondary. Permanent DoTs skip on presence alone. `Combos/PvE/BLU/BLU_Helper.cs`.
- **Cooldowns resume.** With the DoT no longer hogging every GCD, the weave lane fires oGCD cooldowns normally again.
- **The Ram's Voice moved to filler-only.** It freezes the target (Ultravibration setup), so it no longer competes for DPS GCDs and is only used when nothing else is available.

### Added
- **Winged Reprobation chain implemented.** Re-added (it was removed in 1.0.4.27). It is a 4-stack chain that resets its own recast and upgrades to Conviction Marcato at max stacks; once the chain is started (1-3 stacks) or Conviction Marcato is ready, the engine finishes it before other GCDs via `OriginalHook`. A fresh chain starts through the normal priority.

### Notes
- Still ALPHA, greedy single-target priority. The DoT cadence backstop is per-action, so on rapid target swaps a DoT may be considered up briefly on a new target (single-target focus for now; AoE/multi-target weighting is a later phase).

## v1.0.4.27 (2026-05-31)

### Added
- **Full damaging-spell catalog for the BLU autorotation.** The engine now covers the entire Blue Mage damage kit (single-target AND AoE), not a curated subset, so it casts whatever damaging spells you actually have slotted instead of idling once a few were on cooldown. Added (verified IDs via Garland): Goblin Punch (34563), Mountain Buster (11428), Quasar (18324), Both Ends (23287), Aqua Breath (11390), High Voltage (11387), Glower (11404), Plaincracker (11391), Drill Cannons (11398), 1000 Needles (11397), Stotram (23269), Aetherial Spark (23281), Water Cannon (11385), plus the damaging spells already in constants (Mustard Bomb, Peripheral Synthesis, Ram's Voice, Knight's Tours, Perpetual Ray). AoE oGCDs/GCDs are included because they also hit the primary target and serve as filler. `Combos/PvE/BLU/BLU_Helper.cs`.

### Fixed
- **DoT re-application while already up.** Mortal Flame is a permanent DoT, so `GetStatusEffectRemainingTime` returns 0, which defeated the old `remaining > 3s` skip and caused constant re-casting. DoTs are now treated as up if the debuff is detected with time left OR we cast it on this exact target within its own duration (per-target wall-clock via `JustUsedOn`), with explicit handling for permanent DoTs. This also hardens Breath of Magic / Song of Torment against status-readback gaps.
- **Mortal Flame now gets Bristle.** It was mis-tagged as Physical; it deals fire (magic) damage, so it never matched the Bristle path. It is now Magical and is a Bristle target -- and because it is permanent, the Bristle snapshot applies for the whole fight, making it one of the best Bristle payloads.

### Removed / deferred
- **Winged Reprobation removed from greedy auto** -- it is a 3-stack `OriginalHook` combo the flat engine half-fired; it will be handled properly in the burst phase.
- **Channels excluded from greedy auto** (Flame Thrower, Phantom Flurry, Apokalypsis): pressing any other action cancels an active channel, so auto-spamming them loses damage. They need dedicated handling (later phase).
- Self-KO (Final Sting, Self-destruct) and Revenge Blast (needs a low-HP setup) remain excluded by default.

### Notes
- Still ALPHA and greedy single-target-priority (AoE target-count weighting, Moon Flute burst window, heals, and tank/mitigation are later phases). Pure utility/CC/heal/mitigation/buff spells are not auto-cast. A few potencies are approximate and affect only ordering between near-equal options.

## v1.0.4.26 (2026-05-31)

### Fixed
- **BLU autorotation no longer double-casts Mortal Flame (and DoTs generally).** The DoT lane now skips a DoT within `JustUsed()` of its last cast, closing the application-delay window where the debuff had not yet registered on the target and the engine re-fired it. `Combos/PvE/BLU/BLU_Helper.cs`.
- **BLU autorotation no longer stalls / leaves much of the kit uncast.** P1's catalog was too small, so once the few modeled spells were on cooldown the engine idled. Catalog expanded to the full verified level-80 single-target damage kit (added Winged Reprobation, Eruption, Sea Shanty, plus the previously omitted oGCDs). Melee-range spells (Sharpened Knife) are now gated on `InMeleeRange()` so a higher-DPET melee pick can't permanently stall the engine at range. Sonic Boom is the guaranteed filler.
- **Anchor corrected.** `BLU_ST_AdvancedMode` now anchors on the verified Sonic Boom action; the previous Water Cannon anchor used an unverified id and has been removed.

### Notes
- Still ALPHA / greedy single-target only; no Moon Flute burst window, AoE, heals, or tank/mitigation yet. Spells you have slotted that are outside the catalog are still not auto-cast -- coverage broadens in later phases.

## v1.0.4.25 (2026-05-31)

### Added
- **Blue Mage single-target autorotation (ALPHA).** New `BLU_ST_AdvancedMode` preset (`[AutoAction(false,false)]`, anchored on Water Cannon) backed by a new potency-priority engine in `Combos/PvE/BLU/BLU_Helper.cs`. Not a fixed rotation: it scores every spell active in the player's spellbook by damage-per-execution-time (potency x current buff multiplier / time cost, where an oGCD costs ~0.6s weave lock and a GCD costs 2.5s), weaves the best damage oGCD when `CanWeave()`, maintains DoTs only when absent or about to fall (no clipping), dumps Surpanakha as a full 4-charge bundle via the `SurpanakhaDumping` latch, and only spends a GCD on Bristle/Whistle/Tingle in front of a payload >=400 potency (the "worth-it" gate). Greedy (fires on cooldown). Potencies are the level-80 set, hardcoded in `StCatalog`.
- **ALPHA banner on the Blue Mage section.** `Window/Messages/Messages.cs` `PrintBLUMessage` now renders a red experimental / work-in-progress notice above the BLU feature list.

### Notes
- ALPHA quality: not yet verified in-game. The engine is structurally complete but fine ordering between near-equal options depends on potency values that still need an in-game verification pass. Scope is BLU single-target only -- Moon Flute burst window, AoE, heals, and tank/mitigation are not implemented yet, and no other job is affected. Existing manual BLU feature combos are unchanged.

## v1.0.4.24 (2026-05-30)

### Changed
- Gluttony combo priority adjustment in the Ifrit phase; version bumped to 1.0.4.24. (CHANGELOG entry backfilled in v1.0.4.25 to close the narrative gap.)

## v1.0.4.23 (2026-05-30)

### Added
- **Pacification handling (status 6).** Autorotation now detects Pacification on the player and skips oGCD abilities, falling through to GCD weaponskills. Implemented in `AutoRotation/AutoRotationController.cs` by checking `ActionAttackType.Ability` against the next-action's `ReplaceSkill!.ActionIDs.First()` and `continue`-ing to the next priority. New `Pacification = 6` constant added to `Combos/PvE/ALL/ALL.cs` `Debuffs`.
- **Silence handling (status 7).** Autorotation tries to clear Silence with Echo Drops (item 4566, +1,000,000 HQ offset) when off cooldown; if Echo Drops are unavailable, skips spells and falls through to weaponskills. Same `AutoRotationController.cs` location, gated on `ActionAttackType.Spell`. New `Silence = 7` constant added to `ALL.Debuffs`.
- **DismountOnAbility toggle (default ON).** Auto-dismounts when the plugin tries to fire an ability (`ActionType.Action`) while the player is mounted (`ConditionFlag.Mounted` or `RidingPillion`). The triggering press is swallowed -- user re-presses after dismount completes. Renders in the settings UI under Rotation Behavior Options via the existing `[SettingCategory]` / `[Setting]` attribute system. Originally drafted in the May 27 handoff; finally shipped here in `Data/ActionWatching.cs` `UseActionDetour` and `Core/Configuration.cs`.

### Fixed
- **Hardcoded version regression repaired.** The earlier `b30be61a1` feat commit set `<Version>1.0.4.8</Version>` in the csproj (carried over from a copy-pasted script template), which is *lower* than the previously shipping v1.0.4.22. Dalamud only offers updates when the manifest `AssemblyVersion` is higher than installed, so every existing user was stranded on 1.0.4.22 without the new debuff handling. Corrected to 1.0.4.23 in follow-up commit `8859aed55`. csproj, `pluginmaster.json`, embedded `GluttonyCombo.json`, and `latest.zip` all consistent.

### Notes
- This release bundles two distinct features (debuff handling + DismountOnAbility) because the dismount work from May 27 had never actually been committed. They share no code.

## v1.0.4.22 (2026-05-21)

### Added
- **Dynamic BossMod / BossModReborn target-distance adjustment on job change.** New `SetMaxDistanceToTarget(float)` IPC helper in `Services/IPC_Subscriber/BossMod.cs` reaches into BossMod's internal `_ai.Config.MaxDistanceToTarget` field via reflection (`GetFoP` / `SetFoP`). Wired into `GluttonyCombo.cs` `onJobChanged` to set role-appropriate distances:
  - Tank, MeleeDPS -> `3f`
  - Healer -> `15f`
  - RangedDPS, MagicalDPS -> `20f`
- Falls back silently if BossMod isn't loaded or the field reflection fails.

## v1.0.4.21 (2026-05-21)

### Changed
- **Merged WrathCombo 1.0.4.6+ upstream into `CanQueueActionDetour`** (`Data/ActionWatching.cs`). The detour now computes additional-recast-group remaining time directly from `additionalRecastGroupDetail->Total - Elapsed` and blends it with the main recast group via `Math.Max`, instead of the older charges-based `CooldownTotal / charges - CooldownElapsed` math. More accurate queueing window detection during oGCD weaves. The `QueueAdjust` config now controls the *threshold* (default 0.5s when disabled) rather than gating the detour itself.

### Added
- **Locale resource DLLs** shipped with the upstream merge: `latest/de/`, `latest/fr/`, `latest/ja/`, `latest/ko/`, `latest/zh-Hans/`, `latest/zh-Hant/` -- each with a satellite `GluttonyCombo.resources.dll`.

## v1.0.4.20 (2026-05-21)

### Added
- **Dark Knight (DRK) Blackest Night (TBN) Enhancements**:
  - Automatically casts TBN when an incoming tankbuster is detected (uses `HasIncomingTankBusterEffect()`).
  - Automatically casts TBN on cooldown during trash pulls (when 3 or more enemies are targeting the player), bypassing the normal health threshold gates.
  - Added new target utility helper `EnemiesTargetingPlayerCount()` to reliably track current hostile aggro count.

## v1.0.4.19 (2026-05-18)

### Fixed
- **Targeting loop during Pyretic.** Pyretic damage stopped in v1.0.4.17 but Gluttony autorotation kept running because the `NoActStatus` hardcoded ID list (960 / 1387 / 2127) didn't match the latest dungeon's Pyretic variant. `Run()` kept swapping targets, AutoDuty cleared them, `Run()` swapped back.
- **Swapped `NoActStatus.Active()` for Wrath's `CustomComboFunctions.PlayerHasActionPenalty()`** at both call sites. Builds the Pyretic lookup dynamically from Lumina's status sheet by icon (215647) plus encounter-specific IDs, plus Acceleration Bomb expiry timing.
- **Deleted `Data/NoActStatus.cs`** - superseded.
- **Cleaned up the duplicate `using GluttonyCombo.Data;` lines** in `AutoRotationController.cs` (v1.0.4.17 patch script bug).

## v1.0.4.18 (2026-05-18)

### Fixed
- **Gluttony auto mode now works while AutoDuty is running a duty.** The v1.0.4.13 `ShouldYield` gate at `ShouldSkipAutorotation` shut down Gluttony for the entire duration of AutoDuty operations (combat, navigation, all of it), not just during mechanics. Joey hit this in normal play: auto mode did nothing while AutoDuty was active, then resumed the second AutoDuty stopped. Gate removed. Pyretic / Acceleration Bomb safety from v1.0.4.17 remains intact via `NoActStatus` at both `ShouldSkipAutorotation` and `UseActionDetour` - the actual protective mechanism. The AutoDuty yield was a workaround for the same problem with collateral damage; cutting it lets autorotation do its job during normal combat phases.

### Notes
- `AutoDuty.cs` IPC subscriber stays instantiated. Cheap to keep around in case we want a finer-grained gate later (e.g., yield only on specific untarget mechanics), without re-introducing the broken blanket yield.

## v1.0.4.17 (2026-05-18)

### Fixed
- **Pyretic / Acceleration Bomb safety (was killing the player).** v1.0.4.13 added an AutoDuty-yield check at `ShouldSkipAutorotation`, but Gluttony was still firing actions during Pyretic via two paths the yield didn't cover: (a) AutoDuty's already-in-flight queued action draining after the status lands, and (b) Gluttony's `UseActionDetour` intercepting and combo-replacing the call. Joey verified by dying on the first boss of the latest dungeon. New `Data/NoActStatus` helper checks for status IDs 960 (Pyretic) and 1387/2127 (Acceleration Bomb), and is now gated at both `AutoRotationController.ShouldSkipAutorotation` (suppresses plugin-driven autorotation) and at the top of `ActionWatching.UseActionDetour` (returns `false` to swallow any UseAction call entirely while the status is active, no matter who queued it).

### Removed
- **Hold-to-Repeat is gone.** Moved to a standalone plugin (LazyPress, coming separately) so it doesn't ride along with the combo plugin. Removes `Configuration.HoldToRepeatEnabled`, the `OnFrameworkUpdate` block, the `ActionWatching.OnActionSend` subscriber, and the `_holdLastUserPressTickMs` / `_holdSelfFireWindowEndMs` trackers.

## v1.0.4.16 (2026-05-18)

### Changed
- **Hold-to-Repeat re-implemented as event-driven button-state detection.** The v1.0.4.15 version still used `TimeSinceLastAction` as a proxy for "button still held," which was fragile in practice. The new implementation subscribes to `ActionWatching.OnActionSend` and uses the game itself as the held-button oracle: while the user holds a hotbar button, the game's input layer queues an auto-fire at each GCD which keeps our press tracker fresh; when the user releases, the game stops queueing and the tracker goes stale within 350ms. A short self-fire suppression window (80ms) prevents our own `UseAction` call from feeding back into the tracker and looping forever. Removed the v1.0.4.15 self-cooldown gate (no longer needed).
- **Subscribe/unsubscribe wiring**: `ActionWatching.OnActionSend += HoldToRepeat_OnActionSend` added next to the `AutoDutyIPC = new()` init; matching unsubscribe in `Dispose()`.

### Notes
- Still default-OFF. Enable under Main UI Options > Hold to Repeat.
- The 350ms gate is set wider than a frame interval but tighter than half a GCD, which catches every game-side queue refresh while held but releases promptly on let-go.

## v1.0.4.15 (2026-05-18)

### Fixed
- **Install/Update failure (CRITICAL)**: `pluginmaster.json` advertised AssemblyVersion 1.0.4.14 but the bundled `GluttonyCombo.json` inside `latest.zip` reported 1.0.4.12 - the csproj `<Version>` was never bumped in v1.0.4.13 / v1.0.4.14. Dalamud rejected the install with a manifest mismatch and silently uninstalled the plugin on attempted update. csproj `<Version>` is now `1.0.4.15` and `pluginmaster.json` matches.
- **AutoDuty IPC integration (was a no-op)**: `Services/IPC_Subscriber/AutoDuty.cs` subscribed to `AutoDuty.isRunning` / `AutoDuty.isPaused` / `AutoDuty.currentState` - none of which AutoDuty exports. Every call threw, `SafeInvoke` swallowed the error, `ShouldYield` was `false` forever, and the targeting-loop fix from v1.0.4.13 never actually engaged. Rewritten using the project's `ReusableIPC` pattern with the correct PascalCase IPC names (`AutoDuty.IsStopped`, `AutoDuty.IsNavigating`); `ShouldYield` is now `IsEnabled && !IsStopped` so Gluttony yields rotation + targeting whenever AutoDuty has the wheel.
- **Hold-to-Repeat runaway**: the v1.0.4.13 gate was `TimeSinceLastAction < 1500ms`, which would self-perpetuate (each plugin re-fire resets the timer) and could spam an action indefinitely. Tightened the window to 400ms and added a 2600ms self-cooldown so at most one assist fire per GCD cycle, with hard stop the moment the user stops feeding fresh button presses.

### Notes
- Hold-to-Repeat remains disabled by default and is still time-based; a future revision will hook `UseActionDetour` for true button-state detection.

## v1.0.4.14 (2026-05-18)

### Added
- **Hold to Repeat**: New toggleable option (Main UI Options > Hold to Repeat) that continuously fires your last combo-replaced action while you hold the hotbar button. Automatically respects GCD, animation lock, and casting state. Stops immediately when you release the button.

## v1.0.4.13 (2026-05-18)

### Fixed
- **AutoDuty IPC Integration**: Added AutoDuty IPC subscriber to detect when AutoDuty has paused for mechanics (Pyretic, Untarget, etc.). Gluttony now yields autorotation and target acquisition when AutoDuty is in control, preventing the targeting loop where AutoDuty clears the target and Gluttony immediately retargets.
  - New file: `GluttonyCombo/Services/IPC_Subscriber/AutoDuty.cs`
  - Patched: `GluttonyCombo/GluttonyCombo.cs` ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â initializes and disposes AutoDuty IPC
  - Patched: `GluttonyCombo/AutoRotation/AutoRotationController.cs` ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â checks `AutoDutyIPC.ShouldYield` in `ShouldSkipAutorotation()`

---
*Previous versions: see release tags on GitHub.*
