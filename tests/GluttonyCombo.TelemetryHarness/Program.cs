using System.Globalization;
using GluttonyCombo.Data;
using Buff = GluttonyCombo.Data.ComboTelemetryFormat.Buff;
using BstSnapshot = GluttonyCombo.Data.BeastmasterTelemetryFormat.Snapshot;

namespace GluttonyCombo.TelemetryHarness;

/// <summary>
///     Offline assertions on the fork's combo-decision telemetry tap
///     (GluttonyCombo v1.0.4.168). Compiles the real
///     <see cref="ComboTelemetryFormat"/> — no Dalamud, no game — so the wire
///     format the ffxivdb join depends on is proven before shipping.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        // A representative RDM decision: Jolt III (7524) replaced by Verthunder III (25855).
        var line = ComboTelemetryFormat.BuildLine(
            unixMs: 1_788_636_000_123,
            job: "RDM",
            combo: "RDM_ST_SimpleMode",
            original: 7524,
            chosen: 25855,
            gcdRemaining: 1.87f,
            weaveCount: 0,
            canWeave: true,
            targetHpPct: 63.5f,
            buffs: [new Buff(1249, true, 26.4f), new Buff(1234, true, null), new Buff(3211, false, 8.0f)]);

        Check("prefix is the greppable CT|", line.StartsWith("CT|", StringComparison.Ordinal));
        Check("exact line shape",
            line == "CT|1788636000123|RDM|RDM_ST_SimpleMode|7524|25855|1.87|0+|63.5|1249:26.4;1234:-;t3211:8.0",
            line);

        var fields = line.Split('|');
        Check("10 pipe-separated fields", fields.Length == 10, fields.Length.ToString());
        Check("field 1 is unix ms", fields[1] == "1788636000123");
        Check("originalActionId in field 4", fields[4] == "7524");
        Check("chosenActionId in field 5 (the join key)", fields[5] == "25855");
        Check("gcdRemaining 2dp", fields[6] == "1.87");
        Check("weave slot carries count + can-weave", fields[7] == "0+");
        Check("target HP% 1dp", fields[8] == "63.5");
        Check("absent-but-consulted status renders as id:-", fields[9].Contains("1234:-", StringComparison.Ordinal));
        Check("non-player status is t-prefixed", fields[9].Contains("t3211:8.0", StringComparison.Ordinal));

        // Culture must not be able to turn 1.87 into 1,87 and break the parser.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = ComboTelemetryFormat.BuildLine(
                1_788_636_000_123, "RDM", "RDM_ST_SimpleMode", 7524, 25855,
                1.87f, 0, true, 63.5f, [new Buff(1249, true, 26.4f)]);
            Check("invariant decimals under de-DE",
                german == "CT|1788636000123|RDM|RDM_ST_SimpleMode|7524|25855|1.87|0+|63.5|1249:26.4", german);
        }
        finally { CultureInfo.CurrentCulture = previous; }

        // No buffs consulted: the trailing field is simply empty, never malformed.
        var noBuffs = ComboTelemetryFormat.BuildLine(
            1, "WHM", "WHM_ST_MainCombo", 119, 3568, 2.50f, 2, false, 100.0f, []);
        Check("empty keyBuffs still yields 10 fields", noBuffs.Split('|').Length == 10, noBuffs);
        Check("weave slot renders can-weave false", noBuffs.Split('|')[7] == "2-", noBuffs);

        // Budget: a flood of consulted statuses must not blow the ~200 char line.
        var many = Enumerable.Range(0, 40).Select(i => new Buff((uint)(3000 + i), i % 2 == 0, 12.3f)).ToArray();
        var long1 = ComboTelemetryFormat.BuildLine(
            1_788_636_000_123, "SGE", "SGE_ST_DPS", 24283, 24284, 2.44f, 1, true, 12.7f, many);
        Check("line stays within the 200-char budget",
            long1.Length <= ComboTelemetryFormat.MaxLineLength, $"len={long1.Length}");
        Check("truncated line is marked with ~", long1.EndsWith('~'), long1);
        Check("truncation keeps all 10 fields", long1.Split('|').Length == 10, long1);
        Check("truncation never cuts a buff mid-entry",
            long1.TrimEnd('~').Split('|')[9].Split(';').All(e => e.Length == 0 || e.Contains(':')), long1);

        // A long combo name must not silently eat the buff list's structure.
        var longName = ComboTelemetryFormat.BuildLine(
            1_788_636_000_123, "BLU", new string('X', 90), 11385, 11390, 2.20f, 0, false, 99.9f,
            [new Buff(1234, true, 15.0f), new Buff(1235, true, 15.0f)]);
        Check("long combo name still yields 10 fields", longName.Split('|').Length == 10, longName);

        // The emit gate: one line per CHANGE, not per frame.
        var seen = new Dictionary<(uint, uint), uint>();
        Check("first decision emits", ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 25855));
        Check("same decision repeated does not emit", !ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 25855));
        Check("changed choice emits", ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 7524));
        Check("back to the old choice emits", ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 25855));
        Check("a different button is tracked separately", ComboTelemetryFormat.ShouldEmit(seen, 1, 7503, 25855));
        Check("a different preset is tracked separately", ComboTelemetryFormat.ShouldEmit(seen, 2, 7524, 25855));
        Check("unchanged-action decisions also de-duplicate",
            ComboTelemetryFormat.ShouldEmit(seen, 3, 16457, 16457) &&
            !ComboTelemetryFormat.ShouldEmit(seen, 3, 16457, 16457));

        BeastmasterCases();

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The Beastmaster BT| collector: line shape, the change gate, the 4/s rate floor,
    ///     and a REPLAY of real ffxivdb Beastmaster play to measure the actual line rate.
    /// </summary>
    private static void BeastmasterCases()
    {
        Console.WriteLine("-- BST collector (BT|) --");

        var snap = new BstSnapshot(
            TPGauge: 100, FamiliarTPGauge: 132, FamiliarTPAtLastUse: 168,
            ActiveBattlehorn: 2, InstinctualComboState: 7, CurrentAffinity: 5,
            ChainCount: 3, KinshipState: 0x51,
            PetObjectId: 1073741830, PetName: "Cu Sith", PetDataId: 5432,
            AdjustedBeastMode: 44896, AdjustedAvalanche: 44930,
            Statuses: new ushort[] { 4599, 4601, 4621 });

        var line = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577, snap);

        Check("BST prefix is the greppable BT|", line.StartsWith("BT|", StringComparison.Ordinal), line);
        Check("BST exact line shape",
            line == "BT|1788904962577|6484a80207050351|2|5|3|81|pet=1073741830:Cu Sith:5432|bm=44896|av=44930|4599,4601,4621",
            line);
        Check("BST gauge hex is 16 chars (8 bytes)",
            line.Split('|')[2].Length == 16, line);
        // Decode the hex back to the eight bytes it claims to carry: the follow-up cards
        // read these bytes out of SQL, so a byte-order slip here is a silent data defect.
        var hex = line.Split('|')[2];
        var decoded = Enumerable.Range(0, 8)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
            .ToArray();
        Check("BST gauge hex decodes to the source bytes in 0x08..0x0F order",
            decoded.SequenceEqual(new byte[] { 100, 132, 168, 2, 7, 5, 3, 0x51 }),
            string.Join(",", decoded));

        // No familiar out: the pet field must be a stable token, not an empty field.
        var noPet = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { PetObjectId = 0, PetName = null, PetDataId = 0 });
        Check("BST pet=none when no familiar is summoned", noPet.Contains("|pet=none|"), noPet);

        // A translated pet name containing the separator must not fabricate a field.
        var nasty = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { PetName = "Cu|Sith,the:Hound" });
        Check("BST pet name is sanitised", !nasty.Contains("Cu|Sith"), nasty);
        Check("BST sanitised line keeps its field count",
            nasty.Split('|').Length == line.Split('|').Length, nasty);

        // Invariant culture: a de-DE comma decimal would destroy a split_part parse.
        Check("BST line carries no comma decimals",
            !System.Text.RegularExpressions.Regex.IsMatch(line.Split('|')[^1].Replace(",", ""), @"\d,\d"),
            line);

        // Budget: a flood of statuses must not blow the 200-char line.
        var manyStatuses = Enumerable.Range(0, 40).Select(i => (ushort)(4595 + i)).ToArray();
        var longLine = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { Statuses = manyStatuses });
        Check("BST line stays within the 200-char budget",
            longLine.Length <= BeastmasterTelemetryFormat.MaxLineLength, $"len={longLine.Length}");
        Check("BST truncated line is marked with ~", longLine.EndsWith('~'), longLine);
        Check("BST truncation keeps all 11 fields", longLine.Split('|').Length == 11, longLine);

        // --- the change gate ---------------------------------------------------------
        var gate = new BeastmasterTelemetryFormat.GateState();
        long t = 1_000_000;

        Check("BST first snapshot emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap));
        t += 1000;
        Check("BST identical snapshot does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap));
        t += 1000;
        Check("BST a changed gauge byte emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap with { ChainCount = 4 }));
        t += 1000;
        Check("BST a changed pet emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap with { ChainCount = 4, PetObjectId = 99 }));
        t += 1000;
        Check("BST a changed Beast Mode action emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900 }));

        // Statuses alone are deliberately NOT part of the key (they flap).
        t += 1000;
        Check("BST a status-only change does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, Statuses = new ushort[] { 4643 } }));

        // --- the rate floor ----------------------------------------------------------
        var rlGate = new BeastmasterTelemetryFormat.GateState();
        long rt = 5_000_000;
        BeastmasterTelemetryFormat.ShouldEmit(ref rlGate, rt, snap with { ChainCount = 0 });
        Check("BST a change inside the rate window is held",
            !BeastmasterTelemetryFormat.ShouldEmit(ref rlGate, rt + 100, snap with { ChainCount = 1 }));
        Check("BST the held change still emits past the window",
            BeastmasterTelemetryFormat.ShouldEmit(ref rlGate, rt + BeastmasterTelemetryFormat.MinIntervalMs, snap with { ChainCount = 1 }),
            "a suppressed change must not be recorded as seen");

        // The adversary: a snapshot that changes on EVERY tick at 100 fps must still be
        // capped at 4 lines/s.
        var advGate = new BeastmasterTelemetryFormat.GateState();
        var advLines = 0;
        for (var i = 0; i < 1000; i++) // 1000 ticks x 10ms = 10 seconds
            if (BeastmasterTelemetryFormat.ShouldEmit(ref advGate, 9_000_000 + i * 10, snap with { ChainCount = (byte)(i % 250) }))
                advLines++;
        Check($"BST worst case is capped at 4 lines/s (10s of per-tick change -> {advLines} lines)",
            advLines <= 41, $"{advLines} lines in 10s");

        // --- REPLAY: real ffxivdb Beastmaster play -----------------------------------
        var tracePath = Path.Combine(AppContext.BaseDirectory, "trace-bst-ffxivdb.csv");
        if (!File.Exists(tracePath))
        {
            Check("BST replay trace is present", false, tracePath);
            return;
        }

        var changeTimes = File.ReadAllLines(tracePath)
            .Where(l => l.Length > 0 && char.IsDigit(l[0]))
            .Select(l => long.Parse(l, CultureInfo.InvariantCulture))
            .OrderBy(x => x)
            .ToArray();

        Check("BST replay trace has real rows", changeTimes.Length > 5000, $"n={changeTimes.Length}");

        // Rebuild ~100 fps ticks across each active window and step the REAL gate.
        const int tickMs = 10;
        const long idleGapMs = 120_000;
        var replayGate = new BeastmasterTelemetryFormat.GateState();
        var lines = 0;
        long ticks = 0;
        long activeMs = 0;
        byte churn = 0;

        var windowStart = 0;
        for (var i = 0; i < changeTimes.Length; i++)
        {
            var isLast = i == changeTimes.Length - 1;
            if (!isLast && changeTimes[i + 1] - changeTimes[i] <= idleGapMs)
                continue;

            var from = changeTimes[windowStart];
            var to = changeTimes[i];
            activeMs += to - from;

            var ci = windowStart;
            for (var now = from; now <= to; now += tickMs)
            {
                ticks++;
                var changed = false;
                while (ci <= i && changeTimes[ci] <= now) { changed = true; ci++; }
                if (changed) churn++;

                // Between changes the snapshot is identical, which is what the gate sees.
                if (BeastmasterTelemetryFormat.ShouldEmit(ref replayGate, now, snap with { ChainCount = churn }))
                    lines++;
            }

            windowStart = i + 1;
        }

        var minutes = activeMs / 60000.0;
        var perMin = lines / minutes;
        Console.WriteLine($"     replay: {changeTimes.Length} real actions, {ticks:N0} ticks, " +
                          $"{minutes:F1} min active -> {lines} lines ({perMin:F2}/min)");

        Check($"BST replay line rate stays modest ({perMin:F2}/min over {minutes:F0} min)",
            perMin < 60, $"{perMin:F2}/min");
        Check("BST replay emitted fewer lines than state changes",
            lines <= changeTimes.Length, $"{lines} lines vs {changeTimes.Length} changes");
        Check("BST replay never exceeded 4 lines/s on average",
            lines <= activeMs / 250 + 1, $"{lines} lines over {activeMs}ms");
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else { _fail++; Console.WriteLine($"FAIL {what}{(detail is null ? "" : $" -> {detail}")}"); }
    }
}
