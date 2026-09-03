using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class LevelingScoreTests
{
    private static LevelingScore L()
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var tiering = new Tiering(graph, new SourceClassifier(data, graph, new VentureResolver(data), []));
        return new LevelingScore(graph, tiering);
    }

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("formula: floor(floor(base/3) x mod/100) - lvl 20 recipe at job 20 = 947; at job 25 (diff 5) = 757; at job 41+ = 94", () =>
        {
            // base[20] = 2841 -> 947; x80% = 757 (floor 757.6); diff >= 21 -> x10% = 94
            return LevelingScore.ExpPerCraft(20, 20) == 947
                && LevelingScore.ExpPerCraft(25, 20) == 757
                && LevelingScore.ExpPerCraft(41, 20) == 94
                && LevelingScore.ExpPerCraft(99, 20) == 94;
        }),
        ("recipes above the job level pay as if equal level (diff clamps at 0); bad levels -> 0", () =>
        {
            return LevelingScore.ExpPerCraft(10, 20) == LevelingScore.ExpPerCraft(20, 20)
                && LevelingScore.LevelDifference(10, 20) == 0 && LevelingScore.LevelDifference(50, 20) == 21
                && LevelingScore.ExpPerCraft(20, 0) == 0 && LevelingScore.ExpPerCraft(20, 101) == 0 && LevelingScore.ExpPerCraft(0, 20) == 0;
        }),
        ("Evaluate: Sword (BSM lvl 20) for BSM 22 with mats on hand -> diff 2, 871 exp, first-craft bonus 2841, tier Now, eligible", () =>
        {
            var e = L().Evaluate(World.SwordRecipe, World.Bsm, 22, new FakeInventory().Set(World.Ingot, 4).Set(World.Leather, 2))!;
            return e.LevelDifference == 2 && e.ExpPerCraft == 871 && e.FirstCraftBonus == 2841
                && e.Tier == EffortTier.Now && e.HowMany == 2 && e.Eligible && e.ExpFromStock == 1742;
        }),
        ("gated on tier <= 1: a recipe needing a timed node (Ornament) is not eligible; wrong job -> null", () =>
        {
            var l = L();
            var orn = l.Evaluate(World.OrnamentRecipe, World.Bsm, 50, new FakeInventory().Set(World.Coal, 1))!;
            return orn.Tier == EffortTier.SomeEffort && !orn.Eligible
                && l.Evaluate(World.SwordRecipe, World.Ltw, 22, new FakeInventory()) is null;
        }),
        ("Rank for BSM 22 lists only BSM tier<=1 recipes at/below level, best exp first; includeAboveLevel adds lvl 50 Trophy", () =>
        {
            // Inventory: Ore (regular node -> tier 1 anyway), Coal vendor. Sword(20) 871 > Ingot(10) 213 > Arrows(5)/Cycle(1)...
            var inv = new FakeInventory().Set(World.Feather, 1).Set(World.Coal, 5).Set(World.Ore, 10).Set(World.Leather, 1).Set(World.Hide, 1);
            var ranked = L().Rank(World.Bsm, 22, inv).ToList();
            var withAbove = L().Rank(World.Bsm, 22, inv, includeAboveLevel: true).ToList();
            return ranked.Count > 0 && ranked[0].RecipeId == World.SwordRecipe
                && ranked.All(e => e.JobId == World.Bsm && e.RecipeLevel <= 22 && e.Tier <= EffortTier.Easy)
                && ranked.Zip(ranked.Skip(1)).All(p => p.First.ExpPerCraft >= p.Second.ExpPerCraft)
                && !ranked.Any(e => e.RecipeId == World.TrophyRecipe)
                && withAbove.Any(e => e.RecipeId == World.TrophyRecipe);
        }),
    };
}

internal static class UndersuppliedFinderTests
{
    private static PriceQuote Q(uint item, double velNq, int listings, double velHq = 0) =>
        new(item, 100, null, 100, null, 100, null, velNq, velHq, listings, null);

    private static UndersuppliedFinder F(double minVel = 3, int maxList = 2) =>
        new(World.Build(), new RecipeGraph(World.Build())) { MinVelocity = minVel, MaxListings = maxList };

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("velocity >= X and listings <= Y, craftable and marketable only; ordered by velocity desc", () =>
        {
            var prices = new FakePrices()
                .Set(Q(World.Sword, 10, 1))        // hit
                .Set(Q(World.Ingot, 5, 2))         // hit (boundary listings)
                .Set(Q(World.Ore, 50, 0))          // marketable but not craftable -> dropped when craftableOnly
                .Set(Q(World.Coal, 2.9, 0))        // below velocity
                .Set(Q(World.MarketOnly, 20, 3));  // too many listings
            var hits = F().FindCraftable(prices).ToList();
            var any = F().Find([World.Sword, World.Ingot, World.Ore, World.Coal, World.MarketOnly], prices, craftableOnly: false).ToList();
            return hits.Select(h => h.ItemId).SequenceEqual([World.Sword, World.Ingot])
                && any.Select(h => h.ItemId).SequenceEqual([World.Ore, World.Sword, World.Ingot])
                && hits[0].RecipeId == World.SwordRecipe && any[0].RecipeId is null;
        }),
        ("NQ + HQ velocity are summed; recipe honours preferJob; saturation = listings / velocity", () =>
        {
            var prices = new FakePrices().Set(Q(World.Ingot, 2, 2, velHq: 2));
            var bsm = F().Find([World.Ingot], prices, preferJob: World.Bsm).Single();
            var arm = F().Find([World.Ingot], prices, preferJob: World.Arm).Single();
            return bsm.Velocity == 4 && bsm.RecipeId == World.IngotBsm && arm.RecipeId == World.IngotArm
                && Math.Abs(bsm.SaturationDays - 0.5) < 1e-9;
        }),
        ("unpriced, untradeable (Leather) and duplicate candidates are skipped; thresholds are configurable", () =>
        {
            var prices = new FakePrices().Set(Q(World.Leather, 99, 0)).Set(Q(World.Sword, 1, 5));
            var strict = F().Find([World.Leather, World.Sword, World.Sword, World.Trophy], prices).ToList();
            var loose = F(minVel: 1, maxList: 5).Find([World.Sword, World.Sword], prices).ToList();
            return strict.Count == 0 && loose.Count == 1 && loose[0].ItemId == World.Sword;
        }),
    };
}

internal static class CraftingLogFilterTests
{
    private sealed class FakeLog : ICraftingLog
    {
        private readonly HashSet<uint> _done = new();
        public FakeLog Done(params uint[] ids) { _done.UnionWith(ids); return this; }
        public bool IsRecipeComplete(uint recipeId) => _done.Contains(recipeId);
    }

    private static CraftingLogFilter C(FakeLog log) => new(new RecipeGraph(World.Build()), log);

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("notYetCrafted: false for completed and for unknown recipes, true otherwise; Predicate works in LINQ", () =>
        {
            var c = C(new FakeLog().Done(World.SwordRecipe));
            var ids = new uint[] { World.SwordRecipe, World.IngotBsm, 99999 };
            return !c.NotYetCrafted(World.SwordRecipe) && c.NotYetCrafted(World.IngotBsm) && !c.NotYetCrafted(99999)
                && ids.Where(c.Predicate).SequenceEqual([World.IngotBsm]);
        }),
        ("Remaining per job, level-capped, level order; Progress counts done/total", () =>
        {
            var c = C(new FakeLog().Done(World.SwordRecipe, World.LeatherLtw));
            var bsm20 = c.Remaining(World.Bsm, maxLevel: 20).ToList();
            // BSM <= 20 not done: Cycle A/B (1), Arrows (5), IngotBsm (10); Sword (20) is done.
            return bsm20.SequenceEqual([World.CycleARecipe, World.CycleBRecipe, World.ArrowsRecipe, World.IngotBsm])
                && c.Progress(World.Ltw) == (1, 1) && c.Progress(World.Bsm).Done == 1
                && c.Progress().Total == 11 && c.Progress().Done == 2;
        }),
        ("Remaining cheapest-first when a cost function is supplied; unknown cost sorts last", () =>
        {
            var c = C(new FakeLog());
            long? Cost(uint id) => id switch { World.ArrowsRecipe => 5, World.IngotBsm => 1, World.SwordRecipe => null, _ => 100 };
            var r = c.Remaining(World.Bsm, maxLevel: 20, Cost).ToList();
            return r[0] == World.IngotBsm && r[1] == World.ArrowsRecipe && r[^1] == World.SwordRecipe && r.Count == 5;
        }),
    };
}
