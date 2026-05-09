# Armoire Auto-Fill — AGENTS.md

## Overview
Dalamud plugin that scans player inventory for armoire-compatible dungeon gear (level 31+) and shows missing pieces.

## Build & Deploy
```bash
# Build and deploy
bash ~/ArmoireAutoFill/redeploy.sh

# Build only
cd ~/ArmoireAutoFill && ~/.dotnet/dotnet build --configuration Release

# In-game: /xlplugins -> Disable then Enable ArmoireAutoFill
# Command: /armoire (opens main window)
# Config: /armoire then gear icon, or OpenConfigUi in plugin installer
```

## Architecture
```
Plugin.cs              → Entry point (parameterless ctor, [PluginService] DI)
Configuration.cs       → Plugin settings (IPluginConfiguration)
Models/                → ArmoireItem, DungeonInfo, OwnershipStatus, GearSlot
Data/armoire_gear.json → Embedded resource: all known armoire-eligible dungeon gear
Data/ArmoireGearDatabase.cs → Loads JSON at startup, provides AllItems + DungeonSets
Logic/InventoryScanner.cs   → Uses FFXIVClientStructs InventoryManager to scan containers
Windows/MainWindow.cs       → ImGui table: dungeons → items, missing/owned toggle
Windows/ConfigWindow.cs     → Settings (auto-scan toggle)
```

## Tech Stack
- **Dalamud API Level**: 15
- **Framework**: net10.0-windows10.0.26100.0
- **Key deps**: ECommons 3.2.0.9 (pulls in FFXIVClientStructs transitively)
- **Inventory**: FFXIVClientStructs `InventoryManager.Instance()` — unsafe, scans 16 containers

## Adding New Gear Data
Edit `Data/armoire_gear.json`:
```json
{
  "dungeons": [
    {
      "cfcId": 14,
      "name": "Dungeon Name",
      "level": 32,
      "items": [
        {"id": 12345, "name": "Item Name", "slot": "Body"}
      ]
    }
  ]
}
```
- `cfcId` = ContentFinderCondition row ID (from Lumina sheet)
- `slot` = one of: Head, Body, Hands, Legs, Feet
- File is embedded as `ArmoireAutoFill.Data.armoire_gear.json`

## Known Limitations
- **No dungeon mapping for most items**: Only ARR dungeons (31-47) have item→dungeon mappings. HW+ items need community contribution.
- **No Armoire content scanning**: Only scans inventory/armory chest, not what's already in the armoire. Lumina Item struct lacks IsArmoireable property.
- **No auto-duty**: Phase 1 is scanner-only. Auto-duty planned for Phase 3+ (AutoDuty IPC integration).

## Verification
```bash
# Build must pass with 0 errors, 0 warnings
cd ~/ArmoireAutoFill && ~/.dotnet/dotnet build --configuration Release
```
