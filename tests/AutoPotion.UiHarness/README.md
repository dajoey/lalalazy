# AutoPotion real-source UI harness

Renders the real `ConfigWindow.Draw` (with `AutoPotionChrome`, `Configuration`, `PotionTelemetry` and the shared `LalaUiKit`,
all linked from `src/`, never copied) to PNG without the game, a running Dalamud or a GPU. Only the game/runtime boundary is
stubbed, in `Stubs.cs`: the plugin services the window reads (local player, condition flags, ClassJob sheet, game icons, log).
Rasterizer, PNG writer and the game look come from the shared core in `../HarnessShared/UiRender` (see its README).

## Run

Needs an existing Dalamud runtime folder (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `cimgui.dll`).

```
dotnet run --project AutoPotion.UiHarness.csproj -c Release                # built-in mode: hermetic, output/*.png
dotnet run --project AutoPotion.UiHarness.csproj -c Release -- --game      # game look, output/game/*.png
```

Game mode also needs `LALA_UI_ASSET_DIR` (Dalamud's UI font folder) and optionally `LALA_UI_SCALE` (use 1.02). `--strict-glyphs`
makes a missing glyph fail the run. Image names are `<scenario>-<width>x<height>`.

## Scenarios

Every scenario is one `Scenario` record in `Program.cs` (`AllScenarios`): job, character present or dead, combat and duty flags,
a `Configure` action over the real `Configuration`, and optional hover or wheel scrolling. The window is drawn at its first-use
size (1080x780), at its minimum (420x540) and at 900x700 (below the 900 px content width the rules and the rail stack in one
column). Scroll scenarios send one mouse-wheel event to the real child windows, so lower cards are reached the way a player does.
The `scale150-` image is evidence only and is written in game mode only.

Checks that run after rendering: the glyph audit (game mode), the drawn close button closes the window on a real press and
release, the drawn toggle flips its value on a real press and release, and a threshold slider takes a typed value (Ctrl+click, digits, Enter).

## Limits

Job icons are placeholder tiles (the harness has no game files). Names come from a small ClassJob subset stored lower case like
the game data. Item and retainer names are not drawn by this plugin. The shared limits (no GPU blending, no Dalamud window
chrome, no game-symbol glyphs) apply.
