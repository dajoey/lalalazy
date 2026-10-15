#!/usr/bin/env python3
"""Pull one window of GluttonyCombo CR| / BT| / DS| lines into a replay-corpus TSV.

Source 1 (default): the live ffxivdb on jobunthree (`docker exec ffxivdb psql ...`, read-only SELECT).
Source 2 (--from-tsv): an archived plugin-lines TSV (ts<TAB>level<TAB>file<TAB>context<TAB>message).

Output (UTF-8, LF): `#key=value` header lines, then one `<ISO-8601 UTC ts><TAB><message>` row per line.
Beside it (with --name) a `<name>.query.sql` holding the exact SELECT, so the window can be re-pulled.

  fetch_window.py --from "2026-10-14 20:54:57-04" --to "2026-10-14 20:55:59-04" --out /tmp/w.tsv
  fetch_window.py --from ... --to ... --name abaddon-idle-hold --corpus tests/GluttonyCombo.CrucibleReplay/corpus \
      --piece "Abaddon (board 5)" --tags hold,do-not-attack
"""
import argparse, os, re, subprocess, sys

PREFIXES = "CR|BT|DS"


def sql_for(ts_from, ts_to, lead):
    return (
        "select to_char(ts at time zone 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.MS\"Z\"') || E'\\t' || message\n"
        "from plugin_log_lines\n"
        f"where context = 'GluttonyCombo' and message ~ '^({PREFIXES})\\|'\n"
        f"  and ts >= timestamptz '{ts_from}' - interval '{lead} seconds' and ts <= timestamptz '{ts_to}'\n"
        "order by ts, line_sha;"
    )


def psql(sql):
    out = subprocess.run(
        ["docker", "exec", "-i", "ffxivdb", "psql", "-U", "ffxiv", "-d", "ffxivdb", "-At", "-v", "ON_ERROR_STOP=1"],
        input=sql, capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit("psql failed: " + out.stderr.strip())
    return out.stdout.splitlines()


def build_of(ts_from):
    sql = ("select substring(message from 'v=([0-9.]+)') from plugin_log_lines where context='GluttonyCombo' "
           f"and message like 'ER|%|start|p=GluttonyCombo|%' and ts <= timestamptz '{ts_from}' order by ts desc limit 1;")
    rows = psql(sql)
    return rows[0] if rows else "unknown"


def from_archive(path, ts_from, ts_to, lead):
    from datetime import datetime, timedelta, timezone
    def p(s):
        d = datetime.fromisoformat(s.replace(" ", "T"))
        return d if d.tzinfo else d.replace(tzinfo=timezone.utc)
    lo, hi = p(ts_from) - timedelta(seconds=lead), p(ts_to)
    build, rows = "unknown", []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t", 4)
            if len(parts) < 5 or parts[3] != "GluttonyCombo":
                continue
            when = p(parts[0])
            if when <= lo and parts[4].startswith("ER|") and "|start|p=GluttonyCombo|" in parts[4]:
                m = re.search(r"\|v=([0-9.]+)", parts[4])
                build = m.group(1) if m else build
            if lo <= when <= hi and re.match(rf"({PREFIXES})\|", parts[4]):
                rows.append(parts[0] + "\t" + parts[4])
    return build, rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--from", dest="ts_from", required=True)
    ap.add_argument("--to", dest="ts_to", required=True)
    ap.add_argument("--out")
    ap.add_argument("--name")
    ap.add_argument("--corpus", default=os.path.join(os.path.dirname(__file__), "..", "corpus"))
    ap.add_argument("--lead", type=int, default=3, help="seconds of BT|/DS| context before --from (default 3)")
    ap.add_argument("--from-tsv", help="read an archived plugin-lines TSV instead of ffxivdb")
    ap.add_argument("--piece", default="")
    ap.add_argument("--tags", default="")
    ap.add_argument("--note", default="")
    a = ap.parse_args()
    if not a.out and not a.name:
        ap.error("give --out or --name")

    sql = sql_for(a.ts_from, a.ts_to, a.lead)
    if a.from_tsv:
        build, rows = from_archive(a.from_tsv, a.ts_from, a.ts_to, a.lead)
        source = "archive:" + os.path.basename(a.from_tsv)
    else:
        build = build_of(a.ts_from)
        rows = [r for r in psql(sql) if r]
        source = "ffxivdb"
    if not rows:
        sys.exit("no CR|/BT|/DS| lines in that window")

    head = [f"#source={source}", f"#from={a.ts_from}", f"#to={a.ts_to}", f"#lead_s={a.lead}", f"#build={build}"]
    if a.piece: head.append(f"#piece={a.piece}")
    if a.tags: head.append(f"#tags={a.tags}")
    if a.note: head.append(f"#note={a.note}")
    body = "\n".join(head + rows) + "\n"

    out = a.out or os.path.join(a.corpus, a.name + ".tsv")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)
    if a.name:
        with open(os.path.join(a.corpus, a.name + ".query.sql"), "w", encoding="utf-8", newline="\n") as f:
            f.write(f"-- window {a.name}: {source}, build {build}\n-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file\n" + sql + "\n")
    n = lambda p: sum(1 for r in rows if r.split("\t", 1)[1].startswith(p))
    print(f"wrote {out}: {len(rows)} lines (CR {n('CR|')}, BT {n('BT|')}, DS {n('DS|')}), build {build}")


if __name__ == "__main__":
    main()
