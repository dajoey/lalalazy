using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// The paste block Joey copies back from <c>/lcraft spike results</c> (card t_933683a5 decision 7). The gate is
/// 5/5 and a failing line must name the STAGE that broke - "3/5" with no per-vendor reason is a failed deliverable,
/// so these tests assert the text itself, not just the arithmetic.
/// </summary>
public static class SpikeReportTests
{
    private const string V = "0.1.6.5";

    private static SpikeResult Pass(int n, string npc = "Bango Zango", string zone = "Limsa Lominsa Lower Decks",
        double secs = 31.4, params string[] notes) =>
        new(n, npc, zone, true, secs, null, null, "tp 4.2s | nav 1.1s | walk 18.0s (final 2.9y, nudged=no) | interact 0.8s | menu=-/-", notes);

    private static SpikeResult Fail(int n, string stage, string why, string npc = "Rianne", string zone = "Ul'dah - Steps of Nald") =>
        new(n, npc, zone, false, 12.5, stage, why, "tp 4.2s | nav 1.1s | walk 7.0s (final 9.4y, nudged=yes) | interact 0.0s | menu=-/-", []);

    private static readonly SpikeResult[] AllFive =
    [
        Pass(1), Pass(2, "Gerulf"), Pass(3, "Rianne", "Ul'dah - Steps of Nald"),
        Pass(4, "Roarich", "Ul'dah - Steps of Nald"), Pass(5, "Maisenta", "New Gridania"),
    ];

    public static IEnumerable<(string Name, Func<bool> Check)> Tests
    {
        get
        {
            yield return ("5/5 renders PASS - gate met", () =>
                SpikeReport.Render(V, AllFive).Contains("RESULT: 5/5 PASS - gate met"));

            // The gate is Joey's, unchanged: 4/5 does not ship. Not "mostly works", not averaged.
            yield return ("4/5 renders FAIL - gate not met", () =>
            {
                var four = AllFive.Take(4).Append(Fail(5, SpikeStage.Pathfind, "the walk ended 9.4y from the NPC")).ToList();
                var text = SpikeReport.Render(V, four);
                return text.Contains("RESULT: 4/5 FAIL - gate not met (needs 5/5)") && !text.Contains("PASS - gate met");
            });

            yield return ("negative control: 5 results with one FAIL is never 'gate met'", () =>
            {
                var text = SpikeReport.Render(V, AllFive.Take(4).Append(Fail(5, SpikeStage.ShopOpen, "no Shop window opened")).ToList());
                return !text.Contains("gate met") || text.Contains("not met");
            });

            yield return ("fewer than 5 results says INCOMPLETE, never PASS", () =>
            {
                var text = SpikeReport.Render(V, AllFive.Take(3).ToList());
                return text.Contains("INCOMPLETE") && text.Contains("2 vendor(s) not run yet") && !text.Contains("gate met");
            });

            // Decision 7: every failure names the stage AND the reason. This is the check that makes the block
            // actionable for the next lane instead of a bare score.
            yield return ("a failing line names the stage and the reason", () =>
            {
                var text = SpikeReport.Render(V, [Fail(3, SpikeStage.ZoneSettle, "the zone change did not settle in 45 s")]);
                return text.Contains("3/5 Rianne (Ul'dah - Steps of Nald): FAIL")
                    && text.Contains("at zone-settle")
                    && text.Contains("the zone change did not settle in 45 s");
            });

            yield return ("every stage constant is unique and non-empty", () =>
                SpikeStage.All.Length == SpikeStage.All.Distinct().Count()
                && SpikeStage.All.All(s => !string.IsNullOrWhiteSpace(s)));

            yield return ("every line carries the per-vendor ordinal, npc and zone", () =>
            {
                var text = SpikeReport.Render(V, AllFive);
                return AllFive.All(r => text.Contains($"{r.N}/5 {r.Npc} ({r.Zone}):"));
            });

            yield return ("timings and notes ride on every line", () =>
            {
                var text = SpikeReport.Render(V, [Pass(1, notes: "vnavmesh stopped 5.1y out; nudged")]);
                return text.Contains("tp 4.2s | nav 1.1s") && text.Contains("notes: vnavmesh stopped 5.1y out; nudged");
            });

            yield return ("a clean pass says notes: none", () =>
                SpikeReport.Render(V, [Pass(1)]).Contains("notes: none"));

            yield return ("the version carrying the runner is in the header", () =>
                SpikeReport.Render(V, AllFive).StartsWith("LazyCrafter P6 walk-to-vendor spike - v0.1.6.5"));

            yield return ("results render in vendor order regardless of completion order", () =>
            {
                var shuffled = new[] { AllFive[3], AllFive[0], AllFive[4], AllFive[2], AllFive[1] };
                var lines = SpikeReport.Render(V, shuffled).Split('\n').Where(l => l.Contains("/5 ") && !l.StartsWith("RESULT")).ToList();
                return lines.Count == 5 && lines[0].StartsWith("1/5") && lines[4].StartsWith("5/5");
            });

            // The whole point of a paste block: one copy action, no chat decoration, and it survives a round trip.
            yield return ("the block is plain text with no chat prefix", () =>
            {
                var text = SpikeReport.Render(V, AllFive);
                return !text.Contains("[LazyCrafter") && !text.Contains('\r') && text.Split('\n').Length == 1 + 10 + 1;
            });
        }
    }
}
