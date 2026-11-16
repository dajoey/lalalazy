#!/usr/bin/env python3
"""Reported-bug regression gate for lalalazy releases.

A changelog entry that claims a fix for a GitHub issue ("Fixed ... (issue #12)") ships a change
for that report. The release is refused unless a committed test for the plugin names the same
issue, so the reported symptom is pinned by a case and a later change that brings it back fails
the harness instead of shipping.

What counts as a claim: in the newest entry's "### Fixed" section (the whole entry when it has no
"###" subsection), "issue #N" / "issues #N", "fix/fixes/fixed #N", "close(s/d) #N",
"resolve(s/d) #N" with any further "#M" joined by ",", "and" or "&", and a parenthesised list
"(#N, #M)". A bare "#N" elsewhere (a Notes line, an upstream project's number) is not a claim.

What counts as a test: a file committed at HEAD under tests/<folder>/ that mentions "issue #N",
where <folder> is "<Plugin>", starts with "<Plugin>.", or holds a committed .csproj that builds
from src/<Plugin>/. Untracked files and uncommitted edits do not count: commit the test first.

The case must reproduce the reported symptom. A switch that lets a user hide the broken thing is
a workaround, not a fix: the symptom must be gone by default before the issue is cited as fixed.
The gate checks that a test names the issue; whether it reproduces the symptom stays with the
reviewer.

Usage:
    python tools/check-changelog-issue-tests.py --changelog src/<Plugin>/CHANGELOG.md \\
        --plugin <Plugin> [--repo-root .]

Exit 0 = the newest entry claims no issue fix, or every issue it claims is named by a test.
Exit 1 = the uncovered issues (or a git error) are listed; the release MUST NOT ship.
Runs automatically from tools/Package-Plugin.ps1 before anything is built or published.
"""
import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

HEADER = re.compile(r"^##\s")
SUBHEADER = re.compile(r"^###")
FIXED_HEADER = re.compile(r"^###\s*fix(?:ed|es)?\b", re.IGNORECASE)
# Test side: any "issue #N" / "issues #N" mention.
TEST_ISSUE = re.compile(r"issues?\s*#(\d+)(?!\d)", re.IGNORECASE)
# Changelog side: a claim word then #N, plus further #M joined by ",", ", and", "and" or "&".
_JOIN = r"(?:\s*,\s*and\s*|\s*,\s*|\s+and\s+|\s*&\s*)"
CLAIM = re.compile(
    r"(?:\bissues?\s*|\b(?:fix(?:es|ed)?|closes?|closed|resolves?|resolved)\s+)"
    r"#\d+(?!\d)(?:" + _JOIN + r"#\d+(?!\d))*",
    re.IGNORECASE)
PAREN_LIST = re.compile(r"\(\s*#\d+(?:\s*,\s*#\d+)*\s*\)")
NUMBER = re.compile(r"#(\d+)(?!\d)")
SKIP_DIRS = {"bin", "obj", "output", ".vs"}
TEXT_SUFFIXES = {".cs", ".py", ".ps1", ".psm1", ".sh", ".md", ".json", ".txt"}


class GitError(Exception):
    pass


def newest_entry(text: str) -> str:
    """The text of the first '## ' section (the newest version), BOM and leading blank lines tolerated."""
    lines = text.lstrip("\ufeff").splitlines()
    out, inside = [], False
    for line in lines:
        if HEADER.match(line):
            if inside:
                break
            inside = True
        if inside:
            out.append(line)
    return "\n".join(out)


def claim_scope(entry: str) -> str:
    """The '### Fixed' section(s) of the entry; the whole entry when it has no '###' subsection."""
    lines = entry.splitlines()
    if not any(SUBHEADER.match(line) for line in lines):
        return entry
    out, inside = [], False
    for line in lines:
        if SUBHEADER.match(line):
            inside = bool(FIXED_HEADER.match(line))
            continue
        if inside:
            out.append(line)
    return "\n".join(out)


def cited_issues(entry: str) -> list[int]:
    """Issue numbers the entry claims to fix."""
    scope = claim_scope(entry)
    found: set[int] = set()
    for m in CLAIM.finditer(scope):
        found.update(int(n) for n in NUMBER.findall(m.group(0)))
    for m in PAREN_LIST.finditer(scope):
        found.update(int(n) for n in NUMBER.findall(m.group(0)))
    return sorted(found)


def git(repo_root: Path, *args: str) -> subprocess.CompletedProcess:
    # The ceiling stops git climbing out of --repo-root into some enclosing repository.
    env = dict(os.environ, GIT_CEILING_DIRECTORIES=str(repo_root.resolve().parent))
    try:
        return subprocess.run(["git", "-C", str(repo_root), *args], capture_output=True, env=env)
    except OSError as e:
        raise GitError(f"cannot run git: {e}")


def git_text(r: subprocess.CompletedProcess) -> str:
    return (r.stderr or r.stdout).decode("utf-8", errors="replace").strip()


def committed_issue_mentions(repo_root: Path) -> dict[str, set[int]]:
    """{tests/<folder>: issue numbers} from the files committed at HEAD under tests/."""
    r = git(repo_root, "grep", "-z", "-I", "-i", "-n", "-E", r"issues?[[:space:]]*#[0-9]+", "HEAD", "--", "tests/")
    if r.returncode == 1:
        return {}
    if r.returncode != 0:
        raise GitError(f"git grep HEAD -- tests/ failed ({r.returncode}): {git_text(r)}")
    named: dict[str, set[int]] = {}
    for raw in r.stdout.split(b"\n"):
        parts = raw.split(b"\0", 2)
        if len(parts) != 3 or not parts[0].startswith(b"HEAD:"):
            continue
        path = parts[0][len(b"HEAD:"):].decode("utf-8", errors="replace")
        pieces = path.split("/")
        if len(pieces) < 3 or pieces[0] != "tests":
            continue
        if Path(pieces[-1]).suffix.lower() not in TEXT_SUFFIXES or any(d in SKIP_DIRS for d in pieces[2:-1]):
            continue
        content = parts[2].decode("utf-8", errors="replace")
        named.setdefault("tests/" + pieces[1], set()).update(int(n) for n in TEST_ISSUE.findall(content))
    return named


def folder_builds_plugin(repo_root: Path, folder: str, plugin: str) -> bool:
    """True when a .csproj committed under `folder` refers to src/<Plugin>/ (either slash)."""
    r = git(repo_root, "ls-tree", "-r", "-z", "--name-only", "HEAD", "--", folder + "/")
    if r.returncode != 0:
        raise GitError(f"git ls-tree HEAD -- {folder}/ failed ({r.returncode}): {git_text(r)}")
    needles = (f"src\\{plugin}\\".lower(), f"src/{plugin}/".lower())
    for rel in r.stdout.decode("utf-8", errors="replace").split("\0"):
        if not rel.lower().endswith(".csproj"):
            continue
        show = git(repo_root, "show", f"HEAD:{rel}")
        if show.returncode != 0:
            raise GitError(f"git show HEAD:{rel} failed ({show.returncode}): {git_text(show)}")
        body = show.stdout.decode("utf-8", errors="replace").lower()
        if any(n in body for n in needles):
            return True
    return False


def issues_named_for_plugin(repo_root: Path, plugin: str) -> tuple[set[int], list[str]]:
    """(issue numbers named by the plugin's committed tests, the tests/ folders that count)."""
    named: set[int] = set()
    used: list[str] = []
    for folder, numbers in sorted(committed_issue_mentions(repo_root).items()):
        name = folder.split("/", 1)[1]
        if name == plugin or name.startswith(plugin + ".") or folder_builds_plugin(repo_root, folder, plugin):
            named |= numbers
            used.append(folder)
    return named, used


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
        print(f"issue-test gate: {args.plugin} newest entry claims no GitHub issue fix")
        return 0

    try:
        named, used = issues_named_for_plugin(Path(args.repo_root), args.plugin)
    except GitError as e:
        print(f"issue-test gate: cannot read the committed tests: {e}")
        return 1
    missing = [n for n in cited if n not in named]
    if not missing:
        print(f"issue-test gate: {args.plugin} cites {', '.join(f'issue #{n}' for n in cited)}; each is named by a committed test")
        return 0

    where = f"tests/{args.plugin}*, or a tests/ folder built from src/{args.plugin}/ (committed at HEAD)"
    for n in missing:
        print(f"issue-test gate: {args.plugin} newest changelog entry cites issue #{n}, but no test under {where} names it.")
    print("Add a harness case named for the issue that reproduces the reported symptom and passes only with the fix.")
    print("A switch that hides the broken thing is a workaround: fix the symptom by default before citing the issue as fixed.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
