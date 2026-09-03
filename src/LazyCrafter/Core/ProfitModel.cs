using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Prices a recipe against inventory and the market (Plan §Phase 2 task 1, Scope §3.3).
/// <para>
/// Two cost columns are always computed (Scope §0):
/// <b>cash</b> prices only the units you are missing (on-hand stock is free), while
/// <b>market</b> prices every unit at market - the opportunity cost of consuming what you hold.
/// A material's unit price is the cheapest of its market quote and its gil-vendor price. A craftable
/// intermediate costs the cheaper of buying it outright and crafting it from its own materials (recursively);
/// on-hand stock is consumed once as the tree is walked, exactly as <see cref="Tiering"/> does.
/// Materials with no price and no cover are listed in <see cref="ProfitEstimate.UnpricedItems"/> and the
/// cost columns become lower bounds.
/// </para>
/// <para>
/// Revenue uses the selected <see cref="RevenueBasis"/> (min listing by default) for the requested quality,
/// minus market-board tax. <see cref="ProfitEstimate.PerDay"/> is the honest sort key: cash margin per unit x
/// min(units we can produce, daily velocity). "Units we can produce" is unbounded when every missing material
/// is purchasable (the cost already covers buying it) and <see cref="RecipeGraph.HowMany(uint, IInventory)"/>
/// otherwise. Saturation = listings / velocity. <see cref="Rank"/> is the default catalogue order.
/// </para>
/// </summary>
public sealed class ProfitModel
{
    private readonly IGameData _data;
    private readonly RecipeGraph _graph;

    public ProfitModel(IGameData data, RecipeGraph graph)
    {
        _data = data;
        _graph = graph;
    }

    public RevenueBasis Basis { get; init; } = RevenueBasis.MinListing;

    /// <summary>Default sort: velocity-capped daily profit desc, then cash margin desc, then recipe id for stability.</summary>
    public static IEnumerable<ProfitEstimate> Rank(IEnumerable<ProfitEstimate> estimates) =>
        estimates.OrderByDescending(e => e.PerDay)
                 .ThenByDescending(e => e.MarginCash ?? long.MinValue)
                 .ThenBy(e => e.RecipeId);

    /// <summary>Evaluate <paramref name="crafts"/> runs of a recipe; <c>null</c> when the recipe is unknown.</summary>
    public ProfitEstimate? Evaluate(uint recipeId, IInventory inv, IPriceSource prices, double taxPct, bool hq = false, int crafts = 1)
    {
        var node = _graph.Expand(recipeId);
        if (node is null || crafts <= 0) return null;

        var units = checked(Math.Max(1, node.ResultAmount) * crafts);
        var unpriced = new SortedSet<uint>();

        var cash = CostOf(node, crafts, inv, prices, new Dictionary<uint, int>(), cashBasis: true, unpriced);
        var marketUnpriced = new SortedSet<uint>();
        var market = CostOf(node, crafts, inv, prices, new Dictionary<uint, int>(), cashBasis: false, marketUnpriced);

        var quote = prices.Get(node.ResultItemId);
        var unitNq = quote is null ? null : Pick(quote, false);
        var unitHq = quote is null ? null : Pick(quote, true);
        long? revenueNq = unitNq is { } n ? checked(n * units) : null;
        long? revenueHq = unitHq is { } h ? checked(h * units) : null;

        var revenue = hq ? revenueHq : revenueNq;
        var tax = revenue is { } r ? (long)Math.Round(r * taxPct / 100.0, MidpointRounding.AwayFromZero) : 0;
        long? marginCash = revenue is { } rc ? rc - tax - cash : null;
        long? marginMarket = revenue is { } rm ? rm - tax - market : null;

        var howMany = _graph.HowMany(recipeId, inv);
        var velocity = quote is null ? 0 : (hq ? quote.VelocityHq : quote.VelocityNq);
        var listings = quote?.ListingsCount ?? 0;

        var perUnit = marginCash is { } mc ? (double)mc / units : 0;
        // If every material can be bought (market basis priced everything) supply is unbounded; otherwise stock caps it.
        var capacity = marketUnpriced.Count == 0 ? double.PositiveInfinity : howMany;
        var perDay = marginCash is null ? 0 : perUnit * Math.Min(capacity, Math.Max(0, velocity));
        var saturation = velocity > 0 ? listings / velocity : double.PositiveInfinity;

        return new ProfitEstimate(
            recipeId, node.ResultItemId, units, hq,
            revenueNq, revenueHq, cash, market, tax, marginCash, marginMarket,
            howMany, velocity, listings, perDay, saturation, unpriced.ToArray());
    }

    /// <summary>Revenue-side unit price for the result at the configured basis.</summary>
    private long? Pick(PriceQuote q, bool hq) => Basis switch
    {
        RevenueBasis.MedianListing => hq ? q.MedianHq : q.MedianNq,
        RevenueBasis.AvgSale => hq ? q.AvgSaleHq : q.AvgSaleNq,
        _ => hq ? q.MinListingHq : q.MinListingNq,
    };

    /// <summary>Cost-side unit price for a material: cheapest of market (NQ) and gil vendor; <c>null</c> when neither exists.</summary>
    public long? UnitCost(uint itemId, IPriceSource prices)
    {
        long? best = null;
        var q = prices.Get(itemId);
        if (q is not null)
        {
            var p = q.MinListingNq ?? q.AvgSaleNq ?? q.MedianNq;
            if (p is { } mp && mp > 0) best = mp;
        }
        if (_data.IsGilVendor(itemId, out var gil) && gil > 0 && (best is null || gil < best))
            best = gil;
        return best;
    }

    /// <summary>
    /// Material cost of <paramref name="crafts"/> runs. Cash basis consumes on-hand stock first (tracked in
    /// <paramref name="consumed"/>); market basis prices every unit. Items that could not be priced go to <paramref name="unpriced"/>.
    /// </summary>
    public long CostOf(RecipeNode node, int crafts, IInventory inv, IPriceSource prices, Dictionary<uint, int> consumed, bool cashBasis, ISet<uint> unpriced)
    {
        long total = 0;
        foreach (var ing in node.Ingredients)
        {
            var need = checked(ing.Amount * crafts);
            var have = cashBasis ? Take(ing.ItemId, need, inv, consumed) : 0;
            var toPrice = cashBasis ? need - have : need;
            if (toPrice == 0) continue;

            var unit = UnitCost(ing.ItemId, prices);
            long? buy = unit is { } u ? checked(u * toPrice) : null;

            long? craft = null;
            Dictionary<uint, int>? craftConsumed = null;
            SortedSet<uint>? craftUnpriced = null;
            if (ing.SubRecipe is not null)
            {
                var per = Math.Max(1, ing.SubRecipe.ResultAmount);
                var subCrafts = (toPrice + per - 1) / per;
                craftConsumed = new Dictionary<uint, int>(consumed);
                craftUnpriced = new SortedSet<uint>();
                craft = CostOf(ing.SubRecipe, subCrafts, inv, prices, craftConsumed, cashBasis, craftUnpriced);
            }

            // Choose: a fully priced option beats a partial one; among fully priced, the cheaper.
            var craftComplete = craft is not null && craftUnpriced!.Count == 0;
            bool useCraft;
            if (buy is null) useCraft = craft is not null;
            else if (craft is null) useCraft = false;
            else useCraft = craftComplete && craft.Value < buy.Value;

            if (useCraft)
            {
                total = checked(total + craft!.Value);
                foreach (var kv in craftConsumed!) consumed[kv.Key] = kv.Value;
                foreach (var id in craftUnpriced!) unpriced.Add(id);
            }
            else if (buy is not null)
            {
                total = checked(total + buy.Value);
            }
            else
            {
                unpriced.Add(ing.ItemId);
            }
        }
        return total;
    }

    private static int Take(uint itemId, int need, IInventory inv, Dictionary<uint, int> consumed)
    {
        consumed.TryGetValue(itemId, out var used);
        var available = Math.Max(0, inv.Count(itemId) - used);
        var take = Math.Min(available, need);
        consumed[itemId] = used + take;
        return take;
    }
}
