-- window rotation-petsave-b4-bt3: ffxivdb, build 1.0.4.302
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-14 20:56:20+00' - interval '3 seconds' and ts <= timestamptz '2026-10-14 20:57:10+00'
order by ts, line_sha;
