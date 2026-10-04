#!/usr/bin/env python3
"""Every release tag must name its own release commit.

A tag <Plugin>-v<version> is created by the GitHub Release path in
tools/PackageRelease.ps1. Until 2026-10-04 the release was published from the
packager's UNPUSHED worktree with target_commitish 'main', so GitHub resolved
'main' to the remote tip of the moment - always the PREVIOUS release's commit,
because the release commit only exists once the packager's output is committed
and pushed. Found live: GluttonyCombo v1.0.4.267-.273 every one tagged the
previous release's commit (v1.0.4.274 was hand-corrected). Release tags are the
repo's per-version anchors - anything auditing "what shipped in vX" reads the
tag (github-traffic-snapshot parses tag names today) - so a tag naming the
wrong commit is a silent audit lie.

A tag is correct when the tagged commit
  1. is reachable from origin/main, and
  2. ships that version: pluginmaster.json at that commit advertises <version>
     for the plugin (AssemblyVersion or TestingAssemblyVersion), and the channel
     manifest committed beside the zip (plugins/<P>/testing/<P>.json or
     plugins/<P>/latest/<P>.json) carries AssemblyVersion == <version>.

Migration-era tags (created 2026-09-25 by tools/Migrate-ToReleases.ps1 for the
then-current zips of every plugin) satisfy both: they sit on the tip whose
pluginmaster advertised exactly those versions.

Usage:  python tools/check-release-tags.py [repo_root]
Exit 0  = every tag names its own release commit (each printed with its commit).
Exit 1  = stale tags, each listed with the commit that actually ships the
          version, ready for the one-time `git tag -f <tag> <commit>` +
          `git push -f origin <tag>` correction. Branches are NEVER force-pushed;
          moving a tag is reserved for this checker-named correction.
Exit 2  = git/pluginmaster trouble (bad root, fetch failed, unparseable index).
"""
import json
import re
import subprocess
import sys
from pathlib import Path

TAG_RE = re.compile(r"^(.+)-v(\d+(?:\.\d+)+)$")


def git(root, *args, check=True):
    p = subprocess.run(["git", "-C", str(root), *args],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check and p.returncode != 0:
        sys.stderr.write(f"git {' '.join(args)} failed ({p.returncode}): {p.stderr.strip()}\n")
        sys.exit(2)
    return p.stdout


def short(sha):
    return sha[:9] if sha else "?"


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent

    # The tag -> commit map straight from origin (peeled lines resolve annotated
    # tags to their commit; ls-remote prints them as refs/tags/<tag>^{}).
    tags = {}
    skipped = []
    for line in git(root, "ls-remote", "--tags", "origin").splitlines():
        sha, ref = line.split(None, 1)
        ref = ref.strip()
        if ref.startswith("refs/tags/"):
            name = ref[len("refs/tags/"):]
            if name.endswith("^{}"):
                tags[name[:-3]] = (sha, True)
            elif name not in tags or not tags[name][1]:
                tags.setdefault(name, (sha, False))
    git(root, "fetch", "origin", "main")
    main_tip = git(root, "rev-parse", "origin/main").strip()

    blob_cache = {}

    def show(sha, path):
        key = (sha, path)
        if key not in blob_cache:
            p = subprocess.run(["git", "-C", str(root), "show", f"{sha}:{path}"],
                               capture_output=True, text=True, encoding="utf-8", errors="replace")
            blob_cache[key] = p.stdout if p.returncode == 0 else None
        return blob_cache[key]

    def entry_at(sha, plugin):
        text = show(sha, "pluginmaster.json")
        if text is None:
            return None
        try:
            for e in json.loads(text):
                if e.get("InternalName") == plugin:
                    return e
        except json.JSONDecodeError:
            pass
        return None

    stale = []
    checked = 0
    for name in sorted(tags):
        m = TAG_RE.match(name)
        if not m:
            skipped.append(name)
            continue
        plugin, version = m.group(1), m.group(2)
        sha, _ = tags[name]
        checked += 1

        reasons = []
        p = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor", sha, "origin/main"],
                           capture_output=True)
        if p.returncode != 0:
            reasons.append("not reachable from origin/main")

        channels = []
        e = entry_at(sha, plugin)
        if e is None:
            reasons.append("pluginmaster.json at the tagged commit has no entry for " + plugin)
        else:
            for field, chan in (("AssemblyVersion", "latest"), ("TestingAssemblyVersion", "testing")):
                if e.get(field) == version:
                    channels.append(chan)
            if not channels:
                reasons.append(
                    "pluginmaster.json at the tagged commit advertises %s/%s, not %s"
                    % (e.get("AssemblyVersion"), e.get("TestingAssemblyVersion"), version))
        chan_ok = False
        for chan in ("testing", "latest"):
            text = show(sha, f"plugins/{plugin}/{chan}/{plugin}.json")
            if text is not None:
                try:
                    if json.loads(text).get("AssemblyVersion") == version:
                        chan_ok = True
                except json.JSONDecodeError:
                    pass
        if not chan_ok:
            reasons.append("neither channel manifest at the tagged commit carries " + version)

        if reasons:
            stale.append((name, plugin, version, sha, reasons))

    # For each stale tag, find the commit that actually ships the version: the
    # oldest commit on origin/main whose channel manifest for this plugin
    # carries it (that is the release commit - the one that introduced the zip).
    corrections = []
    for name, plugin, version, sha, reasons in stale:
        candidates = []
        for chan in ("testing", "latest"):
            path = f"plugins/{plugin}/{chan}/{plugin}.json"
            for csha in git(root, "log", "origin/main", "--format=%H", "--", path).splitlines():
                text = show(csha, path)
                if text is None:
                    continue
                try:
                    if json.loads(text).get("AssemblyVersion") == version:
                        candidates.append(csha)
                except json.JSONDecodeError:
                    pass
            if candidates:
                break
        fix = candidates[-1] if candidates else None
        corrections.append((name, sha, fix, reasons))

    print(f"origin/main: {short(main_tip)}; {checked} release tags checked"
          + (f"; {len(skipped)} non-release tags skipped ({', '.join(skipped)})" if skipped else ""))
    if not stale:
        for name in sorted(tags):
            m = TAG_RE.match(name)
            if m:
                print(f"OK   {name} -> {short(tags[name][0])}")
        print("OK: every release tag names its own release commit.")
        return 0

    for name, sha, fix, reasons in corrections:
        print(f"STALE {name} -> {short(sha)} ({'; '.join(reasons)})")
        if fix:
            print(f"      true release commit: {short(fix)}   git tag -f {name} {fix}")
        else:
            print("      no commit on origin/main ships this version - manual review needed")
    print(f"{len(stale)} stale tag(s).")
    return 1


if __name__ == "__main__":
    sys.exit(main())
