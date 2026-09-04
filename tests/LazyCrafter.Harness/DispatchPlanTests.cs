using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>Phase 5: routing a cart to the four hand-off channels (Core/DispatchPlan).</summary>
internal static class DispatchPlanTests
{
    private static readonly RetainerStats Miner = new("Miner", Level: 20, JobId: World.Min, ItemLevel: 0, Gathering: 120, Perception: 250);
    private static readonly RetainerStats Paladin = new("Paladin", Level: 90, JobId: World.Pld, ItemLevel: 95, Gathering: 0, Perception: 0);

    private static (RecipeGraph Graph, VentureResolver Ventures, Tiering Tiering) Core(IReadOnlyList<RetainerStats> retainers)
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        return (graph, ventures, new Tiering(graph, new SourceClassifier(data, graph, ventures, retainers)));
    }

    private static DispatchPlan.Plan Plan(IReadOnlyList<RetainerStats> retainers, IInventory inv, params (uint RecipeId, int Crafts)[] lines)
    {
        var (graph, ventures, tiering) = Core(retainers);
        var cart = tiering.AssessCart(lines, inv);
        var planLines = cart.Lines.Select((a, i) => new DispatchPlan.Line(a, lines[i].Crafts)).ToList();
        return DispatchPlan.Build(planLines, cart.Totals, graph, ventures, retainers);
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("everything on hand -> one top-level craft, nothing else", () =>
        {
            // Sword x1: 2 Ingot + 1 Leather, all on hand.
            var p = Plan([], new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1), (World.SwordRecipe, 1));
            return p.Crafts.Count == 1 && p.Crafts[0] is { RecipeId: World.SwordRecipe, Crafts: 1, Depth: 0, AfterGather: false }
                && p.Gathers.Count == 0 && p.Ventures.Count == 0 && p.Vendor.Count == 0 && p.Market.Count == 0 && p.Manual.Count == 0 && p.Deferred.Count == 0
                && p.HasWork && !p.IsEmpty;
        }),
        ("sub-craft from on-hand mats is queued before its parent (depth-first) with ceil(missing / resultAmount) crafts", () =>
        {
            // Sword x1 needs 2 Ingot (0 on hand; Ingot = 2 Ore + 1 Coal, on hand 4 Ore + 2 Coal) + 1 Leather on hand.
            var p = Plan([], new FakeInventory().Set(World.Ore, 4).Set(World.Coal, 2).Set(World.Leather, 1), (World.SwordRecipe, 1));
            return p.Crafts.Count == 2
                && p.Crafts[0] is { RecipeId: World.IngotBsm, Crafts: 2, Depth: 1 }     // same-job (BSM) ingot recipe, 2 crafts for 2 ingots
                && p.Crafts[1] is { RecipeId: World.SwordRecipe, Crafts: 1, Depth: 0 }
                && p.Deferred.Count == 0;
        }),
        ("regular-node material -> GBR gather list; the craft that needs it is queued AfterGather", () =>
        {
            // Sword: Ingot needs Ore (regular node, MIN 10) - no Ore on hand, Coal + Leather on hand.
            var p = Plan([], new FakeInventory().Set(World.Coal, 2).Set(World.Leather, 1), (World.SwordRecipe, 1));
            return p.Gathers.Count == 1 && p.Gathers[0] is { ItemId: World.Ore, Quantity: 4, Kind: SourceKind.RegularNode }
                && p.GatherDictionary()[World.Ore] == 4
                && p.Crafts.Count == 2 && p.Crafts[0].RecipeId == World.IngotBsm && p.Crafts[0].AfterGather && p.Crafts[1].AfterGather
                && p.Ventures.Count == 0 && p.Deferred.Count == 0;
        }),
        ("gather beats venture when both qualify (Ore: node and a qualifying MIN retainer)", () =>
        {
            var p = Plan([Miner], new FakeInventory().Set(World.Coal, 2).Set(World.Leather, 1), (World.SwordRecipe, 1));
            return p.Gathers.Count == 1 && p.Gathers[0].ItemId == World.Ore && p.Ventures.Count == 0;
        }),
        ("venture-only material -> ARC with the best retainer match; dependent crafts are deferred, not sent to Artisan", () =>
        {
            // Trophy: Coal (gil vendor, on hand here) + Hide (drop; venture #2 needs a combat retainer ilvl 60+). Paladin qualifies.
            var p = Plan([Paladin], new FakeInventory().Set(World.Coal, 1), (World.TrophyRecipe, 1));
            return p.Ventures.Count == 1 && p.Ventures[0] is { ItemId: World.Hide, Quantity: 1 } && p.Ventures[0].Match.Retainer == Paladin
                && p.VentureDictionary()[World.Hide] == 1
                && p.Crafts.Count == 0
                && p.Deferred.Count == 1 && p.Deferred[0].RecipeId == World.TrophyRecipe && p.Deferred[0].Reason.Contains("venture")
                && p.Manual.Count == 0;
        }),
        ("no qualifying retainer -> drop-only material is Manual and the craft is deferred", () =>
        {
            var p = Plan([], new FakeInventory().Set(World.Coal, 1), (World.TrophyRecipe, 1));
            return p.Ventures.Count == 0 && p.Manual.Count == 1 && p.Manual[0].ItemId == World.Hide && p.Manual[0].Sources.Contains(SourceKind.Drop)
                && p.Crafts.Count == 0 && p.Deferred.Count == 1 && p.Deferred[0].Reason.Contains("manual") && !p.HasWork;
        }),
        ("gil-vendor material -> Vendor shopping list, craft deferred with a 'buy' reason", () =>
        {
            // Ornament: RareOre (timed node -> gather) + Coal (gil vendor, also marketable). Give RareOre so only Coal is short.
            var p = Plan([], new FakeInventory().Set(World.RareOre, 1), (World.OrnamentRecipe, 1));
            return p.Vendor.Count == 1 && p.Vendor[0] is { ItemId: World.Coal, Quantity: 1 } && p.Market.Count == 0
                && p.Crafts.Count == 0 && p.Deferred.Count == 1 && p.Deferred[0].Reason.Contains("buy");
        }),
        ("market-only material -> Market shopping list", () =>
        {
            var data = World.Build().Recipe(950, 951, 1, World.Bsm, 10, (World.MarketOnly, 3));
            var graph = new RecipeGraph(data);
            var ventures = new VentureResolver(data);
            var tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, []));
            var cart = tiering.AssessCart([(950u, 2)], new FakeInventory().Set(World.MarketOnly, 1));
            var p = DispatchPlan.Build([new DispatchPlan.Line(cart.Lines[0], 2)], cart.Totals, graph, ventures, []);
            return p.Market.Count == 1 && p.Market[0] is { ItemId: World.MarketOnly, Quantity: 5 } && p.Vendor.Count == 0
                && p.Deferred.Count == 1 && p.Deferred[0].Reason.Contains("market");
        }),
        ("cart totals drive the gather quantity across two lines sharing a material", () =>
        {
            // Sword x1 (2 Ingot -> 4 Ore + 2 Coal) + Pendant x1 (Ornament -> RareOre timed + Coal; + Coal). No Ore, RareOre on hand, Coal x4, Leather x1.
            var p = Plan([], new FakeInventory().Set(World.Coal, 4).Set(World.Leather, 1).Set(World.RareOre, 1), (World.SwordRecipe, 1), (World.PendantRecipe, 1));
            var ore = p.Gathers.SingleOrDefault(g => g.ItemId == World.Ore);
            return ore is { Quantity: 4 }
                && p.Crafts.Select(c => c.RecipeId).SequenceEqual([World.IngotBsm, World.SwordRecipe, World.OrnamentRecipe, World.PendantRecipe])
                && p.Crafts.Single(c => c.RecipeId == World.OrnamentRecipe).Depth == 1
                && p.Deferred.Count == 0;
        }),
        ("a blocked sub-craft defers both itself and its parent, with the blocker named", () =>
        {
            // Charm needs Mystery (Unknown). A recipe using Charm as an ingredient:
            var data = World.Build().Recipe(960, 961, 1, World.Bsm, 60, (World.Charm, 1), (World.Coal, 1));
            var graph = new RecipeGraph(data);
            var ventures = new VentureResolver(data);
            var tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, []));
            var cart = tiering.AssessCart([(960u, 1)], new FakeInventory().Set(World.Coal, 1));
            var p = DispatchPlan.Build([new DispatchPlan.Line(cart.Lines[0], 1)], cart.Totals, graph, ventures, []);
            return p.Crafts.Count == 0 && p.Manual.Count == 1 && p.Manual[0].ItemId == World.Mystery
                && p.Deferred.Count == 2 && p.Deferred[0].RecipeId == World.CharmRecipe && p.Deferred[1].RecipeId == 960
                && p.Deferred[1].Reason.Contains($"craft #{World.Charm}");
        }),
        ("RouteLeaf mirrors Build: sub-craft returns the same-job recipe; on-hand -> OnHand; venture -> Venture", () =>
        {
            var (graph, ventures, _) = Core([Paladin]);
            var craft = DispatchPlan.RouteLeaf(new IngredientLeaf(World.Ingot, 2, 0, [SourceKind.SubCraft, SourceKind.Market], EffortTier.Easy), World.Arm, graph, ventures, []);
            var have = DispatchPlan.RouteLeaf(new IngredientLeaf(World.Ingot, 2, 2, [SourceKind.OnHand], EffortTier.Now), World.Bsm, graph, ventures, []);
            var vent = DispatchPlan.RouteLeaf(new IngredientLeaf(World.Hide, 1, 0, [SourceKind.Venture, SourceKind.Drop], EffortTier.Easy), World.Bsm, graph, ventures, [Paladin]);
            var drop = DispatchPlan.RouteLeaf(new IngredientLeaf(World.Hide, 1, 0, [SourceKind.Drop], EffortTier.RealEffort), World.Bsm, graph, ventures, []);
            return craft.Channel == SourceKind.SubCraft && craft.SubRecipe?.RecipeId == World.IngotArm
                && have.Channel == SourceKind.OnHand && vent.Channel == SourceKind.Venture && drop.Channel == SourceKind.Drop;
        }),
        ("empty cart -> empty plan", () =>
        {
            var p = Plan([], new FakeInventory());
            return p.IsEmpty && !p.HasWork;
        }),
    };
}
