#!/usr/bin/env python3
"""Reported-bug regression gate for lalalazy releases.

A changelog entry that cites a GitHub issue ("issue #12") ships a change for that report. The
release is refused unless a test under tests/<Plugin> or tests/<Plugin>.* names the same issue,
so the reported symptom is pinned by a case and a later change that brings it back fails the
harness instead of shipping.

The case must reproduce the symptom the issue reported (Playbooks/Bug Fixing). A switch that
lets a user hide the broken thing is a workaround, not a fix: the report's symptom must be gone
by default before the issue is cited as fixed.

Usage:
    python tools/check-changelog-issue-tests.py --changelog src/<Plugin>/CHANGELOG.md \
        --plugin <Plugin> [--repo-root .]

Exit 0 = the newest entry cites no issue, or every issue it cites is named by a test.
Exit 1 = the uncovered issues are listed (release MUST NOT ship).
Runs automatically from tools/Package-Plugin.ps1 before anything is built or published.
"""
import argparse
import re
import sys
from pathlib import Path

ISSUE = re.compile(r"\bissue\s*#(\d+)(?!\d)", re.IGNORECASE)
HEADER = re.compile(r"^##\s")
SKIP_DIRS = {"bin", "obj", "output", ".vs"}
TEXT_SUFFIXES = {".cs", ".py", ".ps1", ".psm1", ".sh", ".md", ".json", ".txt"}


def newest_entry(text: str) -> str:
    """The text of the first '## ' section (the newest version), BOM and leading blank lines tolerated."""
    lines = text.lstrip("﻿").splitlines()
    out, inside = [], False
    for line in lines:
        if HEADER.match(line):
            if inside:
                break
            inside = True
        if inside:
            out.append(line)
    return "\n".join(out)


def cited_issues(entry: str) -> list[int]:
    return sorted({int(m.group(1)) for m in ISSUE.finditer(entry)})


def test_dirs(repo_root: Path, plugin: str) -> list[Path]:
    tests = repo_root / "tests"
    if not tests.is_dir():
        return []
    return [d for d in sorted(tests.iterdir()) if d.is_dir() and (d.name == plugin or d.name.startswith(plugin + "."))]


def issues_named_by_tests(dirs: list[Path]) -> set[int]:
    named: set[int] = set()
    for d in dirs:
        for p in d.rglob("*"):
            if not p.is_file() or p.suffix.lower() not in TEXT_SUFFIXES:
                continue
            if any(part in SKIP_DIRS for part in p.relative_to(d).parts[:-1]):
                continue
            try:
                text = p.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            named.update(int(m.group(1)) for m in ISSUE.finditer(text))
    return named


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--changelog", required=True)
    ap.add_argument("--plugin", required=True)
    ap.add_argument("--repo-root", default=str(Path(__file__).resolve().parents[1]))
    args = ap.parse_args()

    changelog = Path(args.changelog)
    if not changelog.is_file():
        print(f"issue-test gate: changelog not found: {changelog}")
        return 1
    cited = cited_issues(newest_entry(changelog.read_text(encoding="utf-8", errors="replace")))
    if not cited:
        print(f"issue-test gate: {args.plugin} newest entry cites no GitHub issue")
        return 0

    dirs = test_dirs(Path(args.repo_root), args.plugin)
    named = issues_named_by_tests(dirs)
    missing = [n for n in cited if n not in named]
    if not missing:
        print(f"issue-test gate: {args.plugin} cites {', '.join(f'issue #{n}' for n in cited)}; each is named by a test")
        return 0

    where = ", ".join(str(d.relative_to(args.repo_root)) for d in dirs) or f"tests/{args.plugin}* (none exist)"
    for n in missing:
        print(f"issue-test gate: {args.plugin} newest changelog entry cites issue #{n}, but no test under {where} names it.")
    print("Add a harness case named for the issue that reproduces the reported symptom and passes only with the fix.")
    print("A switch that hides the broken thing is a workaround: fix the symptom by default before citing the issue as fixed.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
