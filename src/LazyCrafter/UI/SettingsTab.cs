using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using LazyCrafter.Adapters;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>
/// Settings (Plan §Phase 4 task 5): inventory source toggles, price basis / scope / refresh interval, undersupplied
/// thresholds, and the dispatch toggles - Dagobert list-after-craft and vnavmesh walk-to-vendor - which exist but
/// default OFF (the behaviour lands in Phase 5 / after the Phase 6 spike). Every change saves immediately and
/// invalidates the catalog.
/// </summary>
public sealed class SettingsTab
{
    private readonly Plugin _plugin;

    public SettingsTab(Plugin plugin) => _plugin = plugin;

    public void Draw()
    {
        var cfg = _plugin.Config;
        var changed = false;

        ImGui.TextColored(ImGuiColors.ParsedGold, "Inventory sources");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Where AllaganTools may look when counting what you have. Each source is one toggle; the FC chest is off by default because it is shared property.");
        if (_plugin.Inventory.Degraded)
            ImGui.TextColored(ImGuiColors.DalamudOrange, "AllaganTools is not available - only the current character's bags are counted until it is.");
        foreach (var s in Enum.GetValues<InventorySource>())
        {
            var on = cfg.IsSourceEnabled(s);
            if (ImGui.Checkbox(SourceLabel(s), ref on)) { cfg.SetSourceEnabled(s, on); changed = true; }
        }
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.ParsedGold, "Prices");
        var basis = (int)cfg.RevenueBasis;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo("Revenue basis", ref basis, ["Cheapest listing", "Median listing", "Average sale price"], 3)) { cfg.RevenueBasis = (RevenueBasis)basis; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Which Universalis number stands in for 'what it sells for'. Cheapest listing overstates on thin markets; the /day column's velocity cap keeps the ranking honest.");
        var byWorld = cfg.PriceByWorld;
        if (ImGui.Checkbox("Price at home world only (instead of the whole data centre)", ref byWorld))
        {
            cfg.PriceByWorld = byWorld;
            _plugin.Prices.Scope = byWorld ? _plugin.Player.HomeWorldName : _plugin.Player.DataCenterName;
            _plugin.Prices.ScopeIsWorld = byWorld;
            changed = true;
        }
        var ttl = cfg.PriceCacheMinutes;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Price refresh interval (minutes)", ref ttl)) { cfg.PriceCacheMinutes = Math.Clamp(ttl, 1, 240); changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Quotes older than this are re-fetched for what is on screen. Only the visible rows, the selected recipe and the cart are ever priced.");
        ImGui.TextDisabled($"scope {(_plugin.Prices.Scope.Length == 0 ? "(none - log in)" : _plugin.Prices.Scope)}, {_plugin.Prices.CacheSize} quotes cached, tax {_plugin.Prices.BestTaxPct:0}%");
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.ParsedGold, "Catalog");
        var above = cfg.ShowAboveLevel;
        if (ImGui.Checkbox("Show recipes above my job level / for jobs I have not unlocked", ref above)) { cfg.ShowAboveLevel = above; changed = true; }
        var minVel = cfg.UndersuppliedMinVelocity;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputDouble("Undersupplied: min sales per day", ref minVel, 0, 0, "%.1f")) { cfg.UndersuppliedMinVelocity = Math.Max(0, minVel); changed = true; }
        var maxList = cfg.UndersuppliedMaxListings;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Undersupplied: max listings on the board", ref maxList)) { cfg.UndersuppliedMaxListings = Math.Max(0, maxList); changed = true; }
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.ParsedGold, "Dispatch");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Hand-off behaviour. Both are off by default; the hand-offs themselves arrive with the dispatch phase.");
        var dago = cfg.DagobertAfterCraft;
        if (ImGui.Checkbox("After Artisan finishes a cart, print Dagobert /pricematch instructions for listing the results", ref dago)) { cfg.DagobertAfterCraft = dago; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Optional, never forced (Scope §0 item 6). v1 prints instructions to chat; the sell list itself is not automated.");
        var vnav = cfg.VnavWalkToVendor;
        if (ImGui.Checkbox("Walk to vendors with vnavmesh after a Lifestream teleport (experimental)", ref vnav)) { cfg.VnavWalkToVendor = vnav; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Off until the vnavmesh spike passes 5/5 vendors (Plan §Phase 6). Until then the vendor hand-off is teleport + map flag + shopping list in chat.");
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.ParsedGold, "Retainers");
        if (_plugin.Player.RetainerHint is { } hint) ImGui.TextWrapped(hint);
        else ImGui.TextUnformatted($"{_plugin.Player.Retainers.Count} managed retainer(s) from ARControl.json: " + string.Join(", ", _plugin.Player.Retainers.Select(r => $"{r.Name} L{r.Level}")));

        if (changed)
        {
            _plugin.SaveConfig();
            _plugin.Catalog.Invalidate();
        }
    }

    private static string SourceLabel(InventorySource s) => s switch
    {
        InventorySource.Bags => "Bags and crystals",
        InventorySource.ArmouryChest => "Armoury chest and equipped gear",
        InventorySource.Saddlebag => "Chocobo saddlebag",
        InventorySource.Retainers => "Retainers (bags, crystals, market listings)",
        InventorySource.AltCharacters => "Other characters (pool every character AllaganTools knows)",
        InventorySource.FCChest => "Free company chest (off by default - shared property)",
        InventorySource.GlamourDresser => "Glamour dresser and armoire",
        _ => s.ToString(),
    };
}
