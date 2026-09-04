# Changelog

## v0.1.0.0 (2026-09-03)

Unreleased development version - not in `pluginmaster.json`; release plumbing is Phase 7.

### Added - Phase 5: dispatch - Artisan IPC, GBR reflection, ARC reflection, Lifestream, ReflectionGuard (2026-09-03, t_85ac10ed)
- `Core/DispatchPlan.cs` - `Build(lines, totals, graph, ventures, retainers)` routes an assessed cart to the hand-off
  channels (pure Core): per missing item **gather > venture > sub-craft > gil vendor > market > manual**; crafts are
  emitted depth-first (intermediates before the recipe that consumes them), flagged `AfterGather` when a gather feeds
  the branch, and `Deferred` with the named blocker when a venture / purchase / manual item sits below them (Artisan
  would only fail on those). `RouteLeaf` answers the per-leaf fulfil buttons the same way. Harness suite
  `DispatchPlanTests` (12 cases); harness 94/94.
- `Adapters/ReflectionGuard.cs` - the version pin + loud failure behind every reflection hand-off (Scope §0). A `Pin`
  names the plugin, the `[MinVersion, MaxVerified)` range the member names were verified against, and the members;
  `Require` checks installed → loaded → version → every member resolves, reports the first failure as one
  `[LazyCrafter] ... hand-off refused: ...` chat line (+ log) and returns null - it never throws. Above `MaxVerified`
  it warns and still checks every member. `Verify` is static and Dalamud-free so `tests/LazyCrafter.GuardProbe` can
  run it against the installed DLLs. Session override for the acceptance test: `/lcraft guard <plugin> <minVersion>`
  (`/lcraft guard reset`, bare `/lcraft guard` prints both pins against what is installed).
- `Adapters/Dispatch/GbrDispatch.cs` - reflection: `GatherBuddy.Crafting.CraftingGatherBridge.CreatePersistentGatherList("LazyCrafter",
  dict)` (public static), then the list is found through the plugin's `AutoGatherListsManager` field → `Lists`, any
  previous "LazyCrafter" list deleted first (`DeleteList`) so quantities do not stack, `Enabled` set, `SetActiveItems()`
  + `Save()`, and auto-gather started with the public IPC `GatherBuddyReborn.SetAutoGatherEnabled(true)`; status from
  `IsAutoGatherEnabled` / `GetAutoGatherStatusText` / `IsAutoGatherWaiting`. 9 members pinned to GBR 7.5.0 source
  (4d16b9d), verified on installed 7.5.5.
- `Adapters/Dispatch/ArcDispatch.cs` - reflection into ARC's live object graph (it has no IPC and clobbers on-disk edits):
  plugin `_configuration.ItemLists` → find/create list "LazyCrafter" (`CollectOneTime`, `InOrder`), append or merge
  `QueuedItem {ItemId, RemainingQuantity}`, attach the list id to the current character's `ItemListIds` (Standalone)
  or its group's (PartOfCharacterGroup; NotManaged is refused with a hint), then persist via ARC's own
  `_pluginInterface.SavePluginConfig` **and** `_configWindow.ShouldSave()`. 25 members pinned to ARC 8.6 source
  (9964d7f) and re-read at tag 8.5 (identical), verified on installed 8.5.
- `Adapters/Dispatch/ArtisanDispatch.cs` - IPC `Artisan.CraftItem(ushort,int)`, `Artisan.IsBusy`, `Artisan.SetStopRequest` /
  `GetStopRequest` (a lingering external stop request is cleared before `CraftItem`).
- `Adapters/VendorLocator.cs` + `Adapters/Dispatch/LifestreamDispatch.cs` - gil vendor: item → `GilShopItem` shops →
  `ENpcBase` handlers (+ LuminaSupplemental `ENpcShop`) → placements from LuminaSupplemental `ENpcPlace` (map coords)
  with a `Level`-sheet (Type 8) fallback → the placement nearest a teleportable aetheryte (positions from the
  `MapMarker` sheet, DataType 3, converted with GBR's marker formula); `Plan` groups a shopping list by vendor
  (greedy, most items first). Hand-off: `Lifestream.IsBusy` → `Lifestream.Teleport(aetheryteId, 0)`, map flag +
  clickable `MapLinkPayload` and the list in chat. Market: `Lifestream.ExecuteCommand("mb")` + priced list.
  Dalamud-free so `tests/LazyCrafter.Probe` exercises it offline (299 placed shop NPCs, 62 aetheryte territories).
- `Adapters/Dispatch/DagobertDispatch.cs` - after a cart finishes and `Config.DagobertAfterCraft` is on: prints what was
  crafted and the `/pricematch` instructions (Dagobert has no sell-list IPC; never forced).
- `Adapters/DispatchService.cs` - runs a plan on `IFramework.Update` in small polled steps, **ARC → GBR → Artisan**:
  ventures first (asynchronous), then the gather list and a wait on `IsAutoGatherEnabled` going false, then each
  craft through `CraftItem` polling `IsBusy` between recipes (15 s start timeout, 2 min busy timeout); vendor / market
  / manual / deferred items are printed up front. `Stop` (button or `/lcraft stop`) turns GBR off and sends Artisan a
  stop request. Every line is `[LazyCrafter]`-prefixed. Per-leaf entry points: `CraftOne`, `GatherOne`, `VentureOne`,
  `VendorOne`, `MarketOne`.
- `Plugin.cs` - `Dispatch` service, `IGameGui`; `/lcraft plan` (what Dispatch would do), `/lcraft dispatch`,
  `/lcraft stop`, `/lcraft guard ...`; `/lcraft debug` logs the dispatch phase + which hand-off plugins are loaded.
- `UI/CartPanel.cs` - **Dispatch** is live (hover = the routed plan), becomes **Stop** + status while running;
  `UI/IngredientTree.cs` - the per-leaf Craft / Gather / Venture / Vendor / Buy buttons call the hand-offs (on the
  framework thread; Craft/Gather/Venture disabled while a dispatch runs); `UI/SettingsTab.cs` - guard status lines
  for GBR and ARC (installed version vs pin) and which IPC plugins are loaded.
- `tests/LazyCrafter.GuardProbe` - loads the installed `GatherBuddyReborn.dll` / `ARControl.dll` in an
  `AssemblyLoadContext` (plugin dir + Dalamud dev hooks) and runs `ReflectionGuard.Verify` on both pins; also proves
  the simulated version mismatch and a renamed member both come back as refusal text, not exceptions. Exit 0 = all
  34 members resolve on what is installed.

### Notes - Phase 5
- Not run in-game (ffxivdev may not launch the client). Reflection targets verified against the installed DLLs by
  `GuardProbe`; IPC names verified against upstream source (Artisan 4.0.5.19, Lifestream 2.5.4.16, GBR 7.5.0).
  V2 confirms each channel live and the `/lcraft guard GatherBuddyReborn 99.0` refusal in chat.
- VendorLocator coverage: LuminaSupplemental 4.3.0 `ENpcPlace` + the `Level` sheet place 299 of the 777 gil shops'
  NPCs; an unplaced vendor is reported as "no placed gil vendor" rather than guessed.
- Sub-craft routing when the branch needs an ARC venture: the craft is deferred (printed), not queued - retainers take
  hours, so re-dispatch the cart once the venture returns.

### Added - Phase 4: ImGui UI - bucket tabs, sortable catalog, ingredient tree, cart, settings (2026-09-03, t_49ca026f)
- `Core/Tiering.cs` - `AssessCart(lines, inv)` -> `CartAssessment {Tier, Lines, Totals, Missing}`: several recipes
  walked against **one** consumed-inventory ledger, so an on-hand unit is credited to at most one cart line; per-item
  totals sum need/have across lines and sub-craft levels. `IngredientLeaf` gained `Depth` (0 = top-level ingredient,
  +1 per chosen sub-craft); a sub-craft's leaves still precede the ingredient they serve in `Leaves`.
- `Core/IngredientTree.cs` - `Build(leaves)` rebuilds the nested tree from a walk's flat leaf list using `Depth`;
  `Flatten(roots)` yields parent-first for drawing.
- `Core/TeamcraftExport.cs` - `Link(lines)` = `https://ffxivteamcraft.com/import/` + base64 of
  `itemId,recipeId|null,quantity;...`. Format verified against TeamCraft's `pages/import/import.component.ts`
  (its test vector `MjA1NDUsbnVsbCwzOzE3OTYyLDMyMzA4LDE7MjAyNDcsbnVsbCwx` round-trips) and Artisan's `Teamcraft.cs`.
  Harness suite `CartTests` (9 cases) covers all three; harness now 82/82.
- `Catalog/CatalogRow.cs` - `CatalogRow` (one recipe as the UI sees it: name, job, level, job level, tier, HowMany,
  leaves, missing summary, NQ + HQ `ProfitEstimate`, marketable, CanBeHq, scrip/craft, desynth EV, log-complete,
  EXP/craft), `CartLine`, `CatalogSnapshot` (generation, rows, tier counts, jobs, cart, cart totals, flags).
- `Catalog/CatalogView.cs` - `CatalogTab` (Now / Easy / SomeEffort / RealEffort / Leveling / LogCompletion /
  Undersupplied), `SortKey` (13 columns + EXP / cost / listings), `ViewRequest` (tab, job, HQ-only, min velocity,
  hide untradeable, search, sort, leveling job, undersupplied thresholds, show-above-level), and the pure
  `ViewBuilder.Build` filter+sort. "Real effort" shows tier 3 **and** Blocked (Scope §3.2: they do not vanish);
  numeric sorts sink nulls regardless of direction; cash cost sorts only when every material was priced.
- `Catalog/CatalogBuilder.cs` - the row-building pass with no Dalamud types (Core + `LuminaGameData` only) so
  `tests/LazyCrafter.Probe` runs the exact worker pass offline; `AllItemIds()` = every ingredient any recipe can
  reach (13,758 ids), `DictInventory` = frozen counts for one pass.
- `Catalog/CatalogService.cs` - **all computation on one background worker, never in Draw** (Plan §Phase 4 task 6):
  waits on `Plugin.GameDataLoad`; per pass gathers the framework-thread reads in one `RunOnFrameworkThread` prologue
  (job levels, `IsRecipeComplete` per recipe, and the client-bag inventory fallback when AllaganTools is absent),
  snapshots AllaganTools counts for every ingredient id, tiers + prices all 13,892 recipes (~450 ms), builds the
  cart, and swaps an immutable `CatalogSnapshot` / `CatalogView` atomically. Pokes from the UI thread: `Invalidate`
  (inventory event, login, settings), `Request(ViewRequest)` (only a *changed* request wakes it), `Pin(recipeId)`,
  `RefreshPrices`, and the cart mutators (persisted to config). Price priming (`PrimeAndRefineAsync`) fetches only
  stale quotes for the top `PriceWindow` = 200 rows of the current view + their materials + the selected recipe +
  the cart (whole craftable set only on the Undersupplied tab), re-evaluates just the rows those quotes touch, and
  repeats at most `MaxPrimeRounds` = 3 times while the top of the view keeps changing; a 1-minute timer re-checks
  staleness so quotes older than `PriceCacheMinutes` refresh on their own.
- `UI/MainWindow.cs` - tab bar with count badges, filter bar (search, job combo - doubles as the job to level on
  the Leveling tab -, HQ, hide untradeable, min /day, above-level, Refresh, status line), catalog | ingredient tree
  split, cart panel at the bottom, Settings tab. Selecting a row pins it for pricing. `/lcraft` toggles the window
  (unchanged since Phase 0).
- `UI/CatalogTable.cs` - the 13 Plan §Phase 4 columns (item · job · lvl · craftable · margin cash · margin market ·
  /day · velocity · saturation · scrip · desynth · tier · missing) plus a per-tab extra (EXP / cost / listings);
  every column sortable (header click -> `SortKey` -> worker sorts; the table never sorts on the draw thread);
  `ImGuiListClipper` so a 6,000-row bucket draws one screen of widgets; right-click: add 1 / add all craftable /
  copy name; `<` prefix on costs that are lower bounds. Per-tab table ids keep per-tab sort/widths.
- `UI/IngredientTree.cs` - header (tier, can-craft, revenue/tax/cash cost/market cost, margins, /day, velocity,
  listings, unpriced list, scrip), quantity + **Add to cart** + **Copy TeamCraft link**, then the tree table:
  per leaf have/need (green when covered), source kinds, unit price (cheapest of market / gil vendor), and one
  fulfil button per channel (Craft / Gather / Venture / Vendor / Buy) - **disabled placeholders that name the
  Phase 5 hand-off** they will trigger.
- `UI/CartPanel.cs` - collapsible; lines (recipe, editable crafts, tier, cash cost, remove), aggregated missing
  list with source + estimated cost, **Dispatch** (disabled until Phase 5), **Export to TeamCraft** (final items,
  quantity x ResultAmount, link to clipboard + chat line), Clear.
- `UI/SettingsTab.cs` - the 7 inventory-source toggles (FC chest off), revenue basis, price by world, refresh
  interval, show-above-level, undersupplied thresholds, retainer status, and the two dispatch toggles
  **Dagobert list-after-craft** and **vnavmesh walk-to-vendor** - both exist and both default **OFF**. Every change
  saves and invalidates the catalog.
- `Configuration.cs` v3 - `RevenueBasis`, `ShowAboveLevel` (false), `UndersuppliedMinVelocity` (3) /
  `UndersuppliedMaxListings` (2), `DagobertAfterCraft` (false), `VnavWalkToVendor` (false), `Cart`. v2 -> v3 needs
  no rewrite.
- `Adapters/LuminaGameData.cs` - `CanBeHq(itemId)` (`Item.CanBeHq`; the HQ price row is only evaluated for these)
  and `JobAbbr(classJobId)` (`ClassJob.Abbreviation`). `Adapters/AllaganInventory.cs` - `Snapshot(ids)`.
- `Plugin.cs` - owns the `CatalogService`; `OnInventoryChanged`, `OnLogin`, the Universalis warm-up and
  `SaveConfig` all `Invalidate()`; `/lcraft debug` also logs the catalog generation/status/tier counts/view.
- `tests/LazyCrafter.Probe` - now also runs the Phase 4 pass offline: `CatalogBuilder` over all 13,892 recipes
  with a seeded fake inventory + every crafter at 100 (446 ms; Now 9 / Easy 6,014 / SomeEffort 4,883 / Blocked
  2,986), `ViewBuilder` for every tab and every sort key, HQ-only, search, job filter, a live Universalis prime of
  the Now window (13 quotes, 4 requests) with the touched-row re-evaluation (4,526 rows), a two-line cart, the
  TeamCraft link, and an ingredient tree.

### Notes - Phase 4
- **Not exercised in-game** (ffxivdev may not launch the client): the ImGui widgets compiled against
  `Dalamud.Bindings.ImGui` (SDK 15) but their behaviour is verified only by the offline probe of the layer beneath
  them. P4's acceptance screenshot ("window populated with real data on Joey's install") is the V2 verify card's.
- Thread model: the draw thread only reads `volatile` snapshot references and calls the poke methods; the worker
  never touches ImGui; `RecipeGraph` / `Tiering` / `ProfitModel` are worker-private. Game reads happen in the
  prologue hop. If the AllaganTools IPC ever proves unsafe off-thread, move `Inventory.Snapshot` into the prologue.
- Fulfil / Dispatch buttons are intentionally disabled placeholders; Phase 5 replaces them with the IPC /
  reflection hand-offs. The Dagobert and vnavmesh toggles are stored but read by nothing yet.

### Added - Phase 3: adapters - game data, inventory, prices, player (2026-09-03)
- `Adapters/LuminaGameData.cs` - `IGameData` over the Excel sheets, indexed once in `Load(GameData, log, gbr?)`
  (~400 ms off-thread) and answered from dictionaries. Recipes from `Recipe` (+`RecipeLevelTable.ClassJobLevel`,
  job = `CraftType + 8`), gil vendors from `GilShopItem` subrows (+`Item.PriceMid`), special shops from
  `SpecialShop.Item[].ReceiveItems`, gatherables from `GatheringItem` -> `GatheringPointBase` -> `GatheringPoint` ->
  `GatheringPointTransient` with GBR's node-type rule (rare-pop table -> Unspoiled, ephemeral window -> Ephemeral,
  `GatheringPoint.Type == 8` -> Clouded; regular wins when an item sits on both), fish from `FishParameter` +
  `SpearfishingItem`, ventures from `RetainerTask` (non-random) joined to `RetainerTaskNormal.Quantity[5]` and the
  matching `RetainerTaskParameter` threshold array (PerceptionDoL / PerceptionFSH / ItemLevelDoW by job category),
  collectables from `CollectablesShopItem` subrows joined to `CollectablesShopRefine` + `CollectablesShopRewardScrip`
  (legacy rows pointing at row 0 skipped), marketable = `ItemSearchCategory > 0 && !IsUntradable` until Universalis'
  list overrides it (`UseMarketableOverride`). Drops and desynth from LuminaSupplemental 4.3.0 embedded CSVs
  (`MobDrop`, `DungeonDrop`, `DungeonChestItem`, `DungeonBossDrop`, `SubmarineDrop`, `AirshipDrop`; `ItemSupplement`
  rows with source `Desynth`, `Probability` % and `Min/Max` -> `DesynthResult`), plus combat-venture items as a drop
  fallback. Live counts on the installed client: 13,892 recipes, 6,743 gil-vendor, 12,886 special-shop, 1,050
  gatherable, 2,758 fish, 968 ventures, 16,843 marketable, 7,843 drop, 1,508 collectable, 21,997 desynth sources.
- `Adapters/GbrData.cs` - reflection reader for a loaded GatherBuddyReborn (`GatherBuddy.GameData.Gatherables`
  -> `NodeType` / `Level` / `GatheringType`), used as an overlay on the sheet node types when GBR is present; any
  shape mismatch logs once and falls back to the sheets.
- `Adapters/InventorySource.cs` + `Adapters/AllaganInventory.cs` - `IInventory` over the AllaganTools IPC
  (`ItemCountOwned(itemId, currentCharOnly, inventoryTypes[])`, `IsInitialized`, `GetCharactersOwnedByActive`,
  `ItemAdded` / `ItemRemoved` / `Initialized` events). Seven per-source toggles - Bags, ArmouryChest, Saddlebag,
  Retainers, AltCharacters, FCChest, GlamourDresser - each mapped to CriticalCommonLib `InventoryType` ids;
  **all on by default except FCChest**. Counts are memoised per item until an inventory event (2 s debounce ->
  `Changed`). Without AllaganTools: `InventoryManager.GetInventoryItemCount` (NQ+HQ, current character's bags)
  and `Degraded = true` for the UI banner.
- `Adapters/UniversalisClient.cs` - `IPriceSource` grown from Dagobert's client: batched
  `GET aggregated/{scope}/{<=100 ids}` (min/median listing, average sale, daily velocity; NQ and HQ; DC or world
  block) plus a field-projected `GET {scope}/{ids}?fields=items.listingsCount,...` per batch for listing counts;
  `marketable` and `tax-rates?world=` cached per session; shared gzip `HttpClient` with
  `User-Agent: LazyCrafter/{ver} (lalalazy; github.com/dajoey/lalalazy)`; in-memory + `{configDir}/prices.json`
  cache with a 10-minute TTL (atomic write, at most once a minute); semaphore of 4; exponential backoff on
  429 / 5xx honouring `Retry-After`. `Get` is cache-only; `PrimeAsync(ids)` fetches only the missing/stale
  marketable subset the caller passes (visible rows + cart), never the whole list. Missing velocity -> `0`,
  never NaN (V1 contract). Live smoke: 152 quotes in 8 requests, 0 failures, re-prime within TTL = 0 fetches,
  disk cache reload restores scope + quotes.
- `Adapters/PlayerState.cs` - `ICraftingLog` (`QuestManager.IsRecipeComplete`), crafter/gatherer levels via
  `IPlayerState.GetClassJobLevel` (API 15), home world / data-centre name via `IPlayerState.HomeWorld`, and
  retainer stats read **read-only** from `pluginConfigs/ARControl.json` (`Characters[].Retainers[]` where
  `LocalContentId` matches, `Managed != false`) plus that character's `GatheredItems` for the venture log gate;
  re-read on mtime change (checked every 30 s). `RetainerHint` explains an empty list.
- `Configuration.cs` v2 - `EnabledSources` keyed by `InventorySource` name (migration fills missing keys),
  `PriceCacheMinutes` (10), `PriceByWorld` (false).
- `Plugin.cs` - wires the adapters; game data loads on a background `Task`; on login the price scope is set to the
  home DC (or world) and the marketable + tax caches warm; `/lcraft debug` logs recipe count, inventory source
  states, price cache size, DC, retainer count (Phase 3 acceptance); `/lcraft prices` primes a 100-item sample.
- `tests/LazyCrafter.Probe` - offline console that opens the installed client's sqpack with a bare Lumina
  `GameData` (no Dalamud), runs `LuminaGameData.Load` + the Core against it (tiering over all 13,892 recipes),
  and smoke-tests `UniversalisClient` against the live API including the disk-cache round trip. Run:
  `dotnet build tests\LazyCrafter.Probe -c Release` then `dotnet tests\LazyCrafter.Probe\bin\Release\net10.0-windows7.0\LazyCrafter.Probe.dll`.

### Notes - Phase 3
- `LuminaGameData` / `UniversalisClient` take a bare `Lumina.GameData` / `Action<string>` logger rather than
  `IDataManager` / `IPluginLog` so the probe can exercise them without the client; the plugin passes
  `Data.GameData` and a lambda onto `IPluginLog`.
- 244 distinct ingredients classify as `Unknown` with the current lookups (Cosmic Container, the airship/submarine
  "Component Materials", etc.) - those recipes land in `Blocked` (2,986 of 13,892 with an empty inventory). Fine
  for v1; a `Company Workshop` / cosmic-exploration source is a follow-up if Joey wants them.
- Not in `pluginmaster.json`; nothing is shipped. Release plumbing is Phase 7.

### Fixed - V1 verify follow-up (2026-09-03, t_003d108b)
- `Core/ProfitModel.cs` `UnitCost`: a material listed on the board **only as HQ** (all NQ price columns null,
  e.g. crafted intermediates, fish) was returned as unpriced -> `UnpricedItems`, `CostComplete=false`, and
  `PerDay` silently stock-capped instead of velocity-capped. The market price now falls back to the HQ columns
  (`MinListingHq ?? AvgSaleHq ?? MedianHq`) when no NQ price exists - an HQ unit satisfies an NQ ingredient slot.
  Decision: the fallback applies ONLY when NQ is absent; an existing NQ price wins even if HQ is cheaper.
- `Core/ProfitModel.cs` `Evaluate`: a `NaN` (or infinite) velocity from a broken quote passed
  `Math.Max(0, v)` / `v > 0` (both false for NaN) and made `PerDay = NaN`, leaving `Rank` order undefined.
  New `ProfitModel.SaneVelocity` (finite and > 0, else 0) is applied before use and is what
  `ProfitEstimate.Velocity` now reports. `Core/UndersuppliedFinder.cs` uses it too (NaN previously slipped past
  the `velocity < MinVelocity` gate).
- `Core/Model/PriceQuote.cs`: invariant doc-note for the Phase 3 UniversalisClient - velocities finite and >= 0
  (missing `dailySaleVelocity` -> 0, never NaN); price columns null (never 0) when a quality has no listing.
- Harness: the two V1 probes are now permanent `AdversarialTests` cases (RED 71/73 against 2d9e7428a, GREEN 73/73).

### Added - Phase 2: Core profit model + scrip / desynth / leveling / undersupplied / log completion (2026-09-03)
- `Core/ProfitModel.cs` + `Core/Model/ProfitEstimate.cs` - `Evaluate(recipeId, inv, prices, taxPct, hq, crafts)`
  -> `ProfitEstimate`. Two cost columns always: **cash** (only missing units priced; on-hand stock consumed
  once, as `Tiering` does) and **market** (every unit at market). A craftable intermediate costs the cheaper
  of buying and crafting it; a material's unit cost is the cheaper of its market min and gil-vendor price
  (`UnitCost`). Revenue at the selectable `RevenueBasis` (MinListing default / MedianListing / AvgSale) for
  NQ or HQ, minus MB tax. `PerDay` = cash margin per unit x min(supply, velocity) where supply is unbounded
  when every material is purchasable, else `HowMany`; `SaturationDays` = listings / velocity. Materials with
  no price are listed in `UnpricedItems` (cost = lower bound). `ProfitModel.Rank` is the default sort
  (PerDay desc, MarginCash desc, id).
- `Core/ScripValue.cs` - `Evaluate(itemId, jobLevel)` -> `ScripEstimate` (scrip per collectability tier,
  `ScripPerCraft` at max tier, level-band flag); `ForCollectability(itemId, value)`.
- `Core/DesynthValue.cs` - `Evaluate(itemId, prices)` -> `DesynthEstimate` (sum of chance x qty x market min
  over marketable outcomes; unpriced outcomes listed; `IsEstimate` always true); `DesynthPremium` = EV - sell price.
- `Core/LevelingScore.cs` - `ExpPerCraft(jobLevel, recipeLevel)` = floor(floor(base[rlvl] / 3) x mod[diff] / 100)
  with the level-difference modifier (0..21) and first-craft EXP (levels 1..100) LUTs embedded (community
  formula, r/ffxiv 2022-08). `Evaluate` gates on tier <= 1; `Rank(job, level, inv)` best EXP first.
- `Core/UndersuppliedFinder.cs` - `Find(candidates, prices)` / `FindCraftable(prices)`: marketable items with
  NQ+HQ velocity >= `MinVelocity` (3) and listings <= `MaxListings` (2), intersected with the craftable set.
- `Core/CraftingLogFilter.cs` + `ICraftingLog` - `NotYetCrafted(recipeId)`, `Predicate`, `Remaining(job, maxLevel,
  cost)` cheapest-first, `Progress(job)`.
- `Core/Interfaces.cs` - `IGameData.Collectable(itemId)` -> `CollectableInfo` (CollectablesShopItem +
  Refine + RewardScrip) and `IGameData.Desynth(itemId)` -> `DesynthResult[]` (Phase 3 fills these from
  Lumina / LuminaSupplemental).
- Harness: `ProfitModelTests.cs`, `ScripDesynthTests.cs`, `ExtrasTests.cs`; `FakeGameData.Collectable()` /
  `.Desynth()`. 65/65 PASS incl. the acceptance case "40k margin / 0.1 velocity ranks BELOW 2k / 30".

### Added - Phase 1: Core recipe graph, source classifier, tiering, venture resolver (2026-09-03)
- `Core/RecipeGraph.cs` - `Expand(recipeId)` builds the `RecipeNode` tree (sub-recipe on the
  parent's job preferred, else lowest id; cycle guard cuts the back edge; memoized per graph),
  `HowMany(recipeId, inv)` / `CanCraft` with Artisan `NumberCraftable` semantics
  (min over ingredients of `(have + craftable-sub) / amount`, x `ResultAmount`).
- `Core/VentureResolver.cs` - `Resolve` / `ResolveAll` / `ResolveBest(itemId, retainers)`
  re-derived from the public `RetainerTask` / `RetainerTaskNormal` / `RetainerTaskParameter`
  sheet semantics (no ARC code): level gate, `ClassJobCategory` 17/18/19 -> MIN/BTN/FSH else
  combat, `Gathering >= RequiredGathering`, `ItemLevel >= RequiredItemLevel`, perception (DoL/FSH)
  or ilvl (DoW) thresholds -> reward tier -> `Quantity[tier]`, optional gathered-log gate.
  Models `Core/Model/RetainerStats.cs` (`RetainerStats`, `VentureMatch`).
- `Core/SourceClassifier.cs` - `Classify(itemId, need, have)` -> `SourceKind[]`: `OnHand` is
  exclusive; otherwise `SubCraft`, `GilVendor`, `SpecialShop`, `RegularNode` | `TimedNode`
  (timed or any non-Regular node type), `Fish`, `Venture` (only when a supplied retainer
  qualifies), `Market`, `Drop`; `Unknown` when nothing matches.
- `Core/Tiering.cs` - `Assess(recipeId, inv, crafts)` -> `RecipeAssessment { Tier, HowMany, Leaves }`.
  Leaf tier = cheapest of its sources (OnHand 0; SubCraft/GilVendor/RegularNode/Venture 1;
  TimedNode/Fish/Market/SpecialShop 2; Drop 3; Unknown Blocked); a `SubCraft` leaf inherits
  its sub-tree's tier; recipe tier = max over top-level ingredients. On-hand stock is consumed
  once as the tree is walked, never credited to two leaves.
- `Core/Interfaces.cs` - `VentureRow.RewardThresholds` (the four `RetainerTaskParameter`
  breakpoints) and `IGameData.IsDrop(itemId)`.
- `tests/LazyCrafter.Harness` - `Fakes.cs` (`FakeGameData`, `FakeInventory`, `FakePrices`),
  `World` fixture, one suite file per Core class; runner prints `PASS/FAIL [suite] name`.
  34/34 PASS incl. "all on hand -> tier 0, HowMany = N" and "missing timed-node mat -> tier 2".

### Added - Phase 0: scaffold (2026-09-03)
- Project scaffold (Phase 0 of `Projects/FFXIV LazyCrafter Plan`): `LazyCrafter.csproj`
  (Dalamud.NET.Sdk/15.0.0, net10.0, ECommons 3.2.0.9, LuminaSupplemental.Excel 4.3.0),
  manifest `LazyCrafter.json`, `Plugin.cs` (house pattern, command `/lcraft`, `/lcraft debug`),
  `Configuration.cs`, empty `UI/MainWindow.cs`.
- `Core/Interfaces.cs` (`IGameData`, `IInventory`, `IPriceSource` + the plain records they exchange),
  `Core/Model/*` (`SourceKind`, `EffortTier`, `PriceQuote`, `IngredientLeaf`, `RecipeNode`),
  `Core/CoreInfo.cs` (harness smoke hook).
- `tests/LazyCrafter.Harness` - plain net10.0 console that compiles `src/LazyCrafter/Core/**`
  directly (no reference to the plugin project) and prints `OK`. Its existence is the
  proof that Core has no Dalamud dependency.

### Notes
- Not in `pluginmaster.json` yet; nothing is shipped. Release plumbing is Phase 7.

