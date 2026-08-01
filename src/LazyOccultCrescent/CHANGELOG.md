# LazyOccultCrescent

Fork of [OhKannaDuh/BOCCHI](https://github.com/OhKannaDuh/BOCCHI) (AGPL-3.0-or-later),
forked at `ded40a71af051a3aa57d326c512a975e7957daf6`. Upstream copyright and licence
are preserved in `LICENSE`.

## v0.1.0.3 (2026-08-01) [testing]

### Fixed
- **A FATE ending mid-transit left the plugin pathing to it and returning to base
  at the same time.** My regression, and a design flaw rather than a typo.
  The Automator already handles this correctly: when `Activity.IsValid()` goes
  false it calls `Plugin.Chain.Abort()` and `vnav.Stop()`. But the movement loop I
  wrote in v0.1.0.0 re-issues movement whenever vnavmesh is not running - that is
  how it recovers from a failed solve - and it had no way to tell "vnavmesh gave
  up" apart from "someone deliberately stopped us". So the Automator's `Stop()`
  read as a stall and got immediately undone, restarting the walk toward a FATE
  that no longer existed, while the idle timer started a Return alongside it.
  Two fixes, one specific and one general:
  - `PathfindAndMoveToChain` now takes an abort predicate checked before anything
    else each tick. `Activity` passes `() => !IsValid()`, so a route to something
    that has ceased to exist is abandoned rather than re-driven.
  - Movement restarts are capped at 4. If the route has been re-issued that many
    times and still is not sticking, something else is driving and the chain
    yields instead of fighting it. The abort predicate covers the case we know
    about; the cap bounds the ones we do not.
  Yielding to manual control resets the restart budget, since handing over to the
  player deliberately is not a failed solve.

## v0.1.0.2 (2026-08-01) [testing]

### Fixed
- **Walk to an aetheryte stopped short of interaction range.** v0.1.0.1 fixed a
  too-strict 3y arrival gate by making it a flat 5y - an overcorrection in the
  other direction, since interacting needs 3.8y. Arrival tolerance is now supplied
  by the caller: aetheryte and shard approaches demand 3.0y, everything else keeps
  the loose default. When vnavmesh stops short of what the caller needs (its route
  ends at the navmesh edge nearest a solid object, which can be outside reach) the
  chain now closes the last stretch directly instead of accepting a bad position.
  Once only - a second failure means it genuinely cannot get closer.
- **It never mounted after teleporting.** Upstream bug, not zone-specific.
  `ShouldMountToPathfindTo()` requires the destination to be more than 20y away,
  but it was sequenced *after* the `PathfindingChain` that had already walked
  there - so it always evaluated at roughly 0y and returned false. Mounting now
  happens before the walk in all four navigation branches, which is the only
  ordering in which that check means anything.
- **Approaches came in at an angle.** The aggro-avoidance detour added in v0.1.0.0
  applied to every route including short final approaches, so a 20y walk to an
  aetheryte could get a 20y sidestep bolted onto it. Detours are now skipped for
  routes under 40y: below that the route is an approach to something, not a
  traversal across open ground.
- **Upstream data error in the Eldergrowth shard.** Its teleport `Destination` X
  was -302.3 while the shard itself sits at +306.94 - a landing point 600y from
  its own aetheryte. The LGB layout confirms the positive value. Sign error,
  corrected. South Horn only; it has been wrong since before the fork.

## v0.1.0.1 (2026-08-01) [testing]

### Fixed
- **Aethernet teleports stopped firing in v0.1.0.0. My regression.** Rewriting
  `PathfindAndMoveToChain` for manual-control yielding replaced the old
  "vnavmesh stopped, we're done" completion with a hard `distance <= 3y` test.
  `TeleportChain` walks to the shard and then needs to be within
  `AethernetData.DISTANCE` (3.8y) to interact - so the arrival gate was *stricter
  than the thing it was feeding*. Aetherytes are solid objects and vnavmesh parks
  at their collision edge, not their origin, so the walk "never arrived" at a spot
  that was already close enough to teleport from. The chain then sat until its
  180-second limit and the teleport never happened.
  Return to base kept working throughout because it casts Return rather than
  walking, which is exactly why only teleports looked broken.
  Fix: tolerance raised to 5y, and completion once again accepts "vnavmesh was
  running and has now stopped" - guarded by a `sawRunning` flag, since `IsRunning`
  reads false for a moment right after a request goes in and would otherwise
  report instant arrival.
- Manual-control detection now takes precedence over arrival inference. Taking
  over at the moment vnavmesh gave up could otherwise be read as a successful
  arrival, advancing the chain from the wrong position.

## v0.1.0.0 (2026-08-01) [testing]

First release that improves on BOCCHI's behaviour rather than just reaching parity
with it in a second zone.

### Added
- **Automation yields to manual control.** There was no player-input detection
  anywhere in the codebase. vnavmesh drives by feeding movement input and follows
  a waypoint list computed once at the start of a chain, so walking away from an
  automated route left it steering toward a waypoint you had already abandoned -
  which reads in game as the character turning round and marching back to where
  you deviated. Now: movement input pauses automation, and on resume the route is
  **recomputed from where you actually are** rather than continued from where it
  left off.
  Detecting input rather than movement is the load-bearing part - the character is
  always "moving" while vnavmesh drives it, so a position delta cannot tell the two
  apart. Keyboard is read via `IKeyState`; the gamepad stick is bound by reflection
  so a Dalamud API rename degrades to "keyboard still works" instead of failing the
  build. 2s settle before resuming. Toggle: Pathfinder → Yield to manual control.
- **Aggro-aware routing.** vnavmesh solves for geometry - it will walk a straight
  line through a pack because the floor is walkable - and it has no avoidance API.
  Occult Crescent has no flying, so routing over the problem is not an option
  either. But vnav exposes `Pathfind` (hand back the waypoints) and `FollowPath`
  (walk a list I give you), so routes are now post-processed: any segment passing
  within 16y of a hostile gets a detour waypoint inserted perpendicular to the
  segment, on the side away from the threat, snapped to the navmesh.
  Deliberately conservative: mobs already targeting you are skipped (the combat
  handlers own that), anything within 30y of the destination is treated as the
  objective rather than an obstacle, detours are capped at 6 per route, and if no
  reachable detour exists it walks the direct line rather than refusing to move.
  Toggle: Pathfinder → Avoid aggro.

### Notes
- Aggro radius is a fixed 16y rather than derived per-mob. Real sight aggro varies
  by enemy and field-operation elites reach further, so this errs wide. If specific
  pulls still catch you, that number is the thing to raise.
- The detour is geometric, not tactical: it does not know about patrol paths, so a
  mob walking toward the detour point can still meet you there.

## v0.0.3.0 (2026-08-01) [testing]

### Fixed
- **Automation made nonsensical teleport choices in North Horn.** Five separate
  places assumed South Horn's base camp. In rough order of damage:
  - **`ReturnChain.GetCostToReturn()` threw outright.** It reads
    `ZoneData.StartingLocations` and throws `"Unable to determine Starting
    position"` when the territory is missing - and North Horn was missing. So
    *every* Return-based navigation in North Horn died mid-chain and the
    Automator fell through to whatever option was left, which is the erratic
    behaviour you actually see. Added the North Horn entry.
  - **`SmartNavigation.Decide` priced "Return then walk" against South Horn's
    aetheryte.** It used the `Aethernet.BaseCamp` literal at
    (830.75, 72.98, -695.98) while North Horn's is at (880.00, 259.74, 880.06) -
    1,576 yalms out on Z alone. The cost model was comparing a real option
    against a fictional one, so the winner was essentially arbitrary. Now uses
    `ZoneData.CurrentBaseCamp`, and logs which anchor it used.
  - **`Hunter` fled toward South Horn's base camp.** On dropping combat it called
    `PathfindAndMoveTo(Aethernet.BaseCamp...)`, aiming the character at a point
    outside the zone.
  - **`BasePathfinder`'s base-camp branch never fired in North Horn.** The
    `aethernet == Aethernet.BaseCamp` comparison is false for
    `NorthHornBaseCamp`, so the Return-to-base-camp route was silently never
    considered when building a path. Now matches either horn.
  - **`PathfinderStep.Aethernet` defaulted to South Horn's base camp**, including
    on `ReturnToBaseCamp()` steps, which never set it explicitly.
- Added `ZoneData.CurrentBaseCamp` / `BaseCampFor()` / `IsBaseCamp()` so there is
  one place to get this right rather than five literals.

### Notes
- North Horn's starting location is approximated by its aetheryte position. South
  Horn's surveyed spawn sits ~21y off its aetheryte, so expect Return cost to be
  understated by roughly that much until the real spawn is observed. That is a
  small bias in one term, not the wrong-zone error above.
- North Horn events still have no curated `Aethernet` hint, so the shard chosen
  for an event is the straight-line nearest rather than a hand-picked one. With
  correct shard positions this is now reasonable, but terrain can still make it
  suboptimal on specific events.

## v0.0.2.0 (2026-08-01) [testing]

### Fixed
- **North Horn events were invisible in settings and crashed the Automator.**
  The config UI is driven by reflection over one hand-written `[Checkbox]`
  property per event, and only the South Horn set existed - so no North Horn
  FATE or Critical Encounter could be listed or selected. Worse, `Automator`
  indexed `FatesMap[fate.Id]` directly, so any live North Horn FATE raised
  `KeyNotFoundException` and took the loop down. Added all 28 missing properties
  (15 CEs, 13 FATEs) and made the lookup total. Maps now hold 30 CEs and 26 FATEs.
  Pot fates 2072/2073 default off and are flagged Experimental, matching how
  South Horn's Persistent/Pleading Pots are treated.

### Added
- **Phantom Dispellers replace demiatma for North Horn.** North Horn does not
  drop demiatma at all - it drops Phantom Dispeller α/β/γ (Item 50974-50976).
  New `PhantomDispeller` enum and an `EventData.Dispeller` field; the two are
  mutually exclusive per zone, so a North Horn event carrying a Demiatma value
  would be wrong by construction.
- **Real North Horn shard positions**, extracted from
  `bg/ex5/03_ocn_o6/btl/o6b2/level/planmap.lgb` rather than discovered at runtime.
  Shard identity was resolved by fitting the map-to-world transform against South
  Horn's five known shards and matching each MapMarker (icon 60959) to its nearest
  layout object - worst fit error 2.1 yalms, the rest under 1.1. Running the same
  extraction over South Horn reproduces upstream's hand-surveyed constants to
  within 0.03y, which is what makes these trustworthy. The North Horn base camp
  aetheryte is now a surveyed constant too.
  Confirmed names: North Horn Base Camp, Sinking Sanctuary, Suspended Masonry,
  Moldering Outskirts, Unhallowed Hamlet, The Crown of Karnak.
- **`DispellerObserver`** learns which event yields which dispeller by watching
  inventory, attributing only when exactly one event is active. This exists
  because the mapping is *provably* not in the game files: a scan of all 7,912
  Excel sheets found exactly nine references to dispeller and demiatma item ids
  in the entire client, and every one was in the Quest sheet - the relic quests
  that consume them (70855 South Horn, 71039 North Horn). No loot table, nothing
  on Fate or DynamicEvent. The drop is decided server-side, which is why
  upstream's South Horn table is hand-observed.

### Notes
- Correcting v0.0.1.0's claim that treasure and carrot radar are inert without
  the generated data files: they are not. Both trackers read the live object
  table, so radar works in North Horn immediately. Only the *optimal route*
  (`Pathfinder`) needs the precomputed JSON.
- Demiatma/Soulshard/Note remain unset on North Horn events - now known to be
  unobtainable from game data rather than merely not found.

## v0.0.1.0 (2026-08-01) [testing]

### Added
- **North Horn (territory 1346) support.** Upstream only ever knew about South Horn;
  every zone gate was a hardcoded `1252`. `ZoneData` is now multi-zone with
  `OccultTerritories` as the single source of truth, and the two modules that each
  carried their own duplicate `[1252]` list read from it.
- **North Horn event tables** (`Data/NorthHornEvents.cs`), datamined from the 7.55
  sqpack on 2026-08-01:
  - 13 FATEs, `Fate` sheet rows 2072-2084. Rows 2072/2073 carry Rule 4 (the pot-fate
    rule) and are tagged `MonsterNote.PersistentPots` so existing skip-pot-fates
    logic keeps working; 2074-2084 carry Rule 1.
  - 15 Critical Encounters plus both towers, `DynamicEvent` rows 49-65. This mirrors
    South Horn's 33-47 + 48 exactly.
  - `EventData.Fates` / `CriticalEncounters` are now merged views over both zones.
    Ids do not overlap between horns, so id lookups stay flat; `FatesFor(territory)`
    and `CriticalEncountersFor(territory)` exist for UI that lists rather than resolves.
- **Six North Horn aetheryte shards.** EObj BaseIds 2015429 (`occult aetheryte`) and
  2015430-2015434 (`aetheryte shard`), paired with PlaceName rows 5571-5576.
  `AethernetData` became a territory-scoped table instead of a flat switch, so
  cross-horn shards can no longer leak into pathfinding or the teleport UI.
- **Runtime zone discovery** (`Data/ZoneDiscovery.cs` + `Modules/ZoneDiscovery/`).
  Aetheryte and shard positions live in the LGB layout, not Excel, so they cannot be
  datamined - upstream solved this by hand-surveying, which is why a new zone was a
  human-with-a-notepad problem. Instead the object table is scanned every 2s for
  shards that have not been placed yet and the result is cached to the plugin config
  directory. A fresh zone bootstraps itself over the first lap.
- **All eight 7.55 phantom jobs** wired into `JobId`, `PlayerStatus` and `Job`.
  Job ids 16-23 are `MKDSupportJob` row ids (Ninja 16, White Mage 17, Black Mage 18,
  Dragoon 19, Summoner 20, Blue Mage 21, Red Mage 22, Necromancer 23); the matching
  "job equipped" statuses are the contiguous block 5328-5335 in the same order.
  Without these, `Job.ChangeToChain()` would wait forever on a status that never
  arrives.
- **GluttonyCombo as a rotation provider** (`Modules/MobFarmer/Gluttony.cs` +
  `IPC/GluttonyCombo.cs`). GluttonyCombo is a Wrath fork exposing the same lease IPC
  under its own prefix. It is tried *before* Wrath Combo because it is currently the
  only rotation plugin implementing the 7.55 phantom jobs - a Wrath-driven farm loop
  does nothing at all in North Horn on eight of the twenty-four jobs. All 24
  `Phantom_<Job>` presets are mapped, against upstream BOCCHI's single Cannoneer entry.

### Fixed
- **Data generators sized their work off the wrong shard count.** `TreasureHuntPanel`
  and `CarrotHuntPanel` computed `MaxProgress` from
  `Enum.GetNames(typeof(Aethernet)).Length`, which now spans both horns, while the
  sweep itself iterates the zone-scoped `AethernetData.All()`. The progress bar would
  have stalled short of 100% on a sweep that had actually finished. Both now use the
  same scoped source.
- **Silent empty generator runs.** A zone whose chests do not match the hardcoded
  SGB filter (1596/1597) produced a structurally valid, completely empty JSON and
  reported success - indistinguishable from a real run until you are stood in game
  looking at a blank radar. Both generators now log raw layout instance count against
  post-filter count, refuse to start on an empty node set, and say why in the panel.
- Replaced five obsolete Dalamud API calls in the debug panels
  (`YalmDistanceX`/`YalmDistanceZ`/`YalmDistanceFromPlayerX`/`YalmDistanceFromPlayerZ`/
  `TargetStatus`). Build is 0 warnings, 0 errors.

### Changed
- Renamed throughout: namespace `BOCCHI` -> `LazyOccultCrescent`, commands
  `/bocchi*` -> `/lazyoccult*` with `/lazyoc*` aliases. Upstream shipped a duplicate
  alias (`/ochillegal` and `/bocchillegal` both mapped to the same command); collapsed
  to one so registration cannot throw.
- Ocelot is consumed from the `FFXIVOcelot` NuGet package in every configuration.
  Upstream used a git submodule in Debug and NuGet in Release; the submodule is
  deliberately not vendored.

### Notes
- **Not yet verified in game.** Built and reasoned about, not played. Everything
  below is unproven until someone stands in North Horn:
  - Zone data files (`Data/NorthHorn/`) do not exist yet - they are produced by the
    in-game generators. Treasure and carrot radar are inert until then.
  - The PlaceName-to-BaseId pairing order for the six shards is inferred from
    contiguity, not observed. If a shard renders with the wrong name that mapping is
    the thing to correct; positions are self-correcting via discovery.
  - Demiatma / Soulshard / MonsterNote for North Horn events are deliberately left
    null rather than guessed. They come from drop tables, not Excel, and a wrong
    mapping would send the Automator to the wrong side of the map.
  - `Data/Traps/` still describes Forked Tower: Blood. FT:Magic is a different tower
    and needs its own survey.
