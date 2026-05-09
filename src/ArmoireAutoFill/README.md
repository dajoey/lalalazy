# Armoire Auto-Fill (Archived)

Dalamud plugin that scans your inventory and armory chest for armoire-compatible dungeon gear, showing what pieces you're missing from each dungeon.

**Status: Archived** — No longer actively maintained. Code retained for reference.

## Features

- Scans inventory, armory chest, and equipped gear for armoire-compatible items
- Queries armoire database to check which pieces have been stored
- Shows missing pieces per dungeon in a sortable table
- Dungeon gear database covering levels 31+

## Commands

| Command | Description |
|---------|-------------|
| `/armoire` | Open the main scanner window |
| `/armoireconfig` | Open configuration |

## Technical Notes

- Built on **net10.0-windows10.0.26100.0** with Dalamud SDK
- Uses Lumina Excel data to detect armoire-compatible gear
- Database stored as embedded JSON (`Data/armoire_gear.json`)

## Building

```bash
dotnet build --configuration Release
```

## Credits

Original plugin by **dajoey**. Built from scratch using the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud).
