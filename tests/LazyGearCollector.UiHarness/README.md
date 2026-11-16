# LazyGearCollector real-source UI harness

Renders the real `CollectorWindow.Draw` (with `CollectorChrome`, `Planner`, `Configuration`, the models and the shared UI kit)
to PNG. Only the plugin services, the inventory scanner and the shop sheet are stubbed (`Stubs.cs`); sample data
(`Data.cs`) is a fixed Phantom Vision style collection.

Game mode (`--game`, needs `DalamudLibPath`, `LALA_UI_ASSET_DIR`, optional `LALA_UI_SCALE`) writes `output/game/*.png` and
prints a glyph audit (`--strict-glyphs` fails on a miss). Without `--game` it renders hermetically to `output/`.
Both modes end by pressing the real drawn target-tier buttons and cached toggle to prove they still change the configuration.

Add a scenario: one `new Scenario(name, width, height, ctx => ...)` line in `BuildScenarios`. A scenario can call
`Typical()`, `Containers()`, `OwnAll(tier)`, `Own(role, slot, tier)`, `Wallet(...)`, `Page(group, overviewPage, role, search)`,
change `Plugin.Config`, set `Plugin.ClientState.IsLoggedIn`, clear `Plugin.Collections`; optional `Mouse` (hover), `Wheel` (scroll the body).
