using System.Globalization;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Knightshopper routing (0.1.7.7): during an explicit dispatch, materials a vendor sells — gil vendors, and
/// currency vendors priced in one currency Knightshopper supports — are handed to Knightshopper's IPC
/// (StartItems, target inventory totals) instead of the walk-there-and-stop. Everything else keeps today's
/// flag-and-name path, and any refusal merges the items back into that path.
///
/// <para>
/// Evidence for the currency table: an offline probe over the live SpecialShop sheets (same cost rule as
/// LuminaGameData.LoadShops) enumerated every cost item actually charged. Knightshopper's IPC doc names the
/// families; the probe pinned the item ids. Beast-tribe tokens (Ixali Oaknot and friends) are deliberately NOT
/// mapped: Knightshopper has no family for them, so they must keep the exact pre-0.1.7.7 route.
/// </para>
/// </summary>
internal static class KnightshopperTests
{
    // Real ids from the sheets/Knightshopper's own docs, so a log line can be matched against a check by eye.
    private const uint Coal = World.Coal;            // gil vendor in the World fixture (3 gil)
    private const uint Emery = 7601;                 // the card t_b431de3a item, reused for currency cases
    private const uint StormSeal = 20;               // CompanySeal family (GC shops use other sheets, dormant row)
    private const uint WolfMark = 25;                // PVP
    private const uint AlliedSeal = 27;              // Hunt
    private const uint Mgp = 29;                     // MGP
    private const uint CenturioSeal = 10307;         // Hunt
    private const uint SackOfNuts = 26533;           // Hunt
    private const uint BicolorGemstone = 26807;      // BicolorGemstone
    private const uint SkybuildersScrip = 28063;     // Firmament
    private const uint Cosmocredit = 45690;          // Cosmocredits
    private const uint ScripToken = 12839;           // Scrip (Blue Crafters' Scrip Token)
    private const uint IxaliOaknot = 21073;          // beast-tribe: NO Knightshopper family, must never route
    private const uint GemVoucher = 35833;           // a voucher: deliberately NOT mapped
    private const uint Gil = 1;

    private static string Name(uint id) => id switch
    {
        Coal => "Coal",
        Emery => "Emery",
        World.Ore => "Ore",
        StormSeal => "Storm Seal",
        IxaliOaknot => "Ixali Oaknot",
        _ => $"#{id}",
    };

    private static Func<uint, long?> GilPrices(params (uint Item, long Price)[] prices) =>
        id => prices.FirstOrDefault(p => p.Item == id) is { Item: not 0 } hit ? hit.Price : null;

    /// <summary>A placed vendor in a teleportable zone - the shape the routing requires (mirrors SpecialShopTests).</summary>
    private static SpecialShopCandidate Offer(uint item, params (uint Cost, int Qty)[] costs) => new(
        item, 1769525 + item, $"{Name(item)} Exchange", "Test NPC", "Limsa Lominsa", ReceiveQuantity: 1,
        costs.Select(c => new SpecialShopCost(c.Cost, Name(c.Cost), c.Qty, c.Qty == 1 ? Name(c.Cost) : Name(c.Cost) + "s")).ToList(),
        new VendorCandidate(1002389, 128, 7, 24.9f, 22.7f, AetheryteId: 3, 20f, 20f, AetheryteDistance: 6f));

    /// <summary>A plan whose only buy is Coal at the gil vendor (World fixture: 3 gil each, but prices come from gilPrice).</summary>
    private static DispatchPlan.Plan VendorPlan(int have, int need)
    {
        var leaf = new IngredientLeaf(Coal, Need: need, Have: have, [SourceKind.GilVendor], EffortTier.SomeEffort);
        var data = new FakeGameData().GilVendor(Coal, 3).Marketable(Coal);
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        return DispatchPlan.Build([], [leaf], graph, ventures, [], null, null, null);
    }

    /// <summary>A plan whose only buy is <paramref name="offer"/>'s item at a currency vendor.</summary>
    private static DispatchPlan.Plan CurrencyPlan(SpecialShopCandidate offer, int need = 1, int have = 0)
    {
        var data = new FakeGameData().Recipe(1, 30406, 1, World.Bsm, 60, (offer.ItemId, 1)).Marketable(offer.ItemId);
        var leaf = new IngredientLeaf(offer.ItemId, Need: need, Have: have, [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort);
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        var ctx = new SpecialShopContext(_ => new[] { offer }, _ => 1_000_000, true);
        return DispatchPlan.Build([], [leaf], graph, ventures, [], null, null, ctx);
    }

    /// <summary>One of each channel: a gil-vendor buy, a beast-tribe currency buy (never routable), a market buy.</summary>
    private static DispatchPlan.Plan MixedPlan()
    {
        var ixali = Offer(Emery, (IxaliOaknot, 7));
        var data = new FakeGameData()
            .GilVendor(Coal, 3).Marketable(Coal).Marketable(Emery).Marketable(World.MarketOnly)
            .Recipe(1, 30406, 1, World.Bsm, 60, (Emery, 1));
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        var leaves = new[]
        {
            new IngredientLeaf(Coal, Need: 3, Have: 1, [SourceKind.GilVendor], EffortTier.SomeEffort),
            new IngredientLeaf(Emery, Need: 1, Have: 0, [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort),
            new IngredientLeaf(World.MarketOnly, Need: 1, Have: 0, [SourceKind.Market], EffortTier.SomeEffort),
        };
        var ctx = new SpecialShopContext(id => id == Emery ? new[] { ixali } : Array.Empty<SpecialShopCandidate>(), _ => 1_000_000, true);
        return DispatchPlan.Build([], leaves, graph, ventures, [], null, null, ctx);
    }

    private static readonly Func<uint, long?> CoalAt3 = GilPrices((Coal, 3));
    private static readonly Func<uint, string> Names = Name;

    public static IEnumerable<(string Name, Func<bool> Check)> Tests => new (string, Func<bool>)[]
    {
        // ------------------------------------------------- K1: what routes

        ("K1 a gil-vendor item routes under Knightshopper Gil with the target total (owned plus needed)", () =>
        {
            var p = VendorPlan(have: 2, need: 5);
            if (p.Vendor.Count != 1 || p.Vendor[0] is not { ItemId: Coal, Quantity: 3, Owned: 2 }) return false;
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 1 && ks.Groups[0].Currency == KnightshopperCurrency.Gil
                && ks.Groups[0].Items.Count == 1
                && ks.Groups[0].Items[0] is { ItemId: Coal, Quantity: 3, TargetTotal: 5 }
                && ks.Fallback.Vendor.Count == 0;
        }),

        ("K1 a currency-shop item priced in one known currency routes under that currency (owned carries over)", () =>
        {
            var p = CurrencyPlan(Offer(Emery, (StormSeal, 1500)), need: 3, have: 2);
            if (p.CurrencyShop.Count != 1 || p.CurrencyShop[0].Owned != 2) return false;
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 1 && ks.Groups[0].Currency == KnightshopperCurrency.CompanySeal
                && ks.Groups[0].Items[0] is { ItemId: Emery, Quantity: 1, TargetTotal: 3 }
                && ks.Fallback.CurrencyShop.Count == 0;
        }),

        ("K1 a special shop that charges only gil routes under Gil as well (thousands of shops do exactly this)", () =>
        {
            var p = CurrencyPlan(Offer(Emery, (Gil, 5000)));
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 1 && ks.Groups[0].Currency == KnightshopperCurrency.Gil
                && ks.Groups[0].Items[0].CurrencyShop is not null
                && ks.Fallback.CurrencyShop.Count == 0;
        }),

        ("K1 a multi-currency offer never routes (one price, two families)", () =>
        {
            var p = CurrencyPlan(Offer(Emery, (StormSeal, 1500), (Gil, 100)));
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 0 && ks.Fallback.CurrencyShop.Count == 1;
        }),

        ("K1 an offer priced in a currency Knightshopper does not support stays put (beast-tribe tokens)", () =>
        {
            var p = CurrencyPlan(Offer(Emery, (IxaliOaknot, 7)));
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 0 && ks.Fallback.CurrencyShop.Count == 1
                && ks.Fallback.CurrencyShop[0].Where.Contains("Ixali");
        }),

        ("K1 market-board items never route", () =>
        {
            var data = new FakeGameData().Marketable(Coal);
            var graph = new RecipeGraph(data);
            var ventures = new VentureResolver(data);
            var leaf = new IngredientLeaf(Coal, Need: 1, Have: 0, [SourceKind.Market], EffortTier.SomeEffort);
            var p = DispatchPlan.Build([], [leaf], graph, ventures, [], null, null, null);
            if (p.Market.Count != 1) return false;
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 0 && ks.Fallback.Market.Count == 1;
        }),

        ("K1 the fallback plan keeps everything the routing did not take, in its own channel", () =>
        {
            var p = MixedPlan();
            if (p.Vendor.Count != 1 || p.CurrencyShop.Count != 1 || p.Market.Count != 1) return false;
            var ks = KnightshopperRouting.Partition(p, enabled: true, CoalAt3);
            return ks.Groups.Count == 1 && ks.Groups[0].Currency == KnightshopperCurrency.Gil
                && ks.Fallback.Vendor.Count == 0
                && ks.Fallback.CurrencyShop.Count == 1 && ks.Fallback.CurrencyShop[0].ItemId == Emery
                && ks.Fallback.Market.Count == 1 && ks.Fallback.Market[0].ItemId == World.MarketOnly;
        }),

        ("K2 with the routing disabled the plan comes back untouched and no groups exist", () =>
        {
            var p = MixedPlan();
            var ks = KnightshopperRouting.Partition(p, enabled: false, CoalAt3);
            return ks.Groups.Count == 0 && ks.Fallback == p;
        }),

        ("K2 an empty plan routes nothing", () =>
            KnightshopperRouting.Partition(DispatchPlan.Build([], [], new RecipeGraph(new FakeGameData()), new VentureResolver(new FakeGameData()), [], null, null, null), true, CoalAt3).Groups.Count == 0),

        // ------------------------------------------------- K3: never spend twice

        ("K3 after the buy the same cart asks again only for what is still missing", () =>
        {
            // First run: 2 owned, 5 needed -> buy 3 to a target of 5.
            var first = KnightshopperRouting.Partition(VendorPlan(have: 2, need: 5), true, CoalAt3);
            if (first.Groups[0].Items[0].TargetTotal != 5) return false;
            // Second run with the purchase complete: nothing missing, nothing to buy at all.
            var second = KnightshopperRouting.Partition(VendorPlan(have: 5, need: 5), true, CoalAt3);
            return second.Groups.Count == 0;
        }),

        ("K3 a re-run against the same shortage asks for the same target, never a higher one", () =>
        {
            // The purchase did not happen (failure, cancel, whatever): the next dispatch sees the same
            // owned/need and must produce the SAME target total, not owned-plus-previous-request.
            var a = KnightshopperRouting.Partition(VendorPlan(have: 2, need: 5), true, CoalAt3).Groups[0].Items[0].TargetTotal;
            var b = KnightshopperRouting.Partition(VendorPlan(have: 2, need: 5), true, CoalAt3).Groups[0].Items[0].TargetTotal;
            return a == 5 && b == 5;
        }),

        // ------------------------------------------------- K4: failure merges back

        ("K4 a failed group merges back into the fallback plan's own channel with its original record", () =>
        {
            var p = MixedPlan();
            var ks = KnightshopperRouting.Partition(p, true, CoalAt3);
            var merged = KnightshopperRouting.MergeFailures(ks.Fallback, ks.Groups);
            return merged.Vendor.Count == 1 && merged.Vendor[0] is { ItemId: Coal, Quantity: 2, Owned: 1 }
                && merged.CurrencyShop.Count == 1 && merged.CurrencyShop[0].ItemId == Emery
                && merged.Market.Count == 1;
        }),

        ("K4 merging nothing changes nothing", () =>
        {
            var p = MixedPlan();
            var ks = KnightshopperRouting.Partition(p, true, CoalAt3);
            var merged = KnightshopperRouting.MergeFailures(ks.Fallback, Array.Empty<KnightshopperRouting.KsGroup>());
            return merged.Vendor.Count == ks.Fallback.Vendor.Count
                && merged.CurrencyShop.Count == ks.Fallback.CurrencyShop.Count
                && merged.Market.Count == ks.Fallback.Market.Count;
        }),

        ("K4 a failed currency group merges back with its offer, so the stop still names the vendor and price", () =>
        {
            var p = CurrencyPlan(Offer(Emery, (StormSeal, 1500)));
            var ks = KnightshopperRouting.Partition(p, true, CoalAt3);
            var merged = KnightshopperRouting.MergeFailures(ks.Fallback, ks.Groups);
            return merged.CurrencyShop.Count == 1
                && merged.CurrencyShop[0].Where.Contains("Test NPC") && merged.CurrencyShop[0].Where.Contains("1,500");
        }),

        // ------------------------------------------------- K5: plain failure messages

        ("K5 a refusal because LazyCrafter is not on Knightshopper's allowed list says exactly that", () =>
        {
            var line = KnightshopperMessages.Start(KnightshopperStartResult.NotAllowed);
            return line.Contains("allowed") && line.Contains("LazyCrafter") && line.Contains("Knightshopper");
        }),

        ("K5 busy, not-ready and unavailable refusals render plain, non-technical lines", () =>
        {
            var busy = KnightshopperMessages.Start(KnightshopperStartResult.Busy);
            var notReady = KnightshopperMessages.Start(KnightshopperStartResult.NotReady);
            var notLoggedIn = KnightshopperMessages.Start(KnightshopperStartResult.NotLoggedIn);
            var unavailable = KnightshopperMessages.Start(KnightshopperStartResult.ItemUnavailable);
            return busy.Contains("busy") && notReady.Length > 0 && notLoggedIn.Length > 0
                && unavailable.Contains("vendor");
        }),

        ("K5 a started request renders no failure text at all", () =>
            KnightshopperMessages.Start(KnightshopperStartResult.Started) == ""),

        ("K5 plugin detail rides along on the failure line", () =>
            KnightshopperMessages.Start(KnightshopperStartResult.ItemUnavailable, "no shop unlocked").Contains("no shop unlocked")),

        ("K5 poll failures and timeouts render plain lines", () =>
        {
            var failed = KnightshopperMessages.Poll(KnightshopperPurchaseState.Failed, "bags full");
            var cancelled = KnightshopperMessages.Poll(KnightshopperPurchaseState.Cancelled, "");
            var unknown = KnightshopperMessages.Poll(KnightshopperPurchaseState.Unknown, "");
            var timeout = KnightshopperMessages.Timeout(TimeSpan.FromMinutes(10));
            return failed.Contains("failed") && failed.Contains("bags full")
                && cancelled.Contains("cancel") && unknown.Length > 0
                && timeout.Contains("10 minutes");
        }),

        // ------------------------------------------------- K6: the run plan says what will be bought

        ("K6 the run-plan line names the currency, the items and the estimated cost before anything is spent", () =>
        {
            var p = VendorPlan(have: 1, need: 4);
            var ks = KnightshopperRouting.Partition(p, true, CoalAt3);
            var lines = KnightshopperRouting.PurchaseLines(ks, Names, CoalAt3);
            return lines.Count == 1 && lines[0].Contains("knightshopper") && lines[0].Contains("Gil")
                && lines[0].Contains("Coal x3") && lines[0].Contains("about 9 gil");
        }),

        ("K6 an unreadable gil price renders an honest unknown-cost line", () =>
        {
            var p = VendorPlan(have: 0, need: 2);
            var ks = KnightshopperRouting.Partition(p, true, _ => null);
            var lines = KnightshopperRouting.PurchaseLines(ks, Names, _ => null);
            return lines.Count == 1 && lines[0].Contains("Coal x2") && lines[0].Contains("unknown amount of gil");
        }),

        ("K6 a group with one priced and one unpriced item shows a lower-bound cost", () =>
        {
            // Two gil-vendor items: Coal priced at 3, Ore unpriced -> the known part becomes a lower bound.
            var data = new FakeGameData().GilVendor(Coal, 3).GilVendor(World.Ore, 12).Marketable(Coal).Marketable(World.Ore);
            var graph = new RecipeGraph(data);
            var ventures = new VentureResolver(data);
            var leaves = new[]
            {
                new IngredientLeaf(Coal, Need: 2, Have: 0, [SourceKind.GilVendor], EffortTier.SomeEffort),
                new IngredientLeaf(World.Ore, Need: 1, Have: 0, [SourceKind.GilVendor], EffortTier.SomeEffort),
            };
            var p = DispatchPlan.Build([], leaves, graph, ventures, [], null, null, null);
            var ks = KnightshopperRouting.Partition(p, true, GilPrices((Coal, 3)));
            var lines = KnightshopperRouting.PurchaseLines(ks, Names, GilPrices((Coal, 3)));
            return lines.Count == 1 && lines[0].Contains("Coal x2") && lines[0].Contains("Ore x1")
                && lines[0].Contains("at least 6 gil");
        }),

        ("K6 currency costs render exactly, in the right plural", () =>
        {
            var p = CurrencyPlan(Offer(Emery, (StormSeal, 1500)), need: 2);
            var ks = KnightshopperRouting.Partition(p, true, CoalAt3);
            var lines = KnightshopperRouting.PurchaseLines(ks, Names, CoalAt3);
            return lines.Count == 1 && lines[0].Contains("Company Seal") && lines[0].Contains("Emery x2")
                && lines[0].Contains("3,000 Storm Seals");
        }),

        ("K6 several groups render one line each", () =>
        {
            var data = new FakeGameData().GilVendor(Coal, 3).Marketable(Coal).Marketable(Emery)
                .Recipe(1, 30406, 1, World.Bsm, 60, (Emery, 1));
            var graph = new RecipeGraph(data);
            var ventures = new VentureResolver(data);
            var seal = Offer(Emery, (StormSeal, 1500));
            var leaves = new[]
            {
                new IngredientLeaf(Coal, Need: 2, Have: 0, [SourceKind.GilVendor], EffortTier.SomeEffort),
                new IngredientLeaf(Emery, Need: 1, Have: 0, [SourceKind.SpecialShop, SourceKind.Market], EffortTier.SomeEffort),
            };
            var ctx = new SpecialShopContext(id => id == Emery ? new[] { seal } : Array.Empty<SpecialShopCandidate>(), _ => 1_000_000, true);
            var p = DispatchPlan.Build([], leaves, graph, ventures, [], null, null, ctx);
            var ks = KnightshopperRouting.Partition(p, true, CoalAt3);
            var lines = KnightshopperRouting.PurchaseLines(ks, Names, CoalAt3);
            return ks.Groups.Count == 2 && lines.Count == 2
                && lines.Any(l => l.Contains("Gil") && l.Contains("Coal"))
                && lines.Any(l => l.Contains("Company Seal") && l.Contains("Emery"));
        }),

        // ------------------------------------------------- K7: the currency table

        ("K7 every currency family Knightshopper supports maps from its real sheet id", () =>
        {
            bool Maps(uint id, KnightshopperCurrency c) =>
                KnightshopperCurrencies.TryFamily(id, out var got) && got == c;
            return Maps(Gil, KnightshopperCurrency.Gil)
                && Maps(StormSeal, KnightshopperCurrency.CompanySeal)
                && Maps(WolfMark, KnightshopperCurrency.PVP)
                && Maps(AlliedSeal, KnightshopperCurrency.Hunt)
                && Maps(CenturioSeal, KnightshopperCurrency.Hunt)
                && Maps(SackOfNuts, KnightshopperCurrency.Hunt)
                && Maps(Mgp, KnightshopperCurrency.MGP)
                && Maps(BicolorGemstone, KnightshopperCurrency.BicolorGemstone)
                && Maps(SkybuildersScrip, KnightshopperCurrency.Firmament)
                && Maps(Cosmocredit, KnightshopperCurrency.Cosmocredits)
                && Maps(ScripToken, KnightshopperCurrency.Scrip);
        }),

        ("K7 items that are not a supported currency never map (beast tribes, vouchers, shards, tokens)", () =>
            !KnightshopperCurrencies.TryFamily(IxaliOaknot, out _)
            && !KnightshopperCurrencies.TryFamily(GemVoucher, out _)
            && !KnightshopperCurrencies.TryFamily(2, out _)          // Fire Shard
            && !KnightshopperCurrencies.TryFamily(37549, out _)      // Seafarer's Cowrie
            && !KnightshopperCurrencies.TryFamily(0, out _)),

        ("K7 family names render as the player knows them", () =>
            KnightshopperCurrencies.Name(KnightshopperCurrency.Gil) == "Gil"
            && KnightshopperCurrencies.Name(KnightshopperCurrency.CompanySeal) == "Company Seal"
            && KnightshopperCurrencies.Name(KnightshopperCurrency.MGP) == "MGP"
            && KnightshopperCurrencies.Name(KnightshopperCurrency.BicolorGemstone) == "Bicolor Gemstone"),
    };
}
