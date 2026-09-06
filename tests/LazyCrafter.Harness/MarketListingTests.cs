using LazyCrafter.Adapters;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// A market-board listing is NOT stock you have (t_c69287be, 2026-09-05).
/// <para>
/// The defect: <c>InventorySources.RetainerTypes</c> included container 12002 (RetainerMarket), so an item you had
/// listed for sale counted towards <c>leaf.Have</c>. <c>Missing</c> then fell to 0, and because
/// <c>DispatchPlan.RouteFor</c> starts with <c>if (leaf.Missing &lt;= 0) return Route.Have</c> the item was never
/// classified at all - it went to <c>Plan.Retrievals</c>, and <c>VisitIngredient</c> adds its blocker BEFORE the
/// on-hand early-out, so every craft above it was deferred behind a retrieval a summoning bell can never satisfy.
/// </para>
/// <para>
/// Each check below is paired with a NEGATIVE CONTROL that reproduces the old behaviour by putting the same stock
/// in a fetchable place (<c>SetElsewhere</c>) instead of on the board. The control must show the blocked plan; if
/// both sides ever agree, the test has stopped proving anything.
/// </para>
/// <para>
/// The second half of the suite (card t_05e6722b) covers the follow-on defect: whether a listing counts as stock
/// is settled above, but <c>DispatchPlan.PlacesFor</c> then walked <c>StoredWhere</c> largest-first with no notion
/// of reachable, so a listing bigger than the retainer stack absorbed the shortfall and the retrieve line named
/// the market board for units sitting on a retainer. Cases A/B/C are the probe from the card; B and C are kept as
/// controls specifically so a fix cannot pass by suppressing listings entirely.
/// </para>
/// </summary>
internal static class MarketListingTests
{
    private static readonly RetainerStats[] NoRetainers = Array.Empty<RetainerStats>();

    /// <summary>Plan <paramref name="crafts"/> runs of <see cref="World.SwordRecipe"/> against the given inventory.</summary>
    private static DispatchPlan.Plan PlanSword(FakeInventory inv, int crafts = 1)
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        var tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, NoRetainers));
        (uint RecipeId, int Crafts)[] lines = [(World.SwordRecipe, crafts)];
        var cart = tiering.AssessCart(lines, inv);
        var planLines = cart.Lines.Select((a, i) => new DispatchPlan.Line(a, lines[i].Crafts)).ToList();
        return DispatchPlan.Build(planLines, cart.Totals, graph, ventures, NoRetainers, null, inv);
    }

    public static IEnumerable<(string Name, Func<bool> Check)> Tests => new (string, Func<bool>)[]
    {
        // ---------------------------------------------------------------- the container list itself
        ("RetainerTypes does not contain the market-board container 12002",
            () => !InventorySources.RetainerTypes.Contains(InventorySources.RetainerMarket)),

        ("RetainerTypes still contains the retainer bags and the crystal pouch",
            () => new uint[] { 10000, 10001, 10002, 10003, 10004, 10005, 10006, 12001 }
                    .All(InventorySources.RetainerTypes.Contains)
                  && InventorySources.RetainerTypes.Length == 8),

        ("the RetainerMarket constant is kept, so StoredWhere can still name a listing",
            () => InventorySources.RetainerMarket == 12002),

        ("no other source silently picked 12002 up",
            () => !Enum.GetValues<InventorySource>()
                    .SelectMany(InventorySources.TypesFor)
                    .Contains(InventorySources.RetainerMarket)),

        // ---------------------------------------------------------------- config migration
        // Defaults() keys off the enum, which is unchanged, so an existing config still resolves every toggle and
        // no migration is needed. Asserted rather than assumed (the card asked for exactly this).
        ("inventory-source defaults are unchanged, so no config migration is needed",
            () =>
            {
                var d = InventorySources.Defaults();
                return d.Count == 7
                    && d["Bags"] && d["ArmouryChest"] && d["Saddlebag"] && d["Retainers"]
                    && d["AltCharacters"] && d["GlamourDresser"] && !d["FCChest"];
            }),

        // ---------------------------------------------------------------- routing
        ("a leaf whose only stock is a market listing is Missing, not Have",
            () =>
            {
                // Ingot: craftable from Ore x2 + Coal x1. The only Ingot in the world is listed for sale.
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Hide, 99)
                                             .SetListed(World.Ingot, 2);
                var data = World.Build();
                var graph = new RecipeGraph(data);
                var tiering = new Tiering(graph, new SourceClassifier(data, graph, new VentureResolver(data), NoRetainers));
                var leaf = tiering.Assess(World.SwordRecipe, inv).Leaves.Single(l => l.ItemId == World.Ingot);
                return leaf.Have == 0 && leaf.Missing == 2;
            }),

        ("NEGATIVE CONTROL: the same stock in a fetchable place still counts as Have",
            () =>
            {
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Hide, 99)
                                             .SetElsewhere(World.Ingot, 2, "retainer Hussypants");
                var data = World.Build();
                var graph = new RecipeGraph(data);
                var tiering = new Tiering(graph, new SourceClassifier(data, graph, new VentureResolver(data), NoRetainers));
                var leaf = tiering.Assess(World.SwordRecipe, inv).Leaves.Single(l => l.ItemId == World.Ingot);
                return leaf.Have == 2 && leaf.Missing == 0;
            }),

        ("a listed leaf never appears in Plan.Retrievals",
            () =>
            {
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Hide, 99)
                                             .SetListed(World.Ingot, 2);
                return PlanSword(inv).Retrievals.All(r => r.ItemId != World.Ingot);
            }),

        ("NEGATIVE CONTROL: fetchable stock in the same place DOES become a retrieval",
            () =>
            {
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Hide, 99)
                                             .SetElsewhere(World.Ingot, 2, "retainer Hussypants");
                return PlanSword(inv).Retrievals.Any(r => r.ItemId == World.Ingot && r.Quantity == 2);
            }),

        ("a listed leaf routes to its real source (sub-craft) instead of dead-ending",
            () =>
            {
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Hide, 99)
                                             .SetListed(World.Ingot, 2);
                var p = PlanSword(inv);
                // The Ingot is made (2 of them, from a same-job BSM recipe), and the Sword above it still runs.
                return p.Crafts.Any(c => c.ResultItemId == World.Ingot && c.Crafts == 2)
                    && p.Crafts.Any(c => c.RecipeId == World.SwordRecipe)
                    && p.Deferred.Count == 0
                    && p.HasWork;
            }),

        ("NEGATIVE CONTROL: with the listing counted as Have the parent craft is deferred, not queued",
            () =>
            {
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Hide, 99)
                                             .SetElsewhere(World.Ingot, 2, "the market board (listed by retainer Hussypants)");
                var p = PlanSword(inv);
                // This is precisely the 2026-09-05 stall: the sword is NOT queued, an impossible retrieval is,
                // and the sword is deferred behind it. (The unrelated Leather sub-craft still runs - its own
                // materials are in the bags - so this asserts the sword specifically, not an empty craft list.)
                return !p.Crafts.Any(c => c.RecipeId == World.SwordRecipe)
                    && p.Retrievals.Any(r => r.ItemId == World.Ingot && r.Quantity == 2)
                    && p.Deferred.Any(d => d.RecipeId == World.SwordRecipe && d.Reason.Contains($"retrieve #{World.Ingot}"));
            }),

        ("a listing is still NAMED so the player is told where the stock went",
            () =>
            {
                var inv = new FakeInventory().SetListed(World.Ingot, 2, "Hussypants");
                var where = inv.StoredWhere(World.Ingot);
                return where.Count == 1
                    && where[0].Where == "the market board (listed by retainer Hussypants)"
                    && where[0].Phrase == "2 on the market board (listed by retainer Hussypants)";
            }),

        ("a listing does not block a craft that has everything else in the bags",
            () =>
            {
                // Every material for the sword is in the bags AND a spare ingot is listed for sale: the listing
                // must be irrelevant, not a blocker.
                var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1).SetListed(World.Ingot, 5);
                var p = PlanSword(inv);
                return p.Retrievals.Count == 0 && p.Deferred.Count == 0
                    && p.Crafts.Count == 1 && p.Crafts[0].RecipeId == World.SwordRecipe;
            }),

        // ------------------------------------------------------- which PLACE a retrieval names (card t_05e6722b)
        // The 0.1.6.1 fix above is about whether a listing counts as stock. These are about where the retrieve
        // line points once the units ARE fetchable: `DispatchPlan.PlacesFor` walked StoredWhere largest-first with
        // no notion of reachable, so a listing bigger than the retainer stack absorbed the whole shortfall and the
        // player was sent to the market board for units that were sitting on a retainer.

        ("CASE A: a big listing must not outrank the retainer actually holding the stock",
            () =>
            {
                // Sword x4 = Ingot x8 + Leather x4. All 8 ingots are on a retainer; 20 more are listed for sale.
                // Have is 8 (the listing is not stock), so the shortfall is 8 - and those 8 are on the retainer.
                var inv = new FakeInventory()
                    .Set(World.Leather, 4)
                    .SetElsewhere(World.Ingot, 8, "retainer Dojarat")
                    .SetListed(World.Ingot, 20, "Hussypants");
                var r = PlanSword(inv, 4).Retrievals.Single(x => x.ItemId == World.Ingot);
                return r.Quantity == 8
                    && r.Places == "retainer Dojarat"
                    && r.Detail == "8 on retainer Dojarat"
                    && r.Where.All(w => w.Fetchable);
            }),

        ("CASE B CONTROL: with a listing SMALLER than the retainer stack, the same plan is unchanged",
            () =>
            {
                // Identical to case A except the listing is 2 instead of 20. This case was already correct before
                // the fix (quantity ordering happened to agree), so it is the guard that the fix changed nothing
                // it was not supposed to.
                var inv = new FakeInventory()
                    .Set(World.Leather, 4)
                    .SetElsewhere(World.Ingot, 8, "retainer Dojarat")
                    .SetListed(World.Ingot, 2, "Hussypants");
                var r = PlanSword(inv, 4).Retrievals.Single(x => x.ItemId == World.Ingot);
                return r.Quantity == 8 && r.Places == "retainer Dojarat" && r.Detail == "8 on retainer Dojarat";
            }),

        ("CASE C CONTROL: a stack that is ONLY listed is not a retrieval at all, at any scale",
            () =>
            {
                // The decisive case for the 0.1.6.1 fix, re-run at case A's scale: a listing alone leaves Have at 0
                // and the item routes to its real source instead of becoming an impossible fetch.
                var inv = new FakeInventory().Set(World.Ore, 99).Set(World.Coal, 99).Set(World.Leather, 4)
                                             .SetListed(World.Ingot, 20, "Hussypants");
                var p = PlanSword(inv, 4);
                return p.Retrievals.All(r => r.ItemId != World.Ingot)
                    && p.Crafts.Any(c => c.ResultItemId == World.Ingot && c.Crafts == 8)
                    && p.Crafts.Any(c => c.RecipeId == World.SwordRecipe);
            }),

        ("the listing is STILL named by StoredWhere - the fix must not suppress it",
            () =>
            {
                // If a future "fix" drops listings out of StoredWhere, case A would pass for the wrong reason.
                // 0.1.6.1 deliberately keeps naming them so the player is told where the stock went.
                var inv = new FakeInventory()
                    .SetElsewhere(World.Ingot, 8, "retainer Dojarat")
                    .SetListed(World.Ingot, 20, "Hussypants");
                var where = inv.StoredWhere(World.Ingot);
                return where.Count == 2
                    && where.Any(w => w.Where == "the market board (listed by retainer Hussypants)" && w.Quantity == 20 && !w.Fetchable)
                    && where.Any(w => w.Where == "retainer Dojarat" && w.Quantity == 8 && w.Fetchable);
            }),

        // ---- PlacesFor directly, so the ordering is proved independently of what StoredWhere happens to return.

        ("PlacesFor takes fetchable places first even when the listing is larger AND listed first",
            () =>
            {
                // Deliberately worst-case input order: the unfetchable place is first and biggest. Neither the
                // producer's order nor the quantity may decide this.
                IReadOnlyList<StoredElsewhere> where =
                [
                    new StoredElsewhere("the market board (listed by retainer Hussypants)", 20, Fetchable: false),
                    new StoredElsewhere("retainer Dojarat", 8),
                ];
                var taken = DispatchPlan.PlacesFor(where, 8);
                return taken.Count == 1 && taken[0].Where == "retainer Dojarat" && taken[0].Quantity == 8;
            }),

        ("PlacesFor still names a listing when NOTHING fetchable holds the units",
            () =>
            {
                // The fallback 0.1.6.1 wants: the refusal can say where the stock actually went. A fix that simply
                // discarded unfetchable places would return "elsewhere" here and fail this check.
                IReadOnlyList<StoredElsewhere> where =
                [
                    new StoredElsewhere("the market board (listed by retainer Hussypants)", 20, Fetchable: false),
                ];
                var taken = DispatchPlan.PlacesFor(where, 8);
                return taken.Count == 1
                    && taken[0].Where == "the market board (listed by retainer Hussypants)"
                    && taken[0].Quantity == 8
                    && !taken[0].Fetchable;
            }),

        ("PlacesFor spills onto a listing only after every fetchable place is exhausted",
            () =>
            {
                // 8 needed, 3 reachable: name the retainer for the 3 and the board for the rest, in that order.
                IReadOnlyList<StoredElsewhere> where =
                [
                    new StoredElsewhere("the market board (listed by retainer Hussypants)", 20, Fetchable: false),
                    new StoredElsewhere("retainer Dojarat", 3),
                ];
                var taken = DispatchPlan.PlacesFor(where, 8);
                return taken.Count == 2
                    && taken[0].Where == "retainer Dojarat" && taken[0].Quantity == 3
                    && taken[1].Where == "the market board (listed by retainer Hussypants)" && taken[1].Quantity == 5;
            }),

        ("PlacesFor keeps most-stocked-first WITHIN the fetchable places",
            () =>
            {
                // The pre-existing behaviour the fix must not lose: fewest places to visit.
                IReadOnlyList<StoredElsewhere> where =
                [
                    new StoredElsewhere("retainer Dojarat", 2),
                    new StoredElsewhere("the chocobo saddlebag", 9),
                    new StoredElsewhere("the market board (listed by retainer Hussypants)", 99, Fetchable: false),
                ];
                var taken = DispatchPlan.PlacesFor(where, 8);
                return taken.Count == 1 && taken[0].Where == "the chocobo saddlebag" && taken[0].Quantity == 8;
            }),

        ("StoredElsewhere defaults to fetchable, so only a listing is ever unreachable",
            () => new StoredElsewhere("retainer Dojarat", 8).Fetchable
                  && new FakeInventory().SetElsewhere(World.Ingot, 8, "retainer Dojarat")
                        .StoredWhere(World.Ingot).Single().Fetchable),

        ("the one-item retrieve shape (Retrieve button, /lcraft fetch) names the retainer too",
            () =>
            {
                // DispatchService.RetrieveOne / the batch re-enqueue build a Retrieve straight from StoredWhere
                // rather than through the planner, so they carried the same wrong-place naming. They now route
                // through PlacesFor; this is that composition, which is the whole pure part of those call sites.
                var inv = new FakeInventory()
                    .SetElsewhere(World.Ingot, 8, "retainer Dojarat")
                    .SetListed(World.Ingot, 20, "Hussypants");
                var r = new DispatchPlan.Retrieve(World.Ingot, 8, DispatchPlan.PlacesFor(inv.StoredWhere(World.Ingot), 8));
                return r.Places == "retainer Dojarat" && r.Detail == "8 on retainer Dojarat";
            }),
    };
}
