# LazyCrucible — Changelog


## v0.1.9.10 (2026-10-05) [testing]
### Fixed
- **Run-roster need coverage no longer fires while AutoDuty is still building its team.** The roster correction used the battlehorn's three-pick threshold, so any selection of four or more familiars counted as a finished team and the correction could fire in a quarter-second pause mid-rebuild — AutoDuty's continuing build then removed the added rows (five runs on 2026-10-05: every roster correction was torn down and no answerer reached the run). The roster now waits for its own capacity (10/12/14/12/15 per board) to be selected, or for the long quiet, before it corrects. A roster AutoDuty has wiped empty is also left alone: an empty selection is the start of its build, not the end of one.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.9.9 (2026-10-03) [testing]
### Fixed
- **A rare game crash when the plugin updates or reloads while the game is running.** The screen-recorder and agent-probe hooks could still be mid-call on the game's thread at the moment the plugin was unloaded, and the game then closed on an unhandled error. Every hook now counts the calls inside it, the unload waits (up to one second) for them to finish before the hooks are released, and a call that arrives after release returns harmlessly instead of failing.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.9.8 (2026-10-03) [testing]
### Fixed
- **Need coverage on top of AutoDuty waits for AutoDuty to finish building its selection.** The previous version wrote the correction after AutoDuty's first toggle, when only one familiar was picked; the selection reverted within a fraction of a second and all four allowed corrections were spent in 1.5 seconds, before AutoDuty had even built its team. A fight that needs a dispel (the Strix Piece) then ran with no dispeller on the horn, five fights in a row. The correction now waits until AutoDuty's selection holds three picks (or has been quiet for 1.5 seconds if it stops short) before it is made. The same applies to the run roster, where AutoDuty clears the selection one pick at a time.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.9.7 (2026-10-03) [testing]
### Added
- **One line per fight names what the horn can answer.** When a formation pass settles for an identified fight, a new `HC|` line lists the rows actually standing on the horn and grades the fight's needs against them (`I/D/C:R/U:row` per need, or `miss` when no row answers), including picks an external driver made. The grade is re-stated whenever the settled horn changes while the screen is open, so answer coverage can be read from one line.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.9.6 (2026-10-03) [testing]
### Fixed
- **Need coverage on top of AutoDuty now reaches the fight's horn slots.** AutoDuty sets its Battlehorn picks one toggle at a time and confirms shortly after the last one. The correction was written once, after the first toggle, and every later toggle overwrote it, so the dispel or interrupt answer a fight calls for never stayed on the horn. The correction is now made again after each AutoDuty toggle (at most four per screen) and the last one lands after AutoDuty's last toggle and before its confirm. The run log's `adn=` field counts the corrections made on a screen.
### Notes
- Testing channel only; the production channel is unchanged.

## v0.1.9.5 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers the selection switches (filling the familiar roster, setting the Battlehorns, picking who rests) and the display switches (opening the guide automatically, announcing picks in chat, recording screens to the log) to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
- The switches that buy, feed, choose treasure or take loot are not offered.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v0.1.9.4 (2026-10-03)

- **Need coverage is corrected on top of AutoDuty's picks.** AutoDuty runs whole boards with its own leveling familiars, and its battlehorn picks and roster rebuilds are blind to what the fights call for: in a full automated evening, every enemy buff that ran its whole duration was a fight where no dispeller was on the horns even though one sat in the roster, and the healer fight often had no interrupter in the roster at all. The plugin no longer stands down entirely while AutoDuty drives: once its selection on a screen has settled (its writes come one toggle at a time), a minimal correction swaps an answerer in for one pick that answers nothing — its leveling picks are otherwise kept, and a fight whose needs are already covered is never touched. The roster gets the same treatment at the board entry: when none of the board's fights' needs has a healthy answerer in the built roster, one is appended (room permitting) or swapped in. The log names every correction (`autoduty-needfix`, with the exact swap) and every need that could not be covered and why (`miss=`). One correction per screen; a later AutoDuty write still wins. Screens played by hand are unchanged.

## v0.1.9.3 (2026-10-02)

- **Fight guide checked against recorded runs.** Entries the runs disprove are corrected: Sand Tempest's blind (Lakhamu + Golem), Borgny's Toxin and petrification (Catoblepas) are never cleansable, so they are no longer cleanse counters and Scouring Ash is no longer listed for petrify; resist items are the counter. Black Eruption is the Bishop's cast. Fanaticism's cast is 6 s and Sweet Steel's 4 s (the game panel's values), Blood Sword is not interruptible, and seven cast times that the recorded castbars contradict by a second or more follow the castbars. Where a run could not settle a claim (the Golem's Might dispel), the guide says no run has shown it.
## v0.1.9.2 (2026-10-02)

- **Fight guide (First Master's Board, Strix Piece)**: the guide no longer lists Soul Crush as a counter for the Aero III knockback. The game never flags either Aero III cast as interruptible (none of the five recorded casts was), so no interrupt can stop it. The line now says the only counter is killing the Plume add before its 12-second cast ends, and the fight's kill order names the Plume.
- **Battlehorns**: with that need gone, Strix Piece picks a dispeller for Ultimate Focus and spends no horn on a Soulkin for the knockback.

## v0.1.9.1 (2026-10-02)

- **Run roster**: the team filled at the entry menu is chosen by the abilities the board's fights need. Each familiar answering an interrupt, dispel or cleanse that a fight of the board requires is placed first (Required before Useful, per fight), and only the remaining places go to the point score. Previously the whole team was ranked by the point score alone, so a board's only Soulkin or Wavekin could be left off the team behind elemental-weakness hitters, and the fight guide's own counters were never considered.
- **Advisor window**: the "Roster" line (familiars in the most battles' picks) is built from the same need-first picks as the Battlehorns, so it matches what each fight actually selects.

## v0.1.9.0 (2026-10-02)

- **Battlehorns**: the three familiars are chosen by the abilities the fight needs. Interrupt (Soul Crush), dispel (Quelling Wave) and cleanse (Scouring Ash) needs are covered first, Required before Useful; the elemental weakness, crowd control and stats only decide between familiars that cover the same needs. Previously a single point score let an elemental-weakness match outvote an interrupt or dispel the fight called for, and the guide could report a need the picker never considered.
- **Fight guide**: the picks and the guide read one need list per fight. A need is Required when the game's enemy panel calls for it or sources agree, and Useful when it is single-sourced or disputed. Each need shows the familiar that covers it. The warning mark appears only for a Required need nothing can cover and gives the reason (not captured, knocked out, or the horn slots went to other needs); an uncovered Useful need is shown without a warning, with the same reason.
- **Fight guide**: counters that a source or the game panel contradicts are marked disputed instead of required.
- **Telemetry**: the horn selection line records which familiar covers each need, or why none does.

## v0.1.8.0 (2026-09-30)

- **Fight guide: incoming-cast warning.** While an enemy casts Atomic Ray (Second Master's Board, Durga Piece: 12.7 s castbar, 3,998 and 4,782 damage to a character with about 4,060 HP, even with the familiar holding enmity), the fight guide window shows the cast name, the seconds left and the damage, and opens on its own when the cast starts (when "Open the fight guide on the upcoming fight" is on). Read-only: nothing is pressed.
- **Fight guide: low-HP entry advice.** When the upcoming fight is First Master's Board battle 3 (Corpse Flower + Queen Hawk) or Second Master's Board battle 6 (Durga) and the character is under 70% HP, the guide says so. Battle 3 is a random-space outcome with no campsite before it; every recorded visit entered at 1-42% HP and ended in knock-outs.
- **Fight guide: text corrected from recorded fights.** On the Properties of Darkness (Strix Piece) is a single hit on whoever holds enmity, not a room-wide hit. Grim Fate is a five-hit cast of 645-1,030 per cast. Sweeping Evisceration, Obliterate and Salivous Snap are listed as hits that follow enmity. Rotten Stench hits the character and the familiar together. Atomic Ray's castbar and damage are the measured ones. Lines taken from recorded fights carry the new `LOG` source tag.
- Only First Master's Board battles 0, 1, 3, 5, 7 and 8 and Second Master's Board battles 1, 3, 5 and 6 have measured data; every other fight in the guide stays guide-derived and keeps its guide source tags.

## v0.1.7.0 (2026-09-30)

- **AutoDuty stand-down**: the plugin now leaves the Crucible screens alone for AutoDuty only while AutoDuty is actually stepping. AutoDuty can keep reporting "running" after its loop has stopped (seen after a run ends), and the plugin used to keep standing down for as long as that lasted, including on boards played by hand. It now also reads AutoDuty's looping and navigating state and where the character is: a loop that is not navigating the current board, is navigating a different board, or has made no progress in the lobby is treated as stalled, and the plugin works normally until AutoDuty starts a new run. The stand-down note in the log and the main window say which case was seen.
- **AutoDuty preset in BossMod Reborn**: while AutoDuty is judged stalled inside a board, the BossMod Reborn preset named "AutoDuty" that the stalled loop left active is cleared. That preset retargets to enemies of its own choosing and walks the character to them, and only AutoDuty stopping clears it. A preset with any other name is never touched.

## v0.1.6.0 (2026-09-29)

- **Run roster**: the entry-menu team fill now waits for a re-opened menu to settle before writing. A re-opened board menu briefly shows the previous session's team in one list while the roster read is still empty, and a fill planned from that mismatch toggled the live team off and aborted on the read-back, leaving the team empty. The fill now waits until the two lists agree, then writes normally.

## v0.1.5.0 (2026-09-26)

- **Battlehorns**: familiar read-back now verifies team membership rather than strict positional order against advisor priority rankings. Previously, when the active party slots held the chosen familiars in an order different from the advisor's score ranking, the read-back treated the permutation as a mismatch and aborted with an erroneous disarm.
- **Battlehorns**: selection restore after an abort now checks membership equality across active horn slots so restored selections leave clean state rather than disarming the screen.

## v0.1.4.0 (2026-09-26)

- **Run roster**: the Bentbranch Meadows entry menu now fills a team even on a board's first entry, when no team is saved yet. Previously the menu was skipped exactly then, which left the First Master's Board without familiars.
- **Run roster**: team size now follows the board (twelve familiars on the First Master's Board) instead of a fixed ten, which had been under-filling every board after the first.

## v0.1.3.0 (2026-09-26)

- **Beast Feed**: feeding no longer stalls while a knocked-out familiar is on the picker. The game marks a knocked-out familiar "cannot eat" whatever is offered, and the kin cross-check read that as the feed not fitting, so every feed was left for the player until the familiar was revived. Knocked-out familiars no longer take part in that check.

## v0.1.2.0 (2026-09-22)

- **Treasure**: takes the best choice instead of only suggesting it. Live button clicks confirmed choice *k* is event param `2+k`, so the pick path that was already built can fire.

## v0.1.1.0 (2026-09-21)

- **Beast Feed**: picks the familiar each feed helps most in the fights ahead and confirms it (kin, satiety, no repeats, knocked-out familiars honoured).
- **Shop**: buys the heals, resistances, gear and feed the upcoming fights call for; never sells and never leaves the shop.
- **Spoils**: takes everything when it all fits; otherwise lists the best items first.
- **Campsite**: selects which familiars rest; Rest stays with the player.
- **Treasure**: marks the best choice and explains why.
- **Fight guide** (`/lazycrucible guide`): all five boards, with horn picks and their reasons, dangerous hits, mechanics and what to bring; opens on the next fight.
- **Hands back**: any input made by hand on one of these screens, or AutoDuty running, hands that screen back to the player. Each screen has its own switch, on by default.
- **Error reporting** covers the new screens: a screen automation that keeps failing drops the input in flight, pauses with one chat notice and retries on its own.

## v0.1.0.0 (2026-09-21)

- **First release**, split out of GluttonyCombo: the Beastmaster familiar selection for Crucible of the Unbroken now lives here, and GluttonyCombo keeps the Beastmaster rotation. Both automations are on by default and each has its own switch.
- **Run roster**: on the Bentbranch Meadows entry menu, sets the ten familiars that cover the board's battles best, once per visit.
- **Battlehorns**: on the formation screen before every fight, puts the three familiars that answer that fight's mechanics on the horns (elemental weakness, interrupts, dispels, cleanses, crowd control the enemies are vulnerable to). Knocked-out familiars are never picked; badly hurt ones lose to a healthy familiar that fits nearly as well. The fight itself is never started.
- **Picks in chat**: one line per fight naming each familiar and why it was chosen (`"Announce picks in chat"`). The window shows the latest selection.
- **Manual edits win**: a roster or horn change made by hand (or by another tool) stops the automation for that screen, and a roster edited that way is kept for the rest of that visit.
- **Stands down for AutoDuty**: while AutoDuty is running a board (it picks its own familiar team), the familiar screens are left alone (`"Stand down while AutoDuty is running"`, on by default).
- **Fixed** (carried over from GluttonyCombo testing builds): the item shop's Beast Feed screen was treated as the Battlehorn screen, so feeding a familiar after the elite fight could fail or move the feed target. The feeding and campsite screens are now never touched.
- **Fixed** (carried over): a run roster cleared and rebuilt on the entry menu (by hand or by AutoDuty) is no longer overwritten mid-edit.
- **Beast-pick advisor**: picks for every battle on every board, the board roster and familiars worth capturing, under "Beast picks by battle".
- **Screen recorder** (`"Record Crucible screens to the log"`, read-only): writes each Crucible screen and every button pressed on it to the Dalamud log, groundwork for automating the path, spoils, treasure, shop, feeding, campsite and beast gear screens.
- Stands down with a chat notice while a GluttonyCombo version that still fills familiars itself is loaded.
- **Problem reports**: `/lazycrucible report <what happened>` or the "Report a problem" button writes a report to the plugin log with the zone, job, recent familiar-selection and screen lines, and the full contents of the Crucible screens that are open.
- **Error reporting**: an error is written as one line carrying the plugin version, build, zone, job and the full stack with file and line numbers. A part that keeps failing (familiar selection, the screen recorder, or its event and click logs) pauses with one chat notice and retries on its own; manual edits are still detected while the event log is paused.
- `/lazycrucible` opens the window; `/lazycrucible changelog` shows this list.
