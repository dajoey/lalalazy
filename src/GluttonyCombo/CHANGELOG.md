# Gluttony Combo â€” Changelog

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
