// Fork (Fight Brain 1a): the forecast's data model and its pure merge/drop rules.
// Deliberately free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.FightBrainHarness) compiles THIS file and asserts the
// exact semantics that ship — the Fight Brain rules:
// duplicate entries for the same hit within 3 s merge and the most specific
// source sets the time; anything 3 s overdue is dropped.

namespace GluttonyCombo.AutoRotation;

using System;
using System.Collections.Generic;

/// <summary> One forecast hit: what will land, when, and where the answer came from. </summary>
/// <param name="Kind">Raidwide, tankbuster, knockback, or a named enemy cast.</param>
/// <param name="AtMs">Unix milliseconds of the moment the hit lands.</param>
/// <param name="Source">Where the entry came from, for the log: timeline, hints, or the cast's action name.</param>
/// <param name="Specificity">Which source wins a merge: 0 most specific (a named cast), 1 a typed hint, 2 a timeline state hint.</param>
/// <param name="SourceId">The action id when the entry came from a cast scan, else 0.</param>
internal readonly record struct FightBrainHit(
    FightBrainHitKind Kind,
    long AtMs,
    string Source,
    int Specificity,
    uint SourceId = 0)
{
    /// <summary> Entries nearer than this to each other describe the same hit and merge. </summary>
    internal const long MergeWindowMs = 3000;

    /// <summary> An entry this far past its time (and still unread) is untrustworthy and dropped. </summary>
    internal const long OverdueMs = 3000;

    internal string Countdown(long nowMs) => $"{(AtMs - nowMs) / 1000.0:F1}s";
}

internal enum FightBrainHitKind
{
    Raidwide,
    Tankbuster,
    Knockback,
    CastHit,
    Downtime,
}

internal static class FightBrainForecastCore
{
    /// <summary>
    ///     The forecast-aware cast gate, shadow (page: a hard cast cannot start when its end, plus
    ///     0.5 s, comes after the moment the player must move): <paramref name="standingInZone"/>
    ///     says the danger zone under the player is already drawn over the player, <paramref name="zoneIn"/>
    ///     is its activation in seconds, <paramref name="castEndIn"/> is cast end + the gate's 0.5 s,
    ///     and <paramref name="walkOut"/> is the time to step clear.
    /// </summary>
    internal static bool WouldBlockCast(double castEndIn, double zoneIn, bool standingInZone, double walkOut) =>
        standingInZone && zoneIn > 0 && castEndIn > zoneIn - walkOut;

    /// <summary>
    ///     The merged forecast: <paramref name="incoming"/> folded into <paramref name="current"/>,
    ///     newest first. Two entries of the same kind within <see cref="FightBrainHit.MergeWindowMs"/>
    ///     are one hit; the more specific source keeps (and re-times) the entry, a cast hint always
    ///     beats a timeline guess. Entries more than <see cref="FightBrainHit.OverdueMs"/> past their
    ///     time are dropped. The list is capped to <paramref name="cap"/> nearest hits.
    /// </summary>
    internal static List<FightBrainHit> Merge(
        IReadOnlyList<FightBrainHit> current, IReadOnlyList<FightBrainHit> incoming, long nowMs, int cap = 12)
    {
        List<FightBrainHit> all = [.. current, .. incoming];
        all.Sort(static (a, b) => a.AtMs.CompareTo(b.AtMs));
        List<FightBrainHit> merged = [];
        foreach (var hit in all)
        {
            if (hit.AtMs < nowMs - FightBrainHit.OverdueMs)
                continue;
            var twin = merged.FindIndex(m => m.Kind == hit.Kind
                                             && Math.Abs(m.AtMs - hit.AtMs) <= FightBrainHit.MergeWindowMs);
            if (twin >= 0)
            {
                if (hit.Specificity < merged[twin].Specificity)
                    merged[twin] = hit;
                continue;
            }
            merged.Add(hit);
        }
        if (merged.Count > cap)
            merged.RemoveRange(cap, merged.Count - cap);
        return merged;
    }
}
