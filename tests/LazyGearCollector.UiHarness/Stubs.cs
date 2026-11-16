using Dalamud.Configuration;

namespace LazyGearCollector;

// Stubs for the game/runtime boundary only. The real CollectorWindow, CollectorChrome, Planner, models and
// Configuration compile unchanged; these types stand in for the plugin entry point, the inventory scanner
// (game memory) and the shop graph (game data sheets).

internal sealed class HarnessClientState
{
    public bool IsLoggedIn { get; set; } = true;
}

internal sealed class HarnessPluginInterface
{
    public int Saves;
    public void SavePluginConfig(IPluginConfiguration configuration) => Saves++;
}

public sealed class Plugin
{
    internal static HarnessClientState ClientState { get; } = new();
    internal static HarnessPluginInterface PluginInterface { get; } = new();

    public Configuration Config { get; set; } = new();
    public ShopGraph Shops { get; } = new();
    public OwnershipScanner Ownership { get; }
    public Planner Planner { get; }
    public List<GearCollection> Collections { get; } = new();

    public Plugin()
    {
        Ownership = new OwnershipScanner(this);
        Planner = new Planner(Ownership, Shops);
    }
}

/// <summary>Item names and shop offers that the game's Item and SpecialShop sheets would provide.</summary>
public sealed class ShopGraph
{
    public Dictionary<uint, string> Names { get; } = new();
    public Dictionary<uint, List<ShopOffer>> Offers { get; } = new();

    public IReadOnlyList<ShopOffer> OffersFor(uint itemId) =>
        Offers.TryGetValue(itemId, out var list) ? list : Array.Empty<ShopOffer>();

    public string ItemName(uint itemId) =>
        itemId == 0 ? "" : Names.TryGetValue(itemId, out var n) ? n : $"Item#{itemId}";
}

public sealed record ShopOffer(uint ShopId, string ShopName, uint ReceiveItemId, uint ReceiveCount, IReadOnlyList<CostLine> Costs);

/// <summary>Same query surface as the real scanner, backed by a fixed table instead of game inventory memory.</summary>
public sealed class OwnershipScanner
{
    private readonly Plugin _plugin;
    public Dictionary<uint, int> Live { get; } = new();

    public OwnershipScanner(Plugin plugin) => _plugin = plugin;

    public void Invalidate() { }

    public int LiveCount(uint itemId) => Live.TryGetValue(itemId, out var n) ? n : 0;

    public int TotalCount(uint itemId)
    {
        var total = LiveCount(itemId);
        if (_plugin.Config.IncludeCachedContainers)
            total += CachedCount(itemId);
        return total;
    }

    public int CachedCount(uint itemId) =>
        _plugin.Config.Snapshots.Values.Where(s => s.Counts.ContainsKey(itemId)).Sum(s => s.Counts[itemId]);

    public IEnumerable<(string Label, int Count, DateTime SeenUtc)> CachedSources(uint itemId) =>
        _plugin.Config.Snapshots.Values
            .Where(s => s.Counts.TryGetValue(itemId, out var n) && n > 0)
            .Select(s => (s.Label, s.Counts[itemId], s.SeenUtc));
}
