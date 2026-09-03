# Changelog

## v0.1.0.0 (2026-09-03)

Unreleased development version - not in `pluginmaster.json`; release plumbing is Phase 7.

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
