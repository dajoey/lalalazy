# LazyCrucible — Changelog

## v0.1.0.0 (2026-09-21)

- **First release**, split out of GluttonyCombo: the Beastmaster familiar selection for Crucible of the Unbroken now lives here, and GluttonyCombo keeps the Beastmaster rotation. Both automations are on by default and each has its own switch.
- **Run roster**: on the Bentbranch Meadows entry menu, sets the ten familiars that cover the board's battles best, once per visit.
- **Battlehorns**: on the formation screen before every fight, puts the three familiars that answer that fight's mechanics on the horns (elemental weakness, interrupts, dispels, cleanses, crowd control the enemies are vulnerable to). Knocked-out familiars are never picked; badly hurt ones lose to a healthy familiar that fits nearly as well. The fight itself is never started.
- **Picks in chat**: one line per fight naming each familiar and why it was chosen (`"Announce picks in chat"`). The window shows the latest selection.
- **Manual edits win**: a roster or horn change made by hand stops the automation for that screen, and a roster edited by hand is kept for the rest of that visit.
- **Fixed** (carried over from GluttonyCombo testing builds): the item shop's Beast Feed screen was treated as the Battlehorn screen, so feeding a familiar after the elite fight could fail or move the feed target. That screen is now never touched.
- **Fixed** (carried over): clearing and rebuilding the run roster on the entry menu no longer gets overwritten mid-edit.
- **Beast-pick advisor**: picks for every battle on every board, the board roster and familiars worth capturing, under "Beast picks by battle".
- **Screen recorder** (`"Record Crucible screens to the log"`, read-only): writes each Crucible screen and every button pressed on it to the Dalamud log, groundwork for automating the path, spoils, treasure, shop, feeding, campsite and beast gear screens.
- Stands down with a chat notice while a GluttonyCombo version that still fills familiars itself is loaded.
- `/lazycrucible` opens the window; `/lazycrucible changelog` shows this list.
