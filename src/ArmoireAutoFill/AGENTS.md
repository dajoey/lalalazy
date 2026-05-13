# Armoire Auto-Fill — AGENTS.md

## Overview
Dalamud plugin that lists every armoire-eligible item in the game, grouped by the dungeon (or other source) that drops it, and shows whether each piece is in your inventory, in your armoire, or still missing.

## Build & Deploy
Per the user's umbrella-repo rule: **publish through `dajoey/lalalazy` on GitHub instead of installing directly into the game.**

```bash
# From the lalalazy repo root, on a feature branch:
cd src/ArmoireAutoFill
DALAMUD_HOME=$HOME/.xlcore/dalamud/Hooks/dev ~/.dotnet/dotnet build --configuration Release

# Commit + push the branch. Once verified, merge to main and bump
# pluginmaster.json so Dalamud's custom-repo install picks up the new build.
```

In-game test once shipped via the custom repo:
```
/xlplugins → Disable then Enable ArmoireAutoFill
/armoire   → opens the main window
```

## Architecture
```
Plugin.cs                       → Entry point (parameterless ctor, [PluginService] DI)
Configuration.cs                → Plugin settings + ArmoireItemIds cache
Models/                         → ArmoireItem, DungeonInfo, OwnershipStatus, GearSlot
Data/ArmoireGearDatabase.cs     → Builds full database at startup from:
                                    * Lumina Cabinet sheet  (every armoire-eligible item)
                                    * Lumina Item sheet     (names + slot)
                                    * LuminaSupplemental    (item → ContentFinderCondition)
Logic/CabinetObserver.cs        → Hooks Cabinet + MiragePrismPrismBox addon refresh;
                                  snapshots UIState.Cabinet.IsItemInCabinet results
                                  into Configuration.ArmoireItemIds.
Logic/InventoryScanner.cs       → Scans inventory + armory chest, then consults the
                                  CabinetObserver cache to produce a 3-state ownership.
Windows/MainWindow.cs           → Dungeon-grouped ImGui table with snapshot status.
Windows/ConfigWindow.cs         → Settings (auto-scan toggle).
```

## Tech Stack
- **Dalamud API Level**: 15
- **Framework**: net10.0-windows10.0.26100.0
- **Key deps**:
  - ECommons 3.2.0.9 (Svc.Data, Svc.AddonLifecycle, Svc.Log)
  - LuminaSupplemental.Excel 4.3.1 (community drop tables; GPL-3.0)
- **Inventory**: `FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()` (unsafe)
- **Cabinet**: `FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance()->Cabinet`

## Cabinet observation — what you need to know
- `Cabinet.IsCabinetLoaded()` returns true only when the game has loaded armoire data from the server — i.e. while the armoire UI is open at an inn. Outside that window, every `IsItemInCabinet` call returns false. That's why the plugin caches results.
- The addon names hooked for snapshot triggers are:
  - `Cabinet` — the armoire UI itself
  - `MiragePrismPrismBox` — the Glamour Dresser (handy fallback since the dresser and armoire are co-located at inns)
- Each snapshot iterates the entire Cabinet excel sheet (~500 rows) and saves the resulting item ID list to `Configuration.ArmoireItemIds`. SetEquals is used to skip the disk write if nothing changed.

## LuminaSupplemental drop tables
`ArmoireGearDatabase.BuildItemToCfcIndex` joins five embedded CSVs:
- `DungeonChest` + `DungeonChestItem` — regular dungeon chests (chest entries carry the CFC, item entries reference a chest by ChestId).
- `DungeonBossChest` — boss reward chests (item + CFC in one row).
- `DungeonBossDrop` — boss drops (totems, cards).
- `DungeonDrop` — generic dungeon drops (minions, mounts, orchestrions, cards).

Each item can map to multiple dungeons; the database materializes one ArmoireItem per (item × dungeon) so the same gear piece will show under each dungeon that drops it.

Items with no LuminaSupplemental mapping fall into a synthetic `"Source unknown"` DungeonInfo (cfcId = 0) which sorts to the bottom of the table.

## Verification
```bash
cd src/ArmoireAutoFill
DALAMUD_HOME=$HOME/.xlcore/dalamud/Hooks/dev ~/.dotnet/dotnet build --configuration Release
# Must complete with 0 errors, 0 warnings.
```
