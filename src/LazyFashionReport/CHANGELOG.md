# Changelog

## v0.1.0.0 (2026-09-06)

### Added
- First testing release: a Fashion Report assistant for the Gold Saucer. Open the in-game Fashion Report window (or type /lfr) and LazyFashionReport shows, for every gear slot: the week's hint, the exact +2 dye and the +1 shade family for left-side slots, and the top candidate items from the crowdsourced database (xivstats) filtered down to items you actually own - bags, glamour dresser and armoire included (files: Core/ScoreMath.cs, Core/Predictor.cs, Adapters/CrowdDataAdapter.cs)
- A live score predictor: as your worn outfit changes, the window shows "scores N - needs +X for 80" per slot and in total, computed from the verified scoring rules (10/8 base per unhinted slot, hinted slots 2 + 8/6 for a correct item, exact dye +2 / same shade +1 on left-side slots) (file: Core/ScoreMath.cs SlotScore / WeeklyBase)
- The weekly base is computed from where the hints landed, never hardcoded: 68 when all four hints are main gear slots, 70 when one is an accessory (verified against week 449's live data) (file: Core/ScoreMath.cs WeeklyBase)
- /lfr opens the assistant window, /lfr refresh re-downloads the crowd data, /lfr changelog shows what's new (file: Plugin.cs OnCommand)
- Settings: auto-open with the Fashion Report window, owned-only candidate filter (on by default), candidates-per-slot limit (file: Configuration.cs, ConfigWindow.cs)

### Notes
- Item candidates come from xivstats.com's crowdsourced database and exact weekly dyes from fashionreportxiv.com; both are cached locally and the plugin degrades gracefully to whatever loaded last when a fetch fails (file: Core/RemoteDataSource.cs)
- The predictor is offline-verified against week 449's published results: base 70, easy100 = the four gold items, easy80 = Brand-new Gloves + Abyssal Blue on the head slot (file: tests/LazyFashionReport.Harness)
- v1 does not dress you, dye you, or submit anything: it only reads and advises (file: Plugin.cs)
