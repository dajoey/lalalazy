using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// The 0.1.3.0 batch fetch's one pure decision: which recipe rows go into the single Artisan
/// RestockFromRetainers(NewCraftingList) session. The session itself is reflection (GuardProbe proves the
/// members against the installed Artisan DLL); here we prove the queue selection against the fake world -
/// deferred-because-of-retrieval crafts queue their rows, mixed-reason deferrals queue too, pure non-retrieval
/// deferrals stay out, duplicates dedupe, unknown rows are dropped.
/// </summary>
internal static class RetainerBatchQueueTests
{
    private static (RecipeGraph Graph, VentureResolver Ventures, Tiering Tiering) Core()
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        return (graph, ventures, new Tiering(graph, new SourceClassifier(data, graph, ventures, [])));
    }

    private static DispatchPlan.Plan Plan(IInventory inv, params (uint RecipeId, int Crafts)[] lines)
    {
        var (graph, ventures, tiering) = Core();
        var cart = tiering.AssessCart(lines, inv);
        var planLines = cart.Lines.Select((a, i) => new DispatchPlan.Line(a, lines[i].Crafts)).ToList();
        return DispatchPlan.Build(planLines, cart.Totals, graph, ventures, [], null, inv);
    }

    private sealed class FakeInventory : IInventory
    {
        private readonly Dictionary<uint, int> _bags = new();
        private readonly Dictionary<uint, List<StoredElsewhere>> _elsewhere = new();
        public FakeInventory Set(uint itemId, int count) { _bags[itemId] = count; return this; }
        public FakeInventory SetElsewhere(uint itemId, int count, string where)
        {
            if (!_elsewhere.TryGetValue(itemId, out var list)) _elsewhere[itemId] = list = new List<StoredElsewhere>();
            list.Add(new StoredElsewhere(where, count));
            return this;
        }
        public int Count(uint itemId) => CountInBags(itemId) + (_elsewhere.TryGetValue(itemId, out var l) ? l.Sum(e => e.Quantity) : 0);
        public int CountInBags(uint itemId) => _bags.TryGetValue(itemId, out var c) ? c : 0;
        public IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId) =>
            _elsewhere.TryGetValue(itemId, out var l) ? l : Array.Empty<StoredElsewhere>();
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("the deferred r3762 shape - every blocker a retrieval - queues its recipe rows for the one batch session", () =>
        {
            // Sword x1 with ALL stock on the retainer: the ingot sub-craft defers on retrieve #200/#201 and the
            // root on craft #100 + retrieve #400. Both deferrals mention a retrieval, so both rows queue.
            var (graph, _, _) = Core();
            var inv = new FakeInventory()
                .SetElsewhere(World.Ore, 4, "retainer Hussypants")
                .SetElsewhere(World.Coal, 2, "retainer Hussypants")
                .SetElsewhere(World.Leather, 1, "retainer Hussypants");
            var p = Plan(inv, (World.SwordRecipe, 1));
            if (p.Crafts.Count != 0 || p.Deferred.Count != 2) return false;
            if (!p.Deferred.All(d => d.Reason.Contains("retrieve #"))) return false;
            var rows = RetainerBatch.Queue(p.Crafts, p.Deferred, id => graph.Row(id) is not null);
            return rows.SequenceEqual([World.IngotBsm, World.SwordRecipe]);
        }),
        ("a deferral blocked by a retrieval AND something else still queues - the fetch does its part, the re-plan keeps the rest deferred", () =>
        {
            // Coal is a gil vendor (buy #201), Ore sits on the retainer: the ingot deferral mixes both reasons,
            // and the batch session still fetches the retainer share.
            var (graph, _, _) = Core();
            var inv = new FakeInventory()
                .SetElsewhere(World.Ore, 4, "retainer Hussypants")
                .SetElsewhere(World.Leather, 1, "retainer Hussypants");
            var p = Plan(inv, (World.SwordRecipe, 1));
            var mixed = p.Deferred.SingleOrDefault(d => d.RecipeId == World.IngotBsm);
            if (mixed is null || !mixed.Reason.Contains("retrieve #") || !mixed.Reason.Contains("buy #")) return false;
            if (p.Vendor.Single().ItemId != World.Coal) return false;
            var rows = RetainerBatch.Queue(p.Crafts, p.Deferred, id => graph.Row(id) is not null);
            return rows.SequenceEqual([World.IngotBsm, World.SwordRecipe]);
        }),
        ("a deferral with no retrieval in it (a manual source) is not queued", () =>
        {
            // Mystery has no source at all - fetching stock cannot help, so the row stays out.
            var (graph, _, _) = Core();
            var p = Plan(new FakeInventory(), (World.CharmRecipe, 1));
            if (p.Deferred.Count != 1 || p.Deferred[0].Reason.Contains("retrieve #")) return false;
            var rows = RetainerBatch.Queue(p.Crafts, p.Deferred, id => graph.Row(id) is not null);
            return rows.Count == 0;
        }),
        ("queued crafts queue their rows; duplicates dedupe; unknown row ids are dropped (Artisan would throw on them)", () =>
        {
            var (graph, _, _) = Core();
            var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            var p = Plan(inv, (World.SwordRecipe, 1));
            if (p.Crafts.Count != 1) return false;
            // Two cart lines for the same recipe would hand Queue the same row twice - it must dedupe.
            var crafts = p.Crafts.Concat(p.Crafts).ToList();
            var rows = RetainerBatch.Queue(crafts, p.Deferred, id => graph.Row(id) is not null);
            if (rows.Count != 1 || rows[0] != World.SwordRecipe) return false;
            var unknown = RetainerBatch.Queue([new DispatchPlan.Craft(99999, World.Sword, 1, 0, false)], [], id => graph.Row(id) is not null);
            return unknown.Count == 0;
        }),
        ("no rowExists filter still queues rows, distinct, in craft order", () =>
        {
            var crafts = new List<DispatchPlan.Craft> { new(World.SwordRecipe, World.Sword, 1, 0, false), new(World.IngotBsm, World.Ingot, 2, 1, false) };
            var rows = RetainerBatch.Queue(crafts, [], null);
            return rows.SequenceEqual([World.SwordRecipe, World.IngotBsm]);
        }),
    };
}
