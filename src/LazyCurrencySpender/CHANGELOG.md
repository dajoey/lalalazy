# Changelog - Lazy Currency Spender

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
