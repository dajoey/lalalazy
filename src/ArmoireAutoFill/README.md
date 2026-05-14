# ![](https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/armoire-icon.png)

# Armoire Auto-Fill

Dalamud plugin that lists every armoire-eligible dungeon drop in the game and shows whether each piece is in your inventory, in your armoire, or still missing.

## Features

- Reads the in-game Cabinet sheet to enumerate every armoire-eligible item (every expansion)
- Three-state ownership: in inventory / armory chest / equipped / saddlebag → "Inventory"; stored in armoire → "Armoire"; otherwise → "Missing"
- Per-dungeon grouping via [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental) drop tables; items with no known dungeon source are excluded from view
- Sort by level, hide completed dungeons by default, toggle to show owned items too

## Commands

| Command | Description |
|---------|-------------|
| `/armoire` | Open the main window |

The config window is available from the plugin installer's gear icon, or from inside the main window.

## How armoire detection works

Two sources, in order of preference:

1. **Live API** (`UIState.Cabinet.IsItemInCabinet`) — full row coverage, including current-expansion items. Only usable while the armoire UI is loaded, which happens when you open an armoire NPC at an inn. The plugin hooks the `Cabinet` and `MiragePrismPrismBox` addons to snapshot whenever the UI refreshes.
2. **Bitmap fallback** (`ItemFinderModule.CabinetItemUnlockBits`) — auto-populates on login, but is capped at 4000 cabinet rows (FixedSizeArray125<uint>), so current-expansion items past row 4000 won't show. Used only as a cold-start fallback before the live API is available.

Once the live API has run at least once this session, the plugin sticks with that snapshot rather than downgrading back to the bitmap. The cached result is persisted across sessions.

## Building

```bash
cd src/ArmoireAutoFill
dotnet build --configuration Release
```

Requires the Dalamud SDK. On Linux set `DALAMUD_HOME` to your `~/.xlcore/dalamud/Hooks/dev/` directory.

## Credits

Original plugin by **dajoey**. Built on the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud). Drop-table data ships via [LuminaSupplemental.Excel](https://github.com/Critical-Impact/LuminaSupplemental) (GPL-3.0, by Critical-Impact). Cabinet observation technique inspired by [seventhxiv/Collections](https://github.com/seventhxiv/Collections).
