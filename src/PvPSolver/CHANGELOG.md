# Changelog - PvP Solver

## [0.1.0.7] - 2026-06-07
### Added
- Auto-targeting for Forlorn / Maiden targets: checks the global `Svc.Objects` list to target any targetable, alive Forlorn/Maiden immediately upon rendering, regardless of whether they are in the hostile list.
- Increased `sipRange` in `ObjectHelper.cs` to 48f (from 25f) to ensure they are targeted immediately upon entering the player's hostile radar range.

## [0.1.0.5] - 2026-06-04
### Changed
- Rebranded and updated all remaining user-facing references to `RSR` and `/rotation` (such as in settings tooltips, enum descriptions, and compatibility diagnostics) to `PvP Solver` and `/pvpsolver`.

## [0.1.0.4] - 2026-06-04
### Changed
- Rebranded and updated all leftover RSR and `/rotation` command references in the UI, settings, and first-start tutorial to PvP Solver and `/pvpsolver` to avoid user confusion.
- Disabled PVE incompatible-plugins download task, preventing the 404 orange warning banner in the UI.
- Updated EzIPC prefix to `PvPSolver` and `PvPSolver.ActionUpdater`, preventing side-by-side IPC collisions.

## [0.1.0.3] - 2026-05-24
### Added
- Wholly Original Rebrand: Renamed core slash commands to `/pvpsolver` and `/pvs` to prevent command collisions.
- Built-in out-of-combat/no-enemy cast protection in core action checking logic (blocks all combat abilities in PvP if no enemies are within 40 yalms).
