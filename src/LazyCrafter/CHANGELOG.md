# Changelog

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
