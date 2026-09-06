# Changelog

## v0.1.6.11 (2026-09-06)

### Added
- **LazyCrafter now tells you when a material is sold by a currency vendor, and can send you there instead of the market board.** A cart short of Emery used to say `needs market Emery` and nothing else; it now says `Emery x1 - or Ixali vendor (North Shroud) for 7 Ixali Oaknots`. Beast-tribe traders, Grand Company quartermasters, scrip and token counters are all read the same way (files: `Core/SpecialShop.cs`, `Adapters/LuminaGameData.cs` `LoadShops`, `Adapters/VendorLocator.cs` `SpecialShopCandidates`)
- New setting, ON by default: *"Prefer a currency shop over the market board when you can already afford it"*. The cheapest affordable offer wins, so 7 Ixali Oaknots beats a 1,500-seal price for the same item. Turning it off keeps the naming and just leaves your purchases on the market board (files: `Configuration.cs` `PreferCurrencyShops`, `UI/SettingsTab.cs`)
- Currency-shop items get their own line in the blocked report, their own row in the Run tab with a **Flag on map** button, and their own entry in `/lcraft plan` - the instruction is "walk to this NPC and trade", which is not the same instruction as "buy this on the board" (files: `Core/PlanReport.cs`, `Core/RunReport.cs` `BlockedLines`, `UI/RunTab.cs` `FlagCurrencyShop`, `Adapters/Dispatch/LifestreamDispatch.cs` `GoToCurrencyShop`)
- A craft waiting on one now says `needs currency shop Emery` rather than `needs market Emery`, so the deferral names the counter you actually have to visit (file: `Core/DispatchPlan.cs` `VisitIngredient`)

### Fixed
- **The plugin knew nothing about currency vendors, and the sheets had the answer all along.** `LoadShops` read every `SpecialShop` row, kept the item ids in a `HashSet`, and threw away the shop, the NPC and the price - so the most it could ever say was "some special shop gives this out". It now keeps the shop id, the receive quantity and the cost items, and the shop id is what lets the existing gil-vendor placement chain (shop -> NPC -> map coordinates -> aetheryte) be reused unchanged for currency vendors (files: `Adapters/LuminaGameData.cs` lines 183-193 as was, `Adapters/VendorLocator.cs`)
- Prices use the game's own plural, so a cost reads `1,500 Storm Seals` and not `1,500 Storm Seal`. Taken from `Item.Plural` rather than derived with an English rule, which would be wrong for exactly the irregular names a currency list is full of (files: `Adapters/LuminaGameData.cs` `ItemPlural`, `Core/SpecialShop.cs` `SpecialShopCost.Phrase`)

### Changed
- 0.1.6.10's zip embedded the notes under the previous version's heading in the installer list. 0.1.6.11 is the same build with the manifest corrected - nothing in the plugin changed
- The market and manual shopping lines are now rendered in Core (`PlanReport`) instead of inline in the dispatcher, so the offline harness asserts on the sentence you actually read. Both of the last two defects on this feature were renderer bugs where the right answer sat in memory and was dropped on the way to chat, and a test on the internal value stayed green through both (files: `Core/PlanReport.cs`, `Adapters/DispatchService.cs`, `Adapters/Dispatch/LifestreamDispatch.cs` `GoToMarket`)
- A currency-shop item counts towards the wave loop's progress check, so a run cannot end early declaring "nothing changed" while you are off fetching one (file: `Core/DispatchLoop.cs` `ItemsOf` / `Describe`)
- Config version 6 -> 7. Nothing is rewritten; `PreferCurrencyShops` is new and existing configs take its ON default (file: `Configuration.cs` `MigrateIfNeeded`)

### Notes
- **It will never make a trade you cannot pay for, and never leaves a material with no source.** The currency shop is preferred only when all of the following hold: the item resolves to a named NPC, that NPC is placed, the placement is in a zone with a teleportable aetheryte, the price is known, your balance of that currency can be read, and it already covers the cost. If any one of those fails, the item stays on the market board exactly as it did in 0.1.6.6 - and the vendor is still named. A balance that cannot be read counts as "cannot afford", so the only way the currency read can be wrong is by sending you back to the market board.
- **That fallback is the normal path, not an edge case.** Measured against the installed game data: 12,886 items are receivable from a special shop, 6,364 have any handler NPC, and only 668 have one that is actually placed somewhere you can stand. The great majority of currency-shop items therefore keep their existing routing untouched.
- **This is not the "move SpecialShop above Market" change it was originally described as, and that change would have made things worse.** There was no special-shop route to reorder: `SourceKind.SpecialShop` mapped to `Route.Manual`, the fall-through, so promoting it above Market would have turned an actionable market listing into `needs a manual source: Emery x1`, a dead end. A real route had to be built, with the fallback as its load-bearing behaviour. There is a harness check pinning exactly that regression.
- **It never opens a shop window or makes a trade for you.** It names the vendor, flags them on your map and prints the price; spending your seals is still your decision, and the plugin has no exchange rate between a currency and gil to make it with.
- Grand Company quartermasters do not appear for Emery specifically - the sheets list only the Ixali vendor (7 Ixali Oaknots) and Talan (1 Fluorite Lens), and Talan is in no placement table at all, so only the Ixali vendor is offered for it. The quartermaster path exists for the many items where the sheets do carry it.
- Proved offline before shipping: 290/290 in `tests/LazyCrafter.Harness` (40 new checks for this change, on top of the suites that shipped in 0.1.6.7 and 0.1.6.8 earlier today) that assert on the RENDERED plan and report text rather than on an internal value. Every fix was individually reverted and the matching checks confirmed to fail before being restored to green, including the two that matter most: the naive "reorder" (which reds 14 checks, among them the market-board fallback) and the removal of the affordability gate (10 checks). Verified against the live sheets on the incident's own worked example - Emery 7601, needed x1 for Iolite r30406.
## v0.1.6.9 (2026-09-06)


- Superseded within the hour by 0.1.6.10, then 0.1.6.11 - the same build republished with corrected version headers (0.1.6.9's header carried a doubled date). All fixes from 0.1.6.7/0.1.6.8 remain present. (file: `CHANGELOG.md`)
## v0.1.6.8 (2026-09-06)

### Added
- **Before every craft, LazyCrafter now checks whether the game can actually accept a command - and if a window is holding the client, it WAITS for you instead of erroring out.** If you finish a shopping cart at the market board and the board window is still open, the run now says `waiting - close the market board to continue` and holds there, re-checking. The moment you close the window it resumes the cart BY ITSELF - no button, no re-plan, nothing lost. Your cart now survives a shopping trip without you touching anything (file: `Adapters/DispatchService.cs` `Phase.Crafts` craft gate + new `Phase.WaitClientFree`)
- **If you walk away with a window open, the run stops cleanly after five minutes** with `stopped - the market board blocked crafting for 5 minutes - close it and press Resume (or /lcraft resume) to continue the same cart.` The cart is preserved, so Resume re-plans and continues exactly as before; the hold never outlives the cap (file: `Adapters/DispatchService.cs` `Phase.WaitClientFree` timeout -> `FinishBlocked`, cap in `Core/ClientWaitPolicy.cs`)
- **The same check now guards Resume.** Pressing Resume with the market board still open used to re-enter the identical broken state (observed in the 2026-09-06 11:58 run: Resume at 11:59:11, still spinning at 12:02). Now the resumed cart holds first and waits for the window, exactly like a mid-cart block (file: `Adapters/DispatchService.cs` `Resume`)

### Changed
- The client-busy check itself got sharper: the addon half now requires the window to be VISIBLE, not merely loaded (some game windows stay loaded after closing - holding the cart for one would be the exact "walked away" failure the cap bounds), and it recognises more windows by name: the market-board purchase window, the retainer venture prompt and result, desynthesis, repair, materia meld, the bank, quantity input and dialogue boxes (file: `Adapters/ClientReadiness.cs`)
- The condition-flag half of the check is now Artisan's own refusal set, verbatim: the nine `Occupied*` conditions Artisan itself treats as "the game will not take a craft command", plus trade / cutscene / zone-change. The crafting conditions (`Crafting`, `PreparingToCraft`, `ExecutingCraftingAction`) are deliberately NOT in it - they are what a working craft looks like, and gating on them would deadlock the dispatcher against itself (files: `Core/ClientWaitPolicy.cs`, `Adapters/ClientReadiness.cs`; flag list pinned by tests)
- While held, the Run tab status counts the hold (`waiting - the market board (1:30)`) and a "still working" heartbeat fires every 3 minutes, so a hold never reads as a hang; the waiting line is printed to chat ONCE on entering the hold (and once more if a DIFFERENT window takes over), not on every poll (file: `Adapters/DispatchService.cs` `Phase.WaitClientFree`)

### Notes
- **Exactly the behaviour picked: wait-and-resume.** Not stop-immediately, not a setting, not a different cap. The five-minute hold and the exact `waiting - close ... to continue` wording are what was shipped.
- **Nothing about genuine shortages or the 0.1.6.7 diagnosis changed.** A material that really is missing is still reported exactly as in 0.1.6.7; a refused craft is still never called a missing material. This build adds the hold in front of the craft, it does not touch the reporting behind it.
- Proved offline before shipping: 264/264 in `tests/LazyCrafter.Harness` (was 250/250), with 14 new checks that drive the hold through a fake clock and assert on the RENDERED chat and status text - entry line, resume line, timeout reason, the never-outlives-the-cap property, and the flag table itself (Artisan's refusal set verbatim, disjoint from the crafting conditions). Two negative controls were run: disabling the craft gate and shrinking the cap to 2 minutes each turned exactly the matching checks red before the code was restored to green (file: `tests/LazyCrafter.Harness/ClientWaitTests.cs`).

## v0.1.6.7 (2026-09-06)

### Fixed
- **A craft the game refused because a window was open is no longer reported as a missing material.** If you finish buying at the market board and press Resume with the board still open, the game refuses every craft command ("Unable to execute command while occupied") and Artisan bounces instantly. LazyCrafter recorded only "expected 98, made 0" - the reason was thrown away - so on the next pass it saw the intermediates were not in your bags, concluded they must be somewhere else, and told you to go and retrieve two materials that had never existed. It then sent you to a summoning bell for them. It now says the true thing instead: the craft was refused, the window that was holding it up is named, and it tells you plainly that nothing is missing from your bags (files: `Core/CraftDiagnosis.cs`, `Adapters/ClientReadiness.cs`, `Adapters/DispatchService.cs` `WaitCraftEnd` / `WaitCraftStart` / `Phase.Crafts`)
- **A material that only failed to exist because its craft was blocked no longer appears in the "pull these off sale" list.** In the 2026-09-06 11:58 run the only material really listed for sale was Cloud Mica x3 on Hussypants; Adamantite Nugget x98 and Cloud Mica Whetstone x99 were noise the bug generated, and they were named as things to unlist even though no retainer was holding any of them. Only genuinely listed materials reach that list now (files: `Core/CraftDiagnosis.cs` `WithoutPhantoms`, `Adapters/DispatchService.cs` `ReportBlockedListings`)
- **The walk to the summoning bell no longer fires for a problem that does not exist.** With nothing genuinely listed for sale, the run now ends without moving you. When something really is on the board, the walk still happens exactly as before (file: `Adapters/DispatchService.cs` `ReportBlockedListings` / `WalkToBell`)
- **A blocked craft no longer opens a retainer bell session on the next pass.** The deferral reason for a refused craft no longer carries the internal `retrieve #` marker, which is what queues an Artisan retainer withdrawal - so the next pass no longer tries to fetch a material that is nowhere (files: `Core/CraftDiagnosis.cs` `DeferralReason`, `Core/RetainerBatch.cs` unchanged and still driven by that marker)

### Added
- LazyCrafter now checks whether the game can actually accept a command before blaming your bags, and can name what is holding it: the market board, a retainer's inventory, the retainer bell, a shop, a dialogue box, a yes/no prompt, a trade window, a cutscene or a zone change (file: `Adapters/ClientReadiness.cs`)

### Notes
- **This build does not wait for the window to close, and does not stop the run when it finds one.** It only stops lying about what went wrong. Whether a blocked craft should wait for you to close the window, or stop the run cleanly, is a separate question and is not answered here.
- **Nothing about a genuine shortage changed.** A material that really is sitting on a retainer, in the saddlebag or on the market board is reported exactly as it was in 0.1.6.6, with the same wording, and is still retrieved, still named and still walked to. Both halves are pinned by tests.
- Proved offline before shipping: 250/250 in `tests/LazyCrafter.Harness` (was 231/231), with 19 new checks that replay the 11:58 run and assert on the RENDERED chat text rather than on an internal value - the defect is a reporting defect, and a test on the internal shortfall list would have stayed green right through it. Two negative controls were run: reverting the diagnosis turned exactly 5 of those checks red, and "fixing" it by silently dropping the phantom materials instead of explaining them turned 4 red - so the suite cannot be satisfied by suppression (file: `tests/LazyCrafter.Harness/OccupiedCraftTests.cs`).

## v0.1.6.6 (2026-09-06)

### Added
- **When a cart is blocked because you have the material listed for sale, LazyCrafter now tells you exactly which retainer to visit and how many units to pull off sale.** One line per retainer, so a single summoning-bell trip clears the whole group: `Hussypants: Silver Ore x7, Iron Ore x6, Cloud Mica x3`. Previously you were told a count and nothing else, and had to work out for yourself which of a dozen materials was actually stuck and where it had gone (files: `Core/BlockedListings.cs`, `Adapters/DispatchService.cs` `ReportBlockedListings`)
- `/lcraft blocked` prints the full per-item detail of the last run's blocked list on demand - every material, its units, the retainer holding it, and the verbatim reason for anything blocked for some other cause. It keeps working after the run has ended, so the end-of-run block can stay short (files: `Plugin.cs` `PrintBlockedListings`, `Core/BlockedListings.cs` `Detail`)
- **After a run ends blocked on your own listings, LazyCrafter walks you to the nearest summoning bell** via Lifestream's own "go to market board" (`/li mb`) - the bells stand with the market boards at every aetheryte plaza. New setting, ON by default: *"When a run ends blocked on your own market listings, walk to the nearest summoning bell"*. It fires ONLY when a run has ENDED and something really is listing-blocked - never mid-craft, never on a clean run, never for a material that was merely slow (files: `Configuration.cs` `WalkToBellWhenBlocked`, `UI/SettingsTab.cs`, `Adapters/DispatchService.cs` `WalkToBell`, `Adapters/Dispatch/LifestreamDispatch.cs` `GoToMarketBoard`)

### Fixed
- **The finishing path threw the whole explanation away.** A run that FINISHED rendered only `", 12 could not be retrieved"` while the retainer names, item ids and quantities sat in memory and were discarded; a run that STOPPED rendered the full detail. The two endings disagreed because `Finish` and `FinishBlocked` each rendered the blocked list their own way and only one of them had been taught about retrievals. Both now call one shared `BlockedListings.MergeIntoBlocked` in Core, so `/lcraft status`, the Run tab and the chat block say the same thing whichever way the run ended (file: `Adapters/DispatchService.cs`, `Finish` and `FinishBlocked`; `Core/BlockedListings.cs` `MergeIntoBlocked`)
- **A material that was merely slow was about to be reported as "go unlist something".** The unfetched list mixes six unrelated causes - a market listing, a settings/preflight blocker, a 10-minute batch session timeout, an Artisan start error, a 4-minute per-item timeout, and an exhausted partial pull - and only the first means anything is listed for sale. The "pull these off sale" instruction is now decided from the DATA (every place holding the stock is unreachable, which only a market-board listing ever is), not from the wording of the reason. Everything else is reported separately, under its own heading, explicitly marked as NOT listed for sale (file: `Core/BlockedListings.cs` `IsListingBlocked`)
- **An item that came back partially could be counted twice.** A partial retainer pull re-queues the remainder, so the same material could appear in the blocked list twice with different quantities. Entries are now folded per item and reported once. The kept figure is the larger of the two, not their total: the second entry is a *remainder of* the first, so adding them would overstate what you have to pull (file: `Core/BlockedListings.cs` `Summarise`)

### Changed
- **The twelve-line wall is gone.** A cart short of twelve materials printed twelve near-identical multi-clause warnings, each in error red, and the actual instruction was in none of them. Each refusal is now one short line at normal level - kept, because silence during a long run reads as a hang - and the one actionable summary prints once at the end (files: `Adapters/DispatchService.cs` `Phase.Retrieve`, `Core/BlockedListings.cs` `RefusalLine`)
- `StoredElsewhere` carries the retainer's bare name alongside the display text, so the summary can group by retainer without parsing a sentence back apart. Set by the producer in `AllaganInventory` (both the retainer-bags and the market-listing splits); falls back to the place name when the retainer names could not be read (files: `Core/Model/StoredElsewhere.cs`, `Adapters/AllaganInventory.cs` `SplitRetainers` / `SplitListings`)
- Config version 5 -> 6. Nothing is rewritten; `WalkToBellWhenBlocked` is new and existing configs take its ON default (file: `Configuration.cs` `MigrateIfNeeded`)

### Notes
- **This build takes no game actions and unlists nothing.** It names what to pull and walks you to a bell; opening the retainer window and taking the item off sale is still yours to do. Assisted pulls (Tier 2) and automatic unlisting (Tier 3) were deliberately left out of this build.
- **HQ vs NQ is not distinguished, and that is a known limitation.** The AllaganTools inventory bridge has no HQ dimension, so the advice reads "pull 7 Silver Ore from Hussypants" and cannot tell you to pull the HQ ones specifically.
- The bell walk adds no navigation of its own. Lifestream exposes no summoning-bell IPC - there is no "bell" string anywhere in its assembly - so this reuses the existing `/li mb` market-board destination, which puts you at the same plaza. No vnavmesh, and if Lifestream is missing or busy the destination is printed and the walk is skipped rather than faked.
- Proved offline before shipping: 231/231 in `tests/LazyCrafter.Harness` (was 209/209), with 22 new checks that assert on the RENDERED report rather than on an internal value - the defect being fixed was a renderer defect, and a test on the internal list would have stayed green throughout it. Each of the three fixes was individually reverted and exactly the matching checks were confirmed to fail (A: 3 checks, B: 4 checks, C: 4 checks).

## v0.1.6.5 (2026-09-05)

### Added
- `/lcraft spike` - a one-off test command that walks you to five gil vendors and reports what happened. It exists so the walk-to-vendor feature can be judged from a real run instead of guessed at: type `/lcraft spike all` anywhere, in any job, with nothing in your cart, and LazyCrafter teleports to Limsa, Ul'dah and Gridania in turn, walks to a named shopkeeper in each, and opens their shop. It takes roughly three to five minutes for all five. Then `/lcraft spike results` prints a short block and copies it to your clipboard - paste that back and it decides whether the real feature gets built (files: `Spike/VendorSpike.cs`, `Core/SpikeReport.cs`)
- The result block names every vendor separately: pass or fail, how long it took, and on a failure the exact step that broke - the teleport, the zone loading, the navmesh, the walk, the shop menu, or the shop window itself. A bare score with no reasons is no use to anyone (file: `Core/SpikeReport.cs`)
- `/lcraft spike list` names the five vendors, `/lcraft spike 1`..`5` runs just one, `/lcraft spike stop` aborts the current one

### Notes
- **Nothing else changed.** The spike command is inert: no cart run, dispatch, shopping list, map flag or vendor hand-off behaves any differently from 0.1.6.4, and LazyCrafter still never teleports or walks you anywhere during a normal run. This build exists only to carry the test command (file: `Plugin.cs`; the two `teleport: false` hand-offs in `Adapters/DispatchService.cs` are untouched)
- vnavmesh and Lifestream are both required. If either is missing or not answering, the command says so in one plain sentence naming the plugin and stops - it never silently does nothing, and a missing plugin is never counted as a failed vendor (file: `Spike/VendorSpike.cs`, `Preflight`)
- The shop menu entry is now chosen by its TEXT, using each NPC's own shop names read from the game's data files, instead of by position in the list. The earlier draft picked the first entry, which is wrong for four of these five NPCs - Bango Zango's first entry is a quest, and Rianne has no "Purchase Items" entry at all (file: `Spike/VendorSpike.cs`, `SelectShopEntry`)
- All five vendor positions were re-verified against the installed game data from two independent sources, which agreed to within 0.1 yalms. Two of them - Bango Zango and Gerulf - are not in the file the first draft read, so those coordinates had never actually been checked
- The `Walk to vendors with vnavmesh` setting stays absent. It comes back only if all five vendors pass; four out of five is not enough
- Tests: 12 new offline checks on the result block, including controls proving four-of-five never reads as a pass and that a failure always names its step (file: `tests/LazyCrafter.Harness/SpikeReportTests.cs`; 209/209 passing, was 197/197)

## v0.1.6.4 (2026-09-05)

- Fixed: a `retrieve` line could send you to the market board for materials that were actually sitting on a retainer. If you had more of an item listed for sale than the retainer was holding, LazyCrafter named the listing as the place to fetch from - so a run said things like `8x from the market board (listed by retainer Hussypants)` when those 8 were on retainer Dojarat and could be pulled at any summoning bell (file: `Core/DispatchPlan.cs`, function: `PlacesFor`)
- Nothing was miscounted: the amount, the `have` total and the routing were all correct, and no craft was blocked that should not have been. Only the place name was wrong - but it reads exactly like the older bug where listings counted as stock you had, so it cost time to diagnose every time it appeared in a log
- The cause was that the places were sorted purely by how much each held, with no notion of whether you can go and get it, so a big listing outranked a small retainer stack. Places you can actually fetch from are now always offered first, and a listing is named only when nothing reachable holds the item (file: `Core/DispatchPlan.cs`, function: `PlacesFor`; `Core/Model/StoredElsewhere.cs`, new `Fetchable` flag)
- Your listings are still shown exactly as before - the fix changes which place is chosen, not what you are told about (file: `Adapters/AllaganInventory.cs`, function: `StoredWhere`)
- The ingredient tree's Retrieve button no longer counts listed stock towards what it offers to fetch, so it cannot offer a retrieval the run would then refuse; the same correction applies to a single-item Retrieve and to materials re-queued after a retainer pass (files: `UI/IngredientTree.cs`, `Adapters/DispatchService.cs`)
- Tests: 10 new harness checks, including the reported case, a control at the size where the old code happened to be right, a control proving a listing alone is still never a retrieval, and one proving a listing is still named - so this cannot be `fixed` by hiding listings (file: `tests/LazyCrafter.Harness/MarketListingTests.cs`)

## v0.1.6.3 (2026-09-05)

### Fixed
- The cart run and the `Flag on map` / `Vendor` buttons could send you to two DIFFERENT vendors for the same item, and whichever printed last won your map flag: on a 2026-09-05 run the chat block said Engerrand in Limsa Lominsa while the map flag landed on a traveling material supplier in The Azim Steppe, both for Tallow Candle. There was one vendor picker for cart runs (which ranked by lowest internal NPC id, so on a single-item list it never looked at distance at all) and a different one for the per-item buttons (which ranked by map distance to the nearest aetheryte). There is now exactly ONE ranking and every button and every chat line goes through it (file: `Core/VendorChoice.cs`, `Adapters/VendorLocator.cs`).
- Vendor choice now considers where YOU are standing: a vendor in the zone you are already in wins outright, then the cheapest teleport (read from your own attuned aetheryte list, so somewhere you have not attuned never wins), then the shortest walk after you land. Previously `nearest` meant `nearest to some aetheryte`, which happily sent you to Stormblood for a candle you could buy in the city you were standing in (file: `Core/VendorChoice.cs`, `Adapters/VendorContextProvider.cs`).
- The vendor named in the Run tab's blocked list is now the same vendor the chat line and the map flag name. It was resolved separately per item, so grouping several items onto one stop could leave the tab pointing somewhere else (file: `Adapters/DispatchService.cs`, `BuildBlocked`).

### Removed
- The Settings checkbox `Walk to vendors with vnavmesh after a Lifestream teleport (experimental)`. Nothing ever read it: the vnavmesh walk-to-vendor spike was skipped, so there was no walking to switch on, and the checkbox has been tickable and inert since it shipped. Its own help text claimed it was gated on that spike passing. If the walk feature is ever built the toggle comes back with it (file: `UI/SettingsTab.cs`; the config field is kept, unused, so existing settings still load).

### Notes
- Ranking degrades safely: not logged in, or if the teleport list cannot be read, it falls back to the old walk-from-aetheryte order rather than failing the hand-off.
- Covered by 25 new offline regression tests (`tests/LazyCrafter.Harness`, suite `vendor`), including the exact Tallow Candle fixture from the 2026-09-05 run and two negative controls that fail if the two old rankings are ever reintroduced.

## v0.1.6.2 (2026-09-05)

- Fixed: putting more than one item in the cart could send materials you can obviously gather or craft to the "needs a manual source" list, and then refuse to craft anything that used them. In the reported run, Iron Ore, Silver Ore, Moko Grass and Adamantite Nugget were all declared manual and 49 recipes were deferred behind them - the exact same items had been gathered normally by a single-item cart thirteen minutes earlier (file: `Core/Tiering.cs`, function: `AssessCart`)
- The cause was a label being reused, not a quantity being counted twice. When two cart lines need the same material, LazyCrafter merges them into one total. If the first line was fully covered by stock, that line's verdict - "you already have this" - was kept for the merged total even after the second line made it short. Nothing else in the plan knows where to get a material you supposedly already have, so it fell through to manual, and every recipe above it was blocked (file: `Core/Tiering.cs`)
- Each merged total is now re-checked against the combined amount you need and the combined amount you have, so a material short across the whole cart is sourced the same way it would be in a cart of one (file: `Core/Tiering.cs`, function: `AssessCart`)
- Single-item carts were never affected - there was nothing to merge - which is why this only showed up when adding several things at once
- Tests: 4 new harness checks, including a control proving the routing code itself was never wrong, so the fix had to be made where the bad label is produced (file: `tests/LazyCrafter.Harness/CartTests.cs`)

## v0.1.6.1 (2026-09-05)

- Fixed: an item you had listed for sale on the market board counted as stock you already had, which could stop a whole cart. LazyCrafter saw the listing, decided nothing was missing, and asked you to "retrieve" it from the board - something a summoning bell cannot do - so every craft above that item sat waiting forever. A run for one Alpine Chandelier stalled on a single listed Hardsilver Nugget it could simply have made (file: `Adapters/InventorySource.cs`, `InventorySources.RetainerTypes`)
- Your listings are still shown, just no longer counted: the plan says "on the market board (listed by retainer X)" as information, and the item is treated as one you still need - so it gets crafted, gathered or bought like anything else (file: `Adapters/AllaganInventory.cs`, function: `StoredWhere`)
- Fixed: many gatherable materials were being sent to the market board instead of to GatherBuddy. Node contents are listed in two places in the game data and LazyCrafter only read one of them, so 79 items - Titanium Ore and Hardsilver Ore among them - looked like they had no node at all. The same Alpine Chandelier run was told to buy 15 Titanium Ore it could have mined (file: `Adapters/LuminaGameData.cs`, function: `LoadGathering`)
- Materials from the two retired Diadem versions (the Grade 2 and Grade 3 Skybuilders' sets, 70 items) are no longer offered as gathers, matching what GatherBuddy itself will accept; the two Oddly Delicate items that are still gatherable there are kept (file: `Adapters/LuminaGameData.cs`)
- The startup line about GatherBuddy data now says how many of its entries actually have a reachable node, instead of a raw total that could not be compared with anything (file: `Adapters/GbrData.cs`)
- Tests: 24 new harness checks covering both fixes, including negative controls that reproduce the original stall and confirm each fix is needed on its own (files: `tests/LazyCrafter.Harness/MarketListingTests.cs`, `tests/LazyCrafter.Harness/CartReplayTests.cs`)
## v0.1.6.0 (2026-09-05)

- Fixed the stutter while LazyCrafter-directed gathering: picking up items used to force a full catalog recompute that froze the game for about a fifth of a second every ~30 seconds - the crafting-log re-read (all 13,892 recipes) now happens once per login instead of on every inventory change (files: `Catalog/CatalogService.cs`, `Plugin.cs`)
- Inventory changes now refresh only item counts and the rows built from them - gather an item and its row still updates within a couple of seconds, without touching the crafting log at all (file: `Catalog/CatalogService.cs` `RefreshCountsAsync`)
- While a dispatch is running, the inventory watcher waits 10 seconds of quiet (instead of 2) before refreshing the catalog, so a gathering route no longer queues a refresh per node; it returns to 2 seconds when the run ends (file: `Adapters/AllaganInventory.cs`)
- Crafting a recipe for the first time still updates its crafting-log status immediately - the completed flag for just that recipe is re-read after each successful craft, so the Log Completion list needs no relog and no full refresh (file: `Adapters/DispatchService.cs`)
- The Refresh button in the window still does a full recount and re-read of everything, crafting log included - only the automatic background passes got cheaper (file: `UI/MainWindow.cs`)



## v0.1.5.1 (2026-09-05)

- Cart edits are now instant: adding an item, changing a quantity or clearing the cart updates the window immediately, even while the full catalog pass is still running - previously an edit stayed invisible until the whole 13,892-recipe pass finished, which after a craft run meant a couple of minutes of what looked like a frozen cart (file: `Catalog/CatalogService.cs`, function: `RepublishCart`)
- Dispatch now always plans against the cart as it is RIGHT NOW: pressing Dispatch (or hovering it for the preview, or `/lcraft plan` / `/lcraft fetch`) reads the live cart, so what runs is what you just typed, never a stale snapshot (files: `Catalog/CatalogService.cs` `LiveCart`, `Adapters/DispatchService.cs` `PlanFor`/`DispatchCart`)
- Finishing a craft run no longer kicks off a full catalog recompute: the refresh now arrives through the normal debounced inventory update a couple of seconds later instead of freezing the window behind a two-minute pass right after every run (file: `Adapters/DispatchService.cs`, function: `Finish`/`FinishBlocked`; the forced refresh is kept only when AllaganTools is absent)
- The dispatcher's internal bag-count refreshes no longer force a catalog recompute each time - thirteen per run used to each queue a full pass (files: `Adapters/AllaganInventory.cs` `DropMemo`, `Adapters/DispatchService.cs`)
- Internal: the cart rebuild reads the published snapshot's immutable row copy instead of the worker's live dictionary, the recipe-expansion memo is concurrency-safe, and the snapshot publish is under a lock - cart edits while a pass runs are now race-free by construction



## v0.1.5.0 (2026-09-05)

- Added the in-game "What's new" popup. After LazyCrafter updates, its changelog now opens once inside the game so you can see what changed without going to GitHub. It waits until you are logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/lcraft changelog` any time to reopen it.
- No change to crafting: the catalog, the cart, the Run tab from 0.1.4.2 and the Artisan / GatherBuddy / AutoRetainer hand-offs all behave exactly as before.

## v0.1.4.2 (2026-09-05)

### Added

- New **Run** tab in the LazyCrafter window: it becomes the first tab and opens automatically whenever a dispatch starts or stops on a blocker, with the phase (Retrieving / Gathering / Crafting / Blocked / Done / Failed) shown in the tab label, plus elapsed time, start time and the cart name in the header (file: `UI/RunTab.cs`, `UI/MainWindow.cs`)
- Run tab step list: every retrieve / gather / craft / venture / vendor / market step with item, quantity, state (pending / running / done / failed / blocked) and a plain-English **Reason** column ("needs market Titanium Ore x15", "GBR no progress 10 min", "Artisan did not start within 15 s"); the running row is highlighted and carries the external plugin's own status text (GBR status, "Artisan busy 1:12", "retainer session 0:40")
- Run tab Blocked section (only while a run is stopped on a blocker): the market items with estimated gil and an **Open market board** button, and the vendor items with a **Flag on map** button per NPC - the same lists the end-of-run chat block prints, now clickable
- Run tab buttons: **Stop**, **Resume** (enabled only when a stopped run can continue) and **Copy report**, which puts a plain-text dump of the whole run on the clipboard for pasting into a note
- `/lcraft status` prints that same report to chat, so you can check on a run without opening the window (file: `Plugin.cs` `PrintRunStatus`, renderer `Core/RunReport.cs`)
- Cart panel: while a run is active the one-line status keeps working and gains a **Run tab** button next to Stop (file: `UI/CartPanel.cs`)

### Notes

- The Run tab only reads the dispatcher's immutable `RunSnapshot` (from 0.1.4.0); nothing is computed during draw
- Offline proof: `tests/LazyCrafter.Probe` renders a synthetic Blocked run and checks the report names every blocked step with its reason, the market total and the vendor NPC location (`run-report probe: OK`)

## v0.1.4.1 (2026-09-05)

### Changed

- The post-craft price-match hand-off now targets Lazy Market Companion (DagobertPriceMatcher was retired 2026-09-05, card t_138ee175): the `Installed` check reads LMC's InternalName `LazyMarketCompanion` instead of Dagobert's, and the `Adapters/Dispatch/DagobertDispatch.cs` file is renamed `PriceMatchDispatch.cs` (property `Dispatch.Dagobert` -> `Dispatch.PriceMatch`)
- Config `DagobertAfterCraft` is renamed `PriceMatchAfterCraft` (Configuration v4 -> v5): existing saved configs keep the value - a Newtonsoft `[JsonProperty("DagobertAfterCraft")]` legacy shadow property reads the old key on load and `MigrateIfNeeded()` copies it across exactly once; the resave writes only the new key. `/lcraft debug` and the Settings tab rename with it; `/pricematch` still works (LMC answers it as a legacy alias)
- Settings tab copy names Lazy Market Companion and notes `/pricematch still works` (file: `UI/SettingsTab.cs`)

### Fixed

- The guard status line (`/lcraft guard`) and the dispatch debug line no longer report a "Dagobert" plugin that no longer exists (files: `Plugin.cs` `GuardCommand`/`LogDebugState`)

### Notes

- New regression proof `tests/LazyCrafter.ConfigMigrate` compiles the real `Configuration.cs` against stubs and asserts a v4 config (old key) round-trips through the rename: 16/16 cases, including a negative control proving the value would be silently lost without the shadow property
- No in-game behavior change beyond the names: the hand-off still only prints instructions (LMC has no IPC for its sell list)

## v0.1.4.0 (2026-09-05)

### Added

- Dispatch is now a LOOP of waves, not one pass: after every wave (retrieve, ventures, gather, crafts) the cart's remaining lines are re-assessed against the LIVE bags and re-planned, and while the fresh plan has work the plugin can do on its own, the next wave runs (files: `Core/DispatchLoop.cs` new, `Adapters/DispatchService.cs` functions `DispatchCart`/`Replan`/`WaveDone`/`TakeDecision`). This is the fix for the Alpine Chandelier run: the ore was gathered and one nugget crafted, then the run ended silently with four crafts never attempted, because deferrals were decided at plan-build time and never revisited
- New terminal phase `Blocked`, distinct from Done and Failed: when nothing is runnable the run stops and prints ONE red block at the END naming what to buy - market list with est. gil, vendor NPCs flagged on the map, manual sources, venture items still out - then "press Resume (or /lcraft resume) to continue the same cart" (file: `Adapters/DispatchService.cs`, function `FinishBlocked`/`PrintBlockedBlock`)
- `Resume()` + `/lcraft resume`: re-plans the same cart from the live bags and continues after the player has bought / fetched the blockers; with nothing runnable it prints the same blocked block again, never silence (file: `Adapters/DispatchService.cs`, function `Resume`). A manual Stop is not resumable
- GBR stall guard: if GBR's status text AND the gathered items' bag counts are unchanged for 10 minutes (while not merely waiting for a timed node window), GBR is stopped and the run goes Blocked with the reason - the gather wait previously looped forever with no timeout (file: `Adapters/DispatchService.cs`, phase `WaitGather`; decision logic `Core/StallGuard.cs` new)
- Per-craft 10-minute cap: an Artisan craft that never finishes gets a stop request and the run fails with the reason - the craft wait previously had no timeout (file: `Adapters/DispatchService.cs`, phase `WaitCraftEnd`)
- Heartbeat: while any wait is in flight, one chat line every 3 minutes ("still working: gathering 2/5 (Titanium Ore), 7:12 elapsed") so a long gather never looks dead; deduped, never spam (file: `Adapters/DispatchService.cs`, function `Heartbeat`)
- `RunSnapshot`: an immutable per-run picture (phase, elapsed, pass, step list with per-step state / reason / external status, blocked shopping list, CanResume) published as `Plugin.Dispatch.Snapshot` for the UI to read per draw without touching game state, plus `/lcraft status`-shaped `Report()` text (file: `Core/RunSnapshot.cs` new; contract v1 with the Run-tab card t_c360953f)

### Changed

- Cart runs are dispatched through the wave loop; the single-item entry points (craft / gather / retrieve one, `/lcraft fetch`) keep their one-wave behaviour (file: `Adapters/DispatchService.cs`, functions `CraftOne`/`GatherOne`/`RetrieveOne`/`RetrieveOnly`)
- A craft wave's completion line counts root cart lines ("2 cart lines finished, 5 crafts made"), not the one-pass "crafts finished 1/1" that read as done while four crafts were never attempted (file: `Adapters/DispatchService.cs`, function `Finish`)
- The GBR wait reports how many gathered items actually landed in the bags ("GBR auto-gather finished - 5 of 5 gathered items landed") instead of a bare "finished" (file: `Adapters/DispatchService.cs`, phase `WaitGather`)

### Fixed

- Zero-progress waves can no longer spin: a wave that changes nothing in the bags ends the run as Blocked with "no progress this pass" instead of re-planning forever (file: `Core/DispatchLoop.cs`, function `Advance`); belt-and-braces cap of 12 passes even with progress

### Notes

- Offline proof: `tests/LazyCrafter.Harness` suite `loop` drives the Alpine Chandelier shape end to end - sub-craft + market leaf -> pass 1 crafts the sub-craft, re-plan, Blocked naming the market item and quantity; fake-buy the item, Resume -> remaining crafts run, Done; a never-changing fake ends Blocked with "no progress" and the pass count asserted (no infinite loop); the stall guard fires on a signal that never changes. Suite `snapshot` asserts the `RunSnapshot.Report()` text names every blocked item.
- Why Core: the loop decision (re-plan, progress, blocked-why) and the snapshot record are pure, so the harness can prove them without Dalamud; `DispatchService` only executes.

## v0.1.3.1 (2026-09-05)

### Fixed

- GBR gather hand-off threw `ArgumentException: Object of type 'System.Object[]' cannot be converted to type 'System.Boolean'` on every dispatch that had a gather: `SetActiveArgs` already returns the parameter array and the call wrapped it in a second array (`Invoke(manager, [SetActiveArgs(m)])`), so `SetActiveItems(bool)` received one `object[]` (file: `Adapters/Dispatch/GbrDispatch.cs`, function: `Dispatch`, line 132 at 4ce33b0d4; shipped broken since P5 / 0.1.0.0, first exercised in-game 2026-09-04 on 0.1.2.0)
- A GBR hand-off that fails after `CreatePersistentGatherList` now deletes the half-created "LazyCrafter" list and saves before reporting, instead of leaving it for the next dispatch to clean up (file: `Adapters/Dispatch/GbrDispatch.cs`, function: `Dispatch`)
- Retainer stock that is a MARKET-BOARD LISTING (AllaganTools container 12002 `RetainerMarket`) was named "N on retainer X" and queued for a fetch Artisan then refused ("no retainer is holding any") - Artisan's retainer count reads bags 10000-10006 + crystals 12001 only, and a summoning bell cannot hand over a listing. `StoredWhere` now reports it as "N on the market board (listed by retainer X)" and the refusal line names listings as unreachable (files: `Adapters/AllaganInventory.cs` `StoredWhere`/`SplitRetainers`/`SplitListings`, `Adapters/InventorySource.cs` `RetainerMarket`, `Adapters/DispatchService.cs`). Catalog counts (Scope 0) are unchanged - listings still count as owned.

### Notes

- Why no probe caught the GBR bug: `tests/LazyCrafter.GuardProbe` proved members exist and never built an argument array. It now builds the real `SetActiveItems` arguments, asserts `args.Length == parameters.Length` and `args[0] is bool`, checks each argument is an instance of its parameter type (the check `MethodBase.Invoke` makes), and runs the 0.1.2.0 nested-array shape as a negative control that must be rejected.
- Evidence: omasky FFXIV chat log 00000007.log 2026-09-04 15:02 ET ("GBR gather hand-off refused: ArgumentException ..."); AllaganTools inventories.csv row for Star Quartz (36186): container 12002, retainer Bussyqueen (same character Grandpa Joe, world 95, known to Artisan's RetainerIDs), qty 1, listed at 788 gil.

## v0.1.3.0 (2026-09-04)

### Added

- Batch retainer fetch: when a dispatch needs retainer stock, ONE Artisan session (`RestockFromRetainers(NewCraftingList)`, decompiled + pinned) walks the retainers once for the whole cart instead of one bell cycle per material (file: `Adapters/Dispatch/RetainerFetch.cs`, function: `BeginBatch`; queue selection: `Core/RetainerBatch.Queue`)
- Fallback preserved: items with no recipe row, and any remainder after the batch pass, still go through the 0.1.2.0 per-item path (file: `Adapters/DispatchService.cs`, phases: `BatchRetrieve`/`BatchWait` before `Retrieve`)

### Fixed

- `RetrieveFromRetainers` setting was a no-op - a nested `if` in `WhyNoFetch` made the config check dead, so switching retrieval off still fetched; the toggle now gates every fetch path (file: `Adapters/DispatchService.cs`, function: `WhyNoFetch`)

### Notes

- The batch session is measured, not assumed: bag counts per demanded item are snapshotted at queue time and compared when Artisan goes idle, and anything still short stays in the per-item queue (trimmed to the remainder).
- Both overloads are proved offline by `tests/LazyCrafter.GuardProbe` (the list overload rides on the pin as an alias - `Adapters/ReflectionGuardExtensions.cs`) against the installed Artisan 4.0.5.19 (SHA-256 of the decompiled DLL matches omasky's installed copy).
- Queue decision tests: `tests/LazyCrafter.Harness/RetainerBatchQueueTests.cs` (deferred-because-of-retrieval crafts queue their rows; mixed-reason deferrals queue; non-retrieval deferrals stay out; unknown rows are dropped).
- ARC reflection pin ceiling raised 8.7 -> 8.8: omasky ships ARControl 8.7 and the exclusive ceiling flagged the installed build as unverified even though every pinned member resolves on it (GuardProbe against omasky's installed DLLs, 2026-09-04).

## v0.1.2.0 (2026-09-03)


### Added


### Changed


### Notes

- The guard-style refusal from 0.1.1.0 is untouched and still the safety net: a craft whose materials are not in the bags at the instant of hand-off is never sent to Artisan.
- Bullets in this entry are deliberately unwrapped single lines. `tools/Package-Plugin.ps1`'s CHANGELOG parser only keeps lines starting with `-`, `**` or a digit, so a hard-wrapped bullet loses everything after its first line in the user-facing `Changelog` field (visible in older entries above).

## v0.1.1.0 (2026-09-03)

Testing-channel fix build (production pointer stays 0.0.0.0). **"Owned" is not "in your bags."** Fixes the defect
a retainer, Artisan could not start, and LazyCrafter reported `1/1 craft finished` 1.25 s later. His verdict, verbatim:
*"needs to grab stock before attempting craft"*.

### Fixed
