using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>In-memory <see cref="IGameData"/> for the harness. Fluent setters; everything defaults to "not sourced".</summary>
internal sealed class FakeGameData : IGameData
{
    private readonly List<RecipeRow> _recipes = new();
    private readonly Dictionary<uint, uint> _gilVendor = new();
    private readonly HashSet<uint> _specialShop = new();
    private readonly Dictionary<uint, GatherInfo> _gather = new();
    private readonly HashSet<uint> _fish = new();
    private readonly List<VentureRow> _ventures = new();
    private readonly HashSet<uint> _marketable = new();
    private readonly HashSet<uint> _drops = new();
    private readonly Dictionary<uint, CollectableInfo> _collectables = new();
    private readonly Dictionary<uint, List<DesynthResult>> _desynth = new();

    public FakeGameData Recipe(uint recipeId, uint resultItem, int resultAmount, uint job, int level, params (uint ItemId, int Amount)[] ingredients)
    {
        _recipes.Add(new RecipeRow(recipeId, resultItem, resultAmount, job, level, ingredients));
        return this;
    }

    public FakeGameData GilVendor(uint itemId, uint gil) { _gilVendor[itemId] = gil; return this; }
    public FakeGameData SpecialShop(uint itemId) { _specialShop.Add(itemId); return this; }
    public FakeGameData Gatherable(uint itemId, GatherInfo info) { _gather[itemId] = info; return this; }
    public FakeGameData Fish(uint itemId) { _fish.Add(itemId); return this; }
    public FakeGameData Venture(VentureRow row) { _ventures.Add(row); return this; }
    public FakeGameData Marketable(uint itemId) { _marketable.Add(itemId); return this; }
    public FakeGameData Drop(uint itemId) { _drops.Add(itemId); return this; }
    public FakeGameData Collectable(CollectableInfo info) { _collectables[info.ItemId] = info; return this; }
    public FakeGameData Desynth(uint itemId, params DesynthResult[] results)
    {
        if (!_desynth.TryGetValue(itemId, out var list)) _desynth[itemId] = list = new List<DesynthResult>();
        list.AddRange(results);
        return this;
    }

    public IEnumerable<RecipeRow> Recipes() => _recipes;
    public bool IsGilVendor(uint itemId, out uint gil) => _gilVendor.TryGetValue(itemId, out gil);
    public bool IsSpecialShop(uint itemId) => _specialShop.Contains(itemId);
    public GatherInfo? Gather(uint itemId) => _gather.TryGetValue(itemId, out var g) ? g : null;
    public bool IsFish(uint itemId) => _fish.Contains(itemId);
    public IEnumerable<VentureRow> Ventures() => _ventures;
    public bool IsMarketable(uint itemId) => _marketable.Contains(itemId);
    public bool IsDrop(uint itemId) => _drops.Contains(itemId);
    public CollectableInfo? Collectable(uint itemId) => _collectables.TryGetValue(itemId, out var c) ? c : null;
    public IReadOnlyList<DesynthResult> Desynth(uint itemId) => _desynth.TryGetValue(itemId, out var l) ? l : Array.Empty<DesynthResult>();
}

/// <summary>
/// In-memory <see cref="IInventory"/>. <see cref="Set"/> puts stock in the bags (the common case, so every existing
/// test keeps its meaning); <see cref="SetElsewhere"/> adds stock that is owned but NOT in the bags, which is what
/// the dispatcher has to fetch before a craft can run.
/// </summary>
internal sealed class FakeInventory : IInventory
{
    private readonly Dictionary<uint, int> _bags = new();
    private readonly Dictionary<uint, List<StoredElsewhere>> _elsewhere = new();
    private readonly Dictionary<uint, List<StoredElsewhere>> _listed = new();

    /// <summary>Stock in the bags: owned AND reachable by a synthesis.</summary>
    public FakeInventory Set(uint itemId, int count) { _bags[itemId] = count; return this; }

    /// <summary>Stock owned but sitting somewhere a craft cannot reach (a retainer, the saddlebag, ...).</summary>
    public FakeInventory SetElsewhere(uint itemId, int count, string where)
    {
        if (!_elsewhere.TryGetValue(itemId, out var list)) _elsewhere[itemId] = list = new List<StoredElsewhere>();
        list.Add(new StoredElsewhere(where, count));
        return this;
    }

    /// <summary>
    /// Units you have LISTED FOR SALE on the market board. Mirrors the adapter after the 2026-09-05 fix: named by
    /// <see cref="StoredWhere"/> so the UI can say where they are, but never part of <see cref="Count"/>, because a
    /// summoning bell cannot hand a listing over. Use <see cref="SetElsewhere"/> to reproduce the OLD behaviour
    /// (listing counted as owned) as a negative control.
    /// <para>
    /// Built with <c>Fetchable: false</c>, exactly as <c>AllaganInventory.SplitListings</c> does, so the rig can
    /// prove the place a retrieval NAMES as well as its quantity (card t_05e6722b).
    /// </para>
    /// </summary>
    public FakeInventory SetListed(uint itemId, int count, string retainer = "Hussypants")
    {
        if (!_listed.TryGetValue(itemId, out var list)) _listed[itemId] = list = new List<StoredElsewhere>();
        list.Add(new StoredElsewhere($"the market board (listed by retainer {retainer})", count, Fetchable: false, Retainer: retainer));
        return this;
    }

    public int Count(uint itemId) => CountInBags(itemId) + (_elsewhere.TryGetValue(itemId, out var l) ? l.Sum(e => e.Quantity) : 0);
    public int CountInBags(uint itemId) => _bags.TryGetValue(itemId, out var c) ? c : 0;

    /// <summary>
    /// Fetchable places first, then the listings - the same ordering <c>AllaganInventory.StoredWhere</c> applies
    /// (card t_05e6722b). Within each group, most-stocked first.
    /// </summary>
    public IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId)
    {
        var here = _elsewhere.TryGetValue(itemId, out var l) ? l : Enumerable.Empty<StoredElsewhere>();
        var listed = _listed.TryGetValue(itemId, out var m) ? m : Enumerable.Empty<StoredElsewhere>();
        var all = here.Concat(listed).OrderByDescending(e => e.Fetchable).ThenByDescending(e => e.Quantity).ToArray();
        return all.Length == 0 ? Array.Empty<StoredElsewhere>() : all;
    }
}

internal sealed class FakePrices : IPriceSource
{
    private readonly Dictionary<uint, PriceQuote> _quotes = new();
    public FakePrices Set(PriceQuote q) { _quotes[q.ItemId] = q; return this; }
    public PriceQuote? Get(uint itemId) => _quotes.TryGetValue(itemId, out var q) ? q : null;
}
