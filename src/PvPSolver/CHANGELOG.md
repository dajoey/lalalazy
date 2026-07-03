# Changelog - PvP Solver

## [0.1.0.9] - 2026-07-02
### Changed
- Hidden the three PvE-only config tabs inherited from upstream: Duty Rotation, Duty, and AutoDuty (incl. the AutoDuty helper-plugin installer list). Tabs are `[TabSkip]`-hidden, not deleted, so nightly subtree merges stay clean. (UI/RotationConfigWindowTab.cs)
- Removed upstream Combat Reborn donation/community links: Ko-fi title-bar button, About-tab Ko-fi button and Discord banner, and the "Thanks to Supporters" About section with its random supporter shout-out hints. (UI/RotationConfigWindow.cs)
- Easter egg window retitled "PvP Solver Lab" (was "RSR Lab"). (UI/EasterEggWindow.cs)

### Notes
- Part of the 2026-07-02 fork-branding cleanup pass across lalalazy forks. No rotation/behavior changes.
- The Actions-tab "Intercepted" option is PvE-only by nature and remains visible; candidate for a later pass.

## [0.1.0.8] - 2026-06-18
### Changed
- Skip loading PvE duty rotations (Bozja, Emanation, MonsterHunter, Orbonne, Phantom, Variant) entirely. PvPSolver is PvP-only, so the duty rotation set had zero relevance; not loading it reduces memory use and startup time. (Updaters/RotationUpdater.cs)

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
