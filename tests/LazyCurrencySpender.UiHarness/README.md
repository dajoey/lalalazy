# LazyCurrencySpender real-source UI harness

Renders the real window code (main, settings, spending guide, configuration wizard, debug window and the Currency
overlay) to PNG on the CPU, in the "game look" of the shared render core (`../HarnessShared/UiRender`): Dalamud's
default font at 17 px, global scale 1.02, "Dalamud Standard" style.

Compiled unchanged: every file in `src/LazyCurrencySpender/Windows`, `Global.cs`, `Config.cs`, `UIHelper.cs`,
`StringHelper.cs`, `ShopHelper.cs`, `MissingLocationLog.cs` and the shared `LalaUiKit`. Stubbed (`Stubs.cs`): the
plugin entry class, services, game data sheets, textures, unmanaged addon/agent structs, shop/currency data classes
and the game-reading helpers. Fixture data lives in `Fixtures.cs`; scenarios in `Program.cs` (`BuildScenarios`).

Run on a machine with the Dalamud runtime (`DalamudLibPath`, `LALA_UI_ASSET_DIR`, `LALA_UI_SCALE=1.02`):

    dotnet run --project tests/LazyCurrencySpender.UiHarness -c Release -- --game

Output: `output/game/<scenario>-<W>x<H>.png`. Without `--game` it prints a note and renders nothing. A glyph audit
runs first (`--strict-glyphs` fails on a missing glyph).

Add a scenario: add a `new Scenario(name, Kind, width, height, () => { ...state setup... })` line; setup runs after
a fresh fixture state (`Fresh()`), so mutate `C` (config), `P` (plugin stub), `PlayerHelper`, `Generator.items`, set a
window section with `SetSection`, or a static with `SetStatic`.
