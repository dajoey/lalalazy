# Changelog — Lazy Skyward Tracker

## v0.0.2.2 (2026-06-26)

### Fixed
- **Gathered Diadem items are now credited to the correct gathering class.** Miner
  vs. Botanist was decided by a fragile item-name keyword heuristic
  (`LooksLikeMinerItem` in `InventoryScanner.cs`, matching "ore/stone/sand" etc.),
  which misfiled every quarried Miner material that lacks such a keyword —
  Spring Water, Truespring Water, Cloud Drop Water, Basalt, Jade, Lutinite, Alumen,
  Clay, Granite, Silex, Ice Stalagmite, Fossil Dust, Basilisk Egg, and the Umbral
  Levinshard / Levinite / Magma Shard line — into the **Botanist** projection.
  18 point-bearing Miner items (Grades 2-4, including their Artisanal forms) were
  affected, so mining those collectables moved the Botanist bar instead of Miner.

### Changed
- **Class is now read from the game's own data.** `BuildGathererLookup` derives the
  achievement from the `HWDGathererInspection` row index (row 1 = Miner -> 2515,
  row 2 = Botanist -> 2518, row 3 = Fisher -> 2521) instead of guessing from the
  item name. The `LooksLikeMinerItem` heuristic was removed.

### Notes
- No change to which items count or their point values. Raw, non-collectable node
  materials (e.g. Grade 4 Skybuilders' Iron Ore) still award 0 Skyward achievement
  points per the game's `HWDGathererInspection` data — they yield Skybuilders'
  Scrips, not Pteranodon progress, so they correctly do not move any bar.
