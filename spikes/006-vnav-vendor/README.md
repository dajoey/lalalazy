# 006: vnavmesh walk-to-vendor after Lifestream teleport (LazyCrafter P6 spike, t_977b94b4)

**Question.** After `Lifestream.Teleport(aetheryteId, 0)`, can `vnavmesh.SimpleMove.PathfindAndMoveTo(npcPos, false)`
reach a gil-vendor NPC and can we open its shop (`TargetSystem.InteractWithObject` → `Shop` addon), reliably,
across 5 vendors in 3 zones? Joey's rule: **ship the walk-to-vendor toggle only if 5/5 and not janky.**

**Status: PARTIAL — research + harness done offline; the 5 in-game runs are NOT done** (this lane does not launch the
client). The toggle stays hidden. The V2 skeptic card (`t_398d1b66`) is where the five result lines get collected.

## What is in this directory

- `VendorProbe/` — offline console (bare Lumina on the installed sqpack, same wiring as `tests/LazyCrafter.Probe`).
  Lists every ENpc with a `GilShop` handler per territory with its world position and distance to the nearest
  teleportable aetheryte. Positions come from the territory's `planevent.lgb` (what ItemVendorLocation does — the
  `Level` sheet only places quest/event NPCs) with `Level` as fallback. Run:
  `dotnet build spikes\006-vnav-vendor\VendorProbe -c Release; dotnet spikes\006-vnav-vendor\VendorProbe\bin\Release\net10.0-windows7.0\VendorProbe.dll "<sqpack>" 129 130 132`
- `../../src/LazyCrafter/Spike/VendorSpike.cs` (this branch only) — the in-game runner. `/lcraft spike list|1..5|all|stop|results`.
  Framework-ticked state machine: teleport → zone change settled → `Nav.IsReady` → dismount → `PathfindAndMoveTo` →
  (one direct `Path.MoveTo` nudge if vnavmesh parks outside 3.5y) → target + `InteractWithObject` → `Shop`, or a
  `SelectIconString`/`SelectString` menu first (selects entry 0 and reports whether `Shop` followed). One result line per
  vendor to chat + `/xllog` with timings, final distance, nudge, menu, and a jank list.

## The five vendors (from the sheets, 2026-09-03)

| # | Zone (territory) | Aetheryte | NPC (ENpcBase) | Position | Walk | Handlers |
|---|---|---|---|---|---|---|
| 1 | Limsa Lominsa Lower Decks (129) | 8 | Bango Zango 1001787 | (-62.1, 18.0, 9.4) | 24y | 13 → menu |
| 2 | Limsa Lominsa Lower Decks (129) | 8 | Gerulf 1003253 | (-149.9, 18.2, 36.9) | 76y | 1 → direct Shop |
| 3 | Ul'dah - Steps of Nald (130) | 9 | Rianne 1001974 | (-67.6, 4.6, -107.5) | 99y | 3 → menu |
| 4 | Ul'dah - Steps of Nald (130) | 9 | Roarich 1004417 | (-33.6, 9.1, -84.3) | 140y, multi-level | 12 → menu |
| 5 | New Gridania (132) | 2 | Maisenta 1001276 | (14.0, 0.1, 2.1) | 34y | 18 → menu |

Chosen for: three zones with a teleport aetheryte each, short and long walks, a single-handler NPC (Shop opens
directly) and multi-handler NPCs (a selection menu comes first), and Ul'dah's stacked-level navmesh.

## What the source says (verified, not remembered)

vnavmesh 1.2.3.13 is installed; names below are from `vnavmesh/IPCProvider.cs` @ master (prefix `vnavmesh.`):

- `SimpleMove.PathfindAndMoveTo(Vector3 dest, bool fly) → bool` is **async**: it queues `NavmeshManager.QueryPath`
  and returns `true` immediately; returns `false` (and logs an error) if a pathfind is already pending
  (`AsyncMoveRequest.MoveTo`). The path is handed to `FollowPath` on a later tick; a failed solve is only a
  `DuoLog` "Failed to find path" — **no IPC signal**. Callers detect it as "`Path.IsRunning` never became true".
- `SimpleMove.PathfindAndMoveCloseTo(dest, fly, float range)` exists — stop within `range` instead of at the point.
- `Path.IsRunning → Waypoints.Count > 0`; `Path.Stop`; `Path.MoveTo(List<Vector3>, bool fly)` (raw waypoints, no
  pathfinding — used for the nudge). `SimpleMove.PathfindInProgress` = solve still pending.
- `FollowPath.Tolerance = 0.25f` (waypoint-passing), `DestinationTolerance` per request. vnavmesh stops at the mesh
  polygon nearest the target, which for a solid object is its collision edge — LazyOccultCrescent's
  `PathfindAndMoveToChain` learned that a 5y "arrival" left the aetheryte out of its 3.8y interact range and added a
  one-shot direct nudge. This spike copies that.
- `FollowPath` has its own stuck detection (`StopOnStuck`, `StuckTimeoutMs`, retry via `OnStuck` when `RetryOnStuck`)
  and `CancelMoveOnUserInput` — both are **user config** on Joey's install, so a run can be silently stopped by either.
- **Navmesh after teleport:** `NavmeshManager.Update()` compares a key built from the active layout (territory +
  layout filter + festival) and, when `AutoLoadNavmesh` is on (default), starts `Reload(true)` — a cached-mesh load.
  `Nav.IsReady` is **false** until it finishes; the spike waits on it (60 s cap) and flags >5 s as jank.
  `Nav.IsReady` is also false during cutscenes (`InCutscene` wait loop).

Lifestream 2.5.4.16 is installed; `Lifestream/IPC/IPCProvider.cs`:

- `Teleport(uint destination, byte subIndex) → bool` = `TeleportService.TeleportToAetheryte(id, sub)` — a **raw**
  `Telepo.Instance()->Teleport`; returns `true` when the aetheryte is in the attuned list and `CanTeleport` passes
  (not in combat/casting/etc.). With `wait:false` (the IPC form) **nothing is enqueued**, so `Lifestream.IsBusy()`
  is NOT a completion signal for this call. Completion has to be observed by the caller: `BetweenAreas` seen then
  cleared, no `NowLoading`/`FadeMiddle`/`FadeBack` addon visible (ECommons `IsScreenReady`), player targetable,
  `ClientState.TerritoryType == expected`. That is what `VendorSpike.ZoneChange` does.
- `IsBusy()` = `TaskManager.IsBusy || FollowPath has waypoints` — meaningful for the name/aethernet forms and
  `/li tp` (those enqueue tasks), not for `Teleport(uint, byte)`.

Interaction (ECommons 3.2.0.9 as vendored in-repo, `NeoTasks.InteractWithObject`):

- Target first (`Svc.Targets.Target = obj`), then `TargetSystem.Instance()->InteractWithObject(obj.Struct(), checkLos:false)`
  once `obj.IsTargetable && !Player.IsAnimationLocked && Player.Interactable`. Same call every Lifestream task uses.
- Addon names: gil shop = `"Shop"` (FFXIVClientStructs `AddonShop`, `[Addon("Shop")]`); multi-handler NPCs open
  `"SelectIconString"` (or `"SelectString"`) first. ECommons `AddonMaster.SelectIconString.Entry.Select` =
  `Callback.Fire(addon, true, index)`. Selecting **by index 0 is a spike shortcut** — a real implementation must
  match the entry text (e.g. "Purchase items"/the shop name from `GilShop.Name`) because entry order differs per NPC.
- Dismount = `ActionManager.UseAction(GeneralAction, 23)` (`GeneralAction` 23 = "Dismount", xivapi v2).

NPC positions: `Level` sheet `Type 8 = ENpcBase`, `12 = Aetheryte` (EXDSchema `Level.yml`) — but city vendors are
mostly **not** in `Level`; they are `EventNPC` instance objects in `bg/<zone>/level/planevent.lgb`
(`LayerCommon.ENPCInstanceObject.ParentData.ParentData.BaseId`). `VendorProbe` reads both. 487 ENpcBase rows carry a
`GilShop` handler.

## Expected jank (from reading, to be confirmed by the five runs)

1. Multi-handler NPCs (4 of the 5) need menu handling; index-0 selection will be wrong for some of them. Text match is
   the fix; still a "not janky" question because the menu adds a visible extra step.
2. vnavmesh parks at the NPC's collision edge; whether 3.5y is inside vanilla talk range depends on the NPC's hitbox.
   The nudge covers it, but a nudge that pushes into the NPC looks bad.
3. Ul'dah (#4) stacks levels; a wrong-floor solve walks the long way or stops under the target.
4. `Nav.IsReady` latency after teleport (cached load is usually ~1 s, cold build is tens of seconds).
5. Joey's vnavmesh config (`CancelMoveOnUserInput`, `StopOnStuck`) can end a run without any IPC signal.

## Verdict: PARTIAL (research VALIDATED, in-game UNRUN)

### What worked
- The whole chain is buildable from public IPC + one FFXIVClientStructs call; Release build 0 warnings / 0 errors on
  branch `spike/p6-vnav-vendor`.
- Vendor positions can be derived offline from the sheets/LGB, so LazyCrafter never needs ItemVendorLocation.

### What didn't / not proven
- Zero in-game runs. The 5/5 gate cannot be judged from this lane.
- `Lifestream.Teleport` completion and `PathfindAndMoveTo` failure are both **unsignalled**; every "reliable" claim
  rests on the caller's own polling.

### Recommendation for the real build
- Keep the toggle hidden (Plan P4 §5 already says "OFF until Phase 6 spike passes"). Decision stays with Joey after
  he runs `/lcraft spike all` on this branch's build and reads the five lines; V2 (`t_398d1b66`) collects them.
- If 5/5: implement as a `VnavDispatch` using `PathfindAndMoveCloseTo(pos, false, 3.0f)`, menu entry **text** match,
  and Lifestream's `/li tp <aetheryte>, <cmd>` chaining is not needed — the polling above is enough.
- If <5/5: v1 stays "teleport + map flag + chat shopping list" (Plan P5 §4), which needs none of this.
