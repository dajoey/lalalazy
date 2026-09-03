using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Answers "can one of my retainers fetch item X by venture, and how many per run?" (Plan §Phase 1 task 5).
/// <para>
/// Re-derived from the public <c>RetainerTask</c> / <c>RetainerTaskNormal</c> / <c>RetainerTaskParameter</c>
/// sheet semantics (see <see cref="VentureRow"/>), not from any other plugin's code:
/// </para>
/// <list type="number">
/// <item>The retainer's level must be at least <c>RetainerTask.RetainerLevel</c>.</item>
/// <item>The venture's <c>ClassJobCategory</c> must match the retainer's job: 17 = MIN, 18 = BTN, 19 = FSH;
///   any other category is a combat venture and requires a non-gatherer retainer.</item>
/// <item>Gathering ventures (MIN/BTN/FSH) additionally require <c>Gathering >= RequiredGathering</c>, and the
///   reward tier is the number of <c>RetainerTaskParameter</c> perception thresholds the retainer meets
///   (<c>PerceptionDoL</c> for MIN/BTN, <c>PerceptionFSH</c> for FSH).</item>
/// <item>Combat ventures require <c>ItemLevel >= RequiredItemLevel</c>, and the reward tier counts the
///   <c>ItemLevelDoW</c> thresholds met.</item>
/// <item>Quantity = <c>RetainerTaskNormal.Quantity[tier]</c>.</item>
/// <item>Gathering ventures are only available once the character has gathered the item at least once
///   (the game's "gathering log" gate). When the caller supplies that set it is enforced; when it is
///   <c>null</c> the gate is skipped (the harness, or a character whose log is unknown).</item>
/// </list>
/// </summary>
public sealed class VentureResolver
{
    public const uint CategoryMiner = 17;
    public const uint CategoryBotanist = 18;
    public const uint CategoryFisher = 19;

    // ClassJob row ids of the three gatherers.
    public const uint JobMiner = 16;
    public const uint JobBotanist = 17;
    public const uint JobFisher = 18;

    private readonly Dictionary<uint, List<VentureRow>> _byItem = new();

    public VentureResolver(IGameData data)
    {
        foreach (var v in data.Ventures())
        {
            if (!_byItem.TryGetValue(v.ItemId, out var list))
                _byItem[v.ItemId] = list = new List<VentureRow>();
            list.Add(v);
        }
    }

    /// <summary>True when any venture at all yields the item, regardless of retainers.</summary>
    public bool HasVenture(uint itemId) => _byItem.ContainsKey(itemId);

    public IReadOnlyList<VentureRow> VenturesFor(uint itemId) =>
        _byItem.TryGetValue(itemId, out var list) ? list : Array.Empty<VentureRow>();

    /// <summary>The best venture this one retainer can run for the item, or <c>null</c> if none qualifies.</summary>
    public VentureMatch? Resolve(uint itemId, RetainerStats retainer, IReadOnlySet<uint>? gatheredItems = null)
    {
        VentureMatch? best = null;
        foreach (var v in VenturesFor(itemId))
        {
            var m = Match(v, retainer, gatheredItems);
            if (m is not null && (best is null || m.Quantity > best.Quantity)) best = m;
        }
        return best;
    }

    /// <summary>Every (retainer, venture) pairing that qualifies.</summary>
    public IEnumerable<VentureMatch> ResolveAll(uint itemId, IEnumerable<RetainerStats> retainers, IReadOnlySet<uint>? gatheredItems = null)
    {
        foreach (var r in retainers)
        {
            var m = Resolve(itemId, r, gatheredItems);
            if (m is not null) yield return m;
        }
    }

    /// <summary>The single highest-yield match across all retainers, or <c>null</c>.</summary>
    public VentureMatch? ResolveBest(uint itemId, IEnumerable<RetainerStats> retainers, IReadOnlySet<uint>? gatheredItems = null)
    {
        VentureMatch? best = null;
        foreach (var m in ResolveAll(itemId, retainers, gatheredItems))
            if (best is null || m.Quantity > best.Quantity) best = m;
        return best;
    }

    private static VentureMatch? Match(VentureRow v, RetainerStats r, IReadOnlySet<uint>? gatheredItems)
    {
        if (r.Level < v.Level) return null;

        var isGatherVenture = v.JobCategory is CategoryMiner or CategoryBotanist or CategoryFisher;
        var retainerIsGatherer = r.JobId is JobMiner or JobBotanist or JobFisher;

        int stat;
        if (isGatherVenture)
        {
            var wanted = v.JobCategory switch
            {
                CategoryMiner => JobMiner,
                CategoryBotanist => JobBotanist,
                _ => JobFisher,
            };
            if (r.JobId != wanted) return null;
            if (r.Gathering < v.RequiredGathering) return null;
            if (gatheredItems is not null && !gatheredItems.Contains(v.ItemId)) return null;
            stat = r.Perception;
        }
        else
        {
            if (retainerIsGatherer) return null;
            if (r.ItemLevel < v.RequiredItemLevel) return null;
            stat = r.ItemLevel;
        }

        var tier = 0;
        foreach (var threshold in v.RewardThresholds)
        {
            if (stat >= threshold) tier++;
            else break;
        }
        if (tier >= v.QuantityTiers.Count) tier = v.QuantityTiers.Count - 1;
        var qty = tier >= 0 && v.QuantityTiers.Count > 0 ? v.QuantityTiers[tier] : 0;
        return new VentureMatch(v, r, Math.Max(0, tier), qty);
    }
}
