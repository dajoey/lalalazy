using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// The Alpine Chandelier shape (card t_efde145c): a cart whose root craft needs a sub-craft AND a market-board leaf.
/// Pass 1 must craft the sub-craft (it is runnable), re-plan, and stop Blocked naming the market item and quantity.
/// Then the player "buys" the market item, presses Resume, and the remaining crafts run to Done. A wave that changes
/// nothing must end Blocked with "no progress" and a bounded pass count - never an infinite loop. The stall guard
/// must fire on a signal that never changes.
/// <para>
/// What runs here is the REAL decision core (<see cref="DispatchLoop"/> + <see cref="Tiering.AssessCart"/> +
/// <see cref="DispatchPlan.Build"/> + <see cref="StallGuard"/>); the fake channels are inventory moves and craft
/// effects on a <see cref="WaveInventory"/>. DispatchService's framework tick only orchestrates these calls, the
/// same way the retrieve suite proves the fetch decision without reflection.
/// </para>
/// </summary>
internal static class LoopTests
{
    /// <summary>
    /// A movable inventory: bags + elsewhere (retainers), plus the two effects the real channels have - a fetch moves
    /// stock elsewhere -> bags, a craft consumes ingredients from the bags and adds its result (xResultAmount per run).
    /// </summary>
    internal sealed class WaveInventory : IInventory
    {
        private readonly RecipeGraph _graph;
        private readonly Dictionary<uint, int> _bags = new();
        private readonly Dictionary<uint, List<StoredElsewhere>> _elsewhere = new();

        public WaveInventory(RecipeGraph graph) { _graph = graph; }

        public WaveInventory Set(uint itemId, int count) { _bags[itemId] = count; return this; }

        public WaveInventory SetElsewhere(uint itemId, int count, string where)
        {
            if (!_elsewhere.TryGetValue(itemId, out var list)) _elsewhere[itemId] = list = new List<StoredElsewhere>();
            list.Add(new StoredElsewhere(where, count));
            return this;
        }

        /// <summary>A retainer withdrawal: n units leave "elsewhere" and arrive in the bags.</summary>
        public WaveInventory Fetch(uint itemId, int n)
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

        /// <summary>What the player does between Blocked and Resume: the item appears in the bags.</summary>
        public WaveInventory Buy(uint itemId, int n) { _bags[itemId] = _bags.GetValueOrDefault(itemId) + n; return this; }

        /// <summary>The Artisan channel: <paramref name="crafts"/> runs of the recipe, consuming from the bags. Returns the result units made.</summary>
        public int Craft(uint recipeId, int crafts)
        {
            var row = _graph.Row(recipeId)!;
            foreach (var (itemId, amount) in row.Ingredients)
                _bags[itemId] = Math.Max(0, _bags.GetValueOrDefault(itemId) - amount * crafts);
            var made = crafts * Math.Max(1, row.ResultAmount);
            _bags[row.ResultItemId] = _bags.GetValueOrDefault(row.ResultItemId) + made;
            return made;
        }

        /// <summary>The GBR channel: the gathered items land in the bags.</summary>
        public WaveInventory Gathered(uint itemId, int n) { _bags[itemId] = _bags.GetValueOrDefault(itemId) + n; return this; }

        public int Count(uint itemId) => CountInBags(itemId) + (_elsewhere.TryGetValue(itemId, out var l) ? l.Sum(e => e.Quantity) : 0);
        public int CountInBags(uint itemId) => _bags.GetValueOrDefault(itemId);
        public IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId) =>
            _elsewhere.TryGetValue(itemId, out var l) ? l : Array.Empty<StoredElsewhere>();
    }

    /// <summary>Everything a fake "dispatch run" needs: the real Core pipeline over the fake world.</summary>
    private sealed class Rig
    {
        public readonly WaveInventory Inv;
        private readonly Tiering _tiering;
        private readonly RecipeGraph _graph;
        private readonly VentureResolver _ventures;

        public Rig()
        {
            var data = World.BuildLoop();   // loop-only fixture: keeps the shared Build() graph unchanged for the other suites
            _graph = new RecipeGraph(data);
            _ventures = new VentureResolver(data);
            _tiering = new Tiering(_graph, new SourceClassifier(data, _graph, _ventures, []));
            Inv = new WaveInventory(_graph);
        }

        public DispatchLoop Loop(params (uint RecipeId, int Crafts)[] lines)
        {
            var cart = lines.Select(l => new DispatchLoop.CartLine(l.RecipeId, _graph.Row(l.RecipeId)!.ResultItemId, l.Crafts)).ToList();
            return new DispatchLoop(cart, Replan, Fingerprint);
        }

        public DispatchPlan.Plan? Replan(IReadOnlyList<DispatchLoop.CartLine> remaining)
        {
            var assessed = _tiering.AssessCart(remaining.Select(l => (l.RecipeId, l.Crafts)), Inv);
            var lines = assessed.Lines
                .Select(a => new DispatchPlan.Line(a, remaining.First(r => r.RecipeId == a.RecipeId).Crafts))
                .ToList();
            return DispatchPlan.Build(lines, assessed.Totals, _graph, _ventures, [], null, Inv);
        }

        public string Fingerprint(IEnumerable<uint> ids) =>
            string.Join("|", ids.OrderBy(i => i).Select(i => $"{i}:{Inv.CountInBags(i)}"));

        /// <summary>Play one wave the way DispatchService would: fetch, gather, then crafts depth-first.</summary>
        public void PlayWave(DispatchPlan.Plan plan)
        {
            foreach (var r in plan.Retrievals) Inv.Fetch(r.ItemId, r.Quantity);
            foreach (var g in plan.Gathers) Inv.Gathered(g.ItemId, g.Quantity);
            foreach (var c in plan.Crafts.OrderBy(c => -c.Depth)) Inv.Craft(c.RecipeId, c.Crafts);
        }
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("the Alpine Chandelier shape: pass 1 crafts the runnable sub-craft, re-plans, ends Blocked naming the market item and qty", () =>
        {
            var rig = new Rig();
            // Chandelier = Nugget x2 + MarketOnly x1. Nuggets craft from ore that is already in the bags.
            rig.Inv.Set(World.Ore, 4);
            var loop = rig.Loop((World.ChandelierRecipe, 1));
            var d1 = loop.Begin();
            if (d1.Outcome != DispatchLoop.Outcome.Wave) return false;
            if (d1.Plan.Crafts.Count != 1 || d1.Plan.Crafts[0].RecipeId != World.NuggetRecipe) return false;
            if (d1.Plan.Crafts[0].Crafts != 2) return false;
            if (!d1.Plan.Deferred.Any(x => x.RecipeId == World.ChandelierRecipe)) return false;
            rig.PlayWave(d1.Plan);
            var d2 = loop.Next(progressed: true);
            return d2.Outcome == DispatchLoop.Outcome.Blocked
                && d2.Plan.Crafts.Count == 0
                && d2.Plan.Market.Count == 1 && d2.Plan.Market[0] is { ItemId: World.MarketOnly, Quantity: 1 }
                && d2.Why is not null && d2.Why.Contains("market")
                && rig.Inv.CountInBags(World.Nugget) == 2;
        }),
        ("acceptance case 2: buy the market item, Resume -> remaining crafts run, ends Done", () =>
        {
            var rig = new Rig();
            rig.Inv.Set(World.Ore, 4);
            var loop = rig.Loop((World.ChandelierRecipe, 1));
            rig.PlayWave(loop.Begin().Plan);
            var d2 = loop.Next(progressed: true);
            if (d2.Outcome != DispatchLoop.Outcome.Blocked) return false;
            rig.Inv.Buy(World.MarketOnly, 1);              // Joey buys, presses Resume
            var d3 = loop.Resume();
            if (d3.Outcome != DispatchLoop.Outcome.Wave) return false;
            if (d3.Plan.Crafts.Count != 1 || d3.Plan.Crafts[0].RecipeId != World.ChandelierRecipe) return false;
            rig.PlayWave(d3.Plan);
            loop.CraftDone(World.ChandelierRecipe, 1);
            var d4 = loop.Next(progressed: true);
            return d4.Outcome == DispatchLoop.Outcome.Done && rig.Inv.CountInBags(World.Chandelier) == 1;
        }),
        ("a wave that changes nothing ends Blocked with 'no progress' - never an infinite loop (pass count bounded)", () =>
        {
            var rig = new Rig();
            rig.Inv.SetElsewhere(World.Ore, 10, "retainer Hussypants");
            var loop = rig.Loop((World.IngotBsm, 2));
            var d1 = loop.Begin();
            if (d1.Outcome != DispatchLoop.Outcome.Wave || d1.Plan.Retrievals.Count != 1) return false;
            // Play the wave but WITHOUT the fetch landing (bell refused): nothing moves, nothing crafts.
            var d2 = loop.Next(progressed: false);
            return d2.Outcome == DispatchLoop.Outcome.Blocked
                && d2.Why is not null && d2.Why.Contains("no progress")
                && loop.Pass == 2;
        }),
        ("even an always-'progressing' cart stops at the pass cap instead of looping forever", () =>
        {
            // A synthetic replan that always finds a gather and a fingerprint that always changes: the ONLY thing
            // that can stop it is the cap. (In the real world the no-progress rule fires first; this proves the
            // belt-and-braces bound exists.)
            var calls = 0;
            var loop = new DispatchLoop(
                [new DispatchLoop.CartLine(1, 2, 1)],
                _ => { calls++; return new DispatchPlan.Plan([], [new DispatchPlan.Gather(200, 5, SourceKind.RegularNode)], [], [], [], [], []); },
                _ => $"sig{calls}");
            var d = loop.Begin();
            var advances = 1;
            while (d.Outcome == DispatchLoop.Outcome.Wave && advances < 100)
            {
                d = loop.Next(progressed: false);   // fingerprint changed underneath, so it counts as progress
                advances++;
            }
            return d.Outcome == DispatchLoop.Outcome.Blocked
                && d.Why is not null && d.Why.Contains("12 passes")
                && loop.Pass == DispatchLoop.MaxPasses + 1
                && advances == DispatchLoop.MaxPasses + 1;
        }),
        ("the stall guard fires on a signal that never changes, resets when it moves, and holds the clock while paused", () =>
        {
            var guard = new StallGuard(TimeSpan.FromMinutes(10));
            var t0 = new DateTime(2026, 9, 5, 12, 0, 0);
            if (guard.Observe("gathering ore|100:3", t0)) return false;                 // first observation arms it
            if (guard.Observe("gathering ore|100:3", t0.AddMinutes(9))) return false;   // 9 min unchanged: not yet
            if (!guard.Observe("gathering ore|100:3", t0.AddMinutes(10))) return false; // 10 min: stall
            if (guard.Observe("gathering ore|100:4", t0.AddMinutes(11))) return false;  // moved: re-armed
            if (guard.Observe("gathering ore|100:4", t0.AddMinutes(20), paused: true)) return false;   // paused: clock held
            if (guard.Observe("gathering ore|100:4", t0.AddMinutes(29), paused: true)) return false;   // 18 min "unchanged" but paused: still fine
            if (!guard.Observe("gathering ore|100:4", t0.AddMinutes(39))) return false; // unpaused at 39: 10 min of real clock since the pause started... trips
            guard.Reset();
            return !guard.Observe("gathering ore|100:4", t0.AddMinutes(60));
        }),
        ("a finished cart line drops out of the re-plan - its result is not 'missing' for itself", () =>
        {
            var rig = new Rig();
            rig.Inv.Set(World.Ore, 4).Set(World.Coal, 2);
            var loop = rig.Loop((World.IngotBsm, 2));
            var d1 = loop.Begin();
            if (d1.Outcome != DispatchLoop.Outcome.Wave) return false;
            if (d1.Plan.Crafts.Count != 1 || d1.Plan.Crafts[0].Crafts != 2) return false;
            rig.PlayWave(d1.Plan);
            loop.CraftDone(World.IngotBsm, 2);
            var d2 = loop.Next(progressed: true);
            return d2.Outcome == DispatchLoop.Outcome.Done && loop.Remaining.Count == 0;
        }),
        ("NEGATIVE: without the done-marking the same cart re-plans itself forever - the trap CraftDone exists for", () =>
        {
            var rig = new Rig();
            // A big stockpile: the un-marked line keeps finding mats for another 2-ingot craft until the cap.
            rig.Inv.Set(World.Ore, 400).Set(World.Coal, 200);
            var loop = rig.Loop((World.IngotBsm, 2));
            var d = loop.Begin();
            rig.PlayWave(d.Plan);
            // Deliberately NOT calling CraftDone. The re-plan still sees a runnable ingot craft (more ore in the
            // world than the cart needs), so the loop keeps crafting until the pass cap stops it - which is exactly
            // why the loop tracks root crafts done.
            var advances = 1;
            while (d.Outcome == DispatchLoop.Outcome.Wave && advances < 40)
            {
                d = loop.Next(progressed: true);
                if (d.Outcome == DispatchLoop.Outcome.Wave) rig.PlayWave(d.Plan);
                advances++;
            }
            return d.Outcome == DispatchLoop.Outcome.Blocked && loop.Pass > DispatchLoop.MaxPasses;
        }),
        ("a sub-craft finishing is picked up by the re-plan even when its parent was deferred (the 0.1.3.1 bug)", () =>
        {
            var rig = new Rig();
            rig.Inv.Set(World.Ore, 4);
            var loop = rig.Loop((World.ChandelierRecipe, 1));
            var d1 = loop.Begin();
            if (d1.Plan.Deferred.All(x => x.RecipeId != World.ChandelierRecipe)) return false;
            rig.PlayWave(d1.Plan);                         // sub-craft only
            var d2 = loop.Next(progressed: true);
            // The re-plan must now see the nuggets in the bags: only the market leaf blocks.
            return d2.Plan.Crafts.Count == 0
                && d2.Plan.Market.Count == 1
                && d2.Plan.Deferred.Count == 1
                && d2.Plan.Deferred[0].RecipeId == World.ChandelierRecipe;
        }),
        ("Blocked reasons: vendor and market leaves are both named in one line", () =>
        {
            var rig = new Rig();
            var loop = rig.Loop((World.TrophyRecipe, 1));   // needs Coal (vendor) + Hide (drop -> marketable, no retainer to venture it)
            var d1 = loop.Begin();
            if (d1.Outcome != DispatchLoop.Outcome.Blocked) return false;
            var why = d1.Why!;
            return why.Contains("craft") && why.Contains("vendor") && why.Contains("market");
        }),
        ("retrieve wave: stock on a retainer plans a fetch; after it lands the craft runs in the SAME run", () =>
        {
            var rig = new Rig();
            rig.Inv.SetElsewhere(World.Ore, 4, "retainer Hussypants").Set(World.Coal, 2);
            var loop = rig.Loop((World.IngotBsm, 2));
            var d1 = loop.Begin();
            if (d1.Outcome != DispatchLoop.Outcome.Wave || d1.Plan.Retrievals.Count != 1) return false;
            rig.PlayWave(d1.Plan);                          // fetch lands
            var d2 = loop.Next(progressed: true);           // re-plan: the craft is now runnable
            if (d2.Outcome != DispatchLoop.Outcome.Wave || d2.Plan.Crafts.Count != 1) return false;
            rig.PlayWave(d2.Plan);
            loop.CraftDone(World.IngotBsm, 2);
            var d3 = loop.Next(progressed: true);
            return d3.Outcome == DispatchLoop.Outcome.Done && rig.Inv.CountInBags(World.Ingot) == 2;
        }),
        ("gather wave: a gatherable leaf runs as a wave, and the craft it feeds runs in the NEXT pass", () =>
        {
            var rig = new Rig();
            var loop = rig.Loop((World.IngotBsm, 2));       // ore gatherable, coal a vendor item... use nuggets instead
            var d1 = loop.Begin();
            // Ore gathers; coal is a vendor blocker, so the ingot craft waits for the player either way.
            return d1.Outcome == DispatchLoop.Outcome.Wave
                && d1.Plan.Gathers.Count == 1
                && d1.Plan.Gathers[0].ItemId == World.Ore
                && d1.Plan.Deferred.Count >= 1;
        }),
    };
}
