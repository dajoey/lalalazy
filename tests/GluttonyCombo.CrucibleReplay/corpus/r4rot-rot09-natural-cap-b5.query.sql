-- window r4rot-rot09-natural-cap-b5: ffxivdb, build 1.0.4.301
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-13 19:39:08-04' - interval '3 seconds' and ts <= timestamptz '2026-10-13 19:39:20-04'
order by ts, line_sha;
