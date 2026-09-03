using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class VentureResolverTests
{
    // Retainers: a MIN, a BTN, and a PLD. Ventures in World: Ore (cat 17 MIN, lvl 10, gathering 50,
    // perception thresholds 100/200/300/400, qty 10..30) and Hide (combat cat 34, lvl 30, ilvl 60,
    // ilvl thresholds 70/80/90/100, qty 5..17).
    private static readonly RetainerStats Miner = new("Miner", Level: 20, JobId: World.Min, ItemLevel: 0, Gathering: 120, Perception: 250);
    private static readonly RetainerStats Botanist = new("Botanist", Level: 90, JobId: World.Btn, ItemLevel: 0, Gathering: 999, Perception: 999);
    private static readonly RetainerStats Paladin = new("Paladin", Level: 90, JobId: World.Pld, ItemLevel: 95, Gathering: 0, Perception: 0);

    private static VentureResolver R() => new(World.Build());

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("two retainers, only the MIN qualifies for a MIN-category venture (BTN is category 18)", () =>
        {
            var all = R().ResolveAll(World.Ore, [Miner, Botanist]).ToList();
            return all.Count == 1 && all[0].Retainer == Miner && all[0].Venture.TaskId == 1;
        }),
        ("perception picks the reward tier: 250 -> tier 2 (>=200, <300) -> quantity 20", () =>
        {
            var m = R().Resolve(World.Ore, Miner);
            return m is { RewardTier: 2, Quantity: 20 };
        }),
        ("perception below the first threshold -> tier 0 (base quantity); above the last -> tier 4", () =>
        {
            var low = R().Resolve(World.Ore, Miner with { Perception = 50 });
            var high = R().Resolve(World.Ore, Miner with { Perception = 450 });
            return low is { RewardTier: 0, Quantity: 10 } && high is { RewardTier: 4, Quantity: 30 };
        }),
        ("retainer level below the venture level, or gathering below RequiredGathering -> null", () =>
            R().Resolve(World.Ore, Miner with { Level = 9 }) is null
            && R().Resolve(World.Ore, Miner with { Gathering = 49 }) is null
            && R().Resolve(World.Ore, Miner with { Gathering = 50 }) is not null),
        ("combat venture: ilvl gates entry and picks the tier (95 -> tier 3 -> 14 Hide); ilvl 59 -> null", () =>
            R().Resolve(World.Hide, Paladin) is { RewardTier: 3, Quantity: 14 }
            && R().Resolve(World.Hide, Paladin with { ItemLevel = 59 }) is null
            && R().Resolve(World.Hide, Miner with { Level = 90 }) is null),   // a gatherer cannot take a combat venture
        ("gathering ventures require the item in the character's gathered set when one is supplied; combat ignores it", () =>
        {
            var unlocked = new HashSet<uint> { World.Ore };
            var none = new HashSet<uint>();
            return R().Resolve(World.Ore, Miner, unlocked) is not null
                && R().Resolve(World.Ore, Miner, none) is null
                && R().Resolve(World.Hide, Paladin, none) is not null;
        }),
        ("ResolveBest returns the highest quantity across retainers; unknown item -> null", () =>
        {
            var better = Miner with { Name = "Miner2", Perception = 999 };
            var best = R().ResolveBest(World.Ore, [Botanist, Miner, better]);
            return best is { Quantity: 30 } && best.Retainer.Name == "Miner2"
                && R().ResolveBest(World.Mystery, [Miner, Botanist, Paladin]) is null;
        }),
    };
}
