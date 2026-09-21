#!/usr/bin/env python3
"""Fail the build if two presets share a numeric value.

Duplicate values in a `Preset` enum are legal C# and produce no warning, but
`PresetStorage.BuildPresets` stores them with `dict[preset] = ...` (an indexer,
not `.Add`), so the two names collapse to one entry: one becomes unreachable in
the UI and in `PresetsByName`, and `IsEnabled(A)` silently reads B's bit. The
plugin ships looking fine and behaving wrong.

This has happened twice, both times from an upstream merge landing a new preset
on a value this fork had already claimed:
  2026-08-30  upstream BLU_ST_DPS / BLU_AoE_DPS vs fork BLU_AutoRotation_DPS/_Heal (70026/70027)
  2026-09-20  upstream Phantom_RedMage_OccultLibra_Refresh vs fork Phantom755_RequireWeakness (110140)

Which side to renumber is not a coin flip. Presets persist BY VALUE in
`EnabledActionsV6`, so renumbering a preset that has already shipped silently
switches an existing user onto a different feature. Renumber the side that has
never shipped in this fork; if BOTH have shipped, it is a product decision and
belongs on a Helm card, not in a merge.

Usage:  python3 tools/check-preset-ids.py [repo_root]
Exit 0 = no duplicates. Exit 1 = duplicates (listed). Exit 2 = found no enum to check.
"""
import collections
import pathlib
import re
import sys

MEMBER = re.compile(r'^\s*([A-Za-z_]\w*)\s*=\s*(\d+)\s*,', re.M)


def check(path: pathlib.Path, root: pathlib.Path) -> int:
    text = path.read_text(encoding="utf-8", errors="replace")
    by_value = collections.defaultdict(list)
    for name, value in MEMBER.findall(text):
        by_value[int(value)].append(name)

    dupes = {v: names for v, names in by_value.items() if len(names) > 1}
    label = path.relative_to(root).as_posix()
    if not dupes:
        print(f"OK  {label}: {len(by_value)} presets, 0 duplicate values")
        return 0

    print(f"FAIL {label}: {len(dupes)} duplicated value(s)")
    for value in sorted(dupes):
        print(f"       {value} -> {', '.join(dupes[value])}")
    return 1


def main() -> int:
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    targets = sorted(p for p in root.glob("src/*/*/Combos/CustomComboPreset.cs"))
    if not targets:
        print(f"no CustomComboPreset.cs under {root}/src - nothing to check", file=sys.stderr)
        return 2
    return max(check(p, root) for p in targets)


if __name__ == "__main__":
    sys.exit(main())
