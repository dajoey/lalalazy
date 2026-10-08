# Changelog - ArmoireAutoFill

## v0.5.1.0 (2026-10-08) [testing]
### Added
- **The shopping list now shows its pieces.** Every currency section in the Knightshopper shopping list can be expanded into a table of the actual pieces: item, price, vendor, one needed of each. Gil prices are the standard vendor price from the item sheet (some vendors charge less with reputation).
### Changed
- Empty states say so in words: nothing missing at all, or none of the missing pieces sold by a vendor Knightshopper can reach.
- The section reports how many additional armoire pieces sit only in shops the list cannot safely name (scripted vendors and gil-priced SpecialShop entries), so the partial total is visible.

## v0.5.0.0 (2026-10-07) [testing]
### Added
- **Knightshopper shopping list.** New collapsible section in the main window: every armoire-eligible item that is missing, that is sold by a vendor Knightshopper can reach, and that is not already in the inventory, grouped by currency (gil, hunt seals, PvP wolf marks). One click per currency copies a Knightshopper import code to the clipboard; pasting it into Knightshopper's matching currency tab (paste button) creates a new shopping list and leaves existing lists untouched. No purchase is ever started by this feature — the clipboard is the only thing it touches.
- Missing pieces that are already in the inventory or armoury chest are reported instead of listed for purchase (auto-store handles those), and pieces no reachable vendor sells are counted so the total adds up.
### Notes
- Testing channel only; the production channel is unchanged.
- Knightshopper itself does not need to be loaded to copy the codes; when it is not detected the window says so. Import-time validation still applies inside Knightshopper, so every generated entry uses only shops Knightshopper's own catalog can reach.
- Quest-locked shop entries are included and counted, since the vendor only offers them after the quest is done.

## v0.4.4.1 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers "Auto-store when the Armoire opens", scanning the inventory on login, showing owned items and hiding finished dungeons to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v0.4.4.0 (2026-09-05)

- Added the in-game "What's new" popup. After Armoire Auto-Fill updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/armoire changelog` any time to reopen it.
- No change to armoire behaviour: the dungeon checklist, the auto-store on opening the armoire, and the gearset/armoury options are all unchanged.

## v0.4.3.0 (2026-07-02)

### Fixed
- **Auto-store on armoire open actually fires now.** v0.4.2.0 ran `StoreAll` directly from the Cabinet addon's PostSetup event, but cabinet contents load from the server asynchronously *after* the addon opens, so `UIState.Cabinet.IsCabinetLoaded()` was still false and the store bailed silently (the manual button worked because the data had loaded by then). PostSetup now just arms a pending flag; a Framework.Update poll fires `StoreAll` once the cabinet data is loaded (10s timeout, disarmed on PreFinalize if the UI closes first). File: `Logic/ArmoireAutoStore.cs`.

## v0.4.2.0 (2026-07-02)

### Changed
- **Auto-store is now ON by default** - the plugin finally lives up to its name. Opening the armoire UI at an inn automatically stores eligible gear from inventory. Config migration (v2 -> v3) flips `AutoStoreOnOpen` on for existing installs; it can still be turned off via the checkbox in the main window. Files: `Configuration.cs`, `Plugin.cs`.
- **Auto-store scope narrowed to the regular inventory (bags) by default.** The armoury chest is no longer scanned unless the new "Also store from armoury chest" option (`AutoStoreIncludeArmory`, off by default) is enabled. Gearset protection (`SkipGearsetItems`) remains on by default. Files: `Logic/ArmoireAutoStore.cs`, `Windows/MainWindow.cs`.

### Fixed
- **Eligibility check order in `StoreAll`.** Items with no Cabinet sheet entry were previously tested via `IsItemInCabinet(GetValueOrDefault(itemId, 0))`, probing cabinet row 0 before the real lookup ran. The `TryGetValue` lookup now runs first and `IsItemInCabinet` only ever sees a real cabinet row. File: `Logic/ArmoireAutoStore.cs`.

## v0.4.1.0 (2026-06-18)

### Added
- **"Skip gear that is in a gearset" option** (on by default). Auto-store now excludes any item that belongs to a saved gearset, so gear in active use is not deposited. Built from RaptureGearsetModule (same source as the in-game gearset UI), HQ flag stripped for matching. New `SkipGearsetItems` config + checkbox in the main window; result message reports how many items were kept. Files: `Logic/ArmoireAutoStore.cs`, `Configuration.cs`, `Windows/MainWindow.cs`.

## v0.4.0.0 (2026-06-18)

### Added
- Auto-store to armoire. New Logic/ArmoireAutoStore.cs stores eligible items into the armoire via the native Cabinet.StoreCabinetItem API. Adds an optional "Auto-store when armoire opens" toggle (off by default) that fires on the Cabinet addon PostSetup, plus a manual "Store all to armoire" button in the main window with a live result message. Deduplicates by item ID and skips items already in the armoire. Files: Logic/ArmoireAutoStore.cs, Configuration.cs (AutoStoreOnOpen), Plugin.cs, Windows/MainWindow.cs.
