# Armoire Auto-Fill

Dalamud plugin that scans your inventory, armory chest, and the armoire itself for armoire-compatible gear, showing what pieces you're still missing from each dungeon.

## Features

- Detects armoire-eligible gear from the in-game Cabinet sheet (covers every expansion)
- Three-state ownership: in inventory, in armoire, or missing
- Per-dungeon grouping using community-curated drop tables (LuminaSupplemental)
- Items with no known drop source are bucketed under "Source unknown"

## Commands

| Command | Description |
|---------|-------------|
| `/armoire` | Open the main scanner window |

The config window is available from the plugin installer's gear icon, or from inside the main window.

## How armoire detection works

The game only exposes the armoire's contents to plugins while the armoire UI is loaded. The plugin hooks the `Cabinet` and `MiragePrismPrismBox` (Glamour Dresser) addons; the first time you open either at an inn, the plugin takes a snapshot of what's stored and caches the result. Subsequent sessions reuse that cache so you don't have to revisit the armoire just to see what's in it.

The status line in the main window shows when the last snapshot was taken.

## Building

```bash
cd src/ArmoireAutoFill
dotnet build --configuration Release
```

Requires the Dalamud SDK and the `DALAMUD_HOME` environment variable on Linux (point it at your `~/.xlcore/dalamud/Hooks/dev/` directory).

## Credits

Original plugin by **dajoey**. Built on the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud). Drop-table data ships via [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental) (GPL-3.0, by Critical-Impact). Cabinet observation technique inspired by [seventhxiv/Collections](https://github.com/seventhxiv/Collections).
