using System;
using ECommons.Configuration;
using Lalalazy.Hub;
using Cfg = CurrencySpender.Configuration.Config;

namespace CurrencySpender;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub).
///
/// This plugin never saves its config on its own (ECommons writes it only when the plugin is disposed), so every
/// setter calls EzConfig.Save() itself. P.config is created on the first framework tick, so every control reports
/// "starting up" until then. Left out on purpose: the selected currency and collectable sets (changing them
/// triggers a heavy recompute), the Debug switch, and the teleport button. Nothing here spends or sells anything.
/// </summary>
internal static class HubAdapter
{
    private static readonly string[] Separators = { "None", "Dot (.)", "Comma (,)" };

    private static ControlState Ready() => P.config == null ? new ControlState(Enabled: false, Why: "starting up") : new ControlState();

    public static void Declare(HubEndpoint ep)
    {
        void T(string id, string label, string group, Func<Cfg, bool> get, Action<Cfg, bool> set, string tip = "")
            => ep.Toggle(id, label, () => P.config != null && get(P.config),
                v =>
                {
                    var c = P.config;
                    if (c == null) return SetOutcome.Refuse("starting up");
                    set(c, v);
                    EzConfig.Save();
                    return SetOutcome.Success;
                }, group: group, tip: tip, state: Ready);

        T("show_ventures", "Show ventures", "Tables", c => c.ShowVentures, (c, v) => c.ShowVentures = v);
        T("show_collectables", "Show collectables", "Tables", c => c.ShowCollectables, (c, v) => c.ShowCollectables = v);
        T("show_missing_collectables", "Show missing collectables", "Tables", c => c.ShowMissingCollectables, (c, v) => c.ShowMissingCollectables = v);
        T("show_items_of_interest", "Show items of interest", "Tables", c => c.ShowItemsOfInterest, (c, v) => c.ShowItemsOfInterest = v);
        T("show_sellables", "Show items eligible for sale", "Tables", c => c.ShowSellables, (c, v) => c.ShowSellables = v,
            tip: "Looks up market prices when the spending window opens. Read-only.");
        T("hide_empty_currencies", "Hide empty currencies", "Tables", c => c.HideEmptyCurrencies, (c, v) => c.HideEmptyCurrencies = v);
        T("open_automatically", "Open automatically", "General", c => c.OpenAutomatically, (c, v) => c.OpenAutomatically = v);

        ep.Stepper("min_sales", "Minimum sales for the sellable table", min: 0, max: 1000, step: 10,
            get: () => P.config?.MinSales ?? 0,
            set: v =>
            {
                var c = P.config;
                if (c == null) return SetOutcome.Refuse("starting up");
                c.MinSales = (int)v;
                EzConfig.Save();
                return SetOutcome.Success;
            }, group: "Tables", tip: "0 turns the filter off.", state: Ready);

        ep.Choice("thousands_separator", "Thousands separator", Separators,
            get: () => Math.Max(0, Math.Min(Separators.Length - 1, P.config?.Seperator ?? 0)),
            set: i =>
            {
                var c = P.config;
                if (c == null) return SetOutcome.Refuse("starting up");
                c.Seperator = Math.Max(0, Math.Min(Separators.Length - 1, i));
                EzConfig.Save();
                return SetOutcome.Success;
            }, group: "General", state: Ready);
    }
}
