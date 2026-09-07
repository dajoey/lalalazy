# LazyGearCollector — Changelog

## v0.0.2.0 (2026-09-05)

- Added the in-game "What's new" popup. After Lazy Gear Collector updates, its changelog now opens once inside the game so the changes are visible without a trip to GitHub. It waits until the character is logged in and out of combat, duty, cutscenes and zoning; closing it (Got it, X or Escape) marks it read. Type `/lazygear changelog` any time to reopen it.
- No change to collection tracking: the Phantom Vision set, tier detection, ownership counting and the remaining-cost figures are all unchanged.

## v0.0.1.0 (2026-07-30)

### Added
- **First release.** Tracks upgradable gear collections: which pieces are owned, what tier each one
  is at, and exactly what it costs to finish.
- **Phantom Vision (Occult Crescent: North Horn, patch 7.55)** as the first collection — 7 role sets
  x 5 armour slots x 4 tiers (base / +1 / +2 / +3), 140 item IDs.
- **Runtime shop-derived pricing.** `ShopGraph.cs` builds a reverse index over the entire
  `SpecialShop` sheet at load ("what trades produce item X, at what price"), and
  `FamilyProvider.cs` walks it to discover every tier's cost. No item IDs, prices or set contents
  are hardcoded, so the numbers stay correct when Square Enix retunes a patch.
- **Arcanaut's trade-up detection** (`Planner.DetectShortcut`). Arcanaut's +1 and +2 exchange into
  Phantom Vision +1/+2 for free. Base Arcanaut's has no exchange, so the planner also spots the
  two-step route — upgrade it in South Horn first, then trade in — which saves 4,000 Silver Obols
  and 3 Fixative per piece over buying into the set fresh.
- **Ownership scanning** across bags, armoury chest and equipped gear (live), plus opportunistic
  snapshots of saddlebag and retainer containers, replayed from config with a "last seen"
  timestamp so the UI never passes off remembered data as live.
- Role roster UI with click-through to per-piece detail, a wallet strip, per-role and
  whole-collection shortfalls, and a selectable goal tier. `/lazygear` to open.

### Notes
- **The glamour dresser cannot be enumerated** at Dalamud API level 15 — `MirageManager` exposes
  only `RestorePrismBoxItem`, with no readable item array. Pieces parked in the dresser will show
  as missing. This is stated in the UI rather than silently undercounted.
- `ItemCostsStruct.CostType` is honoured when reading shop costs. `CostType == 0` means the cost
  row is a real Item; `CostType == 2` is a special-currency *index*. Treating type 2 as an item is
  what makes tomestone shops decode as nonsense like "Ice Shard x495" — those rows are skipped.
- Reference data behind this release, including the full item-ID matrix and the shop table, is in
  the Cowork FFXIV Mods workspace as `north-horn-datamine.md`.

### Verification
- Release build: 0 warnings, 0 errors.
- The set-construction and pricing logic was re-implemented independently against a raw Lumina dump
  of the live 7.55 sheets and asserted: 35 chains / 140 items / 7 roles x 5 slots, tiers 0-3
  complete, currencies resolve to exactly {Enlightenment Silver Obol, Final Final Fixative},
  base = 4,000 obol, upgrades = 3 / 4 / 8 Fixative, trade-ups present only at +1 and +2 and only
  from Arcanaut's, collection total = 140,000 obol + 525 Fixative. 18/18 checks passed, including a
  negative control for the lazy-regex family-split bug caught pre-release.
