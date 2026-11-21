using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>0.1.7.14: routing fish and monster drops to WahTools (Core/FarmContext, Core/DispatchPlan, Core/HuntLoop).
/// 0.1.7.15 adds CraftsNeeding, the WahTools blocked rows, and OrderMobs.</summary>
internal static class FarmTests
{
    private const uint Bsm = 10, Min = 16;
    private const uint Trout = 1, Hide = 2, Pearl = 3, Coal = 4, Stew = 10, Charm = 11, Lure = 12, Treat = 13, Feast = 14;
    private const uint StewRecipe = 100, CharmRecipe = 101, LureRecipe = 102, TreatRecipe = 103, FeastRecipe = 104;
    private const uint Wolf = 7001, Bat = 7002;

    // Stew = 2 Trout (fish only) + 1 Hide (drop, marketable). Charm = 1 Pearl (fish AND a mining node).
    // Lure = 1 Coal (drop AND gil vendor).
    private static FakeGameData World() => new FakeGameData()
        .Recipe(StewRecipe, Stew, 1, Bsm, 10, (Trout, 2), (Hide, 1))
        .Recipe(CharmRecipe, Charm, 1, Bsm, 10, (Pearl, 1))
        .Recipe(LureRecipe, Lure, 1, Bsm, 10, (Coal, 1))
        .Recipe(TreatRecipe, Treat, 1, Bsm, 10, (Hide, 1))
        .Recipe(FeastRecipe, Feast, 1, Bsm, 10, (Treat, 1))
        .Fish(Trout)
        .Fish(Pearl).Gatherable(Pearl, new GatherInfo(Min, 10, NodeType.Regular, Timed: false, Collectable: false))
        .Drop(Hide).Marketable(Hide)
        .Drop(Coal).GilVendor(Coal, 5);

    private static FarmContext Farm(bool fish = true, bool hunt = true, IReadOnlySet<uint>? excluded = null) =>
        new(fish, hunt, id => id == Hide ? new uint[] { Wolf, Bat } : id == Coal ? new uint[] { Bat } : Array.Empty<uint>(), excluded ?? new HashSet<uint>());

    private static DispatchPlan.Plan Plan(IInventory inv, FarmContext? farm, params (uint RecipeId, int Crafts)[] lines)
    {
        var data = World();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        var tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, []));
        var cart = tiering.AssessCart(lines, inv);
        var planLines = cart.Lines.Select((a, i) => new DispatchPlan.Line(a, lines[i].Crafts)).ToList();
        return DispatchPlan.Build(planLines, cart.Totals, graph, ventures, [], null, inv, null, farm);
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("no farm context: fish -> GBR gather, drop -> market board, nothing for WahTools (routing unchanged)", () =>
        {
            var p = Plan(new FakeInventory(), null, (StewRecipe, 1));
            return p.Gathers.Count == 1 && p.Gathers[0] is { ItemId: Trout, Quantity: 2, Kind: SourceKind.Fish }
                && p.Market.Count == 1 && p.Market[0].ItemId == Hide
                && p.Catches.Count == 0 && p.Hunts.Count == 0;
        }),
        ("FarmContext.None routes exactly like no context", () =>
        {
            var a = Plan(new FakeInventory(), null, (StewRecipe, 1));
            var b = Plan(new FakeInventory(), FarmContext.None, (StewRecipe, 1));
            return a.Gathers.Count == b.Gathers.Count && a.Market.Count == b.Market.Count && b.Catches.Count == 0 && b.Hunts.Count == 0;
        }),
        ("fish on: a node-less fish goes to WahTools, not GBR; its craft waits for the gather stage", () =>
        {
            var p = Plan(new FakeInventory().Set(Hide, 1), Farm(), (StewRecipe, 1));
            return p.Catches.Count == 1 && p.Catches[0] is { ItemId: Trout, Quantity: 2 }
                && p.Gathers.Count == 0
                && p.Crafts.Count == 1 && p.Crafts[0] is { RecipeId: StewRecipe, AfterGather: true }
                && p.Deferred.Count == 0 && p.HasWork;
        }),
        ("a fish that a node also yields stays with GBR", () =>
        {
            var p = Plan(new FakeInventory(), Farm(), (CharmRecipe, 1));
            return p.Gathers.Count == 1 && p.Gathers[0] is { ItemId: Pearl, Kind: SourceKind.RegularNode } && p.Catches.Count == 0;
        }),
        ("hunt on: a drop with known monsters goes to WahTools ahead of the market board, monsters in order", () =>
        {
            var p = Plan(new FakeInventory().Set(Trout, 2), Farm(), (StewRecipe, 1));
            return p.Hunts.Count == 1 && p.Hunts[0].ItemId == Hide && p.Hunts[0].Quantity == 1
                && p.Hunts[0].Mobs.SequenceEqual(new[] { Wolf, Bat })
                && p.Market.Count == 0
                && p.Crafts.Count == 1 && p.Crafts[0] is { RecipeId: StewRecipe, AfterGather: true }
                && p.Deferred.Count == 0;
        }),
        ("hunt quantity scales with the cart (3 stews -> 3 hides)", () =>
        {
            var p = Plan(new FakeInventory().Set(Trout, 6), Farm(), (StewRecipe, 3));
            return p.Hunts.Count == 1 && p.Hunts[0].Quantity == 3;
        }),
        ("a drop with no known monster stays on the market board", () =>
        {
            var noMobs = new FarmContext(true, true, _ => Array.Empty<uint>(), new HashSet<uint>());
            var p = Plan(new FakeInventory().Set(Trout, 2), noMobs, (StewRecipe, 1));
            return p.Hunts.Count == 0 && p.Market.Count == 1 && p.Market[0].ItemId == Hide;
        }),
        ("a gil-vendor item is bought, not hunted, even when a monster drops it", () =>
        {
            var p = Plan(new FakeInventory(), Farm(), (LureRecipe, 1));
            return p.Vendor.Count == 1 && p.Vendor[0].ItemId == Coal && p.Hunts.Count == 0;
        }),
        ("each setting works alone: fish off keeps GBR, hunt off keeps the market board", () =>
        {
            var fishOff = Plan(new FakeInventory(), Farm(fish: false), (StewRecipe, 1));
            var huntOff = Plan(new FakeInventory(), Farm(hunt: false), (StewRecipe, 1));
            return fishOff.Gathers.Count == 1 && fishOff.Catches.Count == 0 && fishOff.Hunts.Count == 1
                && huntOff.Catches.Count == 1 && huntOff.Hunts.Count == 0 && huntOff.Market.Count == 1;
        }),
        ("items WahTools refused this run go back to the old routes on the re-plan", () =>
        {
            var p = Plan(new FakeInventory(), Farm(excluded: new HashSet<uint> { Trout, Hide }), (StewRecipe, 1));
            return p.Catches.Count == 0 && p.Hunts.Count == 0
                && p.Gathers.Count == 1 && p.Gathers[0].ItemId == Trout
                && p.Market.Count == 1 && p.Market[0].ItemId == Hide;
        }),
        ("a wave with only WahTools work is runnable to the cart loop, not blocked", () =>
        {
            var hunts = new DispatchPlan.Plan([], [], [], [], [], [], []) { Hunts = [new DispatchPlan.Hunt(Hide, 1, [Wolf])] };
            var loop = new DispatchLoop([new DispatchLoop.CartLine(StewRecipe, Stew, 1)], _ => hunts, _ => "same");
            var d = loop.Begin();
            return hunts.HasWork && !hunts.IsEmpty && d.Outcome == DispatchLoop.Outcome.Wave
                && DispatchLoop.ItemsOf(hunts, []).Contains(Hide);
        }),
        ("HuntLoop: enough items -> done, whatever the batch count", () =>
            HuntLoop.Next(need: 3, gained: 3, batches: 1, dryBatches: 0) == HuntLoop.Step.Done
            && HuntLoop.Next(need: 3, gained: 5, batches: HuntLoop.MaxBatches, dryBatches: 5) == HuntLoop.Step.Done),
        ("HuntLoop: short and still dropping -> hunt more", () =>
            HuntLoop.Next(need: 5, gained: 2, batches: 1, dryBatches: 0) == HuntLoop.Step.HuntMore
            && HuntLoop.Next(need: 5, gained: 2, batches: 3, dryBatches: 1) == HuntLoop.Step.HuntMore),
        ("HuntLoop: gives up after two dry batches in a row or the batch cap", () =>
            HuntLoop.Next(need: 5, gained: 0, batches: 2, dryBatches: HuntLoop.MaxDryBatches) == HuntLoop.Step.GiveUp
            && HuntLoop.Next(need: 5, gained: 4, batches: HuntLoop.MaxBatches, dryBatches: 0) == HuntLoop.Step.GiveUp),
        ("HuntLoop: kills per batch = items missing, clamped to [3, 30]", () =>
            HuntLoop.KillsFor(1) == HuntLoop.MinKills && HuntLoop.KillsFor(12) == 12 && HuntLoop.KillsFor(500) == HuntLoop.MaxKills
            && HuntLoop.KillsFor(0) == HuntLoop.MinKills),
        ("an excluded item drops its dependent crafts, not unrelated ones", () =>
        {
            var graph = new RecipeGraph(World());
            var crafts = new[]
            {
                new DispatchPlan.Craft(StewRecipe, Stew, 1, 0, false),
                new DispatchPlan.Craft(LureRecipe, Lure, 1, 0, false),
            };
            var needing = DispatchPlan.CraftsNeeding(crafts, Hide, graph);
            return needing.Count == 1 && needing.Contains(StewRecipe);
        }),
        ("a two-level chain: a parent whose sub-craft needs the excluded item returns both recipes", () =>
        {
            var graph = new RecipeGraph(World());
            var crafts = new[]
            {
                new DispatchPlan.Craft(TreatRecipe, Treat, 1, 0, false),
                new DispatchPlan.Craft(FeastRecipe, Feast, 1, 0, false),
            };
            var needing = DispatchPlan.CraftsNeeding(crafts, Hide, graph);
            return needing.Count == 2 && needing.Contains(TreatRecipe) && needing.Contains(FeastRecipe);
        }),
        ("blocked report lists WahTools catches and hunts", () =>
        {
            var plan = new DispatchPlan.Plan([], [], [], [], [], [], [])
            {
                Catches = [new DispatchPlan.Catch(Trout, 2)],
                Hunts = [new DispatchPlan.Hunt(Hide, 1, [Wolf])],
            };
            var blocked = PlanReport.BlockedFrom(plan, id => $"#{id}", _ => null);
            return blocked.Count == 2
                && blocked.Any(b => b is { Kind: StepKind.Catch, ItemId: Trout } && b.Where == "WahTools Feesh")
                && blocked.Any(b => b is { Kind: StepKind.Hunt, ItemId: Hide } && b.Where == "WahTools Hunter");
        }),
        ("OrderMobs: one monster below the level sorts first", () =>
            FarmContext.OrderMobs([1], _ => (75, 90), 100).SequenceEqual(new uint[] { 1 })),
        ("OrderMobs: several below - the highest at or below goes first", () =>
            FarmContext.OrderMobs([1, 2, 3], id => id switch { 1 => (50, 60), 2 => (70, 90), _ => (40, 75) }, 100)
                .SequenceEqual(new uint[] { 2, 3, 1 })),
        ("OrderMobs: exactly at the level counts as below", () =>
            FarmContext.OrderMobs([1, 2], id => id switch { 1 => (50, 60), 2 => (80, 100) }, 100)
                .SequenceEqual(new uint[] { 2, 1 })),
        ("OrderMobs: all above the level - the lowest low goes first", () =>
            FarmContext.OrderMobs([1, 2], id => id switch { 1 => (110, 120), 2 => (90, 130) }, 100)
                .SequenceEqual(new uint[] { 2, 1 })),
        ("OrderMobs: unknown levels sit between below and above", () =>
            FarmContext.OrderMobs([1, 2, 3], id => id switch { 1 => (50, 80), 2 => (120, 140), _ => ((int, int)?)null }, 90)
                .SequenceEqual(new uint[] { 1, 3, 2 })),
        ("OrderMobs: ties keep the input order", () =>
            FarmContext.OrderMobs([1, 2, 3], id => id switch { 1 => (70, 80), 2 => (70, 80), _ => (40, 50) }, 100)
                .SequenceEqual(new uint[] { 1, 2, 3 })),
        ("OrderMobs: level 0 leaves the order unchanged", () =>
            FarmContext.OrderMobs([1, 2], id => id switch { 1 => (200, 300), _ => ((int, int)?)null }, 0)
                .SequenceEqual(new uint[] { 1, 2 })),
    };
}
