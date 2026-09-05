# Changelog - AutoPotion

## v0.2.3.0 (2026-09-05)

- Added the in-game "What's new" popup. After AutoPotion updates, its changelog now opens once inside the game so you can see what changed without going to GitHub. It waits until you are logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/autopotion changelog` any time to reopen it.
- No change to potion behaviour: thresholds, per-job profiles and the in-combat / in-duty gates all work exactly as before.

## [0.2.2.1] - 2026-07-02
### Fixed
- **Plugin icon now shows in the Dalamud installer.** The manifest (`AutoPotion.json`) had no
  `IconUrl`, so installed copies displayed the "?" placeholder. Added the LalaImages icon URL.

### Notes
- No behavior changes. Part of the 2026-07-02 lalalazy repo cleanup pass.
