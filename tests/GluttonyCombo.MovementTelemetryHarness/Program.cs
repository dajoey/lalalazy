using System.Globalization;
using GluttonyCombo.Data;

namespace GluttonyCombo.MovementTelemetryHarness;

/// <summary>
///     Offline assertions on the fork's movement tap (2026-11-05).
///     Compiles the real <see cref="MovementFormat"/> - no Dalamud, no game - so the M|/H|/MP|
///     shapes the relay ingest and the ffxivdb tables depend on are proven before shipping.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        // ---- M| tick line --------------------------------------------------------------
        var tick = MovementFormat.BuildTickLine(
            1_788_672_000_100, pullId: 3, zoneId: 822, slot: 0, jobId: 24,
            x: 10.25f, y: -3.5f, z: 107.87f, rot: 2.3456f,
            flags: MovementFormat.FlagInCombat | MovementFormat.FlagMoving | MovementFormat.FlagKeyHeld,
            markerId: 0);
        Check("tick prefix", tick.StartsWith("M|", StringComparison.Ordinal), tick);
        Check("tick exact shape",
            tick == "M|1788672000100|3|822|0|24|10.25|-3.50|107.87|2.346|7|0", tick);
        Check("tick has 12 fields", tick.Split('|').Length == 12, tick.Split('|').Length.ToString());

        var party = MovementFormat.BuildTickLine(
            1_788_672_000_100, 3, 822, 2, 19, 0f, 0f, 0f, 0f, MovementFormat.FlagInCombat, 541);
        Check("party marker id rides the last field", party.EndsWith("|541", StringComparison.Ordinal), party);

        // ---- H| hazard line ------------------------------------------------------------
        var hazard = MovementFormat.BuildHazardLine(
            1_788_672_000_300, 3, 822, sourceId: 0x400237A0, castActionId: 0x9084,
            remainS: 2.37f, totalS: 5.0f, castTargetId: 0x10037862,
            x: 426.8f, y: -385.64f, z: 79.17f, rot: 2.65f);
        Check("hazard exact shape",
            hazard == "H|1788672000300|3|822|1073887136|36996|2.37|5.00|268662882|426.80|-385.64|79.17|2.650",
            hazard);
        Check("hazard has 13 fields", hazard.Split('|').Length == 13, hazard.Split('|').Length.ToString());

        // ---- hazard change gate --------------------------------------------------------
        var steps = new Dictionary<(ulong, uint), int>();
        Check("first hazard emits", MovementFormat.HazardShouldEmit(steps, 7, 100, 5.0f));
        Check("same step does not re-emit", !MovementFormat.HazardShouldEmit(steps, 7, 100, 5.2f));
        Check("half-second step re-emits", MovementFormat.HazardShouldEmit(steps, 7, 100, 4.4f));
        Check("other action emits", MovementFormat.HazardShouldEmit(steps, 7, 200, 5.0f));
        Check("other source emits", MovementFormat.HazardShouldEmit(steps, 8, 100, 5.0f));
        Check("expired cast emits its final step", MovementFormat.HazardShouldEmit(steps, 7, 100, 0.0f));

        // ---- F| forecast line ----------------------------------------------------------
        var forecast = MovementFormat.BuildForecastLine(1_788_672_002_000, 3, 822, "act36996in4.2s");
        Check("forecast exact shape", forecast == "F|1788672002000|3|822|act36996in4.2s", forecast);
        Check("forecast empty token renders none",
            MovementFormat.BuildForecastLine(1, 0, 0, null).EndsWith("|none", StringComparison.Ordinal) &&
            MovementFormat.BuildForecastLine(1, 0, 0, "-").EndsWith("|none", StringComparison.Ordinal));
        Check("forecast token sanitized and capped",
            MovementFormat.BuildForecastLine(1, 0, 0, new string('x', 60))
                .EndsWith("|" + new string('x', 24), StringComparison.Ordinal));

        // ---- MP| decision line ---------------------------------------------------------
        var opts = new List<MovementFormat.MoveOption>
        {
            new(0, 10.2f, -3.5f, 0f, 0f, 0f, 2.1f, BmrChecked: false),
            new(1, 3.4f, 1.0f, 0f, 1.17f, 0.5f, 8.4f, BmrChecked: true),
            new(2, 17.1f, -7.9f, 0f, 1.17f, 0.5f, 3.2f, BmrChecked: false),
        };
        var decision = MovementFormat.BuildDecisionLine(
            1_788_672_001_000, 3, 822, castActionId: 36996, hitInS: 4.2f,
            opts, codePick: 1, jevPick: 2, codeWhy: "checked-spot", jevWhy: "ok");
        Check("decision prefix", decision.StartsWith("MP|", StringComparison.Ordinal), decision);
        Check("decision exact shape",
            decision == "MP|1788672001000|3|822|36996|4.20|0:10.2,-3.5,0.0,0.00,0.0,2.1,0;1:3.4,1.0,0.0,1.17,0.5,8.4,1;2:17.1,-7.9,0.0,1.17,0.5,3.2,0|code=1|jev=2|checked-spot|ok",
            decision);
        Check("jev dash when unanswered",
            MovementFormat.BuildDecisionLine(1, 0, 0, 0, 1f, [opts[0]], 0, -1, "stay", "off")
                .EndsWith("|code=0|jev=-|stay|off", StringComparison.Ordinal));

        // Truncation: wide options never blow the cap, stay (option 0) is always written.
        var wide = new List<MovementFormat.MoveOption> { opts[0] };
        for (var i = 1; i < 6; i++)
            wide.Add(new MovementFormat.MoveOption(i, 1e9f, -1e9f, 0f, 1.17f, 0.5f, 1e9f, BmrChecked: true));
        var big = MovementFormat.BuildDecisionLine(1, 0, 0, 0, 1f, wide, 0, -1, "x", "y");
        Check("decision line capped", big.Length <= MovementFormat.MaxDecisionLength, big.Length.ToString());
        Check("stay survives truncation", big.Contains("0:10.2", StringComparison.Ordinal), big);
        Check("truncation marker present", big.EndsWith("~|code=0|jev=-|x|y", StringComparison.Ordinal), big);

        // Tokens never grow fields.
        Check("sanitize strips pipes",
            MovementFormat.SanitizeToken("a|b|c") == "a_b_c", MovementFormat.SanitizeToken("a|b|c"));
        Check("sanitize caps length",
            MovementFormat.SanitizeToken(new string('x', 40)).Length == 24);
        Check("sanitize empty is none", MovementFormat.SanitizeToken("") == "none");

        // Culture must not be able to turn a decimal point into a comma and break the parser:
        // under de-DE every line must come out byte-identical to the invariant one.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var germanTick = MovementFormat.BuildTickLine(1, 0, 0, 0, 0, 1.5f, 2.5f, 3.5f, 0.5f, 0, 0);
            Check("tick line culture-proof",
                germanTick == MovementFormat.BuildTickLine(1, 0, 0, 0, 0, 1.5f, 2.5f, 3.5f, 0.5f, 0, 0), germanTick);
            var germanDecision = MovementFormat.BuildDecisionLine(1, 0, 0, 0, 1f, opts, 0, -1, "a", "b");
            Check("decision line culture-proof",
                germanDecision == MovementFormat.BuildDecisionLine(1, 0, 0, 0, 1f, opts, 0, -1, "a", "b"),
                germanDecision);
            Check("no decimal commas in the decision line",
                !germanDecision.Contains("3,4", StringComparison.Ordinal), germanDecision);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // ---- relay URL resolve policy (2026-11-06 fix: the movement.install crash) ------
        // The tap resolves its relay base while installing. Before the fix the shipped code
        // read the config un-guarded (GluttonyCombo.Telemetry.cs:61): a not-yet-loaded config
        // crashed the install with a NullReferenceException (Dalamud ER| movement.install,
        // 1.0.4.332 on omasky, 2026-11-05 19:01 ET) and a blank setting switched uploads off.
        Check("null config falls back to the shared relay",
            MovementFormat.ResolveRelayBaseUrl(null) == MovementFormat.DefaultRelayBaseUrl,
            MovementFormat.ResolveRelayBaseUrl(null));
        Check("blank config falls back to the shared relay",
            MovementFormat.ResolveRelayBaseUrl("") == MovementFormat.DefaultRelayBaseUrl &&
            MovementFormat.ResolveRelayBaseUrl("   ") == MovementFormat.DefaultRelayBaseUrl,
            MovementFormat.ResolveRelayBaseUrl("   "));
        Check("configured relay is used trimmed",
            MovementFormat.ResolveRelayBaseUrl("  http://192.168.10.7:9999/  ") == "http://192.168.10.7:9999",
            MovementFormat.ResolveRelayBaseUrl("  http://192.168.10.7:9999/  "));
        Check("shared relay default is the LAN ingest host",
            MovementFormat.DefaultRelayBaseUrl == "http://192.168.10.44:8797",
            MovementFormat.DefaultRelayBaseUrl);

        Console.WriteLine($"movement format harness: {_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS {name}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL {name}{(detail is null ? "" : $": {detail}")}");
        }
    }
}
