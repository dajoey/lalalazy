using System.Globalization;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>The currency families Knightshopper's IPC accepts (its KnightshopperIpc.cs, ApiVersion 2).</summary>
public enum KnightshopperCurrency
{
    BicolorGemstone = 0,
    CompanySeal = 1,
    Gil = 2,
    Hunt = 3,
    MGP = 4,
    PVP = 5,
    Scrip = 6,
    Tomestone = 7,
    Firmament = 8,
    Cosmocredits = 9,
    OccultCrescent = 10,
}

/// <summary>Purchase.StartItems results (Knightshopper IPC doc), mirrored so Core stays adapter-free.</summary>
public enum KnightshopperStartResult
{
    Started = 0,
    Busy = 1,
    InvalidCurrency = 2,
    EmptyList = 3,
    NotReady = 4,
    NotLoggedIn = 5,
    NotAllowed = 6,
    ItemUnavailable = 7,
    InvalidQuantity = 8,
}

/// <summary>Purchase.GetStatus states, mirrored for Core.</summary>
public enum KnightshopperPurchaseState
{
    Unknown = 0,
    Running = 1,
    CancellationRequested = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

/// <summary>
/// Which currency item ids belong to a Knightshopper family. Only ids with solid evidence get a row:
/// every id below is charged by at least one SpecialShop row in the live sheets today (verified by the
/// offline cost probe that shipped with this change, using exactly LoadShops' cost rule), except the
/// grand-company seals (20-22), whose vendors live on other sheet types - that row is dormant but the
/// ids are the canonical seals. Beast-tribe tokens (Ixali Oaknot and friends), vouchers, shards, cowries,
/// trophy crystals, datalogs, manuscripts and the irregular tomestones have NO Knightshopper family and
/// deliberately get none: those items must keep the flag-and-name path they had before this feature.
/// </summary>
public static class KnightshopperCurrencies
{
    private static readonly Dictionary<uint, KnightshopperCurrency> FamilyOfItem = new()
    {
        [1] = KnightshopperCurrency.Gil,                   // Gil itself (thousands of special shops charge it, plus every gil vendor)
        [20] = KnightshopperCurrency.CompanySeal,          // Storm Seal
        [21] = KnightshopperCurrency.CompanySeal,          // Serpent Seal
        [22] = KnightshopperCurrency.CompanySeal,          // Flame Seal
        [25] = KnightshopperCurrency.PVP,                  // Wolf Mark
        [27] = KnightshopperCurrency.Hunt,                 // Allied Seal
        [29] = KnightshopperCurrency.MGP,                  // MGP
        [10307] = KnightshopperCurrency.Hunt,              // Centurio Seal
        [12839] = KnightshopperCurrency.Scrip,             // Blue Crafters' Scrip Token
        [12841] = KnightshopperCurrency.Scrip,             // Blue Gatherers' Scrip Token
        [26533] = KnightshopperCurrency.Hunt,              // Sack of Nuts
        [26807] = KnightshopperCurrency.BicolorGemstone,   // Bicolor Gemstone
        [28063] = KnightshopperCurrency.Firmament,         // Skybuilders' Scrip
        [35831] = KnightshopperCurrency.Tomestone,         // Discal Tomestone
        [38942] = KnightshopperCurrency.Tomestone,         // Orthos Tomestone
        [43548] = KnightshopperCurrency.Tomestone,         // Universal Tomestone
        [45690] = KnightshopperCurrency.Cosmocredits,      // Cosmocredit
        [46728] = KnightshopperCurrency.Tomestone,         // Universal Tomestone 2.0
        [49756] = KnightshopperCurrency.Tomestone,         // Universal Tomestone 3.0
    };

    public static bool TryFamily(uint currencyItemId, out KnightshopperCurrency currency)
    {
        if (FamilyOfItem.TryGetValue(currencyItemId, out var family))
        {
            currency = family;
            return true;
        }
        currency = default;
        return false;
    }

    /// <summary>The family's name as the player knows it (chat lines, run plan).</summary>
    public static string Name(KnightshopperCurrency currency) => currency switch
    {
        KnightshopperCurrency.BicolorGemstone => "Bicolor Gemstone",
        KnightshopperCurrency.CompanySeal => "Company Seal",
        KnightshopperCurrency.Gil => "Gil",
        KnightshopperCurrency.Hunt => "Hunt",
        KnightshopperCurrency.MGP => "MGP",
        KnightshopperCurrency.PVP => "PvP",
        KnightshopperCurrency.Scrip => "Scrip",
        KnightshopperCurrency.Tomestone => "Tomestone",
        KnightshopperCurrency.Firmament => "Firmament",
        KnightshopperCurrency.Cosmocredits => "Cosmocredits",
        KnightshopperCurrency.OccultCrescent => "Occult Crescent",
        _ => currency.ToString(),
    };
}

/// <summary>
/// Splits a dispatch plan into what Knightshopper buys (Groups) and what keeps today's flag-and-name path
/// (Fallback). The split is pure so the harness can drive it; the adapter only carries the results over the
/// IPC. See the class summary on <see cref="KnightshopperRouting"/> for the rules.
/// </summary>
public static class KnightshopperRouting
{
    /// <summary>One item Knightshopper should buy. Quantity is what the run is short; TargetTotal is what
    /// StartItems asks for (owned count plus the shortage) so a re-run can never ask for more than the target.</summary>
    public sealed record KsItem(
        uint ItemId,
        int Quantity,
        int TargetTotal,
        DispatchPlan.Purchase? Vendor,
        DispatchPlan.CurrencyPurchase? CurrencyShop);

    /// <summary>All items for one currency, bought with one StartItems call.</summary>
    public sealed record KsGroup(KnightshopperCurrency Currency, IReadOnlyList<KsItem> Items);

    /// <summary>The routing outcome: groups to hand to Knightshopper, and the plan the rest of the wave runs with.</summary>
    public sealed record KsPlan(IReadOnlyList<KsGroup> Groups, DispatchPlan.Plan Fallback);

    /// <summary>
    /// Partition a plan. Enabled off (setting off, or Knightshopper not available) returns the plan untouched
    /// and no groups. Otherwise: every gil-vendor buy routes under Gil; every currency-shop buy routes only
    /// when ALL of its offer's costs map to ONE Knightshopper family (multi-currency prices, beast-tribe
    /// tokens, vouchers, shards and anything else unmapped keep today's exact path). Market-board items never
    /// route. Quantities stay exactly what the plan says - the routing never adds units.
    /// </summary>
    public static KsPlan Partition(DispatchPlan.Plan plan, bool enabled, Func<uint, long?> gilPrice)
    {
        if (!enabled) return new KsPlan(Array.Empty<KsGroup>(), plan);

        var gilItems = new List<KsItem>();
        var byFamily = new Dictionary<KnightshopperCurrency, List<KsItem>>();
        var keptVendor = new List<DispatchPlan.Purchase>();
        var keptCurrency = new List<DispatchPlan.CurrencyPurchase>();

        // The Vendor channel only ever holds gil-vendor buys, and gil is a Knightshopper family.
        foreach (var v in plan.Vendor)
            gilItems.Add(new KsItem(v.ItemId, v.Quantity, v.Owned + v.Quantity, v, null));

        foreach (var c in plan.CurrencyShop)
        {
            var family = FamilyOf(c.Offer);
            if (family is null)
            {
                keptCurrency.Add(c);
                continue;
            }
            if (!byFamily.TryGetValue(family.Value, out var list))
                byFamily[family.Value] = list = new List<KsItem>();
            list.Add(new KsItem(c.ItemId, c.Quantity, c.Owned + c.Quantity, null, c));
        }

        var groups = new List<KsGroup>();
        if (gilItems.Count > 0)
            groups.Add(new KsGroup(KnightshopperCurrency.Gil, gilItems));
        groups.AddRange(byFamily.OrderBy(kv => kv.Key).Select(kv => new KsGroup(kv.Key, kv.Value)));

        var fallback = groups.Count == 0 ? plan : plan with { Vendor = keptVendor, CurrencyShop = keptCurrency };
        return new KsPlan(groups, fallback);
    }

    /// <summary>Every cost of an offer must map to ONE Knightshopper family, else the offer does not route.</summary>
    private static KnightshopperCurrency? FamilyOf(SpecialShopCandidate offer)
    {
        if (offer.Costs.Count == 0) return null;
        KnightshopperCurrency? family = null;
        foreach (var cost in offer.Costs)
        {
            if (!KnightshopperCurrencies.TryFamily(cost.ItemId, out var f)) return null;
            if (family is null) family = f;
            else if (family != f) return null;
        }
        return family;
    }

    /// <summary>
    /// Put failed groups' items back into the fallback plan, in their original channels and with their
    /// original records - so a failed Knightshopper purchase degrades to exactly the pre-0.1.7.7
    /// flag-and-name wave, vendor walk and all.
    /// </summary>
    public static DispatchPlan.Plan MergeFailures(DispatchPlan.Plan fallback, IReadOnlyList<KsGroup> failed)
    {
        if (failed.Count == 0) return fallback;
        var vendor = fallback.Vendor.ToList();
        var currency = fallback.CurrencyShop.ToList();
        foreach (var group in failed)
        foreach (var item in group.Items)
        {
            if (item.Vendor is not null) vendor.Add(item.Vendor);
            else if (item.CurrencyShop is not null) currency.Add(item.CurrencyShop);
        }
        return fallback with { Vendor = vendor, CurrencyShop = currency };
    }

    /// <summary>
    /// The run-plan lines (chat at dispatch start, /lcraft plan): what Knightshopper will buy, in which
    /// currency, at what cost - printed BEFORE anything is spent.
    /// </summary>
    public static IReadOnlyList<string> PurchaseLines(KsPlan ksPlan, Func<uint, string> name, Func<uint, long?> gilPrice) =>
        ksPlan.Groups
            .Select(g => $"knightshopper {KnightshopperCurrencies.Name(g.Currency)}: {ItemsPhrase(g, name)} - {CostPhrase(g, gilPrice)}")
            .ToList();

    private static string ItemsPhrase(KsGroup group, Func<uint, string> name) =>
        string.Join(", ", group.Items.Select(i => $"{name(i.ItemId)} x{i.Quantity}"));

    /// <summary>
    /// The group's cost, per cost item. Gil-vendor buys are estimates (the sheet's mid price), so the gil
    /// part reads "about N gil"; an unreadable price turns it into a lower bound ("at least"). Currency-offer
    /// costs are exact. Rendered culture-invariant so logs and tests agree everywhere.
    /// </summary>
    private static string CostPhrase(KsGroup group, Func<uint, long?> gilPrice)
    {
        var totals = new SortedDictionary<uint, (long Qty, string Name, string Plural, bool Estimated)>();
        var anyUnknown = false;
        foreach (var item in group.Items)
        {
            if (item.Vendor is not null)
            {
                var unit = gilPrice(item.ItemId);
                if (unit is null) { anyUnknown = true; continue; }
                AddCost(totals, 1, "gil", "gil", unit.Value * item.Quantity, Estimated: true);
            }
            else if (item.CurrencyShop is not null)
            {
                foreach (var cost in item.CurrencyShop.Offer.CostFor(item.Quantity))
                    AddCost(totals, cost.ItemId, cost.Name, cost.Plural, cost.Quantity, Estimated: false);
            }
        }

        if (totals.Count == 0)
            return anyUnknown ? "for an unknown amount of gil" : "for nothing";

        var parts = new List<string>();
        foreach (var (id, (qty, itemName, plural, estimated)) in totals)
        {
            var count = qty.ToString("N0", CultureInfo.InvariantCulture);
            if (id == 1)
            {
                var qualifier = anyUnknown ? "at least" : estimated ? "about" : "";
                parts.Add(qualifier.Length == 0 ? $"{count} gil" : $"{qualifier} {count} gil");
            }
            else
                parts.Add($"{count} {(qty == 1 ? itemName : string.IsNullOrEmpty(plural) ? itemName : plural)}");
        }
        return "for " + string.Join(", ", parts);
    }

    private static void AddCost(
        SortedDictionary<uint, (long Qty, string Name, string Plural, bool Estimated)> totals,
        uint id, string name, string plural, long qty, bool Estimated)
    {
        var existing = totals.TryGetValue(id, out var e) ? e : (0, "", "", false);
        totals[id] = (
            existing.Item1 + qty,
            existing.Item2.Length == 0 ? name : existing.Item2,
            existing.Item3.Length == 0 ? plural : existing.Item3,
            existing.Item4 || Estimated);
    }
}

/// <summary>Plain-language failure lines for the chat. No enum names, no IPC jargon - what happened and what to do.</summary>
public static class KnightshopperMessages
{
    public static string Start(KnightshopperStartResult result, string detail = "") => result switch
    {
        KnightshopperStartResult.Started => "",
        KnightshopperStartResult.Busy => "Knightshopper is already busy with another purchase",
        KnightshopperStartResult.NotAllowed => "LazyCrafter is not on Knightshopper's allowed-plugins IPC list - add it in Knightshopper's settings, then run the cart again",
        KnightshopperStartResult.NotReady or KnightshopperStartResult.NotLoggedIn =>
            "Knightshopper is not ready yet (still loading, or nobody is logged in)",
        KnightshopperStartResult.InvalidCurrency or KnightshopperStartResult.EmptyList or KnightshopperStartResult.InvalidQuantity =>
            WithDetail("Knightshopper rejected the request as invalid", detail),
        KnightshopperStartResult.ItemUnavailable => WithDetail("no unlocked vendor carries these items for that currency", detail),
        _ => detail.Length == 0 ? "the purchase did not start" : detail,
    };

    public static string Poll(KnightshopperPurchaseState state, string detail) => state switch
    {
        KnightshopperPurchaseState.Failed => WithDetail("the purchase failed", detail),
        KnightshopperPurchaseState.Cancelled => "the purchase was cancelled",
        KnightshopperPurchaseState.Unknown => "Knightshopper no longer knows this purchase (was it reloaded mid-run?)",
        KnightshopperPurchaseState.CancellationRequested => "the purchase is still cancelling",
        _ => detail.Length == 0 ? "the purchase is still running" : detail,
    };

    public static string Timeout(TimeSpan waited) =>
        $"the purchase did not finish within {waited.TotalMinutes:0} minutes";

    private static string WithDetail(string line, string detail) =>
        detail.Length == 0 ? line : $"{line} - {detail}";
}
