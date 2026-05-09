# AutoKupo

Dalamud plugin that automates **Kupo of Fortune** card turn-ins at Lizbeth in the Firmament (Ishgard Restoration).

## How It Works

1. Stand near **Lizbeth** in the Firmament
2. Type `/autokupo` to start
3. The plugin automatically clicks through the card selection and turn-in dialogs
4. Randomly selects the right-side option each time
5. Only activates when Lizbeth is targeted/nearby (safety check)

## Commands

| Command | Description |
|---------|-------------|
| `/autokupo` | Toggle auto-turnin on |
| `/autokupo on` | Start auto-turnin |
| `/autokupo off` | Stop auto-turnin |
| `/autokupo stop` | Stop auto-turnin |
| `/autokupoconfig` | Open configuration window |

## Features

- Automatic Kupo of Fortune card selection
- Lizbeth proximity detection (won't fire if you're not near her)
- Random right-side selection for variety
- Configurable delays between actions

## Building

Requires .NET SDK 10 and Dalamud.

```bash
dotnet build --configuration Release
```

## Credits

Original plugin by **dajoey**. Built from scratch using the [Dalamud plugin framework](https://github.com/goatcorp/Dalamud).
