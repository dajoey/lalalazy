# Changelog

## v0.1.1.0 (2026-09-05)

- Fixed the sit/stand yo-yo: the seated check compared Character.Mode against raw bytes 12/13, which are RaceChocobo/TripleTriad in FFXIVClientStructs, so the player was never seen as seated and /sit was re-sent every check, and /sit while seated makes the character STAND (file: `FishSitService.cs`, `ReadPosture`; was `IsInPostureLoop`). Now compares against the `CharacterModes` enum (`EmoteLoop` = ground /sit, `InPositionLoop` = chair/bench/pose) - no raw bytes.
- Added a second, independent seated signal: the game's own `EmoteController.GetPosture()` (`SittingOnGround` / `SittingInChair`), so detection works even if Mode reads Gathering while a line is cast; either signal counts as seated (file: `FishSitService.cs`, `ReadPosture`).
- Belt and braces per fishing session: after one /sit has been sent, another is only sent once a posture read has confirmed the sit took AND the player has then read as not-seated on 2 consecutive checks; if the posture read never confirms, at most ONE /sit goes out per continuous Fishing session (re-arms when the Fishing condition drops). One bad read can no longer stand him up (file: `FishSitService.cs`, `Tick`).
- `/lazyfishsitter debug` now prints Mode (name + byte), ModeParam, EmoteController.EmoteId, the game posture, and the per-session counters so the seated-while-fishing question can be settled from the Dalamud log (file: `FishSitService.cs`, `LogDebugState`).

## v0.1.0.0 (2026-09-05)

- New plugin: while the game's Fishing condition is active, checks every few seconds (default 2) and runs /sit when the player is not already seated (file: `FishSitService.cs`, `Tick`).
- Seated detection reads the local Character's Mode field - 12 (EmoteLoop) covers persistent emotes like /sit, 13 (InPositionLoop) covers chair/bench and pose loops - the same read SimpleHeels' EmoteIdentifier uses, so the plugin never re-sits someone already seated, on a chair, or holding a pose (file: `FishSitService.cs`, `IsInPostureLoop`).
- The /sit is sent through the game's own chat-entry path (UIModule ProcessChatBoxEntry), inlined and trimmed from XivCommon's MIT Chat.cs - the same code ECommons ships inside this repo (file: `ChatCommand.cs`).
- Safety valves: never re-sends the sit command more than once per 3 seconds, and skips while between areas, in events, quest events, cutscenes, combat, mid-emote, or jumping (file: `FishSitService.cs`, `Tick`).
- Config: enabled toggle, check interval 1-10s, and the sit command itself (default /sit, must start with a slash) (files: `Configuration.cs`, `ConfigWindow.cs`).
- Ships on the TESTING channel first. Use /lazyfishsitter for settings, /lazyfishsitter debug for a state dump in the Dalamud log.
