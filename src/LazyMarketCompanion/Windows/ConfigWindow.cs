using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using static ECommons.GenericHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LazyMarketCompanion.Windows;

public sealed class ConfigWindow : Window
{
  private static readonly string[] _virtualKeyStrings = Enum.GetNames<VirtualKey>();
  private static readonly Vector4 Muted = new(0.7f, 0.7f, 0.7f, 1);
  private static readonly Vector4 Warn = new(1, 0.85f, 0.3f, 1);

  private enum Tab { None, AutoMarket, PriceLimits }
  private Tab _forceTab = Tab.None;

  // Auto-market item search
  private string _search = string.Empty;
  private List<(uint Id, string Name, bool CanHq)> _searchResults = [];
  private string _lastSearch = string.Empty;
  private bool _searchHq;

  public ConfigWindow()
    : base("Lazy Market Companion")
  {
    SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620, 420), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
  }

  public void OpenAutoMarketTab() { IsOpen = true; _forceTab = Tab.AutoMarket; }
  public void OpenPriceLimitsTab() { IsOpen = true; _forceTab = Tab.PriceLimits; }

  public override void Draw()
  {
    if (!ImGui.BeginTabBar("##lmcTabs"))
      return;

    if (ImGui.BeginTabItem("Auto-Market", _forceTab == Tab.AutoMarket ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
    {
      DrawAutoMarket();
      ImGui.EndTabItem();
    }

    if (ImGui.BeginTabItem("Price Matching"))
    {
      DrawGeneralConfig();
      ImGui.EndTabItem();
    }

    if (ImGui.BeginTabItem("Min/Max Prices", _forceTab == Tab.PriceLimits ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
    {
      DrawItemPriceLimits();
      ImGui.EndTabItem();
    }

    if (ImGui.BeginTabItem("Retainers & Hotkeys"))
    {
      DrawRetainersAndHotkeys();
      ImGui.EndTabItem();
    }

    ImGui.EndTabBar();
    _forceTab = Tab.None;
  }

  // =====================================================================================
  // Auto-Market tab
  // =====================================================================================

  private void DrawAutoMarket()
  {
    var c = Plugin.Configuration;

    var enabled = c.AutoMarketEnabled;
    if (ImGui.Checkbox("Enable Auto-Market", ref enabled)) { c.AutoMarketEnabled = enabled; c.Save(); }
    Tip("Master switch. When off, the Auto Market buttons and the AutoRetainer hook do nothing.");

    ImGui.SameLine(0, 30);
    var duringAr = c.AutoMarketDuringAutoRetainer;
    if (!AutoRetainerIPC.Installed) ImGui.BeginDisabled();
    if (ImGui.Checkbox("Run during AutoRetainer ventures", ref duringAr)) { c.AutoMarketDuringAutoRetainer = duringAr; c.Save(); }
    if (!AutoRetainerIPC.Installed) ImGui.EndDisabled();
    Tip(AutoRetainerIPC.Installed
      ? "When AutoRetainer cycles a retainer (multi-mode / ventures), claim it after AR's own work, list your items, match prices, hand it back.\r\nOnly enabled retainers (Retainers tab) are claimed."
      : "AutoRetainer is not installed / loaded.");

    ImGui.SameLine(0, 30);
    var inSweep = c.AutoMarketInPinchAllSweep;
    if (ImGui.Checkbox("Include in 'Auto Market' sweep", ref inSweep)) { c.AutoMarketInPinchAllSweep = inSweep; c.Save(); }
    Tip("The 'Auto Market' button on the retainer list lists items on every enabled retainer before pinching. Off = that button only pinches.");

    ImGui.Separator();

    ImGui.TextUnformatted("Stock source:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(180);
    var src = (int)c.AutoMarketSource;
    if (ImGui.Combo("##source", ref src, ["Bags only", "Retainer inventory only", "Bags + retainer inventory"], 3)) { c.AutoMarketSource = (StockSource)src; c.Save(); }
    Tip("Where items may be taken from. Per-item override available in the table.");

    ImGui.SameLine(0, 20);
    var retFirst = c.AutoMarketPreferRetainerStockFirst;
    if (ImGui.Checkbox("Retainer stock first", ref retFirst)) { c.AutoMarketPreferRetainerStockFirst = retFirst; c.Save(); }
    Tip("Sell the retainer's own inventory (venture loot) before touching your bags.");

    ImGui.SameLine(0, 20);
    var partial = c.AutoMarketListPartialStacks;
    if (ImGui.Checkbox("List partial stacks", ref partial)) { c.AutoMarketListPartialStacks = partial; c.Save(); }
    Tip("If there isn't a full listing's worth left, list what's there anyway. Off = only full-size listings.");

    ImGui.TextUnformatted("New listing price:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(260);
    var pm = (int)c.AutoMarketPriceMode;
    if (ImGui.Combo("##pricemode", ref pm, ["Placeholder, then match on the board", "Universalis first (fallback: placeholder)"], 2)) { c.AutoMarketPriceMode = (NewListingPriceMode)pm; c.Save(); }
    Tip("Placeholder: list at an absurd price, then immediately run the normal price match on that slot (Compare Prices).\r\n" +
        "Universalis first: ask Universalis for the data-center low before listing, so the item goes up already priced. Falls back to placeholder when Universalis has nothing.");

    ImGui.SameLine(0, 20);
    ImGui.TextUnformatted("Reserve slots:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(80);
    var reserve = c.AutoMarketReserveSlots;
    if (ImGui.InputInt("##reserve", ref reserve)) { c.AutoMarketReserveSlots = Math.Clamp(reserve, 0, 19); c.Save(); }
    Tip("Leave this many of the retainer's 20 market slots empty for manual listings.");

    var pinchAll = c.AutoMarketPinchAllAfter;
    if (ImGui.Checkbox("Pinch everything after listing", ref pinchAll)) { c.AutoMarketPinchAllAfter = pinchAll; c.Save(); }
    Tip("Off (the default): after listing, only the new listings are priced - much faster.\nOn: re-price ALL of this retainer's listings as well (same as Auto Pinch), which costs a few seconds per existing listing.");

    ImGui.SameLine(0, 20);
    var msgs = c.ShowAutoMarketMessages;
    if (ImGui.Checkbox("Chat messages", ref msgs)) { c.ShowAutoMarketMessages = msgs; c.Save(); }

    if (!c.AutoMarketPinchAllAfter)
    {
      ImGui.TextUnformatted("If a new listing can't be found:"); ImGui.SameLine();
      ImGui.SetNextItemWidth(300);
      var fb = (int)c.AutoMarketPinchFallback;
      if (ImGui.Combo("##pinchfallback", ref fb,
            ["Re-price every listing", "Leave it at the placeholder and tell me", "Re-price only my Auto-Market items"], 3))
      { c.AutoMarketPinchFallback = (PinchFallbackMode)fb; c.Save(); }
      Tip("Auto Market finds its new listings by reading your sell list, so this should not come up.\n" +
          "If it ever does:\n" +
          "Re-price every listing (the default): nothing is left unsellable, but listings you never asked us to touch get re-priced.\n" +
          "Leave it at the placeholder: nothing else is touched, but the new listing sits at 999,999,999 gil and will not sell until you price it or run Auto Pinch.\n" +
          "Only my Auto-Market items: re-price just the listings whose item is on the list below, so a listing you made by hand is never touched.");
    }

    ImGui.Separator();

    // ---- add item ----
    ImGui.TextUnformatted("Add item:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(260);
    ImGui.InputTextWithHint("##search", "type an item name (or right-click an item in your bags)", ref _search, 64);
    ImGui.SameLine();
    ImGui.Checkbox("HQ##addhq", ref _searchHq);
    Tip("Add the HQ variant. HQ and NQ are separate entries.");

    if (_search != _lastSearch)
    {
      _lastSearch = _search;
      _searchResults = _search.Length >= 2 ? SearchItems(_search) : [];
    }

    if (_searchResults.Count > 0)
    {
      if (ImGui.BeginChild("##searchResults", new Vector2(-1, Math.Min(160, 22 * _searchResults.Count + 8)), true))
      {
        foreach (var (id, name, canHq) in _searchResults)
        {
          var hq = _searchHq && canHq;
          var already = c.GetAutoMarketItem(id, hq) != null;
          if (ImGui.Selectable($"{name}{(hq ? " (HQ)" : "")}{(already ? "  [on list]" : "")}##add{id}", false))
          {
            c.GetOrAddAutoMarketItem(id, hq);
            c.Save();
            _search = string.Empty;
            _lastSearch = string.Empty;
            _searchResults = [];
            break;
          }
        }
      }
      ImGui.EndChild();
    }

    // ---- table ----
    if (c.AutoMarketItems.Count == 0)
    {
      ImGui.TextColored(Muted, "No items yet. Search above, or right-click an item in your inventory and choose 'Add to Auto-Market'.");
      return;
    }

    AutoMarketItem? remove = null;
    var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
    if (ImGui.BeginTable("##autoMarketTable", 9, flags, new Vector2(-1, -1)))
    {
      ImGui.TableSetupScrollFreeze(0, 1);
      ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 28f);
      ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("Stack", ImGuiTableColumnFlags.WidthFixed, 70f);
      ImGui.TableSetupColumn("Keep bags", ImGuiTableColumnFlags.WidthFixed, 80f);
      ImGui.TableSetupColumn("Keep ret.", ImGuiTableColumnFlags.WidthFixed, 80f);
      ImGui.TableSetupColumn("Max/ret.", ImGuiTableColumnFlags.WidthFixed, 70f);
      ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 110f);
      ImGui.TableSetupColumn("Fixed price", ImGuiTableColumnFlags.WidthFixed, 95f);
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 60f);
      ImGui.TableHeadersRow();

      foreach (var entry in c.AutoMarketItems.OrderBy(e => ItemNameResolver.GetItemName(e.ItemId)).ThenBy(e => e.HQ).ToList())
      {
        ImGui.PushID(entry.Key);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var on = entry.Enabled;
        if (ImGui.Checkbox("##on", ref on)) { entry.Enabled = on; c.Save(); }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{ItemNameResolver.GetItemName(entry.ItemId)}{(entry.HQ ? " (HQ)" : "")}");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Item ID: {entry.ItemId}  max stack {ItemNameResolver.MaxStack(entry.ItemId)}");

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var stack = entry.StackSize;
        var listingCap = LazyMarketCompanion.AutoMarket.MarketListingCap.For((int)ItemNameResolver.MaxStack(entry.ItemId));
        if (ImGui.InputInt("##stack", ref stack, 0, 0)) { entry.StackSize = Math.Clamp(stack, 0, listingCap); c.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Units per listing. 0 = the market's maximum ({listingCap}). The market accepts at most {listingCap} of this item per listing; larger values are clamped.");

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var keepB = entry.KeepInBags;
        if (ImGui.InputInt("##keepb", ref keepB, 0, 0)) { entry.KeepInBags = Math.Max(keepB, 0); c.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Never sell below this many from your bags.");

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var keepR = entry.KeepInRetainer;
        if (ImGui.InputInt("##keepr", ref keepR, 0, 0)) { entry.KeepInRetainer = Math.Max(keepR, 0); c.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Never sell below this many from the retainer's own inventory.");

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var max = entry.MaxListingsPerRetainer;
        if (ImGui.InputInt("##max", ref max, 0, 0)) { entry.MaxListingsPerRetainer = Math.Max(max, 0); c.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Max listings of this item on one retainer at once (existing ones count). 0 = no cap.");

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var srcIdx = entry.SourceOverride.HasValue ? (int)entry.SourceOverride.Value + 1 : 0;
        if (ImGui.Combo("##src", ref srcIdx, ["(global)", "Bags", "Retainer", "Both"], 4))
        {
          entry.SourceOverride = srcIdx == 0 ? null : (StockSource)(srcIdx - 1);
          c.Save();
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var fixedPrice = entry.FixedPrice;
        if (ImGui.InputInt("##fixed", ref fixedPrice, 0, 0)) { entry.FixedPrice = Math.Max(fixedPrice, 0); c.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("List at exactly this unit price instead of matching. 0 = match the board.");

        ImGui.TableNextColumn();
        if (ImGui.SmallButton("Remove"))
          remove = entry;

        ImGui.PopID();
      }

      ImGui.EndTable();
    }

    if (remove != null)
    {
      c.AutoMarketItems.Remove(remove);
      c.Save();
    }
  }

  private static List<(uint Id, string Name, bool CanHq)> SearchItems(string query)
  {
    var sheet = Svc.Data.GetExcelSheet<Item>();
    var q = query.Trim();
    var exact = new List<(uint, string, bool)>();
    var prefix = new List<(uint, string, bool)>();
    var contains = new List<(uint, string, bool)>();
    foreach (var item in sheet)
    {
      if (item.RowId == 0 || item.ItemSearchCategory.RowId == 0 || item.IsUntradable) continue;
      var name = item.Name.ToString();
      if (name.Length == 0) continue;
      if (name.Equals(q, StringComparison.OrdinalIgnoreCase)) exact.Add((item.RowId, name, item.CanBeHq));
      else if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) prefix.Add((item.RowId, name, item.CanBeHq));
      else if (name.Contains(q, StringComparison.OrdinalIgnoreCase)) contains.Add((item.RowId, name, item.CanBeHq));
      if (exact.Count + prefix.Count + contains.Count > 200) break;
    }
    return exact.Concat(prefix.OrderBy(x => x.Item2)).Concat(contains.OrderBy(x => x.Item2)).Take(25).ToList();
  }

  // =====================================================================================
  // Price matching tab (inherited)
  // =====================================================================================

  private static void DrawGeneralConfig()
  {
    var c = Plugin.Configuration;

    var hq = c.HQ;
    if (ImGui.Checkbox("Use HQ price", ref hq)) { c.HQ = hq; c.Save(); }
    Tip("If checked, will use the hq price (if item is hq; will fail if there is no HQ price on the MB)");

    ImGui.Separator();

    ImGui.BeginGroup();
    ImGui.Text("Price Mode:");
    ImGui.SameLine();
    var enumValues = Enum.GetNames<UndercutMode>();
    int index = Array.IndexOf(enumValues, c.UndercutMode.ToString());
    ImGui.SetNextItemWidth(160);
    if (ImGui.Combo("##undercutModeCombo", ref index, enumValues, enumValues.Length))
    {
      var value = Enum.Parse<UndercutMode>(enumValues[index]);
      if (value == UndercutMode.Percentage && c.UndercutAmount >= 100)
        c.UndercutAmount = 1;
      c.UndercutMode = value;
      c.Save();
    }
    ImGui.EndGroup();
    Tip("Defines whether to match by a fixed Gil amount or use a percentage (0 = exact match)");

    ImGui.BeginGroup();
    ImGui.Text("Match amount (0 = exact match):");
    ImGui.SameLine();
    int amount = c.UndercutAmount;
    ImGui.SetNextItemWidth(160);
    if (c.UndercutMode == UndercutMode.FixedAmount)
    {
      if (ImGui.InputInt("##undercutAmountFixed", ref amount)) { c.UndercutAmount = Math.Clamp(amount, 0, int.MaxValue); c.Save(); }
    }
    else
    {
      if (ImGui.SliderInt("##undercutAmountPercentage", ref amount, 1, 99)) { c.UndercutAmount = amount; c.Save(); }
    }
    ImGui.SameLine();
    ImGui.Text(c.UndercutMode == UndercutMode.FixedAmount ? "Gil" : "%%");
    ImGui.EndGroup();
    Tip("Amount below the lowest listing. 0 = list at exactly the lowest price.");

    ImGui.BeginGroup();
    ImGui.Text("Max price change percentage:");
    ImGui.SameLine();
    float maxUndercut = c.MaxUndercutPercentage;
    ImGui.SetNextItemWidth(160);
    if (ImGui.SliderFloat("##maximumUndercutAmountPercentage", ref maxUndercut, 0.1f, 99.9f, "%.1f")) { c.MaxUndercutPercentage = MathF.Round(maxUndercut, 1); c.Save(); }
    ImGui.SameLine();
    ImGui.Text("%%");
    ImGui.EndGroup();
    Tip("Existing listings are never cut by more than this. Fresh Auto-Market listings (placeholder price) are exempt.");

    var undercutSelf = c.UndercutSelf;
    if (ImGui.Checkbox("Match Self", ref undercutSelf)) { c.UndercutSelf = undercutSelf; c.Save(); }
    Tip("If checked, your own retainer listings will be matched (instead of skipped)");

    var useUniversalis = c.UseUniversalisDataCenterPrices;
    if (ImGui.Checkbox("Use Universalis data center prices", ref useUniversalis)) { c.UseUniversalisDataCenterPrices = useUniversalis; c.Save(); }
    Tip("If checked, price checks use the cheapest listing on your current data center from Universalis instead of the in-game Compare Prices window.");

    ImGui.Separator();

    int mbDelay = c.GetMBPricesDelayMS;
    ImGui.BeginGroup();
    ImGui.Text("Market Board Price Check Delay (ms)");
    if (ImGui.SliderInt("###sliderMBDelay", ref mbDelay, 1, 10000)) { c.GetMBPricesDelayMS = mbDelay; c.Save(); }
    ImGui.EndGroup();
    Tip("Delay before opening the market board price list. Recommended 3000-4000ms.");

    int keepOpen = c.MarketBoardKeepOpenMS;
    ImGui.BeginGroup();
    ImGui.Text("Market Board Keep Open Time (ms)");
    if (ImGui.SliderInt("###sliderMBKeepOpen", ref keepOpen, 1, 10000)) { c.MarketBoardKeepOpenMS = keepOpen; c.Save(); }
    ImGui.EndGroup();
    Tip("Time to keep the market board open when fetching prices. Recommended 1000-2000ms.");

    bool chatErrors = c.ShowErrorsInChat;
    if (ImGui.Checkbox("Show errors in chat", ref chatErrors)) { c.ShowErrorsInChat = chatErrors; c.Save(); }

    ImGui.SameLine(0, 30);
    bool adjustments = c.ShowPriceAdjustmentsMessages;
    if (ImGui.Checkbox("Show price adjustments", ref adjustments)) { c.ShowPriceAdjustmentsMessages = adjustments; c.Save(); }

    ImGui.SameLine(0, 30);
    bool retainerNames = c.ShowRetainerNames;
    if (ImGui.Checkbox("Show retainer names", ref retainerNames)) { c.ShowRetainerNames = retainerNames; c.Save(); }

    int defaultAmount = c.DefaultAmount;
    ImGui.BeginGroup();
    ImGui.Text("Default amount:");
    ImGui.SameLine();
    ImGui.SetNextItemWidth(160);
    if (ImGui.InputInt("##defaultAmount", ref defaultAmount)) { c.DefaultAmount = Math.Clamp(defaultAmount, 0, int.MaxValue); c.Save(); }
    ImGui.SameLine();
    ImGui.Text("Gil");
    ImGui.EndGroup();
    Tip("Price to use when no board price can be found (0 = leave the listing alone)");

    var showCtx = c.ShowInventoryContextMenuEntry;
    if (ImGui.Checkbox("Show inventory context menu entries", ref showCtx)) { c.ShowInventoryContextMenuEntry = showCtx; c.Save(); }
    Tip("Adds 'Add to Auto-Market' and 'Add price limits' to inventory item right-click menus.");

    ImGui.BeginGroup();
    if (ImGui.Button("Clear retainer cache")) { c.SeenRetainers.Clear(); c.Save(); }
    ImGui.EndGroup();
    Tip("Clears the list of your own retainers (used to avoid undercutting yourself).");

    if (c.ImportedFromDagobert)
    {
      ImGui.Separator();
      ImGui.TextColored(Muted, "Settings were imported from Dagobert Price Matcher. You can uninstall that plugin.");
    }

    // Advanced / Diagnostics. Collapsed by default: nothing in here changes what the plugin
    // does, and the one control writes to the log, which deserves a plain warning.
    ImGui.Spacing();
    ImGui.Separator();
    if (ImGui.CollapsingHeader("Advanced / Diagnostics"))
    {
      var telemetry = c.DecisionTelemetry;
      if (ImGui.Checkbox("Log price decisions", ref telemetry)) { c.DecisionTelemetry = telemetry; c.Save(); }
      Tip("Off by default. When on, Lazy Market Companion writes a diagnostic line to the Dalamud\n" +
          "plugin log for every price decision it makes - both the prices it sets and the ones it\n" +
          "refuses to set because they would undercut by more than your maximum. Each line carries\n" +
          "the item, quantity, old price, new price, where the price came from and the percentage\n" +
          "change. Lines start with \"" + MarketTelemetry.Prefix + "\".\n\n" +
          "It changes no pricing behaviour and sends nothing anywhere - the lines only go to your\n" +
          "own local plugin log, so you can check afterwards whether matching the board actually\n" +
          "earned more than the fallback price did.\n\n" +
          "Same as /lmc telemetry on|off.");
      ImGui.TextColored(Muted, $"Writes \"{MarketTelemetry.Prefix}\" lines to the plugin log. Nothing leaves your PC.");
    }
  }

  // =====================================================================================
  // Min/Max tab (inherited)
  // =====================================================================================

  private static void DrawItemPriceLimits()
  {
    ImGui.Text("Per-Item Price Limits");
    Tip("Minimum and maximum unit prices per item, applied to matched prices. 0 = no limit.");

    if (Plugin.Configuration.ItemPriceLimits.Count == 0)
    {
      ImGui.TextColored(Muted, "Right-click an inventory item and choose 'Add price limits'.");
      return;
    }

    ItemPriceLimit? limitToRemove = null;
    var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp;
    if (ImGui.BeginTable("##itemPriceLimitsTable", 4, tableFlags))
    {
      ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("Min", ImGuiTableColumnFlags.WidthFixed, 120f);
      ImGui.TableSetupColumn("Max", ImGuiTableColumnFlags.WidthFixed, 120f);
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70f);
      ImGui.TableHeadersRow();

      foreach (var limit in Plugin.Configuration.ItemPriceLimits
            .OrderBy(limit => ItemNameResolver.GetItemName(limit.ItemId))
            .ThenBy(limit => limit.ItemId)
            .ToList())
      {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(ItemNameResolver.GetItemName(limit.ItemId));
        if (ImGui.IsItemHovered())
          ImGui.SetTooltip($"Item ID: {limit.ItemId}");

        ImGui.TableNextColumn();
        var minPrice = limit.MinPrice;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt($"##itemPriceLimitMin{limit.ItemId}", ref minPrice))
        {
          limit.MinPrice = Math.Clamp(minPrice, 0, int.MaxValue);
          if (limit.MaxPrice > 0 && limit.MaxPrice < limit.MinPrice)
            limit.MaxPrice = limit.MinPrice;
          Plugin.Configuration.Save();
        }

        ImGui.TableNextColumn();
        var maxPrice = limit.MaxPrice;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt($"##itemPriceLimitMax{limit.ItemId}", ref maxPrice))
        {
          limit.MaxPrice = Math.Clamp(maxPrice, 0, int.MaxValue);
          if (limit.MaxPrice > 0 && limit.MinPrice > limit.MaxPrice)
            limit.MinPrice = limit.MaxPrice;
          Plugin.Configuration.Save();
        }

        ImGui.TableNextColumn();
        if (ImGui.SmallButton($"Remove##itemPriceLimitRemove{limit.ItemId}"))
          limitToRemove = limit;
      }

      ImGui.EndTable();
    }

    if (limitToRemove != null)
    {
      Plugin.Configuration.ItemPriceLimits.Remove(limitToRemove);
      Plugin.Configuration.Save();
    }
  }

  // =====================================================================================
  // Retainers + hotkeys tab (inherited)
  // =====================================================================================

  private static void DrawRetainersAndHotkeys()
  {
    var c = Plugin.Configuration;

    ImGui.Text("Retainer selection");
    Tip("Unchecked retainers are skipped by the sweep buttons and never claimed from AutoRetainer.\r\nOpen the retainer list in-game to refresh names.");

    unsafe
    {
      string[]? retainerNameArray = null;
      bool namesUpdated = false;

      if (TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && IsAddonReady(addon))
      {
        try
        {
          var retainerList = new AddonMaster.RetainerList(addon);
          retainerNameArray = [.. retainerList.Retainers.Select(r => r.Name)];
          var currentNames = new HashSet<string>(retainerNameArray);
          var storedNames = new HashSet<string>(c.LastKnownRetainerNames);
          if (!currentNames.SetEquals(storedNames))
          {
            c.LastKnownRetainerNames = [.. retainerNameArray];
            c.EnabledRetainerNames.RemoveWhere(name => !currentNames.Contains(name) && name != Configuration.ALL_DISABLED_SENTINEL);
            c.Save();
            namesUpdated = true;
          }
        }
        catch { }
      }

      var namesToDisplay = retainerNameArray ?? [.. c.LastKnownRetainerNames];
      if (namesToDisplay.Length > 0)
      {
        for (int i = 0; i < namesToDisplay.Length; i++)
        {
          string retainerName = namesToDisplay[i];
          bool allDisabled = c.EnabledRetainerNames.Contains(Configuration.ALL_DISABLED_SENTINEL);
          bool enabled = !allDisabled && (c.EnabledRetainerNames.Count == 0 || c.EnabledRetainerNames.Contains(retainerName));

          if (ImGui.Checkbox($"{retainerName}##retainer{i}", ref enabled))
          {
            c.EnabledRetainerNames.Remove(Configuration.ALL_DISABLED_SENTINEL);
            if (enabled)
            {
              c.EnabledRetainerNames.Add(retainerName);
              if (c.EnabledRetainerNames.Count == namesToDisplay.Length)
                c.EnabledRetainerNames.Clear();
            }
            else
            {
              if (c.EnabledRetainerNames.Count == 0)
              {
                foreach (string name in namesToDisplay)
                  if (name != retainerName)
                    c.EnabledRetainerNames.Add(name);
              }
              else
              {
                c.EnabledRetainerNames.Remove(retainerName);
                if (c.EnabledRetainerNames.Count == 0)
                  c.EnabledRetainerNames.Add(Configuration.ALL_DISABLED_SENTINEL);
              }
            }
            c.Save();
          }

          if (i % 2 == 0 && i < namesToDisplay.Length - 1)
            ImGui.SameLine(0, 150);
        }

        if (retainerNameArray == null && !namesUpdated)
          ImGui.TextColored(Muted, "(cached list - open the retainer list to refresh)");
      }
      else
      {
        ImGui.TextColored(Warn, "Open the retainer list in-game to configure retainer selection");
      }
    }

    ImGui.Separator();

    bool enablePostPinchKey = c.EnablePostPinchkey;
    if (ImGui.Checkbox("Enable Post Pinch Hotkey", ref enablePostPinchKey)) { c.EnablePostPinchkey = enablePostPinchKey; c.Save(); }
    Tip("Hold this key while posting an item manually to auto-fill the matched price.");

    if (enablePostPinchKey)
    {
      ImGui.SameLine();
      ImGui.SetNextItemWidth(140);
      var index = Array.IndexOf(_virtualKeyStrings, c.PostPinchKey.ToString());
      if (ImGui.Combo("##postPinchKeyCombo", ref index, _virtualKeyStrings, _virtualKeyStrings.Length)) { c.PostPinchKey = Enum.Parse<VirtualKey>(_virtualKeyStrings[index]); c.Save(); }
    }

    bool enablePinchKey = c.EnablePinchKey;
    if (ImGui.Checkbox("Enable Pinch Hotkey", ref enablePinchKey)) { c.EnablePinchKey = enablePinchKey; c.Save(); }
    Tip("Press this key while the retainer list / sell list is open to start the sweep.");

    if (enablePinchKey)
    {
      ImGui.SameLine();
      ImGui.SetNextItemWidth(140);
      var index = Array.IndexOf(_virtualKeyStrings, c.PinchKey.ToString());
      if (ImGui.Combo("##pinchKeyCombo", ref index, _virtualKeyStrings, _virtualKeyStrings.Length)) { c.PinchKey = Enum.Parse<VirtualKey>(_virtualKeyStrings[index]); c.Save(); }
    }
  }

  private static void Tip(string text)
  {
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip(text);
  }
}
