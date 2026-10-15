-- window r4snarl-b1-bt1-bone-knight-pull: archive:plugin-lines-20260924-20260930.tsv, build 1.0.4.243
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-09-28T01:31:30Z' - interval '3 seconds' and ts <= timestamptz '2026-09-28T01:32:20Z'
order by ts, line_sha;
