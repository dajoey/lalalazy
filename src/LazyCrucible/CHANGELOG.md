# LazyCrucible — Changelog

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
