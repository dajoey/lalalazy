# LazyFishSitter real-source UI harness

Renders the real `ConfigWindow.Draw` (the plugin's only window) to PNG without FFXIV, a running Dalamud instance, a GPU,
or a visible window. It compiles the real `ConfigWindow`, `FishSitterChrome`, `Configuration`, the Dalamud-free
`Core/SitPolicy`, and the shared `LalaUiKit`. Only the game/runtime boundary is stubbed (`Stubs.cs`): `Plugin`
(config, condition flags, save) and `FishSitService` (same public surface, but driven by the real `SitPolicy`).
Rasterizer, PNG writer and game look come from `../HarnessShared/UiRender` (see its README).

## Run

Needs an existing Dalamud runtime directory (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `cimgui.dll`);
no package reference, no restore. Game mode also needs `LALA_UI_ASSET_DIR` and optionally `LALA_UI_SCALE`.

```bat
set "DalamudLibPath=<existing-runtime-directory>"
set "LALA_UI_ASSET_DIR=<dalamud-UIRes-folder>"
set "LALA_UI_SCALE=1.02"
dotnet run --project LazyFishSitter.UiHarness.csproj -c Release -- --game
```

Without `--game` the harness is hermetic (ImGui built-in font and style, `output/*.png`); game mode writes
`output/game/*.png`. Output is ignored by git. `--strict-glyphs` fails the run when drawn text has a character
the game font lacks.

## Scenarios

Each scenario is one line in `Scenarios` in `Program.cs` plus a `Rig` script: the rig replays a scripted fishing session
(fake clock, fixed epoch) through the real policy, so the status words, last-check text and counters are the real ones.
Sizes are read from the window (`Size` for first use, `SizeConstraints.MinimumSize` for narrowest); `Custom` sizes are
explicit. A `Scale` renders at another global scale (evidence only). Condition flags (combat, cutscene) are set with
`Plugin.Condition.Set(...)` inside a script. Config changes (disabled, a bad command) are set on `r.Plugin.Config`.

The "Last sit command" time is the fixed fake clock converted to the machine's local time zone.
