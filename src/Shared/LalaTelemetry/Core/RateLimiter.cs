// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Collections.Generic;

namespace Lalalazy.Telemetry;

/// <summary>
/// Per-fingerprint emit policy for <c>ER|</c> lines. The first <see cref="Burst"/> occurrences of a
/// fingerprint are written in full; after that occurrences are only counted, and at most one
/// <c>summary</c> line per <see cref="SummaryIntervalMs"/> carries the suppressed count. A fingerprint
/// that has been quiet for <see cref="QuietResetMs"/> is re-armed, so a failure that comes back later
/// (another zone, another job) is written in full again. Not thread-safe: the hub calls it under its lock.
/// </summary>
public sealed class RateLimiter
{
    public enum Verdict
    {
        /// <summary>Write the full line.</summary>
        Full,
        /// <summary>Count it; write nothing.</summary>
        Suppressed,
        /// <summary>Write a summary line now (this occurrence is included in its count).</summary>
        Summary,
        /// <summary>
        /// The fingerprint was re-armed after a quiet spell while a count was still unwritten: write the
        /// summary for the OLD occurrences first, then this one in full.
        /// </summary>
        SummaryThenFull,
    }

    /// <summary>What the hub needs to write a summary line.</summary>
    public readonly record struct Pending(string Fingerprint, long Total, long Suppressed, long SpanMs, object? Meta);

    private sealed class State
    {
        public long Total;
        public int FullWritten;
        public long Suppressed;
        public long LastSeenMs;
        public long LastLineMs;
        public object? Meta;
    }

    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

    public RateLimiter(int burst = 3, long summaryIntervalMs = 60_000, long quietResetMs = 300_000, int maxFingerprints = 512)
    {
        Burst = Math.Max(1, burst);
        SummaryIntervalMs = Math.Max(1, summaryIntervalMs);
        QuietResetMs = Math.Max(SummaryIntervalMs, quietResetMs);
        MaxFingerprints = Math.Max(8, maxFingerprints);
    }

    public int Burst { get; }
    public long SummaryIntervalMs { get; }
    public long QuietResetMs { get; }
    public int MaxFingerprints { get; }
    public int Count => _states.Count;

    /// <summary>
    /// Records one occurrence. <paramref name="meta"/> is kept as the "latest occurrence" payload a later
    /// summary line describes. <paramref name="total"/> is the lifetime occurrence count (this one included);
    /// <paramref name="summary"/> is set when the verdict is <see cref="Verdict.Summary"/>.
    /// </summary>
    public Verdict Check(string fingerprint, long nowMs, object? meta, out long total, out Pending summary)
    {
        summary = default;
        if (!_states.TryGetValue(fingerprint, out var st))
        {
            if (_states.Count >= MaxFingerprints)
                EvictOldest();
            st = new State();
            _states[fingerprint] = st;
        }

        // Re-arm after a quiet spell. A count that was never flushed (nobody called Flush) is handed back
        // as a summary of the OLD occurrences, written before this one.
        var rearmed = false;
        if (st.LastSeenMs != 0 && nowMs - st.LastSeenMs >= QuietResetMs)
        {
            if (st.Suppressed > 0)
            {
                summary = new Pending(fingerprint, st.Total, st.Suppressed, nowMs - st.LastLineMs, st.Meta);
                st.Suppressed = 0;
                rearmed = true;
            }
            st.FullWritten = 0;
        }

        st.Total++;
        st.LastSeenMs = nowMs;
        st.Meta = meta;
        total = st.Total;

        if (st.FullWritten < Burst && st.Suppressed == 0)
        {
            st.FullWritten++;
            st.LastLineMs = nowMs;
            return rearmed ? Verdict.SummaryThenFull : Verdict.Full;
        }

        st.Suppressed++;
        if (nowMs - st.LastLineMs >= SummaryIntervalMs)
        {
            summary = new Pending(fingerprint, st.Total, st.Suppressed, nowMs - st.LastLineMs, st.Meta);
            st.Suppressed = 0;
            st.LastLineMs = nowMs;
            return Verdict.Summary;
        }

        return Verdict.Suppressed;
    }

    /// <summary>
    /// Summaries that are due because occurrences stopped arriving (<paramref name="force"/>: every
    /// pending count regardless of age - used on dispose and when a report is filed).
    /// </summary>
    public List<Pending> Flush(long nowMs, bool force)
    {
        List<Pending>? due = null;
        foreach (var kv in _states)
        {
            var st = kv.Value;
            if (st.Suppressed == 0)
                continue;
            if (!force && nowMs - st.LastLineMs < SummaryIntervalMs)
                continue;
            (due ??= new List<Pending>()).Add(new Pending(kv.Key, st.Total, st.Suppressed, nowMs - st.LastLineMs, st.Meta));
            st.Suppressed = 0;
            st.LastLineMs = nowMs;
        }
        return due ?? new List<Pending>(0);
    }

    /// <summary>True when at least one fingerprint has an unwritten suppressed count.</summary>
    public bool HasPending
    {
        get
        {
            foreach (var st in _states.Values)
                if (st.Suppressed > 0)
                    return true;
            return false;
        }
    }

    private void EvictOldest()
    {
        string? oldest = null;
        var oldestMs = long.MaxValue;
        foreach (var kv in _states)
        {
            if (kv.Value.Suppressed > 0)
                continue;
            if (kv.Value.LastSeenMs < oldestMs)
            {
                oldestMs = kv.Value.LastSeenMs;
                oldest = kv.Key;
            }
        }
        if (oldest is not null)
            _states.Remove(oldest);
    }
}
