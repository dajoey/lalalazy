-- window r4third2-water-b3-bt2: archive:plugin-lines-since-20261002T2037Z.tsv, build 1.0.4.256
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-02T15:33:45Z' - interval '3 seconds' and ts <= timestamptz '2026-10-02T15:34:07Z'
order by ts, line_sha;
