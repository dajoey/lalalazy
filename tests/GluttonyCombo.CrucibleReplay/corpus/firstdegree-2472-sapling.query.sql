-- window firstdegree-2472-sapling: archive:plugin-lines-since-20261006T0000Z-saved-20261013T1808Z.tsv, build 1.0.4.288
-- run: docker exec -i ffxivdb psql -U ffxiv -d ffxivdb -At < this file
select to_char(ts at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || E'\t' || message
from plugin_log_lines
where context = 'GluttonyCombo' and message ~ '^(CR|BT|DS)\|'
  and ts >= timestamptz '2026-10-06 20:25:19+00' - interval '3 seconds' and ts <= timestamptz '2026-10-06 20:29:27+00'
order by ts, line_sha;
