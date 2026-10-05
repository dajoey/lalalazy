# Changelog - Lazy Currency Spender

## v1.2.7.3 (2026-10-05) [testing]
### Fixed
- Fixed the log being flooded with `Location not found!` errors while the spending window was open. Shops run by NPCs that have no entry in the plugin's map-location table (for example Eirene's Rain Exchange and Ermina) logged one error per row on every frame, 159,189 lines in a single session. Such a shop is now noted once at debug level, and its Flag and TP buttons are simply not drawn. Currency tracking, shop listings and the spending suggestions are unchanged.
### Notes
- Testing channel only; the production channel is unchanged.

## v1.2.7.2 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers the table switches (ventures, collectables, missing collectables, items of interest, items eligible for sale, hide empty currencies), opening automatically, the minimum sales for the sellable table and the thousands separator to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v1.2.7.1 (2026-09-22)

- Fixed an error the plugin logged on every game close (the type initializer for ECommons.Automation.Callback threw an exception). The bundled support library predated the current game data layout and failed to initialise during shutdown; it is now current with the rest of the plugin family. No change to currency tracking or the spending suggestions.

## v1.2.7.0 (2026-09-05)

- Added the in-game "What's new" popup. After Lazy Currency Spender updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/cur changelog` any time to reopen it.
- The existing Changelog tab in the settings window still works exactly as before; this is the pop-up on update, not a replacement for it.
- No change to currency tracking or the spending suggestions.

## [1.2.6.1] - 2026-05-25
### Added
- Added a new "Equipment and Gear Exchange" section to the UI to display untradable, non-collectable gear and weapons (like Bygone Brass equipment) purchased with endgame tomestones.

## [1.2.6] - 2026-05-25
### Added
- Enabled weekly capped Allagan Tomestones of Mnemonics currency by default.
- Added automatic SelectedCurrencies migration logic to auto-enable weekly capped tomestones upon updating.

## [1.2.5.1] - 2026-05-25
### Added
- Forked from original CurrencySpender by Blackcatz1911.
- Updated for Dalamud API Level 15 / .NET 10 (FFXIV Patch 7.50 compatibility).
- Registered new command shortcuts: `/lazycur` and `/lazycurrencyspender`.
- Custom premium coin-bag icon added.
- Display clear credits to the original developer inside the settings tab.
