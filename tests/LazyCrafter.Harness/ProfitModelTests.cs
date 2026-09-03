using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class ProfitModelTests
{
    private static PriceQuote Q(uint item, long? minNq, double velNq = 1, int listings = 10,
        long? minHq = null, long? medNq = null, long? avgNq = null, double velHq = 0) =>
        new(item, minNq, minHq, medNq ?? minNq, null, avgNq ?? minNq, null, velNq, velHq, listings, null);

    private static ProfitModel M(FakeGameData? data = null, RevenueBasis basis = RevenueBasis.MinListing)
    {
        data ??= World.Build();
        return new ProfitModel(data, new RecipeGraph(data)) { Basis = basis };
    }

    // Sword = 2 Ingot + 1 Leather; Ingot(BSM) = 2 Ore + 1 Coal; Coal is a 3-gil vendor item.
    private static FakePrices SwordMarket() => new FakePrices()
        .Set(Q(World.Sword, 10_000, velNq: 5, listings: 20))
        .Set(Q(World.Ingot, 1_000))
        .Set(Q(World.Leather, 500))
        .Set(Q(World.Ore, 10))
        .Set(Q(World.Coal, 50));

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("everything on hand -> cash cost 0, market cost prices every unit; both margins present", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            var e = M().Evaluate(World.SwordRecipe, inv, SwordMarket(), taxPct: 0)!;
            // market: 2 Ingot -> min(buy 2000, craft 2x(2x10 + 3)) = 46; Leather 500 -> 546
            return e.CashCost == 0 && e.MarketCost == 546
                && e.RevenueNq == 10_000 && e.MarginCash == 10_000 && e.MarginMarket == 9_454
                && e.CostComplete && e.Units == 1 && e.HowMany == 1;
        }),
        ("cash cost prices only the missing units (1 of 2 Ingot on hand -> one Ingot's worth)", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 1).Set(World.Leather, 1);
            var e = M().Evaluate(World.SwordRecipe, inv, SwordMarket(), taxPct: 0)!;
            // missing 1 Ingot: buy 1000 vs craft (2 Ore x10 + Coal 3) = 23 -> 23
            return e.CashCost == 23 && e.MarketCost == 546 && e.MarginCash == 9_977;
        }),
        ("a craftable intermediate costs the cheaper of buying it and crafting it (buy wins when cheap)", () =>
        {
            var prices = SwordMarket().Set(Q(World.Ingot, 15));
            var e = M().Evaluate(World.SwordRecipe, new FakeInventory(), prices, taxPct: 0)!;
            // 2 Ingot: buy 30 vs craft 46 -> 30; Leather 500 -> 530
            return e.CashCost == 530 && e.MarketCost == 530;
        }),
        ("gil vendor price beats a dearer market quote for the same material (Coal 3 vs 50)", () =>
        {
            var m = M();
            return m.UnitCost(World.Coal, SwordMarket()) == 3
                && m.UnitCost(World.Ore, SwordMarket()) == 10
                && m.UnitCost(World.Mystery, SwordMarket()) is null;
        }),
        ("sub-craft consumes on-hand sub-materials for free on the cash basis (4 Ore + 2 Coal in bags -> 2 Ingot cost 0)", () =>
        {
            var inv = new FakeInventory().Set(World.Ore, 4).Set(World.Coal, 2).Set(World.Leather, 1);
            var e = M().Evaluate(World.SwordRecipe, inv, SwordMarket(), taxPct: 0)!;
            return e.CashCost == 0 && e.MarketCost == 546 && e.HowMany == 1;
        }),
        ("market-board tax is taken off revenue (5% of 10000 = 500) and hits both margins", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            var e = M().Evaluate(World.SwordRecipe, inv, SwordMarket(), taxPct: 5)!;
            return e.Tax == 500 && e.MarginCash == 9_500 && e.MarginMarket == 9_500 - 546;
        }),
        ("revenue basis is selectable: min listing / median / average sale", () =>
        {
            var prices = new FakePrices().Set(Q(World.Sword, 10_000, medNq: 12_000, avgNq: 9_000));
            var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            return M(basis: RevenueBasis.MinListing).Evaluate(World.SwordRecipe, inv, prices, 0)!.RevenueNq == 10_000
                && M(basis: RevenueBasis.MedianListing).Evaluate(World.SwordRecipe, inv, prices, 0)!.RevenueNq == 12_000
                && M(basis: RevenueBasis.AvgSale).Evaluate(World.SwordRecipe, inv, prices, 0)!.RevenueNq == 9_000;
        }),
        ("ACCEPTANCE: velocity cap - a 40k-margin / 0.1-velocity item ranks BELOW a 2k-margin / 30-velocity item", () =>
        {
            // Trophy (Coal + Hide) and Ornament (RareOre + Coal), all materials on hand, no tax -> margin = price.
            var inv = new FakeInventory().Set(World.Coal, 200).Set(World.Hide, 100).Set(World.RareOre, 100);
            var prices = new FakePrices()
                .Set(Q(World.Trophy, 40_000, velNq: 0.1, listings: 3))
                .Set(Q(World.Ornament, 2_000, velNq: 30, listings: 15));
            var m = M();
            var slow = m.Evaluate(World.TrophyRecipe, inv, prices, 0)!;
            var fast = m.Evaluate(World.OrnamentRecipe, inv, prices, 0)!;
            var ranked = ProfitModel.Rank([slow, fast]).ToList();
            return slow.MarginCash == 40_000 && fast.MarginCash == 2_000
                && Math.Abs(slow.PerDay - 4_000) < 1e-6 && Math.Abs(fast.PerDay - 60_000) < 1e-6
                && ranked[0].RecipeId == World.OrnamentRecipe && ranked[1].RecipeId == World.TrophyRecipe;
        }),
        ("per-day is capped by stock when a material cannot be bought (Hide is drop-only: 2 craftable, velocity 30 -> 2/day)", () =>
        {
            var inv = new FakeInventory().Set(World.Coal, 200).Set(World.Hide, 2);
            var prices = new FakePrices().Set(Q(World.Trophy, 1_000, velNq: 30, listings: 15));
            var e = M().Evaluate(World.TrophyRecipe, inv, prices, 0)!;
            return e.HowMany == 2 && Math.Abs(e.PerDay - 2_000) < 1e-6 && e.CostComplete;
        }),
        ("saturation = listings / velocity; infinite when nothing sells; per-day 0 at zero velocity", () =>
        {
            var inv = new FakeInventory().Set(World.Coal, 200).Set(World.Hide, 100);
            var prices = new FakePrices().Set(Q(World.Trophy, 1_000, velNq: 4, listings: 12));
            var e = M().Evaluate(World.TrophyRecipe, inv, prices, 0)!;
            var dead = M().Evaluate(World.TrophyRecipe, inv, new FakePrices().Set(Q(World.Trophy, 1_000, velNq: 0, listings: 12)), 0)!;
            return Math.Abs(e.SaturationDays - 3) < 1e-9
                && double.IsPositiveInfinity(dead.SaturationDays) && dead.PerDay == 0 && dead.MarginCash == 1_000;
        }),
        ("an unpriced missing material is reported and makes the cost a lower bound (Mystery has no price or vendor)", () =>
        {
            var prices = new FakePrices().Set(Q(World.Charm, 5_000, velNq: 10));
            var e = M().Evaluate(World.CharmRecipe, new FakeInventory(), prices, 0)!;
            return !e.CostComplete && e.UnpricedItems.SequenceEqual([World.Mystery]) && e.CashCost == 0
                && e.HowMany == 0 && e.PerDay == 0;   // nothing craftable and nothing buyable -> 0/day
        }),
        ("HQ row uses the HQ price and HQ velocity; no HQ price -> revenue unknown, margins null", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            // Materials all purchasable -> supply unbounded -> per-day = margin x HQ velocity.
            var prices = SwordMarket().Set(Q(World.Sword, 10_000, velNq: 5, minHq: 25_000, velHq: 2));
            var hq = M().Evaluate(World.SwordRecipe, inv, prices, 10, hq: true)!;
            var noHq = M().Evaluate(World.SwordRecipe, inv, new FakePrices().Set(Q(World.Sword, 10_000)), 0, hq: true)!;
            return hq.RevenueHq == 25_000 && hq.Tax == 2_500 && hq.MarginCash == 22_500 && hq.Velocity == 2
                && Math.Abs(hq.PerDay - 45_000) < 1e-6
                && !noHq.RevenueKnown && noHq.MarginCash is null && noHq.MarginMarket is null && noHq.PerDay == 0;
        }),
        ("no price for the result -> revenue unknown but both cost columns still computed", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 1).Set(World.Leather, 1);
            var e = M().Evaluate(World.SwordRecipe, inv, new FakePrices().Set(Q(World.Ingot, 1_000)).Set(Q(World.Leather, 500)).Set(Q(World.Ore, 10)), 0)!;
            return !e.RevenueKnown && e.MarginCash is null && e.CashCost == 23 && e.MarketCost == 546 && e.Velocity == 0;
        }),
        ("N crafts scale units, revenue and cost; unknown recipe -> null", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1);
            var e = M().Evaluate(World.SwordRecipe, inv, SwordMarket(), 0, crafts: 3)!;
            // 3 crafts: 6 Ingot (2 on hand -> 4 missing -> craft 4 x 23 = 92), 3 Leather (1 on hand -> 2 x 500)
            return e.Units == 3 && e.RevenueNq == 30_000 && e.CashCost == 1_092 && e.MarketCost == 3 * 546
                && M().Evaluate(99999, inv, SwordMarket(), 0) is null;
        }),
    };
}
