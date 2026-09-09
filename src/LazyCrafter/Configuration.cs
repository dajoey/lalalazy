using Dalamud.Configuration;
using LazyCrafter.Adapters;
using LazyCrafter.Core.Model;
using Newtonsoft.Json;

namespace LazyCrafter;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 9;

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

    /// <summary>
    /// DEAD SETTING, kept only so configs written before 0.1.6.2 still deserialize (card t_731ea0e7).
    /// No consumer has ever read it: the Phase 6 vnavmesh walk-to-vendor spike was closed SKIPPED (t_977b94b4),
    /// so there is no walk implementation to gate. Its Settings checkbox was removed in 0.1.6.2 rather than left
    /// visible and wired to nothing. If Phase 6 is revived, re-introduce the toggle together with the code that
    /// reads it - do not un-obsolete this in isolation.
    /// </summary>
    [Obsolete("Never read; the Phase 6 vnavmesh walk was skipped. Kept for config compatibility only (t_731ea0e7).")]
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

    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }

    // ---- v6 (Tier 1 blocked-listing summary + bell walk, card t_35be7be5) ----

    /// <summary>
    /// After a run ends with materials that are blocked ONLY because they are listed for sale on the market board,
    /// send the character to the nearest market board via Lifestream (<c>/li mb</c>) - the summoning bells stand
    /// with it - so the listings can be pulled and the cart resumed. Since 0.1.6.12 (card t_034884f4) it also covers
    /// the START of a fetch: when a cart's materials sit on the retainers and no summoning bell is reachable,
    /// Dispatch queues the work and walks the character to the bell instead of refusing at press time.
    /// <para>
    /// ON by default (card t_35be7be5): being told which retainer to visit and then having to walk there yourself
    /// is the manual step this removes. It fires when a run has ENDED (finished or stopped) with a non-empty
    /// listing-blocked summary, and at fetch-queue time while the walk is what a fetch is waiting for: never
    /// mid-craft, never on a clean run, and never for materials that were merely slow. Turn it off to get the
    /// summary without the trip - and the press-time fetch refusal back.
    /// </para>
    /// <para>
    /// Existing configs get it ON: it is a new field, so it takes the default, and v5 -> v6 deliberately does not
    /// rewrite it. There is no "off" that predates this setting to preserve.
    /// </para>
    /// </summary>
    public bool WalkToBellWhenBlocked { get; set; } = true;

    // ---- v7 (currency-shop naming + routing, card t_b431de3a) ----

    /// <summary>
    /// When a missing material is sold at a currency (special) shop by a named, placed NPC, and the player can
    /// ALREADY pay the price, send them to that vendor instead of the market board.
    /// <para>
    /// ON by default. The safety is in the routing, not in this toggle: the vendor is only preferred when the item
    /// resolves to a real placed NPC in a teleportable zone AND the currency balance is readable AND it already
    /// covers the cost; on any miss the item falls through to the market board exactly as it did before 0.1.6.7.
    /// So "on" cannot strand a cart, and cannot spend a currency the player does not have. The cheapest affordable
    /// offer wins, which is how 7 Ixali Oaknot beats a Grand Company seal price.
    /// </para>
    /// <para>
    /// <b>Turning it off does NOT restore the old silence.</b> Currency vendors are still NAMED on the market and
    /// manual lines either way - that half was the actual complaint (the plugin sent Joey to the market board for
    /// Emery without ever mentioning the Ixali vendor). This setting governs only whether the routing PREFERS
    /// them; off means "tell me, but keep buying on the board".
    /// </para>
    /// </summary>
    public bool PreferCurrencyShops { get; set; } = true;

    // ---- v8 (cart-run vendor walk, Helm t-joey-1788793199911) ----

    /// <summary>
    /// Walk the character to gil vendors during a cart run, one vendor per stop, instead of only flagging
    /// them on the map (the pre-0.1.6.14 behaviour). Joey's design: "walk to vendor - stop - wait for resume -
    /// walk to next vendor - stop - wait for resume."
    /// <para>
    /// ON by default: the alternative is being told which vendor to visit and then having to walk there
    /// yourself, which is the manual step this removes. The walk reuses the per-item vendor hand-off
    /// (Lifestream teleport to the aetheryte nearest the NPC + map flag + shopping list), fires only at the
    /// start of a wave that steers the character nowhere itself and when a run ends Blocked - never
    /// mid-craft - and never buys anything: the player buys at the NPC and presses Resume, which re-plans
    /// from the bags and walks to the next vendor.
    /// </para>
    /// <para>
    /// When the walk cannot start (Lifestream missing or busy, teleport refused) the stop degrades to exactly
    /// the pre-0.1.6.14 behaviour: the vendor is flagged on the map and named in chat with what to buy.
    /// </para>
    /// </summary>
    public bool WalkToVendorsOnCart { get; set; } = true;

    // ---- v9 (0.1.7.0, card t_5191608a) ----

    /// <summary>
    /// Sequential resume-mode (0.1.7.0, card t_5191608a): run the user-intervention-requiring parts
    /// of a cart run FIRST as explicit stages - shopping stops (one modal per stop: buy, press
    /// Resume), then the gather plan, then the craft queue - and kick into unattended mode (the
    /// pre-0.1.7.0 run, no popups) once the stage machine believes the rest needs no human. Each
    /// blocked stage shows ONE modal describing exactly what to do, with a single Resume button;
    /// error spam is suppressed (one consolidated popup line per blocked stage, never repeated
    /// per frame).
    /// <para>
    /// ON by default: this is the feature's first testing build and Joey asked for the cadence
    /// ("do this and hit resume... then it kicks into unattended mode"). Turning it off restores
    /// today's monolithic run exactly - the stage controller is created empty, which reads as
    /// one unattended run from the first tick - so a bad stage machine cannot brick the plugin.
    /// </para>
    /// </summary>
    public bool SequentialInterventionMode { get; set; } = true;

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
        if (DagobertAfterCraftLegacy is { } legacyValue)
            PriceMatchAfterCraft = legacyValue;
        DagobertAfterCraftLegacy = null;
        // v5 -> v6: WalkToBellWhenBlocked is new and defaults ON (card t_35be7be5). A config written before this
        // version simply has no key for it, so Newtonsoft leaves the property initialiser in place and the user
        // gets the new behaviour - which is the point. Nothing to rewrite.
        // v6 -> v7: PreferCurrencyShops is new and defaults ON (card t_b431de3a). Same shape as v6: a config
        // written before this version has no key, so the initialiser stands and existing users get the routing.
        // That is deliberate and safe - the reroute only fires when the item resolves to a placed vendor the
        // player can already afford, and falls back to the market board (the pre-0.1.6.7 behaviour) otherwise.
        // v7 -> v8: WalkToVendorsOnCart is new and defaults ON (Helm t-joey-1788793199911). Same shape again:
        // a config written before this version has no key, so the initialiser stands and existing users get
        // the vendor walk. Off is one checkbox in the settings and degrades to map flags plus chat names.
        // v8 -> v9: SequentialInterventionMode is new and defaults ON (0.1.7.0, card t_5191608a). Same shape
        // as v6/v7/v8: a config written before this version has no key for it, so the initialiser stands and
        // existing users get the staged cadence - which is the point of the release. Off is one checkbox in
        // the settings and restores the monolithic run.
        Cart ??= new List<CartEntry>();
        Version = CurrentVersion;
    }
}
