-- window firstdegree-3255-corpse-flower: ffxivdb, build 1.0.4.302
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-14 21:15:17+00' - interval '3 seconds' and ts <= timestamptz '2026-10-14 21:18:11+00'
order by ts, line_sha;
