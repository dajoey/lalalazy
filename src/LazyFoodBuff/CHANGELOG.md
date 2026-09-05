# Changelog

## v0.1.4.0 (2026-09-05)

### Added
- Optional decision diagnostics, off by default. Turn on "Log food decisions" (settings window, Advanced / Diagnostics) or run `/lazyfoodbuff telemetry on`, and every time the plugin settles on a food for your job it writes one line to the plugin log: the food it picked, its score, and the runner-ups it beat — plus a second line when that food is actually eaten. The point is the runner-ups: "picked X at 41.20 over Y at 40.80" is the shape of evidence that can eventually say whether the auto-select weights are right. Nothing leaves your PC; the lines only go to your own local plugin log.

### Notes
- Honest caveat: this is a data-collection investment, not an answer. The outcome side (what a food actually did to damage) needs weeks of encounter data before it says anything statistically — the plugin log will quietly accumulate the decision half in the meantime.
- Nothing about food selection changed. When the toggle is off, the only cost is one boolean read.

## v0.1.3.1 (2026-09-05)

### Fixed
- The "What's new" popup now opens after this very update. 0.1.3.0 stayed silent the first time it ran because it had no record of which version you saw last; the gate now tells an updated plugin (your settings file already exists) apart from a brand-new install (no settings yet) and only the latter stays quiet (`ChangelogGate.Options.ExistingInstall`, read from `pi.ConfigFile.Exists` in `Plugin.cs`).

### Notes
- Nothing else changed. If you want to see the notes again later: `/lazyfoodbuff changelog`.

## v0.1.3.0 (2026-09-05)

### Added
- In-game "What's new" popup. After every update, the first time you are logged in and out of combat, LazyFoodBuff opens a window with the release notes for every version since the one you last saw. It appears once per update; press "Got it" (or close it) and it stays quiet until the next update.
- `/lazyfoodbuff changelog` (or `whatsnew`) reopens the release notes at any time.
- "Open changelog on GitHub" button in the popup links to the full CHANGELOG for this plugin.

### Notes
- This is the pilot of a standing rule for every lalalazy plugin: the popup code is shared source under `src/Shared/LalaChangelog/` and each plugin embeds its own `CHANGELOG.md` at build time (`ChangelogGate.cs`, `ChangelogWindow.cs`, `Core/ChangelogParser.cs`). New config field `Configuration.LastSeenChangelogVersion`; on the first build carrying the feature it records the running version silently and does not open.
- The popup never opens during combat, inside a duty, while zoning, or in a cutscene - it waits until you are free.
- `/lazyfoodbuff` with no argument still opens these settings.
