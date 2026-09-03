# Changelog

## v0.1.0.0 (2026-09-03)

### Added
- Project scaffold (Phase 0 of `Projects/FFXIV LazyCrafter Plan`): `LazyCrafter.csproj`
  (Dalamud.NET.Sdk/15.0.0, net10.0, ECommons 3.2.0.9, LuminaSupplemental.Excel 4.3.0),
  manifest `LazyCrafter.json`, `Plugin.cs` (house pattern, command `/lcraft`, `/lcraft debug`),
  `Configuration.cs`, empty `UI/MainWindow.cs`.
- `Core/` — pure-logic layer with **zero Dalamud/Lumina references**: `Core/Interfaces.cs`
  (`IGameData`, `IInventory`, `IPriceSource` + the plain records they exchange),
  `Core/Model/*` (`SourceKind`, `EffortTier`, `PriceQuote`, `IngredientLeaf`, `RecipeNode`),
  `Core/CoreInfo.cs` (harness smoke hook).
- `tests/LazyCrafter.Harness/` — plain net10.0 console project that compiles `Core/**/*.cs`
  directly (no reference to the plugin project) and prints `OK`. Its existence is the
  proof that Core has no Dalamud dependency.

### Notes
- Not in `pluginmaster.json` yet; nothing is shipped. Release plumbing is Phase 7.
