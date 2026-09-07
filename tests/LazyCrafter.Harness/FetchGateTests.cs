using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// The 0.1.6.13 fetch-gate decisions (card t_3161fa75), pinned in Core because the dispatcher itself cannot be
/// compiled offline: a thrown preflight HOLDS (never a green light), a Lifestream trip under way HOLDS, both
/// holds are bounded by the 3-minute cap with the real reason, a batch session jammed at zero bag movement is
/// caught by the 2-minute stall limit, and the fetch hold ignores what a working session opens while holding
/// on the market board. The refusal wordings are asserted as rendered strings - they are the contract.
/// </summary>
internal static class FetchGateTests
{
    private const string Threw = "could not inspect Artisan's retainer state (TargetInvocationException)";
    private const string BellMiss = "you are not standing next to a summoning bell - walk to one and press Dispatch again";
    private const string OtherDeadEnd = "Artisan is already running a retainer task; let it finish and press Dispatch again";

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("fetchgate: the hold cap is exactly three minutes", () =>
            FetchGatePolicy.HoldCap == TimeSpan.FromMinutes(3)),

        ("fetchgate: the batch stall limit is exactly two minutes", () =>
            FetchGatePolicy.BatchStallLimit == TimeSpan.FromMinutes(2)),

        ("fetchgate: the thrown-preflight prefix is the string RetainerFetch returns", () =>
            FetchGatePolicy.PreflightThrewPrefix == "could not inspect"
            && FetchGatePolicy.PreflightThrew(Threw)
            && FetchGatePolicy.PreflightThrew("could not inspect Artisan's retainer state (NullReferenceException)")
            && !FetchGatePolicy.PreflightThrew(BellMiss)),

        ("fetchgate: a clean preflight with Lifestream idle queues", () =>
            FetchGatePolicy.Decide(null, lifestreamBusy: false, TimeSpan.Zero, walkPossible: true)
                == FetchGatePolicy.FetchVerdict.Queue),

        ("fetchgate: a Lifestream trip under way HOLDS even when the preflight would pass", () =>
            FetchGatePolicy.Decide(null, lifestreamBusy: true, TimeSpan.Zero, walkPossible: true) == FetchGatePolicy.FetchVerdict.Hold
            && FetchGatePolicy.Decide(null, lifestreamBusy: true, TimeSpan.Zero, walkPossible: false) == FetchGatePolicy.FetchVerdict.Hold),

        ("fetchgate: a thrown preflight HOLDS - it must never read as a green light", () =>
            FetchGatePolicy.Decide(Threw, lifestreamBusy: false, TimeSpan.Zero, walkPossible: true) == FetchGatePolicy.FetchVerdict.Hold
            && FetchGatePolicy.Decide(Threw, lifestreamBusy: false, TimeSpan.Zero, walkPossible: false) == FetchGatePolicy.FetchVerdict.Hold),

        ("fetchgate: a thrown preflight past the cap refuses with the real reason", () =>
        {
            var v = FetchGatePolicy.Decide(Threw, lifestreamBusy: false, FetchGatePolicy.HoldCap, walkPossible: true);
            var line = FetchGatePolicy.GaveUp(Threw, FetchGatePolicy.HoldCap);
            return v == FetchGatePolicy.FetchVerdict.RefuseCap
                && line.Contains("could not inspect Artisan's retainer state")
                && line.Contains("after 3 minutes");
        }),

        ("fetchgate: a bell miss holds with the walk on and refuses with it off", () =>
            FetchGatePolicy.Decide(BellMiss, lifestreamBusy: false, TimeSpan.Zero, walkPossible: true) == FetchGatePolicy.FetchVerdict.Hold
            && FetchGatePolicy.Decide(BellMiss, lifestreamBusy: false, TimeSpan.Zero, walkPossible: false) == FetchGatePolicy.FetchVerdict.RefuseNoWalk),

        ("fetchgate: the bell-miss cap refusal keeps the shipped 0.1.6.12 wording", () =>
            FetchGatePolicy.GaveUp(BellMiss, FetchGatePolicy.HoldCap)
                == "gave up waiting for a reachable summoning bell after 3 minutes (you are not standing next to a summoning bell - walk to one and press Dispatch again)."),

        ("fetchgate: a Lifestream trip past the cap refuses with its own truthful line", () =>
            FetchGatePolicy.Decide(null, lifestreamBusy: true, FetchGatePolicy.HoldCap, walkPossible: true) == FetchGatePolicy.FetchVerdict.RefuseCap
            && FetchGatePolicy.GaveUpOnTrip(FetchGatePolicy.HoldCap).Contains("summoning-bell walk")
            && FetchGatePolicy.GaveUpOnTrip(FetchGatePolicy.HoldCap).Contains("after 3 minutes")),

        ("fetchgate: dead ends that are not the bell and not a throw still proceed to the queue call", () =>
            FetchGatePolicy.Decide(OtherDeadEnd, lifestreamBusy: false, TimeSpan.Zero, walkPossible: false)
                == FetchGatePolicy.FetchVerdict.Queue),

        // ------------------------------------------------------------ 0.1.6.15: the walk's own sentences

        ("fetchgate15: the walk names the inn room's bell, never the market board (Helm t-joey-1788808881825)", () =>
            FetchGatePolicy.TripStatus() == "walking to the summoning bell in the inn room"
            && FetchGatePolicy.TripHeartbeat() == "walking to the summoning bell in the inn room so the retainer fetch can run"
            && !FetchGatePolicy.TripStatus().Contains("market board")
            && !FetchGatePolicy.TripHeartbeat().Contains("market board")),

        ("fetchgate15: the walk-passed board has its own wording, distinct from the player-window close-it line", () =>
            FetchGatePolicy.BoardGateStatus() == "waiting - the trip to the bell goes through the market board plaza; close the market board to continue"
            && FetchGatePolicy.BoardGateLine() == "waiting - the bell trip passes the market board plaza; close the market board to continue"
            && FetchGatePolicy.BoardGateStatus() != ClientWaitPolicy.WaitLine("the market board")),

        ("fetchgate15: the player-opened board keeps the plain 0.1.6.13 wording, and the wrong-NPC status names the auto-close", () =>
            FetchGatePolicy.BoardHeldStatus(TimeSpan.FromSeconds(90)) == "waiting - the market board (1:30)"
            && FetchGatePolicy.WrongBoardStatus() == "waiting out a market board the run did not plan to open - closing it and carrying on"
            && FetchGatePolicy.WrongBoardStatus().Contains("closing it and carrying on")),

        ("fetchgate: the fetch hold ignores what a working session opens, and holds on the market board", () =>
            FetchGatePolicy.FetchHoldIgnoredLabels.Contains("a retainer's inventory")
            && FetchGatePolicy.FetchHoldIgnoredLabels.Contains("the summoning bell")
            && FetchGatePolicy.FetchHoldIgnoredLabels.Contains("a zone change")
            && FetchGatePolicy.FetchHoldIgnoredLabels.Contains("a quantity input prompt")
            && !FetchGatePolicy.ShouldHoldFetch("a retainer's inventory")
            && !FetchGatePolicy.ShouldHoldFetch("the summoning bell")
            && !FetchGatePolicy.ShouldHoldFetch("a zone change")
            && FetchGatePolicy.ShouldHoldFetch("the market board")
            && FetchGatePolicy.ShouldHoldFetch("a shop window")
            && FetchGatePolicy.ShouldHoldFetch(CraftDiagnosis.UnknownWindow)
            && !FetchGatePolicy.ShouldHoldFetch(null)),

        ("fetchgate: the ignored list never covers a window the player must close", () =>
            !FetchGatePolicy.FetchHoldIgnoredLabels.Contains("the market board")
            && !FetchGatePolicy.FetchHoldIgnoredLabels.Contains("the market board purchase window")
            && !FetchGatePolicy.FetchHoldIgnoredLabels.Contains("a shop window")
            && !FetchGatePolicy.FetchHoldIgnoredLabels.Contains("a trade window")
            && FetchGatePolicy.FetchHoldIgnoredLabels.Distinct().Count() == FetchGatePolicy.FetchHoldIgnoredLabels.Length),

        ("fetchgate: the fetch-hold timeout names the window, the cap and Resume", () =>
        {
            var r = FetchGatePolicy.FetchTimeoutReason("the market board", ClientWaitPolicy.WaitCap);
            var line = FetchGatePolicy.FetchTimeoutLine("the market board", ClientWaitPolicy.WaitCap);
            return r.Contains("the market board") && r.Contains("blocked the retainer fetch for 5 minutes")
                && r.Contains("press Resume") && line.StartsWith("stopped - ") && line.EndsWith(".");
        }),

        ("fetchgate: the batch stall guard fires only after two minutes of an unchanged all-zero signal", () =>
        {
            var g = new StallGuard(FetchGatePolicy.BatchStallLimit);
            var t = new DateTime(2026, 9, 7, 13, 0, 0, DateTimeKind.Utc);
            var jam = "200:0|201:0";   // zero bag delta on every demanded ingredient
            if (g.Observe(jam, t)) return false;                              // first observation: clock starts
            if (g.Observe(jam, t.AddSeconds(119))) return false;              // 1:59 - not yet
            if (!g.Observe(jam, t.AddSeconds(120))) return false;             // 2:00 - jammed
            if (!g.Observe(jam, t.AddSeconds(300))) return false;             // still jammed
            if (g.Observe("200:5|201:0", t.AddSeconds(301))) return false;    // a withdrawal lands: reset
            if (g.Observe("200:5|201:0", t.AddSeconds(360))) return false;    // 59 s of movement-free... still inside
            return g.Observe("200:5|201:0", t.AddSeconds(422));               // 121 s after the last movement
        }),
    };
}
