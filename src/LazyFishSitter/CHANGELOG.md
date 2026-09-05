# Changelog

## v0.1.2.0 (2026-09-05)

- Re-sits after every cast and every catch. The game forces the character to stand on every hooked fish, so 0.1.1.0's one-/sit-per-fishing-session guard meant the first catch stood you up for good; the unit is now a "stand episode" (fishing start, or the fishing state machine passing through Hooking/ReleasingCatch/ConfirmingCollectable), and each episode gets at most ONE /sit (file: `FishSitService.cs`, `Arm` / `TrackTransitions`).
- Sends only when the game will accept it: reads `EventFramework.Instance()->EventHandlerModule.FishingEventHandler` (FFXIVClientStructs) and requires `State` to be `PoleReady` or `LineInWater` for at least 1 s with `ChangingPosition` false - never mid cast, mid hook, or while already sitting down/standing up (file: `FishSitService.cs`, `Tick`).
- Proof the sit took: `FishingEventHandler.ChangingPosition` going true within 3 s of our send is logged as `game accepted our sit`, and a 3 s outcome line reports accepted/seatedRead for every send; a second /sit in the same episode is never sent once the game accepted the first, whatever the posture detectors say (a /sit on a seated player STANDS him) (file: `FishSitService.cs`, `TrackTransitions`).
- No other re-arm path exists: a "not seated" read after our sit never triggers a re-send on its own (that is exactly what yo-yoed 0.1.0.0). If you stand up by hand mid-cast the plugin leaves you standing until the next cast/hook; if the game refused our one /sit (sent too early), that cast is spent standing and the next hook re-arms (file: `FishSitService.cs`, `Tick`).
- Logs every posture/fishing state CHANGE at Information, rate-limited to 40 lines/min: Fishing flag, FishingState, ChangingPosition, CanFish, Character.Mode/ModeParam, EmoteController.EmoteId, GetPosture(), and the combined SEATED verdict - so whether Mode/GetPosture read seated while sitting-and-fishing can be graded from the Dalamud log / ffxivdb without typing anything in game (file: `FishSitService.cs`, `TrackTransitions`, `LogTransition`).
- A failing FFXIVClientStructs signature (EventFramework or GetPosture) is now logged once at Warning instead of being swallowed silently every check (file: `FishSitService.cs`, `Read`).
- Added the shared in-game "What's new" popup (repo standing rule): after an update this changelog opens once; `/lazyfishsitter changelog` reopens it (files: `Plugin.cs`, `Configuration.cs` `LastSeenChangelogVersion`, `LazyFishSitter.csproj` compiles `src/Shared/LalaChangelog`).

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
