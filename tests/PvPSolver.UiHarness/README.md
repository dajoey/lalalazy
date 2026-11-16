# PvPSolver real-source UI harness

Renders the real `PvpSettingsWindow.Draw` (the fork-only settings window: shell, chrome and its six pages) to PNG without
FFXIV, a running Dalamud instance, a GPU or a visible window. It is a visual capture harness, not a replacement UI and
not an in-game behaviour test. The plugin's older windows (rotation config, control, cooldown, timeline, tutorial) were
not restyled and are not drawn here.

## Prerequisite

An existing Dalamud runtime directory containing `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `Dalamud.Bindings.ImPlot.dll`
(compile-time only) and the matching native `cimgui.dll`, supplied through `DalamudLibPath`. Nothing is restored or
downloaded and the project has no package reference.

```bat
set "DalamudLibPath=<existing-runtime-directory>"
set "LALA_UI_ASSET_DIR=<dalamud-UIRes-folder>"
set "LALA_UI_SCALE=1.02"
dotnet run --project PvPSolver.UiHarness.csproj -c Release -p:DalamudLibPath=%DalamudLibPath% -- --game
```

`run.cmd` does the same for the built-in mode (an optional first argument points at another checkout).

| Mode | Select | Output | Look |
| --- | --- | --- | --- |
| built-in (default) | nothing | `output/*.png` (7 images) | ImGui's 13 px font and default style; hermetic and byte-stable |
| game | `--game` or `LALA_UI_FIDELITY=game` | `output/game/*.png` (28 images) | Dalamud default font, global scale and style (see `../HarnessShared/UiRender/README.md`) |

Game mode also prints a glyph audit (`--strict-glyphs` turns a miss into a failure) and, in both modes, the harness
verifies that its `Configs` stand-in answers every `PvpSettingsCatalog` property with the catalog default, and that a
real press and release on the drawn close button closes the window.

## What is real and what is stubbed

Real (linked with `<Compile Include>` from the plugin and shared kit, never copied): everything in `UI/Pvp/*.cs`
(`PvpSettingsWindow`, `PvpApprovedShell`, `PvpApprovedChrome`, the six tabs, `PvpUi`, `PvpBinding`, `PvpJobOptions`),
`UI/ImguiTooltips.cs`, `src/Shared/LalaUiKit`, all of `Basic/Decisions/*.cs` (catalog, defensive table, targeting list,
switch wording), the fork's `Configs.PvpSettings.cs`, and the small enums and interfaces the window reads.

Stubbed in `Stubs.cs` (the game and runtime boundary only):

- `ECommons` services the window touches: `Job`, `Player.Available/Job`, `PluginLog`, `Svc.PluginInterface`.
- `PvPSolverPlugin` entry points (open full settings, changelog, tutorial): recorded, nothing opens.
- `PvpSwitch` (DataCenter state, zone, command pipeline): the wording is the real `SwitchStatus.Describe`; the three inputs
  are `PvpSwitch.IsOn`, `PvpSwitch.InPvpZone` and `Player.Available`.
- `Configs`: the plugin's generated members the window reads (`ConditionBoolean` toggles, floats, enums, target list,
  teaching colour). Defaults are the real ones and are re-checked against the catalog on every run.
- Rotation option sets (`RotationUpdater.GetRotations`): parsed at start from the REAL PvP rotation sources
  (`RebornRotations/PVPRotations/**`, the `[RotationConfig(CombatType.PvP ...)]` properties: names, defaults, ranges, parent
  links), so every job shows the plugin's own options. Stored values live in `HarnessOptionStore`.
- `Newtonsoft.Json` attribute stand-ins so the real settings partial compiles without the serializer.

## Scenarios and how states are faked

Scenarios are one table (`AllScenarios` in `Program.cs`). Each renders the window at the size read from `PvpSettingsWindow`
(`Size` 1080x760 first use, `SizeConstraints.MinimumSize` 560x420), unscaled like the sibling harnesses (the game opens a
window at `Size` times the global scale). Add one by adding a row; fields:

- `Page`: Match, Survival, Targeting, My Job, Display or Advanced (sets the shell's current page by reflection).
- `Situation`: `NoCharacter`, `OutsidePvp`, `Ready`, `Running` (sets `Player.Available`, `PvpSwitch.*`).
- `Configure`: edits the live `Configs` (the shared `Typical` preset shows changed values, so Reset buttons and default
  hints appear); rotation option values via `HarnessOptionStore.Values[(Job.X, "PropertyName")]`.
- `PickedJob`, `Playing`: the job picked on My Job (static `_picked`, by reflection) and the played job.
- `SwitchMessage`: the failed turn-on message (private `_switchMessage`, by reflection).
- `Popup`: `reset` or `restore` opens the confirmation (private static flags of `PvpAdvancedTab`, by reflection).
- `ScrollY`: mouse wheel clicks sent over the page body (about 86 px each at scale 1.02; `ToBottom` goes to the end; the
  body is a scrolling child, so below-the-fold content needs a scrolled scenario).
- `Hover`: mouse over the footer button, which shows the real tooltip.
- `OptionsFail` / `OptionsLoading` / `RetryEveryFrame`: the rotation cache throws or returns nothing. The real panel keeps
  the failure text for one frame (it then waits 5 s and shows the loading line), so `RetryEveryFrame` clears the wait
  every frame to render the failure frame.
- `BreakDisplayColour`: makes the Display tab throw while it reads the highlight colour, to reach the window's own error
  line. Scenarios that expect a logged error say `ExpectError`; any other logged error fails the run.
- `Size`: `FirstUse`, `Narrow` (560x420), `NarrowWidth` (560 wide, first-use height); `Scale`: per-scenario global scale.
- `InBuiltIn`: also rendered in the hermetic mode.

Output names carry the size (`<name>-1080x760.png`). Render through the shared wrapper on the Windows build host; the
render wipes `output/` first, and `output/` is ignored by git.

## Fidelity limits

Same as the shared core (no GPU blending, no custom player font or style, no game-symbol glyphs, no Dalamud window
chrome; the window's native title bar is replaced by the approved header, `NoTitleBar` is set by the real window). The
real window background, drop shadow and window rounding that Dalamud adds are not drawn. Popups (reset, restore) open
centred in the display area; hover states other than the footer tooltip are not rendered.
