## v0.1.5.5 (2026-11-15) [testing]
- The window was polished for the real in-game font and scale: text no longer clips or sits against card borders.
- The settings window scrolls as one page with even side margins, so toggles, buttons and values sit well inside their cards.
- On wide windows the two columns are balanced: Diagnostics sits under Food selection, leaving no large empty card at the end of the page.
- Job and food pickers are bordered fields with a chevron; the list opens with the search box, then None or Default, then the entries.
- The title bar has a bordered brand chip, a fixed-width status pill showing only the state word and a drawn close button with a tooltip; the footer Stop button is red-tinted.

## v0.1.5.4 (2026-11-15) [testing]
- The settings window has been redesigned as a responsive card layout: a custom header with a close button instead of the native title bar, no outer scrollbar, switch-style toggles, live requirements and counts, cards that adapt to the window size, and a persistent footer with the live state and a Stop button that only turns the automation off. Food selection, fallback behavior, saved settings and automation are unchanged.

## v0.1.5.3 (2026-11-14) [testing]
- The plugin windows have been rebuilt on the shared LalaUiKit: one consistent layout with themed section headers, unified spacing and colour accents. All settings and behaviour are unchanged, and existing configuration loads as-is.

## v0.1.5.2 (2026-11-10) [testing]
- Rebuilt against the Dalamud 15.0.4.0 payload for game patch 7.58. No functional changes.

# Changelog

## v0.1.5.1 (2026-10-03) [testing]
### Added
- **Quick controls for the Lazy Hub window.** The plugin now offers auto-eating, "Only in combat duties", the minutes of food left before eating again and the low-food warning with its count to Lazy Hub (`/lazy`) over Dalamud IPC, so they can be changed from one window next to the other lalalazy plugins. Each change does exactly what the matching setting in the plugin's own window does.
### Notes
- Testing channel only; the production channel is unchanged.
- Without Lazy Hub installed the plugin behaves exactly as before.

## v0.1.5.0 (2026-09-21)

### Changed
- **"Only eat in combat duties" now covers Crucible of the Unbroken.** The five boards count as combat duties, and so does the Bentbranch Meadows entry area while playing Beastmaster (the one job that can enter), so the meal is eaten before a run starts. Eating before entry matters: a meal eaten inside does not refill HP to the raised maximum.

## v0.1.4.0 (2026-09-05)

### Added
- Optional decision diagnostics, off by default. Turn on "Log food decisions" (settings window, Advanced / Diagnostics) or run `/lazyfoodbuff telemetry on`, and every time the plugin settles on a food for the current job it writes one line to the plugin log: the food it picked, its score, and the runner-ups it beat â€” plus a second line when that food is actually eaten. The point is the runner-ups: "picked X at 41.20 over Y at 40.80" is the shape of evidence that can eventually say whether the auto-select weights are right. Nothing leaves the machine; the lines only go to the local plugin log.

### Notes
- Honest caveat: this is a data-collection investment, not an answer. The outcome side (what a food actually did to damage) needs weeks of encounter data before it says anything statistically â€” the plugin log will quietly accumulate the decision half in the meantime.
- Nothing about food selection changed. When the toggle is off, the only cost is one boolean read.

## v0.1.3.1 (2026-09-05)

### Fixed
- The "What's new" popup now opens after this very update. 0.1.3.0 stayed silent the first time it ran because it had no record of the last version shown; the gate now tells an updated plugin (settings file already present) apart from a brand-new install (none yet) and only the latter stays quiet (`ChangelogGate.Options.ExistingInstall`, read from `pi.ConfigFile.Exists`.

### Notes
- Nothing else changed. The notes can be reopened later with `/lazyfoodbuff changelog`.

## v0.1.3.0 (2026-09-05)

### Added
- In-game "What's new" popup. After every update, the first time the character is logged in and out of combat, LazyFoodBuff opens a window with the release notes for every version since the last one shown. It appears once per update; pressing "Got it" (or closing it) keeps it quiet until the next update.
- `/lazyfoodbuff changelog` (or `whatsnew`) reopens the release notes at any time.
- "Open changelog on GitHub" button in the popup links to the full CHANGELOG for this plugin.

### Notes
- This is the pilot of a standing rule for every lalalazy plugin: the popup code is shared across plugins and each plugin embeds its own release notes at build time. New config field `Configuration.LastSeenChangelogVersion`; on the first build carrying the feature it records the running version silently and does not open.
- The popup never opens during combat, inside a duty, while zoning, or in a cutscene - it waits until the character is free.
- `/lazyfoodbuff` with no argument still opens these settings.
