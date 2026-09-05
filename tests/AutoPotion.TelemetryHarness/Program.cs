using System.Globalization;
using AutoPotion;
using Gate = AutoPotion.PotionTelemetryFormat.NearMissGate;

namespace AutoPotion.TelemetryHarness;

/// <summary>
///     Offline assertions on AutoPotion's decision telemetry tap (v0.2.4.0).
///     Compiles the real <see cref="PotionTelemetryFormat"/> — no Dalamud, no game —
///     so the wire format the ffxivdb join depends on, and the near-miss gate that
///     keeps the plugin log habitable, are both proven before shipping.
/// </summary>
/// <remarks>
///     The last block replays 214 minutes of Joey's actual play (6,175
///     <c>player_samples</c> rows pulled from ffxivdb, collapsed to change-points in
///     <c>trace-ffxivdb.csv</c>) through the gate at the plugin's real 150 ms tick
///     cadence and asserts a measured line rate. "It should be low" is not evidence;
///     this is.
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
        GateBehaviour();
        ReplayRealTrace(args.Length > 0 ? args[0] : null);

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- line format

    private static void LineFormat()
    {
        // An HP potion fired: WHM (24) at 43.2% against a 60% threshold, in combat, in a duty.
        // 4551 = Grade 8 Tincture-shaped id; any id round-trips, this one is just representative.
        var hp = PotionTelemetryFormat.BuildLine(
            unixMs: 1_788_636_000_123, job: 24, ev: PotionTelemetryFormat.EvHpFired, itemId: 4551,
            hpPct: 43.2f, hpThr: 60f, mpPct: 71.5f, mpThr: 30f,
            inCombat: true, inDuty: true, deepDungeon: false,
            reason: PotionTelemetryFormat.ReasonOk, item: "Hi-Potion");

        Check("prefix is the greppable PT|", hp.StartsWith("PT|", StringComparison.Ordinal));
        Check("HP fire: exact line shape",
            hp == "PT|1788636000123|24|h|4551|43.2|60.0|71.5|30.0|1|1|0|ok|Hi-Potion", hp);

        var f = hp.Split('|');
        Check("14 pipe-separated fields", f.Length == 14, f.Length.ToString());
        Check("field 1 is unix ms", f[1] == "1788636000123");
        Check("field 2 is the ClassJob RowId (joins player_samples.job)", f[2] == "24");
        Check("field 3 is the event code", f[3] == "h");
        Check("field 4 is the item id", f[4] == "4551");
        Check("hp% and threshold are both 1dp", f[5] == "43.2" && f[6] == "60.0");
        Check("mp% and threshold are both 1dp", f[7] == "71.5" && f[8] == "30.0");
        Check("booleans render as 1/0", f[9] == "1" && f[10] == "1" && f[11] == "0");
        Check("reason code on a fire is 'ok'", f[12] == "ok");
        Check("item name is the last field", f[13] == "Hi-Potion");

        // An MP potion fired: BLM (25), MP the interesting axis.
        var mp = PotionTelemetryFormat.BuildLine(
            1_788_636_000_456, 25, PotionTelemetryFormat.EvMpFired, 4556,
            88.0f, 60f, 21.7f, 30f, true, false, false,
            PotionTelemetryFormat.ReasonOk, "Hi-Ether");
        Check("MP fire: exact line shape",
            mp == "PT|1788636000456|25|m|4556|88.0|60.0|21.7|30.0|1|0|0|ok|Hi-Ether", mp);

        // A deep dungeon regen potion fired: note deepDungeon=1.
        var rg = PotionTelemetryFormat.BuildLine(
            1_788_636_000_789, 21, PotionTelemetryFormat.EvRegenFired, 20309,
            62.5f, 80f, PotionTelemetryFormat.NoMpPool, 30f, true, true, true,
            PotionTelemetryFormat.ReasonOk, "Sustaining Potion");
        Check("regen fire: exact line shape",
            rg == "PT|1788636000789|21|r|20309|62.5|80.0|-1.0|30.0|1|1|1|ok|Sustaining Potion", rg);
        Check("a job with no MP pool renders mpPct as the -1.0 sentinel", rg.Split('|')[7] == "-1.0");

        // A near-miss: no item, and a short stable reason code rather than English prose.
        var nm = PotionTelemetryFormat.BuildLine(
            1_788_636_001_000, 24, PotionTelemetryFormat.EvNearMiss, 0,
            43.2f, 60f, 71.5f, 30f, true, true, false,
            PotionTelemetryFormat.ReasonHpOver, null);
        Check("near-miss: exact line shape",
            nm == "PT|1788636001000|24|n|0|43.2|60.0|71.5|30.0|1|1|0|hpover|", nm);
        Check("near-miss still yields 14 fields (trailing item empty, never malformed)",
            nm.Split('|').Length == 14, nm);
        Check("near-miss carries itemId 0", nm.Split('|')[4] == "0");

        // Every reason code stays short and separator-free, or the ffxivdb parse breaks.
        string[] reasons =
        [
            PotionTelemetryFormat.ReasonOk,
            PotionTelemetryFormat.ReasonHpOver, PotionTelemetryFormat.ReasonHpBlocked,
            PotionTelemetryFormat.ReasonHpNoStock, PotionTelemetryFormat.ReasonHpUseFail,
            PotionTelemetryFormat.ReasonMpBlocked, PotionTelemetryFormat.ReasonMpNoStock,
            PotionTelemetryFormat.ReasonMpUseFail,
            PotionTelemetryFormat.ReasonRgRehab, PotionTelemetryFormat.ReasonRgBlocked,
            PotionTelemetryFormat.ReasonRgNoStock, PotionTelemetryFormat.ReasonRgUseFail,
        ];
        Check("every reason code is short, lowercase and pipe-free",
            reasons.All(r => r.Length is > 0 and <= 10 && !r.Contains('|') && r == r.ToLowerInvariant()));
        Check("reason codes are all distinct", reasons.Distinct().Count() == reasons.Length);

        // A translated item name containing the separator must not fabricate a field.
        var evil = PotionTelemetryFormat.BuildLine(
            1, 24, PotionTelemetryFormat.EvHpFired, 4551, 50f, 60f, 50f, 30f,
            true, false, false, PotionTelemetryFormat.ReasonOk, "Weird|Name");
        Check("a '|' inside an item name cannot add a field", evil.Split('|').Length == 14, evil);
        Check("the offending '|' is replaced, not dropped", evil.EndsWith("Weird/Name", StringComparison.Ordinal), evil);
    }

    // ---------------------------------------------------------------- locale safety

    private static void LocaleSafety()
    {
        // A comma decimal separator silently destroys a '|' parse — and would do it
        // only on Joey's machine, months later, in a table nobody re-checks.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "fr-FR", "ru-RU" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                var line = PotionTelemetryFormat.BuildLine(
                    1_788_636_000_123, 24, PotionTelemetryFormat.EvHpFired, 4551,
                    43.2f, 60f, 71.5f, 30f, true, true, false,
                    PotionTelemetryFormat.ReasonOk, "Hi-Potion");
                Check($"invariant decimals under {name}",
                    line == "PT|1788636000123|24|h|4551|43.2|60.0|71.5|30.0|1|1|0|ok|Hi-Potion", line);
                Check($"no comma decimal separator leaks under {name}",
                    !line.Contains(',', StringComparison.Ordinal), line);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---------------------------------------------------------------- truncation

    private static void Truncation()
    {
        // A pathological item name must be cut, and cut in the LAST field only.
        var longName = PotionTelemetryFormat.BuildLine(
            1_788_636_000_123, 24, PotionTelemetryFormat.EvHpFired, 4551,
            43.2f, 60f, 71.5f, 30f, true, true, false,
            PotionTelemetryFormat.ReasonOk, new string('X', 400));

        Check("over-long line is cut to the budget",
            longName.Length <= PotionTelemetryFormat.MaxLineLength, $"len={longName.Length}");
        Check("truncated line is marked with ~", longName.EndsWith('~'), longName);
        Check("truncation keeps all 14 fields", longName.Split('|').Length == 14, longName);
        Check("truncation only ever cuts the LAST field",
            longName.StartsWith("PT|1788636000123|24|h|4551|43.2|60.0|71.5|30.0|1|1|0|ok|", StringComparison.Ordinal),
            longName);

        // An in-budget name is left completely alone.
        var fits = PotionTelemetryFormat.BuildLine(
            1_788_636_000_123, 24, PotionTelemetryFormat.EvHpFired, 4551,
            43.2f, 60f, 71.5f, 30f, true, true, false,
            PotionTelemetryFormat.ReasonOk, "Grade 8 Tincture of Mind");
        Check("a name that fits is not truncated", !fits.EndsWith('~'), fits);
        Check("a name that fits survives byte-for-byte",
            fits.EndsWith("|Grade 8 Tincture of Mind", StringComparison.Ordinal), fits);

        // Longest realistic values in every fixed field: still inside the budget with
        // room for a name, i.e. truncation can never eat a field the join depends on.
        var worst = PotionTelemetryFormat.BuildLine(
            long.MaxValue, uint.MaxValue, PotionTelemetryFormat.EvNearMiss, uint.MaxValue,
            -100.5f, 100f, -100.5f, 100f, true, true, true,
            PotionTelemetryFormat.ReasonHpNoStock, null);
        Check("worst-case fixed fields stay well inside the budget",
            worst.Length <= PotionTelemetryFormat.MaxLineLength && worst.Split('|').Length == 14,
            $"len={worst.Length} :: {worst}");
        Check("worst-case fixed fields are not truncated", !worst.EndsWith('~'), worst);
    }

    // ---------------------------------------------------------------- the gate

    private static void GateBehaviour()
    {
        const int win = PotionTelemetryFormat.NearMissMinIntervalMs;
        Check("the rate-limit window is a const of at least 5s", win >= 5000, win.ToString());

        var g = new Gate();
        var t = 1_000_000L;

        // The core promise: emit on change, suppress the repeat.
        Check("first near-miss emits",
            PotionTelemetryFormat.ShouldEmitNearMiss(g, 24, PotionTelemetryFormat.ReasonHpOver, t));
        Check("the same reason on the very next tick is suppressed",
            !PotionTelemetryFormat.ShouldEmitNearMiss(g, 24, PotionTelemetryFormat.ReasonHpOver, t + 150));
        Check("the same reason is still suppressed long after the window",
            !PotionTelemetryFormat.ShouldEmitNearMiss(g, 24, PotionTelemetryFormat.ReasonHpOver, t + win * 10));

        // A changed reason inside the window is held, then released — never dropped.
        Check("a NEW reason inside the rate window is held back",
            !PotionTelemetryFormat.ShouldEmitNearMiss(g, 24, PotionTelemetryFormat.ReasonHpNoStock, t + win - 1));
        Check("...and is emitted on the first tick past the window",
            PotionTelemetryFormat.ShouldEmitNearMiss(g, 24, PotionTelemetryFormat.ReasonHpNoStock, t + win));

        // The key is (job, reason): the same reason on a different job is a different story.
        var g2 = new Gate();
        Check("job A emits", PotionTelemetryFormat.ShouldEmitNearMiss(g2, 24, PotionTelemetryFormat.ReasonHpOver, 0));
        Check("same reason, different job, inside the window is held",
            !PotionTelemetryFormat.ShouldEmitNearMiss(g2, 25, PotionTelemetryFormat.ReasonHpOver, win - 1));
        Check("same reason, different job, past the window emits",
            PotionTelemetryFormat.ShouldEmitNearMiss(g2, 25, PotionTelemetryFormat.ReasonHpOver, win));

        // A resolved threshold re-arms the reason (so a later dip is reported again)
        // without re-arming the clock (so flapping still cannot spam).
        var g3 = new Gate();
        PotionTelemetryFormat.ShouldEmitNearMiss(g3, 24, PotionTelemetryFormat.ReasonHpOver, 0);
        g3.NoteResolved();
        Check("after resolving, the same reason inside the window is still rate-limited",
            !PotionTelemetryFormat.ShouldEmitNearMiss(g3, 24, PotionTelemetryFormat.ReasonHpOver, win - 1));
        Check("after resolving, the same reason past the window emits again",
            PotionTelemetryFormat.ShouldEmitNearMiss(g3, 24, PotionTelemetryFormat.ReasonHpOver, win));

        // Toggling telemetry on must report the CURRENT state, not dedupe against a stale one.
        var g4 = new Gate();
        PotionTelemetryFormat.ShouldEmitNearMiss(g4, 24, PotionTelemetryFormat.ReasonHpOver, 0);
        g4.Reset();
        Check("Reset() lets the very same state emit immediately",
            PotionTelemetryFormat.ShouldEmitNearMiss(g4, 24, PotionTelemetryFormat.ReasonHpOver, 1));

        // The load-bearing one: an unchanging near-miss at the real tick cadence must
        // produce ONE line, not one per tick. This is the PvPSolver failure mode.
        var g5 = new Gate();
        var emitted = 0;
        for (var i = 0; i < 4000; i++) // 4000 ticks x 150 ms = 10 minutes stuck below threshold
            if (PotionTelemetryFormat.ShouldEmitNearMiss(g5, 24, PotionTelemetryFormat.ReasonHpNoStock, i * 150L))
                emitted++;
        Check("10 minutes stuck in ONE near-miss state emits exactly 1 line", emitted == 1, emitted.ToString());

        // And the worst realistic adversary: two states alternating every single tick.
        // The rate limit, not the change-detector, is what has to hold here.
        var g6 = new Gate();
        emitted = 0;
        for (var i = 0; i < 4000; i++)
        {
            var reason = i % 2 == 0 ? PotionTelemetryFormat.ReasonHpOver : PotionTelemetryFormat.ReasonHpNoStock;
            if (PotionTelemetryFormat.ShouldEmitNearMiss(g6, 24, reason, i * 150L))
                emitted++;
        }
        var minutes = 4000 * 150 / 60000.0;
        var cap = (int)Math.Ceiling(4000 * 150.0 / win) + 1;
        Check($"10 minutes of tick-by-tick flapping is capped at the rate limit ({emitted} lines / {minutes:F0} min)",
            emitted <= cap, $"emitted={emitted} cap={cap}");
    }

    // ---------------------------------------------------------------- real trace

    /// <summary>
    ///     Replays real ffxivdb <c>player_samples</c> rows through the gate at the
    ///     plugin's 150 ms tick cadence, under Joey's shipped defaults
    ///     (OnlyInCombat=true, HpPotionThreshold=60), with an empty potion bag so
    ///     every crossing is a near-miss — the worst case for line volume.
    /// </summary>
    private static void ReplayRealTrace(string? path)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "trace-ffxivdb.csv");
        if (!File.Exists(path))
        {
            Check($"real ffxivdb trace present at {path}", false, "missing");
            return;
        }

        var rows = new List<(long T, uint Job, int Hp, int MaxHp, int Mp, int MaxMp, bool InCombat)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split(',');
            if (p.Length != 7) continue;
            rows.Add((
                long.Parse(p[0], CultureInfo.InvariantCulture),
                uint.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture),
                int.Parse(p[3], CultureInfo.InvariantCulture),
                int.Parse(p[4], CultureInfo.InvariantCulture),
                int.Parse(p[5], CultureInfo.InvariantCulture),
                p[6] == "1"));
        }

        Check("real trace loaded", rows.Count > 100, $"{rows.Count} change-points");
        if (rows.Count < 2) return;

        const float hpThreshold = 60f;   // shipped JobPotionSettings default
        const int tickMs = 150;          // PotionService's idle throttle
        var gate = new Gate();
        var span = rows[^1].T - rows[0].T;

        long emitted = 0, ticks = 0, crossings = 0;
        var cursor = 0;
        for (var t = rows[0].T; t <= rows[^1].T; t += tickMs)
        {
            while (cursor + 1 < rows.Count && rows[cursor + 1].T <= t) cursor++;
            var s = rows[cursor];
            ticks++;

            // OnlyInCombat=true: out of combat the plugin returns before any decision,
            // so the tap is silent by construction.
            if (!s.InCombat || s.MaxHp <= 0) continue;

            var hpPct = 100f * s.Hp / s.MaxHp;
            if (hpPct > hpThreshold) { gate.NoteResolved(); continue; }

            crossings++;
            if (PotionTelemetryFormat.ShouldEmitNearMiss(gate, s.Job, PotionTelemetryFormat.ReasonHpNoStock, t))
                emitted++;
        }

        var minutes = span / 60000.0;
        var perMin = emitted / minutes;
        Console.WriteLine(
            $"     measured: {minutes:F1} min of real play, {ticks:N0} ticks, {crossings:N0} below-threshold " +
            $"ticks -> {emitted} PT| near-miss lines ({perMin:F2}/min, 1 per {(crossings == 0 ? 0 : (double)crossings / Math.Max(emitted, 1)):F0} crossings)");

        Check("the replay actually exercised the near-miss path", crossings > 0, crossings.ToString());
        Check($"measured near-miss rate stays under 1 line/min ({perMin:F2}/min)", perMin < 1.0, perMin.ToString("F3"));
        // PvPSolver's MajorUpdater is the counter-example: 55,064 lines. Over this trace
        // an ungated tap would have written one line per below-threshold tick.
        Check("the gate suppresses at least 90% of what an ungated tap would write",
            emitted <= crossings * 0.10, $"emitted={emitted} ungated={crossings}");
    }

    // ---------------------------------------------------------------- plumbing

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else { _fail++; Console.WriteLine($"FAIL {what}{(detail is null ? "" : $" -> {detail}")}"); }
    }
}
