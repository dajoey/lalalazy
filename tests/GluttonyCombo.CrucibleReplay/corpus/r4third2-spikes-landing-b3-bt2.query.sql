-- window r4third2-spikes-landing-b3-bt2: archive:plugin-lines-20260924-20260930.tsv, build 1.0.4.235
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-09-26T14:38:15Z' - interval '3 seconds' and ts <= timestamptz '2026-09-26T14:38:27Z'
order by ts, line_sha;
