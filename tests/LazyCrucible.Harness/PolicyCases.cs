using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>
///     Selection policies (phase 2): board graph, feed, campsite, shop, treasure, spoils and the actuation allow-list.
///     Every case states the rule it pins; the rules are the ones DESIGN.md §2 lists.
/// </summary>
internal static class PolicyCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    // Familiar rows used below (BST_Beasts): 5 opo-opo (beastkin), 7 coblyn (soulkin), 18 dullahan (soulkin),
    // 19 bat (cloudkin), 20 flying trap (seedkin), 27 uragnite (wavekin), 40 ghost (ashkin), 41 salamander (wavekin).
    private static FamiliarState Fam(int row, int index, int hp = 100, int used = 0, int max = 3, int[]? ate = null, bool? cannot = null) =>
        new(row, index, hp, used, max, ate ?? [], cannot);

    private static RunContext Ctx(int board, Dictionary<int, double>? demand = null, Dictionary<CrucibleThreat, int>? threats = null,
        int fights = 3, bool camp = false, int playerHp = 100, ScoreGoal goal = ScoreGoal.SurvivalFirst, CrucibleNeeds needs = CrucibleNeeds.None)
    {
        var upcoming = Enumerable.Range(1, fights).Select(i => new UpcomingBattle(i, true, false)).ToList();
        return new RunContext
        {
            Board = board,
            Upcoming = upcoming,
            CampsAhead = camp ? [2] : [],
            PlayerHpPercent = playerHp,
            Goal = goal,
            HornDemand = demand ?? new Dictionary<int, double>(),
            ThreatLevels = threats ?? new Dictionary<CrucibleThreat, int>(),
            NeedsAhead = needs,
        };
    }

    public static void Run()
    {
        Boards();
        Items();
        Feed();
        Camp();
        Shop();
        TreasureAndSpoils();
        Guard();
    }

    private static void Boards()
    {
        Console.WriteLine("-- board graph (what is ahead) --");
        var start = CrucibleBoards.Upcoming(1, -1);
        Check("B1 from the start: all 6 battles ahead, boss last",
            start.Count == 6 && start[^1].Battle == 0 && start.Select(u => u.Battle).OrderBy(b => b).SequenceEqual([0, 1, 2, 3, 4, 5]),
            string.Join(",", start.Select(u => $"{u.Battle}{(u.Certain ? "!" : "?")}")));
        Check("B1 from the start: move 1, banemite (4), ogre (5) and boss are on every path; the move-2 fork is not",
            start.Single(u => u.Battle == 1).Certain && start.Single(u => u.Battle == 4).Certain
            && start.Single(u => u.Battle == 5).Certain && start.Single(u => u.Battle == 0).Certain
            && !start.Single(u => u.Battle == 2).Certain && !start.Single(u => u.Battle == 3).Certain);

        var afterArch = CrucibleBoards.Upcoming(1, 2);
        Check("B1 after the arch demon (left fork): piscodemon is behind, 4 / 5 / boss ahead",
            afterArch.Select(u => u.Battle).SequenceEqual([4, 5, 0]), string.Join(",", afterArch.Select(u => u.Battle)));
        Check("B1 after the arch demon: both campsites ahead (2 familiars each)",
            CrucibleBoards.CampsAhead(1, 2).SequenceEqual([2, 2]), string.Join(",", CrucibleBoards.CampsAhead(1, 2)));
        Check("B1 after the ogre: one campsite (right side) and the boss ahead",
            CrucibleBoards.CampsAhead(1, 5).SequenceEqual([2]) && CrucibleBoards.Upcoming(1, 5).Select(u => u.Battle).SequenceEqual([0]));
        var b2 = CrucibleBoards.Upcoming(2, 5);
        Check("B2 after the tablitaurs: the random-space demon fight is possible, never certain; boss certain",
            b2.Any(u => u.Battle == 6 && u.RandomOnly && !u.Certain) && b2.Any(u => u.Battle == 0 && u.Certain),
            string.Join(",", b2.Select(u => $"{u.Battle}{(u.Certain ? "!" : "?")}")));
        Check("B2 random space can be a 3-familiar campsite",
            CrucibleBoards.CampsAhead(2, 5).Contains(3), string.Join(",", CrucibleBoards.CampsAhead(2, 5)));
        Check("Where: B1 ogre = row 6 elite; B2 demon = random space",
            CrucibleBoards.Where(1, 5) == "row 6, elite" && CrucibleBoards.Where(2, 6).Contains("random space"),
            $"{CrucibleBoards.Where(1, 5)} / {CrucibleBoards.Where(2, 6)}");
        var m2 = CrucibleBoards.Upcoming(5, -1);
        Check("M2 from the start: 14 battles, boss last", m2.Count == 14 && m2[^1].Battle == 0, m2.Count.ToString());
    }

    private static void Items()
    {
        Console.WriteLine("-- item table (parsed from the sheet tooltips) --");
        Check("Item table: 203 rows, 75 gear / 68 items / 60 feed",
            CrucibleItems.All.Length == 204
            && CrucibleItems.All.Count(i => i.Type == CrucibleItemType.Gear) == 75
            && CrucibleItems.All.Count(i => i.Type == CrucibleItemType.Item) == 68
            && CrucibleItems.All.Count(i => i.Type == CrucibleItemType.Feed) == 60);
        var crab = CrucibleItems.Get(149);
        Check("Crab Ball Simular: soulkin only (dullahan yes, opo-opo no)",
            crab.SuitsKin(Lalalazy.Crucible.BeastmasterKinType.Soulkin) && !crab.SuitsKin(Lalalazy.Crucible.BeastmasterKinType.Beastkin));
        Check("Grape Juice Simular: max HP -80% flagged as a drawback",
            CrucibleItems.Get(197).MaxHp == -80 && (CrucibleItems.Get(197).Hazards & ItemHazard.MaxHpDown) != 0);
        Check("Lily Simular keeps feed at a campsite; Lassi heals fully there",
            (CrucibleItems.Get(166).Flags & ItemFlags.KeepsFeedAtCamp) != 0 && (CrucibleItems.Get(203).Flags & ItemFlags.FullHealAtCamp) != 0);
        Check("Crimson Ribbon resists petrify, blind and paralysis; G2 antipoison serum resists poison",
            CrucibleItems.Get(18).Resists == (CrucibleStatus.Paralysis | CrucibleStatus.Blind | CrucibleStatus.Petrify)
            && CrucibleItems.Get(87).Resists == CrucibleStatus.Poison);
        Check("Beast potions heal 10/23/36/50%", CrucibleItems.Get(76).Heal == 10 && CrucibleItems.Get(79).Heal == 50);
    }

    private static void Feed()
    {
        Console.WriteLine("-- feed policy --");
        var ctx = Ctx(1, new() { [18] = 3, [5] = 1 });
        Check("CanEat: Crab Ball (soulkin feed) refused for opo-opo, allowed for dullahan",
            !FeedPolicy.CanEat(Fam(5, 0), 149, out var w1) && w1 == "wrong kin" && FeedPolicy.CanEat(Fam(18, 1), 149, out _), w1);
        var fa = FeedPolicy.CanEat(Fam(18, 0, used: 2, max: 2), 144, out var a);
        var fb = FeedPolicy.CanEat(Fam(18, 0, ate: [144]), 144, out var b);
        var fc = FeedPolicy.CanEat(Fam(18, 0, hp: 0), 144, out var c);
        var fd = FeedPolicy.CanEat(Fam(18, 0, cannot: true), 144, out var d);
        Check("CanEat: full satiety, same feed twice, knocked out, screen 'cannot eat' all refused",
            !fa && a == "full" && !fb && b == "already ate it" && !fc && c == "knocked out" && !fd && d.Contains("cannot"),
            $"{a}/{b}/{c}/{d}");

        var fams = new[] { Fam(5, 0), Fam(18, 1), Fam(40, 2) };
        var target = FeedPolicy.BestTarget(144, fams, ctx);
        Check("Picker: G1 Primafodder goes to the familiar needed for the most fights ahead (dullahan, 3)",
            target is { FamiliarRow: 18, FamiliarIndex: 1 } && target.Value.Reason.Contains("Dullahan"), target?.Reason);

        var grape = FeedPolicy.BestTarget(197, fams, ctx);
        Check("Picker: Grape Juice (max HP -80%) is worth less than nothing to everyone (never auto-fed)",
            grape is null || grape.Value.Score <= 0, grape?.Score.ToString());

        var poisonCtx = Ctx(4, new() { [18] = 2 }, new() { [CrucibleThreat.Poison] = 2 });
        Check("Porcini (poison resist) is worth more when a certain fight ahead poisons",
            FeedPolicy.Value(157, Fam(18, 0), poisonCtx) > FeedPolicy.Value(157, Fam(18, 0), Ctx(4)) + 20);

        var starve = Ctx(1, new() { [18] = 3 }, goal: ScoreGoal.StarveTheFever);
        Check("Starve the Fever goal: never feeds", FeedPolicy.BestTarget(144, fams, starve) is null
            && FeedPolicy.BestPurchase([(0, 144, 10)], 999, fams, starve) is null);

        var hurt = new[] { Fam(41, 0, hp: 35), Fam(5, 1) };
        var buy = FeedPolicy.BestPurchase([(0, 146, 30), (1, 155, 30)], 100, hurt, Ctx(1, new() { [41] = 3 }));
        Check("Shop feed: Milk Simular (max HP +50%, heals) over Meat Simular (damage) for a hurt, needed familiar",
            buy is { FeedRow: 155, FamiliarRow: 41, StockIndex: 1 }, buy?.Reason);
        Check("Shop feed: Milk Simular is not for soulkin; a needed dullahan gets nothing from it",
            FeedPolicy.BestPurchase([(1, 155, 30)], 100, [Fam(18, 0, hp: 35)], Ctx(1, new() { [18] = 3 })) is null);
        Check("Shop feed: unaffordable feed is skipped",
            FeedPolicy.BestPurchase([(0, 155, 300)], 100, hurt, Ctx(1, new() { [41] = 3 })) is null);

        var bold = FeedPolicy.BestPurchase([(0, 151, 20)], 100, [Fam(5, 0)], Ctx(1, goal: ScoreGoal.FeedTheBold));
        var survival = FeedPolicy.BestPurchase([(0, 151, 20)], 100, [Fam(5, 0)], Ctx(1));
        Check("Feed the Bold goal buys a low-value feed that survival-first skips", bold is not null && survival is null,
            $"bold={bold?.Score} survival={survival?.Score}");

        var campCtx = Ctx(1, new() { [18] = 2 }, camp: true);
        Check("Campsite ahead: a hurt familiar's feed is discounted (it may rest and lose it); Lily is not",
            FeedPolicy.Weighted(144, Fam(18, 0, hp: 40), campCtx) < FeedPolicy.Weighted(144, Fam(18, 0, hp: 40), Ctx(1, new() { [18] = 2 }))
            && FeedPolicy.Value(166, Fam(18, 0), campCtx) > FeedPolicy.Value(166, Fam(18, 0), Ctx(1)));
    }

    private static void Camp()
    {
        Console.WriteLine("-- campsite policy --");
        var ctx = Ctx(1, new() { [18] = 2, [40] = 1 }, playerHp: 100);
        var one = CampPolicy.Choose([Fam(18, 0, hp: 30), Fam(40, 1, hp: 95), Fam(5, 2)], 2, ctx);
        Check("B1, you at full HP, dullahan needed and at 30%: dullahan rests (ghost at 95% gains too little to be worth the smaller heal)",
            one.Rows.SequenceEqual([18]) && one.Indices.SequenceEqual([0]) && one.HealPercent == 45, one.Reason);

        var self = CampPolicy.Choose([Fam(18, 0, hp: 90), Fam(40, 1, hp: 95)], 2, Ctx(1, new() { [18] = 2 }, playerHp: 20));
        Check("B1, you at 20%, familiars nearly full: nobody rests, you take the 90% self heal",
            self.Rows.Count == 0 && self.HealPercent == 90, self.Reason);

        var ko = CampPolicy.Choose([Fam(18, 0, hp: 0), Fam(40, 1, hp: 20)], 2, Ctx(1, new() { [18] = 3, [40] = 1 }));
        Check("A knocked-out familiar never rests", !ko.Rows.Contains(18) && ko.Rows.Contains(40), ko.Reason);

        var cap = CampPolicy.Choose([Fam(18, 0, hp: 10), Fam(40, 1, hp: 10), Fam(5, 2, hp: 10)], 1, Ctx(3, new() { [18] = 2, [40] = 2, [5] = 2 }));
        Check("Campsite limit respected (1 familiar)", cap.Rows.Count <= 1, cap.Reason);

        var fed = CampPolicy.Choose([Fam(18, 0, hp: 60, ate: [144, 155, 157]), Fam(40, 1, hp: 60)], 1, Ctx(1, new() { [18] = 1, [40] = 1 }));
        Check("Equal need and HP: the unfed familiar rests, the fed one keeps its feed", fed.Rows.SequenceEqual([40]), fed.Reason);
        var lily = CampPolicy.Choose([Fam(18, 0, hp: 50, ate: [144, 166]), Fam(40, 1, hp: 60)], 1, Ctx(1, new() { [18] = 1, [40] = 1 }));
        Check("A familiar that ate Lily Simular loses nothing by resting (lower HP rests)", lily.Rows.SequenceEqual([18]), lily.Reason);

        var b4 = CampPolicy.Choose([Fam(18, 0, hp: 30)], 2, Ctx(4, new() { [18] = 2 }));
        Check("Boards 4-5: the reason says the heal table is assumed", b4.Reason.Contains("assumed"), b4.Reason);
    }

    private static void Shop()
    {
        Console.WriteLine("-- shop policy --");
        var ctx = Ctx(1, fights: 3);
        var stock = new List<ShopOffer>
        {
            new(0, 144, 20, false), new(1, 155, 40, false),     // feed
            new(8, 76, 30, false), new(9, 78, 90, false),       // G1 / G3 beast potion
            new(10, 67, 20, false), new(11, 100, 20, false),    // Merchant's Cap, Thief's Eye (score)
            new(12, 23, 200, false), new(13, 9, 150, false),    // Angel Robe, Power Armlet
        };
        var first = ItemPolicies.NextPurchase(stock, 400, Inventory.Empty, [Fam(18, 0)], ctx);
        Check("No heals held, 3 fights ahead: the best affordable heal first (G3 Beast Potion)",
            first is { Row: 78, Index: 9 } && first.Value.Reason.Contains("heals"), first?.Reason);

        var healed = new Inventory([78, 78, 76], []);
        var poisonCtx = Ctx(4, fights: 3, threats: new() { [CrucibleThreat.Poison] = 2 });
        var robe = ItemPolicies.NextPurchase(stock, 400, healed, [], poisonCtx);
        Check("Heals held, a poison fight ahead: Angel Robe (poison resist, damage taken) over Power Armlet (damage)",
            robe is { Row: 23 }, robe?.Reason);

        var owned = new Inventory([78, 78, 76], [23]);
        var noDup = ItemPolicies.NextPurchase(stock, 400, owned, [], poisonCtx);
        Check("Owned gear is never bought again", noDup?.Row != 23, noDup?.Reason);

        var poor = ItemPolicies.NextPurchase(stock, 25, healed, [], ctx);
        Check("Only score items / an unaffordable rest: nothing worth buying (Merchant's Cap, Thief's Eye skipped)",
            poor is null || (poor.Value.Row != 67 && poor.Value.Row != 100), poor?.Reason);

        var fullItems = new Inventory([78, 78, 78, 78, 78, 78, 78, 78, 78, 78], []);
        var gearOnly = ItemPolicies.NextPurchase(stock, 400, fullItems, [], ctx);
        Check("Item slots full: no Crucible item is bought", gearOnly is null || CrucibleItems.Get(gearOnly.Value.Row).Type != CrucibleItemType.Item, gearOnly?.Reason);

        var skip = ItemPolicies.NextPurchase(stock, 400, Inventory.Empty, [], ctx, new HashSet<int> { 9 });
        Check("A stock entry that already failed this visit is skipped", skip?.Index != 9, skip?.Reason);

        var bought = stock.Select(o => o.Index == 9 ? o with { Bought = true } : o).ToList();
        var afterBuy = ItemPolicies.NextPurchase(bought, 400, Inventory.Empty, [], ctx);
        Check("A bought entry is never bought again", afterBuy?.Index != 9, afterBuy?.Reason);

        var feedStock = new List<ShopOffer> { new(0, 155, 40, false), new(12, 24, 300, false) };
        var shield = ItemPolicies.NextPurchase(feedStock, 400, healed, [Fam(41, 0, hp: 40)], Ctx(1, new() { [41] = 1 }));
        Check("Master Shield (protects you every fight) outranks Milk Simular for a familiar needed once",
            shield is { Row: 24 }, shield?.Reason);
        var milk = ItemPolicies.NextPurchase(feedStock, 100, healed, [Fam(41, 0, hp: 40)], Ctx(1, new() { [41] = 3 }));
        Check("Shield unaffordable: Milk Simular for the hurt familiar needed in 3 fights, with its target",
            milk is { Row: 155, Feed.FamiliarRow: 41 }, milk?.Reason);
    }

    private static void TreasureAndSpoils()
    {
        Console.WriteLine("-- treasure and spoils --");
        var ctx = Ctx(1, fights: 2);
        var pick = ItemPolicies.TreasurePick([(0, 101), (1, 77), (2, 128), (3, 24)], Inventory.Empty, [], ctx);
        Check("Treasure: Master Shield over G2 Beast Potion, Fang of Fire and Merchant's Eye", pick is { Index: 3, Row: 24 }, pick?.Reason);
        var healFirst = ItemPolicies.TreasurePick([(0, 101), (1, 79), (2, 128)], Inventory.Empty, [], ctx);
        Check("Treasure: with no heals held, G4 Beast Potion over a fang and a score item", healFirst is { Row: 79 }, healFirst?.Reason);
        var full = new Inventory([78, 78, 78, 78, 78, 78, 78, 78, 78, 78], []);
        var none = ItemPolicies.TreasurePick([(0, 101), (1, 79), (2, 128)], full, [], ctx);
        Check("Treasure: item slots full and only items offered: left to the player", none is null, none?.Reason);

        var fits = ItemPolicies.Spoils([(0, 79), (1, 128)], new Inventory([78], [2]), [], ctx);
        Check("Spoils: everything fits → Take all", fits.TakeAll, fits.Reason);
        var over = ItemPolicies.Spoils([(0, 79), (1, 128)], new Inventory([78, 78, 78, 78, 78, 78, 78, 78, 78], []), [], ctx);
        Check("Spoils: one item slot free for two items → not Take all, ranked (potion first)",
            !over.TakeAll && over.Ranked[0].Row == 79 && over.Reason.Contains("slot"), over.Reason);
        var dup = ItemPolicies.Spoils([(0, 2)], new Inventory([], [2]), [], ctx);
        Check("Spoils: gear already owned → not Take all", !dup.TakeAll, dup.Reason);
    }

    private static void Guard()
    {
        Console.WriteLine("-- actuation allow-list --");
        Check("Allowed: shop buy [2, i], familiar pick [1, i], Take all node 46, treasure param 2-9, Yes/No",
            ActuationGuard.Allowed("XBMContentsItemShop", ActuationGuard.Input.Callback, [2, 5])
            && ActuationGuard.Allowed("XBMPetParty", ActuationGuard.Input.AgentEvent, [1, 4])
            && ActuationGuard.Allowed("XBMContentsBooty", ActuationGuard.Input.Button, [], node: 46)
            && ActuationGuard.Allowed("XBMContentsTreasure", ActuationGuard.Input.Button, [], eventParam: 2)
            && ActuationGuard.Allowed("SelectYesno", ActuationGuard.Input.Callback, [0])
            && ActuationGuard.Allowed("SelectYesno", ActuationGuard.Input.Callback, [1]));
        Check("Refused: challenge [8], commence, campsite Rest [3], close [-2], shop close node 40, HUD suspend/forfeit, result",
            !ActuationGuard.Allowed("XBMStageDetailList", ActuationGuard.Input.Callback, [8])
            && !ActuationGuard.Allowed("ContentsFinderConfirm", ActuationGuard.Input.Callback, [8])
            && !ActuationGuard.Allowed("XBMPetParty", ActuationGuard.Input.AgentEvent, [3])
            && !ActuationGuard.Allowed("XBMPetParty", ActuationGuard.Input.AgentEvent, [-2])
            && !ActuationGuard.Allowed("XBMPetParty", ActuationGuard.Input.AgentEvent, [2, 0])
            && !ActuationGuard.Allowed("XBMContentsItemShop", ActuationGuard.Input.Button, [], node: 40)
            && !ActuationGuard.Allowed("XBMContentsMainHUD", ActuationGuard.Input.Callback, [2])
            && !ActuationGuard.Allowed("XBMContentsMainHUD", ActuationGuard.Input.Callback, [3])
            && !ActuationGuard.Allowed("XBMResult", ActuationGuard.Input.Button, [], node: 61)
            && !ActuationGuard.Allowed("XBMContentsBooty", ActuationGuard.Input.Button, [], node: 40)
            && !ActuationGuard.Allowed("XBMContentsTreasure", ActuationGuard.Input.Button, [], eventParam: 1)
            && !ActuationGuard.Allowed("SelectYesno", ActuationGuard.Input.Callback, [2]));

        var latch = new ScreenLatch();
        var opened = latch.Update(true);
        latch.StandDown("player edit");
        var stillOpen = latch.Update(true);
        Check("Latch: opens once; a stand-down lasts the whole open", opened && !stillOpen && !latch.MayAct);
        latch.Update(false);
        Check("Latch: a close clears the stand-down; the next open acts", latch.Update(true) && latch.MayAct);
    }
}
