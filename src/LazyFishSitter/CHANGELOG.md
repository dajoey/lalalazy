# Changelog

## v0.1.0.0 (2026-09-05)

- New plugin: while the game's Fishing condition is active, checks every few seconds (default 2) and runs /sit when the player is not already seated (file: `FishSitService.cs`, `Tick`).
- Seated detection reads the local Character's Mode field - 12 (EmoteLoop) covers persistent emotes like /sit, 13 (InPositionLoop) covers chair/bench and pose loops - the same read SimpleHeels' EmoteIdentifier uses, so the plugin never re-sits someone already seated, on a chair, or holding a pose (file: `FishSitService.cs`, `IsInPostureLoop`).
- The /sit is sent through the game's own chat-entry path (UIModule ProcessChatBoxEntry), inlined and trimmed from XivCommon's MIT Chat.cs - the same code ECommons ships inside this repo (file: `ChatCommand.cs`).
- Safety valves: never re-sends the sit command more than once per 3 seconds, and skips while between areas, in events, quest events, cutscenes, combat, mid-emote, or jumping (file: `FishSitService.cs`, `Tick`).
- Config: enabled toggle, check interval 1-10s, and the sit command itself (default /sit, must start with a slash) (files: `Configuration.cs`, `ConfigWindow.cs`).
- Ships on the TESTING channel first. Use /lazyfishsitter for settings, /lazyfishsitter debug for a state dump in the Dalamud log.
