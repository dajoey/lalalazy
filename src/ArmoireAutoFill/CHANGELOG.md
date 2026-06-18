# Changelog - ArmoireAutoFill

## v0.4.0.0 (2026-06-18)

### Added
- Auto-store to armoire. New Logic/ArmoireAutoStore.cs stores eligible items into the armoire via the native Cabinet.StoreCabinetItem API. Adds an optional "Auto-store when armoire opens" toggle (off by default) that fires on the Cabinet addon PostSetup, plus a manual "Store all to armoire" button in the main window with a live result message. Deduplicates by item ID and skips items already in the armoire. Files: Logic/ArmoireAutoStore.cs, Configuration.cs (AutoStoreOnOpen), Plugin.cs, Windows/MainWindow.cs.
