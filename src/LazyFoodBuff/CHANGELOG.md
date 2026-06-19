# Changelog

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
