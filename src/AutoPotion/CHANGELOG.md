# Changelog - AutoPotion

## v0.2.5.0 (2026-09-05)

- New per-job option: "Auto-use Echo Drops when silenced". When it is on and Silence lands (mob silence spells, AoE silences, deep dungeon traps), AutoPotion uses Echo Drops from inventory to cure it automatically. Item use is not blocked by Silence, so the cure works exactly when nothing else can.
- OFF by default like every option, and per job like the rest: tick the new box in the settings window for the jobs it should apply to. With it off (the default) nothing changes - not even a status check.
- It reuses the same machinery as the potions: HQ Echo Drops are preferred over normal ones, the shared 750 ms cooldown after any potion use applies, and a Silence that arrives during that cooldown is cured on the next tick.
- If the diagnostic tap is enabled, the cure logs as an `e` event; when it cannot fire the near-miss reasons are `edblocked` (on cooldown / blocked) or `ednostock` (none in the bags).
- No change to potion behaviour. HP/MP/regen thresholds, the overshoot guard, the deep dungeon Rehabilitation lockout and the in-combat / in-duty gates all work exactly as before.

## v0.2.4.0 (2026-09-05)

- Added an optional "Log potion decisions" diagnostic, OFF by default. Turn it on with `/autopotion telemetry on` or the new Advanced / Diagnostics section of the settings window, and AutoPotion writes one line to its plugin log every time it uses a potion, plus a rate-limited line when a threshold was crossed and nothing fired.
- Purpose: the log line records the HP%, the MP%, the threshold that was in force and the job, so a threshold can be verified after the fact instead of guessed at. Nothing leaves the machine - the lines only go to the local Dalamud plugin log.
- The "nothing fired" lines say WHY in a short code: `hpover` (a potion was available but the heal would have been wasted), `hpblocked` / `mpblocked` / `rgblocked` (on cooldown or blocked by a status), `hpnostock` / `mpnostock` / `rgnostock` (none left in inventory) and `rgrehab` (Rehabilitation still up in a deep dungeon).
- These lines are heavily rate-limited on purpose: a repeated state logs once, not once per frame. Measured against 214 minutes of real play the near-miss lines come out under one line per minute, so the log stays readable.
- No change to potion behaviour. Thresholds, per-job profiles, the overshoot guard, the deep dungeon Rehabilitation lockout and the in-combat / in-duty gates all work exactly as before, and with the setting off (the default) the plugin does not even build a log line.

## v0.2.3.0 (2026-09-05)

- Added the in-game "What's new" popup. After AutoPotion updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/autopotion changelog` any time to reopen it.
- No change to potion behaviour: thresholds, per-job profiles and the in-combat / in-duty gates all work exactly as before.

## [0.2.2.1] - 2026-07-02
### Fixed
- **Plugin icon now shows in the Dalamud installer.** The manifest (`AutoPotion.json`) had no
  `IconUrl`, so installed copies displayed the "?" placeholder. Added the LalaImages icon URL.

### Notes
- No behavior changes. Part of the 2026-07-02 lalalazy repo cleanup pass.
