using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>
///     The three shop visits of the 2026-10-04 11:14-11:36 ET Crucible run (both Lauda fights, 7 deaths, no Beast Potion held),
///     replayed through the purchase policy. Stock rows, prices and the carried items are the recorded XBMContentsItemShop reads
///     (ffxivdb plugin_log_lines) and the main HUD stock just before each visit. Finding: the heal reserve bought every heal the
///     shop offered; two of the three shops offered no Beast Potion at all, so nothing for the reserve rule to buy.
/// </summary>
internal static class HealStockCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private static RunContext Ctx(int fights = 2) => new()
    {
        Board = 3,
        Upcoming = Enumerable.Range(1, fights).Select(i => new UpcomingBattle(i, true, false)).ToList(),
        PlayerHpPercent = 100,
        HornDemand = new Dictionary<int, double>(),
        ThreatLevels = new Dictionary<CrucibleThreat, int>(),
    };

    private static List<ShopOffer> Stock(params (int Row, int Price)[] offers) =>
        offers.Select((o, i) => new ShopOffer(i, o.Row, o.Price, false)).ToList();

    public static void Run()
    {
        Console.WriteLine("-- replay: heal stock, 2026-10-04 11:14 / 11:22 / 11:36 ET shops --");

        // 11:14: 1,200 tokens, one G3 Beast Potion (idx 9, 250) among feed and gear; a G3 already carried (Temporal Sand beside it).
        var s1114 = Stock((166, 65), (185, 294), (153, 218), (195, 150), (146, 70), (191, 101), (169, 180), (144, 130), (103, 47),
                          (78, 250), (113, 310), (115, 95), (41, 840), (54, 630), (74, 378), (13, 522));
        var inv1114 = new Inventory([78, 138], []);
        var first1114 = ItemPolicies.NextPurchase(s1114, 1200, inv1114, [], Ctx());
        Check("11:14 shop, 1 heal held (reserve 2): the one potion on offer is bought first, before feed and gear (idx 9, row 78)",
            first1114 is { Index: 9, Row: 78 }, first1114?.Reason);
        var inv1114b = new Inventory([78, 78, 138], []);
        var after1114 = ItemPolicies.NextPurchase(s1114.Select(o => o.Index == 9 ? o with { Bought = true } : o).ToList(), 950, inv1114b, [], Ctx());
        Check("11:14 shop, reserve met (2 held): the next pick is not a forced heal",
            after1114 is null || !CrucibleItems.IsHealing(after1114.Value.Row), after1114?.Reason);

        // 11:22: 1,214 tokens, nothing held that heals; the stock has no heal row of any kind (16 entries: feed, Feral/offense items, gear).
        var s1122 = Stock((183, 179), (155, 149), (196, 185), (201, 220), (202, 202), (194, 336), (165, 242), (162, 125), (107, 110),
                          (135, 840), (103, 47), (114, 500), (50, 1342), (32, 833), (61, 1575), (52, 564));
        var inv1122 = new Inventory([137, 138], []);
        Check("11:22 shop: no heal row of any grade in the 16 offers (supply, not policy)", s1122.All(o => !CrucibleItems.IsHealing(o.Row)));
        var pick1122 = ItemPolicies.NextPurchase(s1122, 1214, inv1122, [], Ctx());
        Check("11:22 shop, 0 heals held (reserve 2), none on offer: nothing heal-shaped is bought, and no purchase pretends to be a heal",
            inv1122.HealingHeld == 0 && (pick1122 is null || !pick1122.Value.Reason.Contains("keeping")), pick1122?.Reason);

        // 11:36: 879 tokens, nothing held that heals; the only heal offered is G2 Crucible Ash (party heal, idx 10, 350).
        var s1136 = Stock((177, 89), (199, 70), (175, 193), (148, 90), (146, 70), (147, 105), (197, 42), (179, 114), (113, 310), (108, 22),
                          (81, 350), (139, 160), (71, 1321), (28, 750), (13, 522), (49, 1320));
        var inv1136 = new Inventory([137, 114, 115], []);
        var first1136 = ItemPolicies.NextPurchase(s1136, 879, inv1136, [], Ctx());
        Check("11:36 shop, 0 heals held: the only heal on offer (G2 Crucible Ash, idx 10) is bought first, as logged",
            first1136 is { Index: 10, Row: 81 } && first1136.Value.Reason.Contains("keeping"), first1136?.Reason);
        Check("11:36 shop: no Beast Potion (rows 76-79) was on offer", s1136.All(o => o.Row is < 76 or > 79));
    }
}
