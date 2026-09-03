using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>A marketable item that sells faster than the board restocks it (Plan §Phase 2 task 5).</summary>
public sealed record UndersuppliedItem(
    uint ItemId,
    double Velocity,
    int Listings,
    /// <summary>Recipe that makes it (the caller's preferred job wins ties); <c>null</c> when it is not craftable.</summary>
    uint? RecipeId,
    /// <summary>Days of stock: listings / velocity.</summary>
    double SaturationDays);

/// <summary>
/// Items with real demand and (almost) no supply on the DC right now (Plan §Phase 2 task 5, Scope §5.5):
/// <c>velocity &gt;= MinVelocity</c> and <c>listings &lt;= MaxListings</c>, intersected with the craftable set.
/// Velocity is the NQ+HQ daily sale velocity. Ordered by velocity desc, then fewest listings.
/// </summary>
public sealed class UndersuppliedFinder
{
    private readonly IGameData _data;
    private readonly RecipeGraph _graph;

    public UndersuppliedFinder(IGameData data, RecipeGraph graph)
    {
        _data = data;
        _graph = graph;
    }

    public double MinVelocity { get; init; } = 3;
    public int MaxListings { get; init; } = 2;

    /// <summary>
    /// Scan <paramref name="candidates"/> (normally the craftable result items, already priced).
    /// <paramref name="craftableOnly"/> drops items with no recipe. Items with no quote are skipped.
    /// </summary>
    public IEnumerable<UndersuppliedItem> Find(IEnumerable<uint> candidates, IPriceSource prices, bool craftableOnly = true, uint? preferJob = null)
    {
        var hits = new List<UndersuppliedItem>();
        foreach (var itemId in candidates.Distinct())
        {
            if (!_data.IsMarketable(itemId)) continue;
            var q = prices.Get(itemId);
            if (q is null) continue;
            var velocity = Math.Max(0, q.VelocityNq) + Math.Max(0, q.VelocityHq);
            if (velocity < MinVelocity || q.ListingsCount > MaxListings) continue;
            var recipe = _graph.RecipeFor(itemId, preferJob)?.RecipeId;
            if (craftableOnly && recipe is null) continue;
            hits.Add(new UndersuppliedItem(itemId, velocity, q.ListingsCount, recipe,
                velocity > 0 ? q.ListingsCount / velocity : double.PositiveInfinity));
        }
        return hits.OrderByDescending(h => h.Velocity).ThenBy(h => h.Listings).ThenBy(h => h.ItemId);
    }

    /// <summary>Convenience: scan every craftable result item known to the graph.</summary>
    public IEnumerable<UndersuppliedItem> FindCraftable(IPriceSource prices, uint? preferJob = null) =>
        Find(_graph.RecipeIds.Select(id => _graph.Row(id)!.ResultItemId), prices, craftableOnly: true, preferJob);
}
