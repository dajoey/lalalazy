# Changelog

## v0.1.0.1 (2026-09-05)

### Fixed
- Plugin failed to load ("A window with this name/ID already exists"): the config window and the retainer-list button overlay were both registered as "Lazy Market Companion" in one WindowSystem; the overlay is now "Lazy Market Companion##overlay" (file: `MarketAutomation.cs`, ctor). Found in omasky dalamud.log at 12:14:27 on the first install attempt.
- `/pricematch` alias is only registered when no other plugin (Dagobert) already owns it, and only removed on unload if we registered it (file: `Plugin.cs`, `_ownsLegacyCommand`).

### Notes
- The Dagobert settings import DID run on that first attempt (4 retainers, 0 price limits) and is idempotent, so a reinstall picks up the imported config.

## v0.1.0.0 (2026-09-05)

### Added
- New plugin. Successor to Dagobert Price Matcher (fork of SHOEGAZEssb/Dagobert, AGPL-3.0; the price-matching engine, Universalis lookup, per-item min/max limits, retainer selection and hotkeys are carried over unchanged in behaviour). Files: `MarketAutomation.cs`, `MarketBoardHandler.cs`, `UniversalisPriceProvider.cs`, `Configuration.cs`.
- Auto-Market list: items you always sell, each with stack size per listing, keep-in-bags, keep-in-retainer, max listings per retainer, stock-source override and optional fixed price. Add from the config window search or the inventory right-click menu ("Add to Auto-Market"). HQ and NQ are separate entries. Files: `Configuration.cs` (`AutoMarketItem`), `Windows/ConfigWindow.cs`.
- Auto-Market engine: while a retainer's sell list is open, snapshots bags + the retainer's inventory + the 20 market slots, plans listings (Dalamud-free `AutoMarket/AutoMarketPlanner.cs`, unit-tested in `tests/LazyMarketCompanion.Harness`), lists each via `InventoryManager.MoveToRetainerMarket` at a placeholder price, waits for the server to confirm the slot, then runs the normal price match on the new listings (or on everything, toggle). File: `AutoMarket/AutoMarketService.cs`, `MarketAutomation.cs` (`InsertAutoMarketThenPinch`).
- "Auto Market" button next to "Auto Pinch" on both the retainer list (all enabled retainers) and the sell list (this retainer). `/lmc market`, `/lmc pinch`, `/lmc sweep`, `/lmc cancel`, `/lmc debug` subcommands.
- AutoRetainer integration: with "Run during AutoRetainer ventures" on, the plugin claims each enabled retainer through AutoRetainer's postprocess IPC (`OnRetainerAdditionalTask` -> `RequestPostprocess` -> `OnRetainerReadyForPostprocess` -> `FinishPostprocessRequest`), opens Sell Items, auto-markets, pinches, closes, and hands the retainer back. Watchdog releases AutoRetainer on abort/timeout and after a 5-minute cap. File: `AutoRetainerIPC.cs`, `MarketAutomation.cs` (`OnArReadyToPostprocess`).
- New-listing price mode: placeholder-then-match (default) or Universalis-first. Global toggles: stock source, retainer-stock-first, list partial stacks, reserve N market slots, pinch-all-after, chat messages.
- One-time settings import from `pluginConfigs/DagobertPriceMatcher.json` on first load (price limits, retainer selection, seen retainers, all matching options). File: `Plugin.cs` (`LoadOrImportConfiguration`).

### Changed
- Command is `/lmc`; `/pricematch` kept as a hidden alias.
- Chat prefix `[LMC]`. Windows System.Speech TTS removed (Dalamud chat notifications only); the plugin no longer ships `System.Speech.dll`.
- "Sell items" menu entry is matched by Addon-sheet text (row 2380) with the old index-2 fallback.
- The max-price-cut guard is bypassed for listings still at the Auto-Market placeholder price, so a fresh listing always drops to the matched price.

### Notes
- Ships on the TESTING channel first. Dagobert Price Matcher stays published until this is verified in-game, then gets retired (P3).
- `MoveToRetainerMarket` / `SetRetainerMarketPrice` / `RetainerMarket` container facts verified against FFXIVClientStructs and DailyRoutines' AutoRetainerWork (2026-09-05).
