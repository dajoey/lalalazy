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
/// </summary>
internal static class MarketListingTests
{
    private static readonly RetainerStats[] NoRetainers = Array.Empty<RetainerStats>();

    /// <summary>Plan one craft of <see cref="World.SwordRecipe"/> against the given inventory.</summary>
    private static DispatchPlan.Plan PlanSword(FakeInventory inv)
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        var tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, NoRetainers));
        (uint RecipeId, int Crafts)[] lines = [(World.SwordRecipe, 1)];
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
    };
}
