# Changelog - Lazy WT Math

## v3.2.3.0 (2026-09-05)

- Added the in-game "What's new" popup. After Lazy WT Math updates, its changelog now opens once inside the game so you can see what changed without going to GitHub. It waits until you are logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read.
- New command `/lazywtmath` reopens that popup. This plugin had no command at all before: the probabilities still appear inside the Wondrous Tails window itself, exactly as before, and nothing about how they are calculated has changed.
- The rebuild against current FFXIVClientStructs from 3.2.2.10 (which stopped the Wondrous Tails crash loop) is included.

## [3.2.2.10] - 2026-09-05
### Fixed
- **Rebuilt against current FFXIVClientStructs (7.55.1.8875) to stop the WeeklyBingo crash loop.** The 3.2.2.9 binaries (built 2026-07-02) threw `System.TypeLoadException: Could not load type 'FFXIVClientStructs.FFXIV.Client.System.Memory.ICreatable'` from `AddonWeeklyBingoController.AddonRefresh` on every WeeklyBingo PostUpdate (3,032 errors in one session on 2026-09-05), because the bundled KamiToolKit.dll was compiled against an older FFXIVClientStructs whose `ICreatable<T>` shape no longer matches the one Dalamud ships. No source changes; clean Release rebuild of LazyWTMath + KamiToolKit against the Dalamud 15.0.3.2 hooks currently loading in the client (files: `KamiToolKit.dll`, `LazyWTMath.dll`).

### Notes
- Fix for Helm thread t-plugerr-52f9741c16e8 (kanban t_a7838408). If the crash persists after updating, the next step is merging upstream MidoriKami/EzWondrousTails and its current KamiToolKit.

## [3.2.2.9] - 2026-07-02
### Fixed
- **Plugin icon now shows in the Dalamud installer.** Installed copies were built before
  `IconUrl` was added to `LazyWTMath.json` and displayed the "?" placeholder. This release
  exists to push the corrected manifest to installed clients; no code changes.

### Notes
- Part of the 2026-07-02 lalalazy repo cleanup pass.
