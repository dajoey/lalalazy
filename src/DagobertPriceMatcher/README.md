# Dagobert Price Matcher

Dalamud plugin that automatically adjusts your market board prices to **match** the lowest offer (instead of undercutting).

## How It Works

When you adjust prices via the retainer market board, Dagobert sets your price to the current lowest offer. By default it matches exactly (0 gil difference). You can configure a negative offset to undercut if desired.

## Configuration

Use `/pricematch` to open the configuration window.

| Setting | Default | Description |
|---------|---------|-------------|
| Match Amount | 0 | Gil difference from lowest offer. Use negative to undercut. |
| Auto-pinch | Enabled | Automatically clicks the price adjustment buttons. |

## Building

Requires .NET 10 SDK and Dalamud.

```bash
# Clone with submodules
git clone --recurse-submodules <repo>
dotnet build --configuration Release
```

## Credits

**Forked from [Dagobert](https://github.com/SHOEGAZEssb/Dagobert)** by **SHOEGAZEssb**.

Licensed under **AGPLv3**. See [LICENSE.md](LICENSE.md).

### Key Changes from Upstream

- Default match amount changed from -1 (undercut by 1 gil) to **0** (exact price match)
- Author metadata updated
