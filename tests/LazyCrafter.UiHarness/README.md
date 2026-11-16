# LazyCrafter real-source UI harness

Renders the real `MainWindow.Draw` (top bar, bucket row, page header, footer, and the Catalog, Cart, Run and Settings pages with their panels) to PNG without FFXIV, a running Dalamud instance, a GPU or a visible window. It is a visual capture harness, not an in-game behavior test.

## What is real and what is stubbed

Real, linked with `<Compile Include>` (never copied): `src/LazyCrafter/UI/*.cs` (window, chrome, run tab, settings tab, cart panel, catalog table, ingredient tree), `src/Shared/LalaUiKit`, `src/LazyCrafter/Core/**`, the catalog row/view records, `Configuration.cs` and the inventory source list.

Stubbed (`Stubs.cs`, plain settable state, no behavior): the plugin entry point (`Plugin`), the catalog worker (`CatalogService`), the dispatcher and its hand-offs (`DispatchService`, Artisan/Lifestream/price-match, reflection pins), game data (`LuminaGameData`), the price client, the inventory bridge and player state. No `PackageReference`, no network.

`Fixtures.cs` builds the catalog rows, ingredient trees, cart and run snapshots; `Program.cs` holds the scenario list and the renderer. Item, job and retainer-style names are in-game names of realistic length; numbers are plausible, fixed values.

## Run

Needs the same environment as the other UI harnesses (`DalamudLibPath`, and for game mode `LALA_UI_ASSET_DIR`, optional `LALA_UI_SCALE`; see `../HarnessShared/UiRender/README.md`).

```bat
dotnet run --project LazyCrafter.UiHarness.csproj -c Release -- --game
```

Game mode writes `output/game/*.png` (21 images) and prints a glyph audit; `--strict-glyphs` turns a missing glyph into a failure. Without `--game` the harness stays hermetic (ImGui built-in font) and writes the 15 default-size scenarios to `output/`; in that mode two byte-identical images fail the run.

## Adding a scenario

Add a `yield return new("name", width, height, x => { ... })` to `BuildScenarios()` in `Program.cs`. `x.P` is the stub plugin, `x.C` the catalogue, `x.W` the real window. Start with `Logged(x)`, then:

- `x.SelectTab(CatalogTab.X)` and `x.Section("Cart" | "Run" | "Settings")` choose the page; `x.Select(row)` selects a recipe; `x.Field("_search", ...)` sets any other private view field of `MainWindow` by reflection.
- `Publish(x, tab, rows, undersuppliedOrNull, withCart:, degraded:)` publishes the snapshot and the view the worker would have produced.
- Run state: set `x.P.Dispatch.Snapshot` (see `Fixtures.RunRunning/RunBlocked/RunFailed/RunDone`), `x.P.Dispatch.Current` and `.Status`.
- Mark a variant `GameOnly = true` to keep it out of the hermetic set; `Scale = 1.5f` renders it at another global scale.

Hover tooltips, open combos and context menus are not captured (the mouse sits off screen). `run-running` is the only clock-dependent image: its elapsed and started text follow the real clock.
