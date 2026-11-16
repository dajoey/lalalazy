# LazyWTMath UI harness

Two jobs in one console project:

1. the original presentation checks (`LazyWTMathPresentation` parsing, 13 checks), unchanged;
2. real-source UI rendering: the real `LazyWTMathWindow`, `LazyWTMathChrome`, `LazyWTMathPresentation`, the real
   solver `PerfectTails` and the shared `LalaUiKit` are compiled by link and drawn to PNG on the CPU, then two drawn
   controls (What's new, close) are pressed and released to prove their real callbacks fire.

Needs an existing Dalamud runtime folder (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `cimgui.dll`);
nothing is restored or downloaded. Render core and game look: `../HarnessShared/UiRender/README.md`.

```
dotnet run --project tests/LazyWTMath.UiHarness -c Release            built-in look -> output/*.png (7 first-use states)
dotnet run --project tests/LazyWTMath.UiHarness -c Release -- --game  game look -> output/game/*.png (16 scenarios)
```

Game mode also needs `LALA_UI_ASSET_DIR` and optionally `LALA_UI_SCALE` (see the core README). It prints a glyph audit;
`--strict-glyphs` turns a missing glyph into a failure.

## What is stubbed

Only the game boundary, in `Stubs.cs`:

- `FFXIVClientStructs...PlayerState`: `Instance()` is null while "logged out"; `PlayerState.Set(loggedIn, stickerCount, cells)`
  sets the journal the window and the solver read. The harness does not reference the client-structs assembly.
- `WondrousTailsSolver.System`: the plugin's static holder, reduced to `PerfectTails` (the real one also names a native
  addon controller from the vendored toolkit).

The solver is the real one. Its shuffle averages are random samples, so `Program.SeedShuffleSamples` recomputes them over a
fixed seed (same code path, reproducible numbers). The failed state is produced by 10 placed cells, which the solver
rejects like any unknown board.

## Adding a scenario

Add a `new Scenario(name, size, cells)` line to `Scenarios` in `Program.cs`: `cells` is the list of placed 0-based board
cells (`null` = no character). Options: `Hover = Hover.Close` (pointer over the close control, shows its tooltip),
`Scale` (global UI scale), `ScrollEnd` + `Wheel` (wheel-scroll the column under the pointer; the window scrolls its body,
so content below the first screen is only visible this way or in a taller window), `GameOnly`.
Sizes: first-use 920x700, minimum 480x600, maximum 1400x1000 (from `LazyWTMathWindow`).

Not renderable: the native Wondrous Tails journal overlay and its text nodes (vendored toolkit, game nodes), and the
shared changelog popup (out of scope).
