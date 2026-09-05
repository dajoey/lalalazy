# Changelog

## v0.1.0.0 (2026-09-05)

### Added

- First release. LazyRetainerLive reads the logged-in character's LIVE retainer table (the same in-memory data AutoRetainer uses to drive its own countdowns) and serves it as JSON on `http://127.0.0.1:10504/retainers`, so the ffxiv dashboard's retainer panel can show venture countdowns that update the moment a venture is collected instead of whenever AutoRetainer next saves its config file.
- When you are not logged in (or the live table has no data yet) the endpoint answers HTTP 503, which tells the dashboard relay to fall back to AutoRetainer's file. Loopback only - the port is never reachable from other machines.

### Notes

- Companion of AutoRetainer - it reads, it never writes, assigns, or collects anything. Settings: Enabled (default on) and loopback port (default 10504). Commands: `/lazyretainerlive` opens settings, `/lazyretainerlive debug` dumps listener + snapshot state, `/lazyretainerlive changelog` reopens this popup.
- Why this exists: AutoRetainer's `DefaultConfig.json` (which feeds the dashboard today) is saved only at AutoRetainer's own save points, so its venture rows can be tens of minutes stale while ventures are actually completing. The countdown for the character you are logged in as lives in the game's memory - this plugin is a small read-only window onto it.
