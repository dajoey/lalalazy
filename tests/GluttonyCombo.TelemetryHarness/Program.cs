using System.Globalization;
using GluttonyCombo.Data;
using Buff = GluttonyCombo.Data.ComboTelemetryFormat.Buff;

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

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else { _fail++; Console.WriteLine($"FAIL {what}{(detail is null ? "" : $" -> {detail}")}"); }
    }
}
