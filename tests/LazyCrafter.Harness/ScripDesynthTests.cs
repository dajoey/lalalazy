using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class ScripValueTests
{
    // Purple crafters' scrip = currency 33913 (any id works for the fake). ScripMat: 3 tiers.
    private static FakeGameData Data() => World.Build()
        .Collectable(new CollectableInfo(World.ScripMat, Currency: 33913, LevelMin: 90, LevelMax: 100,
            Collectability: [600, 800, 1000], Reward: [72, 90, 108], ExpRatio: [50, 70, 100]));

    private static ScripValue S() => new(Data());

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("collectable -> scrip per tier in table order, scripPerCraft = top tier", () =>
        {
            var e = S().Evaluate(World.ScripMat)!;
            return e.ScripPerTier.SequenceEqual([72, 90, 108]) && e.CollectabilityPerTier.SequenceEqual([600, 800, 1000])
                && e.ScripPerCraft == 108 && e.Currency == 33913 && e.AcceptedAtLevel;
        }),
        ("not a collectable -> null; ForCollectability walks the breakpoints (599 -> 0, 800 -> 90, 1200 -> 108)", () =>
        {
            var s = S();
            return s.Evaluate(World.Ore) is null && s.ForCollectability(World.Ore, 1000) == 0
                && s.ForCollectability(World.ScripMat, 599) == 0
                && s.ForCollectability(World.ScripMat, 800) == 90
                && s.ForCollectability(World.ScripMat, 1200) == 108;
        }),
        ("level band: 89 is out, 90 and 100 are in; LevelMax 0 means open-ended", () =>
        {
            var s = S();
            var open = new ScripValue(World.Build().Collectable(new CollectableInfo(World.ScripMat, 1, 50, 0, [100], [10], [100])));
            return !s.Evaluate(World.ScripMat, 89)!.AcceptedAtLevel && s.Evaluate(World.ScripMat, 90)!.AcceptedAtLevel
                && s.Evaluate(World.ScripMat, 100)!.AcceptedAtLevel && open.Evaluate(World.ScripMat, 100)!.AcceptedAtLevel;
        }),
    };
}

internal static class DesynthValueTests
{
    private static PriceQuote Q(uint item, long? minNq) => new(item, minNq, null, minNq, null, minNq, null, 1, 0, 5, null);

    // Sword desynths into Ingot (50% x1), Ore (100% x2) and Mystery (10%, untradeable).
    private static FakeGameData Data() => World.Build()
        .Desynth(World.Sword, new DesynthResult(World.Ingot, 0.5), new DesynthResult(World.Ore, 1.0, 2), new DesynthResult(World.Mystery, 0.1));

    private static FakePrices Prices() => new FakePrices().Set(Q(World.Ingot, 1_000)).Set(Q(World.Ore, 10)).Set(Q(World.Sword, 400));

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("expected value = sum(chance x qty x market min): 0.5x1000 + 1.0x2x10 = 520; untradeable outcome unpriced", () =>
        {
            var e = new DesynthValue(Data()).Evaluate(World.Sword, Prices())!;
            return Math.Abs(e.ExpectedValue - 520) < 1e-9 && e.Priced.Count == 2 && e.Unpriced.Single().ItemId == World.Mystery
                && !e.Complete && e.IsEstimate;
        }),
        ("no desynth data -> null; premium = desynth EV - sell price (520 - 400 = +120 => break it)", () =>
        {
            var d = new DesynthValue(Data());
            var p = d.DesynthPremium(World.Sword, Prices());
            return d.Evaluate(World.Ore, Prices()) is null && d.DesynthPremium(World.Ore, Prices()) is null
                && p is { } v && Math.Abs(v - 120) < 1e-9;
        }),
        ("an outcome with no price contributes 0 and marks the estimate incomplete; all priced -> Complete", () =>
        {
            var noIngot = new FakePrices().Set(Q(World.Ore, 10));
            var e = new DesynthValue(Data()).Evaluate(World.Sword, noIngot)!;
            var full = new DesynthValue(World.Build().Desynth(World.Sword, new DesynthResult(World.Ore, 1.0, 3)))
                .Evaluate(World.Sword, noIngot)!;
            return Math.Abs(e.ExpectedValue - 20) < 1e-9 && e.Unpriced.Count == 2
                && full.Complete && Math.Abs(full.ExpectedValue - 30) < 1e-9;
        }),
    };
}
