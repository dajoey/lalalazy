using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>Expected market value of desynthesizing one unit of an item (Plan §Phase 2 task 3). Always an estimate.</summary>
public sealed record DesynthEstimate(
    uint ItemId,
    /// <summary>Σ over outcomes of chance x quantity x market min (NQ). Outcomes with no price contribute 0.</summary>
    double ExpectedValue,
    /// <summary>Outcomes that had a price and fed the total.</summary>
    IReadOnlyList<DesynthResult> Priced,
    /// <summary>Outcomes with no market price (untradeable or no quote) - the total is a lower bound when this is non-empty.</summary>
    IReadOnlyList<DesynthResult> Unpriced)
{
    /// <summary>Marked "estimate" in the UI: drop tables are community data and prices are a snapshot.</summary>
    public bool IsEstimate => true;
    public bool Complete => Unpriced.Count == 0;
}

/// <summary>
/// "What is this worth if I break it instead?" (Plan §Phase 2 task 3, Scope §5.9).
/// Expected value = Σ(drop chance x quantity x market min NQ). Uses the min listing at DC scope because that is
/// what the outputs would compete with; gil-vendor outputs are valued at 0 (they are worthless to sell).
/// </summary>
public sealed class DesynthValue
{
    private readonly IGameData _data;

    public DesynthValue(IGameData data) => _data = data;

    /// <summary>Estimate for one unit; <c>null</c> when the item has no known desynth outcomes.</summary>
    public DesynthEstimate? Evaluate(uint itemId, IPriceSource prices)
    {
        var results = _data.Desynth(itemId);
        if (results.Count == 0) return null;

        double total = 0;
        var priced = new List<DesynthResult>();
        var unpriced = new List<DesynthResult>();
        foreach (var r in results)
        {
            var q = prices.Get(r.ItemId);
            var unit = q?.MinListingNq ?? q?.AvgSaleNq ?? q?.MedianNq;
            if (unit is { } u && u > 0 && _data.IsMarketable(r.ItemId))
            {
                total += Math.Clamp(r.Chance, 0, 1) * Math.Max(0, r.Quantity) * u;
                priced.Add(r);
            }
            else
            {
                unpriced.Add(r);
            }
        }
        return new DesynthEstimate(itemId, total, priced, unpriced);
    }

    /// <summary>
    /// Desynth value of a crafted result minus what it sells for: positive means breaking it beats selling it.
    /// <c>null</c> when either side is unknown.
    /// </summary>
    public double? DesynthPremium(uint itemId, IPriceSource prices)
    {
        var d = Evaluate(itemId, prices);
        var sell = prices.Get(itemId)?.MinListingNq;
        if (d is null || sell is null) return null;
        return d.ExpectedValue - sell.Value;
    }
}
