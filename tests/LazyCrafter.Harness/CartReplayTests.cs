using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Replay of the run that stalled: Alpine Chandelier, recipe 2861, omasky 2026-09-05 19:32:03-19:34:35
/// (LazyCrafter 0.1.6.0, card t_c69287be). Real recipe ids, real item ids and the real ingredient amounts,
/// read out of the game's own sheets with <c>tests/LazyCrafter.Probe</c>, so this is the actual cart rather
/// than a shape that resembles it.
/// <para>
/// What the plugin did that evening:
/// <code>
/// gathers=[12539x12,5111x3,5526x3] crafts=[] vendor=[5998x7] market=[12537x15,12535x4]
/// deferred=[r2332:needs market #12537, r2333:needs craft #12524,
///           r2529:needs market #12535, retrieve #12520 x1 (from the market board (listed by retainer Hussypants)),
///           r2861:needs craft #12525, craft #12521, buy #5998]
/// </code>
/// Two independent defects produced that: (1) the single Hardsilver Nugget listed for sale counted as owned, so it
/// became an impossible retrieval that blocked r2529 and r2861; (2) Titanium Ore and Hardsilver Ore were absent from
/// the gatherable map, so two mineable ores were routed to the market board.
/// </para>
/// </summary>
internal static class CartReplayTests
{
    // Items, exactly as the game numbers them.
    private const uint AlpineChandelier = 14068, TitaniumIngot = 12525, TitaniumNugget = 12524, TitaniumOre = 12537;
    private const uint HardsilverIngot = 12521, HardsilverNugget = 12520, HardsilverOre = 12535, HardsilverSand = 12532;
    private const uint CloudMica = 12539, IronOre = 5111, GrenadeAsh = 5526, SilverOre = 5113, TallowCandle = 5998;
    private const uint FireCrystal = 8, WindCrystal = 10, EarthCrystal = 11;

    // Recipes.
    private const uint RChandelier = 2861, RTitaniumIngot = 2333, RTitaniumNugget = 2332;
    private const uint RHardsilverIngot = 2529, RHardsilverNugget = 2528;

    private const uint Crp = 9, Arm = 11, Min = 16;

    private static readonly RetainerStats[] NoRetainers = Array.Empty<RetainerStats>();

    /// <summary>
    /// The real sub-tree of recipe 2861. <paramref name="oresGatherable"/> switches Fix 2 on and off: with it false
    /// the two ores are missing from the gatherable map exactly as they were in 0.1.6.0, which is the negative
    /// control for the gather fix.
    /// </summary>
    private static FakeGameData Sheets(bool oresGatherable)
    {
        var d = new FakeGameData()
            .Recipe(RChandelier, AlpineChandelier, 1, Crp, 57, (TitaniumIngot, 3), (HardsilverIngot, 1), (TallowCandle, 7), (FireCrystal, 5), (EarthCrystal, 4))
            .Recipe(RTitaniumIngot, TitaniumIngot, 1, Crp, 56, (CloudMica, 4), (TitaniumNugget, 1), (FireCrystal, 5))
            .Recipe(RTitaniumNugget, TitaniumNugget, 1, Crp, 54, (TitaniumOre, 5), (IronOre, 1), (GrenadeAsh, 1), (FireCrystal, 4))
            .Recipe(RHardsilverIngot, HardsilverIngot, 1, Arm, 56, (HardsilverOre, 4), (HardsilverNugget, 1), (WindCrystal, 5))
            .Recipe(RHardsilverNugget, HardsilverNugget, 1, Arm, 54, (HardsilverSand, 5), (SilverOre, 1), (WindCrystal, 4))
            // Non-hidden nodes that routed correctly on the night.
            .Gatherable(CloudMica, new GatherInfo(Min, 58, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(IronOre, new GatherInfo(Min, 14, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(GrenadeAsh, new GatherInfo(Min, 40, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(HardsilverSand, new GatherInfo(Min, 56, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(SilverOre, new GatherInfo(Min, 50, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(FireCrystal, new GatherInfo(Min, 1, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(WindCrystal, new GatherInfo(Min, 1, NodeType.Regular, Timed: false, Collectable: false))
            .Gatherable(EarthCrystal, new GatherInfo(Min, 1, NodeType.Regular, Timed: false, Collectable: false))
            .GilVendor(TallowCandle, 225)
            .Marketable(TitaniumOre).Marketable(HardsilverOre).Marketable(CloudMica).Marketable(TallowCandle)
            .Marketable(TitaniumIngot).Marketable(HardsilverIngot).Marketable(HardsilverNugget).Marketable(TitaniumNugget);

        if (oresGatherable)
        {
            // Both are Mining nodes in a live territory (Titanium Ore: The Dravanian Forelands, GatheringItem 307,
            // node level 55; Hardsilver Ore: The Dravanian Hinterlands, GatheringItem 313, node level 58).
            d.Gatherable(TitaniumOre, new GatherInfo(Min, 55, NodeType.Regular, Timed: false, Collectable: false));
            d.Gatherable(HardsilverOre, new GatherInfo(Min, 58, NodeType.Regular, Timed: false, Collectable: false));
        }
        return d;
    }

    private static DispatchPlan.Plan Replay(bool oresGatherable, FakeInventory inv)
    {
        var data = Sheets(oresGatherable);
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        var tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, NoRetainers));
        (uint RecipeId, int Crafts)[] lines = [(RChandelier, 1)];
        var cart = tiering.AssessCart(lines, inv);
        var planLines = cart.Lines.Select((a, i) => new DispatchPlan.Line(a, lines[i].Crafts)).ToList();
        return DispatchPlan.Build(planLines, cart.Totals, graph, ventures, NoRetainers, null, inv);
    }

    /// <summary>The night's inventory: crystals and candles in the bags, one Hardsilver Nugget listed for sale.</summary>
    private static FakeInventory NightBags() => new FakeInventory()
        .Set(FireCrystal, 99).Set(WindCrystal, 99).Set(EarthCrystal, 99)
        .Set(TallowCandle, 7);

    public static IEnumerable<(string Name, Func<bool> Check)> Tests => new (string, Func<bool>)[]
    {
        // ------------------------------------------------------------------ Fix 2: the ores are mineable
        ("replay: Titanium Ore and Hardsilver Ore route to Gather, not Market",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                return p.Gathers.Any(g => g.ItemId == TitaniumOre)
                    && p.Gathers.Any(g => g.ItemId == HardsilverOre)
                    && p.Market.Count == 0;
            }),

        ("NEGATIVE CONTROL: without the gatherable-map fix both ores fall through to Market",
            () =>
            {
                var p = Replay(oresGatherable: false, NightBags().SetListed(HardsilverNugget, 1));
                // This is the 19:32 line verbatim: market=[12537x15,12535x4].
                return p.Market.Any(m => m.ItemId == TitaniumOre && m.Quantity == 15)
                    && p.Market.Any(m => m.ItemId == HardsilverOre && m.Quantity == 4)
                    && !p.Gathers.Any(g => g.ItemId == TitaniumOre || g.ItemId == HardsilverOre);
            }),

        ("replay: the ore quantities still add up (Titanium Ore x15 for 3 ingots, Hardsilver Ore x4 for 1)",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                return p.Gathers.Single(g => g.ItemId == TitaniumOre).Quantity == 15
                    && p.Gathers.Single(g => g.ItemId == HardsilverOre).Quantity == 4;
            }),

        // ------------------------------------------------------------------ Fix 1: the listing is not stock
        ("replay: the listed Hardsilver Nugget produces NO blocking retrieval",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                return p.Retrievals.Count == 0;
            }),

        ("NEGATIVE CONTROL: counting the listing as owned recreates the impossible retrieval",
            () =>
            {
                var p = Replay(oresGatherable: true,
                    NightBags().SetElsewhere(HardsilverNugget, 1, "the market board (listed by retainer Hussypants)"));
                return p.Retrievals.Any(r => r.ItemId == HardsilverNugget && r.Quantity == 1
                                          && r.Places.Contains("market board"));
            }),

        ("replay: Hardsilver Nugget and Hardsilver Ingot are both crafted",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                return p.Crafts.Any(c => c.ResultItemId == HardsilverNugget && c.Crafts == 1)
                    && p.Crafts.Any(c => c.ResultItemId == HardsilverIngot && c.Crafts == 1);
            }),

        ("NEGATIVE CONTROL: with the listing counted, r2529 defers behind the retrieval instead of crafting",
            () =>
            {
                var p = Replay(oresGatherable: true,
                    NightBags().SetElsewhere(HardsilverNugget, 1, "the market board (listed by retainer Hussypants)"));
                return !p.Crafts.Any(c => c.ResultItemId == HardsilverIngot)
                    && p.Deferred.Any(d => d.RecipeId == RHardsilverIngot
                                        && d.Reason.Contains($"retrieve #{HardsilverNugget}"));
            }),

        // ------------------------------------------------------------------ the whole cart, both fixes on
        ("replay: with both fixes the cart is entirely runnable - crafts queued, nothing deferred",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                var madeAll = new[] { TitaniumNugget, TitaniumIngot, HardsilverNugget, HardsilverIngot, AlpineChandelier }
                    .All(i => p.Crafts.Any(c => c.ResultItemId == i));
                return madeAll && p.Deferred.Count == 0 && p.Retrievals.Count == 0 && p.Manual.Count == 0 && p.HasWork;
            }),

        ("NEGATIVE CONTROL: with neither fix the cart reproduces the 19:32 stall (0 crafts, everything deferred)",
            () =>
            {
                var p = Replay(oresGatherable: false,
                    NightBags().SetElsewhere(HardsilverNugget, 1, "the market board (listed by retainer Hussypants)"));
                // The 19:32 line verbatim: crafts=[], the two ores on the market list, and every recipe in the
                // tree deferred - including r2529 behind the impossible retrieval of the listed nugget.
                // (HasWork is still true: three gathers were queued. The cart made no PROGRESS, which is why the
                // run ended "no progress this pass" two minutes later - not because there was nothing queued.)
                return p.Crafts.Count == 0
                    && p.Deferred.Any(d => d.RecipeId == RTitaniumNugget && d.Reason.Contains($"market #{TitaniumOre}"))
                    && p.Deferred.Any(d => d.RecipeId == RHardsilverIngot && d.Reason.Contains($"market #{HardsilverOre}"))
                    && p.Deferred.Any(d => d.RecipeId == RHardsilverIngot && d.Reason.Contains($"retrieve #{HardsilverNugget}"))
                    && p.Deferred.Any(d => d.RecipeId == RChandelier)
                    && p.Retrievals.Any(r => r.ItemId == HardsilverNugget);
            }),

        // Both fixes are load-bearing: neither one alone finishes this cart. Verified by observing the plan
        // under each half (scratch PlanDump against the worktree, 2026-09-05).
        ("NEGATIVE CONTROL: the listing fix ALONE still strands the cart on the two ores",
            () =>
            {
                var p = Replay(oresGatherable: false, NightBags().SetListed(HardsilverNugget, 1));
                // The Hardsilver Nugget is now made (its own mats gather fine), but both Titanium recipes and
                // the chandelier stay deferred because the ores are still being sent to the market board.
                return p.Crafts.Any(c => c.ResultItemId == HardsilverNugget)
                    && !p.Crafts.Any(c => c.ResultItemId == AlpineChandelier)
                    && p.Market.Any(m => m.ItemId == TitaniumOre)
                    && p.Deferred.Any(d => d.RecipeId == RChandelier);
            }),

        ("NEGATIVE CONTROL: the gatherable fix ALONE still strands the cart on the listing",
            () =>
            {
                var p = Replay(oresGatherable: true,
                    NightBags().SetElsewhere(HardsilverNugget, 1, "the market board (listed by retainer Hussypants)"));
                // Both ores now gather and the Titanium branch runs, but the Hardsilver branch is still blocked
                // behind the retrieval no bell can satisfy - so the chandelier never runs.
                return p.Crafts.Any(c => c.ResultItemId == TitaniumIngot)
                    && !p.Crafts.Any(c => c.ResultItemId == HardsilverIngot)
                    && !p.Crafts.Any(c => c.ResultItemId == AlpineChandelier)
                    && p.Deferred.Any(d => d.RecipeId == RHardsilverIngot
                                        && d.Reason.Contains($"retrieve #{HardsilverNugget}"));
            }),

        ("replay: sub-crafts are ordered below the recipe that consumes them",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                var nugget = p.Crafts.Single(c => c.ResultItemId == TitaniumNugget);
                var ingot = p.Crafts.Single(c => c.ResultItemId == TitaniumIngot);
                var root = p.Crafts.Single(c => c.ResultItemId == AlpineChandelier);
                return nugget.Depth > ingot.Depth && ingot.Depth > root.Depth;
            }),

        ("replay: a craft fed by a gather is held until GBR is idle",
            () =>
            {
                var p = Replay(oresGatherable: true, NightBags().SetListed(HardsilverNugget, 1));
                return p.Crafts.Single(c => c.ResultItemId == TitaniumNugget).AfterGather;
            }),
    };
}
