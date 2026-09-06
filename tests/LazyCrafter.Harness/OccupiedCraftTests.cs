using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Mechanism 6 (card t_0b4d8b2c): a game-"occupied" craft failure must never be reported as
/// "retrieve/unlist these materials".
///
/// <para><b>The run being replayed.</b> Joey, 2026-09-06 11:58, 0.1.6.6. He had just bought the last ingredients
/// at the market board, so the board window still owned the client's input and Artisan bounced every craft:</para>
/// <code>
/// 11:58:19 [LazyCrafter] Artisan: crafting Adamantite Nugget x98 (1/2).
/// 11:58:21 [Artisan]     Error Warnings [4]: Unable to execute command while occupied.
/// 11:58:21 [LazyCrafter] Artisan: Adamantite Nugget - expected 98, made 0.
/// 11:58:26 [LazyCrafter] Artisan craft of Bladed Steel Jig refused: Adamantite Nugget x98 is not in your bags
///                        (98 not in your bags), Cloud Mica Whetstone x99 is not in your bags...
/// 11:58:26 [LazyCrafter] retrieve before crafting: Adamantite Nugget x98 from elsewhere; ...
/// 11:58:26 [LazyCrafter] to unblock cart you have to pull 1 material off sale (3 units across 1 retainer):
/// 11:58:26 [LazyCrafter]   Hussypants: Cloud Mica x3
/// 11:58:26 [LazyCrafter] heading to the nearest market board ...
/// </code>
/// <para>
/// Only <b>Cloud Mica x3 / Hussypants</b> was a real blocker. The nugget and the whetstone were never "elsewhere"
/// and were never listed for sale - they did not exist because the crafts that would have made them were refused.
/// The plugin invented a summoning-bell errand and Lifestream physically walked him there.
/// </para>
///
/// <para><b>Every check here asserts on the RENDERED TEXT.</b> That is not a style preference: this is a reporting
/// defect end to end. The shortfall list, the quantities and the item ids were all CORRECT in memory throughout -
/// an assertion on <c>BagsShortfall</c>'s output, or on the blocked-materials collection, stays green right through
/// the bug. What was wrong was the sentence built from them.</para>
///
/// <para><b>Not tested here, deliberately:</b> anything that waits, retries, polls or auto-stops on the client being
/// busy. That is Joey's behaviour decision, pending on Helm thread <c>t-joey-1788710417021</c>, and this card does
/// not implement it.</para>
/// </summary>
internal static class OccupiedCraftTests
{
    // The real ids from his run, so a log line can be matched by eye.
    private const uint AdamantiteNugget = 12537, CloudMicaWhetstone = 12539, CloudMica = 5116, BladedSteelJig = 12600;
    private const uint AdamantiteOre = 12535;
    private const uint JigRecipe = 2861;

    private const string Board = "the market board";

    private static string Name(uint id) => id switch
    {
        AdamantiteNugget => "Adamantite Nugget",
        CloudMicaWhetstone => "Cloud Mica Whetstone",
        CloudMica => "Cloud Mica",
        BladedSteelJig => "Bladed Steel Jig",
        AdamantiteOre => "Adamantite Ore",
        _ => $"#{id}",
    };

    /// <summary>
    /// The 11:58 shape, built from the REAL Core path rather than by hand: an inventory in which the two
    /// intermediates are nowhere at all (the crafts that would have made them were refused), and the recipe that
    /// consumes them. <see cref="DispatchPlan.BagsShortfall"/> produces exactly what the dispatcher saw.
    /// </summary>
    private static IReadOnlyList<DispatchPlan.Retrieve> ElevenFiftyEightShortfall()
    {
        var inv = new FakeInventory();      // nothing in bags, nothing elsewhere, nothing listed for these two
        var jig = new RecipeRow(JigRecipe, BladedSteelJig, 1, World.Bsm, 60,
            [(AdamantiteNugget, 98), (CloudMicaWhetstone, 99)]);
        return DispatchPlan.BagsShortfall(jig, 1, inv);
    }

    /// <summary>The blocked-craft record the dispatcher would hold after the five refusals at 11:58:19-26.</summary>
    private static CraftDiagnosis.BlockedCrafts Refused()
    {
        var blocked = new CraftDiagnosis.BlockedCrafts();
        blocked.Note(AdamantiteNugget, Board);
        blocked.Note(CloudMicaWhetstone, Board);
        return blocked;
    }

    /// <summary>The genuine listing blocker: Cloud Mica x3, all of it on sale via Hussypants.</summary>
    private static DispatchPlan.Retrieve GenuineListing() =>
        new(CloudMica, 3, [new StoredElsewhere("the market board (listed by retainer Hussypants)", 3, Fetchable: false, Retainer: "Hussypants")]);

    /// <summary>The chat block the bags guard prints, as chat would show it.</summary>
    private static string RenderRefusal(CraftDiagnosis.BlockedCrafts blocked) =>
        string.Join("\n", CraftDiagnosis.RefusalLines(
            Name(BladedSteelJig), CraftDiagnosis.SplitShortfall(ElevenFiftyEightShortfall(), blocked), Name));

    /// <summary>The end-of-run "pull these off sale" block, as chat would show it, phantoms already stripped.</summary>
    private static string RenderEndOfRun(CraftDiagnosis.BlockedCrafts blocked)
    {
        (DispatchPlan.Retrieve, string)[] unfetched =
        [
            // What the run actually recorded: the two phantoms (invented by the old code from the hole in the bags)
            // plus the one real listing blocker.
            (ElevenFiftyEightShortfall()[0], "no retainer is holding any in its bags"),
            (ElevenFiftyEightShortfall()[1], "no retainer is holding any in its bags"),
            (GenuineListing(), "no retainer is holding any in its bags"),
        ];
        var real = CraftDiagnosis.WithoutPhantoms(unfetched, blocked);
        var lines = new List<string>();
        if (CraftDiagnosis.EndOfRunLine(blocked) is { } busy) lines.Add(busy);
        lines.AddRange(BlockedListings.Lines(BlockedListings.Summarise(real, Name), "cart"));
        return string.Join("\n", lines);
    }

    /// <summary>Would the Lifestream bell walk fire? It is gated on exactly this (see <c>DispatchService.WalkToBell</c>).</summary>
    private static bool BellWalkWouldFire(CraftDiagnosis.BlockedCrafts blocked, params (DispatchPlan.Retrieve, string)[] unfetched) =>
        BlockedListings.Summarise(CraftDiagnosis.WithoutPhantoms(unfetched, blocked), Name).HasListings;

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        // ---------------------------------------------------------------- 1. the craft failure names its cause

        ("busy: a zero-made craft under an occupied client is not filed as a bare shortfall", () =>
        {
            var reason = CraftDiagnosis.StepReason(CraftDiagnosis.Cause.ClientBusy, 98, 0, Board);
            // The exact string the old code produced, and which made the two cases indistinguishable downstream.
            return reason != "expected 98, made 0"
                && reason.Contains("refused the craft command")
                && reason.Contains(Board)
                && reason.Contains("nothing is missing");
        }),

        ("busy: a genuine shortfall still reads exactly as it did (no collateral rewording)", () =>
            CraftDiagnosis.StepReason(CraftDiagnosis.Cause.Shortfall, 98, 3, null) == "expected 98, made 3"),

        ("busy: the craft line names the window and tells him nothing is missing", () =>
        {
            var line = CraftDiagnosis.BusyCraftLine("Adamantite Nugget", 98, 0, Board);
            return line.Contains("Adamantite Nugget")
                && line.Contains(Board)
                && line.Contains("Nothing is missing from your bags")
                && line.Contains("Resume");
        }),

        // ---------------------------------------------------------------- 2. the phantom never enters the retrieve path

        ("11:58 REPLAY: the refusal block does NOT tell him to retrieve the nugget or the whetstone", () =>
        {
            var r = RenderRefusal(Refused());
            // The two false sentences from his log, verbatim in shape.
            return !r.Contains("retrieve before crafting")
                && !r.Contains("Adamantite Nugget x98 is not in your bags")
                && !r.Contains("Cloud Mica Whetstone x99 is not in your bags")
                && !r.Contains("from elsewhere");
        }),

        ("11:58 REPLAY: the refusal block says the true thing instead, and names the window", () =>
        {
            var r = RenderRefusal(Refused());
            return r.Contains("Adamantite Nugget x98 was never made")
                && r.Contains("Cloud Mica Whetstone x99 was never made")
                && r.Contains("would not accept the craft command")
                && r.Contains(Board)
                && r.Contains("not on the market board");
        }),

        ("11:58 REPLAY: a real shortfall in the SAME run still gets the retrieve wording", () =>
        {
            // Negative control for suppression: the whetstone is genuinely on a retainer, the nugget is a phantom.
            // If the fix worked by simply dropping everything, this check goes red.
            var inv = new FakeInventory().SetElsewhere(CloudMicaWhetstone, 99, "retainer Dojarat");
            var jig = new RecipeRow(JigRecipe, BladedSteelJig, 1, World.Bsm, 60,
                [(AdamantiteNugget, 98), (CloudMicaWhetstone, 99)]);
            var split = CraftDiagnosis.SplitShortfall(DispatchPlan.BagsShortfall(jig, 1, inv), Refused());
            var r = string.Join("\n", CraftDiagnosis.RefusalLines(Name(BladedSteelJig), split, Name));
            return r.Contains("retrieve before crafting")
                && r.Contains("Cloud Mica Whetstone x99")
                && r.Contains("retainer Dojarat")
                && r.Contains("Adamantite Nugget x98 was never made")
                && !r.Contains("Adamantite Nugget x98 from");
        }),

        ("busy: with NO craft refused, the shortfall wording is byte-for-byte the pre-fix wording", () =>
        {
            // The other half of the same control: nothing was blocked, so nothing may change.
            var split = CraftDiagnosis.SplitShortfall(ElevenFiftyEightShortfall(), new CraftDiagnosis.BlockedCrafts());
            var r = string.Join("\n", CraftDiagnosis.RefusalLines(Name(BladedSteelJig), split, Name));
            return r.Contains("Artisan craft of Bladed Steel Jig refused: Adamantite Nugget x98 is not in your bags (98 not in your bags)")
                && r.Contains("retrieve before crafting: Adamantite Nugget x98 from elsewhere")
                && !r.Contains("was never made");
        }),

        ("busy: a phantom must NOT carry the 'retrieve #' token that queues a retainer bell session", () =>
        {
            // RetainerBatch.Queue selects deferrals whose reason contains "retrieve #" and opens an Artisan bell
            // session for those recipes. A phantom in there sends him to a bell for a material that is nowhere.
            var reason = CraftDiagnosis.DeferralReason(CraftDiagnosis.SplitShortfall(ElevenFiftyEightShortfall(), Refused()));
            return !reason.Contains("retrieve #")
                && reason.Contains("blocked craft")
                && reason.Contains(Board);
        }),

        ("busy: a genuine shortfall's deferral DOES still carry 'retrieve #' (the batch queue must keep working)", () =>
        {
            var inv = new FakeInventory().SetElsewhere(AdamantiteNugget, 98, "retainer Dojarat");
            var jig = new RecipeRow(JigRecipe, BladedSteelJig, 1, World.Bsm, 60, [(AdamantiteNugget, 98)]);
            var reason = CraftDiagnosis.DeferralReason(
                CraftDiagnosis.SplitShortfall(DispatchPlan.BagsShortfall(jig, 1, inv), Refused()));
            return reason.Contains($"retrieve #{AdamantiteNugget} x98")
                && RetainerBatch.Queue(
                       Array.Empty<DispatchPlan.Craft>(),
                       [new DispatchPlan.Deferral(JigRecipe, BladedSteelJig, 1, reason)],
                       _ => true).Contains(JigRecipe);
        }),

        // ---------------------------------------------------------------- 3. the unlist advice and the bell walk

        ("11:58 REPLAY: the unlist block names ONLY Cloud Mica x3 / Hussypants", () =>
        {
            var r = RenderEndOfRun(Refused());
            return r.Contains("Hussypants: Cloud Mica x3")
                && r.Contains("pull 1 material off sale")
                && r.Contains("3 units across 1 retainer")
                // the two phantoms must appear nowhere in the pull advice
                && !r.Contains("Adamantite Nugget x98")
                && !r.Contains("Cloud Mica Whetstone x99");
        }),

        ("11:58 REPLAY: he is told the crafts were refused, not that materials are missing", () =>
        {
            var r = RenderEndOfRun(Refused());
            return r.Contains("some crafts never ran")
                && r.Contains("refused the craft command")
                && r.Contains(Board)
                && r.Contains("not a missing material");
        }),

        ("11:58 REPLAY: with ONLY phantoms blocking, the summoning-bell walk does not fire", () =>
        {
            // The Lifestream walk (`[Lifestream] Appending command 3` in his log) is gated on HasListings. Two
            // phantoms and nothing real must leave it false - otherwise he is physically moved for nothing.
            var blocked = Refused();
            var phantoms = ElevenFiftyEightShortfall();
            return !BellWalkWouldFire(blocked,
                (phantoms[0], "no retainer is holding any in its bags"),
                (phantoms[1], "no retainer is holding any in its bags"));
        }),

        ("11:58 REPLAY: with a REAL listing blocker present, the bell walk still fires", () =>
        {
            // The other side of the same gate: suppressing the walk unconditionally would be a new bug.
            var blocked = Refused();
            var phantoms = ElevenFiftyEightShortfall();
            return BellWalkWouldFire(blocked,
                (phantoms[0], "no retainer is holding any in its bags"),
                (GenuineListing(), "no retainer is holding any in its bags"));
        }),

        ("busy: a phantom never reaches the Run tab's 'needs you' list either", () =>
        {
            var phantoms = ElevenFiftyEightShortfall();
            (DispatchPlan.Retrieve, string)[] unfetched =
            [
                (phantoms[0], "no retainer is holding any in its bags"),
                (GenuineListing(), "no retainer is holding any in its bags"),
            ];
            var merged = BlockedListings.MergeIntoBlocked(
                Array.Empty<BlockedItem>(), CraftDiagnosis.WithoutPhantoms(unfetched, Refused()), Name);
            var snap = new RunSnapshot(
                RunState.Done, "Done", "Done", "x", "cart", ["Bladed Steel Jig"],
                new DateTime(2026, 9, 6, 11, 49, 0), new DateTime(2026, 9, 6, 11, 58, 30), TimeSpan.FromMinutes(9), 3,
                Array.Empty<RunStep>(), merged, null, false).Report();
            return snap.Contains("Cloud Mica x3") && !snap.Contains("Adamantite Nugget");
        }),

        // ---------------------------------------------------------------- 4. the discriminator itself

        ("busy: a material a retainer IS holding stays a genuine retrieval even when its craft was refused", () =>
        {
            // Both conditions are required. A refused craft alone must not suppress a retrieval that is real -
            // that would trade one wrong answer for another.
            var inv = new FakeInventory().SetElsewhere(AdamantiteNugget, 98, "retainer Dojarat");
            var jig = new RecipeRow(JigRecipe, BladedSteelJig, 1, World.Bsm, 60, [(AdamantiteNugget, 98)]);
            var split = CraftDiagnosis.SplitShortfall(DispatchPlan.BagsShortfall(jig, 1, inv), Refused());
            return split.Genuine.Count == 1 && split.Phantom.Count == 0;
        }),

        ("busy: an item whose craft was never refused is genuine even with nowhere holding it", () =>
        {
            var split = CraftDiagnosis.SplitShortfall(ElevenFiftyEightShortfall(), new CraftDiagnosis.BlockedCrafts());
            return split.Genuine.Count == 2 && split.Phantom.Count == 0;
        }),

        ("busy: the blocked-craft record is per run and clears", () =>
        {
            var blocked = Refused();
            blocked.Clear();
            return blocked.IsEmpty
                && CraftDiagnosis.EndOfRunLine(blocked) is null
                && CraftDiagnosis.SplitShortfall(ElevenFiftyEightShortfall(), blocked).Phantom.Count == 0;
        }),

        ("busy: an unnamed window still produces a truthful line, never a silent fall-through", () =>
        {
            // Single-item fixture on purpose: the two-item one would leave the whetstone genuine, and asserting
            // "no retrieve wording anywhere" would then be asserting the wrong thing.
            var inv = new FakeInventory();
            var jig = new RecipeRow(JigRecipe, BladedSteelJig, 1, World.Bsm, 60, [(AdamantiteNugget, 98)]);
            var blocked = new CraftDiagnosis.BlockedCrafts();
            blocked.Note(AdamantiteNugget, null);
            var r = string.Join("\n", CraftDiagnosis.RefusalLines(
                Name(BladedSteelJig),
                CraftDiagnosis.SplitShortfall(DispatchPlan.BagsShortfall(jig, 1, inv), blocked), Name));
            return r.Contains("Adamantite Nugget x98 was never made")
                && r.Contains(CraftDiagnosis.UnknownWindow)
                && !r.Contains("from elsewhere");
        }),

        ("busy: a clean run says nothing at all about busy crafts", () =>
            CraftDiagnosis.EndOfRunLine(new CraftDiagnosis.BlockedCrafts()) is null),
    };
}
