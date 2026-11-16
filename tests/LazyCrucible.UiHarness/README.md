# LazyCrucible real-source UI harness

Renders the real `MainWindow.Draw`, `GuideWindow.Draw`, and `SuggestionOverlay.Draw` methods to PNG without FFXIV, a running Dalamud instance, a GPU, or a visible/hidden window. It is a visual capture harness, not a replacement UI and not an in-game behavior test.

## Existing runtime prerequisite

An existing Dalamud runtime directory is required. It must already contain `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, and the matching native `cimgui.dll`; the harness neither downloads nor restores these files and has no package reference. Supply that directory through the `DalamudLibPath` environment variable before running:

```bat
set "DalamudLibPath=<existing-runtime-directory>"
run.cmd
```

The project references the existing managed DLL and copies those existing managed/native runtime files into its ignored build output so the process can load them. No game installation or plugin configuration is read or modified.

## Run

From this directory on Windows with the .NET 10 SDK and `DalamudLibPath` set:

```bat
run.cmd
```

An optional first argument points at another lalalazy checkout; relative arguments resolve from the caller's directory. The default is the repository containing this test project. The command builds Release, renders eight deterministic state setups, checks that every PNG has a distinct SHA-256, and exits. Outputs go under `output/`; generated images and build output are ignored by git.

The project compiles the real window sources, the existing shared UI kit, the guide parser and embedded `CrucibleGuide.json`, and the real data/ranking/policy sources. Only game/runtime state, telemetry, and unmanaged addon geometry are stubbed. Dalamud window hosting and ImGui bindings come from the supplied existing `Dalamud.dll`. Rasterizer, PNG writer and the game look come from the shared render core in `../HarnessShared/UiRender` (see its README).

### Two render modes

| Mode | How to select | Output | Look |
| --- | --- | --- | --- |
| built-in (default) | nothing | `output/*.png` | ImGui's embedded 13 px monospace font and default dark style; hermetic, no host file is read |
| game | `--game` argument, or `LALA_UI_FIDELITY=game` | `output/game/*.png` | what a stock Dalamud install draws with: Dalamud's default font, global scale, "Dalamud Standard" style |

Game mode additionally needs `LALA_UI_ASSET_DIR` (the folder holding Dalamud's UI font files, `NotoSansCJK-Medium.ttc` and `FontAwesome710FreeSolid.otf` are required) and optionally `LALA_UI_SCALE` (the global UI scale, default `1.0`). It fails with a message naming the missing folder or file.

```bat
set "DalamudLibPath=<existing-runtime-directory>"
set "LALA_UI_ASSET_DIR=<dalamud-UIRes-folder>"
set "LALA_UI_SCALE=1.02"
dotnet run --project LazyCrucible.UiHarness.csproj -c Release -- --game
```

(`run.cmd` forwards the environment, so `set LALA_UI_FIDELITY=game` before `run.cmd` also works.) Game mode writes the eight scenarios at their existing sizes plus every main and guide scenario at the windows' first-use size 1280x840 and at a narrow 920x640, named `<scenario>-1280x840` and `<scenario>-920x640`; 22 images in all. It also prints a glyph audit (every non-ASCII character in the guide data and in the string literals of the compiled window sources must exist in the game font; `--strict-glyphs` turns a miss into a failure). In game mode two scenarios that render identically are reported as a warning instead of failing, because a size variant can legitimately hide the state it sets up.

## Captures

- Main: empty/ready, AutoDuty standby, older-GluttonyCombo conflict, and expanded beast-pick advisor.
- Guide: real Strix Piece entry, real Atomic Ray incoming warning, and missing-entry branch.
- Suggestion overlay: real foreground text and frame drawing with deterministic addon geometry.

The real guide must load with zero validation problems. Image differences are expected when layout, styling, text, or state branches change.

## Fidelity limits

Built-in mode uses a CPU rasterizer and only ImGui's built-in font; it never reads host font files. Its glyph coverage and text metrics differ from Dalamud's live font atlas, so some production symbols render as fallback glyphs and line widths differ. Use it as the hermetic regression capture.

Game mode reproduces the stock default font, scale and style (the exact members are listed in `../HarnessShared/UiRender/README.md`) and keeps the rest of this harness unchanged. It still does not reproduce the GPU renderer (blending is a CPU approximation), a custom font or style chosen by a player, the game's own glyphs (the private-use game symbols are absent), the Dalamud window chrome and title bar, input interaction, custom textures, draw callbacks, multiple viewports or DPI scaling. The real game also runs a different Dalamud build than the supplied runtime directory, so small drift is possible. It captures source compilation, rendered content/layout and state branches for human inspection; it does not compare golden images or assert scenario-specific pixels. In-game visual and interaction checks remain separate release gates.
