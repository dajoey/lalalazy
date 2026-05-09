# PvP Solver

Auto-rotation plugin for FFXIV **PvP only**. Automatically activates in PvP zones (Frontlines, Crystalline Conflict, Rival Wings) and disables in PvE areas. Designed to run alongside **Wrath Combo** (which handles PvE rotations).

## Features

- Full PvP rotations for all 22 jobs
- Automatic PvP zone detection — no manual toggling
- Per-job rotation configuration via `/rotationconfig`
- Compatible with Wrath Combo (IPC integration — defers PvE to Wrath)
- Drawing/Highlight mode for learning rotations
- Macro support for custom rotation sequences

## Commands

| Command | Description |
|---------|-------------|
| `/rotationconfig` | Open configuration window |
| `/pvpfinder` | Force PvP mode detection |

## Building

Requires .NET 10 SDK and Dalamud.

```bash
# Clean build (required between changes due to source generator cache)
rm -rf PvPSolver.Basic/obj PvPSolver/obj PvPSolver.SourceGenerators/obj
dotnet build --configuration Release
```

## Credits

**Forked from [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)** by **ArchiDog1998** / **FFXIV-CombatReborn**.

Licensed under **GPLv3**. See [COPYING](COPYING) and [COPYING.LESSER](COPYING.LESSER).

### Key Changes from Upstream

- Removed all PvE rotation logic — this plugin only operates in PvP
- Remapped action IDs from PvE to PvP equivalents (Swiftcast, Thin Air, etc.)
- Default rotations rewritten for PvP action sets
- IPC integration with WrathCombo for PvE deferral
- Fixed source generator (Resources.resx name mismatch)
- Fixed `LogOnce` deduplication (was reference equality, now string-keyed)
- Added `Count > 0` guard to prevent index-out-of-range on empty rotation lists
