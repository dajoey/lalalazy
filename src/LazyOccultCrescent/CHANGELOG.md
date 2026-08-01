# LazyOccultCrescent

Fork of [OhKannaDuh/BOCCHI](https://github.com/OhKannaDuh/BOCCHI) (AGPL-3.0-or-later),
forked at `ded40a71af051a3aa57d326c512a975e7957daf6`. Upstream copyright and licence
are preserved in `LICENSE`.

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
