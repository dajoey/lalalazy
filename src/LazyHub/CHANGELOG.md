# Changelog

## v0.1.0.0 (2026-10-03)

### Added
- **First release: one in-game window for the whole lalalazy suite.** `/lazy` (or `/lazyhub`) opens a native game-style window with two tabs.
- **Plugins tab:** all fifteen lalalazy plugins in a grid, each with its icon, its name, a live status and an Open button. Status reads as Loaded, Loaded (testing), Not loaded or Not installed, and refreshes about once a second. Open launches that plugin's main window (or its settings window when it has no main window) and is available only while the plugin is running.
- **Quick tab:** shows whether Gluttony Combo's auto-rotation is on or off, read from Gluttony Combo itself. The card says so when Gluttony Combo is not running.
- **Server info bar entry** showing how many of the suite's plugins are running, for example `13/15`. Clicking it opens or closes the window.
- The plugin opens from the Dalamud plugin installer's Open button as well.

### Notes
- This is a testing-channel first release. It reads status and opens windows; it does not change any plugin's settings yet.
- The window uses the stock game window frame. The icons are the repository's pixel-art plugin icons.
- After an update the "What's new" window shows these notes once. `/lazy changelog` reopens them.
