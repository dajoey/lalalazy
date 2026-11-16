# LazySkywardTracker UI harness

Console harness for the tracker window. It compiles the real `TrackerWindow.cs`, `SkywardChrome.cs` and the shared
`LalaUiKit` sources (linked, not copied) and stubs only the game boundary in `Stubs.cs`: the plugin entry point
(achievement table, progress cache, completed set, refresh request), the inventory scan, the logged-in flag and the
Dalamud icon font. The shared render core in `../HarnessShared/UiRender` rasterizes ImGui draw data to PNG on the CPU.

```
dotnet run --project tests/LazySkywardTracker.UiHarness -c Release                 # checks + hermetic built-in render to output/
dotnet run --project tests/LazySkywardTracker.UiHarness -c Release -- --game       # game-look render to output/game/ + glyph audit
```

Game mode needs `DalamudLibPath`, `LALA_UI_ASSET_DIR` and optionally `LALA_UI_SCALE` (see the core README).
`--strict-glyphs` fails the run when a drawn character has no glyph in the game font.

## Checks

Live-state decisions (`DeriveLive`), wide row column maths (`ColumnsFor`), and a drift check that the stubbed job and
achievement table equals the one in `Plugin.cs`.

## Scenarios

Defined in `Scenarios.cs`: a name, a one-line description of what it must show, a size, a `Setup` that fakes game
state, and optional `Wheel` (real mouse-wheel scrolling of the jobs list), `TooltipRun` (hover the Nth visible row
that has an inventory projection, found by sweeping the mouse so the window's own hover logic opens the tooltip) and
`Scale` (game mode only). To add one, add an entry to `Scenarios.All`; state helpers are `Progress`, `Bag` and
`Plugin.Completed`.
