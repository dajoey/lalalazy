#!/usr/bin/env python3
"""Export the Crucible Planner's data files (docs/crucible/data/*.json) from the same sheet slices that
generate the plugin's C# tables, so the web advisor and the in-game advisor read one source.

  advisor.json  - boards, enemies, battles, beast profiles (mirrors tools/bst-crucible/gen_crucible_data.py
                  field for field; the JS port in docs/crucible/advisor.js consumes it) plus the beast roster
                  parsed from src/Shared/LalaCrucible/BST_Beasts.cs (kin, release traits, capture level).
  boards.json   - board graphs for the map UI from tools/crucible-planner/sheets/crucible_map.json
                  (nodes with map coordinates, edges, per-battle enemy panels with casts and statuses, bonus points).
  version.json  - game version key and export time.

Usage: python3 tools/crucible-planner/export_planner_data.py
Re-run after any patch: refresh the sheet slices (tools/bst-crucible, tools/crucible-planner/sheets/build.py),
run gen_crucible_data.py, rebuild tests/CruciblePlanner.Golden, then this, then `node --test docs/crucible/test`.
"""
import datetime
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SHEETS = os.path.join(REPO, "tools", "bst-crucible", "crucible_sheets_7.56.json")
BEASTS = os.path.join(REPO, "tools", "bst-crucible", "crucible_beasts_7.56.json")
BEASTS_CS = os.path.join(REPO, "src", "Shared", "LalaCrucible", "BST_Beasts.cs")
MAP = os.path.join(HERE, "sheets", "crucible_map.json")
OUT = os.path.join(REPO, "docs", "crucible", "data")

KIN = {"None": 0, "Beastkin": 1, "Vilekin": 2, "Cloudkin": 3, "Seedkin": 4, "Wavekin": 5, "Scalekin": 6, "Soulkin": 7, "Ashkin": 8}
AFFINITY = {"None": 0, "Volant": 1, "Rampant": 2, "Durant": 3, "Eldritch": 4, "Sunstrider": 5, "Moonstalker": 6}
RELEASE = {"None": 0, "Damage": 1, "AoE": 2, "Exit": 4, "Sleep": 8, "Knockback": 16, "DrawIn": 32, "CrowdControl": 64,
           "TargetDebuff": 128, "PartyBuff": 256, "Mitigation": 512, "PetBuff": 1024, "PetCast": 2048}
NEEDS = {"Interrupt": 1, "Dispel": 2, "Cleanse": 4}
ROLE = {"Enemy": 0, "EliteEnemy": 1, "Boss": 2}
VULN_NAMES = ["Slow", "Petrification", "Paralysis", "Silence/Interrupt", "Blind", "Poison", "Stun", "Sleep", "Bind", "Heavy", "Doom"]

BEAST_RE = re.compile(
    r'new\((\d+), "([^"]+)", BeastmasterKinType\.(\w+), BeastmasterAffinity\.(\w+), (\d+), (\d+), ([\w.| ]+?), (true|false), (\d+)\)')


def parse_beasts_cs():
    text = open(BEASTS_CS, encoding="utf-8").read()
    rows = {}
    for m in BEAST_RE.finditer(text):
        row = int(m.group(1))
        traits = 0
        for part in m.group(7).split("|"):
            traits |= RELEASE[part.strip().split(".")[-1]]
        rows[row] = {
            "row": row, "name": m.group(2), "kin": KIN[m.group(3)], "affinity": AFFINITY[m.group(4)],
            "trickSkillId": int(m.group(5)), "releaseSkillId": int(m.group(6)), "release": traits,
            "trickKnocksBack": m.group(8) == "true", "captureLevel": int(m.group(9)),
        }
    count = max(rows) if rows else 0
    if count != 50 or sorted(rows) != list(range(1, 51)):
        sys.exit(f"BST_Beasts.cs parse failed: rows {sorted(rows)[:5]}... count {count}")
    return [None] + [rows[r] for r in range(1, count + 1)]


def advisor(data, extra, beasts):
    stars = extra["enemy_stars"]
    boards = [None]
    for b in data["boards"]:
        boards.append({"board": b["board"], "territory": b["territory"], "cfc": b["cfc"], "level": b["level"],
                       "itemLevel": b["item_level"], "beastRank": b["beast_rank"], "roster": b["roster"],
                       "battles": len(b["battles"]), "name": b["name"]})
    enemies = []
    for b in data["boards"]:
        for bat in b["battles"]:
            for e in bat["enemies"]:
                needs = 0
                for a in e["actions"]:
                    st = a["status"]
                    if a["interruptible"]:
                        needs |= NEEDS["Interrupt"]
                    if st and st["category"] == "beneficial" and st["dispellable"]:
                        needs |= NEEDS["Dispel"]
                    if st and st["category"] == "detrimental" and st["cleansable"]:
                        needs |= NEEDS["Cleanse"]
                vuln = 0
                for i, bit in enumerate(e["resist_bits"]):
                    if bit == "0":
                        vuln |= 1 << i
                st = stars[str(e["name_id"])]
                packed = (st[0] << 12) | (st[1] << 9) | (st[2] << 6) | (st[3] << 3) | st[4]
                enemies.append({"nameId": e["name_id"], "board": b["board"], "battle": bat["battle"], "sub": e["sub"],
                                "weakness": e["weakness"], "vulnerable": vuln, "needs": needs, "stars": packed, "name": e["name"]})
    battles = []
    for key, info in sorted(extra["battle_roles"].items(), key=lambda kv: (int(kv[0].split(":")[0]), int(kv[0].split(":")[1]))):
        board, battle = key.split(":")
        battles.append({"board": int(board), "battle": int(battle), "role": ROLE[info["role"].replace(" ", "")], "randomOnly": bool(info["random_only"])})
    profiles = [None]
    for bst in extra["beasts"]:
        flat = [v for rank in ("5", "10", "15", "20", "25") for v in bst["stats"][rank]]
        profiles.append({"row": bst["row"], "autoElement": bst["auto_element"], "autoMagic": bool(bst["auto_magic"]),
                         "inflicts": bst["inflicts"], "stats": flat})
    if [p["row"] for p in profiles[1:]] != list(range(1, 51)):
        sys.exit("beast profiles are not rows 1..50 in order")
    return {"source": data["source"], "beastCount": 50, "boards": boards, "enemies": enemies, "battles": battles,
            "beastProfiles": profiles, "beasts": beasts}


def board_maps(m):
    out = []
    for b in m["boards"]:
        nodes = []
        for n in b["nodes"]:
            node = {"id": n["node"], "type": n["type"], "depth": n["depth"], "x": n["map_xy"]["x"], "y": n["map_xy"]["y"]}
            if n.get("xbm_content_battle"):
                node["battle"] = int(n["xbm_content_battle"].split(":")[1])
            if "familiar_recover_count" in n:
                node["recover"] = n["familiar_recover_count"]
            for k in ("outcomes", "random_outcomes", "random"):
                if k in n:
                    node["outcomes"] = n[k]
            nodes.append(node)
        battles = {}
        for bt in b["battles"]:
            battle = int(bt["xbm_content_battle"].split(":")[1])
            enemies = []
            for e in bt["enemies"]:
                panel = []
                for a in e.get("panel_actions", []):
                    st = a.get("status")
                    panel.append({
                        "name": a["name"], "target": a["panel_target"]["text"], "area": a["panel_area"]["text"],
                        "castS": a["cast_s"], "shape": (a.get("cast_type") or {}).get("shape"), "range": a.get("effect_range"),
                        "attackType": a.get("attack_type"), "aspect": a.get("aspect"),
                        "interruptible": bool(a.get("interruptible_INFERRED_Action_Unknown15")),
                        "responses": a.get("suggested_responses", []),
                        "status": None if not st else {"name": st["name"], "category": st["category"],
                                                       "cleansable": bool(st.get("cleansable")), "dispellable": bool(st.get("dispellable_INFERRED")),
                                                       "description": st.get("description", "")},
                    })
                enemies.append({
                    "nameId": e["bnpcname_id"], "name": e["name"], "sub": int(e["xbm_battle_detail"].split(":")[1]),
                    "weakness": e["weakness"]["raw"], "weaknessText": e["weakness"]["text"],
                    "stars": e["stars_INFERRED"], "vulnerableTo": e["vulnerable_to_INFERRED"], "panel": panel,
                })
            battles[str(battle)] = {"battle": battle, "role": bt["role"], "randomOnly": bool(bt["random_only"]),
                                    "reachedVia": bt.get("reached_via", []), "enemies": enemies}
        out.append({
            "board": b["xbm_content_row"], "name": b["name"], "shortCode": b["cfc_short_code"], "level": b["class_job_level_sync"],
            "itemLevel": b["item_level_sync"], "rankSync": b["synk_rank"], "teamSize": b["team_size_max"],
            "timeLimitMin": b["time_limit_min"], "unlockQuest": b.get("unlock_quest"), "bonusPoints": b.get("bonus_points", {}),
            "nodes": nodes, "edges": b["edges_from_to"], "battles": battles,
        })
    return {"source": m["source"], "boards": out, "vulnerabilityNames": VULN_NAMES}


def main():
    data = json.load(open(SHEETS, encoding="utf-8"))
    extra = json.load(open(BEASTS, encoding="utf-8"))
    m = json.load(open(MAP, encoding="utf-8"))
    os.makedirs(OUT, exist_ok=True)
    adv = advisor(data, extra, parse_beasts_cs())
    maps = board_maps(m)
    key = re.search(r"([0-9a-f]{16})", data["source"])
    version = {"game": "7.56", "versionKey": key.group(1) if key else None,
               "exportedAt": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
               "sources": [data["source"], extra["source"], m["source"]]}
    for name, obj in (("advisor.json", adv), ("boards.json", maps), ("version.json", version)):
        with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump(obj, f, ensure_ascii=False, separators=(",", ":"))
            f.write("\n")
        print(f"wrote {name}: {os.path.getsize(os.path.join(OUT, name))} bytes")
    print(f"enemies {len(adv['enemies'])}, battles {len(adv['battles'])}, nodes {sum(len(b['nodes']) for b in maps['boards'])}")


if __name__ == "__main__":
    main()
