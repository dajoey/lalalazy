using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// The retrieval loop's decision logic (card t_63b845ad): "owned but on a retainer" must become an actual fetch,
/// and once the fetch lands, the very same cart must re-plan into real crafts instead of the same refusal.
/// <para>
/// <see cref="LazyCrafter.Adapters.Dispatch.RetainerFetch"/> itself is reflection into Artisan and cannot run here;
/// its member names are proved offline by <c>tests/LazyCrafter.GuardProbe</c> against the installed DLL. What IS
/// testable - and what actually broke in 0.1.1.0 - is the surrounding decision: does the plan produce a Retrieve,
/// does the post-fetch re-plan turn the deferral into a craft, and does a partial pull ask for the remainder only.
/// </para>
/// <para>
/// 0.1.3.0 adds the batch fetch (ONE Artisan bell session for the whole cart). Its queue decision - which
/// recipe rows to feed it - is pure and has its own suite: <see cref="RetainerBatchQueueTests"/>.
/// </para>
/// </summary>
internal static class RetrieveTests
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

    /// <summary>Moves stock from "elsewhere" into the bags, the way a successful fetch does.</summary>
    private sealed class MovableInventory : IInventory
    {
        private readonly Dictionary<uint, int> _bags = new();
        private readonly Dictionary<uint, List<StoredElsewhere>> _elsewhere = new();

        public MovableInventory Set(uint itemId, int count) { _bags[itemId] = count; return this; }

        public MovableInventory SetElsewhere(uint itemId, int count, string where)
        {
            if (!_elsewhere.TryGetValue(itemId, out var list)) _elsewhere[itemId] = list = new List<StoredElsewhere>();
            list.Add(new StoredElsewhere(where, count));
            return this;
        }

        /// <summary>What a retainer withdrawal does: n units leave the retainer and arrive in the bags.</summary>
        public MovableInventory Fetch(uint itemId, int n)
        {
            var left = n;
            if (_elsewhere.TryGetValue(itemId, out var list))
            {
                for (var i = 0; i < list.Count && left > 0; i++)
                {
                    var take = Math.Min(left, list[i].Quantity);
                    list[i] = list[i] with { Quantity = list[i].Quantity - take };
                    left -= take;
                }
                list.RemoveAll(e => e.Quantity <= 0);
                if (list.Count == 0) _elsewhere.Remove(itemId);
            }
            _bags[itemId] = _bags.GetValueOrDefault(itemId) + (n - left);
            return this;
        }

        public int Count(uint itemId) => CountInBags(itemId) + (_elsewhere.TryGetValue(itemId, out var l) ? l.Sum(e => e.Quantity) : 0);
        public int CountInBags(uint itemId) => _bags.GetValueOrDefault(itemId);
        public IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId) =>
            _elsewhere.TryGetValue(itemId, out var l) ? l : Array.Empty<StoredElsewhere>();
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        // ---- the shape of the fix: fetch, then the SAME cart plans into a craft.

        ("the 0.1.1.0 nag loop: same cart, same inventory, replanned twice -> identical refusal both times", () =>
        {
            // This is what Joey hit: pressing Dispatch again changed nothing because nothing had moved.
            var inv = new MovableInventory().SetElsewhere(World.Ingot, 2, "retainer Hussypants").SetElsewhere(World.Leather, 1, "retainer Hussypants");
            var a = Plan(inv, (World.SwordRecipe, 1));
            var b = Plan(inv, (World.SwordRecipe, 1));
            return a.Retrievals.Count == 2 && b.Retrievals.Count == 2 && a.Crafts.Count == 0 && b.Crafts.Count == 0;
        }),
        ("after the fetch actually lands, the same cart replans into a real craft and no retrievals", () =>
        {
            // The whole point of the card: the second plan is the one taken after Phase.Retrieve moved the stock.
            var inv = new MovableInventory().SetElsewhere(World.Ingot, 2, "retainer Hussypants").SetElsewhere(World.Leather, 1, "retainer Hussypants");
            var before = Plan(inv, (World.SwordRecipe, 1));
            foreach (var r in before.Retrievals) inv.Fetch(r.ItemId, r.Quantity);
            var after = Plan(inv, (World.SwordRecipe, 1));
            return before.Crafts.Count == 0 && before.Deferred.Count == 1
                && after.Retrievals.Count == 0 && after.Deferred.Count == 0
                && after.Crafts.Count == 1 && after.Crafts[0] is { RecipeId: World.SwordRecipe, Crafts: 1 }
                && after.HasWork;
        }),
        ("a partial pull leaves a Retrieve for the remainder only, not the original quantity", () =>
        {
            // RestockFromRetainers stops once one retainer's pass satisfies its bag check, so partials are normal.
            var inv = new MovableInventory().SetElsewhere(World.Ingot, 2, "retainer Hussypants").Set(World.Leather, 1);
            var first = Plan(inv, (World.SwordRecipe, 1));
            inv.Fetch(World.Ingot, 1);                                  // only one came back
            var second = Plan(inv, (World.SwordRecipe, 1));
            return first.Retrievals.Single().Quantity == 2
                && second.Retrievals.Single() is { ItemId: World.Ingot, Quantity: 1 }
                && second.Crafts.Count == 0;
        }),
        ("the execution-time guard clears once the fetch has landed (BagsShortfall goes empty)", () =>
        {
            var (graph, _, _) = Core();
            var row = graph.Row(World.SwordRecipe)!;
            var inv = new MovableInventory().SetElsewhere(World.Ingot, 2, "retainer Hussypants").Set(World.Leather, 1);
            var before = DispatchPlan.BagsShortfall(row, 1, inv);
            inv.Fetch(World.Ingot, 2);
            var after = DispatchPlan.BagsShortfall(row, 1, inv);
            return before.Count == 1 && before[0].Places == "retainer Hussypants" && after.Count == 0;
        }),
        ("Joey's cart shape: 107 runs, every material on one retainer -> one Retrieve per material at 107x scale", () =>
        {
            // Super-Ether x107 with all four mats on retainer Hussypants. Sword x107 = 214 Ingot + 107 Leather.
            var inv = new MovableInventory()
                .SetElsewhere(World.Ingot, 300, "retainer Hussypants")
                .SetElsewhere(World.Leather, 200, "retainer Hussypants");
            var p = Plan(inv, (World.SwordRecipe, 107));
            var ingot = p.Retrievals.Single(r => r.ItemId == World.Ingot);
            var leather = p.Retrievals.Single(r => r.ItemId == World.Leather);
            if (ingot.Quantity != 214 || leather.Quantity != 107) return false;
            if (ingot.Places != "retainer Hussypants") return false;
            foreach (var r in p.Retrievals) inv.Fetch(r.ItemId, r.Quantity);
            var after = Plan(inv, (World.SwordRecipe, 107));
            // "crafts finished 107/107", not "0/0, 4 still to retrieve": one Artisan craft of 107 runs.
            return after.Retrievals.Count == 0 && after.Crafts.Count == 1 && after.Crafts[0].Crafts == 107;
        }),
        ("stock in the saddlebag is still a Retrieve but names the saddlebag - a bell cannot reach it", () =>
        {
            // The executor refuses this one with a reason instead of pretending; the plan must still surface it.
            var inv = new MovableInventory().SetElsewhere(World.Ingot, 2, "the chocobo saddlebag").Set(World.Leather, 1);
            var p = Plan(inv, (World.SwordRecipe, 1));
            var r = p.Retrievals.Single();
            return r.Places == "the chocobo saddlebag" && r.Detail == "2 in the chocobo saddlebag" && p.Crafts.Count == 0;
        }),
        ("a one-item retrieve-only plan is not empty and is not craft work", () =>
        {
            // What /lcraft fetch and the tree's Retrieve button build.
            var p = new DispatchPlan.Plan([], [], [], [], [], [], [], [new DispatchPlan.Retrieve(World.Ingot, 5, [new StoredElsewhere("retainer Hussypants", 5)])]);
            return !p.IsEmpty && !p.HasWork && p.Retrievals.Single().Detail == "5 on retainer Hussypants";
        }),
        ("everything already in the bags -> no Retrieve, byte-identical to the pre-card behaviour", () =>
        {
            var inv = new MovableInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            var p = Plan(inv, (World.SwordRecipe, 1));
            return p.Retrievals.Count == 0 && p.Crafts.Count == 1 && p.Deferred.Count == 0 && p.HasWork;
        }),
    };
}
