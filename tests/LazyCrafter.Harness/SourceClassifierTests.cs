using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class SourceClassifierTests
{
    private static readonly RetainerStats Miner = new("Miner", Level: 20, JobId: World.Min, ItemLevel: 0, Gathering: 120, Perception: 250);

    private static SourceClassifier C(IInventory? inv = null, params RetainerStats[] retainers)
    {
        var data = World.Build();
        return new SourceClassifier(data, new RecipeGraph(data), new VentureResolver(data), retainers);
    }

    private static bool Is(IReadOnlyList<SourceKind> got, params SourceKind[] want) =>
        got.OrderBy(k => k).SequenceEqual(want.OrderBy(k => k));

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("have >= need -> OnHand only, nothing else is consulted", () =>
            Is(C().Classify(World.Coal, need: 2, have: 2), SourceKind.OnHand)
            && Is(C().Classify(World.Coal, need: 2, have: 5), SourceKind.OnHand)),
        ("gil vendor + marketable -> GilVendor, Market", () =>
            Is(C().Classify(World.Coal, need: 2, have: 0), SourceKind.GilVendor, SourceKind.Market)),
        ("regular node + venture with a qualifying retainer + marketable -> RegularNode, Venture, Market", () =>
            Is(C(null, Miner).Classify(World.Ore, 1, 0), SourceKind.RegularNode, SourceKind.Venture, SourceKind.Market)),
        ("venture is NOT offered when no supplied retainer qualifies (venture exists, wrong job)", () =>
        {
            var botanist = Miner with { JobId = World.Btn };
            return Is(C(null, botanist).Classify(World.Ore, 1, 0), SourceKind.RegularNode, SourceKind.Market)
                && Is(C().Classify(World.Ore, 1, 0), SourceKind.RegularNode, SourceKind.Market);
        }),
        ("timed/unspoiled node -> TimedNode (not RegularNode); fish -> Fish", () =>
            Is(C().Classify(World.RareOre, 1, 0), SourceKind.TimedNode, SourceKind.Market)
            && Is(C().Classify(World.Trout, 1, 0), SourceKind.Fish)),
        ("craftable item -> SubCraft (alongside other sources); special shop -> SpecialShop; drop -> Drop", () =>
            Is(C().Classify(World.Ingot, 1, 0), SourceKind.SubCraft, SourceKind.Market)
            && Is(C().Classify(World.ScripMat, 1, 0), SourceKind.SpecialShop)
            && Is(C().Classify(World.Hide, 1, 0), SourceKind.Drop)),
        ("nothing matches -> Unknown; market-only -> Market", () =>
            Is(C().Classify(World.Mystery, 1, 0), SourceKind.Unknown)
            && Is(C().Classify(World.MarketOnly, 1, 0), SourceKind.Market)),
    };
}
