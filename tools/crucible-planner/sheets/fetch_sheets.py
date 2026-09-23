#!/usr/bin/env python3
"""Fetch every sheet dump that build.py loads into tools/crucible-planner/sheets/raw/.

Pins the game version (default: 7.56 key f5af21155b99a524, the committed slice baseline).
Flattens xivapi v2 relation objects to scalar ids so build.py sees the same shape as the
2026-09-16 research dumps. Writes raw/VERSION.json with the API-reported version key.

Usage:
  python3 tools/crucible-planner/sheets/fetch_sheets.py
  python3 tools/crucible-planner/sheets/fetch_sheets.py --out /tmp/crucible-raw --version latest
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.parse
import urllib.request

# Reuse the rate-limited helpers already in this directory.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xiv  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_VERSION = "f5af21155b99a524"  # 7.56 — matches committed crucible_map / slices

# Field lists match the 2026-09-16 dump key order (byte-identical json.dumps indent=1).
FULL_SHEETS = {
    "XBMContent": ["BonusPoints", "ContentFinderCondition", "RecommendedRank", "SynkRank", "TeamSize"],
    "XBMContentBattle": ["BattleDetail"],
    "XBMBattleDetail": [
        "Element", "Name", "Resist", "Unknown1", "Unknown2", "Unknown3",
        "Unknown5", "Unknown6", "Unknown7", "Unknown8", "Unknown9",
    ],
    "XBMBattleDetailAction": ["Action", "ActionEffectType", "ActionTarget", "Status"],
    "XBMContentStageEvent": ["Unknown0", "Unknown1", "Unknown2", "Unknown3"],
    "XBMContentStageEventMap": ["Unknown0", "Unknown1", "Unknown2", "Unknown3", "Unknown4"],
    "XBMContentCamp": ["FamiliarRecoverCount"],
    "XBMContentRandomStageEvent": ["RandomStageEvent"],
    "XBMRandomStageEvent": ["Unknown0", "Unknown1"],
    "XBMActionTarget": ["Unknown0"],
    "XBMActionEffectType": ["Unknown0"],
    "XBMElement": ["Name"],
    "XBMStageEventType": ["Unknown0"],
    "XBMEntrance": ["Unknown0", "Unknown1", "Unknown2"],
    "XBMScoreBonus": ["AlwaysActive", "Description", "Name"],
    "ActionCategory": ["Name"],
}

LINKED_FIELDS = {
    "Quest_linked": ("Quest", ["Name"]),
    "Action_linked": (
        "Action",
        [
            "ActionCategory", "ActionCombo", "ActionProcStatus", "ActionTimelineHit",
            "AdditionalCooldownGroup", "AffectsPosition", "AnimationEnd", "AnimationStart",
            "Aspect", "AttackType", "AutoAttackBehaviour", "BehaviourType",
            "CanTargetAlliance", "CanTargetAlly", "CanTargetHostile", "CanTargetOwnPet",
            "CanTargetParty", "CanTargetPartyPet", "CanTargetSelf", "CanUseWhileMounted",
            "Cast100ms", "CastType", "ClassJob", "ClassJobCategory", "ClassJobLevel",
            "CooldownGroup", "DeadTargetBehaviour", "EffectRange", "EquivalenceGroup",
            "ExtraCastTime100ms", "Icon", "IsPlayerAction", "IsPvP", "IsRoleAction",
            "LogActionMessage", "LogCastMessage", "LogMissMessage", "MaxCharges", "Name",
            "NeedToFaceTarget", "Omen", "OmenAlt", "PreservesCombo", "PrimaryCostType",
            "PrimaryCostValue", "Range", "Recast100ms", "RequiresLineOfSight",
            "SecondaryCostType", "SecondaryCostValue", "StatusGainSelf", "TargetArea",
            "Unknown10", "Unknown14", "Unknown15", "Unknown16", "Unknown18", "Unknown1",
            "Unknown21", "Unknown22", "Unknown23", "Unknown25", "Unknown27", "Unknown28",
            "Unknown2", "Unknown4", "Unknown8", "Unknown_70", "UnlockLink", "VFX",
            "XAxisModifier",
        ],
    ),
    "ActionTransient_linked": ("ActionTransient", ["Description"]),
    "Status_linked": (
        "Status",
        [
            "CanDispel", "CanIncreaseRewards", "CanStatusOff", "ClassJobCategory",
            "Description", "ExclusionGroup", "Flag2", "Flags", "HitEffect", "Icon",
            "InflictedByActor", "Invisibility", "IsFcBuff", "IsGaze", "IsPermanent",
            "LockActions", "LockControl", "LockMovement", "Log", "MaxStacks", "Name",
            "NoLogVfx", "ParamEffect", "ParamModifier", "PartyListPriority",
            "StatusCategory", "TargetType", "Transfiguration", "Unknown2", "Unknown3",
            "Unknown5", "Unknown6", "Unknown7", "Unknown8", "Unknown_70_1",
            "Unknown_70_2", "VFX",
        ],
    ),
    "BNpcName_linked": ("BNpcName", ["Article", "Plural", "Singular", "StartsWithVowel"]),
    "BNpcResist_linked": ("BNpcResist", ["Unknown0"]),
    "ContentFinderCondition_linked": (
        "ContentFinderCondition",
        [
            "AcceptClassJobCategory", "ClassJobLevelRequired", "ClassJobLevelSync",
            "Content", "ContentLinkType", "ContentMemberType", "ContentType", "Image",
            "IsInDutyFinder", "ItemLevelRequired", "ItemLevelSync", "Name", "ShortCode",
            "SortKey", "TerritoryType", "UnlockCriteria2", "UnlockCriteria",
            "UnlockType", "UnlockType2",
        ],
    ),
    "TerritoryType_linked": (
        "TerritoryType",
        [
            "ArrayEventHandler", "BGM", "BattalionMode", "Bg", "ContentFinderCondition",
            "ExVersion", "ExclusiveType", "IsPvpZone", "LoadingImage", "Map", "Mount",
            "Name", "PlaceName", "PlaceNameRegion", "PlaceNameZone", "QuestBattle",
            "Stealth", "TerritoryIntendedUse", "WeatherRate",
        ],
    ),
    "InstanceContent_linked": (
        "InstanceContent",
        [
            "BNpcBaseBoss", "ContentDirectorBattleTalk", "ContentDirectorManagedSG",
            "ContentFinderCondition", "InstanceContentType", "LGBEventRange", "SortKey",
            "TimeLimitmin",
        ],
    ),
    "Map_linked": (
        "Map",
        [
            "DiscoveryIndex", "Id", "MapIndex", "MapMarkerRange", "OffsetX", "OffsetY",
            "PlaceName", "PlaceNameRegion", "PlaceNameSub", "SizeFactor", "TerritoryType",
        ],
    ),
}


def flatten(v):
    """Collapse xivapi relation / icon objects to the scalar ids build.py expects."""
    if isinstance(v, dict):
        if "value" in v:
            return flatten(v["value"])
        if "id" in v and "path" in v:
            return v["id"]
        return {k: flatten(x) for k, x in v.items()}
    if isinstance(v, list):
        return [flatten(x) for x in v]
    return v


def project(fields: dict, keys: list[str]) -> dict:
    flat = {k: flatten(v) for k, v in fields.items()}
    return {k: flat[k] for k in keys if k in flat}


def dump_rows(rows: list[dict], keys: list[str]) -> list[dict]:
    out = []
    for r in rows:
        entry = {"row_id": r["row_id"]}
        if "subrow_id" in r:
            entry["subrow_id"] = r["subrow_id"]
        entry["fields"] = project(r["fields"], keys)
        out.append(entry)
    out.sort(key=lambda r: (r["row_id"], r.get("subrow_id", -1)))
    return out


def write_json(path: str, obj) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=1, ensure_ascii=False)


def resolve_version(requested: str) -> str:
    """Map a version name ('7.56', 'latest') or key to the API version key."""
    with urllib.request.urlopen(
        urllib.request.Request(
            "https://v2.xivapi.com/api/version",
            headers={"User-Agent": "crucible-research/1.0"},
        ),
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


def fetch_sheet(sheet: str, fields: list[str], version: str, rowids=None) -> tuple[list[dict], str]:
    # Temporarily pin version on xiv helpers by wrapping get URLs.
    fields_csv = ",".join(fields)
    out = []
    after = None
    reported = None
    while True:
        q = {"schema": xiv.SCHEMA, "limit": 500, "fields": fields_csv, "version": version}
        if rowids is not None:
            q["rows"] = ",".join(map(str, rowids))
        if after is not None:
            q["after"] = after
        import time
        time.sleep(0.3)
        url = f"{xiv.BASE}/{sheet}?" + urllib.parse.urlencode(q, safe="@(),*:")
        d = xiv.get(url)
        if d is None:
            raise SystemExit(f"404 fetching {sheet}")
        reported = d.get("version", reported)
        rs = d.get("rows", [])
        out.extend(rs)
        if rowids is not None or len(rs) < 500:
            break
        last = rs[-1]
        after = (
            f"{last['row_id']}"
            if "subrow_id" not in last
            else f"{last['row_id']}:{last['subrow_id']}"
        )
    return out, reported


def fetch_rows_by_id(sheet: str, fields: list[str], version: str, ids: list[int]) -> tuple[list[dict], str]:
    if not ids:
        return [], version
    # Batch in chunks the API accepts comfortably.
    reported = version
    all_rows = []
    chunk = 100
    for i in range(0, len(ids), chunk):
        part, reported = fetch_sheet(sheet, fields, version, rowids=ids[i : i + chunk])
        all_rows.extend(part)
    return all_rows, reported


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--out", default=os.path.join(HERE, "raw"), help="output directory for *.json dumps")
    ap.add_argument(
        "--version",
        default=DEFAULT_VERSION,
        help="game version key or name (default: 7.56 key; use 'latest' for current)",
    )
    args = ap.parse_args()
    version = resolve_version(args.version)
    out_dir = args.out
    os.makedirs(out_dir, exist_ok=True)
    print(f"fetching into {out_dir} at version {version}", flush=True)

    reported = version
    sheets = {}
    for name, fields in FULL_SHEETS.items():
        print(f"  {name}...", flush=True)
        rows, reported = fetch_sheet(name, fields, version)
        sheets[name] = dump_rows(rows, fields)
        write_json(os.path.join(out_dir, f"{name}.json"), sheets[name])
        print(f"    {len(sheets[name])} rows", flush=True)

    # Linked id sets from the XBM dumps.
    xc = sheets["XBMContent"]
    cfc_ids = sorted({r["fields"]["ContentFinderCondition"] for r in xc if r["row_id"]})
    bda = sheets["XBMBattleDetailAction"]
    action_ids = sorted({r["fields"]["Action"] for r in bda if r["fields"].get("Action")})
    status_ids = sorted({r["fields"]["Status"] for r in bda if r["fields"].get("Status")})
    bd = sheets["XBMBattleDetail"]
    name_ids = sorted({r["fields"]["Name"] for r in bd if r["fields"].get("Name")})
    resist_ids = sorted({r["fields"]["Resist"] for r in bd})  # includes 0

    # CFC -> Territory / InstanceContent / Unlock quest
    print("  ContentFinderCondition_linked...", flush=True)
    cfc_sheet, fields = LINKED_FIELDS["ContentFinderCondition_linked"]
    cfc_rows, reported = fetch_rows_by_id(cfc_sheet, fields, version, cfc_ids)
    cfc_dumped = dump_rows(cfc_rows, fields)
    write_json(os.path.join(out_dir, "ContentFinderCondition_linked.json"), cfc_dumped)

    tt_ids = sorted({r["fields"]["TerritoryType"] for r in cfc_dumped})
    ic_ids = sorted({r["fields"]["Content"] for r in cfc_dumped})
    quest_ids = sorted({r["fields"]["UnlockCriteria"] for r in cfc_dumped if r["fields"].get("UnlockCriteria")})

    print("  TerritoryType_linked...", flush=True)
    tt_sheet, fields = LINKED_FIELDS["TerritoryType_linked"]
    tt_rows, reported = fetch_rows_by_id(tt_sheet, fields, version, tt_ids)
    tt_dumped = dump_rows(tt_rows, fields)
    write_json(os.path.join(out_dir, "TerritoryType_linked.json"), tt_dumped)

    # Five maps per board territory: TerritoryType.Map .. Map+4
    map_ids = []
    for r in tt_dumped:
        base = r["fields"]["Map"]
        map_ids.extend(range(base, base + 5))
    map_ids = sorted(set(map_ids))

    linked_jobs = [
        ("InstanceContent_linked", ic_ids),
        ("Quest_linked", quest_ids),
        ("Action_linked", action_ids),
        ("ActionTransient_linked", action_ids),
        ("Status_linked", status_ids),
        ("BNpcName_linked", name_ids),
        ("BNpcResist_linked", resist_ids),
        ("Map_linked", map_ids),
    ]
    for dump_name, ids in linked_jobs:
        sheet, fields = LINKED_FIELDS[dump_name]
        print(f"  {dump_name} ({len(ids)} ids)...", flush=True)
        rows, reported = fetch_rows_by_id(sheet, fields, version, ids)
        write_json(os.path.join(out_dir, f"{dump_name}.json"), dump_rows(rows, fields))

    write_json(
        os.path.join(out_dir, "VERSION.json"),
        {"version": reported or version, "requested": args.version, "resolved": version},
    )
    print(f"done. VERSION={reported or version}", flush=True)


if __name__ == "__main__":
    main()
