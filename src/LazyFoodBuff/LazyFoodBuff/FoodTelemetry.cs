using System.Globalization;

namespace LazyFoodBuff;

/// <summary>
///     Optional, off-by-default food-decision tap (v0.1.4.0). When
///     <see cref="Configuration.DecisionTelemetry"/> is on, every settled food
///     decision writes one structured line at Information level through the
///     normal plugin logger:
///     <c>FT|unixms|job|mode|ev|chosenItemId|hq|score|runnersUp</c>.<br />
///     It rides the existing dalamud.log → ffxivdb <c>plugin_log_lines</c>
///     harvest (no transport of its own), so the recommendation can be lined
///     up against what the player was doing at the time.
/// </summary>
/// <remarks>
///     <para><b>Cost when off:</b> one bool read at the emit points in
///     <see cref="FoodService"/>; nothing else runs.</para>
///     <para><b>The point of the card is the runners-up.</b> A log of "I picked
///     X" teaches nothing; "I picked X at 41.20 over Y at 40.80" is a
///     hypothesis you can test against weeks of encounter data. Up to
///     <see cref="FoodTelemetryFormat.MaxRunnersUp"/> alternatives ride along,
///     descending by score.</para>
///     <para><b>Gates.</b> <c>FT|r</c>/<c>FT|n</c> emit only on a changed
///     decision behind a 30 s floor (<see cref="FoodTelemetryFormat.ShouldEmitChange"/>);
///     <c>FT|a</c> is floor-only (<see cref="FoodTelemetryFormat.ShouldEmitEvent"/>)
///     because a re-eat 25 minutes later is a real application, not a repeat —
///     and both exist because the recommendation path is reachable on every
///     500 ms tick and the eat path once retried 10 times in 12 seconds
///     (ffxivdb 2026-09-05 13:50).</para>
///     <para>The line format and the gates live in
///     <see cref="FoodTelemetryFormat"/> so they can be asserted offline by
///     <c>tests/LazyFoodBuff.TelemetryHarness</c>.</para>
/// </remarks>
internal static class FoodTelemetry
{
    /// <inheritdoc cref="FoodTelemetryFormat.Prefix"/>
    public const string Prefix = FoodTelemetryFormat.Prefix;

    /// <summary> Last emitted decision per (job, mode, ev). </summary>
    private static readonly Dictionary<(uint Job, string Mode, string Ev), FoodTelemetryFormat.LastDecision> Last =
        new();

    /// <summary> Forgets every remembered decision (toggle-on, so the current state re-emits). </summary>
    public static void Reset() => Last.Clear();

    /// <summary>
    ///     Records one settled decision. Emits only through the format-class
    ///     gate appropriate to <paramref name="ev"/>; never throws into the
    ///     caller. Callers sort <paramref name="runnersUp"/> descending by
    ///     score and pass up to <see cref="FoodTelemetryFormat.MaxRunnersUp"/>
    ///     (the line truncates there).
    /// </summary>
    /// <param name="job"> The ClassJob RowId the decision was made for. </param>
    /// <param name="mode"> <see cref="FoodTelemetryFormat.ModeAuto"/> / ModeManual / ModeFallback. </param>
    /// <param name="ev"> <see cref="FoodTelemetryFormat.EvRecommend"/> / EvApplied / EvNone. </param>
    /// <param name="chosen"> The food that won, or null when nothing did. </param>
    /// <param name="score"> The winner's Score(). </param>
    /// <param name="runnersUpFactory">
    ///     The alternatives, sorted descending by score — evaluated LAZILY, only
    ///     when the gate decides a line will actually be written (building the
    ///     list costs ~900 inventory reads per pass; the suppressed path must
    ///     not pay them).
    /// </param>
    public static void Record(
        uint job, string mode, string ev,
        Food? chosen, float score, Func<IEnumerable<FoodTelemetryFormat.RunnerUp>?>? runnersUpFactory)
    {
        try
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var itemId = chosen?.Id ?? 0;
            var hq = chosen is { } f && f.InventoryCount(true) > 0;

            var emit = ev == FoodTelemetryFormat.EvApplied
                ? FoodTelemetryFormat.ShouldEmitEvent(Last, job, mode, ev, itemId, hq, score, nowMs)
                : FoodTelemetryFormat.ShouldEmitChange(Last, job, mode, ev, itemId, hq, score, nowMs);

            if (!emit) return;

            Plugin.Log.Information(FoodTelemetryFormat.BuildLine(
                nowMs, job, mode, ev, itemId, hq, score, runnersUpFactory?.Invoke()));
        }
        catch (Exception ex)
        {
            // A telemetry tap must never be able to break food selection.
            Plugin.Log.Debug(ex, "[FoodTelemetry] failed to emit a decision line");
        }
    }
}
