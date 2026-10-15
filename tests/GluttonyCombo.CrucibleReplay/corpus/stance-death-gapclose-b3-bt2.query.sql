-- window stance-death-gapclose-b3-bt2: archive:plugin-lines-20260924-20260930.tsv, build 1.0.4.235
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-09-26 14:38:15+00:00' - interval '3 seconds' and ts <= timestamptz '2026-09-26 14:39:10+00:00'
order by ts, line_sha;
