namespace LazyFashionReport.Core;

/// <summary>Every way a missing fashion piece can be obtained, best-first order for display.
/// Craft = a Recipe-sheet entry exists; GilVendor = sold for gil; SpecialShop = currency shop
/// (GC seals, beast-tribe tokens, ...); Market = other players. The planner returns the FIRST
/// that resolves, and the UI renders the label; the Shop button exists only for the two
/// vendor kinds (both resolve to a placed, named NPC).</summary>
public enum BuySource
{
    Craft,
    GilVendor,
    SpecialShop,
    Market,
    None,
}

/// <summary>One cost line of a currency-shop offer ("1500 Storm Seals"). A currency is just an
/// item row; the phrase keeps the game's own plural so "1,500 Storm Seal" never renders.</summary>
public sealed record ShopCost(uint ItemId, string Name, int Quantity, string Plural)
{
    public string Phrase => $"{Quantity:N0} {(Quantity == 1 ? Name : string.IsNullOrEmpty(Plural) ? Name : Plural)}";
}

/// <summary>The resolved buy target for one missing piece.</summary>
public sealed record BuyOption
{
    public required BuySource Source { get; init; }
    /// <summary>Recipe id when Source == Craft.</summary>
    public RecipeOption? Recipe { get; init; }
    /// <summary>Shop id when Source is a vendor kind.</summary>
    public uint ShopId { get; init; }
    /// <summary>"Vendor Name (Zone 11.2, 8.4)" or "currency shop" - already display-ready.</summary>
    public string Label { get; init; } = "";
    /// <summary>Currency costs for SpecialShop (empty for gil vendors - the label carries the price).</summary>
    public IReadOnlyList<ShopCost> Costs { get; init; } = Array.Empty<ShopCost>();
    /// <summary>Territory for the map flag (0 = no flag).</summary>
    public uint TerritoryId { get; init; }
    public uint MapId { get; init; }
    public float MapX { get; init; }
    public float MapY { get; init; }
    public bool HasMapFlag => TerritoryId != 0;

    /// <summary>Median market-board price from Universalis, when fetched (null = not fetched /
    /// no data). Rendering says "check the market board" rather than a stale number.</summary>
    public uint? MarketMedian { get; init; }
}

/// <summary>
/// Pure resolver for the buy leg (fetch-missing step two, v0.3.0.0): given the game-side
/// lookups (recipe, gil vendor, placed special-shop offer, marketability), pick the best
/// source for one missing item. Priority: craft (the existing leg) > gil vendor (exact gil
/// price, no currency math) > special shop (named, placed, costed offers only) > market.
/// A gil vendor that is not placed still wins over market for LABELLING (the price is real),
/// it just gets no map flag; an unplaced special shop is dropped entirely per the LazyCrafter
/// D1 lesson (an unplaced currency vendor is a dead end, the market board is actionable).
/// Offline-harness-tested with fake lookups.
/// </summary>
public static class BuyResolver
{
    /// <param name="recipeFor">Item -> recipe, or null.</param>
    /// <param name="gilVendorFor">Item -> (price, placed vendor label) or null. Label null = not placed.</param>
    /// <param name="specialShopFor">Item -> best placed costed offer, or null.</param>
    /// <param name="marketable">Whether the item sells on the market board.</param>
    public static BuyOption Resolve(
        uint itemId,
        Func<uint, RecipeOption?>? recipeFor,
        Func<uint, (uint Price, string? PlacedLabel, uint TerritoryId, uint MapId, float X, float Y)?>? gilVendorFor,
        Func<uint, BuyOption?>? specialShopFor,
        bool marketable)
    {
        if (recipeFor?.Invoke(itemId) is { } recipe)
            return new BuyOption { Source = BuySource.Craft, Recipe = recipe, Label = "" };

        if (gilVendorFor?.Invoke(itemId) is { } vendor)
        {
            var price = vendor.Price > 0 ? $"{vendor.Price:N0} gil" : "gil";
            return new BuyOption
            {
                Source = BuySource.GilVendor,
                Label = vendor.PlacedLabel is null ? $"gil vendor ({price})" : $"{vendor.PlacedLabel} - {price}",
                TerritoryId = vendor.TerritoryId,
                MapId = vendor.MapId,
                MapX = vendor.X,
                MapY = vendor.Y,
            };
        }

        if (specialShopFor?.Invoke(itemId) is { } shop)
            return shop with { };            // already fully resolved by the adapter

        if (marketable)
            return new BuyOption { Source = BuySource.Market, Label = "market board" };

        return new BuyOption { Source = BuySource.None, Label = "no known source" };
    }
}
