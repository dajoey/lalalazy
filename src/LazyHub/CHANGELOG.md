## v0.1.1.8 (2026-11-21) [testing]
- Refreshed the bundled native-window toolkit (KamiToolKit) to the upstream api16-era release; the hub window's scroll area now recalculates its layout through the toolkit's new layout path.
## v0.1.1.7 (2026-11-16) [testing]
- The Lazy Hub window now has the same dark, rounded look as the other lalalazy windows, with crisp edges instead of blurry ones.
- Settings buttons show the full word, and the buttons and switches now match the style of the other plugin windows.
- The window no longer shows the game's blue background between panels, has a status bar at the bottom, and rows and sections line up evenly with consistent spacing between the two columns.
- The What's new window now uses the same custom window style as the plugin windows.

## v0.1.1.6 (2026-11-15) [testing]
- Lazy Hub now presents quick controls and plugin details in a responsive card-based shell with clear live state and automatically hidden inner scrolling.

## v0.1.1.5 (2026-11-14) [testing]
- The bundled interface toolkit no longer carries an unused image-processing library, so the plugin installs fewer files and no longer ships a package with known security advisories. No behavior changes.

## v0.1.1.4 (2026-11-14) [testing]
- The plugin windows have been rebuilt on the shared LalaUiKit: one consistent layout with themed section headers, unified spacing and colour accents. All settings and behaviour are unchanged, and existing configuration loads as-is.

## v0.1.1.3 (2026-11-10) [testing]
- Rebuilt against the Dalamud 15.0.4.0 payload for game patch 7.58. No functional changes.

# Changelog

## v0.1.1.2 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The hub window is unchanged.
- Testing channel only; the production channel is unchanged.

## v0.1.1.1 (2026-11-05) [testing]

## v0.1.1.2 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The hub window is unchanged.
- Testing channel only; the production channel is unchanged.

- Updated the bundled KamiToolKit UI library to the current upstream version; the hub window is built on the framework's current internals now, and the window-frame tint targets the framework's texture-backed chrome nodes.
- The native window is created on the game's main thread after the UI framework finishes initializing, instead of during plugin load.
- Kept the fork's hot-reload safety guard: teardown disables the hooks, lets any call already inside them finish, and only then disposes.
- Testing channel only; the production channel is unchanged.

## v0.1.1.0 (2026-10-03) [testing]

## v0.1.1.2 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The hub window is unchanged.
- Testing channel only; the production channel is unchanged.

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

## v0.1.1.2 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The hub window is unchanged.
- Testing channel only; the production channel is unchanged.

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
