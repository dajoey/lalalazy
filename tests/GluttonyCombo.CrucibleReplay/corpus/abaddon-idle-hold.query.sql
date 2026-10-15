-- window abaddon-idle-hold: ffxivdb, build 1.0.4.304
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-14 20:54:57-04' - interval '3 seconds' and ts <= timestamptz '2026-10-14 20:55:59-04'
order by ts, line_sha;
