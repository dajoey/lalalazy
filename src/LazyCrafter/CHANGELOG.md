# Changelog

## v0.1.0.0 (2026-09-03)

Unreleased development version - not in `pluginmaster.json`; release plumbing is Phase 7.

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
