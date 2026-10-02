# LazyCrucible — Changelog

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
