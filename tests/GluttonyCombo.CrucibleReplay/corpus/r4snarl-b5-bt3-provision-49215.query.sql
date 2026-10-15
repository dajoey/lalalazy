-- window r4snarl-b5-bt3-provision-49215: archive:plugin-lines-20260924-20260930.tsv, build 1.0.4.245
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-09-30T01:21:04Z' - interval '3 seconds' and ts <= timestamptz '2026-09-30T01:21:12Z'
order by ts, line_sha;
