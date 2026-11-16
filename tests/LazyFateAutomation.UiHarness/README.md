# LazyFateAutomation real-source UI harness

Renders the real `FateToolKitWindow` (full and minimised form, every page, popups, tooltip) to PNG on the CPU without
FFXIV, a running Dalamud instance or a GPU. A visual capture harness, not an in-game behaviour test.

## What is real and what is stubbed

Compiled from the plugin source (linked, never copied): `FateToolKitWindow.cs`, `FateChrome.cs`, `MinimisableWindow.cs`,
`Configuration.cs`, `FateZoneLogic.cs` (the real FATE eligibility rule), `FateKinds.cs` and the shared `LalaUiKit`.
The shared render core comes from `../HarnessShared/UiRender`.

Stubbed (`Stubs.cs`, `Fixtures.cs`): the game and runtime boundary only. `PublicEvent` (FATE memory reads), `FateToolKit`
(run state, mode and zone rules mirrored from the real kit), `FateZones` and `FateGrindModes` (rows from fixtures), the
automation task, navmesh IPC, `Svc`/`Service`, the local player. Zone names, FATE names, grind-mode names and their
status text are real game and plugin data. The ECommons territory selector window opened by "Edit Selected Zones..." and
the server-bar entry are not drawn by this window and are not rendered.

## Run

Needs the existing Dalamud runtime folder (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`,
`FFXIVClientStructs.dll`, `Newtonsoft.Json.dll`, `cimgui.dll`) and the .NET 10 SDK. No package is restored.

```bat
set "DalamudLibPath=<existing-runtime-directory>"
set "LALA_UI_ASSET_DIR=<dalamud-UIRes-folder>"
set "LALA_UI_SCALE=1.02"
dotnet run --project LazyFateAutomation.UiHarness.csproj -c Release -- --game
```

| Switch | Effect |
| --- | --- |
| (none) | built-in mode: ImGui's 13 px font, hermetic, writes `output/*.png` (all scenarios except game-only ones) |
| `--game` | game look (Dalamud font, scale, style), writes `output/game/*.png`, runs the glyph check |
| `--only a,b` | render only the named scenarios |
| `--strict-glyphs` | fail when drawn text has a character the game font lacks |

## Scenarios

Defined in `Program.cs` (`Scenarios`). Each has a name, a one-line "shows", a size, a setup that sets the fake state and
the real configuration, and optional mouse steps. Sizes: 1120x760 is the window's first-use size, 672x760 is 60 percent
of the width (the window sets no minimum), 700x140 is the minimised size. To add one, add a `new Scenario(...)`:
`c.Page(...)` picks the page, `c.Config` is the real `Configuration`, `c.Tweak` the kit, `c.Run(...)` starts the fake
automation, `c.Minimise()` switches to the minimised form, `c.ZoneFilter(...)` types into the zone filter, and
`PublicEvent.Fates` (see `Populate`) sets the visible FATEs. Steps: `Move` hovers, `Click` presses and releases,
`Scroll` turns the wheel, all in window pixels (the window sits at 0,0). Click targets are positions read from a render;
re-check them after a layout change.
