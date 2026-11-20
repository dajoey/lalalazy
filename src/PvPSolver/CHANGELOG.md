## v0.1.2.31 (2026-11-20) [testing]
- Round 6 completion: the post-release marker grep still found four generated StatusID entries whose description text carries a marker word (ThrownForALoop, Soteria, BackFromTheBrink, BannerOfHonedAcuity). All four are unreferenced PvE statuses and are removed from the generated table; no code reads them.
- No code change and no PvP behaviour change; every remaining marker hit is a cited part of a PvP path (the Sage Kardia and Dancer partner targeting, and the tank lock-on VFX check).
- Testing channel only; the production channel is unchanged.

## v0.1.2.30 (2026-11-20) [testing]
- PvE remnant removal, round 6: PvE-only engine paths that no PvP rotation reads are removed; no behaviour change in PvP.
- Removed the PvE party-buff target finders no PvP action uses (ActionTargetInfo.FindTheSpear, FindTheBalance, FindPhantomRespite), their TargetType members and the Spear/Balance card priority lists with their Job-tab editors and saved files. The DNC partner and SGE Kardia tank priorities remain: Closed Position (PvP) and Kardia (PvP) still target through them.
- Removed the provoke/shirk machinery: the AutoStatus Provoke and Shirk flags, the ProvokeAbility and ProvokeGCD virtuals, StateUpdater.ShouldAddProvoke, DataCenter.ProvokeTarget and FindProvokeTarget, the "Provoke anything not on the no provoke list." setting, the ProvokeDelay setting, and the "Don't provoke" territory list with its saved file.
- Removed the PvE event-status gates from the heal and ability paths (Scalebound, ShackledHealing, ShackledAbilities) and from the command path (Transcendent); the heals and abilities they gated are unchanged.
- Removed the tank-buster target list (DataCenter.TankbusterTargets, ActionTargetInfo.FindTankbusterTarget). The generic tank lock-on VFX check stays: the PvP defense gate reads it through DataCenter.IsHostileCastingToTank.
- Removed the positional leftovers: CustomRotation.CanHitPositional, ActionSetting.EnemyPositional, ObjectHelper.FindEnemyPositional, HasPositional, IsBossFromIcon, GetObjectNPC, the EnemyPositional enum, and the boss-position fallback of the ground-target finder.
- Removed 17 marker-named PvE statuses from the generated StatusID table that no code references any more (Transcendent and Transcendent_2648, TheBalance 829/1338/1882/3887, TheSpear 832/1885/3889, Scalebound, ShackledHealing, ShackledAbilities, DancePartner_2027, BannerOfTranscendentFinesse, SoulsInTheBalance, RingingRespite, DirectionalDisregard).
- Testing channel only; the production channel is unchanged.

## v0.1.2.29 (2026-11-20) [testing]
- PvE remnant removal, round 5 completion: the remaining PvE marker hits of the previous build are removed; no behaviour change in PvP.
- Removed the six PvE boss-specific lock-on effect paths (Necron, Kam'lanaut and four Arcadion tank-buster effects) from DataCenter.TankbusterPaths; the two generic tank lock-on effects remain.
- Removed seven chocobo-race statuses that no code references from the generated StatusID table (Heavy_630, Tireless, Frenzied, Lamed, Distracted, Mimic, FeatherField).
- Testing channel only; the production channel is unchanged.

## v0.1.2.28 (2026-11-20) [testing]
- PvE remnant removal, round 5: the PvE engine mechanics that the earlier rounds left in place are removed, so the source describes a PvP plugin only; no behaviour change in PvP.
- Auto True North removed: the "Auto True North (Melee DPS)" setting, StateUpdater.ShouldAddPositional, the 22-entry action-to-positional map (ConfigurationHelper.ActionPositional) and StatusHelper.NoPositionalStatus are gone, so the 22 PvE action ids that only that map named and the True North status leave the generated ActionID and StatusID tables. The Dispel/Stance/Positional special command keeps its Positional flag and its numeric value; its description now reads "Open a window to use Esuna."
- Tank-stance settings removed: "Automatically activate tank stance" and "Auto provoke when there is another tank in party" (saved values are ignored on load), StateUpdater.ShouldAddTankStance and the alliance-tank counting helpers, the CustomRotation.TankStance virtual (always null: no PvP rotation overrides it), CustomRotation.HasTankStance and AutoStatus.TankStance. The provoke condition no longer depends on the removed setting.
- Blue Mage removed: BlueMageRotation with its generated base class, DataCenter.BluSlots and BluRole, the blue-mage branch of ActionBasicInfo.IsOnSlot, ActionSetting.RequiredBluSlotActionId, the Mimicry target finder (TargetType.MimicryTarget) with the unused CombatRole enum, ActionUpdater.UpdateSlots, the Phantom Flurry and Waning Nocturne checks, the "Set Blue Mage Actions Automatically" setting, the BLU entries of the job tables and the Blue Mage statuses (Aetheric Mimicry, Nocturne, Basic Instinct, Surpanakha's Fury, Boost, Harmonized, Phantom Flurry).
- Chocobo companion handling removed: the solo-with-chocobo party shortcut and chocobo filter in the two card-target finders of ActionTargetInfo, ObjectHelper.IsPlayerCharacterChocobo and CustomRotation.HasChocobo.
- Job traits removed: the per-job ...Trait properties and AllTraits of the generated base rotations, IBaseTrait, BaseTrait, ICustomRotation.AllTraits, and the trait-gated PvE gauge helpers (Ninja HasJin, Gunbreaker MaxAmmo/NormalMaxAmmo/OvercappedAmmo/IsAmmoCapped, Sage Addersgall/Addersting/AddersgallTime/AddersgallEndAfter/AddersgallEndAfterGCD, Warrior OnslaughtMax, Bard BloodletterMax, Viper MaxRattling/MaxAnguine, Black Mage IsPolyglotStacksMaxed, Pictomancer MaxStrikingMuse/Level100) with their Debug-tab lines; no PvP rotation read any of them.
- Raise statuses removed from the generated StatusID table (Raise, Raise_1140, Reraise); JobFilterType keeps only None and NoJob, the only values any setting uses.
- Kept on purpose: ObjectHelper.IsEnemy still probes hostility with the generic Blizzard action and ActionManagerHelper still reads the recast of the generic Heated Split Shot, because no PvP action id is proven to behave the same for those two probes.
- Testing channel only; the production channel is unchanged.

## v0.1.2.27 (2026-11-20) [testing]
- PvE remnant removal, round 4 (whole-plugin audit leftovers): inert PvE text and dead code that no PvP path reaches are removed; no behaviour change in PvP.
- Source generator catalog (PvPSolver.SourceGenerators/Properties/Resources.resx): the generated tables now carry PvP content only. ActionID drops from 1,904 to 382 members (1,522 PvE action ids removed; every PvP member and the few ids the engine still probes are kept), CustomRotation and the 21 job base rotations drop their per-action PvE properties (1,424 to 348 action entries, including the PvE limit-break overrides), StatusID drops 62 PvE-content statuses (Fate, Eureka, Variant, Occult, food and duty-action effects), and the unused TerritoryContentType enum (duty, raid, Eureka and other PvE content types) is gone with its generator method. The resource file shrinks from 4.3 MB to 2.2 MB.
- Raise leftovers removed: AutoStatus.Raise, JobFilterType.Raise, CustomRotation.ShouldSkipAction (nine no-op callers in CustomRotation_GCD), the CustomRotation.Raise virtual, ActionBasicInfo.RaiseAction, the cancel-cast-on-Raise-status check in MiscUpdater, and the unused ObjectHelper.IsFriendly extension with its Raise probe.
- The isDutyAction constructor parameter and the IsDutyAction flag are removed from BaseAction and ActionBasicInfo, along with the matching context-menu test; the generated action constructors no longer pass it.
- The PvEFilter attribute chain (UIAttribute, ISearchable, Searchable, SearchableCollection and seven Configs entries) is removed; the legacy settings window applies only the PvP visibility filter.
- Removed 89 UiString entries that had no call sites.
- The Debug tab no longer lists the alliance member count or the chocobo flag.
- Removed PvE-only engine tables and checks no PvP action can match: IActionHelper.HealingActions and MovingActions (with the melee-range check that read MovingActions), the PvE entries of the special-ability list and the Liturgy of the Bell range override in ActionTargetInfo, the Sprint icon override, the White Mage Freecure and Thin Air MP checks with ThinAirState and HasThinAir, the Passage of Arms, Flamethrower and Improvisation movement-lock checkboxes (Extra settings, saved values ignored), and the commented-out Passage of Arms and Collective Unconscious code.
- Testing channel only; the production channel is unchanged.

## v0.1.2.26 (2026-11-20) [testing]
- Follow-up cleanup to 0.1.2.25 from the whole-plugin PvE audit; no behaviour change in PvP.
- The "Use beneficial ground-targeted actions" tooltip no longer describes trial and raid fallback positions; it now says arena.
- Removed two duty-specific shared-damage VFX paths (an alliance raid and a dungeon boss) from the shared-damage path list in DataCenter.
- Removed the unused UiString entries for the old Supporters heading and the Raise/Shirk condition set, and about 40 lines of commented-out legacy PvE target code (TargetUpdater, TargetHostileType, CustomRotation_OtherInfo) plus several comments that still named duty, raid or Fate content.
- Testing channel only; the production channel is unchanged.

## v0.1.2.25 (2026-11-20) [testing]
- PvE remnant removal, round 3 (whole-plugin audit): the remaining PvE-only settings, commands and code paths are gone, so the settings windows, commands and help text describe a PvP plugin only.
- Raise: every Raise/resurrection setting (Auto > Healing), the dead-player target selection (TargetUpdater), the Raise/Shirk special command with its Control-window button, and the Raise Info debug panel are removed. Raise could never run in PvP (DataCenter.CanRaise returned false there).
- AutoDuty and Henchman operating modes are removed: the AutoDuty and Henched state commands, the AutodutyChangeOperatingMode IPC call and the IsAutoDuty and IsHenched flags. The Off and PvP states are unchanged and PvP keeps its numeric value.
- Action interception is removed: the UseAction hook (ActionQueueManager), the Intercepted Action window, the nine intercept settings, the per-action Intercepted toggle and the Intercepting special state. It never ran in PvP (the hook was gated on not being in PvP).
- Settings removed: auto-off on duty completion, auto-on when the alliance or a field operation is in combat, out-of-duty movement speed, Cactbot timeline integration (two settings), Fate target filtering, Hunt/Relic/Leve target priority, party NPC and solo-instance NPC healing, the chocobo and focus-target party-member options, and the Solo Deep Dungeons targeting mode.
- Dead PvE code removed: alliance-raid detection and the raid timer (DataCenter), duty-action tracking, Variant and Occult status handling, the Wrath Combo IPC subscriber, the raid language table, the supporters list and its unused window code.
- The hidden Duty settings tab is now the PvP specific tab (it only holds the PvP specific settings) and the Duty Info debug panel is now the Territory Info panel.
- RotationHarness shape checks updated for the removed AutoDuty state function (308 checks).
- No changes to the 21 PvP rotation bodies; removed command and mode values keep the numeric values of the remaining ones, so saved settings and IPC callers are unaffected.
- Testing channel only; the production channel is unchanged.

## v0.1.2.24 (2026-11-20) [testing]
- PvE remnant removal, round 2: the whole duty-rotation engine, BMR boss-timeline integration, raid/fate/occult-crescent targeting (special-immune chain, M10S bro targeting, Forlorn/Fate/quest-mob priority settings), Territory Info rotation hooks, and the duty rotation command are gone. PvPSolver now reads and behaves as a complete, purpose-built PvP plugin.
- RotationHarness shape checks updated to the PvP-only reality; BmrReadyHarness retired (it asserted the removed BMR integration).
- No changes to the 21 PvP rotation bodies; targeting and action engine used by PvP paths is intact.
- Testing channel only; the production channel is unchanged.

﻿## v0.1.2.23 (2026-11-20) [testing]
- PvE remnant removal, round 2: the duty-rotation engine is gone. Removed: the whole Basic/Rotations/Duties subsystem (DutyRotation and the Bozja, Emanation, Monster Hunter, Orbonne, Phantom and Variant duty shells, DutyTerritoryAttribute), the DutyRotations command and its command handling, the duty-rotation hooks, settings rows and command plumbing in the rotation updaters, config classes and the rotation config window (including the duty tab), the Masked Carnivale helper, and the duty-rotation template in the source generators (a 620 KB PvE duty catalog that no longer ships).
- The BossModReborn boss-timeline integration is removed: the BMRTimeline IPC subscriber, the BossMod updater and its download path, the timeline raidwide/tankbuster/knockback mitigation windows, the timeline debug panel and its config toggles. The plugin no longer talks to Boss Mod Reborn.
- The last PvE-only catalog entries are scrubbed from the source generators: the Mega, Soothing, Resistance, Resistance Kit and Occult potion actions, the Dungeons, Deep Dungeons and V&C Dungeon Finder content types, and the To-the-Dungeons status.
- A regression from the removal edit is fixed before release: the DoActions, ToggleActions, Settings and NextAction command cases were briefly lost with the duty command and are restored verbatim from 0.1.2.22.
- Removed config keys no longer exist; saved settings that mentioned them are ignored on load.
- No PvP rotation behavior change; the 21 PvP rotations are untouched.
- Testing channel only; the production channel is unchanged.

## v0.1.2.22 (2026-11-20) [testing]
- PvE remnant removal: the entire potion/item auto-use subsystem is gone. Removed: HP potion, MP potion, medicine and Phoenix Down item actions (Basic/Actions/HpPotionItem.cs, MpPotionItem.cs, MedicineItem.cs, PheonixDownItem.cs), the item action base (IBaseItem, BaseItem, ItemConfig), the tincture-usage setting and enum (Configs.TinctureType, TinctureUseType), the HP/MP potion and Phoenix Down auto-use settings and thresholds, the per-rotation item config store, the cooldown window's items filter and its Show Items' Cooldown setting, the teaching-mode item hotbar highlight, the next-action "Item" grouping, the config window's item debug panel, and the Potion auto-use calls in the ability path (CustomRotation_Ability).
- This backs out the 0.1.2.21 potion port (upstream RotationSolverReborn 996f281830) and its whole subsystem. Items cannot be used in PvP; the plugin no longer carries item logic at all.
- Removed config keys no longer exist; saved settings that mentioned them are ignored on load.
- No PvP rotation behavior change; the 21 PvP rotations are untouched.
- Testing channel only; the production channel is unchanged.

## v0.1.2.21 (2026-11-20) [testing]
- Upstream engine sync (RotationSolverReborn 996f281830, healer rotation and item usage update): the HP and MP potion items read an item's base potion data when the item carries no high-quality data, so a potion without high-quality values no longer reports a zero heal or restore amount (HpPotionItem, MpPotionItem).
- A potion is not used while the character is dead, and the HP potion checks the HP threshold before the heal-size check (HpPotionItem.CanUse, MpPotionItem.CanUse).
- HP and MP potion auto-use never runs in PvP (CustomRotation_Items.UseHpPotion, UseMpPotion).
- Not carried: upstream's healer, Summoner, Reaper, Red Mage, Ninja and Pictomancer PvE rotation edits, the new healer helper, the Adloquium tank priority list, the regeneration-potion handling for Eureka, Palace of the Dead and Deep Dungeon items, the new item-usage settings (out-of-combat use, time-to-kill limit, no-healer limit) and the Auto > Item Usage page, the main window changes, and a reworded warning and plugin-compatibility entry about another plugin. This plugin does not contain those parts.
- No rotation behavior change; the 21 PvP rotations are untouched.
- Testing channel only; the production channel is unchanged.

## v0.1.2.20 (2026-11-18) [testing]
- Upstream engine sync (RotationSolverReborn c170c9cd5d, source reorganization): upstream moved its engine, configuration and rotation code into new folders and namespaces and split several large files into partial classes (ActionTargetInfo, DataCenter, ObjectHelper, StatusHelper, Configs). A line-by-line comparison of the old and new files found no behavior change in any of them, so this plugin keeps its own layout and namespaces.
- All 21 PvP rotations now declare game version 7.58, and their source-code links point at upstream's new rotation folders. Metadata only; no rotation behavior change.
- A saved rotation choice is matched by full name, by short name or by the trailing name segment, so a rotation still resolves if its namespace moves (RotationUpdater.MatchesRotationName).
- The settings code generators group fields by namespace, kind and class name instead of by syntax node, so one type declared across several files produces a single generated settings class (ConditionBoolGenerator, JobConfigGenerator, JobChoiceConfigGenerator).
- Not carried: upstream's folder and namespace renames, Extra and Reborn PvE rotation edits, the Rotation template namespaces in the PvE generator resources, the main window contributor list, and the RebornMaterial package bump (this plugin does not use that package).
- Testing channel only; the production channel is unchanged.

## v0.1.2.19 (2026-11-17) [testing]
- Upstream engine sync (RotationSolverReborn 866988a7ee, status and target check improvements): the status reads behind target selection use the game's status array directly. Hostile-target filtering, invincible and priority status checks, dispel and dangerous-status detection, and the status-lock check read it without building status objects first (StatusHelper.GetStatuses, TargetUpdater, ObjectHelper, ActionUpdater).
- The check for a full status list reads the status slots directly, and the applied-status lookups read the effect-time cache without touching the status list. The effect-time window is measured on a monotonic tick clock, so a system clock change can no longer extend or cut it (DataCenter.StartEffectTime, Watcher).
- Top-priority hostile checks are cached for 15 ms per target to cut repeated work in the target scan (ObjectHelper.IsTopPriorityHostile). Time-to-kill and resistance checks take their limit from the caller, and the cone and line target checks reuse precomputed trigonometry (ActionTargetInfo).
- Rotation settings that hold a decimal number parse with the invariant culture first and the current culture second, and reject NaN and infinity. The timed action command parses the time the same way, and the IPC action command passes the trimmed action name with the time rounded to three decimals (RotationConfigFloat, RotationConfigBase, RSCommands_OtherCommand, IPCProvider).
- State commands that name a targeting type also accept a numeric index, and the targeting index wraps correctly in both directions (RSCommands_BasicInfo, RSCommands_StateSpecialCommand).
- Delayed target switching ignores a new request while one is still waiting and looks the candidate up by object id when the delay ends, so a target that despawned in between is skipped (RSCommands_Actions.SetTargetWithDelay). Macro targets are remembered by object id instead of by object reference (MacroItem).
- The Disable Auto Rotation call to WrathCombo is limited to one per second (IPCSubscriber).
- HP and MP potion use respects the two potion settings before choosing a potion, and the potion items pass the clipping check through (CustomRotation_Items, HpPotionItem, MpPotionItem, BaseItem).
- When the saved configuration cannot be read, a copy is kept as PvPSolver_LoadFailed.json before settings reset (RotationSolverPlugin). The disabled-hotbar highlight reads hotbar slots by reference (HotbarDisabledColor).
- Not carried: upstream changes to rotations and windows this plugin does not contain (extra and Reborn PvE rotations, the main window, the auto-attack updater, the changelog json), and upstream's wider rewrite of the status reader methods, which this plugin's own status code does not share a base with. Defensive null checks on the status list stay in place.
- Testing channel only; the production channel is unchanged.

## v0.1.2.18 (2026-11-15) [testing]
- The first-start tutorial has a new look and walks through the PvP Solver settings window tab by tab, with buttons that open the matching tab.
- The control and cooldown windows have a clean bordered look, and an unlocked window can be moved by a small handle along its top edge.
- The full legacy settings window has a matching header that shows the plugin version, with a Reset button and a close button. The reset and restore confirmations in the Advanced tab no longer show a plain title bar.
- The What's new window and the Report a problem popup now use the same custom window style as the plugin windows.

## v0.1.2.17 (2026-11-15) [testing]
- The settings window was polished for the real in-game font and scale: text no longer clips or sits against card borders.
- The window uses a header band with a brand chip, a drawn close button, a fixed-width status pill, a section band and a footer with the main action.
- Settings are rows inside cards, with the label and a muted help line on the left, the control on the right and a Reset beside any value that differs from its default.
- Slider values are printed beside the track and still open for typing with Ctrl+click; the target priority list has Up, Down and Remove buttons and an In use now tag, and the job picker shows one row per role.
- Buttons and link chips wrap instead of clipping, dropdowns are as wide as their longest option, and the reset and restore confirmations are centred over the window with a dimmed backdrop.

## v0.1.2.16 (2026-11-15) [testing]
- The settings window has been rebuilt as a responsive card-based shell with custom two-level navigation, toggle tracks, styled sliders and choices, a custom close button in place of the native title bar, and a persistent Stop footer. Rotation, targeting, presets and saved settings are unchanged.

## v0.1.2.15 (2026-11-14) [testing]
- The plugin windows have been rebuilt on the shared LalaUiKit: one consistent layout with themed section headers, unified spacing and colour accents. All settings and behaviour are unchanged, and existing configuration loads as-is.

## v0.1.2.14 (2026-11-10) [testing]
- Rebuilt against the Dalamud 15.0.4.0 payload for game patch 7.58. No functional changes.

## 0.1.2.13 (2026-11-09) [testing]

- **Removed the AI decision layer.** The optional relayed decision model (focus picking, Limit Break firing and the healer-call shadow lines, with their "call the shots" settings row and its relay address field) is gone from the plugin. No fight snapshot is built or sent anywhere anymore, and the plugin makes no decision calls of any kind.
- Targeting, Guard, Recuperate, the defensive table, StickyTarget and every other rotation behavior are unchanged. The defensive rules keep reading real HP (current HP out of the maximum) under the neutral name `RealHp.HpRatio`; no threshold or default moved.
- PvP Solver still never moves the character and never fires a PvP Limit Break on its own logic, exactly as before.
- Testing channel only; the production channel is unchanged. Not verified in play yet.

## 0.1.2.12 (2026-11-06) [testing]

- **The Jev co-pilot logs the plain code rule's answer beside Jev's, for both of its questions.** Each `JV|focus-pick` line names the enemy Jev picked and the enemy the plain rule would pick (the lowest-HP enemy in attack range); each `JV|lb-shadow` line names Jev's Limit Break call and the code rule's. Both answers are recorded side by side and graded after the session, to show which one decides better. Log only: targeting, Limit Break firing and every other behavior are unchanged.
- Testing channel only; the production channel is unchanged. Not verified in play yet.

## 0.1.2.11 (2026-11-05)

- Upstream engine sync (RotationSolverReborn a560469db): the rotation engine now builds in upstream's merged single-project layout.
- Ports upstream engine fixes: the action manager is read at call time instead of a pointer cached at startup (a stale-pointer crash class), configuration saves are atomic (write to a temp file, then replace), and the shield-value calculation can no longer overflow on very large HP values.
- No rotation behavior change; the 21 PvP rotations and their presets are untouched.

# Changelog - PvP Solver

## v0.1.2.10 (2026-11-01) [testing]
### Fixed
- **The per-job defensive actions also read real HP.** Heart of Corundum, Tempera Coat, Earth's Reply, Riddle of Earth, the Bloodbath rows, Lady of Crowns, Microcosmos, Meisui, Impalement, Xenoglossy's low-HP arm and Shadowbringer's HP arm compared their "use below this HP" thresholds against predicted HP, which counts heals still on the way; predicted can read 84% one second before a death at about 40% real HP. They now compare against the current HP out of the maximum, the same real value Guard and the defensive table already use. Thresholds and defaults are unchanged, and reads of enemies' and allies' HP (Smite, Guardian, Blasting Zone, Perfectio, the burst target) are untouched.
- Testing channel only; the production channel is unchanged. Not verified in play yet.

## v0.1.2.9 (2026-11-01) [testing]
### Fixed
- **Self-defence now reads real HP.** Guard, Recuperate and the generic defensive actions (the "use below X% HP" table) compared their thresholds against predicted HP, which counts heals still on the way; predicted can read 84% one second before a death at about 40% real HP, so nothing fired. All of them now compare against the current HP out of the maximum, the same real value the Jev snapshot uses. Thresholds and defaults are unchanged.
- Testing channel only; the production channel is unchanged. Not verified in play yet.
## v0.1.2.8 (2026-10-31) [testing]
### Fixed
- **"Let Jev call the shots" now sees real HP.** The player's and allies' HP sent to Jev is the current HP out of the maximum, no longer PvP Solver's predicted HP, which counts heals still on the way. In one match Jev was told 84% one second before a death at about 40%.
- **Jev's pick no longer drops when Jev is briefly unsure.** The relay now keeps the last pick itself while that enemy is still hittable, and drops it on purpose when the enemy goes under Guard, out of reach or stale. PvP Solver follows the relay's call and never brings a dropped pick back. Only with an older relay does PvP Solver keep the last pick, for up to three seconds after Jev gave it, while that enemy is still a valid target.
- **The Limit Break log matches what happened.** `JV|lb-fired` is written once when the Limit Break goes off in a fire window. `JV|lb-fire-skip` is written only when a window closes without it, with the reason: unusable, follow-up, out-of-range, no-target or timeout. A report of the use that arrives up to two seconds late still counts as fired. Before, a skip line could appear in windows that then fired, and windows that really failed wrote nothing.
- **Each action used is reported once.** The game reports each action twice; the same action on the same target within 1.5 seconds now counts once in the snapshot.
### Changed
- **Alliance members are included (Frontline).** After the party members, the nearest alliance members are sent too, marked as alliance, up to eight allies in all. Each ally now carries its object id, so Jev can name an ally to heal.
- **Healer call, logged only.** When Jev suggests whom a healer should heal (an ally, self, attack or Guard), the suggestion is written as one `JV|heal-shadow` log line for the first suggestion in each fight and each time it changes; an answer with no suggestion does not count as a change. Nothing acts on it yet.
- **Relay failures are visible in the log.** One summary line per fight (when combat ends, or every 60 seconds while calls fail) counts calls that succeeded, timed out, got an error reply or failed. No line per call, and no call is counted in a later fight. An unreadable heal or Limit Break value in a reply no longer drops the rest of the answer.
- PvP Solver still never moves the character.
- Testing channel only; the production channel is unchanged.

## v0.1.2.7 (2026-10-29) [testing]
### Changed
- **"Let Jev call the shots" reports what actually happened.** Each snapshot also carries the player's current target and the actions actually used since the last one (action and target only), so each of Jev's picks can be checked against what PvP Solver did and whether that enemy went down.
- Jev's focus pick is kept steady: it moves to another enemy only when that one is clearly the better pick.
### Fixed
- **A line, cone or splash Limit Break that Jev called could be skipped** because the action kept its own "most enemies" target. It is now aimed at the enemy Jev chose, which the game has just confirmed as a valid target. A skipped fire writes one reason line to the log.
- Testing channel only; the production channel is unchanged.

## v0.1.2.6 (2026-10-29) [testing]
### Added
- **"Let Jev call the shots" now fires the PvP Limit Break.** When the Limit Break is ready, Jev is asked about this job's own Limit Break (what it does, from the game's description) with the facts that decide it: the damage against each enemy's remaining HP, Guard, Kuzushi and low-HP executes, how many enemies a line, cone or area would catch, and which allies are hurt or under attack. It fires when that wins a kill, catches several enemies, or saves the player or an ally, and holds otherwise. Only enemies the game says the Limit Break can be used on right now are offered, and the game is asked again just before firing.
- The focus pick also weighs each enemy's remaining HP and how many allies are already attacking it.
- With the setting off, or the relay slow or down, no Limit Break is fired (as before).
- Testing channel only; the production channel is unchanged.

## v0.1.2.5 (2026-10-29) [testing]
### Added
- **"Let Jev call the shots"** (Targeting tab, off by default). About once a second in a PvP fight, a snapshot of jobs, HP, distance, Guard, invulnerability and crowd control (never player names) goes to a decision relay on the home network. Jev, a decision model, picks the enemy to focus; that enemy is attacked first whenever the current targeting mode allows it, after the stay-on-my-debuff-target rule.
- When the relay is slow, down or unsure, nothing changes: an answer older than three seconds is ignored and the usual targeting order applies.
- Testing channel only; the production channel is unchanged.

## v0.1.2.4 (2026-10-22) [testing]
### Changed
- **All 21 PvP rotations declare the upstream "Reborn" rotation type** (RotationSolverReborn 79f60d87c), upstream's tag for rotations written for the pre-Evercold job kits. Metadata only; rotations load and behave as before.
- Testing channel only; the production channel is unchanged.

## v0.1.2.3 (2026-10-18) [testing]
### Changed
- **All 21 PvP rotations declare game version 7.57**, and their source-code metadata points to the current rotation paths.
- **Paladin Guardian uses only the configured general-ability checks.** Duplicate attack-ability triggers that could bypass the HP and Hallowed Ground settings are removed.
- Testing channel only; the production channel is unchanged.

## v0.1.2.2 (2026-10-13) [testing]
### Fixed
- **When BossModReborn is unloaded, its timeline data (upcoming raidwides, tankbusters, knockbacks) is cleared once**, so proactive mitigation no longer acts on countdowns left over from before the unload.
- The last plugin lookup that could log an error on every call (the BossModReborn version lookup) reads the installed-plugin list instead.
### Changed
- The BossModReborn check writes one log line when it first finds (or does not find) BossModReborn and one each time it is loaded or unloaded, never one per frame.
### Notes
- Testing channel only; the production channel is unchanged. Not verified in play yet.

## v0.1.2.1 (2026-10-08) [testing]
### Fixed
- **BossModReborn integration was never detected, and it filled the log with errors.** The check for BossModReborn (and for WrathCombo) went through a lookup that cannot read BossModReborn's plugin object, so it reported the plugin as missing even while loaded, and wrote an error with a stack trace to the Dalamud log each time it was asked, once per frame while the settings window was open (over 11,000 lines in 35 minutes). The check now reads the list of installed plugins and quietly reports whether the plugin is loaded, so the BossModReborn timeline data is used when BossModReborn is running and the log stays clean.

## v0.1.2.0 (2026-10-08) [testing]
### Added
- **New settings window.** `/pvpsolver`, the plugin's settings button and Lazy Hub now open a new window with the settings that matter for PvP, in plain words with a help line under each. It has six tabs: Match (when PvP Solver turns itself on and off), Survival (Guard, Purify, Sprint and the defensives master switch), Targeting (who to attack, area attacks, burst, invulnerable enemies), My Job, Display and Advanced.
- **Settings can be changed in any zone.** Every setting, including each job's own options, can be edited anywhere and for any of the 21 jobs, not only for the played job inside a PvP zone. Only turning PvP Solver on still needs a PvP zone; its button says so. Job options need a character in the world.
- **Optional automatic defensives, all off by default.** Each is a "use when HP is below" setting with its own percentage, per job, in the My Job tab: Aquaveil (White Mage), Wreath of Ice (Black Mage), Tempera Coat (Pictomancer), Forte (Red Mage), Radiant Aegis (Summoner), Arcane Crest (Reaper), Snake Scales (Viper), The Blackest Night (Dark Knight), Holy Sheltron (Paladin), Bloodwhetting (Warrior), Curing Waltz (Dancer), and the PvP role actions Rampart (tanks) and Stoneskin II (healers), which can be used only while they are the selected role action. Recuperate keeps its existing rule (15,000 or more HP missing) and gains an optional second condition, an HP percentage. Default percentages run from 45 to 70.
- **Defensives master switch.** One switch in the Survival tab (on by default) turns all automatic defensives off at once. It also covers the six that were already automatic at a fixed HP: Heart of Corundum, Riddle of Earth, Lady of Crowns, Microcosmos, Meisui and Impalement, so turning it off stops those too.
- **Worst case of the defensives.** After one automatic defensive is used no other is used for 2.5 seconds, and none while a barrier, damage reduction or heal of the same kind from another turned-on defensive is still active, but with several turned on, more than one can still be used during one long stretch of low HP.
### Changed
- **Six fixed HP thresholds are now settings with unchanged defaults.** Heart of Corundum (Gunbreaker) at or below 30%, Riddle of Earth (Monk) below 80%, Lady of Crowns and Microcosmos (Astrologian) below 60%, Meisui (Ninja) below 50% and Impalement (Dark Knight) below 60% behave exactly as before until changed. Each can be moved or turned off in the My Job tab.
- **Job options are stored per job.** Options were stored under the job being played, and the first time a job was used in a session its options shared one store, so same-named options (the Bloodbath and Smite thresholds of the six melee jobs) could carry over from one job to another. Each job's options are now stored under that job. Saved settings are kept.
### Notes
- Testing channel only; the production channel is unchanged.
- The previous settings window is still available: Advanced tab, or `/pvpsolver advanced` (optionally followed by a tab name such as Actions).
- No saved setting is renamed or lost. The new settings use new keys that appear in the settings file only after they are changed.
- Checked offline only so far: the window layout, the automatic defensives with real targets and the job options of a job that is not being played have not yet been run in a match.

## v0.1.1.5 (2026-10-07) [testing]
### Fixed
- **Targeting selection in PvP.** Entering the PvP state forced lowest-HP targeting, so the configured targeting list and the focus-fire (`PvPHighestPressure`) mode could not be selected. The configured list now applies; with default settings targeting is unchanged (lowest HP first).
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.1.4 (2026-10-07) [testing]
### Fixed
- **Tank ability stall in PvP.** With several enemies in melee range and a tank's own mitigation on cooldown, the rotation stopped issuing every later ability until the situation ended. The defensive stage now hands over to the rest of the rotation in PvP.
- **Melee attack abilities considered at 5 yalms.** Melee jobs considered their attack abilities only with an enemy within 3 yalms although their weaponskills reach 5; in PvP the check now uses 5 yalms. Each ability still checks its own range.
- **Smite ignores Guard.** The target filter no longer excludes an enemy that has Guard when Smite is the action.
- **Sprint and Purify with no enemy nearby.** The list of actions allowed with no enemy within 40 yalms named the wrong actions; PvP Sprint and Purify are now allowed.
- **White Mage and Bard Guard, Recuperate and Elixir on the GCD path.** The emergency GCD step ended in the wrong base step and skipped them.
- **Scholar Chain Stratagem condition** now checks the enemy the action will actually target.
- **Viper Rattling Coil** no longer waits on Snake Scales, which the rotation never uses.
- **Gunbreaker.** The Blasting Zone 50% check reads the chosen target; the Heart of Corundum self-save now targets the Gunbreaker.
- **Paladin Guardian** respects the Hallowed Ground HP threshold and the 10-yalm limit.
- **Warden's Paean on allies (Bard) and Aquaveil on allies (White Mage).** The optional cleanse of other players could never find a target because it only counted afflictions the caster had applied; it now counts any affliction. Both options remain off by default.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.1.3 (2026-10-04) [testing]
### Fixed
- **A rare game crash when the plugin updates while the game is running.** While PvP Solver hooks are active and the plugin is installed or updated again, it now disables the hooks only after any call still inside them has finished, so a call can no longer land on a hook that is already gone. PvP rotation behaviour is unchanged.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.1.2 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers the PvP Solver on/off state (on only in a PvP zone, off anywhere), "Turn on when a match starts", "Turn off when a match ends", "Use no actions while in Guard", "Allow Sprint with no target", the Guard HP threshold and the four Purify toggles to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v0.1.1.1 (2026-09-05)

- Fixed a log line that repeated forever instead of once. The startup line "MajorUpdater: first valid cycle" sat behind a check that only asked whether rotations had loaded yet, but the flag that answers that is only ever set while actually in a PvP-valid state - so out of PvP it never flipped and the line printed on every single update cycle, including at the title screen with no character logged in. It now sits inside the block that really does load the rotations, so it prints once each time rotations are loaded and describes the state it claims to describe.
- Impact: over one 5.5-hour sample this single line was 55,497 of the 106,116 log lines collected across every installed plugin - 52.3% of the whole log - and 29,152 of those were written at the title screen by a PvP plugin. The same window would now produce 91 of them, a 99.8% reduction, with no loss of information: the line still states what condition the plugin came up in.
- No gameplay change. Rotation loading, targeting and the PvP rotations themselves are untouched; the only edit is where the log statement sits.

### Notes
- The paired "triggering rotation load..." line already fired 91 times in that sample rather than once, because `MajorUpdater.IsValid` resets `_rotationsLoaded` on every zone transition, logout and `Player.Available == false`. That reload behaviour is unchanged here and is tracked separately; it is why the line is "once per rotation load" rather than literally once per session.
- Second log-spam fix in this plugin (v0.1.0.11 throttled the per-frame "no rotation found" warnings). Every other Information-level plugin log line was audited this release: remaining helper logs are gated behind the InDebug setting, and nothing else logs at Information on a per-cycle path.

## v0.1.1.0 (2026-09-05)

- Added the in-game "What's new" popup. After PvP Solver updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/pvpsolver changelog` (or `/pvs changelog`) any time to reopen it.
- No change to rotations or targeting: the sticky-target and team-pressure work from 0.1.0.13 is untouched, and the seen-version is stored in its own small file so upstream RotationSolverReborn syncs stay clean.

## [0.1.0.13] - 2026-08-25
### Added
- **`PvPHighestPressure` targeting mode** (issue #4). Selects the hostile the most members of the player's own side are currently targeting, breaking ties toward the lowest HP percentage. Party and alliance lists are both counted, so it behaves in Frontline (where the rest of the player's side arrives as alliance members) as well as in Crystalline Conflict. The player's current target is excluded from the count, otherwise the mode would reinforce whatever was already targeted. Appended to the end of `TargetingType`, so existing saved `TargetingTypes` indices are unaffected, and it is **not** added to the default list - it is opt-in from Target settings.

### Fixed
- **Auto-target abandoned an enemy mid damage-window** (issue #4). `FindHostileRaw` re-ranked every hostile from scratch on each action and returned the top of the list, with no reference to the target already selected; `RSCommands.DoAction` then hard-assigned `Svc.Targets.Target` to it. Under an HP-based targeting mode this flips target the instant another enemy's HP crosses the player's, so a just-applied Wildfire is left on a target no longer being hit. `FindHostileRaw` now consults `FindStickyTarget` before returning: if the current target is still a legal candidate **for the action being used** and carries at least one status sourced from the player with `0 < RemainingTime <= StickyTargetMaxRemaining`, it is kept.

### Changed
- Two new options under Target settings: **`StickyTarget`** (default **on**) and **`StickyTargetMaxRemaining`** (default 30s). The second exists so an effectively permanent aura cannot pin the target forever - `RemainingTime == 0` is this codebase's convention for a permanent status and is already excluded, and the cap covers long ones.

### Notes
- **Trade-off, by design:** while the debuff is ticking the rotation will stay on that target even if another enemy drops lower. That is the requested behavior, and turning `StickyTarget` off restores the previous snap-to-lowest-HP handling exactly.
- **Scoped to PvP.** `FindStickyTarget` returns early unless `DataCenter.IsPvP`, so PvE target selection is byte-for-byte unchanged.
- Sticky selection reads `Svc.Targets.Target`, i.e. the target the previous action settled on, which is what makes this hysteresis rather than a second independent pick.
- `CountTeamPressure` scores every candidate once before sorting rather than inside the comparator; a comparator that re-reads live target data can throw `IComparer.Compare returns inconsistent results` if a teammate switches target mid-sort.
- **Not yet verified in-game** - shipped to the testing channel only.

## [0.1.0.12] - 2026-08-06
### Changed
- Synced upstream RotationSolverReborn `35ceb6e82` ("Guard status check for Doom to ensure valid object"): the PvP-rotation portion of that commit is a `GameVersion` attribute bump from `7.5` to `7.55` across all 21 PvP rotations.

### Notes
- **No gameplay change.** The Doom guard itself lives in and upstream's PvE rotations - base classes this fork intentionally does not track (RUNBOOK 3.2). The fork's PvP rotations contain no Doom status checks, so there is nothing to port.
- Synced from upstream; all 21 PvP rotation files verified byte-identical to the upstream tip (CR-normalised).
- **Promoted to production.** `AssemblyVersion` 0.1.0.9 -> 0.1.0.12; both channels now ship this build. In-game gate waived: the 0.1.0.11 fix executes only outside PvP (guard on `DataCenter.IsPvP`) and this sync is attribute-only, so PvP play cannot exercise what changed; the user-facing check is the absence of the PvE `no rotation found` warning in `dalamud.log` (issue #3).

## [0.1.0.11] - 2026-08-03
### Fixed
- **Per-frame log spam in all PvE content** (issue #3). `UpdateCustomRotation` is driven from the framework tick via `MajorUpdater.RSRRotationAndStateUpdate`, and emitted `WRN... no rotation found for job=<job> combatType=PvE` roughly 50 times per second for as long as the player was outside PvP. PvPSolver is PvP-only and ships no PvE rotations, so the lookup could never succeed; worse, the miss path set `DataCenter.CurrentRotation = null` immediately before logging, which permanently defeated the unchanged-state guard at the top of the method and forced a full re-resolve every tick.

### Changed
- `UpdateCustomRotation` now returns early when `DataCenter.IsPvP` is false, clearing `CurrentRotation`/`CurrentRotationActions` once on the PvP-to-PvE transition instead of every frame. `curCombatType` is consequently always `CombatType.PvP` below that guard.
- Removed the ungated `PluginLog.Information($"UpdateCustomRotation: job=..., IsPvP=..., groups=...")` call. It ran unconditionally on every invocation, ahead of every early return - invisible to users filtering at Warning+, but hammering `dalamud.log` in PvP and PvE alike.
- Both remaining "no rotation" warnings (`no rotation found for job=...` and `No valid rotations found for...`) are now throttled by a `_lastNoRotationLogged` (job, combat type) tuple, so a persistent miss reports once rather than once per frame. Reset in `ChangeRotation` so a later successful load re-arms the warning.

### Notes
- **Shipped to production with 0.1.0.12.** In-game gate waived: every changed statement executes only when `DataCenter.IsPvP` is false, so in-PvP behavior is provably unchanged; the empirical check is that the PvE `no rotation found` warning stops firing (issue #3).
- Both log lines date to the initial vendored commit `9b3652dae`, so this affected every release to date.
- No rotation, targeting or combat behavior changes - the end state in PvE is identical (`CurrentRotation == null`), it is just no longer recomputed and re-logged every tick.

## [0.1.0.10] - 2026-07-31
### Changed
- Synced upstream RotationSolverReborn commit `2ac940563`: the `EmergencyGCD` hook now takes the queued next GCD as a parameter, bringing it in line with `EmergencyAbility`, interrupts, dispels and the heal hooks, which have all taken it for some time. Applied to the base engine and to the two rotations that override it.

### Notes
- **No gameplay change.** The new `nextGCD` parameter is not read by any implementation of the hook â€” not in this fork, and not anywhere in upstream `641bd792e`. It is plumbing for future upstream use. Purify (stun / heavy / bind / silence / deep freeze / Miracle of Nature), Guard, Recuperate and Standard-issue Elixir priority are unchanged, as are the WHM Aquaveil and BRD The Warden's Paean cleanse paths.
- The WHM and BRD PvP rotations are now byte-identical to upstream (verified by diff, CR-normalised).
- First release on the **reinstated testing channel**. The channel was removed; its previous entry never worked because `TestingDalamudApiLevel` was unset, so Dalamud hid the build. Now set to 15 alongside `TestingAssemblyVersion`. Production remains 0.1.0.9 until this build is verified in-game.
- Escalated by the nightly upstream-merge on under RUNBOOK 3.2 ("never pull base classes"). RUNBOOK amended the same day with a signature-only carve-out so equivalent changes sync unattended.

## [0.1.0.9] - 2026-07-02
### Changed
- Hidden the three PvE-only config tabs inherited from upstream: Duty Rotation, Duty, and AutoDuty (incl. the AutoDuty helper-plugin installer list). Tabs are `[TabSkip]`-hidden, not deleted, so nightly subtree merges stay clean.
- Removed upstream Combat Reborn donation/community links: Ko-fi title-bar button, About-tab Ko-fi button and Discord banner, and the "Thanks to Supporters" About section with its random supporter shout-out hints.
- Easter egg window retitled "PvP Solver Lab" (was "RSR Lab").

### Notes
- Part of a fork-branding cleanup pass across lalalazy forks. No rotation/behavior changes.
- The Actions-tab "Intercepted" option is PvE-only by nature and remains visible; candidate for a later pass.

## [0.1.0.8] - 2026-06-18
### Changed
- Skip loading PvE duty rotations (Bozja, Emanation, MonsterHunter, Orbonne, Phantom, Variant) entirely. PvPSolver is PvP-only, so the duty rotation set had zero relevance; not loading it reduces memory use and startup time.

## [0.1.0.7] - 2026-06-07
### Added
- Auto-targeting for Forlorn / Maiden targets: checks the global `Svc.Objects` list to target any targetable, alive Forlorn/Maiden immediately upon rendering, regardless of whether they are in the hostile list.
- Increased `sipRange` in to 48f (from 25f) to ensure they are targeted immediately upon entering the player's hostile radar range.

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
