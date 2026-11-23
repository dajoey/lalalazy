## v3.2.3.12 (2026-11-23) [testing]
- Refreshed the bundled native-window toolkit (KamiToolKit) to the upstream interface rework, which splits node capabilities into dedicated interfaces (attachable, colour, position, rotation, scale, size, visibility, silent set) and adds a window frame node.
- No setting or window behavior changes; the probability overlay builds through the same nodes as before.

## v3.2.3.11 (2026-11-22) [testing]
- Refreshed the bundled native-window toolkit (KamiToolKit) to the upstream layout-engine rework, which rebuilds the internals of the list, grid and tab layout nodes and adds a shared layout base class (LayoutNodeBase) plus size-change batching on the node base.
- No setting or window behavior changes; the probability overlay renders through the same text and collision nodes as before.

## v3.2.3.10 (2026-11-21) [testing]
- Refreshed the bundled native-window toolkit (KamiToolKit) to the upstream api16-era release, which reorganizes how native game windows and nodes are built.
- The probability overlay keeps its journal-matched background, text colours, font size and spacing through the toolkit's new node layout.
## v3.2.3.9 (2026-11-16) [testing]
- Fixed a crash risk while logging in or out with the Wondrous Tails journal open.
- The probability overlay no longer recomputes every frame.

## v3.2.3.8 (2026-11-15) [testing]
### Changed
- The window was polished for the real in-game font and scale: text no longer clips or sits against card borders.
- Cards keep their content height, so the sticker map, Shuffle average, About and Journal overlay no longer stretch into empty space at large sizes.
- At the default size the whole sticker map is visible in the Idle, Empty and Failed states too, and the three line-chance cards stay in one row down to the narrowest width.
- The header has a bordered name chip, a neutral status pill showing only the state word and a What's new button.
- The Close button shows an accent cross and outline on hover, with its tooltip directly beneath it.

## v3.2.3.7 (2026-11-15) [testing]
### Changed
- The Dalamud Open actions now show a responsive probability window with a custom header without the native title bar, a close control, a live sticker count and sticker map, line chances and the solver-supplied shuffle averages. What's new in the header reopens this changelog. The probability text beside the journal uses higher-contrast colours. Solver results, configuration and commands are unchanged.

## v3.2.3.6 (2026-11-14) [testing]
- The bundled interface toolkit no longer carries an unused image-processing library, so the plugin installs fewer files and no longer ships a package with known security advisories. No behavior changes.

## v3.2.3.5 (2026-11-10) [testing]
- Rebuilt against the Dalamud 15.0.4.0 payload for game patch 7.58. No functional changes.

# Changelog - Lazy WT Math

## v3.2.3.4 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The Wondrous Tails probability readout is unchanged.
- Testing channel only; the production channel is unchanged.

## v3.2.3.3 (2026-11-05) [testing]

## v3.2.3.4 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The Wondrous Tails probability readout is unchanged.
- Testing channel only; the production channel is unchanged.

- Updated the UI framework bundled inside the plugin to the current upstream KamiToolKit; the Wondrous Tails probability readout is built on the framework's current internals now.
- Kept the fork's hot-reload safety guard: the plugin's hooks disable, let any call already inside them finish, and only then dispose, so an update while the game is running can no longer crash the game from a call landing on a hook that is gone.
- Testing channel only; the production channel is unchanged.

## v3.2.3.2 (2026-10-04) [testing]

## v3.2.3.4 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The Wondrous Tails probability readout is unchanged.
- Testing channel only; the production channel is unchanged.

- Fixed a rare game crash when the plugin updates while the game is running: while its hooks are active and the plugin is updated again, they are disabled only after any call still inside them has finished, so a call can no longer land on a hook that is already gone. The Wondrous Tails math readout is unchanged.
- Testing channel only; the production channel is unchanged.

## v3.2.3.1 (2026-09-07)

## v3.2.3.4 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The Wondrous Tails probability readout is unchanged.
- Testing channel only; the production channel is unchanged.

- Removed the `/lazywtmath` command introduced in 3.2.3.0. It existed only to reopen the "What's new" popup, and this plugin's only interface is the probability readout inside the Wondrous Tails window itself.
- The changelog popup itself is unchanged: it still opens once automatically after an update, and it can be reopened any time from the Dalamud plugin installer (the plugin's "Open" button).
- No math, plugin behaviour or configuration changed.

## v3.2.3.0 (2026-09-05)

## v3.2.3.4 (2026-11-07) [testing]

- No plugin-code changes: the plugin now builds against a fixed Dalamud library set (the pinned staging 15.0.3.9 payload) instead of whichever libraries the build machine happens to have installed, so packaging this plugin works reliably again. The Wondrous Tails probability readout is unchanged.
- Testing channel only; the production channel is unchanged.

- Added the in-game "What's new" popup. After Lazy WT Math updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read.
- New command `/lazywtmath` reopens that popup. This plugin had no command at all before: the probabilities still appear inside the Wondrous Tails window itself, exactly as before, and nothing about how they are calculated has changed.
- The rebuild against current FFXIVClientStructs from 3.2.2.10 (which stopped the Wondrous Tails crash loop) is included.

## [3.2.2.10] - 2026-09-05
### Fixed
- **Rebuilt against current FFXIVClientStructs (7.55.1.8875) to stop the WeeklyBingo crash loop.** The 3.2.2.9 binaries (built 2026-07-02) threw `System.TypeLoadException: Could not load type 'FFXIVClientStructs.FFXIV.Client.System.Memory.ICreatable'` from `AddonWeeklyBingoController.AddonRefresh` on every WeeklyBingo PostUpdate (3,032 errors in one session on 2026-09-05), because the bundled KamiToolKit.dll was compiled against an older FFXIVClientStructs whose `ICreatable<T>` shape no longer matches the one Dalamud ships. No source changes; clean Release rebuild of LazyWTMath + KamiToolKit against the Dalamud 15.0.3.2 hooks currently loading in the client (files: `KamiToolKit.dll`, `LazyWTMath.dll`).

### Notes
- If the crash persists after updating, the next step is merging upstream MidoriKami/EzWondrousTails and its current KamiToolKit.

## [3.2.2.9] - 2026-07-02
### Fixed
- **Plugin icon now shows in the Dalamud installer.** Installed copies were built before
  `IconUrl` was added to `LazyWTMath.json` and displayed the "?" placeholder. This release
  exists to push the corrected manifest to installed clients; no code changes.

### Notes
- Part of the 2026-07-02 lalalazy repo cleanup pass.
