-- window r4opt-2-4-zombies: archive:plugin-lines-20260924-20260930.tsv, build 1.0.4.233
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-09-25T01:51:48Z' - interval '3 seconds' and ts <= timestamptz '2026-09-25T01:52:10Z'
order by ts, line_sha;
