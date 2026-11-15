# LazyCrucible real-source UI harness

Renders the real `MainWindow.Draw`, `GuideWindow.Draw`, and `SuggestionOverlay.Draw` methods to PNG without FFXIV, Dalamud, a GPU, or a visible/hidden window. It is a visual capture harness, not a replacement UI and not an in-game behavior test.

## Run

From this directory on Windows with the .NET 10 SDK:

```bat
run.cmd
```

An optional first argument points at another lalalazy checkout; relative arguments resolve from the caller's directory. The default is the repository containing this test project. The command builds Release, renders eight deterministic state setups, checks that every PNG has a distinct SHA-256, and exits. Outputs go under `output/`; generated images and local NuGet packages are ignored by git. Deterministic state setup is not a promise of cross-machine byte-identical images: installed fonts can change text metrics and layout.

The project compiles the real window sources, the existing shared UI kit, the guide parser and embedded `CrucibleGuide.json`, and the real data/ranking/policy sources. Only Dalamud window hosting, game/runtime state, telemetry, and unmanaged addon geometry are stubbed.

## Captures

- Main: empty/ready, AutoDuty standby, older-GluttonyCombo conflict, and expanded beast-pick advisor.
- Guide: real Strix Piece entry, real Atomic Ray incoming warning, and missing-entry branch.
- Suggestion overlay: real foreground text and frame drawing with deterministic addon geometry.

The real guide must load with zero validation problems. Image differences are expected when layout, styling, text, or state branches change.

## Fidelity limits

The harness uses ImGui.NET with a CPU rasterizer and a host-installed font atlas. It does not reproduce Dalamud's GPU renderer, live global style, input interaction, custom textures, draw callbacks, multiple viewports, or DPI scaling. The Dalamud FontAwesome paw is represented by a plain `P` glyph because the production icon font is outside this project. It captures source compilation, rendered content/layout, and state branches for human inspection; it does not compare golden images or assert scenario-specific pixels. In-game visual and interaction checks remain separate release gates.
