# Changelog - ArmoireAutoFill

## v0.4.4.0 (2026-09-05)

- Added the in-game "What's new" popup. After Armoire Auto-Fill updates, its changelog now opens once inside the game so you can see what changed without going to GitHub. It waits until you are logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/armoire changelog` any time to reopen it.
- No change to armoire behaviour: the dungeon checklist, the auto-store on opening the armoire, and the gearset/armoury options are all unchanged.

## v0.4.3.0 (2026-07-02)

### Fixed
- **Auto-store on armoire open actually fires now.** v0.4.2.0 ran `StoreAll` directly from the Cabinet addon's PostSetup event, but cabinet contents load from the server asynchronously *after* the addon opens, so `UIState.Cabinet.IsCabinetLoaded()` was still false and the store bailed silently (the manual button worked because the data had loaded by then). PostSetup now just arms a pending flag; a Framework.Update poll fires `StoreAll` once the cabinet data is loaded (10s timeout, disarmed on PreFinalize if the UI closes first). File: `Logic/ArmoireAutoStore.cs`.

## v0.4.2.0 (2026-07-02)

### Changed
- **Auto-store is now ON by default** - the plugin finally lives up to its name. Opening the armoire UI at an inn automatically stores eligible gear from your bags. Config migration (v2 -> v3) flips `AutoStoreOnOpen` on for existing installs; it can still be turned off via the checkbox in the main window. Files: `Configuration.cs`, `Plugin.cs`.
- **Auto-store scope narrowed to the regular inventory (bags) by default.** The armoury chest is no longer scanned unless the new "Also store from armoury chest" option (`AutoStoreIncludeArmory`, off by default) is enabled. Gearset protection (`SkipGearsetItems`) remains on by default. Files: `Logic/ArmoireAutoStore.cs`, `Windows/MainWindow.cs`.

### Fixed
- **Eligibility check order in `StoreAll`.** Items with no Cabinet sheet entry were previously tested via `IsItemInCabinet(GetValueOrDefault(itemId, 0))`, probing cabinet row 0 before the real lookup ran. The `TryGetValue` lookup now runs first and `IsItemInCabinet` only ever sees a real cabinet row. File: `Logic/ArmoireAutoStore.cs`.

## v0.4.1.0 (2026-06-18)

### Added
- **"Skip gear that is in a gearset" option** (on by default). Auto-store now excludes any item that belongs to one of your saved gearsets, so it will not deposit gear you actively use. Built from RaptureGearsetModule (same source as the in-game gearset UI), HQ flag stripped for matching. New `SkipGearsetItems` config + checkbox in the main window; result message reports how many items were kept. Files: `Logic/ArmoireAutoStore.cs`, `Configuration.cs`, `Windows/MainWindow.cs`.

## v0.4.0.0 (2026-06-18)

### Added
- Auto-store to armoire. New Logic/ArmoireAutoStore.cs stores eligible items into the armoire via the native Cabinet.StoreCabinetItem API. Adds an optional "Auto-store when armoire opens" toggle (off by default) that fires on the Cabinet addon PostSetup, plus a manual "Store all to armoire" button in the main window with a live result message. Deduplicates by item ID and skips items already in the armoire. Files: Logic/ArmoireAutoStore.cs, Configuration.cs (AutoStoreOnOpen), Plugin.cs, Windows/MainWindow.cs.
