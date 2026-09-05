# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/repo-icon.png)

# lalalazy — FFXIV Dalamud Plugins

A collection of Dalamud plugins for Final Fantasy XIV, maintained by [dajoey](https://github.com/dajoey).

## Plugins

| Plugin | Description | Status |
|--------|-------------|--------|
| **Gluttony Combo** | XIVCombo for very lazy players. Condenses combos and mutually exclusive abilities onto a single button — and then some. Fork of Wrath Combo. | Active |
| **PvP Solver** | Auto-rotation for PvP combat. All jobs. Activates automatically in PvP zones. Designed to run alongside Gluttony Combo. | Active |
| **Lazy Market Companion** | Auto-lists your always-sell items through your retainers (bags + retainer inventory, configurable stack size / reserve), matches board prices, optional AutoRetainer hook. Successor to Dagobert Price Matcher. | Testing |
| **AutoPotion** | Auto-uses HP potions and deep dungeon regen potions at configurable HP thresholds. | Active |
| **Armoire Auto-Fill** | Per-dungeon view of armoire-eligible gear pieces you're still missing. Detects in-armoire, in-inventory, and equipped state. | Active |
| **Lazy WT Math** | Adds row probabilities to the Wondrous Tails display along with the average probability of what would happen if you shuffled. Fork of EzWondrousTails. | Active |
| **Lazy Currency Spender** | Finds the best way to spend your tomestones, scrips, and Poetics — backed by live Universalis market prices. Surfaces tomestone gear and missing collectibles you can buy. Fork of CurrencySpender. | Active |
| **Lazy Fate Automation** | Fully automated FATE grinding utilizing vnavmesh, lifestream, and Gluttony Combo. | Active |
| **Lazy Skyward Tracker** | Track your Skybuilders' (Skyward) points for all jobs toward the Pteranodon mount. | Active |
| **LazyFoodBuff** | Auto-eats food in combat duties incl. deep dungeons. Per-job food selection, auto-select based on best stats, and a low-food (running-out) warning. | Active |
| **LazyOccultCrescent** | Occult Crescent field companion covering South Horn and North Horn. Treasure and Fortune Carrot radar with optimal routes, live FATE/CE tracking, aethernet teleports, currency and EXP per hour, and an optional FATE/CE/mob farm loop. Fork of BOCCHI (AGPLv3). | Active |
| **LazyCrafter** | Catalogs every recipe you can craft, prices it with Universalis, and hands the missing materials to Artisan / GatherBuddyReborn / AutoRetainer / Lifestream. | Active |
| **Lazy Fish Sitter** | Sits you down while you fish. Checks every few seconds while fishing and runs /sit if you are standing. Never re-sits once you are seated (ground, chair, or pose). | Testing |

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

### Lazy Market Companion
Original plugin, successor to Dagobert Price Matcher (the Dagobert fork was retired from this repo on 2026-09-05). Carries Dagobert's price-matching engine (AGPLv3, credit SHOEGAZEssb) and adds the Auto-Market list, listing from bags and retainer inventory via InventoryManager.MoveToRetainerMarket, and the AutoRetainer postprocess hook. Planner is Dalamud-free and unit-tested in 	ests/LazyMarketCompanion.Harness.

### AutoPotion
Original plugin by dajoey. Built from scratch using the Dalamud plugin SDK.

### Armoire Auto-Fill
Original plugin by dajoey. Reads the in-game Cabinet sheet for the canonical armoire-eligible item list, joined with [LuminaSupplemental.Excel](https://github.com/Critical-Impact/LuminaSupplemental) (GPL-3.0, by Critical-Impact) for dungeon drop attribution. Cabinet observation technique inspired by [seventhxiv/Collections](https://github.com/seventhxiv/Collections).

## Build

Each plugin builds with the Dalamud SDK (.NET 10 SDK required). See individual plugin READMEs in `src/` for details.

```bash
cd src/GluttonyCombo && dotnet build --configuration Release
cd src/PvPSolver && dotnet build --configuration Release
cd src/LazyMarketCompanion && dotnet build --configuration Release
cd src/AutoPotion && dotnet build --configuration Release
cd src/ArmoireAutoFill && dotnet build --configuration Release
cd src/LazyWTMath && dotnet build --configuration Release
cd src/LazyCurrencySpender && dotnet build --configuration Release
cd src/LazyFateAutomation && dotnet build --configuration Release
cd src/LazySkywardTracker && dotnet build --configuration Release
cd src/LazyGearCollector && dotnet build --configuration Release
cd src/LazyFoodBuff/LazyFoodBuff && dotnet build --configuration Release
cd src/LazyOccultCrescent/LazyOccultCrescent && dotnet build --configuration Release
cd src/LazyCrafter && dotnet build --configuration Release
cd src/LazyFishSitter && dotnet build --configuration Release

```

### LazyCrafter
Original plugin by dajoey. Catalogs every craftable recipe, prices it with Universalis, and dispatches the missing-material work to [Artisan](https://github.com/PunishXIV/Artisan), GatherBuddyReborn, [AutoRetainer](https://github.com/FFXIV-CombatReborn/ARControl) and Lifestream via their IPC / reflected interfaces.

## License

Individual plugins retain the licenses of their origin projects. See `COPYING` / `LICENSE` files in each `src/` subdirectory.