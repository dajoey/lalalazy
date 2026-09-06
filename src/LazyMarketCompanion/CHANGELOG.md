# Changelog

## v0.1.5.0 (2026-09-05)

### Fixed
- Auto Market was still re-pricing your entire retainer on every run. 0.1.3.0 was supposed to price only the listings it had just created, but it worked out which row of your sell list to click by ASSUMING the list is shown in the same order as the retainer's 20 market slots. On your client it is not, and it never was: on all four Auto-Market runs on 2026-09-05 the row it picked held something else entirely (an Ice Crystal, a Heavens' Eye Materia VII, a Zormor Stone Lantern, a Table Orchestrion, a Liquid Glass). The safety check caught it every time - which is why a listing of yours was never mis-priced - but the safety net was "re-price everything", so what you actually saw was the old behaviour with extra waiting (file: `AutoMarket/MarketRowMap.cs`; reported by Joey 2026-09-05).
- Auto Market now FINDS its new listings by reading your sell list instead of assuming its order, so it stops re-pricing your whole retainer. Each row of the open list is asked which market slot it is showing, and the listings created this run are matched to those rows exactly - so the order of your sell list, and whether you have it sorted at all, no longer matters (files: `SellListReader.cs` and `AutoMarket/SellListRows.cs`, new).
- If two of your listings are the same item, the new one is identified by its asking price: a listing still sitting at the 999,999,999 gil placeholder is by definition one this run just created (file: `AutoMarket/SellListRows.cs`, `MatchByName`).

### Added
- New setting, "If a new listing can't be found", under Auto Market (only shown when "Pinch everything after listing" is off). It decides what happens in the case that should now never arise. "Re-price every listing" is what the plugin has always done and stays the default, so this update changes nothing on its own; "Leave it at the placeholder and tell me" touches nothing and says so in chat, at the cost of a listing that will not sell until you price it; "Re-price only my Auto-Market items" re-prices just the listings whose item is on your Auto-Market list, so a listing you made by hand is never touched (files: `Configuration.cs`, `AutoMarketPinchFallback`; `Windows/ConfigWindow.cs`).

### Notes
- All three of 0.1.3.0's safety checks are unchanged and still run - they are the reason a listing of yours has never been re-priced by mistake. The only thing that changed is where the row comes from: read from the game, not worked out from the slot (file: `MarketAutomation.cs`, `InsertPinchForNewListings`).
- Your sell list only draws about half its rows at a time, so identifying rows by NAME alone cannot see the ones scrolled off screen. That is why the slot each row reports is the primary reading and the name is a cross-check; a row that reports a slot but names a different item than the game has in that slot causes the whole batch to be refused rather than half-applied (file: `AutoMarket/SellListRows.cs`, `MatchBySlot`).
- Harness now at 93 checks, including a replay of all five failed row identifications from 2026-09-05: each one asserts that the old order-based guess picks the wrong row AND that reading the rows picks the right one (file: `tests/LazyMarketCompanion.Harness/Program.cs`).
## v0.1.4.0 (2026-09-05)

### Added
- New off-by-default price-decision log: when enabled, one diagnostic line is written to the plugin log for every price decision the matcher makes - both the prices it sets and, just as importantly, the writes it REFUSES because they would undercut by more than your maximum, which previously left almost no trace. Each line carries the item, its quantity, the old price, the new price before and after your per-item min/max limits, where the price came from (market board, Universalis, or the default-amount fallback), and the percentage change (file: `MarketTelemetryFormat.cs` + `MarketTelemetry.cs`, new).
- Turn it on with `/lmc telemetry on` or the "Log price decisions" checkbox under Advanced / Diagnostics in the Price Matching tab. It is off by default, changes no pricing behaviour, and sends nothing anywhere - the lines only go to your own local plugin log (file: `Plugin.cs`, `HandleTelemetryCommand`; `Windows/ConfigWindow.cs`).
- The line format is designed to be joined against your retainer sale messages ("...have sold for X gil") so you can finally answer, with data, whether matched prices actually earn more than fallback prices and how long listings take to sell (file: `MarketTelemetryFormat.cs`).

### Notes
- Quantity on a line is 0 (unknown) when the retainer holds two listings of the same item and quality, because the open price dialog does not say which row it is; otherwise it is the listing's stack size (file: `MarketTelemetryFormat.cs`, `ResolveQuantity`).
- Fixed-price Auto-Market listings do not appear in the log: they are priced when listed and never pass through the matcher (file: `MarketAutomation.cs`).
## v0.1.3.0 (2026-09-05)

### Changed
- Auto Market now re-prices ONLY the listings it just created, instead of walking the retainer's entire sell inventory. "Pinch everything after listing" is now OFF by default and is the opt-in; re-tick it in `/lmc` settings to get the old behaviour back. On a retainer holding 20 listings where 7 are new, that is roughly 22-45s of price matching instead of 60-125s, because each item priced costs a context menu, a price dialog and the market-board open/price delays (file: `Configuration.cs`, `AutoMarketPinchAllAfter`; requested by Joey 2026-09-05).
- Existing installs are moved to the new behaviour by a config migration, not just the changed default: a C# field initializer only ever reaches a fresh config, because Newtonsoft deserializes the saved value straight over it. Config schema is now v2 and `Plugin.MigrateIfNeeded` turns the setting off once, logs one line saying so, and saves (file: `Plugin.cs`, `MigrateIfNeeded`; `Configuration.cs`, `CurrentVersion`).

### Fixed
- "Pinch only the new listings" could leave a brand-new listing stranded at its 999,999,999 gil placeholder price while re-pricing an unrelated listing instead. The path was shipped in 0.1.0.0 but never ran in production (the setting had never been off), and it addressed listings by inferring a sell-list ROW from the market container SLOT, on an assumption the game does not guarantee: that the list shows occupied slots in container order. In placeholder-then-match mode a wrong row is silent - no error, the new listing simply never sells (file: `MarketAutomation.cs`, `InsertAutoMarketThenPinch`; `AutoMarket/AutoMarketService.cs`, `ListIndexOfSlot`).
- That mapping is now checked three times, and every failure falls back to re-pricing every row rather than pricing the wrong one. Before clicking: the sell list must show exactly one row per occupied market slot, and every slot just listed must map onto a row that holds the item that was listed there. Per row, once the game has the listing open: the item in the price dialog must be the item the mapping promised, or that row is cancelled out of, unpriced. At the end: any new listing that was never reached triggers a full re-price of the retainer, so nothing is left at the placeholder (file: `MarketAutomation.cs`, `InsertPinchForNewListings` / `VerifyPinchRow` / `VerifyNewListingsPriced`).
- Fixed-price listings are skipped by the new pass, as they were before - they are already at their final price and never need matching.

### Added
- New `MarketRowMap` (file: `AutoMarket/AutoMarketPlanner.cs` sibling `AutoMarket/MarketRowMap.cs`) holds the row/slot mapping and its guards as Dalamud-free code, so it is covered by the offline harness. Harness cases 18-20 cover the slot-ordered layout including Joey's real 2026-09-05 run shape (7 new listings into slots 3,7,9,10,11,12,15 of a 20/20 retainer), lists with gaps, a list that is NOT in slot order, a row count that disagrees with the occupied count, and a partially-bad batch being refused whole rather than half-applied (file: `tests/LazyMarketCompanion.Harness/Program.cs`).
- `AutoMarketService.MarketPrice(slot)` reads a market slot's current unit price back from the client (`InventoryManager.GetRetainerMarketPrice`), used to log any new listing still sitting at the placeholder price after a pass.

### Notes
- If you preferred the old behaviour - re-pricing everything on every Auto Market run - tick "Pinch everything after listing" in `/lmc` settings. Its tooltip now states which way is the default.
- The fallback is deliberately the SLOW path, never a skip: leaving a listing at 999,999,999 gil means it silently never sells, which is worse than spending the extra time.

## v0.1.2.0 (2026-09-05)

- Added the in-game "What's new" popup. After Lazy Market Companion updates, its changelog now opens once inside the game so you can see what changed without going to GitHub. It waits until you are logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/lmc changelog` any time to reopen it.
- No change to selling: the per-listing cap fix from 0.1.1.1 (99 units, 9999 for crystals/shards/clusters) and every Auto-Market and price-match setting are unchanged.

## v0.1.1.1 (2026-09-05)

### Fixed
- Auto Market could disconnect you from the game: a listing larger than the market accepts (99 units for normal items, 9999 for crystals/shards/clusters) is not refused by the server, it drops the connection. On 2026-09-05 15:04 the plugin sent Kukuru Butter HQ x297 in one MoveToRetainerMarket call and the client was kicked to the title screen 312 ms later (omasky dalamud.log; ffxivdb plugin_log_lines). Root cause: the per-listing stack size was only clamped to the item's BAG stack size (999 since patch 4.2), but patch 4.2 left "the maximum of 99 for items sold in markets" unchanged (Lodestone 4.2 notes); DailyRoutines' PriceAdjustWorker applies the same 99 / 9999 rule. Fix: new `MarketListingCap` (file: `AutoMarket/AutoMarketPlanner.cs`) and the planner now clamps every listing to it, so a 999 or "0 = max" stack size lists in 99s (file: `AutoMarket/AutoMarketPlanner.cs`, `Plan`).
- Pre-flight guard right before the game call: `AutoMarketService.Execute` refuses any op above the cap with an error log instead of sending it (file: `AutoMarket/AutoMarketService.cs`, `Execute` / `ItemMaxStack`).
- Config window: the per-listing stack input is clamped to the market cap and the tooltip states it (file: `Windows/ConfigWindow.cs`).

### Notes
- Crystal rules at 500 per listing are unaffected: seven Ice Crystal x500 listings went through on 0.1.1.0 at 15:12 without a disconnect, and the cap for 9999-stack items is 9999.
- Existing rules with stack size 100-999 (or 0 on a 999-stack item) now produce several 99-unit listings instead of one big one; the plan log says "stack size N clamped to the market's 99 per listing" once per rule.
- Harness cases 15-17 reproduce the 297 HQ incident, the crystal exception and the stack-0 case (file: `tests/LazyMarketCompanion.Harness/Program.cs`).

## v0.1.1.0 (2026-09-05)

### Fixed
- Crystals, shards and clusters were never listed by Auto Market: the stock snapshot only scanned Inventory1-4 and RetainerPage1-7, but crystals live in the Crystals (2001) and RetainerCrystals (12001) containers, so every crystal rule saw zero stock and was skipped without a word (file: `AutoMarket/AutoMarketService.cs`, `BagTypes` / `RetainerTypes`; reported by Joey 2026-09-05, Helm t-joey-1788632331833).

### Added
- Planner note when an enabled rule has no stock in any source it may sell from ("<item>: no stock in bags or retainer"), and when both sources are disabled (file: `AutoMarket/AutoMarketPlanner.cs`, `Plan`).
- Planner note when a source holds less than one full listing and "list partial stacks" is off ("<item>: N sellable in Bags is less than one full listing of M (partial stacks are off)"), instead of a silent skip. Relevant for crystals: a rule with stack size 0 defaults to the item's max stack, which is 9999 for crystals, so set a stack size on crystal rules or turn partial stacks on (file: `AutoMarket/AutoMarketPlanner.cs`, `Plan`).
- Harness cases 12-14 cover the crystal containers, the no-stock notes and the below-full-listing note (file: `tests/LazyMarketCompanion.Harness/Program.cs`).

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
