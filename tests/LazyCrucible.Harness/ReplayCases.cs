using System.Text.Json;
using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>
///     Replays of recorded selection screens (tests/LazyCrucible.Harness/Fixtures: AtkValue snapshots from the
///     2026-09-17..21 runs, trimmed to the indices the layouts need; no character names) through the screen readers and
///     the policies, plus the Yes/No prompt guard against the client's own prompt texts (Addon sheet rows).
/// </summary>
internal static class ReplayCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private sealed class Fixture
    {
        public string addon { get; set; } = "";
        public string ts { get; set; } = "";
        public int n { get; set; }
        public Dictionary<string, string[]> values { get; set; } = [];
    }

    private static AtkCells Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var f = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path))!;
        return AtkCells.FromFixture(f.values, f.n);
    }

    public static void Run()
    {
        PickerEcho();
        Shop();
        Feed();
        Camp();
        TreasureAndBooty();
        Prompts();
    }

    /// <summary>
    ///     events.json: every recorded feed-picker flow (agent events + picker visibility). Selections that arrive with the
    ///     open are the picker's own echo; every human pick came later than the echo window.
    /// </summary>
    private static void PickerEcho()
    {
        Console.WriteLine("-- replay: feed picker event timing (events.json) --");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "events.json")));
        var echoes = 0;
        var humans = new List<long>();
        foreach (var flow in doc.RootElement.GetProperty("feed_flows").EnumerateArray())
        {
            DateTimeOffset? open = null;
            var sawHumanSinceOpen = false;
            foreach (var e in flow.GetProperty("events").EnumerateArray())
            {
                var ts = DateTimeOffset.Parse(e.GetProperty("ts").GetString()!);
                var src = e.GetProperty("src").GetString();
                if (src == "VIS" && e.GetProperty("addon").GetString() == "XBMPetParty")
                {
                    open = e.GetProperty("visible").GetBoolean() ? ts : null;
                    sawHumanSinceOpen = false;
                    continue;
                }
                if (src != "PSP" || e.GetProperty("kind").GetInt32() != 0)
                    continue;
                var values = e.GetProperty("values").EnumerateArray().Select(v => v.GetInt32()).ToList();
                if (values.Count != 2 || values[0] != 1)
                    continue;
                // An auto-restored cursor is logged in the same millisecond as the (re)open, before or right after it.
                var sinceOpen = open is { } o ? (long)(ts - o).TotalMilliseconds : 0;
                if (open is null || (!sawHumanSinceOpen && sinceOpen <= 5))
                {
                    echoes++;
                    if (!PickerTiming.IsOpenEcho(Math.Max(0, sinceOpen)))
                        humans.Add(-1); // would be misread as the player's
                    continue;
                }
                sawHumanSinceOpen = true;
                humans.Add(sinceOpen);
            }
        }
        Check("Picker echo: the 9 recorded auto-selections at a reopen (00:45:06.998, 4 at 00:45:15.008, 11:43:58.471, 11:44:02.446, 11:44:12.540, 12:36:01.147) fall inside the echo window",
            echoes == 9 && !humans.Contains(-1),
            $"echoes={echoes}");
        Check("Picker echo: every recorded human pick came after the window (earliest 1.72 s), so it is seen as the player's",
            humans.Count > 0 && humans.All(ms => !PickerTiming.IsOpenEcho(ms)) && humans.Min() == 1722, string.Join(",", humans));
    }

    private static void Shop()
    {
        Console.WriteLine("-- replay: shop (09-21 11:43-11:44, 09-17 20:21) --");
        var fresh = Screens.ReadShop(Load("shop_fresh_discounts.json"));
        Check("Shop 11:43:38: 1,481 tokens, 16 entries (8 feed, 4 items, 4 gear in that order)",
            fresh.Tokens == 1481 && fresh.Stock.Count == 16
            && fresh.Stock.Take(8).All(o => CrucibleItems.Get(o.Row).Type == CrucibleItemType.Feed)
            && fresh.Stock.Skip(8).Take(4).All(o => CrucibleItems.Get(o.Row).Type == CrucibleItemType.Item)
            && fresh.Stock.Skip(12).All(o => CrucibleItems.Get(o.Row).Type == CrucibleItemType.Gear),
            $"{fresh.Tokens} {fresh.Stock.Count}");
        Check("Shop 11:43:38: discounted entries read their price ('30 (-50%)' → 30) and flag",
            fresh.Stock[0] is { Row: 176, Price: 30, Discounted: true } && fresh.Stock[9] is { Row: 96, Price: 150, Discounted: true }
            && fresh.Stock[2] is { Price: 100, Discounted: false });
        Check("Shop 11:43:38: carried 6 items (3 G1 Crucible Ash, Temporal Sand, Tome of the Impervious, Crucible Feather) and 9 gear",
            fresh.Inventory.Items.Count == 6 && fresh.Inventory.Items.Count(r => r == 80) == 3 && fresh.Inventory.Gear.Count == 9
            && fresh.Inventory.Owns(23) && fresh.Inventory.HealingHeld == 3,
            $"items={string.Join(",", fresh.Inventory.Items)} gear={string.Join(",", fresh.Inventory.Gear)}");

        var after = Screens.ReadShop(Load("shop_after_three_feed_buys.json"));
        Check("Shop 11:44:11: three feed entries bought (3, 4, 5), tokens 898",
            after.Tokens == 898 && after.Stock.Where(o => o.Bought).Select(o => o.Index).SequenceEqual([3, 4, 5]));
        var colour = Screens.ReadShop(Load("shop_unaffordable_macro.json"));
        Check("Shop 09-17 20:21: colour-wrapped (unaffordable) prices parse: 840, 950, 1,342, 945",
            colour.Tokens == 686 && colour.Stock[11].Price == 840 && colour.Stock[12].Price == 950
            && colour.Stock[13].Price == 1342 && colour.Stock[15].Price == 945,
            string.Join(",", colour.Stock.Select(o => o.Price)));

        // The policy on the real 11:43:38 stock, board 1 after the ogre (boss ahead), roster = the five recorded familiars.
        var ctx = new RunContext
        {
            Board = 1,
            LastBattle = 5,
            Upcoming = [new UpcomingBattle(0, true, false)],
            CampsAhead = [2],
            HornDemand = new Dictionary<int, double> { [22] = 1, [18] = 1, [20] = 1 },
        };
        var fams = new List<FamiliarState>
        {
            new(28, 0, 100, 0, 2, []), new(22, 1, 100, 0, 2, []), new(18, 2, 40, 0, 2, []),
            new(27, 3, 100, 0, 2, []), new(20, 4, 96, 0, 2, []),
        };
        var first = ItemPolicies.NextPurchase(fresh.Stock, fresh.Tokens, fresh.Inventory, fams, ctx);
        Check("Shop policy on the recorded stock: 3 heals held (reserve 2) → no forced heal; buys something worth it, never a score item",
            first is { } f && f.Score >= ItemValue.Worthwhile && CrucibleItems.Get(f.Row).Role != ItemRole.Score, first?.Reason);
        Check("Shop policy on the recorded stock: owned gear (none of 20/13/56/72 owned) can be chosen; nothing bought twice",
            first is null || !fresh.Inventory.Owns(first.Value.Row));
    }

    private static void Feed()
    {
        Console.WriteLine("-- replay: Beast Feed picker (09-21 12:35, 11:43) --");
        var before = Screens.ReadPetParty(Load("feed_before.json"));
        Check("Feed picker 12:35:52: mode 3, the five recorded familiars read with rows, HP and satiety",
            before.Mode == 3 && before.Familiars.Count >= 5
            && before.Familiars[0] is { Row: 28, Index: 0, HpPercent: 100, SatietyUsed: 0, SatietyMax: 2 }
            && before.Familiars[2] is { Row: 18, HpPercent: 40, CannotEat: true },
            string.Join(" ", before.Familiars.Select(f => $"{f.Row}:{f.HpPercent}%:{f.SatietyUsed}/{f.SatietyMax}:{f.CannotEat}")));
        var lemon = 179;
        Check("Feed picker 12:35:52: Lemon Simular's kin list explains every 'cannot eat' mark (only soulkin Dullahan refused)",
            before.Familiars.All(f => f.CannotEat is not { } c || c == !CrucibleItems.Get(lemon).SuitsKin(Lalalazy.Crucible.BST_Beasts.All[f.Row].Kin)));

        var ctx = new RunContext { Board = 1, HornDemand = new Dictionary<int, double> { [22] = 2, [27] = 1 } };
        var target = FeedPolicy.BestTarget(lemon, before.Familiars, ctx);
        Check("Feed picker 12:35:52: a harmless feed goes to the familiar needed most (sabotender, index 1) — the recorded choice",
            target is { FamiliarRow: 22, FamiliarIndex: 1 } && target.Value.Score >= 0, target?.Reason);

        var fed = Screens.ReadPetParty(Load("feed_after_success.json"));
        var sabo = fed.Familiars.First(f => f.Row == 22);
        Check("Feed picker 12:36:02: the fed familiar shows the feed (row 179) and satiety 1 of 2",
            sabo.Ate(lemon) && sabo.SatietyUsed == 1 && sabo.SatietyMax == 2);
        Check("Feed picker 12:36:02: the same feed is never offered to it again (the recorded refusal was that repeat)",
            !FeedPolicy.CanEat(sabo, lemon, out var why) && why == "already ate it", why);

        var salt = Screens.ReadPetParty(Load("feed_cannot_eat.json"));
        var saltTarget = FeedPolicy.BestTarget(183, salt.Familiars, ctx);
        Check("Feed picker 11:43:59: Salt Simular (soulkin only) → Dullahan, the only familiar the screen allows (recorded choice k=2)",
            saltTarget is { FamiliarRow: 18, FamiliarIndex: 2 }, saltTarget?.Reason);
        var memory = new FeedMemory();
        memory.Update(fed);
        var planned = memory.Familiars([22, 28], new Dictionary<int, int>());
        Check("Feed memory: what the picker showed carries into the shop's planning (sabotender 1/2, ate Lemon)",
            planned[0] is { Row: 22, SatietyUsed: 1, SatietyMax: 2 } && planned[0].Ate(lemon) && planned[1].SatietyUsed == 0);
    }

    private static void Camp()
    {
        Console.WriteLine("-- replay: campsite (09-20 01:40) --");
        var open = Screens.ReadPetParty(Load("camp_open.json"));
        Check("Campsite 01:40:13: mode 4, nobody selected, slime 27% and dullahan 35% the most hurt",
            open.Mode == 4 && open.Familiars.All(f => !f.RestSelected)
            && open.Familiars.First(f => f.Row == 17).HpPercent == 27 && open.Familiars.First(f => f.Row == 18).HpPercent == 35);
        var ctx = new RunContext
        {
            Board = 1,
            PlayerHpPercent = 100,
            HornDemand = open.Familiars.ToDictionary(f => f.Row, _ => 1.0),
        };
        var healthy = CampPolicy.Choose(open.Familiars, 2, ctx);
        Check("Campsite, you healthy, 2 may rest: slime and dullahan (the recorded choice)",
            healthy.Rows.OrderBy(r => r).SequenceEqual([17, 18]) && healthy.Indices.OrderBy(i => i).SequenceEqual([0, 4]) && healthy.HealPercent == 30,
            healthy.Reason);
        var low = CampPolicy.Choose(open.Familiars, 2, new RunContext { Board = 1, PlayerHpPercent = 15, HornDemand = ctx.HornDemand });
        Check("Campsite, you at 15%: survival first takes your 90% self heal over two 30% rests", low.Rows.Count == 0 && low.HealPercent == 90, low.Reason);
        var picked = Screens.ReadPetParty(Load("camp_after_pick2.json"));
        Check("Campsite 01:40:15: the read-back flag (B+75) shows both picks",
            picked.Familiars.Where(f => f.RestSelected).Select(f => f.Index).SequenceEqual([0, 4]));
    }

    private static void TreasureAndBooty()
    {
        Console.WriteLine("-- replay: treasure and spoils --");
        var t0 = Screens.ReadTreasure(Load("treasure_manual_choice0.json"));
        Check("Treasure 11:30:03: 4 choices (Flame Shield, Mythril Greaves, Mystic Boots, Astral Wristlet), 5 items and 3 gear carried",
            t0.Choices.Select(c => c.Row).SequenceEqual([66, 53, 12, 37]) && t0.Inventory.Items.Count == 5 && t0.Inventory.Gear.Count == 3);
        var ctx = new RunContext { Board = 1, Upcoming = [new UpcomingBattle(0, true, false), new UpcomingBattle(5, true, false)] };
        var p0 = ItemPolicies.TreasurePick(t0.Choices, t0.Inventory, [], ctx);
        Check("Treasure 11:30:03: survival first picks Mythril Greaves (damage taken -10%, max HP +5%)", p0 is { Index: 1, Row: 53 }, p0?.Reason);
        var t1 = Screens.ReadTreasure(Load("treasure_manual_choice1.json"));
        var p1 = ItemPolicies.TreasurePick(t1.Choices, t1.Inventory, [], ctx);
        Check("Treasure 12:35:33: G2 Beastmaster Reraiser (95% auto-revive) over Gold Hairpin, Vampiric Essence, Crown of the Wild",
            p1 is { Index: 0, Row: 99 }, p1?.Reason);

        var booty = Screens.ReadBooty(Load("booty_normal.json"));
        Check("Spoils 12:56:46: 201 tokens + 211 offered; loot G2 Antipoison Soul Serum, Astral Wristlet, G2 Wildfire Weakener",
            booty.Tokens == 201 && booty.TokensOffered == 211 && booty.Loot.Select(l => l.Row).SequenceEqual([87, 37, 117]) && booty.Loot.All(l => !l.Taken));
        var take = ItemPolicies.Spoils(booty.Loot.Select(l => (l.Index, l.Row)).ToList(), booty.Inventory, [], ctx);
        Check("Spoils 12:56:46: 2 items + 2 gear carried, everything fits → Take all", take.TakeAll, take.Reason);
        var partly = Screens.ReadBooty(Load("booty_partly_taken.json"));
        Check("Spoils 09-17 20:08: taken flags read per entry (Fang of Water and Temporal Sand taken, Warded Shield not)",
            partly.Loot.Where(l => !l.Taken).Select(l => l.Row).SequenceEqual([63]));
        var four = Screens.ReadBooty(Load("booty_four_loot.json"));
        Check("Spoils (boss node): four entries read", four.Loot.Count == 4 && four.Loot.Select(l => l.Row).SequenceEqual([20, 80, 70, 115]));

        var hud = Load("hud_after_treasure.json");
        var gear = Enumerable.Range(0, 10).Select(s => hud[60 + 5 * s + 3].Int).Where(r => r > 0).ToList();
        Check("HUD after the 12:59:08 treasure: Green Beret (row 11) in the gear list (the read-back the treasure pick uses)",
            gear.Contains(11), string.Join(",", gear));
    }

    private static void Prompts()
    {
        Console.WriteLine("-- Yes/No prompt guard (Addon sheet texts) --");
        (bool Ok, string Why) C(PromptGuard.Kind k, string text, params string[] names) => PromptGuard.Check(k, text, names);

        Check("Purchase: 'Purchase G3 Beast Potion?' with the chosen item → Yes",
            C(PromptGuard.Kind.Purchase, "Purchase G3 Beast Potion?", "G3 Beast Potion").Ok);
        Check("Purchase: another item named → refused",
            !C(PromptGuard.Kind.Purchase, "Purchase G1 Beast Potion?", "G3 Beast Potion").Ok);
        Check("Purchase: 'Discard your G1 Crucible Ash to purchase G3 Beast Potion?' (items full) → refused",
            !C(PromptGuard.Kind.Purchase, "Discard your G1 Crucible Ash to purchase G3 Beast Potion?", "G3 Beast Potion").Ok);
        Check("Feed: 'Purchase Lemon Simular and feed it to your Sabotender?' with both names → Yes; wrong familiar → refused",
            C(PromptGuard.Kind.Feed, "Purchase Lemon Simular and feed it to your Sabotender?", "Lemon Simular", "Sabotender").Ok
            && !C(PromptGuard.Kind.Feed, "Purchase Lemon Simular and feed it to your Worm?", "Lemon Simular", "Sabotender").Ok);
        Check("Treasure: 'Choose Master Shield?' → Yes; 'Choose Fang of Fire?' when Master Shield was chosen → refused",
            C(PromptGuard.Kind.Treasure, "Choose Master Shield?", "Master Shield").Ok
            && !C(PromptGuard.Kind.Treasure, "Choose Fang of Fire?", "Master Shield").Ok);
        Check("Treasure: 'Close the coffer without receiving any items?' → refused",
            !C(PromptGuard.Kind.Treasure, "Close the coffer without receiving any items?\nYou cannot reopen the treasure coffer after closing it.").Ok);
        Check("Take all: every loot name listed → Yes; one missing → refused",
            C(PromptGuard.Kind.TakeAll, "You will receive:\n・ 211 Territory Tokens\n・ G2 Antipoison Soul Serum\n・ Astral Wristlet\n・ G2 Wildfire Weakener\nProceed?",
                "G2 Antipoison Soul Serum", "Astral Wristlet", "G2 Wildfire Weakener").Ok
            && !C(PromptGuard.Kind.TakeAll, "You will receive:\n・ 211 Territory Tokens\n・ Astral Wristlet\nProceed?", "G2 Antipoison Soul Serum").Ok);
        Check("Never: forfeit, suspend, challenge, commence with an empty horn, leave the shop, rest, leave loot — for any kind",
            new[]
            {
                "Forfeit and exit the Crucible of the Unbroken?",
                "Suspend progress and exit the Crucible of the Unbroken?",
                "※Your current team is smaller than the maximum allowance.\nProceed to challenge this board?",
                "At least one battlehorn has not been assigned.\nCommence battle anyway?",
                "Conclude purchasing and leave the shop?\nYou cannot return to the shop after leaving.",
                "Rest and recover 30% of HP for you and your:\n・ Slime\nProceed?",
                "Some loot remains unclaimed. Leave it behind?",
            }.All(t => Enum.GetValues<PromptGuard.Kind>().All(k => !C(k, t).Ok)));
        Check("No prompt expected / empty prompt → never Yes",
            !C(PromptGuard.Kind.None, "Purchase G3 Beast Potion?").Ok && !C(PromptGuard.Kind.Purchase, "").Ok);
    }
}
