using Dalamud.Configuration;
using LazyCrafter.Adapters;

namespace LazyCrafter;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Inventory sources AllaganTools may be asked about (Scope §0 "Inventory scope": everything it can
    /// see, each individually toggleable, FC chest off by default). Keyed by <see cref="InventorySource"/>
    /// name so the JSON stays readable and survives enum reordering.
    /// </summary>
    public Dictionary<string, bool> EnabledSources { get; set; } = InventorySources.Defaults();

    /// <summary>Universalis price cache TTL in minutes (Plan §Phase 3 task 3: 10).</summary>
    public int PriceCacheMinutes { get; set; } = 10;

    /// <summary>Scope the price quotes are taken at: the home data centre (default) or the home world only.</summary>
    public bool PriceByWorld { get; set; } = false;

    public bool IsSourceEnabled(InventorySource source) =>
        EnabledSources.TryGetValue(source.ToString(), out var on) ? on : InventorySources.DefaultFor(source);

    public void SetSourceEnabled(InventorySource source, bool on) => EnabledSources[source.ToString()] = on;

    /// <summary>Idempotent; called once from the Plugin constructor.</summary>
    public void MigrateIfNeeded()
    {
        if (Version >= CurrentVersion) return;
        // v1 -> v2: same dictionary shape; just make sure every source has a key so the settings tab shows them all.
        foreach (var s in Enum.GetValues<InventorySource>())
            EnabledSources.TryAdd(s.ToString(), InventorySources.DefaultFor(s));
        Version = CurrentVersion;
    }
}
