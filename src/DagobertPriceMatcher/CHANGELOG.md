# Changelog — DagobertPriceMatcher

## v1.12.0.6 (2026-07-02)

### Changed
- **Manifest `RepoUrl` now points at the fork.** `DagobertPriceMatcher.json` previously pointed
  at upstream `SHOEGAZEssb/Dagobert`; it now points to `https://github.com/dajoey/lalalazy`, so
  the repo link shown in the Dalamud installer goes to the fork that actually ships this build.

- **Plugin icon now shows in the Dalamud installer.** The manifest's `IconUrl` was empty,
  so installed copies displayed the "?" placeholder. Now points at the LalaImages icon.

### Notes
- No source/behavior changes. Part of the 2026-07-02 fork-branding cleanup pass across lalalazy forks.
- The stale upstream manifest `Dagobert.json` remains in-tree (not packaged) to keep nightly diff-apply merges quiet.

## v1.12.0.5 (2026-06-18)

### Added
- **Cross-platform notification option.** New `UseDalamudNotifications` config — when enabled,
  AutoPinch alerts print to Dalamud chat instead of Windows `System.Speech` TTS, so the plugin
  is usable on Linux/Wine. Windows TTS is now wrapped in try/catch with a chat fallback if the
  synthesizer throws. `IClientState` injected for platform awareness. Files: `AutoPinch.cs`,
  `Configuration.cs`, `Plugin.cs`, `Windows/ConfigWindow.cs`.

### Fixed
- **Recovery of a broken automated patch.** Added the `UseDalamudNotifications` property that the
  new code referenced but never defined (compile error), closed an unbalanced `if` block in
  `ConfigWindow.cs` (CS1513), and fixed `\nr` tooltip newline typos.

## v1.12.0.4 (2026-06-15)

### Added
- **"Show inventory context menu entry" toggle** (on by default), synced from upstream Dagobert v1.14.1.0 (`114a95cc2`): new `ShowInventoryContextMenuEntry` config (`Configuration.cs`), an early-return guard in `OnContextMenuOpened` (`Plugin.cs`), and a checkbox in `Windows/ConfigWindow.cs`. Turn it off to remove Dagobert's right-click entry from inventory items.

### Notes
- `System.Speech` kept at 10.0.7 (our csproj pin; upstream's csproj moved to 10.0.9). No functional impact — our vendored runtime DLL is unchanged.
- Fork invariants preserved: exact price matching (0 undercut default), `/pricematch` command, DagobertPriceMatcher branding/version line.

## v1.12.0.3 (2026-06-12)

### Added
- **Per-item min/max price limits**, synced from upstream Dagobert v1.14.0.0 (`60e1ad1b4`): new `ItemPriceLimit` config model (`Configuration.cs`), per-item limit UI in `Windows/ConfigWindow.cs`, an inventory context-menu entry to add a limit (`Plugin.cs`), and `ItemNameResolver.cs` to map item names to IDs. Limits clamp the computed price in `AutoPinch.cs` after price matching.

### Changed
- Upstream range merged: `60e1ad1b4` (feature), `fec67b6bb` (packages.lock). The ECommons submodule bump (`b787ca261`) was NOT pulled — our vendored ECommons builds clean against the new code.
- `System.Speech` kept at 10.0.7 (our csproj pin; upstream's lock moved to 10.0.9).

### Notes
- Fork invariants preserved: exact price matching (0 undercut default), `/pricematch` command (upstream's `/dagobert` rename in the dispose path resolved to ours), DagobertPriceMatcher branding/version line in the csproj.

## v1.12.0.2 (2026-06-06)

### Added
- Optional **Universalis** data-center price source (`UniversalisClient.cs`, `UniversalisPriceProvider.cs`), synced from upstream Dagobert through v1.13.1.0.

### Fixed
- HQ price detection when many NQ listings are present.
- Deadlock when no market board entries are returned.
- Price cache now cleared when the price source changes; assorted `MarketBoardHandler` edge cases.

### Notes
- Fork behavior preserved: matches the lowest market-board price (0 undercut by default). Adjustment messages keep the "Matching" wording and now append the price source. Merge conflicts in `AutoPinch.cs` and `Communicator.cs` were resolved to keep our matching behavior while adopting upstream's source indicator.
