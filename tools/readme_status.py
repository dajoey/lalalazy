#!/usr/bin/env python3
"""Write the README plugin table's Status column from pluginmaster.json.

The Status cell of each row in the README's "Plugins" table is the plugin's channel versions as the
feed serves them, so the page can never say Testing for a plugin that is on production:

    Production 0.1.5.1                          production and testing serve the same build
    Production 1.0.4.320 · Testing 1.0.4.323    a newer build is on the testing channel
    Testing only 0.1.9.16                       no production build yet

Rows are matched by the bold plugin name in the first cell against the feed's `Name`; a row the
feed does not list is left as written. Descriptions are hand-kept and never touched.

Callers: tools/Package-Plugin.ps1 (inside every release commit) and the readme-status workflow
(after any other change to pluginmaster.json).

Usage: python3 tools/readme_status.py [--repo DIR] [--check]
  --check   exit 1 when the README is out of date; write nothing
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

HEADING = re.compile(r"^#{1,6}\s+Plugins\s*$")
CELL_SPLIT = re.compile(r"(?<!\\)\|")   # a description may carry an escaped \|
NAME = re.compile(r"^\*\*(.+?)\*\*$")
UNRELEASED = {"", "0", "0.0", "0.0.0", "0.0.0.0"}


def _version_key(version: str) -> tuple:
    return tuple(int(p) if p.isdigit() else 0 for p in str(version).split("."))


def status_cell(entry: dict) -> str:
    prod = str(entry.get("AssemblyVersion") or "").strip()
    test = str(entry.get("TestingAssemblyVersion") or "").strip()
    if entry.get("IsTestingExclusive") or prod in UNRELEASED:
        return f"Testing only {test or prod}"
    if test and test not in UNRELEASED and _version_key(test) > _version_key(prod):
        return f"Production {prod} · Testing {test}"
    return f"Production {prod}"


def _table_span(lines: list[str]) -> tuple[int, int] | None:
    """[start, end) of the table under the Plugins heading, header row included."""
    for i, line in enumerate(lines):
        if HEADING.match(line.rstrip("\r\n")):
            j = i + 1
            while j < len(lines) and not lines[j].strip():
                j += 1
            k = j
            while k < len(lines) and lines[k].lstrip().startswith("|"):
                k += 1
            return (j, k) if k > j else None
    return None


def _cells(line: str) -> list[str]:
    """A row split on unescaped pipes: ['', cell1, ..., cellN, ''] for a well-formed row."""
    return CELL_SPLIT.split(line.rstrip("\r\n"))


def _well_formed(parts: list[str]) -> bool:
    return len(parts) >= 3 and not parts[0].strip() and not parts[-1].strip()


def _row_name(parts: list[str]) -> str:
    m = NAME.match(parts[1].strip()) if len(parts) > 2 else None
    return m.group(1).strip() if m else ""


def _status_index(header: list[str]) -> int | None:
    """The Status column's index in a split row, by its header name; None when absent."""
    if not _well_formed(header):
        return None
    names = [c.strip().lower() for c in header]
    return names.index("status") if "status" in names else None


def plugin_rows(text: str) -> list[str]:
    """Plugin names in the Plugins table, in order."""
    lines = text.splitlines(keepends=True)
    span = _table_span(lines)
    if not span:
        return []
    return [n for n in (_row_name(_cells(line)) for line in lines[span[0]:span[1]]) if n]


def rewrite(text: str, feed: list[dict]) -> str:
    """Rewrite the Status cell of each Plugins-table row the feed lists. A row whose shape
    differs from the header's (missing closing pipe, extra or missing cells) is left alone."""
    by_name = {str(p.get("Name") or "").strip(): p for p in feed if isinstance(p, dict)}
    lines = text.splitlines(keepends=True)
    span = _table_span(lines)
    if not span:
        return text
    header = _cells(lines[span[0]])
    col = _status_index(header)
    if col is None:
        return text
    for i in range(span[0] + 1, span[1]):
        parts = _cells(lines[i])
        if len(parts) != len(header) or not _well_formed(parts):
            continue
        entry = by_name.get(_row_name(parts))
        if not entry:
            continue
        body = lines[i].rstrip("\r\n")
        parts[col] = f" {status_cell(entry)} "
        lines[i] = "|".join(parts) + lines[i][len(body):]
    return "".join(lines)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    ap.add_argument("--repo", default=str(Path(__file__).resolve().parents[1]))
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args(argv)
    repo = Path(args.repo)
    readme = repo / "README.md"
    raw = readme.read_bytes().decode("utf-8")
    feed = json.loads((repo / "pluginmaster.json").read_bytes().decode("utf-8-sig"))
    new = rewrite(raw, feed)
    if new == raw:
        print("README status column is current")
        return 0
    if args.check:
        print("README status column is out of date: run python3 tools/readme_status.py", file=sys.stderr)
        return 1
    readme.write_bytes(new.encode("utf-8"))
    print("README status column updated from pluginmaster.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
