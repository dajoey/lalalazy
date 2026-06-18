# Changelog - ArmoireAutoFill

## v0.4.1.0 (2026-06-18)

### Added
- **"Skip gear that is in a gearset" option** (on by default). Auto-store now excludes any item that belongs to one of your saved gearsets, so it will not deposit gear you actively use. Built from RaptureGearsetModule (same source as the in-game gearset UI), HQ flag stripped for matching. New `SkipGearsetItems` config + checkbox in the main window; result message reports how many items were kept. Files: `Logic/ArmoireAutoStore.cs`, `Configuration.cs`, `Windows/MainWindow.cs`.

## v0.4.0.0 (2026-06-18)

### Added
- Auto-store to armoire. New Logic/ArmoireAutoStore.cs stores eligible items into the armoire via the native Cabinet.StoreCabinetItem API. Adds an optional "Auto-store when armoire opens" toggle (off by default) that fires on the Cabinet addon PostSetup, plus a manual "Store all to armoire" button in the main window with a live result message. Deduplicates by item ID and skips items already in the armoire. Files: Logic/ArmoireAutoStore.cs, Configuration.cs (AutoStoreOnOpen), Plugin.cs, Windows/MainWindow.cs.
