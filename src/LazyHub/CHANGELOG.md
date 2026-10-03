# Changelog

## v0.1.1.0 (2026-10-03) [testing]

### Added
- **Switches and settings for the whole suite.** Plugins that offer controls can be turned on or off and have their common settings changed from the hub, over Dalamud IPC. Each change does exactly what the matching setting in the owning plugin's own window does; the hub never writes another plugin's configuration.
- **Quick tab:** a Gluttony Combo tray with the Auto-Rotation switch and hotbar buttons (click a button, then click a hotbar slot to place the action; Escape cancels), then one switch per plugin that offers one. Switches with real consequences (Auto-Market, the FATE bot) ask for a second click within a few seconds.
- **Plugin page:** the plugin's settings in groups: switches, number steppers (`-` and `+`) and option pickers (`<` and `>`). A setting that another plugin or a running task holds is shown as locked with the reason and refuses changes.
- **Plugins tab:** all fifteen plugins with status, Settings and Open. A plugin on a version that does not offer controls yet has Open only.
- **Safe mode:** `/lazy safe` switches to the plain window and back. The choice is remembered.
- **Look:** the window frame is tinted toward lalalazy green with forest-green panels, gold accents and each plugin's icon.

### Fixed
- **Plugin icons fit their slot.** They were drawn at their full 64-pixel size instead of the 44-pixel slot, which covered the first letters of each name and status line and touched the row below.

### Notes
- Testing channel only. Plugins offer controls from the versions released with this one: update them to see their settings here.
- The hub asks each plugin for its controls about once a second while the window is open and not at all while it is closed.
- After an update the "What's new" window shows these notes once. `/lazy changelog` reopens them.

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
