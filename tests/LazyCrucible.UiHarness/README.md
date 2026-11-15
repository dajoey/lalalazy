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

The project compiles the real window sources, the existing shared UI kit, the guide parser and embedded `CrucibleGuide.json`, and the real data/ranking/policy sources. Only game/runtime state, telemetry, and unmanaged addon geometry are stubbed. Dalamud window hosting and ImGui bindings come from the supplied existing `Dalamud.dll`.

## Captures

- Main: empty/ready, AutoDuty standby, older-GluttonyCombo conflict, and expanded beast-pick advisor.
- Guide: real Strix Piece entry, real Atomic Ray incoming warning, and missing-entry branch.
- Suggestion overlay: real foreground text and frame drawing with deterministic addon geometry.

The real guide must load with zero validation problems. Image differences are expected when layout, styling, text, or state branches change.

## Fidelity limits

The harness uses a CPU rasterizer and only ImGui's built-in font; it never reads host font files. Built-in-font glyph coverage and text metrics differ from Dalamud's live font atlas, so some production symbols can render as fallback glyphs and line widths can differ. The harness does not reproduce Dalamud's GPU renderer, live global style, input interaction, custom textures, draw callbacks, multiple viewports, or DPI scaling. It captures source compilation, rendered content/layout, and state branches for human inspection; it does not compare golden images or assert scenario-specific pixels. In-game visual and interaction checks remain separate release gates.
