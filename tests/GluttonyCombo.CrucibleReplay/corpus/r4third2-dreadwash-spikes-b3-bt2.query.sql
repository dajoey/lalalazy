-- window r4third2-dreadwash-spikes-b3-bt2: archive:plugin-lines-since-20261002T2037Z.tsv, build 1.0.4.251
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-01T23:33:28Z' - interval '3 seconds' and ts <= timestamptz '2026-10-01T23:33:54Z'
order by ts, line_sha;
