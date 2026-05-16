# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/repo-icon.png)

# lalalazy — FFXIV Dalamud Plugins

A collection of Dalamud plugins for Final Fantasy XIV, maintained by [dajoey](https://github.com/dajoey).

## Plugins

| Plugin | Description | Status |
|--------|-------------|--------|
| **Gluttony Combo** | XIVCombo for very lazy players. Condenses combos and mutually exclusive abilities onto a single button — and then some. Fork of Wrath Combo. | Active |
| **PvP Solver** | Auto-rotation for PvP combat. All jobs. Activates automatically in PvP zones. Designed to run alongside Gluttony Combo. | Active |
| **Dagobert Price Matcher** | Matches market board prices instead of undercutting. Default match amount: 0 (exact match). | Active |
| **AutoPotion** | Auto-uses HP potions and deep dungeon regen potions at configurable HP thresholds. | Active |
| **Armoire Auto-Fill** | Per-dungeon view of armoire-eligible gear pieces you're still missing. Detects in-armoire, in-inventory, and equipped state. | Active |

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

> **Note:** Gluttony Combo replaces Wrath Combo. Disable Wrath Combo before installing Gluttony Combo — they cannot be loaded at the same time.

## Credits & Origins

### Gluttony Combo
Fork of [WrathCombo](https://github.com/PunishXIV/WrathCombo) by Team Wrath / PunishXIV. Licensed under GPLv3. Lalalazy-branded fork with custom improvements including healer raidwide mitigation overlap protection and ground-targeted heal auto-placement on tanks.

### PvP Solver
Fork of [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn) by ArchiDog1998 / FFXIV-CombatReborn. Licensed under GPLv3. Rewired for PvP-only operation — PvE rotations stripped, action IDs remapped to PvP equivalents. Designed to run alongside Gluttony Combo for PvE coverage.

### Dagobert Price Matcher
Fork of [Dagobert](https://github.com/SHOEGAZEssb/Dagobert) by SHOEGAZEssb. Licensed under AGPLv3. Changed default price behavior from undercutting to exact matching.

### AutoPotion
Original plugin by dajoey. Built from scratch using the Dalamud plugin SDK.

### Armoire Auto-Fill
Original plugin by dajoey. Reads the in-game Cabinet sheet for the canonical armoire-eligible item list, joined with [LuminaSupplemental.Excel](https://github.com/Critical-Impact/LuminaSupplemental) (GPL-3.0, by Critical-Impact) for dungeon drop attribution. Cabinet observation technique inspired by [seventhxiv/Collections](https://github.com/seventhxiv/Collections).

## Build

Each plugin builds with the Dalamud SDK (.NET 10 SDK required). See individual plugin READMEs in `src/` for details.

```bash
cd src/GluttonyCombo && dotnet build --configuration Release
cd src/PvPSolver && dotnet build --configuration Release
cd src/DagobertPriceMatcher && dotnet build --configuration Release
cd src/AutoPotion && dotnet build --configuration Release
cd src/ArmoireAutoFill && dotnet build --configuration Release
```

## License

Individual plugins retain the licenses of their origin projects. See `COPYING` / `LICENSE` files in each `src/` subdirectory.