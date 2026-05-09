# lalalazy — FFXIV Dalamud Plugins

A collection of Dalamud plugins for Final Fantasy XIV, maintained by [dajoey](https://github.com/dajoey).

## Plugins

| Plugin | Description | Status |
|--------|-------------|--------|
| **PvP Solver** | Auto-rotation for PvP combat. All jobs. Activates automatically in PvP zones. | Active |
| **Dagobert Price Matcher** | Matches market board prices instead of undercutting. Default match amount: 0 (exact match). | Active |
| **AutoPotion** | Auto-uses HP potions and deep dungeon regen potions at configurable HP thresholds. | Active |
| **Armoire Auto-Fill** | Scans inventory for missing armoire-compatible dungeon gear. | Archived |

## Installation

Add this custom plugin repository URL in Dalamud:

```
https://raw.githubusercontent.com/dajoey/lalalazy/main/pluginmaster.json
```

1. In-game, type `/xlsettings` and go to the **Experimental** tab
2. Scroll to **Custom Plugin Repositories**
3. Paste the URL above into the text field and click **+**
4. Click the **Save** icon (bottom-right)
5. Plugins will appear in the **Available Plugins** tab in `/xlplugins`

## Credits & Origins

### PvP Solver
Fork of [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn) by ArchiDog1998 / FFXIV-CombatReborn. Licensed under GPLv3. Rewired for PvP-only operation — PvE rotations stripped, action IDs remapped to PvP equivalents. Designed to run alongside Wrath Combo for PvE coverage.

### Dagobert Price Matcher
Fork of [Dagobert](https://github.com/SHOEGAZEssb/Dagobert) by SHOEGAZEssb. Licensed under AGPLv3. Changed default price behavior from undercutting to exact matching.

### AutoPotion
Original plugin by dajoey. Built from scratch using the Dalamud plugin SDK.

### Armoire Auto-Fill
Original plugin by dajoey. Archived.

## Build

Each plugin builds with the Dalamud SDK. See individual plugin READMEs in `src/` for details.

```bash
cd src/PvPSolver && dotnet build --configuration Release
cd src/DagobertPriceMatcher && dotnet build --configuration Release
cd src/AutoPotion && dotnet build --configuration Release
```

## License

Individual plugins retain the licenses of their origin projects. See `COPYING` / `LICENSE` files in each `src/` subdirectory.
