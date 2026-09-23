import json, os
D=os.path.dirname(os.path.abspath(__file__))
M=json.load(open(f'{D}/crucible_map.json')); E=json.load(open(f'{D}/enums.json'))
out=[]; w=out.append

w("""# Crucible of the Unbroken: game-data map (FFXIV 7.56)

Source: xivapi v2, game version key `f5af21155b99a524` (7.56), schema `exdschema@2` rev `f3cacf3b`. All values were pulled live; raw sheet dumps are in `raw/`. Machine-readable outputs:
- `crucible_map.json`: boards, then nodes, battles, enemies, panel actions, with every ID and decoded enum.
- `detection.json`: TerritoryType/CFC to board, and BNpcName ID to (board, battle).
- `enums.json`: decoded enum tables.
- `build.py` / `report.py` regenerate the outputs from `raw/`. `xiv.py` is the fetch helper.

Notation: `Sheet#row` or `Sheet#row:subrow`. **[INFERRED]** = not stated by data or schema; the reasoning is given next to it. **[GUIDE-CHECKED]** = an inference that matches the Icy Veins board 1-2 guide, which was used only to decode meanings.

## 1. Boards

| XBMContent# | Board | CFC# | TerritoryType# (name, bg) | PlaceName# | Maps# | Lvl sync | iLvl sync | SynkRank | RecommendedRank (raw) | TeamSize | Time limit | Unlock quest | Nodes | Battles |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|""")
for b in M['boards']:
    maps=f"{b['maps'][0]['map_id']}-{b['maps'][-1]['map_id']}"
    w(f"| {b['xbm_content_row']} | {b['name']} | {b['cfc_id']} (`{b['cfc_short_code']}`) | {b['territory_type_id']} (`{b['territory_name']}`, `{b['territory_bg']}`) | {b['place_name_id']} | {maps} | {b['class_job_level_sync']} | {b['item_level_sync'] or '-'} | {b['synk_rank']} | {b['recommended_rank_raw']} | {b['team_size_max']} | {b['time_limit_min']} min | Quest#{b['unlock_quest']['id']} {b['unlock_quest']['name']} | {b['node_count']} | {len(b['battles'])} |")
w("""
Common to all five boards:
- `ContentFinderCondition.ContentType` = 40 (`ContentType#40` "●XBM").
- `ContentMemberType` = 1. `AcceptClassJobCategory` = 203 (BST only).
- `InstanceContent#56001-56005`, with `InstanceContentType` = 22.
- `TerritoryType.TerritoryIntendedUse` = **62**. An xivapi search finds exactly these 5 territories with 62.
- `TerritoryType.ContentFinderCondition` = **0** on these rows, so the territory-to-CFC link in the sheet is empty.
- `ExclusiveType` = 1, `BattalionMode` = 1.
- Row `XBMContent#0` is a template (SynkRank 255).

Field notes:
- **SynkRank** is the beast-rank sync. It matches the guide's "Beast Rank 5/10/15/20/25".
- **TeamSize** is the maximum number of familiars in the team. Addon#17787 says "team is smaller than the maximum allowance". [GUIDE-CHECKED] The guide's full loadouts are 10 beasts for board 1 and 12 for board 2.
- **RecommendedRank**: the raw values are 3/8/13/18/23. The EXDSchema comment says the exe subtracts 2. Displayed meaning not verified.
- **BonusPoints[33]** is indexed by `XBMScoreBonus` row. A value of 0 means that bonus is unavailable on that board (see `crucible_map.json` `bonus_points`).
- **XBMEntrance#1-5**: `Unknown0` = unlock Quest ID (equals `CFC.UnlockCriteria`), `Unknown1` = XBMContent row, `Unknown2` = always 1 (unknown).
- Each territory has **5 Map rows**:
  - `<bg>/01` has offset (700,y) and a MapMarkerRange. **[INFERRED]** This is the board-layout map: `MapMarker#747-751` subrows carry `DataKey` = node index and node-type icons 63850-63856.
  - `/02-/05` sit at 4 offsets in a 2x2 grid. **[INFERRED]** These are 4 physical arenas shared by all battles on that board. No sheet maps battle to arena.

## 2. How the sheets link

```
XBMContent#board ── ContentFinderCondition ── TerritoryType / InstanceContent
XBMContentStageEvent#board:node   U0=StageEventType, U1=depth(row on board), U2=type_index, U3=? (sequential)
   type 2 Enemy / 3 Elite / 4 Boss  -> XBMContentBattle#board:type_index -> BattleDetail -> XBMBattleDetail#bd:enemy
   type 6 Campsite                  -> XBMContentCamp#board:type_index (FamiliarRecoverCount)
   type 8 Random                    -> XBMContentRandomStageEvent#board:type_index -> RandomStageEvent
                                        -> XBMRandomStageEvent#row:outcome (U0=type, U1=type_index)
   type 5 Shop / 7 Treasure         -> type_index refers to no client sheet found
XBMBattleDetail#bd:enemy  Name->BNpcName, Unknown1=portrait icon, Unknown2/Unknown3->XBMBattleDetailAction,
                          Resist->BNpcResist, Unknown5..9 = 1-5 star ratings, Element->XBMElement (weakness)
XBMBattleDetailAction#row Action, Status, ActionTarget->XBMActionTarget, ActionEffectType->XBMActionEffectType
XBMContentStageEventMap#board:n  U0=x, U1=y, U2=glyph (1=node, others=connector), U3/U4 = node indices (edge U3->U4)
```
Key facts and evidence:
- **Battle-to-board key**: the `XBMContentBattle` row ID *is* the board (XBMContent row). Its subrow is the battle index. **Subrow 0 is always the boss** (StageEvent type 4 has `type_index` 0 on every board).
- **Every battle subrow is reachable.** The fixed nodes plus random-node outcomes cover exactly all subrows on every board, and the same holds for camps.
- **Random-only battles** (never a fixed node): 2:6, 3:7, 4:3, 4:4, 4:6, 4:9, 5:3, 5:4, 5:7, 5:11, 5:12.
- **Stage event type** (StageEvent.U0) is decoded by Addon#17539+type:
  - 17540 start ("スタート地点です")
  - 17541 Enemy #, 17542 Elite Enemy #, 17543 Boss
  - 17544 Shop, 17545 Campsite, 17546 Treasure, 17547 Random
- Cross-checks on the stage event types:
  - Type 6 counts match `XBMContentCamp` subrows (Addon#17545 "Recover HP for yourself and N familiar" = FamiliarRecoverCount).
  - Type 8 counts match `XBMContentRandomStageEvent` subrows exactly on all 5 boards.
  - The `MapMarker#747-751` icon per node is consistent with type: 63850 camp, 63851 shop, 63852 treasure, 63853 random, 63854 enemy, 63855 elite, 63856 boss.
  - [GUIDE-CHECKED] On board 1, "left at first fork, right at second fork = two camps" matches the map graph (x=5 left, x=7 right).
- The board graph (edges, x/y) is in `crucible_map.json` under `nodes[].map_xy` and `edges_from_to`.

## 3. Decoded enums
""")
w("| XBMActionTarget# | text (sheet string) | | XBMActionEffectType# | text (sheet string) |\n|---|---|---|---|---|")
at=E['XBMActionTarget']; ae=E['XBMActionEffectType']
for i in range(max(len(at),len(ae))):
    a=at.get(str(i),''); e=ae.get(str(i),'')
    w(f"| {i if str(i) in at else ''} | {(a if a else '(blank)') if str(i) in at else ''} | | {i} | {e} |")
w("""
Both are plain string lists in 7.56, so these meanings are **data, not inference**.
- ActionTarget 0 = blank label. It is used with Front, Universal, etc. **[INFERRED]** It means the current target or facing.
- EffectType 0 = "???". It appears on Toxic Fumes 48796, Gigantic Rage 49359, Pom Holy 49387, and Roulette 49418.

| XBMElement# | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|
| name | Fire | Wind | Earth | Lightning | Ice | Water | Blunt | Piercing | Slashing |

`XBMBattleDetail.Element` is the enemy's **weakness**: Addon#17753 "Weakness:". 0 = none shown (Guttler, Lauda, gargoyle, gigantis, cyclops, light sprite).

[GUIDE-CHECKED] The recommended damage type matches on all 12 guide encounters:
- Bone knight/bishop: Blunt. Arch demon: Lightning. Piscodemon: Slashing. Banemite: Ice. Pas de Seul: Piercing.
- Manticore: Wind. Taurus: Piercing. Wyvern: Lightning. Voidmancer: Earth. Tablitaur: Fire.
- Loosefrox: Blunt. Chewchum: Water.

**BNpcResist.Unknown0[11]** ("Vulnerabilities:", Addon#17754): `true` = immune/resists, `false` = vulnerable. **[INFERRED]** Index order:

| idx | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| status | Slow | Petrification (Deep Freeze?) | Paralysis | **Silence/Interrupt** | Blind | Poison | Stun | Sleep | Bind | Heavy (Disease?) | Doom |

Evidence for the index order and polarity:
- **Index order.** EXDSchema says `XBMPet.InflictsStatus` uses the same indexer. The beast-tooltip keywords co-occur with each index:
  - Slow 3/3, Paralysis 3/3, Blind 3/3 (+2 accuracy-down beasts), Poison 6/11, Stun 1/1, Bind 2/2, Doom 2/2.
  - Idx3 is set on Golem, Ice Golem, and Spriggan. The guide names Golem and Ice Golem as Soul Crush interrupt sources.
  - Idx1 appears with Petrification ×2 and Deep Freeze ×1. Idx9 appears with Heavy ×2 and Disease ×2.
- **Polarity.** Boss rows are all-true (`BNpcResist#1`), while trivial adds are mostly false.
- **Decisive check.** Across all 122 panel enemies, an enemy has idx3 = false **if and only if** it has a panel action with `Action.Unknown15` = true. That is 12/12 both ways.

**Action columns used**
- `AttackType` (sheet text is JP): 1 slashing, 2 piercing, 3 blunt, 4 shot, 5 magic, 6 breath, 7 sound, 8 LB.
- `Aspect`: 1 Fire, 2 Ice, 3 Wind, 4 Earth, 5 Lightning, 6 Water, 7 Unaspected, 0 none. Checked against Blizzard, Sand Breath, Water, Ancient Aero, Void Thunder III.
- `CastType`: 1 single, 2 circle, 10 donut, 11 cross, 12 line, 13 cone.

**Interruptible = `Action.Unknown15`** (EXDSchema column after `PreservesCombo`). **[INFERRED]**, strongly corroborated:
- [GUIDE-CHECKED] true on Ossify 46871 and Rallying Cheer 48203, which the guide calls interruptible.
- false on Clear Mind 46898 and Kinborrow 48242, which the guide calls dispellable but not interruptible.
- 100% consistent with BNpcResist idx3.
- Across the whole Action sheet (CSV dump from the earlier BST research) it is true on 1,990 actions, mostly player spells, so it looks like a "spell / silence-able / interruptible" flag.
- At runtime, prefer Dalamud `IBattleChara.IsCastInterruptible`.

**Cleansable debuff** = `Status.StatusCategory==2 && Status.CanDispel`. [GUIDE-CHECKED] Windburn 4880 (Ancient Aero) and Poison 5140 (Deadly Thrust, Cold Caress) are CanDispel=true, and the guide calls them cleansable. Heavy 5356 (Silkscreen) and Doom 5421 are false, and the guide never calls those cleansable.

**Dispellable enemy buff** = `Status.StatusCategory==1 && Status.Unknown8`. **[INFERRED]**
- `CanDispel` is false on every enemy buff, so it cannot be the flag.
- `Unknown8` is true on 1225 Damage Up (Clear Mind, Rallying Cheer, Fanaticism), 5423 Popoto Skin (Kinborrow), and 2074 Physical Damage Up (Ossify). The guide calls those dispellable.
- Sheet-wide, `Unknown8` is true only on category-1 statuses (591 rows).
- It is false on 1572 (Might), 5434 Paralyzing Spikes, 2413 Covered, 390, 616, 3129, 5145, 2550. Those may be non-dispellable, but that is unverified.

**XBMBattleDetail.Unknown5-9** are 1-5 values. **[INFERRED]** They are star ratings (Addon#17761 "★") in the beast stat order STR/INT/PHY R/MAG R/CON (Addon#17939-17943, `XBMPetParamGrow` has 5 fields). Bosses Guttler and Lauda are 5/5/5/5/5.

`suggested_responses` in the JSON is a heuristic built from the rules above:
- interrupt = Unknown15
- dispel = buff + Unknown8
- cleanse = debuff + CanDispel
- move = shaped AoE (Front/Rear/Lateral/Circle/Ring/Cross) not aimed at Allies
- aggro_swap_candidate = Highest Enmity + Single Target

Universal = unavoidable; nothing is suggested for it.

## 4. Encounters (panel data)

Action cells use this format: `ActionID Name [Target/Area, cast s] +Status (id name, D=CanDispel, U8=Unknown8) ⇒ responses`. Vulnerable = BNpcResist bits that are false.
""")
for b in M['boards']:
    w(f"\n### {b['name']} (XBMContent#{b['xbm_content_row']}, TT {b['territory_type_id']}, CFC {b['cfc_id']})\n")
    w("Nodes (StageEvent subrow:type[depth]): " + ", ".join(f"{n['node']}:{n['type']}[{n['depth']}]" + (f"->{n['xbm_content_battle']}" if 'xbm_content_battle' in n else "") + ("->{"+"|".join((o['type']+(':'+o['xbm_content_battle'] if 'xbm_content_battle' in o else f":{o['type_index']}")) for o in n['random_outcomes'])+"}" if 'random_outcomes' in n else "") for n in b['nodes']))
    w("\n| XBMContentBattle# | XBMBattleDetail# | Role / reached via | BNpcName# | Name | Weak | BNpcResist# → vulnerable | Panel actions |\n|---|---|---|---|---|---|---|---|")
    for bt in b['battles']:
        for i,e in enumerate(bt['enemies']):
            acts=[]
            for a in e['panel_actions']:
                s=a['status']; st=''
                if s:
                    fl=[x for x,c in (('D',s['CanDispel']),('U8',s['Unknown8_dispellable_buff_INFERRED'])) if c]
                    st=f" +{s['id']} {s['name']} ({'buff' if s['category']=='beneficial' else 'debuff'}{','+','.join(fl) if fl else ''})"
                tgt=a['panel_target']['text'] or '-'
                acts.append(f"{a['action_id']} {a['name']} [{tgt}/{a['panel_area']['text']}, {a['cast_s']}s]{st}" + (f" ⇒ {','.join(a['suggested_responses'])}" if a['suggested_responses'] else ""))
            vul=', '.join(e['vulnerable_to_INFERRED']) or 'none'
            role=f"{bt['role']} / {'; '.join(bt['reached_via'])}" if i==0 else ''
            w(f"| {bt['xbm_content_battle'] if i==0 else ''} | {e['xbm_battle_detail']} | {role} | {e['bnpcname_id']} | {e['name']} | {e['weakness']['text'] or '-'} | {e['bnpcresist_row']} → {vul} | {'<br>'.join(acts) or '-'} |")

w("""
## 5. Runtime detection recommendations

**(a) In Crucible**
- **Primary:** `IClientState.TerritoryType` ∈ {1339, 1340, 1341, 1342, 1343}.
  - These are the only TerritoryType rows with `TerritoryIntendedUse` 62, and each is `CFC.TerritoryType` for exactly one XBM CFC.
  - Future-proof variant: look up `TerritoryType.TerritoryIntendedUse == 62` via Lumina, which catches new boards added in later patches.
- **Secondary:** current CFC (e.g. `GameMain.Instance()->CurrentContentFinderConditionId`) ∈ 1088-1092, or `CFC.ContentType == 40`. Don't derive CFC from `TerritoryType.ContentFinderCondition`, which is 0 for these rows.
- **Tertiary** confirmation only: duty action slots containing Challenge 46750 / Snarl 46751. They depend on load timing and are not a location signal.

**(b) Which board**
- Board is 1:1 with territory: 1339→1, 1340→2, 1341→3, 1342→4, 1343→5 (or CFC 1088→1 … 1092→5).
- The territory `Name`/bg code `f1x1`…`f1x5` matches CFC `ShortCode`.

**(c) Which encounter**
- Scan hostile `IBattleNpc`s (SubKind 5, Enemy/Combatant) and look up `NameId` in `detection.json` `bnpcname_to_encounters`.
- **All 122 panel BNpcName IDs (14531-14693, 14699, 14748) map to exactly one (board, battle)**, and no ID is reused across battles or boards. Any one matched hostile identifies the battle.
- **Match by ID, never by name string.** Display names are shared between different IDs:

| name | BNpcName IDs (battle) |
|---|---|
| bone bishop | 14532 (1:1), 14567 (3:1) |
| golem piece | 14581 (3:5), 14617 (4:7) |
| bomb piece | 14625 (4:9), 14640 (5:3) |
| lightning sprite | 14632 (5:1), 14686 (5:12) |
| cyclops piece | 14662 (5:7), 14671 (5:10) |
| Thanatos piece | 14595 (3:0), 14699 (5:0) |

- Non-panel BNpcName IDs in the same block are mechanics/adds with no XBMBattleDetail entry. Treat them as "Crucible object", not as an encounter key. Examples:
  - bone knight 14566 (same text as panel 14531), bone fragment 14568, gobbie bomb 14563, abyssal lance 14534
  - zu egg 14575/14576, aetheric charge 14547/14560/14650, combusting/molten blade 14593/14594 and 14694/14695
  - dodo/pugil/opo-opo/puk piece 14666-14669, grim reaper piece 14690, final hourglass 14689, etc.
- The Addon role label ("Enemy/Elite Enemy/Boss") comes from the StageEvent node type. Battle subrow 0 is always the boss.
- `IClientState.MapId` (/02-/05 arena maps) is a weak hint only, because 4 arenas are shared across 6-14 battles.

**(d) Response triggers**
- **Interrupt:** use `IBattleChara.IsCasting && IsCastInterruptible` as the live signal. The data flag `Action.Unknown15` gives the same answer for panel casts.
  - Interruptible panel casts (12): Ossify 46871, Fanaticism 46928, Rallying Cheer 48203, Dreadwash 48485, Caustic Vomit 48489, Confound 48740, Natural Nurture 48782, Massive Explosion 48802, Raise 49287, Camaraderie 49368, Rupture 49373/49375.
- **Dispel:** an enemy has a status with category 1 + `Unknown8`: 1225, 2074, 2528, 5020, 5423, 5465.
- **Cleanse:** you or your pet has category 2 + `CanDispel`: 4880, 5140, 5141, 5381, 5382, 5495, 5498, 5500.
  - Doom 5421 (Mortal Ray/Gaze) is NOT CanDispel. The guide says it is cleansed by floor tiles.
- **Move:** by `XBMActionEffectType` of the casting action (see the tables).
- **Aggro swap:** `ActionTarget` 3 "Highest Enmity" + Single Target casts, e.g. Cold Caress 46935, Void Paralyze 48247, Caustic Vomit 48489, Final Sting 48689. Deadly Hold 48138 is labeled Player/Single Target, but the guide says to let the pet tank it. Forward Guard (directional parry, swap to get behind) is not in the panel. Both must be hand-added.
- **Caveat:** the panel lists only ≤2 actions per enemy (`XBMBattleDetail.Unknown2/Unknown3`). Many casts in the guide are absent from game data and need a cast-ID list from logs: Forward Guard, Black Eruption, Dismember, Mindjack, Liquid Hell, Burning Ward, Beguiling Mist, Gobbieboom Barrage, etc.

## 6. XBMItem / XBMScoreBonus (brief)

**XBMItem#1-203** (rows 0, 204, 205 are empty; `Type` → `XBMItemType`):
- **Beast Gear** (type 1): rows 1-75, e.g. Belt of Constitution, Ring of Sacrifice, Coward's Knife, Thief's Knife, Master Shield, Hero's Crown.
- **Crucible Items** (type 2): rows 76-143, including:
  - G1-G4 Beast Potion, G1-G3 Crucible Ash, Antidote/Needle/Eye Drops
  - Anti-X Soul Serums, Blessed Horn (revive familiar), Beastly Smokebomb (flee), Reraisers
  - Elemental Fangs, Tomes, Temporal Sand, Spellforge/Steelsting Tome
- **Feed** (type 3): rows 144-203, including Primafodder, the "* Simular" foods, Magnum Water, Crucible Tonic, and Untaming Oil.
- Useful field: `SellPrice` is in territory tokens. Descriptions are in `raw/XBMItem.json`.

**XBMScoreBonus#0-32**:
- Willing Sacrifice I-III, No Beast Left Behind, Beast Squad I-III, Self-preservation, Selfless
- Infinity and Beyond I-III (AlwaysActive=false), Unbeastable, Nameday Attire, Walking Armory
- Economic Ethos, Habitual Hoarder, Starve the Fever, Feed the Bold, Coin Clutcher, Rolling in Riches, Sleepless Survival
- Beastkin/Vilekin/Cloudkin/Seedkin/Wavekin/Scalekin/Soulkin/Ashkin Gambit
- First/Second/Third Degree (AlwaysActive=false)

**XBMScoreRank#0-8**: Legendary, Apex, Elite, Renowned, Exemplary, Adept, Journeyman, Novice, Apprentice.

## 7. Unknown / could not decode

1. **BNpcBase IDs** for any Crucible enemy. They are not referenced by any XBM sheet (spawns are server/LGB-side). Log `DataId`/BaseId at runtime.
2. **Battle → arena map** (Map /02-/05) assignment. No sheet link was found.
3. `XBMContentStageEvent.Unknown3`: sequential per board (subrow+2, +7, +5, +2, +2 for boards 1-5). It is not the MapMarker subrow. Purpose unknown (maybe a layout/event-range instance).
4. `XBMStageEventType.Unknown0`: values by type are start 0, enemy 3, elite 2, boss 1, shop 5, camp 6, treasure 4, random 7. **[INFERRED]** A display/sort priority; unverified.
5. Shop (type 5) and Treasure (type 7) `type_index`: no client sheet found for stock/loot tables.
6. `XBMContentStageEventMap.U2` connector glyph codes (4-10): only "1 = node" and edge endpoints (U3→U4) are decoded.
7. `XBMEntrance.Unknown2` (always 1). `XBMContent.RecommendedRank` display semantics (raw 3/8/13/18/23; schema says the exe subtracts 2).
8. BNpcResist index **1** (Petrification vs Deep Freeze) and index **9** (Heavy vs Disease) are the weakest mappings. Indices 0, 2-8 and 10 have direct keyword matches, and index 3 is proven by the Unknown15 cross-check.
9. `XBMBattleDetail.Unknown5-9` stat order is inferred, not confirmed.
10. `Status.Unknown8` = dispellable and `Action.Unknown15` = interruptible are **[INFERRED]** (§3), though well corroborated.
11. `XBMActionEffectType` 0 "???" on 4 actions: its shape is literally unknown in data.
12. `XBMBattleDetailAction#34` (48157 Ruinous Ring, Self/Ring) and `#84` (48737 Explosive Aether, Self/Circle) are not referenced by any XBMBattleDetail row. They are possibly cut from the panel, but the casts may still occur. Ruinous Ring is a Taurus piece cast in the guide; Explosive Aether is likely from the board-4 machina fight.
13. Casts that exist in fights but not in the panel data (see §5d caveat).
""")
open(f'{D}/REPORT.md','w').write('\n'.join(out))
print(len('\n'.join(out)))
