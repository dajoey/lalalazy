using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// V1 verify-card adversarial cases (skeptic lane, independent of the P1/P2 builder).
/// Required by the plan: recipe cycle, zero velocity, HQ-only recipe. Each is pushed through
/// the WHOLE Core pipeline (Expand / HowMany / Tiering / ProfitModel / Rank), not just the one
/// class the builder tested. The "[probe]" cases document boundary behaviour the adapters must
/// respect; they are expectations, not builder claims.
/// </summary>
internal static class AdversarialTests
{
    // Extra items/recipes that World does not have. Ids chosen clear of World's ranges.
    private const uint CycA = 1000, CycB = 1001, CycC = 1002, Loop = 1003, Catalyst = 1004;
    private const uint CycARecipe = 1000, CycBRecipe = 1001, CycCRecipe = 1002, LoopRecipe = 1003;
    private const uint HqGear = 1100, HqGearRecipe = 1100;
    private const uint HqMatGear = 1101, HqMatGearRecipe = 1101;

    private static FakeGameData Data() => World.Build()
        // 3-node cycle A -> B -> C -> A (every result is also an ingredient further round).
        .Recipe(CycARecipe, CycA, 1, World.Bsm, 1, (CycB, 1))
        .Recipe(CycBRecipe, CycB, 1, World.Bsm, 1, (CycC, 1))
        .Recipe(CycCRecipe, CycC, 1, World.Bsm, 1, (CycA, 1))
        // Self-loop: Loop = 1 Loop + 1 Catalyst (a "refine" shape).
        .Recipe(LoopRecipe, Loop, 1, World.Bsm, 1, (Loop, 1), (Catalyst, 1))
        // HQ-only result: sells only HQ. Mats are Ingot (craftable/market) + Coal (vendor).
        .Recipe(HqGearRecipe, HqGear, 1, World.Bsm, 50, (World.Ingot, 1), (World.Coal, 1))
        // HQ-only material: MarketOnly is only ever listed HQ on the board.
        .Recipe(HqMatGearRecipe, HqMatGear, 1, World.Bsm, 50, (World.MarketOnly, 1), (World.Coal, 1))
        .Marketable(CycA).Marketable(HqGear).Marketable(HqMatGear);

    private static PriceQuote Q(uint item, long? minNq, double velNq = 1, int listings = 10,
        long? minHq = null, double velHq = 0) =>
        new(item, minNq, minHq, minNq, minHq, minNq, minHq, velNq, velHq, listings, null);

    private static (FakeGameData data, RecipeGraph graph, ProfitModel model, Tiering tiering) Rig()
    {
        var data = Data();
        var graph = new RecipeGraph(data);
        var classifier = new SourceClassifier(data, graph, new VentureResolver(data), Array.Empty<RetainerStats>());
        return (data, graph, new ProfitModel(data, graph), new Tiering(graph, classifier));
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("recipe cycle / Expand: 3-node ring A->B->C->A expands one lap with the back edge cut; self-loop ingredient is a plain leaf", () =>
        {
            var (_, g, _, _) = Rig();
            var a = g.Expand(CycARecipe)!;
            var b = a.Ingredients.Single().SubRecipe!;
            var c = b.Ingredients.Single().SubRecipe!;
            var backEdge = c.Ingredients.Single();
            var finite = a.RecipeId == CycARecipe && b.RecipeId == CycBRecipe && c.RecipeId == CycCRecipe
                && backEdge.ItemId == CycA && backEdge.SubRecipe is null;
            var loop = g.Expand(LoopRecipe)!;
            var selfLeaf = loop.Ingredients.Single(i => i.ItemId == Loop);
            return finite && selfLeaf.SubRecipe is null && loop.Ingredients.Count == 2;
        }),
        ("recipe cycle / HowMany: ring with nothing -> 0; 2 C on hand -> 2 A via B; self-loop needs a Loop to make a Loop (Catalyst alone -> 0, +3 Loop -> 3)", () =>
        {
            var (_, g, _, _) = Rig();
            return g.HowMany(CycARecipe, new FakeInventory()) == 0
                && g.HowMany(CycARecipe, new FakeInventory().Set(CycC, 2)) == 2
                && g.HowMany(LoopRecipe, new FakeInventory().Set(Catalyst, 5)) == 0
                && g.HowMany(LoopRecipe, new FakeInventory().Set(Catalyst, 5).Set(Loop, 3)) == 3;
        }),
        ("recipe cycle / Tiering: closed unmarketable ring -> Blocked; ring whose head is marketable -> SomeEffort (buy one to make one); 1 C on hand -> Easy, HowMany 1", () =>
        {
            var (_, _, _, t) = Rig();
            var blocked = t.Assess(World.CycleARecipe, new FakeInventory());
            var viaMarket = t.Assess(CycARecipe, new FakeInventory());
            var eased = t.Assess(CycARecipe, new FakeInventory().Set(CycC, 1));
            return blocked.Tier == EffortTier.Blocked && blocked.HowMany == 0
                && viaMarket.Tier == EffortTier.SomeEffort && viaMarket.HowMany == 0
                && eased.Tier == EffortTier.Easy && eased.HowMany == 1;
        }),
        ("recipe cycle / Profit: A sells 1000 and the only priced leaf in A->B->C->A is A itself -> cash cost 1000, margin 0, per-day 0; circularity does not inflate margin", () =>
        {
            var (_, _, m, _) = Rig();
            var prices = new FakePrices().Set(Q(CycA, 1_000, velNq: 5, listings: 3));
            var e = m.Evaluate(CycARecipe, new FakeInventory(), prices, 0)!;
            return e.CashCost == 1_000 && e.MarketCost == 1_000 && e.MarginCash == 0 && e.CostComplete && e.PerDay == 0;
        }),

        ("zero velocity: 0 / negative velocity -> per-day 0 and +Inf saturation on both cash-unbounded and stock-capped paths; ranks below any positive-velocity item; ties break on margin", () =>
        {
            var (_, _, m, _) = Rig();
            var inv = new FakeInventory().Set(World.Coal, 200).Set(World.Hide, 100).Set(World.Ingot, 10).Set(World.Leather, 10);

            // Unbounded-capacity path (Sword: every mat purchasable) at velocity 0 -> min(+Inf, 0) must be 0, not NaN.
            var swordMarket = new FakePrices()
                .Set(Q(World.Sword, 10_000, velNq: 0, listings: 7))
                .Set(Q(World.Ingot, 1_000)).Set(Q(World.Leather, 500)).Set(Q(World.Ore, 10));
            var sword = m.Evaluate(World.SwordRecipe, inv, swordMarket, 5)!;
            var unbounded = sword.MarginCash == 9_500 && sword.PerDay == 0 && !double.IsNaN(sword.PerDay)
                && double.IsPositiveInfinity(sword.SaturationDays) && sword.Velocity == 0;

            // Stock-capped path (Trophy: Hide is drop-only) with a NEGATIVE velocity (bad upstream data).
            var neg = m.Evaluate(World.TrophyRecipe, inv, new FakePrices().Set(Q(World.Trophy, 40_000, velNq: -3, listings: 2)), 0)!;
            var negative = neg.PerDay == 0 && double.IsPositiveInfinity(neg.SaturationDays) && neg.MarginCash == 40_000;

            // HQ row with zero HQ velocity while NQ sells fast -> the HQ row's per-day is 0 (rows are independent).
            var hq = m.Evaluate(World.TrophyRecipe, inv, new FakePrices().Set(Q(World.Trophy, 1_000, velNq: 50, listings: 2, minHq: 90_000, velHq: 0)), 0, hq: true)!;
            var hqZero = hq.MarginCash == 90_000 && hq.PerDay == 0 && hq.Velocity == 0;

            // Ranking: 40k margin at velocity 0 sits below 100 margin at velocity 0.5; two dead items tie-break on margin.
            var prices = new FakePrices()
                .Set(Q(World.Trophy, 40_000, velNq: 0, listings: 1))
                .Set(Q(World.Ornament, 100, velNq: 0.5, listings: 1))
                .Set(Q(World.Sword, 5_000, velNq: 0, listings: 1))
                .Set(Q(World.Ingot, 1_000)).Set(Q(World.Leather, 500)).Set(Q(World.Ore, 10));
            var inv2 = new FakeInventory().Set(World.Coal, 200).Set(World.Hide, 100).Set(World.RareOre, 100).Set(World.Ingot, 10).Set(World.Leather, 10);
            var ranked = ProfitModel.Rank([
                m.Evaluate(World.TrophyRecipe, inv2, prices, 0)!,
                m.Evaluate(World.OrnamentRecipe, inv2, prices, 0)!,
                m.Evaluate(World.SwordRecipe, inv2, prices, 0)!]).Select(x => x.RecipeId).ToList();
            var order = ranked.SequenceEqual([World.OrnamentRecipe, World.TrophyRecipe, World.SwordRecipe]);

            // Undersupplied: a dead item (velocity 0, 0 listings) is NOT "undersupplied" at the default threshold.
            var dead = new UndersuppliedFinder(Data(), new RecipeGraph(Data()))
                .Find([World.Trophy], new FakePrices().Set(Q(World.Trophy, 1_000, velNq: 0, listings: 0)));
            var notUndersupplied = !dead.Any();

            return unbounded && negative && hqZero && order && notUndersupplied;
        }),

        ("HQ-only recipe: result listed HQ only -> NQ row revenue unknown (null margins, per-day 0, cost still computed); HQ row prices/velocity from HQ fields; Rank puts the HQ row first", () =>
        {
            var (_, _, m, _) = Rig();
            var inv = new FakeInventory().Set(World.Ingot, 1).Set(World.Coal, 1);
            // HqGear: no NQ listing at all, HQ min 50k, HQ velocity 3, NQ velocity 0.
            var prices = new FakePrices()
                .Set(Q(HqGear, minNq: null, velNq: 0, listings: 4, minHq: 50_000, velHq: 3))
                .Set(Q(World.Ingot, 1_000));

            var nq = m.Evaluate(HqGearRecipe, inv, prices, 5, hq: false)!;
            var hq = m.Evaluate(HqGearRecipe, inv, prices, 5, hq: true)!;

            var nqOk = !nq.RevenueKnown && nq.RevenueNq is null && nq.RevenueHq == 50_000
                && nq.MarginCash is null && nq.MarginMarket is null && nq.PerDay == 0 && nq.Tax == 0
                && nq.CashCost == 0 && nq.MarketCost == 1_003 && nq.CostComplete;   // costs still computed: Ingot 1000 + Coal 3
            var hqOk = hq.RevenueKnown && hq.RevenueHq == 50_000 && hq.Tax == 2_500
                && hq.MarginCash == 47_500 && hq.MarginMarket == 46_497
                && hq.Velocity == 3 && Math.Abs(hq.PerDay - 47_500 * 3) < 1e-6   // all mats purchasable -> velocity-capped
                && Math.Abs(hq.SaturationDays - 4.0 / 3.0) < 1e-9;
            var ranked = ProfitModel.Rank([nq, hq]).ToList();
            var rankOk = ranked[0].Hq && !ranked[1].Hq;

            // Zero crafts / a recipe with no ingredients edge: crafts <= 0 -> null, never a divide-by-zero.
            var guard = m.Evaluate(HqGearRecipe, inv, prices, 5, hq: true, crafts: 0) is null;

            return nqOk && hqOk && rankOk && guard;
        }),

        // The two probes below FAILED against Core @ 7d9420bd2 on 2026-09-03 (V1 verify) and were fixed in
        // the follow-up card t_003d108b. They pin the adapter contract: an HQ unit satisfies an NQ ingredient
        // slot, and a PriceQuote velocity must be a finite non-negative number (missing -> 0, never NaN).
        ("[probe] HQ-only MATERIAL: a mat listed only HQ (NQ null, HQ 500) is still purchasable -> UnitCost 500, cost complete, per-day velocity-capped; NQ price wins whenever it exists, even if HQ is cheaper", () =>
        {
            var (_, _, m, _) = Rig();
            // MarketOnly (502) is neither craftable nor vendor-sold; the board only ever has HQ listings.
            var prices = new FakePrices()
                .Set(Q(HqMatGear, 10_000, velNq: 4, listings: 2))
                .Set(Q(World.MarketOnly, minNq: null, velNq: 0, minHq: 500, velHq: 2));
            var unit = m.UnitCost(World.MarketOnly, prices);
            var e = m.Evaluate(HqMatGearRecipe, new FakeInventory(), prices, 0)!;
            var hqOnly = unit == 500 && e.CostComplete && e.UnpricedItems.Count == 0
                && e.CashCost == 503 && e.MarketCost == 503 && e.MarginCash == 9_497
                && e.HowMany == 0                                          // nothing on hand ...
                && Math.Abs(e.PerDay - 9_497 * 4) < 1e-6;                  // ... but every mat is purchasable -> velocity-capped, not stock-capped

            // Decision (t_003d108b): the HQ columns are a FALLBACK for a missing NQ price, not a second bidder.
            // NQ 600 listed alongside HQ 500 -> 600 (the NQ market is what the cost column tracks).
            var both = new FakePrices().Set(Q(World.MarketOnly, minNq: 600, velNq: 1, minHq: 500, velHq: 1));
            var nqWins = m.UnitCost(World.MarketOnly, both) == 600;

            // A zero/absent HQ price is not a price either: NQ null + HQ 0 -> still unpriced.
            var zeroHq = new FakePrices().Set(Q(World.MarketOnly, minNq: null, velNq: 0, minHq: 0, velHq: 0));
            var stillUnpriced = m.UnitCost(World.MarketOnly, zeroHq) is null;

            return hqOnly && nqWins && stillUnpriced;
        }),

        ("[probe] NaN velocity from a broken quote never reaches PerDay/Saturation/Rank/Undersupplied (treated as 0)", () =>
        {
            var (_, _, m, _) = Rig();
            var inv = new FakeInventory().Set(World.Coal, 200).Set(World.Hide, 100).Set(World.RareOre, 100);
            var nan = m.Evaluate(World.TrophyRecipe, inv,
                new FakePrices().Set(Q(World.Trophy, 1_000, velNq: double.NaN, listings: 2)), 0)!;
            var perDayOk = !double.IsNaN(nan.PerDay) && nan.PerDay == 0 && nan.Velocity == 0
                && double.IsPositiveInfinity(nan.SaturationDays) && nan.MarginCash == 1_000;

            // +Inf is equally broken upstream data: nothing sells "infinitely", treat it as unknown -> 0.
            var inf = m.Evaluate(World.TrophyRecipe, inv,
                new FakePrices().Set(Q(World.Trophy, 1_000, velNq: double.PositiveInfinity, listings: 2)), 0)!;
            var infOk = !double.IsNaN(inf.PerDay) && inf.PerDay == 0 && inf.Velocity == 0 && double.IsPositiveInfinity(inf.SaturationDays);

            // Rank: the NaN row (margin 1000, per-day 0) sorts BELOW a real 0.5-velocity row and its position is deterministic.
            var prices = new FakePrices()
                .Set(Q(World.Trophy, 1_000, velNq: double.NaN, listings: 2))
                .Set(Q(World.Ornament, 100, velNq: 0.5, listings: 1));
            var ranked = ProfitModel.Rank([
                m.Evaluate(World.TrophyRecipe, inv, prices, 0)!,
                m.Evaluate(World.OrnamentRecipe, inv, prices, 0)!]).Select(x => x.RecipeId).ToList();
            var rankOk = ranked.SequenceEqual([World.OrnamentRecipe, World.TrophyRecipe])
                && ranked.SequenceEqual(ProfitModel.Rank([
                    m.Evaluate(World.OrnamentRecipe, inv, prices, 0)!,
                    m.Evaluate(World.TrophyRecipe, inv, prices, 0)!]).Select(x => x.RecipeId));

            // Undersupplied: NaN velocity with 0 listings must not pass the "velocity >= 3" gate through NaN comparisons.
            var dead = new UndersuppliedFinder(Data(), new RecipeGraph(Data()))
                .Find([World.Trophy], new FakePrices().Set(Q(World.Trophy, 1_000, velNq: double.NaN, listings: 0)));
            var notUndersupplied = !dead.Any();

            return perDayOk && infOk && rankOk && notUndersupplied;
        }),
    };
}
