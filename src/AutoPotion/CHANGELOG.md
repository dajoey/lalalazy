# Changelog - AutoPotion

## v0.2.4.0 (2026-09-05)

- Added an optional "Log potion decisions" diagnostic, OFF by default. Turn it on with `/autopotion telemetry on` or the new Advanced / Diagnostics section of the settings window, and AutoPotion writes one line to its plugin log every time it uses a potion, plus a rate-limited line when a threshold was crossed and nothing fired.
- Why you would want it: the log line records the HP%, the MP%, the threshold that was in force and which job you were on, so you can check afterwards whether a threshold is set where you think it is instead of guessing. Nothing leaves your PC - the lines only go to your own local Dalamud plugin log.
- The "nothing fired" lines say WHY in a short code: `hpover` (a potion was available but the heal would have been wasted), `hpblocked` / `mpblocked` / `rgblocked` (on cooldown or blocked by a status), `hpnostock` / `mpnostock` / `rgnostock` (none left in your bags) and `rgrehab` (Rehabilitation still up in a deep dungeon).
- These lines are heavily rate-limited on purpose: a repeated state logs once, not once per frame. Measured against 214 minutes of real play the near-miss lines come out under one line per minute, so the log stays readable.
- No change to potion behaviour. Thresholds, per-job profiles, the overshoot guard, the deep dungeon Rehabilitation lockout and the in-combat / in-duty gates all work exactly as before, and with the setting off (the default) the plugin does not even build a log line.

## v0.2.3.0 (2026-09-05)

- Added the in-game "What's new" popup. After AutoPotion updates, its changelog now opens once inside the game so you can see what changed without going to GitHub. It waits until you are logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/autopotion changelog` any time to reopen it.
- No change to potion behaviour: thresholds, per-job profiles and the in-combat / in-duty gates all work exactly as before.

## [0.2.2.1] - 2026-07-02
### Fixed
- **Plugin icon now shows in the Dalamud installer.** The manifest (`AutoPotion.json`) had no
  `IconUrl`, so installed copies displayed the "?" placeholder. Added the LalaImages icon URL.

### Notes
- No behavior changes. Part of the 2026-07-02 lalalazy repo cleanup pass.
