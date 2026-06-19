# Changelog

## v0.1.1.0 (2026-06-18)

### Changed
- **Low-food warning replaces low-time warning.** The alert now fires on how much of the food you're eating remains in your inventory, not on time left on the Well Fed buff. This is what was originally intended. `FoodService.CheckWarning` now counts the active food (or the food it would auto-select for your job) and warns once when it drops to or below the threshold, re-arming after you restock.
- **New config: "Warn when food left is at or below" count slider** (default 3), replacing the old minutes slider. `Configuration.WarningThresholdCount` replaces `WarningThresholdMinutes`. `ConfigWindow.cs` section renamed to "Low-Food Warning".

### Fixed
- **Now eats in deep dungeons.** Palace of the Dead, Heaven-on-High and Eureka Orthos (all `TerritoryIntendedUse == 31`) were excluded from the combat-duty allow-list, so auto-eat silently did nothing there. Added Deep Dungeon to `FoodService.CombatDutyIntendedUses`.

### Notes
- Refresh-before-expiry (auto re-eat to extend the buff) is unchanged and still time-based — only the *warning* moved to a count.

## v0.1.0.0 (2026-06-18)

### Initial Release

- **Auto-eat in combat duties**: Automatically consumes food when entering or during combat duties (dungeons, raids, trials, alliance raids, criterion, variant dungeons). Excludes Diadem, field operations, deep dungeons, Gold Saucer, and overworld.
- **Per-job food selection**: Configure a specific food item per job, or use auto-select mode. Switch jobs in-game to edit different profiles.
- **Auto-select engine**: Scores all food items in your inventory against the optimal stat priorities for your current job (Tenacity for tanks, Crit/Det for melee, Piety/Det for healers, etc.) and picks the best one.
- **Fallback to auto-select**: If your manually selected per-job food isn't in inventory, optionally fall back to auto-select.
- **Low-time warning**: Plays a sound and shows a chat notification when your food buff remaining time drops below a configurable threshold. Fires once per food session.
- **Refresh threshold**: Automatically re-eats food to extend the duration when remaining time drops below a configurable threshold (default 5 minutes). Food caps at 30 minutes.
- **Configurable duty gating**: Option to only auto-eat in combat duties (default on).
- **Debug command**: `/lazyfoodbuff debug` shows current state, territory info, active food, and top 5 recommended foods for your job.
