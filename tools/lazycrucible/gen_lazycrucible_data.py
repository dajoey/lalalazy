#!/usr/bin/env python3
"""Generate LazyCrucible's item table and board graphs from crucible_items_boards_7.56.json.

The JSON is a slice of the FFXIV 7.56 game sheets (xivapi v2, decoded 2026-09-16):
  - XBMPet rows 1-50: SatietyMax (how many feeds a familiar can eat in one run).
  - XBMItem rows 1-203: name, type (1 beast gear, 2 Crucible item, 3 beast feed), sell price and the
    tooltip text. Every number the selection policies use (max HP %, damage taken %, vulnerabilities,
    resistances, heal %, kin suitability, self-inflicted drawbacks) is parsed from that tooltip text here,
    so the policies never carry a hand-typed item effect.
  - XBMContentStageEvent / StageEventMap / Camp / RandomStageEvent: every board's spaces (type, depth,
    battle, campsite familiar count, random-space outcomes) and the edges between them.
Only the item ROLE (what an item is for: heal, cure, revive, gear, feed...) is assigned by hand below,
from the item's name and tooltip.

Usage: python3 tools/lazycrucible/gen_lazycrucible_data.py
Writes src/LazyCrucible/Data/CrucibleItems.Generated.cs and src/LazyCrucible/Data/CrucibleBoards.Generated.cs.
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(HERE, "crucible_items_boards_7.56.json")
OUT_ITEMS = os.path.join(REPO, "src", "LazyCrucible", "Data", "CrucibleItems.Generated.cs")
OUT_BOARDS = os.path.join(REPO, "src", "LazyCrucible", "Data", "CrucibleBoards.Generated.cs")

KINS = {"beastkin": 1, "vilekin": 2, "cloudkin": 3, "seedkin": 4, "wavekin": 5, "scalekin": 6, "soulkin": 7, "ashkin": 8}
STATUS = {"Poison": "Poison", "Paralysis": "Paralysis", "Blind": "Blind", "Petrification": "Petrify",
          "Sleep": "Sleep", "Doom": "Doom", "Stun": "Stun"}

# Item role by row (type 2 Crucible items and a few gear pieces whose job is not a stat line).
ROLE_BY_ROW = {
    2: "SelfRevive",       # Ring of Sacrifice
    3: "AfterBattleHeal",  # Ring of Curing
    4: "CampPotion",       # Chemist's Satchel
    31: "Flee",            # Coward's Knife
    57: "ExtraTreasure",   # Thief's Knife
    58: "Score", 59: "Score", 60: "Score",  # Thief's Garb/Gloves/Boots (loot rate)
    67: "Score", 68: "Score", 69: "Score",  # Merchant's Cap/Garb/Shoes (tokens)
    76: "Heal", 77: "Heal", 78: "Heal", 79: "Heal",
    80: "HealParty", 81: "HealParty", 82: "HealParty",
    83: "Cure", 84: "Cure", 85: "Cure",
    86: "Serum", 87: "SerumParty", 88: "Serum", 89: "SerumParty", 90: "Serum", 91: "SerumParty",
    92: "Serum", 93: "SerumParty", 94: "Serum", 95: "SerumParty",
    96: "Revive", 97: "Flee", 98: "Reraise", 99: "Reraise",
    100: "Score", 101: "Score",
    102: "Mitigation", 103: "Offense", 104: "Offense", 105: "Feral", 106: "Offense", 107: "Feral",
    108: "Offense", 109: "Feral", 110: "Offense", 111: "Feral", 112: "MaxHpBuff", 113: "Feral",
    114: "SwiftPoisonResist", 115: "Offense",
    116: "Absorb", 117: "Absorb", 118: "Absorb", 119: "Absorb", 120: "Absorb", 121: "Absorb",
    122: "Absorb", 123: "Absorb", 124: "Absorb", 125: "Absorb", 126: "Absorb", 127: "Absorb",
    128: "Fang", 129: "Fang", 130: "FangDispel", 131: "Fang", 132: "Fang", 133: "Fang", 134: "Fang",
    135: "Offense", 136: "Mitigation", 137: "Mitigation", 138: "Rewind", 139: "Fang", 140: "AutoHeal",
    141: "AutoCure", 142: "Offense", 143: "Offense",
}
CURE_STATUS = {83: "Poison", 84: "Petrify", 85: "Blind"}


def cs_string(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def pct(pattern, text, sign=1):
    m = re.search(pattern, text)
    return sign * int(m.group(1)) if m else 0


def parse_item(it):
    d = it["description"].replace("\n", " / ")
    row, typ = it["row"], it["type"]
    maxhp = pct(r"Maximum HP \+(\d+)%", d) - pct(r"Maximum HP -(\d+)%", d)
    dt = pct(r"Damage Taken -(\d+)%", d)  # reduction, positive = good
    pv = pct(r"Physical Vulnerability -(\d+)%", d)
    mv = pct(r"Magic Vulnerability -(\d+)%", d)
    dd = max(pct(r"(?<!Physical )(?<!Magic )Damage Dealt \+(\d+)%", d), 0)
    pdd = pct(r"Physical Damage Dealt \+(\d+)%", d)
    mdd = pct(r"Magic Damage Dealt \+(\d+)%", d)
    heal = 0
    for p in (r"Restores (\d+)% of HP upon consumption", r"Restores (\d+)% of HP to self or familiar",
              r"Restores (\d+)% of HP to self and nearby allies", r"restoring (\d+)% of HP"):
        heal = max(heal, pct(p, d))
    resists = [STATUS[s] for s in STATUS
               if re.search(s + r" Resistance \+100%", d) or re.search(r"(?:Increases|and) " + s.lower() + r" resistance by 100%", d)]
    if row in CURE_STATUS:
        resists = []
    hazards = []
    if re.search(r"Maximum HP -\d+%", d):
        hazards.append("MaxHpDown")
    if re.search(r"Slow \+\d+%", d):
        hazards.append("Slow")
    if "damage over time on self" in d:
        hazards.append("SelfDot")
    if re.search(r"(chance to inflict|Inflicts) (Poison|Paralysis|Petrification|Stun|Doom|Nightmare|Blind|Chains of Condemnation) on self", d):
        hazards.append("SelfStatus")
    if re.search(r"Movement Speed -\d+%", d):
        hazards.append("MoveSpeedDown")
    if re.search(r"^Inflicts (Stun|Nightmare|Petrification|Blind|Pollen) while", it["description"]):
        hazards.append("SelfStatus")
    kins = 0
    m = re.search(r"\[Suitable for ([^\]]+)\.\]", d)
    if m:
        for k in re.split(r",\s*(?:and\s+)?|\s+and\s+", m.group(1)):
            k = k.strip()
            if k:
                kins |= 1 << KINS[k]
    role = ROLE_BY_ROW.get(row)
    if role is None:
        role = "Feed" if typ == 3 else ("Gear" if typ == 1 else "Offense")
    extra = []
    if "Effects also apply to summoned familiar" in d:
        extra.append("PetGear")
    if "Preserves the effects of feed when resting at a campsite" in d:
        extra.append("KeepsFeedAtCamp")
    if "Completely restores HP at campsites" in d:
        extra.append("FullHealAtCamp")
    cure = CURE_STATUS.get(row)
    return {
        "row": row, "type": typ, "name": it["name"], "sell": it["sell"], "maxhp": maxhp, "dt": dt, "pv": pv, "mv": mv,
        "dd": dd, "pdd": pdd, "mdd": mdd, "heal": heal, "resists": resists, "cure": cure, "hazards": sorted(set(hazards)),
        "kins": kins, "role": role, "flags": extra,
    }


def flags(names, enum):
    return " | ".join(f"{enum}.{n}" for n in names) if names else f"{enum}.None"


def gen_items(data):
    items = [parse_item(it) for it in data["items"]]
    by_row = {i["row"]: i for i in items}
    lines = []
    w = lines.append
    w("// <auto-generated>")
    w("// GENERATED by tools/lazycrucible/gen_lazycrucible_data.py from tools/lazycrucible/crucible_items_boards_7.56.json.")
    w("// Do not edit by hand: change the generator or the sheet slice and re-run it.")
    w("// Source: " + data["source"])
    w("// </auto-generated>")
    w("")
    w("namespace LazyCrucible;")
    w("")
    w("internal static partial class CrucibleItems")
    w("{")
    w("    /// <summary> XBMItem rows 0-203 (0 unused). Effects parsed from the tooltip text. </summary>")
    w("    public static readonly CrucibleItem[] All =")
    w("    [")
    w("        default,")
    for row in range(1, max(by_row) + 1):
        i = by_row.get(row)
        if i is None:
            w("        default,")
            continue
        t = {1: "Gear", 2: "Item", 3: "Feed"}[i["type"]]
        w(f"        new({row}, CrucibleItemType.{t}, {cs_string(i['name'])}, {i['sell']}, ItemRole.{i['role']}, "
          f"MaxHp: {i['maxhp']}, DamageTaken: {i['dt']}, PhysVuln: {i['pv']}, MagicVuln: {i['mv']}, Heal: {i['heal']}, "
          f"DamageDealt: {i['dd']}, PhysDamage: {i['pdd']}, MagicDamage: {i['mdd']}, "
          f"Resists: {flags(i['resists'], 'CrucibleStatus')}, Cures: {('CrucibleStatus.' + i['cure']) if i['cure'] else 'CrucibleStatus.None'}, "
          f"Hazards: {flags(i['hazards'], 'ItemHazard')}, Kins: 0x{i['kins']:03x}, Flags: {flags(i['flags'], 'ItemFlags')}),")
    w("    ];")
    w("")
    w("    /// <summary> XBMPet.SatietyMax: how many feeds each familiar can eat in a run (index = XBMPet row, 0 unused). </summary>")
    sat = {p["row"]: p["satiety_max"] for p in data["pets"]}
    w("    public static readonly byte[] SatietyMax = [0, " + ", ".join(str(sat[r]) for r in range(1, max(sat) + 1)) + "];")
    w("}")
    w("")
    open(OUT_ITEMS, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    return items


def gen_boards(data):
    lines = []
    w = lines.append
    w("// <auto-generated>")
    w("// GENERATED by tools/lazycrucible/gen_lazycrucible_data.py from tools/lazycrucible/crucible_items_boards_7.56.json.")
    w("// Do not edit by hand: change the generator or the sheet slice and re-run it.")
    w("// Source: " + data["source"])
    w("// </auto-generated>")
    w("")
    w("namespace LazyCrucible;")
    w("")
    w("internal static partial class CrucibleBoards")
    w("{")
    w("    /// <summary> Every board's spaces, index = board (0 unused); battle -1 = not a battle space. </summary>")
    w("    public static readonly CrucibleSpace[][] Spaces =")
    w("    [")
    w("        [],")
    types = {"Start": "Start", "Enemy": "Enemy", "Elite Enemy": "Elite", "Boss": "Boss", "Shop": "Shop",
             "Campsite": "Campsite", "Treasure": "Treasure", "Random": "Random"}
    for b in data["boards"]:
        w("        [")
        for n in b["nodes"]:
            rnd = "null"
            if "random" in n:
                outs = ", ".join(f"new(SpaceType.{types[o['type']]}, {o.get('battle', -1)}, {o.get('camp_familiars', 0)})" for o in n["random"])
                rnd = f"[{outs}]"
            w(f"            new({n['node']}, SpaceType.{types[n['type']]}, {n['depth']}, {n.get('battle', -1)}, {n.get('camp_familiars', 0)}, {rnd}),")
        w("        ],")
    w("    ];")
    w("")
    w("    /// <summary> Board graph edges (from space, to space), index = board (0 unused). </summary>")
    w("    public static readonly (int From, int To)[][] Edges =")
    w("    [")
    w("        [],")
    for b in data["boards"]:
        w("        [" + ", ".join(f"({a}, {c})" for a, c in b["edges"]) + "],")
    w("    ];")
    w("}")
    w("")
    open(OUT_BOARDS, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def main():
    data = json.load(open(SRC, encoding="utf-8"))
    items = gen_items(data)
    gen_boards(data)
    print(f"items: {len(items)} rows -> {os.path.relpath(OUT_ITEMS, REPO)}")
    print(f"boards: {len(data['boards'])} -> {os.path.relpath(OUT_BOARDS, REPO)}")


if __name__ == "__main__":
    main()
