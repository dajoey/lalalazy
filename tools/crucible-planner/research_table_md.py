#!/usr/bin/env python3
"""Render tests/LazyCrucible.Harness/ResearchBehavior.tsv as the notebook table (Markdown on stdout).

The TSV is the single source; the LazyCrucible harness fails when a guide item has no row, a row's text is stale or an
`implemented` row names a harness case that does not exist. Usage:
    python3 tools/crucible-planner/research_table_md.py > table.md
Then write it to the notebook page `Projects/BST Rebuild 2026-09/Crucible Research To Behavior` through Helm.
"""
import csv
import collections
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TSV = os.path.join(REPO, "tests", "LazyCrucible.Harness", "ResearchBehavior.tsv")
ORDER = ["implemented", "differs", "unknown", "deferred", "manual"]


SPATIAL = "manual because it is positioning or dodging: the plugin never moves or turns the character (no pathing), so spatial play stays with the player."


def cell(text, limit):
    text = text.replace("|", "/").replace("\n", " ").strip()
    return text if len(text) <= limit else text[: limit - 1].rstrip() + "…"


def main():
    rows = list(csv.reader(open(TSV, encoding="utf-8"), delimiter="\t", quoting=csv.QUOTE_NONE))[1:]
    counts = collections.Counter(r[3] for r in rows)
    out = []
    out.append("| Status | Rows |\n|---|---|")
    for s in ORDER:
        out.append(f"| {s} | {counts[s]} |")
    out.append(f"| **all** | **{len(rows)}** |\n")
    out.append("| Fight | Research item | Status | What the plugin does | Log check / what settles it |\n|---|---|---|---|---|")
    for r in rows:
        key, fight, item, status, behavior, where, proof, log = (r + [""] * 8)[:8]
        if behavior == SPATIAL:
            behavior = "manual because it is positioning or dodging (the plugin never moves the character)"
        out.append(f"| {key} {cell(fight.split(' ', 1)[1] if ' ' in fight else fight, 34)} | {cell(item, 150)} | {status} | {cell(behavior, 420)} | {cell(log, 260)} |")
    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main()
