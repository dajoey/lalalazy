namespace LazyCrafter.Core;

/// <summary>Scrip value of one collectable turn-in (Plan §Phase 2 task 2).</summary>
public sealed record ScripEstimate(
    uint ItemId,
    uint Currency,
    /// <summary>Scrip per turn-in at each collectability tier (low / mid / high), in table order.</summary>
    IReadOnlyList<int> ScripPerTier,
    /// <summary>Collectability required for each tier.</summary>
    IReadOnlyList<int> CollectabilityPerTier,
    /// <summary>Scrip per craft assuming the top tier is reached.</summary>
    int ScripPerCraft,
    /// <summary>Whether the job level is inside the shop's accepted band.</summary>
    bool AcceptedAtLevel);

/// <summary>
/// Turns <see cref="CollectableInfo"/> into "scrip per craft" (Plan §Phase 2 task 2, Scope §5.2).
/// <para>
/// Assumes max tier by default (Artisan-driven crafts reliably hit it); <see cref="ForCollectability"/>
/// answers the honest question for a given collectability value. The level band is reported, not enforced -
/// the UI decides whether to hide out-of-band turn-ins.
/// </para>
/// </summary>
public sealed class ScripValue
{
    private readonly IGameData _data;

    public ScripValue(IGameData data) => _data = data;

    /// <summary>Scrip estimate for an item, or <c>null</c> when it is not a collectable.</summary>
    public ScripEstimate? Evaluate(uint itemId, int jobLevel = int.MaxValue)
    {
        var info = _data.Collectable(itemId);
        if (info is null || info.Reward.Count == 0) return null;

        var tiers = Math.Min(info.Reward.Count, info.Collectability.Count);
        var scrip = info.Reward.Take(tiers).ToArray();
        var coll = info.Collectability.Take(tiers).ToArray();
        var max = scrip.Length == 0 ? 0 : scrip.Max();
        var inBand = jobLevel == int.MaxValue || (jobLevel >= info.LevelMin && (info.LevelMax <= 0 || jobLevel <= info.LevelMax));
        return new ScripEstimate(itemId, info.Currency, scrip, coll, max, inBand);
    }

    /// <summary>Scrip paid for a turn-in at <paramref name="collectability"/>; 0 below the lowest breakpoint.</summary>
    public int ForCollectability(uint itemId, int collectability)
    {
        var e = Evaluate(itemId);
        if (e is null) return 0;
        var best = 0;
        for (var i = 0; i < e.ScripPerTier.Count; i++)
            if (collectability >= e.CollectabilityPerTier[i] && e.ScripPerTier[i] > best)
                best = e.ScripPerTier[i];
        return best;
    }
}
