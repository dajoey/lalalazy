# Changelog

## v0.1.1.0 (2026-09-07)

### Fixed
- The window no longer shows a bare list of slots with "no hint" under every row. The weekly hint/theme/dye data comes from fashionreportxiv.com, whose payload is formatted differently than the plugin assumed; the data downloaded successfully but every field silently failed to be read, so the window looked half-built while the log said everything was fine. All fields now bind correctly, and when a data source fails to load the window says which one and why instead of showing an empty table (files: `Core/RemoteDataSource.cs`, `FashionService.cs`, `ReportWindow.cs`)
- A single unknown item id in the crowdsourced data no longer aborts the whole weekly rebuild. One junk id (1010533) threw while names were being looked up and took every hint, dye and candidate down with it; unknown ids now fall back to showing the raw item number (file: `Adapters/SheetAdapter.cs`, `WarmItemNames`)
- The settings window's data-status line now names each source separately instead of one combined "data loaded" that stayed green while the hint source had failed (file: `ConfigWindow.cs`)

### Notes
- Verified offline against week 449's real downloaded payload (theme "Hunter from the Far East": four hints, six dye slots, base 70, the published easy-100 set scores exactly 100). The regression fixture replays the actual bytes from the live site through the same parsing path the plugin uses (file: `tests/LazyFashionReport.Harness`)
- If the window still says "MISSING" for a source, /lfr refresh re-downloads; cached data from earlier sessions is used when a download fails (file: `Core/RemoteDataSource.cs`)

## v0.1.0.0 (2026-09-06)

### Added
- First testing release: a Fashion Report assistant for the Gold Saucer. Open the in-game Fashion Report window (or type /lfr) and LazyFashionReport shows, for every gear slot: the week's hint, the exact +2 dye and the +1 shade family for left-side slots, and the top candidate items from the crowdsourced database (xivstats) filtered down to items actually owned - bags, glamour dresser and armoire included (files: Core/ScoreMath.cs, Core/Predictor.cs, Adapters/CrowdDataAdapter.cs)
- A live score predictor: as the worn outfit changes, the window shows "scores N - needs +X for 80" per slot and in total, computed from the verified scoring rules (10/8 base per unhinted slot, hinted slots 2 + 8/6 for a correct item, exact dye +2 / same shade +1 on left-side slots) (file: Core/ScoreMath.cs SlotScore / WeeklyBase)
- The weekly base is computed from where the hints landed, never hardcoded: 68 when all four hints are main gear slots, 70 when one is an accessory (verified against week 449's live data) (file: Core/ScoreMath.cs WeeklyBase)
- /lfr opens the assistant window, /lfr refresh re-downloads the crowd data, /lfr changelog shows what's new (file: Plugin.cs OnCommand)
- Settings: auto-open with the Fashion Report window, owned-only candidate filter (on by default), candidates-per-slot limit (file: Configuration.cs, ConfigWindow.cs)

### Notes
- Item candidates come from xivstats.com's crowdsourced database and exact weekly dyes from fashionreportxiv.com; both are cached locally and the plugin degrades gracefully to whatever loaded last when a fetch fails (file: Core/RemoteDataSource.cs)
- The predictor is offline-verified against week 449's published results: base 70, easy100 = the four gold items, easy80 = Brand-new Gloves + Abyssal Blue on the head slot (file: tests/LazyFashionReport.Harness)
- v1 does not change gear, apply dyes or submit anything: it only reads and advises (file: Plugin.cs)
