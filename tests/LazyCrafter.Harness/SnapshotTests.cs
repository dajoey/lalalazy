using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// The run snapshot the Run tab and <c>/lcraft status</c> render (card t_efde145c, contract v1 with t_c360953f):
/// the report text must name every blocked item with its quantity, the estimated gil for market items, the vendor
/// location when known, and the "press Resume" line - for a Blocked run, from the immutable record alone.
/// </summary>
internal static class SnapshotTests
{
    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("a Blocked report names every blocked item, qty, est. gil and the Resume line", () =>
        {
            var snap = new RunSnapshot(
                RunState.Blocked, "Blocked", "Blocked", "blocked: waiting on you", "cart",
                ["Alpine Chandelier"], new DateTime(2026, 9, 5, 16, 19, 24), new DateTime(2026, 9, 5, 16, 36, 6),
                TimeSpan.FromMinutes(16).Add(TimeSpan.FromSeconds(42)), 2,
                new[]
                {
                    new RunStep(StepKind.Craft, 300, "Titanium Ingot", 3, StepState.Blocked, "needs market Titanium Ore x15", null, 30),
                    new RunStep(StepKind.Craft, 100, "Hardsilver Ingot", 1, StepState.Blocked, "needs market Hardsilver Ore x4", null, 10),
                    new RunStep(StepKind.Craft, 800, "Alpine Chandelier", 1, StepState.Blocked, "needs craft #300 x3", null, 80),
                },
                new[]
                {
                    new BlockedItem(StepKind.Market, 200, "Titanium Ore", 15, 187_500, null),
                    new BlockedItem(StepKind.Market, 201, "Hardsilver Ore", 4, 21_200, null),
                    new BlockedItem(StepKind.Vendor, 2015, "Tallow Candle", 7, null, "Merchant (Limsa Lominsa 9.8, 11.2)"),
                },
                "4 crafts still blocked", true);
            var r = snap.Report();
            return r.Contains("blocked")
                && r.Contains("Titanium Ore x15") && r.Contains("187,500")
                && r.Contains("Hardsilver Ore x4") && r.Contains("21,200")
                && r.Contains("Tallow Candle x7") && r.Contains("Limsa Lominsa")
                && r.Contains("press Resume")
                && r.Split('\n').Count(l => l.StartsWith("  [-] craft ")) == 3;
        }),

        ("a Running report shows the running row's external status and the elapsed time", () =>
        {
            var snap = new RunSnapshot(
                RunState.Running, "WaitGather", "Gathering", "GBR: gathering", "cart", ["Alpine Chandelier"],
                new DateTime(2026, 9, 5, 16, 19, 24), null, TimeSpan.FromMinutes(7).Add(TimeSpan.FromSeconds(12)), 1,
                new[] { new RunStep(StepKind.Gather, 200, "Titanium Ore", 15, StepState.Running, null, "Gathering Titanium Ore (2/4 left)", 0) },
                Array.Empty<BlockedItem>(), null, false);
            var r = snap.Report();
            return r.Contains("[>] gather Titanium Ore x15 - Gathering Titanium Ore (2/4 left)")
                && r.Contains("7:12 elapsed");
        }),

        ("Empty reports idle and Copy stays useful", () =>
        {
            var r = RunSnapshot.Empty.Report();
            return r.Contains("idle") && !r.Contains("press Resume") && RunSnapshot.Empty.CanResume == false;
        }),

        ("FormatElapsed: m:ss under an hour, h:mm:ss over", () =>
            RunSnapshot.FormatElapsed(TimeSpan.FromSeconds(432)) == "7:12"
            && RunSnapshot.FormatElapsed(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(2))) == "1:00:02"),

        ("KindLabel / state marks are stable so the Probe and the UI agree on text", () =>
            RunSnapshot.KindLabel(StepKind.Retrieve) == "retrieve"
            && RunSnapshot.KindLabel(StepKind.Craft) == "craft"
            && RunSnapshot.KindLabel(StepKind.Market) == "market"),
    };
}
