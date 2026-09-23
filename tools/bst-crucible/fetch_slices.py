#!/usr/bin/env python3
"""Regenerate tools/bst-crucible/crucible_sheets_7.56.json and crucible_beasts_7.56.json.

Pulls the fields named in each file's `source` string from xivapi v2 and writes the same
JSON shape the C# generator (gen_crucible_data.py) and the web exporter consume.

Default version is the 7.56 key baked into the committed filenames. Pass --version latest
(or a newer key) when a balance patch lands, then bump the output filenames.

Usage:
  python3 tools/bst-crucible/fetch_slices.py
  python3 tools/bst-crucible/fetch_slices.py --out-dir /tmp/slices --version f5af21155b99a524
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_VERSION = "f5af21155b99a524"
BASE = "https://v2.xivapi.com/api/sheet"
SCHEMA = "exdschema@2:latest"

BOARD_NAMES = {
    1: "First Board of the Unbroken",
    2: "Second Board of the Unbroken",
    3: "Third Board of the Unbroken",
    4: "First Master's Board",
    5: "Second Master's Board",
}
STAGE_EVENT_TYPE = {
    1: "Start",
    2: "Enemy",
    3: "Elite Enemy",
    4: "Boss",
    5: "Shop",
    6: "Campsite",
    7: "Treasure",
    8: "Random",
}
# XBMActionTarget / XBMActionEffectType.Unknown0 text (same as enums.json / build.py).
ACTION_TARGET = {0: "", 1: "Self", 2: "Ground", 3: "Highest Enmity", 4: "Random", 5: "Player", 6: "Allies"}
ACTION_AREA = {
    0: "???", 1: "Single Target", 2: "Front", 3: "Rear", 4: "Front/Rear",
    5: "Lateral", 6: "Circle", 7: "Ring", 8: "Circle/Ring", 9: "Universal", 10: "Cross",
}
# Pet auto-attack -> panel weakness: Magic uses Aspect; physical uses AttackType.
ATTACK_TO_WEAK = {1: 9, 2: 8, 3: 7}  # Slashing/Piercing/Blunt
ASPECT_TO_WEAK = {1: 1, 2: 5, 3: 2, 4: 3, 5: 4, 6: 6}  # Fire/Ice/Wind/Earth/Lightning/Water
RANKS = (5, 10, 15, 20, 25)

SHEETS_SOURCE = (
    "FFXIV 7.56 game sheets via xivapi v2 (version {ver}): XBMContent, XBMContentBattle, "
    "XBMBattleDetail, XBMBattleDetailAction, BNpcName, BNpcResist, Action, Status. "
    "Decoded 2026-09-16."
)
BEASTS_SOURCE = (
    "FFXIV 7.56 via xivapi v2 ({ver}): XBMPet (Action, InflictsStatus, ParamGrow), "
    "XBMPetParamGrow (subrow = rank-1) at ranks 5/10/15/20/25, pet auto-attack Action "
    "Aspect/AttackType; XBMBattleDetail star ratings (Unknown5-9: STR, INT, PHY_R, MAG_R, "
    "CON, order inferred)."
)


def flatten(v):
    if isinstance(v, dict):
        if "value" in v:
            return flatten(v["value"])
        if "id" in v and "path" in v:
            return v["id"]
        return {k: flatten(x) for k, x in v.items()}
    if isinstance(v, list):
        return [flatten(x) for x in v]
    return v


def get(url: str):
    for i in range(4):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "crucible-research/1.0"})
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            time.sleep(2 * (i + 1))
        except Exception:
            time.sleep(2 * (i + 1))
    raise SystemExit("failed " + url)


def rows(sheet: str, fields: str, version: str, rowids=None, limit=500):
    out = []
    after = None
    while True:
        q = {"schema": SCHEMA, "limit": limit, "fields": fields, "version": version}
        if rowids is not None:
            q["rows"] = ",".join(map(str, rowids))
        if after is not None:
            q["after"] = after
        time.sleep(0.3)
        d = get(f"{BASE}/{sheet}?" + urllib.parse.urlencode(q, safe="@(),*:"))
        rs = d.get("rows", [])
        out.extend(rs)
        if rowids is not None or len(rs) < limit:
            break
        last = rs[-1]
        after = (
            f"{last['row_id']}"
            if "subrow_id" not in last
            else f"{last['row_id']}:{last['subrow_id']}"
        )
    return out


def byid(rs):
    return {r["row_id"]: flatten(r["fields"]) for r in rs}


def bysub(rs):
    g = {}
    for r in rs:
        g.setdefault(r["row_id"], {})[r.get("subrow_id")] = flatten(r["fields"])
    return g


def resolve_version(requested: str) -> str:
    with urllib.request.urlopen(
        urllib.request.Request("https://v2.xivapi.com/api/version", headers={"User-Agent": "x"}),
        timeout=60,
    ) as r:
        versions = json.load(r)["versions"]
    if requested == "latest":
        for v in versions:
            if "latest" in (v.get("names") or []):
                return v["key"]
        return versions[-1]["key"]
    for v in versions:
        if v["key"] == requested or requested in (v.get("names") or []):
            return v["key"]
    raise SystemExit(f"unknown version {requested!r}")


def status_obj(sid, ST):
    if not sid:
        return None
    s = ST[sid]
    cat = s["StatusCategory"]
    return {
        "id": sid,
        "name": s["Name"],
        "category": {1: "beneficial", 2: "detrimental"}.get(cat, cat),
        "cleansable": bool(cat == 2 and s["CanDispel"]),
        "dispellable": bool(cat == 1 and s["Unknown8"]),
    }


def action_obj(bda_row, BDA, ACT, ST):
    b = BDA[bda_row]
    aid = b["Action"]
    if not aid:
        return None
    a = ACT[aid]
    return {
        "action": aid,
        "name": a["Name"],
        "cast_s": a["Cast100ms"] / 10,
        "target": ACTION_TARGET.get(b["ActionTarget"], ""),
        "area": ACTION_AREA.get(b["ActionEffectType"], "???"),
        "interruptible": bool(a["Unknown15"]),
        "status": status_obj(b["Status"], ST),
    }


def build_sheets(version: str) -> dict:
    print("fetching XBM + linked sheets...", flush=True)
    XC = byid(rows("XBMContent", "BonusPoints,ContentFinderCondition,RecommendedRank,SynkRank,TeamSize", version))
    XCB = bysub(rows("XBMContentBattle", "BattleDetail", version))
    XBD = bysub(
        rows(
            "XBMBattleDetail",
            "Element,Name,Resist,Unknown1,Unknown2,Unknown3,Unknown5,Unknown6,Unknown7,Unknown8,Unknown9",
            version,
        )
    )
    BDA = byid(rows("XBMBattleDetailAction", "Action,ActionEffectType,ActionTarget,Status", version))
    XSE = bysub(rows("XBMContentStageEvent", "Unknown0,Unknown1,Unknown2,Unknown3", version))
    XCRSE = bysub(rows("XBMContentRandomStageEvent", "RandomStageEvent", version))
    XRSE = bysub(rows("XBMRandomStageEvent", "Unknown0,Unknown1", version))

    cfc_ids = sorted(XC[i]["ContentFinderCondition"] for i in range(1, 6))
    CFC = byid(
        rows(
            "ContentFinderCondition",
            "ClassJobLevelSync,ItemLevelSync,TerritoryType,Name",
            version,
            rowids=cfc_ids,
        )
    )
    name_ids = sorted({e["Name"] for bd in XBD.values() for e in bd.values() if e.get("Name")})
    resist_ids = sorted({e["Resist"] for bd in XBD.values() for e in bd.values()})
    action_ids = sorted({BDA[r]["Action"] for r in BDA if BDA[r].get("Action")})
    status_ids = sorted({BDA[r]["Status"] for r in BDA if BDA[r].get("Status")})

    BN = byid(rows("BNpcName", "Singular", version, rowids=name_ids))
    BR = byid(rows("BNpcResist", "Unknown0", version, rowids=resist_ids))
    ACT = byid(
        rows("Action", "Name,Cast100ms,Unknown15", version, rowids=action_ids)
    )
    ST = byid(
        rows("Status", "Name,StatusCategory,CanDispel,Unknown8", version, rowids=status_ids)
    )

    boards = []
    for bid in range(1, 6):
        c = XC[bid]
        cfc = CFC[c["ContentFinderCondition"]]
        refs = {}
        for sub, se in sorted(XSE[bid].items()):
            t, idx = se["Unknown0"], se["Unknown2"]
            if t in (2, 3, 4):
                refs.setdefault(idx, []).append(STAGE_EVENT_TYPE[t])
            if t == 8:
                rse = XCRSE[bid][idx]["RandomStageEvent"]
                for _, o in sorted(XRSE[rse].items()):
                    ot, oi = o["Unknown0"], o["Unknown1"]
                    if ot in (2, 3, 4):
                        refs.setdefault(oi, []).append(f"Random->{STAGE_EVENT_TYPE[ot]}")
        battles = []
        for bsub, cb in sorted(XCB[bid].items()):
            bd = cb["BattleDetail"]
            kinds = set(refs.get(bsub, []))
            role = (
                "Boss"
                if bsub == 0
                else ("Elite Enemy" if any("Elite" in k for k in kinds) else "Enemy")
            )
            enemies = []
            for sub in sorted(XBD[bd]):
                e = XBD[bd][sub]
                rbits = "".join("1" if x else "0" for x in BR[e["Resist"]]["Unknown0"])
                acts = []
                for key in ("Unknown2", "Unknown3"):
                    if e.get(key):
                        a = action_obj(e[key], BDA, ACT, ST)
                        if a:
                            acts.append(a)
                enemies.append(
                    {
                        "name_id": e["Name"],
                        "name": BN[e["Name"]]["Singular"],
                        "sub": sub,
                        "weakness": e["Element"],
                        "resist_bits": rbits,
                        "actions": acts,
                    }
                )
            battles.append({"battle": bsub, "role": role, "enemies": enemies})
        boards.append(
            {
                "board": bid,
                "name": BOARD_NAMES[bid],
                "territory": cfc["TerritoryType"],
                "cfc": c["ContentFinderCondition"],
                "level": cfc["ClassJobLevelSync"],
                "item_level": cfc["ItemLevelSync"],
                "beast_rank": c["SynkRank"],
                "roster": c["TeamSize"],
                "battles": battles,
            }
        )
    return {
        "source": SHEETS_SOURCE.format(ver=version),
        "boards": boards,
        "_xbd": XBD,
        "_xcb": XCB,
        "_xse": XSE,
        "_xcrse": XCRSE,
        "_xrse": XRSE,
    }


def build_beasts(version: str, sheets_ctx: dict) -> dict:
    print("fetching XBMPet + ParamGrow + Pet names...", flush=True)
    pets = byid(
        rows(
            "XBMPet",
            "Action,InflictsStatus,ParamGrow,Pet",
            version,
        )
    )
    grows = bysub(
        rows(
            "XBMPetParamGrow",
            "Constitution,Intelligence,MagicalResistance,PhysicalResistance,Strength",
            version,
        )
    )
    pet_ids = sorted({pets[i]["Pet"] for i in range(1, 51)})
    pet_names = byid(rows("Pet", "Name", version, rowids=pet_ids))
    auto_ids = sorted({pets[i]["Action"] for i in range(1, 51)})
    autos = byid(rows("Action", "Aspect,AttackType", version, rowids=auto_ids))

    beasts = []
    for rid in range(1, 51):
        p = pets[rid]
        a = autos[p["Action"]]
        at, asp = a["AttackType"], a["Aspect"]
        auto_magic = at == 5
        auto_element = ASPECT_TO_WEAK.get(asp, 0) if auto_magic else ATTACK_TO_WEAK.get(at, 0)
        inflicts = 0
        for i, bit in enumerate(p["InflictsStatus"]):
            if bit:
                inflicts |= 1 << i
        pg = p["ParamGrow"]
        stats = {}
        for rank in RANKS:
            g = grows[pg][rank - 1]
            stats[str(rank)] = [
                g["Strength"],
                g["Intelligence"],
                g["PhysicalResistance"],
                g["MagicalResistance"],
                g["Constitution"],
            ]
        beasts.append(
            {
                "row": rid,
                "name": pet_names[p["Pet"]]["Name"],
                "auto_action": p["Action"],
                "auto_element": auto_element,
                "auto_magic": auto_magic,
                "inflicts": inflicts,
                "stats": stats,
            }
        )

    # enemy_stars: encounter order across boards (not sorted by name_id).
    enemy_stars = {}
    XBD = sheets_ctx["_xbd"]
    XCB = sheets_ctx["_xcb"]
    for bid in range(1, 6):
        for bsub, cb in sorted(XCB[bid].items()):
            bd = cb["BattleDetail"]
            for sub in sorted(XBD[bd]):
                e = XBD[bd][sub]
                nid = str(e["Name"])
                if nid not in enemy_stars:
                    enemy_stars[nid] = [
                        e["Unknown5"],
                        e["Unknown6"],
                        e["Unknown7"],
                        e["Unknown8"],
                        e["Unknown9"],
                    ]

    # battle_roles from stage-event refs (same rules as build.py / sheets slice).
    battle_roles = {}
    XSE, XCRSE, XRSE = sheets_ctx["_xse"], sheets_ctx["_xcrse"], sheets_ctx["_xrse"]
    for bid in range(1, 6):
        refs = {}
        for sub, se in sorted(XSE[bid].items()):
            t, idx = se["Unknown0"], se["Unknown2"]
            if t in (2, 3, 4):
                refs.setdefault(idx, []).append(STAGE_EVENT_TYPE[t])
            if t == 8:
                rse = XCRSE[bid][idx]["RandomStageEvent"]
                for _, o in sorted(XRSE[rse].items()):
                    ot, oi = o["Unknown0"], o["Unknown1"]
                    if ot in (2, 3, 4):
                        refs.setdefault(oi, []).append(f"Random->{STAGE_EVENT_TYPE[ot]}")
        for bsub in sorted(XCB[bid]):
            kinds = set(refs.get(bsub, []))
            role = (
                "Boss"
                if bsub == 0
                else ("Elite Enemy" if any("Elite" in k for k in kinds) else "Enemy")
            )
            random_only = bool(refs.get(bsub)) and all("Random" in r for r in refs[bsub])
            battle_roles[f"{bid}:{bsub}"] = {"role": role, "random_only": random_only}

    return {
        "source": BEASTS_SOURCE.format(ver=version),
        "beasts": beasts,
        "enemy_stars": enemy_stars,
        "battle_roles": battle_roles,
    }


def write_json(path: str, obj) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=1, ensure_ascii=False)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--out-dir", default=HERE, help="directory for the two JSON outputs")
    ap.add_argument("--version", default=DEFAULT_VERSION)
    ap.add_argument(
        "--suffix",
        default="7.56",
        help="filename suffix (crucible_sheets_<suffix>.json); bump when version changes",
    )
    args = ap.parse_args()
    version = resolve_version(args.version)
    print(f"version={version}", flush=True)
    ctx = build_sheets(version)
    sheets = {"source": ctx["source"], "boards": ctx["boards"]}
    beasts = build_beasts(version, ctx)
    os.makedirs(args.out_dir, exist_ok=True)
    sheets_path = os.path.join(args.out_dir, f"crucible_sheets_{args.suffix}.json")
    beasts_path = os.path.join(args.out_dir, f"crucible_beasts_{args.suffix}.json")
    write_json(sheets_path, sheets)
    write_json(beasts_path, beasts)
    print(f"wrote {sheets_path}", flush=True)
    print(f"wrote {beasts_path}", flush=True)


if __name__ == "__main__":
    main()
