# Changelog

## v0.1.11.0 (2026-09-06)

### Added
- **Auto-Market can now decide which items are worth listing.** New setting in the Auto-Market tab: "Only list items worth more than [N] gil, net of fees". When it is on, Auto-Market checks every item on your list against current Universalis prices BEFORE anything is listed, and skips the ones whose total sellable value (current board price x everything it could sell of that item, after the market's 5% fee) is at or under your number. The setting ships OFF and the number ships at 0, so nothing changes until you turn it on (file: `AutoMarket/MarketGate.cs`, new).
- A held-back item is NOT sold, vendored or destroyed. It stays exactly where it was - your bags or the retainer inventory - and the closing chat line of the run says how many were held back. If that is ever not what you want, the items are still in your hands.
- Why it holds instead of selling to a vendor: you asked for below-threshold items to be vendored at the retainer. I checked, in the game's own data: the retainer bell menu has no vendor option at all - a retainer sells on the market board and that is all it does - and every shop in the game that buys items is an NPC you have to walk to. So at the retainer, holding an item is the only safe thing this plugin can do with it; selling it for vendor pennies somewhere else would mean walking, which you told me not to build.
- **A listing order for when the retainer's market is nearly full.** New dropdown in the Auto-Market tab: when there are not enough free market slots for everything on your list, "Fastest selling first" (the default) sends one Universalis request, ranks your items by how many of that item's quality actually sell per day, and gives the scarce slots to what moves. "Cheapest first" and "Most expensive first" rank by current board price from the same request, and "List order" is how it worked before (files: `AutoMarket/MarketGate.cs`; `AutoMarket/AutoMarketService.cs`, `ApplyValueGate`; `MarketAutomation.cs`, `StartGateLookup`).
- Items Universalis has no fresh data for keep their list position and sort last, and a failed request changes nothing: everything lists, in list order, exactly as before.

### Notes
- THE SAFETY RULE, because it decides everything: uncertain data always LISTs, never holds. No Universalis data for an item, data older than the freshness window (6 hours by default, same idea as the Auto Pinch pre-flight), no listing of the right quality on the board, or the request failing - in every one of those cases the item is listed as normal. Holding an item on a guess would be the irreversible side; listing an item the gate should have held only costs a market slot until it sells.
- The value check and the listing order share one Universalis request per retainer, fired before the plan is built, so an item the gate holds back cannot take a market slot another item could have used.
- The gate asks the same board your price matching uses - your home world unless "Use Universalis data center prices" is on.
- Universalis' per-item "sales per day" numbers only come back when the request also asks for recent sales; the gate's request does (your Auto Pinch pre-flight request is unchanged, byte for byte).
- New settings are new fields with defaults, so existing settings are untouched and there is nothing to re-tick after updating.
- Offline test suite is now at 258 checks, up from 221. The new case pins the gate math (the 5% fee, net-at-the-threshold holds, one gil above lists), the sort fixture (cheapest/fastest/dearest each reorder the same four items, ties and unknowns keep list order), the scarce-slot integration (with 2 free slots, fastest-first gives both to the fast item AND a control proves list order gave them to the first item), the per-quality velocity split, and the full uncertainty battery: stale, no timestamp, no data, no listing of the quality, null quote, gate off, threshold 0 - every one must LIST (file: `tests/LazyMarketCompanion.Harness/Program.cs`, case 36).

## v0.1.10.0 (2026-09-06)

### Fixed
- Auto Pinch's "skip listings that do not need pricing" check was asking the wrong market board. It asked about your whole data centre while your prices actually come from the in-game Compare Prices window, which is your home world - so the price it predicted almost never matched the price on your listing and it walked nearly every row anyway (file: `UniversalisPriceProvider.cs`, `GetQuotes`).
- It now asks whatever board your price matching uses: your world normally, or the data centre if you have "Use Universalis data center prices" turned on. Replayed over 80 of your live listings, this alone takes the skip count from 17 to 66.

### Added
- New setting "Only skip when nobody else is undercutting you", on by default, at the bottom of the Price Matching tab. It makes Auto Pinch judge a listing the same way your green/yellow/red overlay does: your own retainers' listings are ignored when working out the price to beat, and a row nobody else has undercut is left alone (files: `AutoMarket/PinchPreflight.cs`, rule 5a; `Configuration.cs`, `AutoPinchMirrorOverlay`).
- That covers two cases the scope fix alone does not: a listing whose only cheaper competition is another of your own retainers, and a listing nobody is undercutting at all.
- The setting does nothing while "Undercut my own retainers" is on. That setting means you want your own listings treated as competition, so ignoring them would make Auto Pinch skip a row it should have re-priced.

### Notes
- Every uncertainty still walks the row, exactly as before: no Universalis data, data older than your freshness window, an unreadable row, a failed or slow request, and a brand-new listing still at the placeholder price is never skipped under any circumstances.
- Your "Market Board Price Check Delay" and "Market Board Keep Open Time" are still the biggest single lever on how long a sweep takes. They have not been touched.
- The log line to look for is "pinch pre-flight: walking N of M row(s)", and it now names not-undercut skips separately.

## v0.1.9.0 (2026-09-06)

### Added
- Auto Pinch can now skip listings that do not need pricing. Before it opens a single listing it asks Universalis about every item on the retainer in one request, works out what price this plugin would set, and walks past the rows where that price is the price the listing already has (files: `AutoMarket/PinchPreflight.cs`, new; `AutoMarket/PriceMath.cs`, new; `MarketAutomation.cs`, `InsertPinchPass`).
- Measured on your 2026-09-06 11:26-11:36 sweep: of the 39 existing listings it re-priced, 17 came out at exactly the price they already had and 3 moved by a rounding error (243 to 242, 400 to 399, 30,971 to 30,951). That is 20 of 39 rows, about 3 minutes of a 9.5-minute sweep, spent writing numbers back unchanged.
- The reason those 17 are no-ops is not a bug: you are already the cheapest on the data centre for those items and "Match Self" is off, so the matched price IS your own price. That is exactly the condition this checks for.
- A "not worth it" threshold, shipping at 1%: a listing whose price would move by less than 1% of what it is priced at is left alone. That covers all three of the rounding-error moves above. There is also a gil threshold, shipping at 0 (off), if you would rather set an absolute floor.
- Four new settings at the bottom of the Price Matching tab: the on/off switch (on), how fresh Universalis data has to be before it is trusted (6 hours), the gil threshold (0 = off) and the percentage threshold (1%). Nothing needs re-ticking after the update - existing settings are untouched.

### Notes
- THE HONEST CATCH, because it decides whether you want this on: you do NOT price from Universalis. "Use Universalis data center prices" is off in your config, so the real price comes from the in-game Compare Prices window. This pre-flight uses Universalis regardless, because it has to judge 20 items before opening any of them. So it PREDICTS what the pricing pass would do, and it can be wrong when Universalis and the in-game board disagree.
- The specific risk in one sentence: someone undercuts you, nobody has uploaded that to Universalis yet, so it still reads as "you are cheapest", and that listing sits at its old price until the next sweep.
- Everything uncertain walks the row rather than skipping it, deliberately: no Universalis data, data older than the freshness window, an unreadable row, a request that fails or times out, or the whole request coming back empty. A needless walk costs about ten seconds; a wrong skip can cost a sale.
- A new listing still at the 999,999,999 gil placeholder is never skipped under any circumstances, whatever the board says. That rule is checked first, before anything else is even looked at.
- The pre-flight only applies to the full-row pinch passes - the Auto Pinch button, the Auto Pinch sweep, and "Pinch everything after listing". The "price only what I just listed" pass from 0.1.6.0 is untouched.
- One log line per pass says what it did, e.g. "pinch pre-flight: walking 19 of 39 row(s); skipped 17 already at the right price, 3 under the threshold" - so you can check what it actually skipped rather than take my word for it.
- SAID AGAIN BECAUSE IT MATTERS MORE THAN THIS FEATURE: your "Market Board Price Check Delay" is 5,069 ms and "Market Board Keep Open Time" is 5,041 ms, against defaults of 3,000 and 1,000. Those two ARE the 10.5 seconds per row. Halving them would save more time than this whole feature does. They are yours to change and I have not touched them.
- The price formula moved into one place (`AutoMarket/PriceMath.cs`) so the pre-flight's prediction and the pricing pass cannot drift apart. If they ever differed, the pre-flight would skip rows the pass would really have re-priced, which is the one failure mode worth caring about here.
- No settings were changed, so there is nothing to migrate. The four new ones are new fields with defaults, which existing configs pick up as-is.
- Offline test suite is now at 221 checks, up from 171. The new ones replay your 39-row sweep and assert exactly 17 already-right skips, exactly 3 threshold skips and 19 rows still walked - plus a control that the old behaviour walked all 39, a check that the shared price formula matches the pre-0.1.9.0 one over 320 inputs, and cases proving stale data, missing data, an HQ row, and a placeholder listing all still get walked (file: `tests/LazyMarketCompanion.Harness/Program.cs`).

## v0.1.8.0 (2026-09-06)

### Added
- **Price from recent sales when nothing is on the board.** New tick box in the Price Matching tab, off until you turn it on. When a price check finds NOTHING listed on your data centre, the listing is priced at the **median of the recent data-centre sales** from Universalis instead of being abandoned at the 999,999,999 gil placeholder with "no board price found" (files: `SaleHistoryPricing.cs` new; `UniversalisPriceProvider.cs`, `GetSaleHistoryPrice` / `PriceFromSaleHistory`; `MarketAutomation.cs`, `SetNewPrice` / `StartSaleHistoryRequest`; asked for by Joey 2026-09-06).
- **A freshness guard, set to 30 days.** If the newest sale is older than that, no price is invented: the listing keeps the placeholder price and you get the same chat message as before. Some items last sold years ago - one measured item's newest data-centre sale is from June 2022 - and pricing off that is a guess, not a price. The window is editable under the tick box (1-365 days).

### Changed
- Universalis requests now ask for recent sales (`entries`) as well as live listings, but **only while the new tick box is on**. From 0.1.0.0 to 0.1.7.0 every request said `entries=0`, which is why Universalis' own average and sale-velocity numbers always came back as 0 (file: `UniversalisClient.cs`, `GetMarketData`). The sales ride along in the same request as the listings, so an empty board still costs one call, not two.

### Notes
- **Nothing changes until you tick it.** The setting ships off, the request shape is byte-for-byte what it was while it is off, and an item with a live board is priced exactly as before - matching the cheapest listing, as always.
- **The median, not the average.** Universalis' `averagePrice` is skewed by outliers: measured on item 16644 (empty board, 2026-09-06) it reported 1,824,207 gil, while the median of the same 10 sales was 53,550. The average is not usable as a listing price, so it is not used.
- **Your undercut/match setting is not applied to a history price.** There is no competing listing to undercut, and the median already is what the item has been clearing at. Your per-item minimum/maximum price limits DO still apply, and they remain the safety net here - the max-cut percentage guard has never applied to a brand-new listing.
- An item with an empty board *and* no sale history at all (they exist) is still left at the placeholder. Nothing can price those.
- If Universalis does not answer within 6 seconds the listing is left unpriced and the sweep carries on, rather than stalling the run.
- Offline test suite is now at 169 checks, up from 147. The 22 new ones replay real Universalis data pulled on 2026-09-06: item 16644 must price at 53,550, item 30037 (2022 sales) must be refused, an item with no history must be distinguished from a stale one, the 30-day boundary is asserted from both sides, HQ pricing must ignore NQ sales, and a control asserts the number really is the median and not the newest/cheapest/mean sale (file: `tests/LazyMarketCompanion.Harness/Program.cs`).

## v0.1.7.0 (2026-09-05)

### Fixed
- Auto Market no longer re-prices a retainer it did not put anything up on. If a retainer's market board is already full there is nothing to list, and until now that made the plugin fall through to re-pricing all 20 of that retainer's existing listings - the exact thing you asked it to stop doing, on the retainers where it had done no work at all (file: `AutoMarket/PinchScope.cs`, new; `MarketAutomation.cs`; reported by Joey 2026-09-05).
- This was never a new bug: it is how "Auto Market" behaved from the very first release, when the button meant "list, then Auto Pinch everything". The three previous fixes all lived in the code that prices new listings, and a retainer with nothing to list never got that far.
- On a retainer where nothing was listed, the log now says so ("nothing was listed on this retainer, leaving its listings alone") instead of silently starting a 20-row re-pricing pass.

### Notes
- Nothing is lost: re-pricing an entire retainer is still available two ways, and both are things you ask for on purpose - the Auto Pinch button, and the "Pinch everything after listing" setting in the Auto Market tab.
- The "Pinch everything after listing" setting is unchanged and still means exactly what it says, including on a retainer that had nothing to list.
- Both Auto Market button tooltips were still promising the old behaviour ("Auto-Market then Auto Pinch every enabled retainer") and now describe what actually happens.
- No settings changed, so there is nothing to migrate and nothing to re-tick after updating.
- Offline test suite is now at 147 checks, up from 137. The new ones replay your 4-retainer sweep (3 listings, full, 1 listing, full) and assert that 2 retainers price only their new listings, 2 price nothing, and none re-price everything - plus a control that the old condition really did re-pass exactly the 2 full ones, and a control that "Pinch everything after listing" still re-prices the lot (file: `tests/LazyMarketCompanion.Harness/Program.cs`).

## v0.1.6.0 (2026-09-05)

### Fixed
- Auto Market was STILL re-pricing your whole retainer, even after 0.1.5.0. It now picks its listings the way you asked for: it reads back what every one of your 20 listings is currently priced at, and the ones it just created are the ones still sitting at the 999,999,999 gil placeholder. That comes from the game's own retainer data, so it does not care what order your sell list is in, whether a row is scrolled off screen, or how an item's name is spelled (files: `AutoMarket/AutoMarketService.cs`, `MarketPricesBySlot`; `AutoMarket/SellListRows.cs`, `ScanPlaceholders`; asked for by Joey 2026-09-05).
- A listing you priced yourself is never at the placeholder price, so it is now impossible for this pass to re-price one. That is a stronger promise than the name-matching it replaces, because it is a number the game gives us rather than text read off a label.
- The specific failure on 2026-09-05 at 20:37: one new listing went into market slot 10, the plugin found its row correctly, and then threw the whole thing away and re-priced all 20 anyway - because an unrelated row further up the list disagreed with itself. Rows you are not pricing can no longer cancel the ones you are (file: `AutoMarket/SellListRows.cs`, `MatchBySlot`).
- That unrelated row disagreed because the plugin misread it. Its item was "Snow Cotton Ushanka of Scouting", the sell list clipped the name to fit the column, and the leftover text was read as a completely different real item called "Snow Cotton". Item names are now only accepted when nothing else fits the text just as well - a clipped name reads as "cannot tell", never as the wrong item (file: `AutoMarket/ItemNameMatch.cs`, new; `ItemNameResolver.cs`).
- A listing that already carries a real price is left alone instead of being re-priced, and if every listing this run created is already priced the pass now simply finishes rather than falling back to re-pricing everything (file: `MarketAutomation.cs`, `InsertPinchForNewListings`).

### Notes
- If one of your listings happens to be sitting at 999,999,999 gil and this run did not create it, it is deliberately left untouched and mentioned in the log. Being at the placeholder price is not on its own permission to re-price something.
- The safety checks from 0.1.3.0 all still run, and the per-listing confirmation just before any price is written is unchanged - it has never once let a wrong listing through (file: `MarketAutomation.cs`, `VerifyPinchRow`).
- The "If a new listing can't be found" setting added in 0.1.5.0 is unchanged and still defaults to re-pricing everything. It now applies to a much narrower set of situations, and on a normal run it should never come up at all.
- Offline test suite is now at 137 checks, up from 93. The new ones replay the 20:37 failure with the real item ids from the log and assert both halves: that the old logic vetoed it and that the new logic prices only the one new listing. There is also a check that a placeholder-priced listing this run did not create is never selected (file: `tests/LazyMarketCompanion.Harness/Program.cs`).
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
