using Dalamud.Configuration;
using LazyCrafter.Adapters;
using LazyCrafter.Core.Model;
using Newtonsoft.Json;

namespace LazyCrafter;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 5;

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

    /// <summary>
    /// Dispatch toggles (Plan §Phase 4 task 5). Both OFF by default; Phase 5 / Phase 6 wire the behaviour.
    /// Renamed from <c>DagobertAfterCraft</c> when DagobertPriceMatcher was retired (2026-09-05): the
    /// price-match hand-off now targets Lazy Market Companion. Existing configs keep their value via the
    /// legacy shadow property below, copied once in <see cref="MigrateIfNeeded"/>.
    /// </summary>
    public bool PriceMatchAfterCraft { get; set; } = false;
    public bool VnavWalkToVendor { get; set; } = false;

    /// <summary>
    /// Legacy JSON key for <see cref="PriceMatchAfterCraft"/> (it was the property name before the Lazy Market
    /// Companion rename, card t_89a7ebec). Newtonsoft fills it when an old config is loaded; MigrateIfNeeded
    /// copies it across once and nulls it, after which saves stop writing the old key (NullValueHandling.Ignore).
    /// </summary>
    [JsonProperty("DagobertAfterCraft", NullValueHandling = NullValueHandling.Ignore)]
    public bool? DagobertAfterCraftLegacy { get; set; }

    // ---- v4 (retrieve from retainers, card t_63b845ad) ----

    /// <summary>
    /// Fetch materials that are sitting on a retainer into the bags before crafting, by driving Artisan's
    /// <c>RestockFromRetainers</c> at a summoning bell (Joey: "stock the ingredients in my bag first").
    /// ON by default: without it a cart whose materials are on a retainer can only be refused, which is the
    /// nag loop this replaces. Turn it off to go back to being told what to fetch by hand.
    /// </summary>
    public bool RetrieveFromRetainers { get; set; } = true;

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
        // v3 -> v4: RetrieveFromRetainers defaults ON - an existing config that never had the field gets the
        // new behaviour, which is the fix the user asked for; it is opt-OUT, not opt-in.
        // v4 -> v5: DagobertAfterCraft -> PriceMatchAfterCraft (DagobertPriceMatcher retired 2026-09-05,
        // succeeded by Lazy Market Companion). Newtonsoft filled the legacy shadow property above if the old
        // key was present; copy it across once so nobody loses the setting, then stop writing the old key.
        if (DagobertAfterCraftLegacy is { } legacyDagobert)
            PriceMatchAfterCraft = legacyDagobert;
        DagobertAfterCraftLegacy = null;
        Cart ??= new List<CartEntry>();
        Version = CurrentVersion;
    }
}
