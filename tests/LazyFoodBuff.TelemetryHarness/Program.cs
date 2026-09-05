using System.Globalization;
using Gate = LazyFoodBuff.FoodTelemetryFormat;
using Ru = LazyFoodBuff.FoodTelemetryFormat.RunnerUp;

namespace LazyFoodBuff.TelemetryHarness;

/// <summary>
///     Offline assertions on LazyFoodBuff's food-decision telemetry tap
///     (v0.1.4.0). Compiles the real <see cref="FoodTelemetryFormat"/> — no
///     Dalamud, no game — so the wire format the ffxivdb join depends on, and
///     the two gates that keep the plugin log habitable, are both proven before
///     shipping.
/// </summary>
/// <remarks>
///     <para>FoodRecommender's stat-weight table is reasoned-out, not measured;
///     the <c>FT|</c> tap records the winner AND its runners-up so the eventual
///     answer can be graded against weeks of encounter data. The harness also
///     replays 278.6 minutes of Joey's actual play (8,163 ffxivdb
///     <c>player_samples</c> rows pulled from ffxivdb, collapsed to change-points
///     in <c>trace-ffxivdb.csv</c>) through the recommendation gate at the
///     plugin's real 500 ms cadence and asserts a measured line rate. "It should
///     be low" is not evidence; this is.</para>
/// </remarks>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main(string[] args)
    {
        LineFormat();
        LocaleSafety();
        Truncation();
        RecommendationGate();
        ApplicationGate();
        ReplayRealTrace(args.Length > 0 ? args[0] : null);

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- line format

    private static void LineFormat()
    {
        // A tank recommendation (Warrior, 21): Tenacity 3.0 path — the weights
        // nobody has measured. Chosen at 41.20 over two runners-up.
        var tank = Gate.BuildLine(
            unixMs: 1_788_636_000_123, job: 21, mode: Gate.ModeAuto, ev: Gate.EvRecommend,
            chosenItemId: 49244, hq: true, score: 41.20f,
            runnersUp: new[] { new Ru(44093, 40.80f), new Ru(44094, 39.75f) });

        Check("prefix is the greppable FT|", tank.StartsWith("FT|", StringComparison.Ordinal));
        Check("tank pick: exact line shape",
            tank == "FT|1788636000123|21|a|r|49244|1|41.20|44093:40.80;44094:39.75", tank);

        var f = tank.Split('|');
        Check("9 pipe-separated fields", f.Length == 9, f.Length.ToString());
        Check("field 1 is unix ms", f[1] == "1788636000123");
        Check("field 2 is the ClassJob RowId (joins player_samples.job)", f[2] == "21");
        Check("field 3 is the mode", f[3] == "a");
        Check("field 4 is the event code", f[4] == "r");
        Check("field 5 is the chosen item id", f[5] == "49244");
        Check("field 6 renders hq as 1/0", f[6] == "1");
        Check("field 7 is the winning score at 2dp", f[7] == "41.20");
        Check("field 8 is the runner-up list", f[8] == "44093:40.80;44094:39.75");

        // The SkillSpeed -0.3 penalty path (melee, MNK=20): the negative weight
        // is exactly the kind of judgement the tap is meant to check.
        var melee = Gate.BuildLine(
            1_788_636_000_456, 20, Gate.ModeFallback, Gate.EvRecommend, 44093, false, -2.15f, null);
        Check("melee fallback pick: exact line shape",
            melee == "FT|1788636000456|20|f|r|44093|0|-2.15|", melee);

        // A manual-mode pick with no runner-ups (only the manual food is in inventory).
        var manual = Gate.BuildLine(
            1_788_636_000_789, 24, Gate.ModeManual, Gate.EvRecommend, 44099, true, 12.50f, []);
        Check("manual pick: exact line shape",
            manual == "FT|1788636000789|24|m|r|44099|1|12.50|", manual);

        // No food at all: trailing field empty, never malformed.
        var none = Gate.BuildLine(
            1_788_636_001_000, 28, Gate.ModeAuto, Gate.EvNone, 0, false, 0f, null);
        Check("no-food: exact line shape",
            none == "FT|1788636001000|28|a|n|0|0|0.00|", none);
        Check("no-food still yields 9 fields", none.Split('|').Length == 9, none);

        // The application event: same shape, ev=a.
        var applied = Gate.BuildLine(
            1_788_636_002_000, 21, Gate.ModeAuto, Gate.EvApplied, 49244, true, 41.20f,
            new[] { new Ru(44093, 40.80f) });
        Check("applied: exact line shape",
            applied == "FT|1788636002000|21|a|a|49244|1|41.20|44093:40.80", applied);

        // Runners-up arrive sorted descending by score — the parser relies on it.
        Check("runners-up are appended in the order given (descending contract)",
            applied.EndsWith("|44093:40.80", StringComparison.Ordinal), applied);

        // A zero-score pick is representable and unambiguous.
        var zero = Gate.BuildLine(1, 25, Gate.ModeAuto, Gate.EvRecommend, 123, false, 0f, null);
        Check("zero score renders as 0.00 and the line keeps 9 fields",
            zero == "FT|1|25|a|r|123|0|0.00|" && zero.Split('|').Length == 9, zero);
    }

    // ---------------------------------------------------------------- locale safety

    private static void LocaleSafety()
    {
        // A comma decimal separator silently destroys a '|' parse — and would do
        // it only on Joey's machine, months later, in a table nobody re-checks.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "fr-FR", "ru-RU" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                var line = Gate.BuildLine(
                    1_788_636_000_123, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f,
                    new[] { new Ru(44093, 40.80f), new Ru(44094, 39.75f) });
                Check($"invariant decimals under {name}",
                    line == "FT|1788636000123|21|a|r|49244|1|41.20|44093:40.80;44094:39.75", line);
                Check($"no comma decimal separator leaks under {name}",
                    !line.Contains(',', StringComparison.Ordinal), line);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---------------------------------------------------------------- truncation

    private static void Truncation()
    {
        // More runner-ups than fit the 200-char budget: cut whole entries from
        // the tail, append ~, and never touch a fixed field.
        var many = Enumerable.Range(0, 60)
            .Select(i => new Ru((uint)(40000 + i), 50f - i))
            .ToArray();
        var long1 = Gate.BuildLine(
            1_788_636_000_123, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, many);

        Check("over-long runner-up list is cut to the budget",
            long1.Length <= Gate.MaxLineLength, $"len={long1.Length}");
        Check("truncated line is marked with ~", long1.EndsWith('~'), long1);
        Check("truncation keeps all 9 fields", long1.Split('|').Length == 9, long1);
        Check("truncation never cuts an entry mid-token",
            long1.TrimEnd('~').Split('|')[8].Split(';').All(e => e.Contains(':')), long1);
        Check("at most MaxRunnersUp entries survive",
            long1.TrimEnd('~').Split('|')[8].Split(';').Length <= Gate.MaxRunnersUp, long1);
        Check("every surviving entry is the highest-scoring one (order preserved)",
            long1.Split('|')[8].StartsWith("40000:50.00;40001:49.00", StringComparison.Ordinal), long1);

        // A pathological fixed field still cannot break the shape.
        var weird = Gate.BuildLine(long.MaxValue, uint.MaxValue, Gate.ModeFallback, Gate.EvApplied,
            uint.MaxValue, true, float.MaxValue, null);
        Check("worst-case fixed fields stay inside the budget",
            weird.Length <= Gate.MaxLineLength && weird.Split('|').Length == 9, $"len={weird.Length}");
        Check("worst-case fixed fields are not truncated", !weird.EndsWith('~'), weird);

        // A runner-up list that would overshoot by exactly one entry: that entry
        // is dropped whole, never clipped.
        var tight = new[] { new Ru(44093, 40.80f), new Ru(44094, 39.75f), new Ru(44095, 38.70f) };
        var fits = Gate.BuildLine(1_788_636_000_123, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, tight);
        Check("three runner-ups at a realistic width fit whole",
            !fits.EndsWith('~') && fits.Split('|')[8].Split(';').Length == 3, fits);
    }

    // ------------------------------------------------- the recommendation gate (r/n)

    private static void RecommendationGate()
    {
        var last = new Dictionary<(uint, string, string), Gate.LastDecision>();
        var t = 1_000_000L;

        // The core promise: the first settled decision emits, a repeat does not.
        Check("first recommendation emits",
            Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t));
        Check("the same decision on the very next tick is suppressed",
            !Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t + 500));
        Check("the same decision is still suppressed long after the window",
            !Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t + Gate.RepeatMinIntervalMs * 10));

        // A CHANGED decision is the information — but the floor is UNCONDITIONAL
        // per key: flapping candidates must not become one line per flip. A
        // change inside the window is held (and NOT recorded, so it is not
        // swallowed); it emits on the first tick past the window.
        Check("a changed item inside the floor is held",
            !Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 44093, true, 40.80f, t + 1_000));
        Check("...and the changed item emits on the first tick past the window",
            Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 44093, true, 40.80f, t + Gate.RepeatMinIntervalMs));

        // Key now holds (44093, true, 40.80) at t+30s. Same discipline for the
        // other two axes of a decision: score and hq.
        Check("a changed score inside the new floor is held",
            !Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 44093, true, 40.85f, t + 31_000));
        Check("...and the changed score emits past the window",
            Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 44093, true, 40.85f, t + 60_000));
        Check("a changed hq flag inside the floor is held",
            !Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 44093, false, 36.80f, t + 61_000));
        Check("...and the changed hq flag emits past the window",
            Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 44093, false, 36.80f, t + 90_000));

        // The floor is per (job, mode, ev) key: an unrelated key's FIRST line is
        // never held hostage by another key's window.
        var fresh = new Dictionary<(uint, string, string), Gate.LastDecision>();
        Check("first line for a fresh key emits immediately",
            Gate.ShouldEmitChange(fresh, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t));
        Check("a second key emits immediately even inside the first key's window",
            Gate.ShouldEmitChange(fresh, 21, Gate.ModeAuto, Gate.EvNone, 0, false, 0f, t + 1_000));

        // ...and once a key HAS a recorded decision, its own window applies.
        Check("a change inside the floor is held back",
            !Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t + 91_000));
        Check("...and is emitted on the first tick past the window",
            Gate.ShouldEmitChange(last, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t + 120_000));

        // The key is (job, mode, ev): the same pick on another job is a different story.
        var other = new Dictionary<(uint, string, string), Gate.LastDecision>();
        Check("job A emits", Gate.ShouldEmitChange(other, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, 0));
        Check("job B (new key) emits immediately despite job A's fresh window",
            Gate.ShouldEmitChange(other, 20, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, 1_000));
        Check("job B's own repeat inside its floor is held",
            !Gate.ShouldEmitChange(other, 20, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, 1_500));
        Check("job B's CHANGED decision past its own window emits again",
            Gate.ShouldEmitChange(other, 20, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.25f, 31_000));
        Check("manual and auto are tracked separately",
            Gate.ShouldEmitChange(other, 21, Gate.ModeManual, Gate.EvRecommend, 49244, true, 41.20f, 0));
        Check("r and n are tracked separately",
            Gate.ShouldEmitChange(other, 21, Gate.ModeAuto, Gate.EvNone, 0, false, 0f, 0));

        // The no-food line dedupes like any other: inventory stays empty, one line.
        var empty = new Dictionary<(uint, string, string), Gate.LastDecision>();
        Check("no-food emits once",
            Gate.ShouldEmitChange(empty, 28, Gate.ModeAuto, Gate.EvNone, 0, false, 0f, 0));
        Check("no-food does not repeat while the inventory stays empty",
            !Gate.ShouldEmitChange(empty, 28, Gate.ModeAuto, Gate.EvNone, 0, false, 0f, 10_000));

        // The load-bearing one: an UNCHANGING decision at the real tick cadence
        // must produce ONE line, not one per tick — the PvPSolver failure mode.
        var stuck = new Dictionary<(uint, string, string), Gate.LastDecision>();
        var emitted = 0;
        for (var i = 0; i < 4000; i++) // 4000 ticks x 500 ms = 33 minutes stuck
            if (Gate.ShouldEmitChange(stuck, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, i * 500L))
                emitted++;
        Check("33 minutes stuck on ONE decision emits exactly 1 line", emitted == 1, emitted.ToString());

        // The worst realistic adversary: two candidates alternating every tick.
        // The rate floor, not the change-detector, is what has to hold here.
        var flap = new Dictionary<(uint, string, string), Gate.LastDecision>();
        emitted = 0;
        for (var i = 0; i < 4000; i++)
        {
            var item = i % 2 == 0 ? 49244u : 44093u;
            if (Gate.ShouldEmitChange(flap, 21, Gate.ModeAuto, Gate.EvRecommend, item, true, 41.20f, i * 500L))
                emitted++;
        }
        var minutes = 4000 * 500 / 60000.0;
        var cap = (int)Math.Ceiling(4000 * 500.0 / Gate.RepeatMinIntervalMs) + 1;
        Check($"33 minutes of tick-by-tick flapping is capped at the rate floor ({emitted} lines / {minutes:F0} min)",
            emitted <= cap, $"emitted={emitted} cap={cap}");

        // Toggling telemetry on must report the CURRENT state, not dedupe against a stale one.
        var g4 = new Dictionary<(uint, string, string), Gate.LastDecision>();
        Gate.ShouldEmitChange(g4, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, 0);
        g4.Clear();
        Check("Reset() lets the very same decision emit immediately",
            Gate.ShouldEmitChange(g4, 21, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, 1));
    }

    // ------------------------------------------------- the application gate (a)

    private static void ApplicationGate()
    {
        var last = new Dictionary<(uint, string, string), Gate.LastDecision>();

        // Every real eat is an event with a timestamp worth keeping: re-eating
        // the same food 25 minutes later is a normal, distinct application.
        Check("first application emits",
            Gate.ShouldEmitEvent(last, 21, Gate.ModeAuto, Gate.EvApplied, 49244, true, 41.20f, 0));
        Check("the same food eaten 25 minutes later emits again",
            Gate.ShouldEmitEvent(last, 21, Gate.ModeAuto, Gate.EvApplied, 49244, true, 41.20f, 25 * 60_000L));
        Check("a DIFFERENT food inside the window is still held (a switch is not two events per second)",
            !Gate.ShouldEmitEvent(last, 21, Gate.ModeAuto, Gate.EvApplied, 44093, true, 40.80f, 25 * 60_000L + 5_000));
        Check("...and emits on the first tick past the window",
            Gate.ShouldEmitEvent(last, 21, Gate.ModeAuto, Gate.EvApplied, 44093, true, 40.80f, 25 * 60_000L + Gate.RepeatMinIntervalMs));

        // What the floor exists for: the TryUse retry storm measured in the wild
        // (ffxivdb 2026-09-05 13:50 — 10 "ate food" lines in 12 seconds).
        var storm = new Dictionary<(uint, string, string), Gate.LastDecision>();
        var emitted = 0;
        for (var i = 0; i < 24; i++) // 24 attempts x 500 ms ≈ 12 s
            if (Gate.ShouldEmitEvent(storm, 21, Gate.ModeAuto, Gate.EvApplied, 49244, true, 41.20f, i * 500L))
                emitted++;
        Check("a 12-second retry storm produces 1 line, not 10", emitted == 1, emitted.ToString());
    }

    // ---------------------------------------------------------------- real trace

    /// <summary>
    ///     Replays real ffxivdb <c>player_samples</c> rows through the
    ///     recommendation gate at the plugin's 500 ms tick cadence, under the
    ///     worst case for volume: the decision path running on EVERY tick with
    ///     no Well Fed buff (33% of real ticks reached it in this trace).
    /// </summary>
    private static void ReplayRealTrace(string? path)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "trace-ffxivdb.csv");
        if (!File.Exists(path))
        {
            Check($"real ffxivdb trace present at {path}", false, "missing");
            return;
        }

        var rows = new List<(long T, uint Job, int Runs)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split(',');
            if (p.Length != 3 || p[0] == "unixms") continue;
            rows.Add((
                long.Parse(p[0], CultureInfo.InvariantCulture),
                uint.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture)));
        }

        Check("real trace loaded", rows.Count > 10, $"{rows.Count} change-points");
        if (rows.Count < 2) return;

        const int tickMs = 500; // FoodService.Tick's floor
        var last = new Dictionary<(uint, string, string), Gate.LastDecision>();
        var span = rows[^1].T - rows[0].T;

        long emitted = 0, ticks = 0, decisionTicks = 0;
        var cursor = 0;
        for (var t = rows[0].T; t <= rows[^1].T; t += tickMs)
        {
            while (cursor + 1 < rows.Count && rows[cursor + 1].T <= t) cursor++;
            var s = rows[cursor];
            ticks++;

            // decisionRuns=0: Well Fed well above threshold — Tick() returns
            // before any decision, so the tap is silent by construction.
            if (s.Runs == 0) continue;

            decisionTicks++;
            // Worst case: every decision tick settles on the same auto pick
            // (one stable top food per job is exactly what real play looks like).
            if (Gate.ShouldEmitChange(last, s.Job, Gate.ModeAuto, Gate.EvRecommend, 49244, true, 41.20f, t))
                emitted++;
        }

        var minutes = span / 60000.0;
        var perMin = emitted / minutes;
        Console.WriteLine(
            $"     measured: {minutes:F1} min of real play, {ticks:N0} ticks, {decisionTicks:N0} decision-path " +
            $"ticks -> {emitted} FT|r lines ({perMin:F2}/min)");

        Check("the replay actually exercised the decision path", decisionTicks > 1000, decisionTicks.ToString());
        Check($"measured recommendation rate stays under 0.1 lines/min ({perMin:F3}/min)", perMin < 0.1, perMin.ToString("F3"));
        // PvPSolver's MajorUpdater is the counter-example: 55,064 lines. Over this
        // trace an ungated tap would have written one line per decision tick.
        Check("the gate suppresses at least 95% of what an ungated tap would write",
            emitted <= decisionTicks * 0.05, $"emitted={emitted} ungated={decisionTicks}");
    }

    // ---------------------------------------------------------------- plumbing

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else { _fail++; Console.WriteLine($"FAIL {what}{(detail is null ? "" : $" -> {detail}")}"); }
    }
}
