using System;
using System.Collections.Generic;

namespace LazyGearCollector;

/// <summary>
/// The planning result for the whole collection, kept for a short time. The window asks for it several
/// times per frame (progress bar, role table, role detail); planning walks the shop graph, so it must not
/// run once per ask. The key is the collection reference, the target tier and the include-cached flag;
/// a changed key or an expired entry plans again at once.
/// </summary>
public sealed class PlanCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(2);

    private GearCollection? _collection;
    private int _targetTier = -1;
    private bool _includeCached;
    private DateTime _expiry = DateTime.MinValue;
    private (List<PiecePlan> Plans, Dictionary<uint, long> Total, int Done) _value;

    public (List<PiecePlan> Plans, Dictionary<uint, long> Total, int Done) Get(
        GearCollection collection, int targetTier, bool includeCached, DateTime now,
        Func<(List<PiecePlan> Plans, Dictionary<uint, long> Total, int Done)> compute)
    {
        if (!ReferenceEquals(_collection, collection)
            || _targetTier != targetTier
            || _includeCached != includeCached
            || now >= _expiry)
        {
            _value = compute();
            _collection = collection;
            _targetTier = targetTier;
            _includeCached = includeCached;
            _expiry = now + Lifetime;
        }

        return _value;
    }
}

/// <summary>Resolves each item id once. A resolver that returns null (for example the item sheet is not loaded yet) is not remembered.</summary>
public sealed class NameMemo
{
    private readonly Func<uint, string?> _resolve;
    private readonly Dictionary<uint, string> _names = new();

    public NameMemo(Func<uint, string?> resolve) => _resolve = resolve;

    public string? Get(uint itemId)
    {
        if (_names.TryGetValue(itemId, out var cached)) return cached;
        var name = _resolve(itemId);
        if (name != null) _names[itemId] = name;
        return name;
    }
}
