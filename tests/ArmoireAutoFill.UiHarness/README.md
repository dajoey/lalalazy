# ArmoireAutoFill real-source UI harness

Renders the real `MainWindow.Draw` (Collection, Shopping and Auto-store pages) and `ConfigWindow.Draw` (Settings) to PNG
without FFXIV, a running Dalamud instance, a GPU, or a visible window. It is a visual capture harness, not a
replacement UI and not an in-game behaviour test. Rasterizer, PNG writer and game look come from the shared render core
in `../HarnessShared/UiRender` (see its README).

## Run

Needs an existing Dalamud runtime directory (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `Lumina.dll`,
`Lumina.Excel.dll`, native `cimgui.dll`); nothing is downloaded or restored and there is no package reference.

```bat
set "DalamudLibPath=<existing-runtime-directory>"
set "LALA_UI_ASSET_DIR=<dalamud-UIRes-folder>"
set "LALA_UI_SCALE=1.02"
dotnet run --project ArmoireAutoFill.UiHarness.csproj -c Release -- --game
```

Without `--game` the harness is hermetic (ImGui's built-in 13 px font and style, no host file read) and writes
`output/*.png`; with it, `output/game/*.png` use the Dalamud default font, global scale and "Dalamud Standard" style.
`--strict-glyphs` turns a missing-glyph report into a failure. `run.cmd` is the same without the game switch. `output/`
is ignored by git.

## What is real and what is stubbed

Compiled from the real sources (never copies): `src/ArmoireAutoFill/Windows/*.cs`, `Models/*.cs`, `Configuration.cs`,
`Data/KnightshopperCatalogCore.cs`, `Data/Shopping/*.cs` (the pure shopping-list builder, share encoder and
instruction text) and the shared `src/Shared/LalaUiKit`. The shopping list a page shows is the real
`ShoppingListBuilder.Build` output.

Stubbed in `Stubs.cs` (the game and runtime boundary only): `InventoryScanner`, `CabinetObserver`, `ArmoireAutoStore`,
`ArmoireGearDatabase`, `KnightshopperCatalogBuilder`, `ArmoireShoppingInput` (same public surface as the real classes,
settable state), the `Plugin` entry point (configuration plus a no-op plugin interface so `Configuration.Save()` does
nothing), and the ECommons service locator (only the Knightshopper IPC probe). Telemetry and the changelog popup are not
compiled.

`Fixtures/armoire-real-data.json` holds real game data read offline from a game install with the plugin's own selection
rules: the 3844 armoire item names, the 14 dungeons the plugin lists with their real pieces and slots, 537 shop rows
for armoire pieces with real vendor names and prices, and the real skip counts. Ownership, scan age, the player's quest
and achievement progress and the clipboard are synthesized by `Sim.cs` from item ids, so every run draws the same pixels.
Times are stubbed as ages (`ScanAge`), so "6m ago" is stable.

## Window sizes

The windows' own sizes decide what a first-time player sees:

- `MainWindow` sets no size, so ImGui auto-fits it inside `SizeConstraints` (minimum 520x380). The harness measures
  that with the same flags and constraints (`MeasureAutoFit`): 520x388, which is also the narrowest allowed size.
- `ConfigWindow`: 600x360 first use (`FirstUseEver`), minimum 400x300.

The first-use renders therefore show a cramped window whose body is about 40 px tall. To see the compact layouts in
full the set also has narrowest-width renders with a tall canvas (520 wide), and resized widths that switch the wide
layouts on (content widths 720, 780 and 1000 px).

## Scenarios (game mode, 17 images by default; the scale-1.5 image is evidence only; `--extra` adds three simple state variants: all dungeons complete, shopping scan needed, nothing missing)

See the table printed by the run. Names are `<surface>-<size or state>`; `Program.cs` holds the list, `Sim.cs` the
state presets (`FirstRun`, `Typical`, `WaitingForCabinet`, `AllComplete`, `GearDatabaseMissing`, `CatalogBuilt`,
`CatalogBuilding`, `CatalogSheetsUnavailable`, `CatalogBuildFailed`, `NothingMissing`, `StoreRunning`,
`StoreFinished`).

### Adding a scenario

Add a `new Scenario(name, surface, size, setup)` line. `size` is `FirstUse`, `Narrowest` or `Fixed` (set `Width` and
`Height`; tall canvases show body content that scrolls out of view at small sizes). `setup` runs after `Sim.Reset()`:
call a `Sim` preset, then change `Plugin.Configuration` or the `Sim.*` stubs. To press things, set `Click` to ordinals
of the interactive items in the left column, top to bottom, found by sweeping the mouse; the run prints the list it
found (`clickable #n at y ... (of N: ...)`). On the Collection page ordinal 0 is the table header, so dungeon rows start
at 1; on Shopping the "Copy import code" buttons and "Pieces" tree nodes are in the list (opening a table adds its
header, which shifts the later ordinals). `HoverFirstPiece` hovers the first piece line under the first pressed row and
finds the tooltip by looking for the spot where a tooltip window appears. `ImportStatus` sets the text the real "Copy
import code" button shows.

Interaction checks at the end of every run: a real press and release on the Settings toggle flips
`Configuration.ScanOnLoad`, and a real click on each window's close button closes that window.

## Glyph audit

Game mode checks that every non-ASCII character in the string literals of the compiled UI sources, the shared kit,
the shopping instruction text and the fixture names has a glyph in the game font, and prints the result.

## Fidelity limits

Same as the shared core (see its README): CPU rasterizer blending, no game-symbol glyphs, no player font or style
customisation, no Dalamud window chrome. Real-game differences in saved window positions and sizes are not modelled.
