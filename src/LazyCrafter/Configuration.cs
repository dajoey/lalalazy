using Dalamud.Configuration;
using LazyCrafter.Adapters;
using LazyCrafter.Core.Model;

namespace LazyCrafter;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Inventory sources AllaganTools may be asked about (Scope §0 "Inventory scope": everything it can
    /// see, each individually toggleable, FC chest off by default). Keyed by <see cref="InventorySource"/>
    /// name so the JSON stays readable and survives enum reordering.
    /// </summary>
    public Dictionary<string, bool> EnabledSources { get; set; } = InventorySources.Defaults();

    /// <summary>Universalis price cache TTL in minutes (Plan §Phase 3 task 3: 10). Also the price refresh interval.</summary>
    public int PriceCacheMinutes { get; set; } = 10;

    /// <summary>Scope the price quotes are taken at: the home data centre (default) or the home world only.</summary>
    public bool PriceByWorld { get; set; } = false;

    // ---- v3 (Phase 4 UI) ----

    /// <summary>Which Universalis number is "what it sells for" (Scope §3.3, selectable).</summary>
    public RevenueBasis RevenueBasis { get; set; } = RevenueBasis.MinListing;

    /// <summary>Show recipes above the character's job level / for jobs not unlocked (Scope §3.1 toggle). Off by default.</summary>
    public bool ShowAboveLevel { get; set; } = false;

    /// <summary>Undersupplied finder thresholds (Plan §Phase 2 task 5: velocity >= X, listings <= Y).</summary>
    public double UndersuppliedMinVelocity { get; set; } = 3;
    public int UndersuppliedMaxListings { get; set; } = 2;

    /// <summary>Dispatch toggles (Plan §Phase 4 task 5). Both OFF by default; Phase 5 / Phase 6 wire the behaviour.</summary>
    public bool DagobertAfterCraft { get; set; } = false;
    public bool VnavWalkToVendor { get; set; } = false;

    /// <summary>The cart, so it survives a plugin reload.</summary>
    public List<CartEntry> Cart { get; set; } = new();

    [Serializable]
    public sealed class CartEntry
    {
        public uint RecipeId { get; set; }
        public int Crafts { get; set; }
    }

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
        // v2 -> v3: new fields all have safe defaults (dispatch toggles OFF); nothing to rewrite.
        Cart ??= new List<CartEntry>();
        Version = CurrentVersion;
    }
}
