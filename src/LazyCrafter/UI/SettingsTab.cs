using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using LazyCrafter.Adapters;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>
/// Settings (Plan §Phase 4 task 5): inventory source toggles, price basis / scope / refresh interval, undersupplied
/// thresholds, the dispatch toggles - price-match list-after-craft via Lazy Market Companion (Phase 5, read by DispatchService) and vnavmesh
/// walk-to-vendor (still hidden behind the Phase 6 spike) - and the reflection-guard status of the GBR / ARC
/// hand-offs. Every change saves immediately and invalidates the catalog.
/// </summary>
public sealed class SettingsTab
{
    private readonly Plugin _plugin;

    public SettingsTab(Plugin plugin) => _plugin = plugin;

    public void Draw()
    {
        var cfg = _plugin.Config;
        var changed = false;

        // Data health first: a build whose LuminaSupplemental resources failed to load still works for
        // everything else, so a silent failure passed an in-game verify once (0.1.0.0 shipped with
        // Sylvan.Data.Csv pruned out of the zip -> 0 desynth sources, 168 drops, no placed vendors).
        // Anything wrong here is red, at the top, before the toggles.
        var supFailures = (_plugin.GameData?.SupplementalFailures ?? []).ToList();
        if (_plugin.Dispatch.VendorsIfBuilt is { } vl) supFailures.AddRange(vl.SupplementalFailures);
        if (supFailures.Count > 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, $"Supplemental data failed to load ({supFailures.Count} resource(s)) - this build is broken.");
            ImGui.TextColored(ImGuiColors.DalamudRed, "Drop classification, desynth values and gil-vendor locations are missing or incomplete.");
            foreach (var f in supFailures.Take(8)) ImGui.TextColored(ImGuiColors.DalamudRed, $"    {f}");
            if (supFailures.Count > 8) ImGui.TextColored(ImGuiColors.DalamudRed, $"    ... and {supFailures.Count - 8} more");
            ImGui.TextColored(ImGuiColors.DalamudRed, "Report it: the plugin package is missing a dependency, reinstalling will not help.");
            ImGui.Separator();
        }
        else if (_plugin.GameData is { } okGd)
        {
            ImGui.TextColored(ImGuiColors.HealerGreen, $"Supplemental data OK - {okGd.DropCount:N0} drop items, {okGd.DesynthSourceCount:N0} desynth sources.");
            ImGui.Spacing();
        }

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
        ImGuiComponents.HelpMarker("Hand-off behaviour. Both are off by default.");
        var g = _plugin.Dispatch.Guard;
        foreach (var pin in new[] { Adapters.Dispatch.GbrDispatch.Pin, Adapters.Dispatch.ArcDispatch.Pin, Adapters.Dispatch.RetainerFetch.Pin })
        {
            var installed = g.InstalledVersion(pin.InternalName, out var loaded);
            var min = g.Overrides.TryGetValue(pin.InternalName, out var o) ? o : pin.MinVersion;
            var ok = installed is not null && loaded && installed >= min;
            ImGui.TextColored(ok ? ImGuiColors.HealerGreen : ImGuiColors.DalamudOrange,
                $"{pin.InternalName}: {(installed is null ? "not installed" : loaded ? installed.ToString() : installed + " (not loaded)")} - reflection pinned to [{min}, {pin.MaxVerified}){(o is not null ? " [session override]" : "")}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{pin.Members.Count} member names verified against {pin.VerifiedAgainst}. A mismatch refuses the hand-off with a chat error instead of throwing. Test it with /lcraft guard {pin.InternalName} 99.0");
        }
        ImGui.TextDisabled($"Artisan {(_plugin.Dispatch.Artisan.Installed ? "loaded" : "missing")} · Lifestream {(_plugin.Dispatch.Lifestream.Installed ? "loaded" : "missing")} · Price match (Lazy Market Companion) {(_plugin.Dispatch.PriceMatch.Installed ? "loaded" : "missing")} (IPC)");
        var fetch = cfg.RetrieveFromRetainers;
        if (ImGui.Checkbox("Fetch missing materials from your retainers before crafting (needs a summoning bell)", ref fetch)) { cfg.RetrieveFromRetainers = fetch; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("On by default. When a craft's materials are owned but sitting on a retainer, Dispatch walks Artisan's retainer withdrawal (bell -> retainer -> Entrust Items -> withdraw) to move them into your bags, then crafts. Needs Artisan and AllaganTools, and you must be standing next to a summoning bell. Off: LazyCrafter only tells you what to fetch by hand.");

        var bell = cfg.WalkToBellWhenBlocked;
        if (ImGui.Checkbox("Walk to the nearest summoning bell when a fetch needs one, or when a run ends blocked on your own market listings", ref bell)) { cfg.WalkToBellWhenBlocked = bell; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("On by default (cards t_35be7be5 and t_034884f4). Two trips share this toggle. (1) When a craft's materials are on the retainers and no summoning bell is reachable, Dispatch queues the work and walks the character to the bell via Lifestream's 'go to market board' (/li mb) instead of refusing at the button - the fetch starts once a bell is actually reachable, and if the bell is still unreachable after 3 minutes the fetch is refused once in red. (2) When a run ENDS blocked on your own market listings, LazyCrafter sends you to the bell the same way so the listings can be pulled. Never mid-craft, never on a clean run. Off: no walking - a fetch without a bell is refused at press time, and a listing-blocked run just prints the summary. Either way, '/lcraft blocked' reprints the full detail.");

        var vendorWalk = cfg.WalkToVendorsOnCart;
        if (ImGui.Checkbox("Walk to gil vendors during a cart run, one vendor per stop (buy, press Resume, next vendor)", ref vendorWalk)) { cfg.WalkToVendorsOnCart = vendorWalk; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("On by default. When a cart is missing gil-vendor items, the run walks the character to the vendor NPCs - one vendor per stop, exactly like the summoning-bell walk: teleport to the nearest aetheryte, map flag, shopping line. Buy at the NPC, press Resume, and the run re-plans and walks to the next vendor. It never buys anything itself and never fires mid-craft. If Lifestream is missing, busy, or the teleport is refused, the stop falls back to flagging the vendor on the map and naming it in chat - the behaviour before this toggle existed. Off: vendors are flagged on the map only; buy at each and press Resume.");

        var currency = cfg.PreferCurrencyShops;
        if (ImGui.Checkbox("Prefer a currency shop over the market board when you can already afford it", ref currency)) { cfg.PreferCurrencyShops = currency; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "On by default (card t_b431de3a). Many materials are sold by currency vendors - beast-tribe traders, Grand Company quartermasters, scrip counters - " +
            "for seals, tokens or tomestones rather than gil. LazyCrafter now reads those shops and will send you to one INSTEAD of the market board, " +
            "but only when it resolves to a named NPC standing in a zone you can teleport to AND your balance of that currency already covers the price; " +
            "the cheapest affordable offer wins. If anything is missing or you cannot afford it, the item stays on the market board exactly as before - " +
            "it will never make a trade you cannot pay for, and it will never leave a material with no source. " +
            "Off: currency vendors are still NAMED on the shopping list, you just keep buying on the board.");

        var pm = cfg.PriceMatchAfterCraft;
        if (ImGui.Checkbox("After Artisan finishes a cart, print /pricematch (Lazy Market Companion) instructions for listing the results", ref pm)) { cfg.PriceMatchAfterCraft = pm; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Optional, never forced (Scope §0 item 6). Prints what was crafted and the /pricematch instructions to chat when a cart finishes; the sell list itself is not automated (Lazy Market Companion has no IPC for it). /pricematch still works - Lazy Market Companion answers it as a legacy alias.");
        // The "walk to vendors with vnavmesh" checkbox lived here until 0.1.6.2 (card t_731ea0e7). Nothing ever
        // read Configuration.VnavWalkToVendor - the Phase 6 vnavmesh spike was closed SKIPPED (t_977b94b4,
        // "walk-to-vendor stays hidden"), so the toggle was live, tickable, persisted, and wired to nothing while
        // its own help text claimed it was gated on a spike that never landed. If P6 is revived the checkbox comes
        // back WITH the code that reads it. The config property is kept (obsolete) so existing configs still load.
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
