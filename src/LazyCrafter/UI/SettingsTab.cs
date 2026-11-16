using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyCrafter.Adapters;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>Grouped settings with drawn tracks. Every mutation keeps the existing save and invalidate path.</summary>
public sealed class SettingsTab
{
    private readonly Plugin _plugin;
    private bool _changed;

    public SettingsTab(Plugin plugin) => _plugin = plugin;

    public void Draw()
    {
        _changed = false;
        DrawDataHealth();
        CrafterChrome.TwoColumns("settings", 0.52f,
            () =>
            {
                CrafterChrome.Card("inventory_sources", DrawInventorySources);
                CrafterChrome.Card("price_settings", DrawPrices);
                CrafterChrome.Card("catalog_settings", DrawCatalogSettings);
            },
            () =>
            {
                CrafterChrome.Card("integration_status", DrawIntegrations);
                CrafterChrome.Card("dispatch_settings", DrawDispatchSettings);
                CrafterChrome.Card("retainer_settings", DrawRetainers);
            });

        if (!_changed)
            return;
        _plugin.SaveConfig();
        _plugin.Catalog.Invalidate();
    }

    private void Setting(string label, Func<bool> get, Action<bool> set, string? help = null)
    {
        var value = get();
        if (!CrafterChrome.Toggle(label, ref value, help))
            return;
        set(value);
        _changed = true;
    }

    private void DrawDataHealth()
    {
        var failures = (_plugin.GameData?.SupplementalFailures ?? []).ToList();
        if (_plugin.Dispatch.VendorsIfBuilt is { } vendors)
            failures.AddRange(vendors.SupplementalFailures);
        CrafterChrome.Card("supplemental_data", () =>
        {
            CrafterChrome.Section("Data health", "Source classifications, desynth values and placed vendors depend on these resources.");
            if (failures.Count > 0)
            {
                Widgets.InfoBox($"Failed — {failures.Count} supplemental resource(s) did not load. Drop classification, desynth values and vendor locations are incomplete.", UiState.Failed);
                foreach (var failure in failures.Take(8))
                    Widgets.SecondaryWrapped(failure);
                if (failures.Count > 8)
                    Widgets.Caption($"... and {failures.Count - 8} more");
                Widgets.SecondaryWrapped("Reinstalling will not repair a package that is missing a dependency; report the listed resource failures.");
            }
            else if (_plugin.GameData is { } data)
            {
                Widgets.InfoBox($"Ready — {data.DropCount:N0} drop items and {data.DesynthSourceCount:N0} desynth sources loaded.", UiState.Ready);
            }
            else if (_plugin.GameDataLoad.IsCompleted)
            {
                Widgets.InfoBox("Failed — game data did not load, so supplemental data health cannot be confirmed.", UiState.Failed);
            }
            else
            {
                Widgets.InfoBox("Running — supplemental data health appears when game data finishes loading.", UiState.Running);
            }
        }, failures.Count == 0 && _plugin.GameData is not null ? CrafterChrome.CardTone.Ready : CrafterChrome.CardTone.Surface);
    }

    private void DrawInventorySources()
    {
        CrafterChrome.Section("Inventory sources", "Choose where AllaganTools may count owned materials. The free company chest remains opt-in because it is shared property.");
        if (_plugin.Inventory.Degraded)
            Widgets.InfoBox(Copy.Missing("AllaganTools") + " — only the current character's bags are counted.", UiState.Missing);
        foreach (var source in Enum.GetValues<InventorySource>())
        {
            var captured = source;
            Setting(SourceLabel(source), () => _plugin.Config.IsSourceEnabled(captured), value => _plugin.Config.SetSourceEnabled(captured, value));
        }
    }

    private void DrawPrices()
    {
        var cfg = _plugin.Config;
        CrafterChrome.Section("Prices", "Only visible rows, the selected recipe and the cart are priced.");
        var basis = (int)cfg.RevenueBasis;
        ImGui.SetNextItemWidth(MathF.Min(260, ImGui.GetContentRegionAvail().X));
        if (ImGui.Combo("Revenue basis", ref basis, ["Cheapest listing", "Median listing", "Average sale price"], 3))
        {
            cfg.RevenueBasis = (RevenueBasis)basis;
            _changed = true;
        }
        Widgets.SecondaryWrapped("Cheapest listing can overstate thin markets; the /day velocity cap still limits the ranking.");
        Setting("Price at the home world only", () => cfg.PriceByWorld, value =>
        {
            cfg.PriceByWorld = value;
            _plugin.Prices.Scope = value ? _plugin.Player.HomeWorldName : _plugin.Player.DataCenterName;
            _plugin.Prices.ScopeIsWorld = value;
        }, "Off uses the whole home data centre. Changing scope keeps the existing Universalis client and cache behavior.");
        var ttl = cfg.PriceCacheMinutes;
        ImGui.SetNextItemWidth(MathF.Min(180, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputInt("Refresh interval (minutes)", ref ttl))
        {
            cfg.PriceCacheMinutes = Math.Clamp(ttl, 1, 240);
            _changed = true;
        }
        Widgets.Caption($"Scope {(_plugin.Prices.Scope.Length == 0 ? "(none — log in)" : _plugin.Prices.Scope)} · {_plugin.Prices.CacheSize} quotes cached · tax {_plugin.Prices.BestTaxPct:0}%");
    }

    private void DrawCatalogSettings()
    {
        var cfg = _plugin.Config;
        CrafterChrome.Section("Catalog thresholds", "Control recipe visibility and the Undersupplied bucket without changing the saved request contract.");
        Setting("Show recipes above the current job level or for locked jobs", () => cfg.ShowAboveLevel, value => cfg.ShowAboveLevel = value);
        var minimumVelocity = cfg.UndersuppliedMinVelocity;
        ImGui.SetNextItemWidth(MathF.Min(180, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputDouble("Undersupplied minimum sales / day", ref minimumVelocity, 0, 0, "%.1f"))
        {
            cfg.UndersuppliedMinVelocity = Math.Max(0, minimumVelocity);
            _changed = true;
        }
        var maximumListings = cfg.UndersuppliedMaxListings;
        ImGui.SetNextItemWidth(MathF.Min(180, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputInt("Undersupplied maximum listings", ref maximumListings))
        {
            cfg.UndersuppliedMaxListings = Math.Max(0, maximumListings);
            _changed = true;
        }
    }

    private void DrawDispatchSettings()
    {
        var cfg = _plugin.Config;
        CrafterChrome.Section("Dispatch behavior", "These tracks keep the existing run, fallback and purchase semantics.");
        Setting("Fetch owned materials from retainers before crafting", () => cfg.RetrieveFromRetainers, value => cfg.RetrieveFromRetainers = value,
            "Uses Artisan's retainer withdrawal at a summoning bell. Off leaves retrieval as a manual shopping step.");
        Setting("Walk to a summoning bell when retrieval or listed materials need one", () => cfg.WalkToBellWhenBlocked, value => cfg.WalkToBellWhenBlocked = value,
            "Uses the existing Lifestream route, never mid-craft. Off refuses an unreachable fetch and leaves listing-blocked runs as a summary.");
        Setting("Walk to gil vendors during a cart run", () => cfg.WalkToVendorsOnCart, value => cfg.WalkToVendorsOnCart = value,
            "Stops at one vendor at a time so the player can buy and press Resume. It never buys directly and falls back to a map flag when travel is unavailable.");
        Setting("Prefer an affordable currency shop over the market board", () => cfg.PreferCurrencyShops, value => cfg.PreferCurrencyShops = value,
            "Only a named, placed vendor with a readable and sufficient currency balance is preferred. Otherwise the item remains on the market-board list. No trade is made automatically.");
        Setting("Run shopping stops before unattended gathering and crafting", () => cfg.SequentialInterventionMode, value => cfg.SequentialInterventionMode = value,
            "Names each manual stop once and waits for Resume. Off keeps the monolithic wave loop.");
        Setting("Buy vendor items through Knightshopper during dispatch", () => cfg.BuyVendorItemsWithKnightshopper, value => cfg.BuyVendorItemsWithKnightshopper = value,
            "Uses the existing purchase IPC once per currency group, to target inventory totals, after printing the list and estimated cost. Market-board items never route. Knightshopper must also allow LazyCrafter; missing, refused or unavailable items fall back to the usual shopping stops. Off disables automatic vendor purchases.");
        Setting("Print price-match instructions after Artisan finishes a cart", () => cfg.PriceMatchAfterCraft, value => cfg.PriceMatchAfterCraft = value,
            "Prints the crafted results and /pricematch instructions. The sell list itself is not automated.");
    }

    private void DrawIntegrations()
    {
        CrafterChrome.Section("Integrations", "Live load state and reflection pins; no hand-off is changed by this page.");
        var guard = _plugin.Dispatch.Guard;
        foreach (var pin in new[] { Adapters.Dispatch.GbrDispatch.Pin, Adapters.Dispatch.ArcDispatch.Pin, Adapters.Dispatch.RetainerFetch.Pin })
        {
            var installed = guard.InstalledVersion(pin.InternalName, out var loaded);
            var minimum = guard.Overrides.TryGetValue(pin.InternalName, out var overridden) ? overridden : pin.MinVersion;
            var ok = installed is not null && loaded && installed >= minimum;
            var state = installed is null ? UiState.Missing : ok ? UiState.Ready : UiState.Blocked;
            var value = installed is null ? "Missing" : ok ? "Ready" : "Blocked";
            CrafterChrome.Requirement(pin.InternalName, value, state);
            Widgets.Caption($"{(installed is null ? "not installed" : loaded ? installed.ToString() : installed + " (not loaded)")} · pinned [{minimum}, {pin.MaxVerified}){(overridden is not null ? " · session override" : "")}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{pin.Members.Count} member names verified against {pin.VerifiedAgainst}. Test a refusal with /lcraft guard {pin.InternalName} 99.0");
        }
        CrafterChrome.Requirement("Artisan", _plugin.Dispatch.Artisan.Installed ? "Ready" : "Missing Artisan",
            _plugin.Dispatch.Artisan.Installed ? UiState.Ready : UiState.Missing);
        CrafterChrome.Requirement("Lifestream", _plugin.Dispatch.Lifestream.Installed ? "Ready" : "Missing Lifestream",
            _plugin.Dispatch.Lifestream.Installed ? UiState.Ready : UiState.Missing);
        CrafterChrome.Requirement("Price match IPC", _plugin.Dispatch.PriceMatch.Installed ? "Ready" : "Missing Lazy Market Companion",
            _plugin.Dispatch.PriceMatch.Installed ? UiState.Ready : UiState.Missing);
    }

    private void DrawRetainers()
    {
        CrafterChrome.Section("Retainers", "Live retainer information used by venture and retrieval planning.");
        if (_plugin.Player.RetainerHint is { } hint)
            Widgets.SecondaryWrapped(hint);
        else
            Widgets.SecondaryWrapped($"{_plugin.Player.Retainers.Count} managed retainer(s): " +
                string.Join(", ", _plugin.Player.Retainers.Select(retainer => $"{retainer.Name} L{retainer.Level}")));
    }

    private static string SourceLabel(InventorySource source) => source switch
    {
        InventorySource.Bags => "Bags and crystals",
        InventorySource.ArmouryChest => "Armoury chest and equipped gear",
        InventorySource.Saddlebag => "Chocobo saddlebag",
        InventorySource.Retainers => "Retainers (bags, crystals, market listings)",
        InventorySource.AltCharacters => "Other characters known to AllaganTools",
        InventorySource.FCChest => "Free company chest (shared property)",
        InventorySource.GlamourDresser => "Glamour dresser and armoire",
        _ => source.ToString(),
    };
}
