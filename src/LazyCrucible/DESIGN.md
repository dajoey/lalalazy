# LazyCrucible — design (0.1.0.0)

What the plugin decides on every screen of a Crucible of the Unbroken run, what is known and unknown
about each screen, and the one read-only capture that closes the unknowns without further probe rounds.

Evidence tags: **[obs]** seen in the plugin logs (ffxivdb `plugin_log_lines`, runs of 2026-09-17 to
2026-09-21); **[pub]** published in someone else's source (licence in §7); **[inf]** inferred, unverified.
Timestamps are US Eastern.

## 1. Status in 0.1.0.0

| Area | 0.1.0.0 does | Switch (default) |
|---|---|---|
| Run roster, Bentbranch entry menu | Writes the ten best-coverage familiars for the board, once per menu open | `AutoRoster` (on) |
| Battlehorns, pre-fight formation | Writes the three familiars that answer the identified fight | `AutoHorns` (on) |
| Every other screen | Records only (§5); never acts | `RecordScreens` (on) |
| Beast-pick advisor | Panel: picks per battle, board roster, familiars worth capturing | — |

Guards on every write: `XBMPetParty` itself open and its own mode naming the roster list (mode 0,
pre-entry only) or the horn preview (mode 2, board only); feeding (3, 5) and campsite (4) never
written; read-back after every toggle with snapshot/restore; the start-battle confirm is never pressed;
any selection edit the pass did not send (the player, a preset, AutoDuty) stands the pass down for that
screen, and for the roster for the rest of that Bentbranch visit; stands down entirely while AutoDuty
reports a run in progress (`YieldToAutoDuty`, on) or while a GluttonyCombo ≤ 1.0.4.229 (which still
carries its own writer) is loaded.

### The coexistence question (owner decision, not settled here)

AutoDuty (a fork with Crucible support, `erdelf/AutoDuty@9c680db16f`) already drives most of a run
[pub, and live 2026-09-21 12:50–13:02: "Running AutoDuty in First Board of the Unbroken, Looping 6
times"]: entry NPC, board select, its own familiar team (Leveling / Recommended modes), horns, spoils
("Take all"), treasure, shop, campsite rest, board item use, and path choice by walking. At 12:50 it
rebuilt a *leveling* roster while GluttonyCombo's writer rewrote a *coverage* roster four times.
LazyCrucible therefore yields to AutoDuty by default. Before any screen below is automated, the
owner should decide which tool drives runs:

- **A. AutoDuty drives, LazyCrucible advises** (today's default): no duplicate screen automation; the
  familiar picks are AutoDuty's.
- **B. LazyCrucible drives the familiar decisions, AutoDuty the rest**: needs AutoDuty's team handling
  off during Crucible runs (its config is reachable through its `SetConfig` IPC [pub], key names not
  yet read).
- **C. LazyCrucible drives everything** (AutoDuty off): the §3 plan below.

## 2. Policy — survival first

Order of priorities for every decision: (1) keep familiars alive (a knocked-out familiar is gone for
the run), (2) win the run, (3) score bonuses. Score-only choices (e.g. "Starve the Fever": no feeding)
are never taken at the cost of (1) or (2); a later opt-in "score mode" may take them only on boards
the character has already cleared.

## 3. Screens

Agents in the linked ClientStructs: `XBMContentsMainHUD` 497, `XBMItemDetail` 498,
`XBMBattleMonsterDetail` 499, `XBMMonsterNotebook` 500, `XBMPetParty` 501, `XBMStageDetailList` 502,
`XBMStageList` 503, `XBMStageMap` 504, `XBMResult` 505, `XBMRanking` 506 (enum only; structs are
PR #1952-only for 500-502). Addons without an agent of their own: ContentsBooty, ContentsTreasure,
ContentsItemShop, ContentsGearEffect, ContentsItemDispose, ActivePet, PetActionDetail, PresetPreview,
StageSummary, BonusList.

### 3.1 Entrance and board select (Central Shroud 148, NPC by the Bentbranch Meadows aetheryte)

- **Known** [obs]: `XBMStageList` n=14 (`2:s=<board name>`, `3:u=<XBMContent row>`), then
  `XBMStageMap`, then `XBMStageDetailList`+`XBMPetParty` (stage mode 0, party mode 0 = roster).
  [pub, AutoDuty/BeastHelper, facts only]: talk → SelectString / SelectIconString; `XBMStageList`
  callback `[2,board]` highlights, `[1,board]` opens. BeastHelper crashed twice inside
  `AgentXBMStageDetailList.Update` firing that callback the instant the list appeared, and once
  re-entering < 5 s after a run ended; it now waits 2 s / 8 s.
- **Unknown**: our own capture of those callbacks; which board the player intends (the roster pass
  today falls back to board 1 when `AgentXBMPetParty.ContentId` is unset).
- **Decision**: board = the one the player highlights; never auto-select a board (a player choice).
  If automated later: replay captured values only, ≥ 2 s after the list is visible, ≥ 8 s after a run.

### 3.2 Duty entry and confirm

- **Known** [pub]: `XBMStageDetailList` `[8]` = challenge (DailyRoutines hooks this as kind 0 [8]);
  a `SelectYesno` if the team has fewer than ten; `ContentsFinderConfirm` Commence.
- **Decision**: never automated — starting a run is the player's decision (same rule as the horn
  pass: the start-battle confirm is never pressed).

### 3.3 Run roster (automated) and 3.4 Battlehorns (automated)

- Roster: coverage ranking over the board's battles (`PickSlotsCoverage`, 10 slots), TogglePet
  (PR #1952 sig, live-proven) + ApplyPetSelection, read-back by membership.
- Horns: next fight identified from the stage agent's focused entry (`_entrySelection`), ranked by
  `PickSlots` (weakness, interrupt/dispel/cleanse needs, crowd control, rank-synced stats, HP factor,
  0 % never picked); membership-delta toggles via `ReceiveEvent(kind 0, [1, SelectedPets index])`.
- Live record [obs, 5 runs .227-.229]: 26 horn writes read back OK; for all 20 identified pre-fight
  passes the horns the fight started with (`BT| sl=`) equal the pass's picks. The 4 other targeted
  passes were not horn screens (3 × the shop's feed picker, 1 × the notebook's team screen), both now
  excluded. Each fight gets different familiars (board 1: bt 1 opo-opo/morbol/dullahan, bt 3
  dullahan/uragnite/flying trap, bt 4 ziz/flying trap/ghost, elite ghost/salamander/uragnite or worm,
  boss sabotender/salamander/ghost or morbol).
- **Unknown**: whether `_entrySelection` names the focused entry on boards 2-5 (only board 1 graded).

### 3.5 Path choice (`XBMStageDetailList`, stage mode 3 on the board)

- **Known** [obs]: the pre-entry graph lists every space: `[6]` 2 battle / 0 other, `[+5]` space type
  (0 enemy, 1 elite, 2 boss, 3 shop, 4 camp, 5 treasure), 40 values per entry; battle rows carry the
  `XBMBattleDetail` row (→ board/battle via the generated table). [pub, AutoDuty path file for 1339]:
  the choice is made by **walking** onto the space (forks at x = -705 left / -695 right).
- **Unknown**: what commits a space when it is stepped on (event object? director event?); whether
  the candidate spaces' enemies are readable before the fork (the focused-entry detail is).
- **Decision** (survival first): score each branch by (a) the fights' fit to the *alive* roster
  (sum of `PickSlots` scores of the best three per fight), (b) access to a campsite when the roster's
  HP is low, (c) a shop when tokens are high; treasure over shop when the roster is healthy. Movement
  itself stays manual until the commit mechanism is recorded (§5: `XO|` + `XK|`).

### 3.6 Spoils (`XBMContentsBooty`, n=147, 0.1-0.3 s after each battle)

- **Known** [obs]: tokens before/gained `[2]/[4]`; up to 4 loot entries (present 6+5k, XBMItem row
  9+5k); held items 27-76, gear 78-127; per-loot taken flags 129-132. [pub]: "Take all" = button
  node 46 (a plain button — no FireCallback), then `SelectYesno` Yes.
- **Unknown**: the per-item take event; `[145]`; the inventory-full discard flow (probably
  `XBMContentsItemDispose`, never seen).
- **Decision**: take everything; when full, discard the lowest-value Crucible item (sell price from
  `XBMItem`) but never a healing item before the boss.

### 3.7 Treasure (`XBMContentsTreasure`, n=144)

- **Known** [obs]: 4 choices (present 3+5k, XBMItem row 6+5k), held items from 24, gear from 75.
  Board 1 pools per treasure space recorded across 9 runs. [pub]: choice buttons are ButtonClick
  events with Param ≥ 2 (probably 2+k [inf]), then Yes.
- **Unknown**: exact Param per choice (§5 `XR|`); the two-pick flow with Thief's Knife.
- **Decision**: rank choices by survival value for the rest of the board: familiar HP/defence gear
  > healing items > damage gear > score items.

### 3.8 Shop buy / sell (`XBMContentsItemShop`, n=269)

- **Known** [obs, re-checked 11:43:57]: `[1]` tokens, `[2]` stock count (16); stock from 3, stride 5:
  present, XBMItem row, price text (" (-50%)" when discounted), discount flag, bought flag. Always
  8 feed + 4 items + 4 gear. [pub]: buy = callback `[2, stockIndex]`, then Yes; close = node 40 then Yes.
- **Unknown**: selling (never observed, no source).
- **Decision**: buy feeds for the next fights' horn familiars first (§3.9), then healing items for the
  boss, then gear; keep a reserve for the next shop only if one is still reachable.

### 3.9 Beast Feed (`XBMPetParty` agent mode 3, opened from the shop)

- **Known** [obs]: feed target = `kind 0 [1, SelectedPets index]`, `kind 1 [0]` = Yes, then
  `kind 1 [-2]` + `kind 0 [-2]` close; a lone `kind 1 [0]` = refused. Per familiar a 77-value block
  (B = 6+77k): B+2 "cannot eat this feed" (matched kin rules in all 5 sessions), B+7..16 feeds eaten,
  B+72/B+73 satiety used/max, B+76 XBMPet row. `SelectedPetIds` (0x90) holds the feed target. Rules
  [pub, consolegameswiki]: capacity = `XBMPet.SatietyMax` (1-5), no duplicate feed per familiar, kin
  restrictions, not while knocked out; a campsite rest wipes feed unless Lily Simular was eaten.
  G1 Primafodder: max HP +15 %, heals 10 % [obs].
- **Decision** (survival first): feed only familiars that will be on the horns in the remaining
  fights (the `PickSlots` picks for those fights), max-HP / defence feeds first, never a familiar
  flagged B+2, never past satiety, never a knocked-out familiar; buy feed only after the last campsite
  the path still passes (or with Lily Simular). "Feed the Bold" (20+ feeds) is a side effect, never a
  goal; "Starve the Fever" is never chased (it costs survival).
- **0.1.0.0**: excluded from every write (the .227-.229 `bt=5 readback=fail` defect was the horn pass
  writing into this picker). Automating it needs only the Yes/No prompt text (§5 `XB|SelectYesno`).

### 3.10 Campsite (`XBMPetParty` agent mode 4)

- **Known** [obs, 4 visits]: `[2]=4 [3]=0`; picks `kind 0 [1, index]` (B+75 flips to 1), Rest =
  `kind 0 [3]`, then `kind 2 [0]`, `kind 2 [-2]`, `kind 0 [-2]`. 2 familiars picked: +30 % HP each;
  none picked: 90 % self heal [pub].
- **Unknown**: where the camp's familiar count and heal percentage are exposed.
- **Decision**: rest the lowest-HP alive familiars that the remaining fights need; if all needed
  familiars are healthy, take the self heal.

### 3.11 Beast gear (`XBMContentsGearEffect`, n=45) — read-only list; nothing to decide.

### 3.12 Board items (`XBMContentsMainHUD`, n=111, agent 497)

- **Known** [obs]: items from 9, 10×5 (shown, **usable now**, icon, XBMItem row, name); gear from 60.
  [pub, AutoDuty]: `[6, slot]` opens the item menu, then `ContextMenu [0,0,0]` (live 12:59:01).
- **Decision**: use healing items at the familiar's HP threshold in battle (the rotation already swaps
  hurt familiars); use Temporal Sand–type items only outside battle; save boss-only items.

### 3.13 Result (`XBMResult`, n=169) — read the score; button 61 continues [pub]. Suspend / forfeit
(HUD buttons [2]/[3]): unknown flow; **never automated** (player decisions).

## 4. Implementation order after the capture

1. Feed (3.9) and campsite (3.10): same agent as the horn pass, events already recorded.
2. Spoils (3.6) and treasure (3.7): one screen each, deterministic.
3. Shop buy (3.8), then board items (3.12).
4. Path choice (3.5): needs the commit mechanism first.

## 5. One capture session (0.1.0.0 recorder, always on, read-only)

What the recorder writes (all lines through `CrucibleLog`, context `LazyCrucible`):

| Line | Source | Answers |
|---|---|---|
| `XV\|ms\|open=Name` / `close=Name` | addon visibility, 10 Hz | the run's screen sequence |
| `XB\|ms\|Name\|n=..\|idx:type=value;..` (+`XB+\|`) | AtkValues on open/change, ≤ 1/s per addon, 2000 values, 60-char strings | every screen's contents (all 10 familiars on XBMPetParty; shop, spoils, treasure, result) |
| `XC\|ms\|Name\|upd=..\|n=..\|values` | `AtkUnitBase.FireCallback` hook | exact callback values per click (replayable) |
| `XR\|ms\|Name\|type=..\|param=..\|node=..\|d=hex16` | Dalamud addon lifecycle PreReceiveEvent (clicks only) | plain-button node/param (Take all, treasure choice, Rest, HUD buttons) |
| `XE\|ms\|ag=Agent\|via=..\|kind=..\|n=..\|values` | ReceiveEvent on agents 497-506 (pp/nb keep `PSP\|`) | agent-side events for every screen |
| `XA\|ms\|pp\|mode=..\|sub=..\|selIdx=..\|u138=..\|u13c=..` / `XA\|ms\|stage\|content=..\|mode=..` | PR #1952 offsets, on change | feed/camp modes, `SetMode` parameters (Unk138 probably the feed row) |
| `XO\|ms\|pos=..\|obj=kind:DataId@x,z;..` | on a board, ≤ 1/s, objects within 40 y | how stepping onto a space commits the path |
| `XK\|ms\|+Flag` / `-Flag` | condition changes in 148 / boards | event states around path commits and menus |
| `PS\|..\|note=autoduty\|running=0/1` | AutoDuty IPC | which run segments were AutoDuty's |

Session script — one sitting, one board-1 run plus a short manual pass (≈ 30 min):

1. One AutoDuty run with LazyCrucible's `YieldToAutoDuty` on: records the exact callbacks AutoDuty sends
   on every screen it drives (facts by observation) next to the screen contents.
2. One manual run (AutoDuty stopped), LazyCrucible roster/horns on, doing each of these once: take
   one spoils item individually; pick treasure by clicking; buy one item and **sell** one; feed one
   familiar and try one invalid feed; rest at the camp with one familiar (and, on a later run, two);
   use one board item from the HUD; open Beast Gear; open Suspend and answer No; open Forfeit and
   answer No; if possible fill the item slots so the discard screen appears.

After it, every screen in §3 has contents (`XB`), the actions that drive it (`XC`/`XR`/`XE`), the
agent state around it (`XA`) and, for the path, position/object/condition traces (`XO`/`XK`).

### What the recorder cannot do (stated, not guessed)

- No memory dumps of `AgentXBMContentsMainHUD` or the Crucible director: their struct sizes are not
  published; the slot map (+0x50) and director inventory (+0x2384) come from unlicensed, unverified
  code, and reading at guessed offsets is exactly what this project does not ship.
- Path commits are recorded only indirectly (position, nearby objects, conditions); no hook on the
  event framework or director.
- `XBMStageDetailList` has 40,058 AtkValues; the recorder keeps the first 2000 (50 entries of 40),
  which covers the board-1 graph; boards 2-5 unverified.
- `XR` records the first 16 bytes of the event data raw (list indices live there; layout differs by
  event type).
- Screens only appear on Beastmaster (the recorder, like the automation, runs only on BST).
- Four addons have never been seen: `XBMContentsItemDispose`, `XBMStageSummary`, `XBMRanking`,
  `XBMMonsterNotebookFilterSetting`.

## 6. Residual assumptions of 0.1.0.0 (each beside its guard)

- XBMPetParty AtkValues [2]/[3] mirror agent Mode/SubMode (26 opens agree; agent fields logged as
  `agm=`/`ags=` for confirmation) — guard: any feed/camp signal vetoes a write; unknown modes never write.
- `_entrySelection` focus = the next fight (board 1 only) — guard: coverage fallback never replaces a
  non-empty horn; picks name the fight in chat, so a wrong focus is visible.
- An event the pass did not send is an edit to respect (includes AutoDuty and presets) — guard is the
  stand-down itself (the safe direction: at worst the horns stay as someone else left them).
- Roster board at Bentbranch falls back to board 1 when `ContentId` is unset — logged as `b=` on the
  roster decision line.

## 7. Upstream sources and licences

| Source | Licence | Use here |
|---|---|---|
| FFXIVClientStructs + PR #1952 | MIT | offsets, signatures, agent/addon ids |
| ECommons | MIT | Callback hook pattern (FireCallback delegate) |
| Dalamud | AGPL-3.0 | addon lifecycle API (runtime use) |
| DailyRoutines ModulesPublic | AGPL-3.0 | facts (stage agent kind 0 [8] = start battle) |
| BossmodReborn / WrathCombo | BSD-3-Clause | nothing XBM-specific found |
| MagitekRoutine, RotationSolverReborn | GPL-3.0 | Crucible piece data only (MagitekRB PR #325) |
| erdelf/AutoDuty | none ("Unlicensed" per README) | facts only: callbacks, node ids, path positions, IPC names |
| BeastHelper (Rin-0617) | none | facts only: crash timings, board callbacks |
| BOCXIV/Beastmaster-JP, CherryXIV | none | facts only |
