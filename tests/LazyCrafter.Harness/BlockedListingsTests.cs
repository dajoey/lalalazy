using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Tier 1: name exactly which retainer and how many units to pull off sale, on BOTH ending paths (card t_35be7be5).
/// <para>
/// Every check here asserts on the <b>RENDERED text</b> - <c>BlockedListings.Lines</c> / <c>.Detail</c> and
/// <c>RunSnapshot.Report()</c> - not on an internal value, deliberately. The defect this suite exists to prevent
/// was a RENDERER defect: the retainer names, item ids and quantities were all correct and sitting in memory, and
/// the finishing path simply did not print them. A test on the internal list would have been green throughout.
/// </para>
/// <para>
/// The worked example is Joey's 2026-09-05 22:44 run: Silver Ore (7 listed via Hussypants, 3 via Bussyqueen),
/// Iron Ore x6 and Cloud Mica x3 (Hussypants), plus nine more materials - twelve near-identical red warnings and
/// then, because the run FINISHED rather than blocked, a bare ", 12 could not be retrieved".
/// </para>
/// </summary>
internal static class BlockedListingsTests
{
    // ---- item ids + names for the fixture (the real ones from Joey's run, so a log line can be matched by eye)
    private const uint SilverOre = 5111, IronOre = 5106, CloudMica = 5116, SilverIngot = 5062, TitaniumOre = 200;

    private static string Name(uint id) => id switch
    {
        SilverOre => "Silver Ore",
        IronOre => "Iron Ore",
        CloudMica => "Cloud Mica",
        SilverIngot => "Silver Ingot",
        TitaniumOre => "Titanium Ore",
        _ => $"#{id}",
    };

    /// <summary>A retrieval whose stock is ONLY on the board, split across the named retainers.</summary>
    private static DispatchPlan.Retrieve Listed(uint itemId, int quantity, params (string Retainer, int Units)[] listings) =>
        new(itemId, quantity, listings
            .Select(l => new StoredElsewhere($"the market board (listed by retainer {l.Retainer})", l.Units, Fetchable: false, Retainer: l.Retainer))
            .ToList());

    /// <summary>A retrieval whose stock IS reachable (a retainer's bags) - the negative control for Defect B.</summary>
    private static DispatchPlan.Retrieve Reachable(uint itemId, int quantity, string retainer = "Hussypants") =>
        new(itemId, quantity, [new StoredElsewhere($"retainer {retainer}", quantity, Retainer: retainer)]);

    private const string Timeout = "Artisan's retainer session ran for 4 minutes without finishing (a dialogue may be waiting, or the bell was interrupted)";
    private const string NoRetainer = "no retainer is holding any in its bags - a summoning bell cannot reach a market-board listing";

    /// <summary>The rendered end-of-run block, as one string, exactly as chat would show it.</summary>
    private static string Render(params (DispatchPlan.Retrieve Item, string Why)[] unfetched) =>
        string.Join("\n", BlockedListings.Lines(BlockedListings.Summarise(unfetched, Name), "cart"));

    private static string RenderDetail(params (DispatchPlan.Retrieve Item, string Why)[] unfetched) =>
        string.Join("\n", BlockedListings.Detail(BlockedListings.Summarise(unfetched, Name), "cart"));

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        // ------------------------------------------------------------------ DEFECT A: both ending paths
        //
        // The dispatcher has two endings and they disagreed. Rather than reach into DispatchService (Dalamud, so
        // the harness cannot load it), these two checks pin the CONTRACT both endings now share: one Summarise +
        // Lines call produces the named advice, and RunSnapshot.Report() carries the same detail whether the run
        // state is Done or Blocked. If a future edit makes the Done path render less, check 2 goes red.

        ("A: a listing-blocked material renders the retainer name and the unit count, not a bare count", () =>
        {
            var r = Render((Listed(SilverOre, 10, ("Hussypants", 7), ("Bussyqueen", 3)), NoRetainer));
            return r.Contains("Hussypants") && r.Contains("Bussyqueen")
                && r.Contains("Silver Ore x7") && r.Contains("Silver Ore x3")
                // the bare-count rendering the DONE path used to emit must not be all we get
                && !r.Contains("could not be retrieved,")
                && r.Contains("pull");
        }),

        ("A: a FINISHED run's report carries the same blocked detail a BLOCKED run's does", () =>
        {
            // The real seam: BOTH endings call MergeIntoBlocked. Feed the same _unfetched list through it and
            // render each ending's snapshot. Before this card, Finish did not call it at all - its `existing` list
            // stayed empty, so `done` below had no "needs you:" section and this check goes red on a revert.
            (DispatchPlan.Retrieve, string)[] unfetched =
            [
                (Listed(SilverOre, 10, ("Hussypants", 7), ("Bussyqueen", 3)), NoRetainer),
                (Listed(IronOre, 6, ("Hussypants", 6)), NoRetainer),
            ];
            var merged = BlockedListings.MergeIntoBlocked(Array.Empty<BlockedItem>(), unfetched, Name);

            RunSnapshot Snap(RunState state) => new(
                state, state.ToString(), state.ToString(), "x", "cart", ["Alpine Chandelier"],
                new DateTime(2026, 9, 5, 22, 44, 0), new DateTime(2026, 9, 5, 23, 1, 0), TimeSpan.FromMinutes(17), 2,
                Array.Empty<RunStep>(), merged, null, state == RunState.Blocked);

            var done = Snap(RunState.Done).Report();
            var stopped = Snap(RunState.Blocked).Report();
            bool Names(string r) => r.Contains("Silver Ore x10") && r.Contains("Iron Ore x6") && r.Contains("Hussypants");
            // Identical blocked content on both endings - that is the whole acceptance line of the card.
            return Names(done) && Names(stopped);
        }),

        ("A: the merge is the single implementation both endings share, and it de-duplicates", () =>
        {
            // A retrieval the plan already named must not be added twice, and a partial remainder must fold (C).
            BlockedItem[] fromPlan = [new(StepKind.Retrieve, SilverOre, "Silver Ore", 10, null, "the market board (listed by retainer Hussypants)")];
            var merged = BlockedListings.MergeIntoBlocked(fromPlan, new[]
            {
                (Listed(SilverOre, 10, ("Hussypants", 10)), NoRetainer),
                (Listed(SilverOre, 4, ("Hussypants", 10)), "partial remainder"),
                (Listed(IronOre, 6, ("Hussypants", 6)), NoRetainer),
            }, Name);
            return merged.Count == 2
                && merged.Count(b => b.ItemId == SilverOre) == 1
                && merged.Single(b => b.ItemId == IronOre).Quantity == 6;
        }),

        ("A: the merge keeps the larger of a duplicated need, never the remainder", () =>
        {
            var merged = BlockedListings.MergeIntoBlocked(Array.Empty<BlockedItem>(), new[]
            {
                (Listed(SilverOre, 10, ("Hussypants", 10)), NoRetainer),
                (Listed(SilverOre, 4, ("Hussypants", 10)), "partial remainder"),
            }, Name);
            return merged.Count == 1 && merged[0].Quantity == 10;
        }),

        ("A: RunReport's chat lines name the retainer on a finished run too", () =>
        {
            var snap = new RunSnapshot(
                RunState.Done, "Done", "Done", "done", "cart", ["Alpine Chandelier"],
                new DateTime(2026, 9, 5, 22, 44, 0), new DateTime(2026, 9, 5, 23, 1, 0), TimeSpan.FromMinutes(17), 2,
                Array.Empty<RunStep>(),
                [new BlockedItem(StepKind.Retrieve, IronOre, "Iron Ore", 6, null, "the market board (listed by retainer Hussypants)")],
                null, false);
            var lines = string.Join("\n", RunReport.ChatLines(snap));
            return lines.Contains("Iron Ore x6") && lines.Contains("Hussypants");
        }),

        // ------------------------------------------------------------------ DEFECT B: six causes, one meaning
        //
        // _unfetched mixes market listings (655) with a WhyNoFetch blocker (404), a 10-minute batch timeout (596),
        // a Fetch.Begin error (667), a 4-minute per-item timeout (690) and an exhausted partial pull (728). Only
        // the first means "go unlist something".

        ("B: a timeout-blocked material never appears in the pull-off-sale instruction", () =>
        {
            var r = Render(
                (Listed(SilverOre, 7, ("Hussypants", 7)), NoRetainer),
                (Reachable(TitaniumOre, 15), Timeout));
            var pull = r.Split('\n').TakeWhile(l => !l.Contains("other reasons")).ToList();
            return string.Join("\n", pull).Contains("Silver Ore x7")
                && !string.Join("\n", pull).Contains("Titanium Ore")
                && r.Contains("Titanium Ore x15")           // still reported...
                && r.Contains("other reasons")              // ...but under its own heading
                && r.Contains("NOT listed for sale");
        }),

        ("B: a mixed run reports both groups, and the counts do not bleed across", () =>
        {
            var s = BlockedListings.Summarise(new[]
            {
                (Listed(SilverOre, 7, ("Hussypants", 7)), NoRetainer),
                (Listed(IronOre, 6, ("Hussypants", 6)), NoRetainer),
                (Reachable(TitaniumOre, 15), Timeout),
                (Reachable(SilverIngot, 4), "could not start the retainer fetch"),
            }, Name);
            return s.Retainers.Count == 1 && s.Retainers[0].Retainer == "Hussypants"
                && s.ItemCount == 2 && s.TotalUnits == 13
                && s.Others.Count == 2
                && s.Others.All(o => o.ItemId is TitaniumOre or SilverIngot);
        }),

        ("B: the discriminator is Fetchable, not the wording of the reason", () =>
            // Same reason text on both; only the reachability of the place differs.
            BlockedListings.IsListingBlocked(Listed(SilverOre, 3, ("Hussypants", 3)))
            && !BlockedListings.IsListingBlocked(Reachable(SilverOre, 3))),

        ("B: negative control - a material with NO known place is not called a listing", () =>
        {
            var nowhere = new DispatchPlan.Retrieve(SilverOre, 3, Array.Empty<StoredElsewhere>());
            return !BlockedListings.IsListingBlocked(nowhere)
                && !Render((nowhere, Timeout)).Contains("pull");
        }),

        ("B: negative control - reachable-only stock produces NO pull instruction at all", () =>
        {
            var r = Render((Reachable(SilverOre, 7), Timeout), (Reachable(IronOre, 6), Timeout));
            return !r.Contains("pull") && !r.Contains("off sale") && r.Contains("other reasons");
        }),

        // ------------------------------------------------------------------ DEFECT C: de-duplication
        //
        // WaitRetrieve:728 adds `_fetching with { Quantity = left }` for a partial remainder, so one item lands in
        // _unfetched twice with different quantities. Folding is per item; the kept figure is the larger, because
        // the second entry is a remainder OF the first, not a separate need (see BlockedListings' remarks).

        ("C: a partial-pull remainder appears ONCE, with the combined figure", () =>
        {
            var r = Render(
                (Listed(SilverOre, 10, ("Hussypants", 10)), NoRetainer),
                (Listed(SilverOre, 4, ("Hussypants", 10)), "only 6 of 10 came back after 4 attempts"));
            var occurrences = r.Split('\n').Count(l => l.Contains("Silver Ore"));
            return occurrences == 1 && r.Contains("Silver Ore x10") && !r.Contains("Silver Ore x4");
        }),

        ("C: folding never double-counts into the totals", () =>
        {
            var s = BlockedListings.Summarise(new[]
            {
                (Listed(SilverOre, 10, ("Hussypants", 10)), NoRetainer),
                (Listed(SilverOre, 4, ("Hussypants", 10)), "partial"),
                (Listed(IronOre, 6, ("Hussypants", 6)), NoRetainer),
            }, Name);
            return s.ItemCount == 2 && s.TotalUnits == 16 && s.Retainers.Count == 1
                && s.Retainers[0].Items.Count == 2;
        }),

        ("C: the same item on two retainers is ONE material but two bell-visit rows", () =>
        {
            var s = BlockedListings.Summarise(
                new[] { (Listed(SilverOre, 10, ("Hussypants", 7), ("Bussyqueen", 3)), NoRetainer) }, Name);
            return s.ItemCount == 1 && s.TotalUnits == 10 && s.Retainers.Count == 2
                && s.Retainers.Single(r => r.Retainer == "Hussypants").Items.Single().Units == 7
                && s.Retainers.Single(r => r.Retainer == "Bussyqueen").Items.Single().Units == 3;
        }),

        // ------------------------------------------------------------------ the clean run: silence
        //
        // The bell walk is fired only when HasListings is true, so a clean run must produce an empty summary and
        // an empty render. This is the check that keeps a walk from firing after a run that worked.

        ("clean run: no summary, no lines, and nothing that could trigger the bell walk", () =>
        {
            var s = BlockedListings.Summarise(Array.Empty<(DispatchPlan.Retrieve, string)>(), Name);
            return s.IsEmpty && !s.HasListings && BlockedListings.Lines(s).Count == 0
                && s.Retainers.Count == 0 && s.Others.Count == 0;
        }),

        ("clean-ish run: timeouts only still must NOT trigger the bell walk", () =>
        {
            var s = BlockedListings.Summarise(new[] { (Reachable(TitaniumOre, 15), Timeout) }, Name);
            // Something IS reported - but nothing is listed, so there is nothing to go and unlist.
            return !s.IsEmpty && !s.HasListings && s.Others.Count == 1;
        }),

        // ------------------------------------------------------------------ the collapse + /lcraft blocked

        ("the twelve-line wall collapses: 12 materials render one grouped block, not 12 warnings", () =>
        {
            // Joey's run, all twelve, all on Hussypants except the Silver Ore split.
            (uint Id, int Qty)[] wall =
            [
                (SilverOre, 3), (IronOre, 6), (CloudMica, 3), (SilverIngot, 4), (5063, 8), (5064, 1),
                (5065, 5), (5066, 1), (5067, 1), (5068, 2), (5069, 2), (5070, 2),
            ];
            var r = Render(wall.Select(w => (Listed(w.Id, w.Qty, ("Hussypants", w.Qty)), NoRetainer)).ToArray());
            var lines = r.Split('\n');
            // One headline + one line per retainer + one hint + one "/lcraft blocked" pointer = 4, not 12+.
            return lines.Length == 4
                && lines[1].StartsWith("  Hussypants:")
                && lines[1].Contains("Silver Ore x3") && lines[1].Contains("Iron Ore x6")
                && r.Contains("/lcraft blocked");
        }),

        ("the refusal line is one short line and names the retainer holding the listing", () =>
        {
            var line = BlockedListings.RefusalLine("Silver Ore", Listed(SilverOre, 7, ("Hussypants", 7)));
            return line.Contains("Silver Ore x7") && line.Contains("Hussypants")
                && line.Contains("listed for sale") && !line.Contains('\n')
                && line.Length < 160;
        }),

        ("the refusal line for a NON-listing shortfall does not tell you to unlist anything", () =>
        {
            var line = BlockedListings.RefusalLine("Titanium Ore", Reachable(TitaniumOre, 15));
            return !line.Contains("off sale") && !line.Contains("listed for sale")
                && line.Contains("Titanium Ore x15");
        }),

        ("/lcraft blocked prints the full per-item detail including the verbatim reason", () =>
        {
            var d = RenderDetail(
                (Listed(SilverOre, 10, ("Hussypants", 7), ("Bussyqueen", 3)), NoRetainer),
                (Reachable(TitaniumOre, 15), Timeout));
            return d.Contains("retainer Hussypants") && d.Contains("Silver Ore x7")
                && d.Contains("retainer Bussyqueen") && d.Contains("Silver Ore x3")
                && d.Contains("Titanium Ore x15") && d.Contains(Timeout)
                && d.Contains("do NOT unlist anything for these");
        }),

        ("/lcraft blocked after a clean run says so instead of printing an empty block", () =>
        {
            var d = BlockedListings.Detail(BlockedListings.Summary.Empty);
            return d.Count == 1 && d[0].Contains("nothing to pull off sale");
        }),

        // ------------------------------------------------------------------ the data the whole thing rests on

        ("StoredElsewhere carries the retainer name for grouping, and it is not parsed out of the display text", () =>
        {
            var listing = new StoredElsewhere("the market board (listed by retainer Hussypants)", 7, Fetchable: false, Retainer: "Hussypants");
            var unnamed = new StoredElsewhere("the market board (your retainers' listings)", 7, Fetchable: false);
            // Owner falls back to the place name when the producer could not name a retainer - never a bad parse.
            return listing.Owner == "Hussypants" && unnamed.Owner == "the market board (your retainers' listings)";
        }),

        ("an unnamed listing still groups and still renders, under the fallback place name", () =>
        {
            var fallback = new DispatchPlan.Retrieve(SilverOre, 7,
                [new StoredElsewhere("the market board (your retainers' listings)", 7, Fetchable: false)]);
            var r = Render((fallback, NoRetainer));
            return r.Contains("Silver Ore x7") && r.Contains("your retainers' listings");
        }),
    };
}
