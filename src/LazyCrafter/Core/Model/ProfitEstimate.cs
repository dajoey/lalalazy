namespace LazyCrafter.Core.Model;

/// <summary>Which Universalis number stands in for "what this sells for" (Scope §3.3, selectable).</summary>
public enum RevenueBasis
{
    /// <summary>Cheapest current listing at DC scope (default; overstates on thin markets - see velocity cap).</summary>
    MinListing,
    /// <summary>Median current listing.</summary>
    MedianListing,
    /// <summary>Average realised sale price.</summary>
    AvgSale,
}

/// <summary>
/// One recipe's money picture for one run of <see cref="ProfitModel.Evaluate"/> (Plan §Phase 2 task 1).
/// All gil figures are per craft (i.e. per <c>ResultAmount</c> units) unless the name says otherwise.
/// Both cost columns are always present (Scope §0 "Both columns").
/// </summary>
public sealed record ProfitEstimate(
    uint RecipeId,
    uint ResultItemId,
    /// <summary>Units of the result one evaluation covers (<c>ResultAmount x crafts</c>).</summary>
    int Units,
    /// <summary>Whether the margin/per-day figures were computed against the HQ price row.</summary>
    bool Hq,
    /// <summary>Revenue for <see cref="Units"/> at the chosen basis; <c>null</c> when the item has no NQ price.</summary>
    long? RevenueNq,
    /// <summary>Same for HQ; <c>null</c> when no HQ price exists (untradeable, or the item cannot be HQ).</summary>
    long? RevenueHq,
    /// <summary>Gil you would actually spend: only the <b>missing</b> materials are priced; on-hand stock is free.</summary>
    long CashCost,
    /// <summary>Opportunity cost: <b>every</b> material priced at market, including what you already hold.</summary>
    long MarketCost,
    /// <summary>Market-board tax on the revenue (revenue x taxPct / 100), for the quality row chosen by <see cref="Hq"/>.</summary>
    long Tax,
    /// <summary>Revenue - tax - <see cref="CashCost"/>. <c>null</c> when revenue is unknown.</summary>
    long? MarginCash,
    /// <summary>Revenue - tax - <see cref="MarketCost"/>. <c>null</c> when revenue is unknown.</summary>
    long? MarginMarket,
    /// <summary>Units of the result that can be crafted right now from stock (<see cref="RecipeGraph.HowMany(uint, IInventory)"/>).</summary>
    int HowMany,
    /// <summary>Daily sale velocity of the result at DC scope for the chosen quality.</summary>
    double Velocity,
    /// <summary>Current listings of the result on the board.</summary>
    int Listings,
    /// <summary>Velocity-capped daily profit: cash margin per unit x min(units we could make, velocity). The default sort key.</summary>
    double PerDay,
    /// <summary>Days of stock on the board: listings / velocity. <c>+Inf</c> when nothing sells.</summary>
    double SaturationDays,
    /// <summary>Materials that had no price and no on-hand cover, so the cost columns are lower bounds.</summary>
    IReadOnlyList<uint> UnpricedItems)
{
    public bool RevenueKnown => (Hq ? RevenueHq : RevenueNq) is not null;
    public bool CostComplete => UnpricedItems.Count == 0;
    /// <summary>Cash margin per single unit of the result (the number the velocity cap multiplies).</summary>
    public double? MarginCashPerUnit => MarginCash is { } m && Units > 0 ? (double)m / Units : null;
}
