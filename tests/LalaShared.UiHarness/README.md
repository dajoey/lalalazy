# Shared popups real-source UI harness

Renders the real shared popups to PNG on the CPU: the "What's new" changelog window (`src/Shared/LalaChangelog/ChangelogWindow.cs`, fed with real `CHANGELOG.md` files of this repository through the real parser) and the "Report a problem" popup (`src/Shared/LalaTelemetry/ReportPopup.cs`), both on the shared `LalaUiKit` shell parts. Only `DalamudTelemetry` (the game and runtime boundary of the report popup) is stubbed, in `Stubs.cs`.

Needs an existing Dalamud runtime folder (`DalamudLibPath`: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`, `cimgui.dll`); nothing is restored or downloaded. See `../LazyCrucible.UiHarness/README.md` and `../HarnessShared/UiRender/README.md` for the two render modes.

```bat
run.cmd                                 built-in look, 2 hermetic states, output/*.png
dotnet run -c Release -- --game         game look (needs LALA_UI_ASSET_DIR, optional LALA_UI_SCALE), output/game/*.png
dotnet run -c Release -- --game --strict-glyphs     also fail when drawn text has no glyph in the game font
```

Game mode renders every scenario of the `BuildScenarios` table in `Program.cs`: the changelog window with one short entry, several long version blocks (collapsed and expanded, with "### Changed" sub-headings), long bullets, every entry of a plugin, an empty changelog, at the first-use size and the narrowest allowed size, scrolled and at scale 1.5; and the report popup opened from its button and from a tab bar, empty, with typed text, after a write, with the close tooltip. The run also checks, with real pointer input, that the close box and "Got it" close the changelog, that a version card header collapses it, and that the report popup's Cancel, close box and Write report behave as before; and audits the drawn non-ASCII characters (source literals and every plugin's changelog text) against the game font.
