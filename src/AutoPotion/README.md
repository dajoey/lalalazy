# AutoPotion

Dalamud plugin that automatically uses potions at configurable HP thresholds.

## Features

- **Auto uses high-tier HP potions** — selects the best HP potion in your inventory (HQ > NQ, highest grade first)
- **Deep dungeon support** — auto-uses dungeon-specific regen potions:
  - Sustaining Potion (Palace of the Dead)
  - Eurekan Potion (Eureka Orthos)
  - Orthos Potion (Heaven-on-High)
  - Pilgrim's Potion (multiple deep dungeons)
- **Configurable thresholds** via `/autopotion`

## Supported Potions

| Potion Type | Grades |
|-------------|--------|
| HP Potions | Grades 2–8 (HQ and NQ) |
| Deep Dungeon Regen | Sustaining, Eurekan, Orthos, Pilgrim's |

## Commands

| Command | Description |
|---------|-------------|
| `/autopotion` | Open configuration window |
| `/pot` | Toggle auto-potion on/off |

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | On | Master toggle |
| HP Threshold % | 75 | Use potion when HP falls below this % |
| DD Potion Threshold % | 80 | Use deep dungeon regen potion below this % |

## Building

Requires .NET 10 SDK and Dalamud.

```bash
dotnet build --configuration Release
```

## Credits

Original plugin by **dajoey**. Built from scratch using the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud).
