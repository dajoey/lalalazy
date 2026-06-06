# Changelog — DagobertPriceMatcher

## v1.12.0.2 (2026-06-06)

### Added
- Optional **Universalis** data-center price source (`UniversalisClient.cs`, `UniversalisPriceProvider.cs`), synced from upstream Dagobert through v1.13.1.0.

### Fixed
- HQ price detection when many NQ listings are present.
- Deadlock when no market board entries are returned.
- Price cache now cleared when the price source changes; assorted `MarketBoardHandler` edge cases.

### Notes
- Fork behavior preserved: matches the lowest market-board price (0 undercut by default). Adjustment messages keep the "Matching" wording and now append the price source. Merge conflicts in `AutoPinch.cs` and `Communicator.cs` were resolved to keep our matching behavior while adopting upstream's source indicator.
