using System;
using Lalalazy.Hub;
using LazyCrafter.Core.Model;

namespace LazyCrafter;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Every setter does what the Settings tab does
/// (set the config field, then Plugin.SaveConfig, which also refreshes the sources, the price TTL and the catalog).
/// "Price at home world only" also moves the price client's scope, exactly as the tab does.
/// The three hand-off toggles and the currency-shop preference take effect live inside a run, so they refuse
/// writes while a cart run is in progress. Left out on purpose: the per-source toggles (each forces a full catalog
/// recompute and the free company chest is shared), and the obsolete and bookkeeping fields. Nothing here starts a run.
/// </summary>
internal static class HubAdapter
{
    private static readonly string[] Bases = { "Cheapest listing", "Median listing", "Average sale price" };

    public static void Declare(HubEndpoint ep, Plugin plugin)
    {
        var c = plugin.Config;
        void Save() => plugin.SaveConfig();
        ControlState NotWhileRunning() => plugin.Dispatch.Running
            ? new ControlState(Enabled: false, Why: "A cart run is in progress.")
            : new ControlState();

        const string catalog = "Catalog";
        const string prices = "Prices";
        const string dispatch = "Cart runs";

        ep.Toggle("show_above_level", "Show recipes above my level", () => c.ShowAboveLevel,
            v => { c.ShowAboveLevel = v; Save(); }, group: catalog,
            tip: "Also lists recipes above the current job level and for jobs not unlocked.");
        ep.Stepper("undersupplied_min_velocity", "Undersupplied: min sales per day", min: 0, max: 1000, step: 0.5,
            get: () => c.UndersuppliedMinVelocity, set: v => { c.UndersuppliedMinVelocity = Math.Max(0, v); Save(); },
            decimals: 1, group: catalog);
        ep.Stepper("undersupplied_max_listings", "Undersupplied: max listings", min: 0, max: 500, step: 1,
            get: () => c.UndersuppliedMaxListings, set: v => { c.UndersuppliedMaxListings = Math.Max(0, (int)v); Save(); },
            group: catalog);

        ep.Choice("revenue_basis", "Revenue basis", Bases, () => (int)c.RevenueBasis,
            i => { c.RevenueBasis = (RevenueBasis)i; Save(); }, group: prices,
            tip: "Which market number stands in for what an item sells for.");
        ep.Toggle("price_by_world", "Price at home world only", () => c.PriceByWorld,
            v =>
            {
                c.PriceByWorld = v;
                // The Settings tab also moves the price client's scope now; at login it is set from the config.
                var dc = plugin.Player.DataCenterName;
                if (!string.IsNullOrEmpty(dc))
                {
                    plugin.Prices.Scope = v ? plugin.Player.HomeWorldName : dc;
                    plugin.Prices.ScopeIsWorld = v;
                }
                Save();
            }, group: prices, tip: "Off prices across the whole data centre.");
        ep.Stepper("price_cache_minutes", "Price refresh interval", min: 1, max: 240, step: 5,
            get: () => c.PriceCacheMinutes, set: v => { c.PriceCacheMinutes = Math.Clamp((int)v, 1, 240); Save(); },
            unit: " min", group: prices);
        ep.Toggle("pricematch_after_craft", "Price-match message after a craft", () => c.PriceMatchAfterCraft,
            v => { c.PriceMatchAfterCraft = v; Save(); }, group: prices,
            tip: "Prints a chat reminder only. Sends and changes nothing.");

        ep.Toggle("sequential_mode", "One intervention at a time", () => c.SequentialInterventionMode,
            v => { c.SequentialInterventionMode = v; Save(); }, group: dispatch,
            tip: "Applies from the next cart run.");
        ep.Toggle("retrieve_from_retainers", "Fetch materials from retainers", () => c.RetrieveFromRetainers,
            v => { c.RetrieveFromRetainers = v; Save(); }, group: dispatch, state: NotWhileRunning);
        ep.Toggle("walk_to_bell", "Walk to a summoning bell when needed", () => c.WalkToBellWhenBlocked,
            v => { c.WalkToBellWhenBlocked = v; Save(); }, group: dispatch, state: NotWhileRunning);
        ep.Toggle("walk_to_vendors", "Walk to vendors on a cart run", () => c.WalkToVendorsOnCart,
            v => { c.WalkToVendorsOnCart = v; Save(); }, group: dispatch, state: NotWhileRunning);
        ep.Toggle("prefer_currency_shops", "Prefer a currency shop", () => c.PreferCurrencyShops,
            v => { c.PreferCurrencyShops = v; Save(); }, group: dispatch, state: NotWhileRunning,
            tip: "Prefers a currency shop over the market board when it is already affordable.");
    }
}
