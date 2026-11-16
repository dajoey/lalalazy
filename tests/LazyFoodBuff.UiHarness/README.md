# LazyFoodBuff real-source UI harness

Renders the real `ConfigWindow.Draw` (the plugin's only window) with the real `FoodUiChrome`, `Configuration`,
`FoodRecommender`, `FoodTelemetryFormat` and `LalaUiKit` sources to PNG, on the CPU, through the shared core in
`../HarnessShared/UiRender`. Only the game and runtime boundary is stubbed (`Stubs.cs`): the plugin services, the
Well Fed status, the combat-duty test, the food inventory, the ClassJob sheet and the job icon texture.

```
DalamudLibPath=<runtime dir with Dalamud.dll, Dalamud.Bindings.ImGui.dll, cimgui.dll>
LALA_UI_ASSET_DIR=<Dalamud UIRes folder>   LALA_UI_SCALE=1.02
dotnet run --project tests/LazyFoodBuff.UiHarness -c Release -- --game      # game look, images in output/game/
dotnet run --project tests/LazyFoodBuff.UiHarness -c Release                # built-in look, hermetic, images in output/
```

Game mode renders the full set (default size, the stacked mid and minimum widths, a scale 1.5 evidence image); built-in
mode renders the default-size subset. `--strict-glyphs` fails the run when a drawn character has no glyph in the game font.

## Adding a scenario

1. Add a state function next to `FirstRun` / `ReadyFed` in `Program.cs`: call `Common(inDuty)` (resets the fake world and
   loads the food list), then set `World.*` (logged in, dead, Well Fed food and time left, duty) and the `Configuration`.
2. Add a `yield return new(N("name", w, h), w, h, YourState) { ... }` line in `BuildScenarios`. Options: `SelectedJob`
   (ClassJob row shown in the profile selector), `ScrollToEnd` (both columns to their end), `ScrollNotches` (stacked
   body down by wheel notches), `Popup` (`JobSelector` / `FoodPicker`: a real click opens the combo; the click position
   is found by probing), `CanvasHeight` (display taller than the window for popups), `EditSlider` (Ctrl+click a threshold slider and type), `Scale`.

After the renders the harness also runs interaction checks on the two threshold sliders (Ctrl+click and double-click typed
entry, Enter and Escape, range clamping, Left and Right stepping with navigation on, no stepping with it off) and fails
the run if any of them does not hold.

Real food and job names are used; the stat bonuses are approximate and only steer which food the real ranking picks.
The job icon is one flat placeholder tile (the game icons are not available here).
