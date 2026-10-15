-- window r4opt-1-5-wisps: archive:plugin-lines-20260924-20260930.tsv, build 1.0.4.245
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-09-29T00:14:20Z' - interval '3 seconds' and ts <= timestamptz '2026-09-29T00:15:25Z'
order by ts, line_sha;
