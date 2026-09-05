using System.Globalization;
using System.Text;

namespace LazyFoodBuff;

/// <summary>
///     The pure half of LazyFoodBuff's food-decision telemetry tap (v0.1.4.0):
///     the emit gate and the line format, with no Dalamud/game types, so
///     <c>tests/LazyFoodBuff.TelemetryHarness</c> can assert the exact shape of
///     what ships. <see cref="FoodTelemetry"/> is the live half that reads the
///     game state and calls in here.
/// </summary>
/// <remarks>
///     <para><b>Why the gate exists.</b> Food selection LOOKS infrequent, but
///     <see cref="FoodService.Tick"/> runs every framework tick behind a 500 ms
///     floor, and the low-food warning (WarningEnabled=true by default) reaches
///     <see cref="FoodRecommender.RecommendBest"/> through CheckWarning on every
///     tick with no Well Fed buff — measured on 278.6 minutes of real play from
///     ffxivdb: 33% of ticks run the decision path, so an ungated tap writes
///     <c>FT|r</c> lines at ~40/min. That is the PvPSolver failure mode
///     (55,064 lines, 53% of the plugin_log_lines corpus). The same replay
///     through this gate is asserted in the harness at under 0.1 lines/min.</para>
/// </remarks>
internal static class FoodTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'FT|%'</c> in ffxivdb. </summary>
    public const string Prefix = "FT|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 200;

    /// <summary> Most runners-up entries appended before truncation. </summary>
    public const int MaxRunnersUp = 3;

    /// <summary>
    ///     Minimum milliseconds between two emissions for the SAME (job, mode,
    ///     ev) key. A const, not a config knob — a user-tunable spam limit is a
    ///     user-tunable footgun.
    /// </summary>
    public const int RepeatMinIntervalMs = 30_000;

    /// <summary> mode field: AutoSelect mode. </summary>
    public const string ModeAuto = "a";

    /// <summary> mode field: Manual mode. </summary>
    public const string ModeManual = "m";

    /// <summary> mode field: Manual mode that fell back to auto-select. </summary>
    public const string ModeFallback = "f";

    /// <summary> ev field: a recommendation was computed (the candidates list). </summary>
    public const string EvRecommend = "r";

    /// <summary> ev field: the recommended food was actually eaten. </summary>
    public const string EvApplied = "a";

    /// <summary> ev field: nothing scored / no food chosen. </summary>
    public const string EvNone = "n";

    /// <summary> One alternative the chooser passed over: the item id and its score, descending. </summary>
    internal readonly record struct RunnerUp(uint ItemId, float Score);

    /// <summary> The last emitted decision per (job, mode, ev), with the time it was emitted. </summary>
    internal sealed record LastDecision(uint ItemId, bool Hq, float Score, long AtMs);

    /// <summary>
    ///     Builds one telemetry line:
    ///     <c>FT|unixms|job|mode|ev|chosenItemId|hq|score|runnersUp</c>.
    ///     <paramref name="runnersUp"/> is appended in the order given (the
    ///     caller sorts descending by score) and is the only field allowed to
    ///     be truncated: cut on whole entries, append <c>~</c>, never cut a
    ///     fixed field or an entry mid-token.
    /// </summary>
    public static string BuildLine(
        long unixMs, uint job, string mode, string ev,
        uint chosenItemId, bool hq, float score,
        IEnumerable<RunnerUp>? runnersUp)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 64);

        sb.Append(Prefix)
          .Append(unixMs).Append('|')
          .Append(job).Append('|')
          .Append(mode).Append('|')
          .Append(ev).Append('|')
          .Append(chosenItemId).Append('|')
          .Append(hq ? '1' : '0').Append('|')
          .Append(score.ToString("F2", inv)).Append('|');

        // Everything before the runner-up list is what the ffxivdb join needs;
        // the list is the only part allowed to be cut short.
        var written = 0;
        var truncated = false;

        if (runnersUp is not null)
        {
            using var e = runnersUp.GetEnumerator();
            while (e.MoveNext())
            {
                if (written >= MaxRunnersUp) { truncated = true; break; }

                var ru = e.Current;
                var mark = sb.Length;
                if (written > 0) sb.Append(';');
                sb.Append(ru.ItemId).Append(':').Append(ru.Score.ToString("F2", inv));

                // Cut on a whole entry, leaving room for the truncation marker.
                if (sb.Length > MaxLineLength - 1)
                {
                    sb.Length = mark;
                    truncated = true;
                    break;
                }
                written++;
            }
        }

        if (truncated) sb.Append('~');
        return sb.ToString();
    }

    /// <summary>
    ///     The recommendation gate (ev <c>r</c>/<c>n</c>): emit when the settled
    ///     decision for this (job, mode, ev) tuple differs from the last EMITTED
    ///     one for the same tuple, and the rate floor has passed. A decision
    ///     suppressed ONLY by the floor is deliberately NOT recorded, so it
    ///     still emits on the first tick past the window instead of being
    ///     permanently swallowed by a boring transition seconds earlier. An
    ///     UNCHANGED decision never re-emits, however long it holds — the
    ///     runners-up comparison inside each line is the information; a repeat
    ///     of it carries none.
    /// </summary>
    internal static bool ShouldEmitChange(
        Dictionary<(uint Job, string Mode, string Ev), LastDecision> last,
        uint job, string mode, string ev,
        uint itemId, bool hq, float score, long nowMs)
    {
        var key = (job, mode, ev);
        var fresh = new LastDecision(itemId, hq, score, nowMs);

        if (last.TryGetValue(key, out var prev))
        {
            if (prev.ItemId == fresh.ItemId && prev.Hq == fresh.Hq && prev.Score == fresh.Score)
                return false; // unchanged decision: nothing to learn

            if (nowMs - prev.AtMs < RepeatMinIntervalMs)
                return false; // changed inside the floor: stays "new", emits when the window passes
        }

        last[key] = fresh;
        return true;
    }

    /// <summary>
    ///     The application gate (ev <c>a</c>): every real eat is an event with a
    ///     timestamp worth keeping — re-eating the same food 25 minutes later is
    ///     a normal, distinct application, so this is rate-floor ONLY, with no
    ///     change dedupe. What it exists for is the <c>TryUse</c> retry storm
    ///     (measured in the wild: 10 eats logged in 12 seconds), which it caps
    ///     at one line per window.
    /// </summary>
    internal static bool ShouldEmitEvent(
        Dictionary<(uint Job, string Mode, string Ev), LastDecision> last,
        uint job, string mode, string ev,
        uint itemId, bool hq, float score, long nowMs)
    {
        var key = (job, mode, ev);

        if (last.TryGetValue(key, out var prev) && nowMs - prev.AtMs < RepeatMinIntervalMs)
            return false; // inside the floor, however identical or different

        last[key] = new LastDecision(itemId, hq, score, nowMs);
        return true;
    }
}
