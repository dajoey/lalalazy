# LazyRetainerLive real-source UI harness

Renders the real `ConfigWindow.Draw` (with the real `RetainerLiveChrome`, `RetainerLivePresentation`, `Configuration`, wire model and the shared `LalaUiKit`) to PNG on the CPU. Only the plugin and service boundary is stubbed: `Stubs.cs` holds a `Plugin` (config, listener error text, save counter) and a `RetainerLiveService` (published snapshot, live-table flag).

Needs an existing Dalamud runtime folder (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `cimgui.dll`); nothing is restored or downloaded. See `../LazyCrucible.UiHarness/README.md` and `../HarnessShared/UiRender/README.md` for the two render modes.

```bat
run.cmd                                 built-in look, 3 hermetic states, output/*.png
dotnet run -c Release -- --game         game look (needs LALA_UI_ASSET_DIR, optional LALA_UI_SCALE), output/game/*.png
dotnet run -c Release -- --game --strict-glyphs     also fail when drawn text has no glyph in the game font
```

Game mode renders every scenario in the `Scenarios` table of `Program.cs`: first run, ready (typical, extreme values, minimal character), blocked (last-known table), failed (listener bind error), idle (listener off), at the first-use size 980x680 and the narrowest allowed size 420x480, plus a mid-width, tall and scrolled variants, the close-button tooltip and one scale 1.5 evidence image. To add one, add an entry (name, size, a `Plugin` setup lambda). The run also checks that a real press on the close control closes the window and a press on the stop button disables the listener and saves, and audits the drawn non-ASCII characters against the game font.
