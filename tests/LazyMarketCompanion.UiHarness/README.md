# LazyMarketCompanion real-source UI harness

Renders the real `ConfigWindow`, `AutoMarketPanelWindow`, `OwnerTooltip`, `InventoryTab` and `MarketChrome` sources (linked with
`<Compile Include>`, never copied) to PNG on the CPU. Only the game/runtime boundary is stubbed (`Stubs.cs`, `InventoryServiceStub.cs`,
`Fixtures.cs`). Rasterizer, PNG writer and game look come from `../HarnessShared/UiRender` (see its README for the modes and the
`DalamudLibPath` / `LALA_UI_ASSET_DIR` / `LALA_UI_SCALE` variables). No package reference, no network.

    dotnet run --project LazyMarketCompanion.UiHarness.csproj -c Release -- --game       # output/game/*.png
    dotnet run --project LazyMarketCompanion.UiHarness.csproj -c Release                 # built-in look, output/*.png (hermetic)

`--strict-glyphs` fails the run when drawn text has a character without a glyph in the game font. The run also fails if a draw
guard swallowed an exception, if `OverlayMirror` drifted from `MarketAutomation.cs`, or if the toggle/close-button interaction checks fail.

## Adding a scenario
Add a line to `Scenarios.Build`: `Config(name, w, h, new ConfigSetup { Page = ..., Cfg = ..., Inv = ..., Edit = c => ... })`,
`Panel(...)` or `Tooltip(...)`. Config/inventory state comes from `Fixtures` (`FirstRun`, `Typical`, `TypicalInventory`, `BlankInventory`).
Long pages are rendered in a tall canvas because the body scrolls inside a child window.

## Stubs that fake state
- `HarnessAddons.Open(name, x, y, w, h)`: declares an open game addon (inventory rectangle for panel docking, item detail for the tooltip).
- `InventoryService` (stand-in): canned facts; venture-loot buckets, gear plan and ownership still come from the real `Inventory/Core`.
- `Item` sheet and `ItemNameResolver`: the catalogue in `Fixtures.Items`.
- `OverlayMirror`: MarketAutomation cannot be compiled here, so its two overlay drawing methods are mirrored and checked against the source text.
- Not rendered: `AutoMarketMarkers` (coloured dots drawn into the game's bag grid via native addon nodes), item hover tooltips, the "Turn Auto-Market on?" modal, expanded gear-mover tree nodes.
