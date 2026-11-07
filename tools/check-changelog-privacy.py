#!/usr/bin/env python3
"""Changelog privacy gate for lalalazy releases.

The changelog ships verbatim to three PUBLIC surfaces: the pluginmaster.json
Changelog field (the Dalamud plugin feed), the manifest embedded in the zip
(shown by the in-game "What's new" popup), and the GitHub Release body. It must
never carry the requester's words or internal fleet references.

Usage:
    python tools/check-changelog-privacy.py --changelog src/<Plugin>/CHANGELOG.md \
        [--extra plugins/<Plugin>/<channel>/<Plugin>.json] [--extra pluginmaster.json]

Exit 0 = clean. Exit 1 = findings listed (release MUST NOT ship).
Runs automatically from tools/Package-Plugin.ps1 before anything is built or
published; run it by hand when editing changelogs outside a release.
"""
import argparse
import re
import sys

# (id, pattern, why) - patterns are matched on the decoded text, case-sensitive
# unless noted. Keep the WHY column factual; it is printed on failure.
PATTERNS = [
    ("requester-name", re.compile(r"\bJoey\b"), "the requester's name"),
    ("relay-agent", re.compile(r"\bDuet\b"), "internal agent name"),
    ("quote-attribution", re.compile(r"live report|reported live|Recorded from the|test verdict|his words|in her words|in his words|work card"), "report/quote attribution phrasing - describe the defect, not who reported it"),
    ("speech-quote", re.compile(r"word for word"), "verbatim-quote framing"),
    ("internal-tracker", re.compile(r"Helm thread|Helm card|\bkanban\b|\bt-[a-z]+-[0-9a-z]{6,}\b|\bt_[0-9a-f]{8}\b"), "internal tracker/thread ids"),
    ("session-reference", re.compile(r"\byour [A-Za-z]+ \d{1,2} session\b|\b(January|February|March|April|May|June|July|August|September|October|November|December) \d{1,2} session\b"), "named private play-session reference"),
    ("internal-network", re.compile(r"\b(192\.168|10\.\d+|172\.(1[6-9]|2\d|3[01]))\.\d+\.\d+\b|\bhelmweb\b|\bjobunthree\b|\bDAJOEYROG\b|\bjobuntwo\b"), "internal host/IP detail"),
    ("character-names", re.compile(r"Grandpa Joe|dajoeybaz|omasky|Bussyqueen|Hussypants"), "character/retainer names"),
]

# Text fields that legitimately contain URLs/ids and are NOT scanned: RepoUrl,
# IconUrl, DownloadLink*, Punchline, AuthorTags. This tool only scans the text
# handed to it (the changelog field / CHANGELOG.md), so URL fields never pass
# through it. Do not "fix" a flagged line by adding an exception here; rewrite
# the changelog line.


def scan_text(label, text, findings):
    for fid, rx, why in PATTERNS:
        for m in rx.finditer(text):
            line = text.count("\n", 0, m.start()) + 1
            snippet = text[max(0, m.start() - 60):m.end() + 60].replace("\n", " ")
            findings.append(f"{label}:{line}: [{fid}] {why} :: ...{snippet}...")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--changelog", required=True, help="CHANGELOG.md path to scan")
    ap.add_argument("--extra", action="append", default=[],
                    help="additional file (manifest / pluginmaster.json) to scan for its Changelog field")
    args = ap.parse_args()

    findings = []
    try:
        with open(args.changelog, encoding="utf-8") as f:
            scan_text(args.changelog, f.read(), findings)
    except OSError as e:
        print(f"cannot read {args.changelog}: {e}")
        return 1

    import json
    for path in args.extra:
        try:
            with open(path, encoding="utf-8") as f:
                doc = json.load(f)
        except (OSError, ValueError) as e:
            print(f"cannot read {path}: {e}")
            return 1
        entries = doc if isinstance(doc, list) else [doc]
        for i, ent in enumerate(entries):
            cl = ent.get("Changelog") if isinstance(ent, dict) else None
            if cl:
                scan_text(f"{path}[{i}].Changelog", cl, findings)

    if findings:
        print("CHANGELOG PRIVACY GATE: FAILED - do not publish this text.")
        print("Changelogs are public (plugin feed, in-game popup, GitHub Release).")
        print("Describe the change in neutral words; never quote the requester,")
        print("name them, or reference internal ids, sessions or network details.")
        print("See CLAUDE.md \"Changelog voice\".")
        for f in findings:
            print(f"  {f}")
        return 1
    print(f"changelog privacy gate: OK ({args.changelog})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
