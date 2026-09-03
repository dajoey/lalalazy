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

    public IEnumerable<RecipeRow> Recipes() => _recipes;
    public bool IsGilVendor(uint itemId, out uint gil) => _gilVendor.TryGetValue(itemId, out gil);
    public bool IsSpecialShop(uint itemId) => _specialShop.Contains(itemId);
    public GatherInfo? Gather(uint itemId) => _gather.TryGetValue(itemId, out var g) ? g : null;
    public bool IsFish(uint itemId) => _fish.Contains(itemId);
    public IEnumerable<VentureRow> Ventures() => _ventures;
    public bool IsMarketable(uint itemId) => _marketable.Contains(itemId);
    public bool IsDrop(uint itemId) => _drops.Contains(itemId);
}

internal sealed class FakeInventory : IInventory
{
    private readonly Dictionary<uint, int> _counts = new();
    public FakeInventory Set(uint itemId, int count) { _counts[itemId] = count; return this; }
    public int Count(uint itemId) => _counts.TryGetValue(itemId, out var c) ? c : 0;
}

internal sealed class FakePrices : IPriceSource
{
    private readonly Dictionary<uint, PriceQuote> _quotes = new();
    public FakePrices Set(PriceQuote q) { _quotes[q.ItemId] = q; return this; }
    public PriceQuote? Get(uint itemId) => _quotes.TryGetValue(itemId, out var q) ? q : null;
}
