using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using static ECommons.GenericHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LalaUiKit;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LazyMarketCompanion.Windows;

public sealed class ConfigWindow : Window
{
  private static readonly string[] _virtualKeyStrings = Enum.GetNames<VirtualKey>();
  private enum Tab { None, AutoMarket, PriceLimits }
  private enum PageId { AutoMarket, Inventory, PriceMatching, PriceLimits, Setup }
  private sealed record MarketPage(PageId Id, string Group, string Title, string Description);

  private static readonly MarketPage[] Pages =
  [
    new(PageId.AutoMarket, "Market", "Auto-Market", "Always-sell items, stock rules, listing limits, and category routing."),
    new(PageId.PriceMatching, "Market", "Price Matching", "Board matching, fallbacks, timing, messages, and diagnostics."),
    new(PageId.PriceLimits, "Market", "Min/Max Prices", "Per-item minimum and maximum unit prices applied to matched prices."),
    new(PageId.Inventory, "Inventory", "Inventory", "Space at a glance, venture loot, ownership, and the gear mover."),
    new(PageId.Setup, "Setup", "Retainers & Hotkeys", "Retainer selection, keyboard actions, overlay buttons, and support."),
  ];

  private Tab _forceTab = Tab.None;
  private PageId _currentPage = PageId.AutoMarket;
  private MarketPage CurrentPage => Pages.First(page => page.Id == _currentPage);

  // Auto-market item search
  private string _search = string.Empty;
  private List<(uint Id, string Name, bool CanHq)> _searchResults = [];
  private string _lastSearch = string.Empty;
  private bool _searchHq;

  /// <summary>
  /// 0.1.53.0: read-only feed for the quarantined-moves list below (wired in Plugin.cs to the
  /// live reconciliation ledger). A Func, not a reference, so the window never owns automation
  /// state - it renders whatever the ledger holds at draw time.
  /// </summary>
  internal Func<IReadOnlyList<(string Key, DateTime RecordedAtUtc)>>? ReconcileLedgerSource { get; set; }

  /// <summary>
  /// 0.1.58.0: the quarantined-moves list is no longer read-only. A hold left over from a
  /// transient condition would otherwise sit out its whole 24-hour retry window while the stack
  /// stays put, which reads in the inventory as an item that simply will not move. These release
  /// one entry, or all of them; the stack itself is not touched, it just becomes plannable again
  /// on the next sweep. Funcs, not references, so the window never owns automation state.
  /// </summary>
  internal Func<string, bool>? ReconcileLedgerRelease { get; set; }

  /// <summary>0.1.58.0: see <see cref="ReconcileLedgerRelease"/>.</summary>
  internal Func<int>? ReconcileLedgerReleaseAll { get; set; }

  /// <summary>The Inventory tab's draw (Inventory/InventoryTab.cs), wired in Plugin.cs. Null = no tab.</summary>
  internal System.Action? DrawInventoryTab { get; set; }

  /// <summary>Live automation state for the shell pill and footer (wired in Plugin.cs). Null reads idle.</summary>
  internal Func<bool>? AutomationBusy { get; set; }

  /// <summary>The footer Stop's cancel - the same cancel the retainer overlay's Cancel button uses (wired in Plugin.cs).</summary>
  internal Action<string>? CancelAutomation { get; set; }

  public ConfigWindow()
    : base("Lazy Market Companion")
  {
    Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620, 420), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
  }

  public void OpenAutoMarketTab() { IsOpen = true; _forceTab = Tab.AutoMarket; }
  public void OpenPriceLimitsTab() { IsOpen = true; _forceTab = Tab.PriceLimits; }

  /// <summary>The shell draws its own full-bleed bands from the window edge, so the window itself carries no padding.</summary>
  public override void PreDraw()
  {
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
  }

  public override void PostDraw()
  {
    ImGui.PopStyleVar();
  }

  public override void Draw()
  {
    MarketChrome.PushFrame();
    try
    {
      DrawFrame();
    }
    finally
    {
      MarketChrome.PopFrame();
    }
  }

  private UiState CurrentState()
  {
    var busy = AutomationBusy?.Invoke() ?? false;
    return busy ? UiState.Running : !Plugin.Configuration.AutoMarketEnabled ? UiState.Idle : Plugin.Configuration.AutoMarketItems.Count == 0 ? UiState.Empty : UiState.Ready;
  }

  private void DrawFrame()
  {
    if (_forceTab == Tab.AutoMarket)
      _currentPage = PageId.AutoMarket;
    else if (_forceTab == Tab.PriceLimits)
      _currentPage = PageId.PriceLimits;

    var groups = Pages.Select(page => page.Group).Distinct().ToArray();
    var nav = groups.Select(group => new MarketChrome.NavTab(group, CurrentPage.Group == group)).ToArray();
    MarketChrome.HeaderBand("Lazy Market Companion", CurrentState(), nav, out var navClicked, out var brandClicked, out var closeClicked);
    if (navClicked >= 0)
      _currentPage = Pages.First(page => page.Group == groups[navClicked]).Id;
    if (brandClicked)
      _currentPage = PageId.AutoMarket;
    if (closeClicked)
      IsOpen = false;

    // Sub-navigation only exists for a group with two or more pages.
    var features = Pages.Where(page => page.Group == CurrentPage.Group).ToArray();
    if (features.Length >= 2)
    {
      MarketChrome.SubNavBand(features.Select(page => page.Title).ToArray(), Array.FindIndex(features, page => page.Id == _currentPage), out var featureClicked);
      if (featureClicked >= 0)
        _currentPage = features[featureClicked].Id;
    }
    MarketChrome.PageHeader(CurrentPage.Title, CurrentPage.Description, LiveCount());

    var busy = AutomationBusy?.Invoke() ?? false;
    var footerHeight = MarketChrome.FooterHeight(ImGui.GetWindowWidth(), busy);
    if (MarketChrome.BeginBody(footerHeight))
    {
      switch (_currentPage)
      {
        case PageId.AutoMarket: DrawAutoMarket(); break;
        case PageId.Inventory:
          if (DrawInventoryTab is not null)
            DrawInventoryTab();
          else
            MarketChrome.Notice("Needs setup · inventory services are unavailable.", UiState.NeedsSetup);
          break;
        case PageId.PriceMatching: DrawGeneralConfig(); break;
        case PageId.PriceLimits: DrawItemPriceLimits(); break;
        case PageId.Setup: DrawRetainersAndHotkeys(); break;
      }
    }
    MarketChrome.EndBody();
    DrawFooter();
    _forceTab = Tab.None;
  }

  private string LiveCount()
  {
    var c = Plugin.Configuration;
    return _currentPage switch
    {
      PageId.AutoMarket => $"{c.AutoMarketItems.Count(entry => entry.Enabled)} enabled · {c.AutoMarketItems.Count} total",
      PageId.PriceMatching => $"{c.SeenRetainers.Count} cached retainers · {c.UndercutMode}",
      PageId.PriceLimits => $"{c.ItemPriceLimits.Count} price limits",
      PageId.Inventory => $"{c.CategoryRetainerRules.Count} routes · {c.VentureLootOptIns.Count} loot opt-ins",
      PageId.Setup when c.EnabledRetainerNames.Contains(Configuration.ALL_DISABLED_SENTINEL) => "all retainers skipped",
      PageId.Setup when c.EnabledRetainerNames.Count == 0 => "all retainers enabled",
      PageId.Setup => $"{c.EnabledRetainerNames.Count} retainers enabled",
      _ => string.Empty,
    };
  }

  private void DrawFooter()
  {
    var busy = AutomationBusy?.Invoke() ?? false;
    var state = CurrentState();
    var action = busy ? "the current market action" : !Plugin.Configuration.AutoMarketEnabled ? "Auto-Market is off" : Plugin.Configuration.AutoMarketItems.Count == 0 ? "no Auto-Market entries" : "watching the Auto-Market list";
    var detail = busy ? "Stop cancels the current action." : Plugin.Configuration.AutoMarketEnabled && Plugin.Configuration.AutoMarketItems.Count == 0 ? "Add an item from the Auto-Market page or inventory context menu." : $"{Plugin.Configuration.AutoMarketItems.Count} configured entries · {Plugin.Configuration.ItemPriceLimits.Count} price limits";
    MarketChrome.Footer(state, action, detail, busy ? "Stop" : null, busy ? () => CancelAutomation?.Invoke("cancelled from the config window") : null);
  }

  // =====================================================================================
  // Auto-Market tab
  // =====================================================================================

  private static bool ComboField(string label, string id, ref int index, string[] items)
  {
    var local = index;
    var changed = MarketChrome.Field(label, width => MarketChrome.Combo(id, ref local, items, width));
    if (changed)
      index = local;
    return changed;
  }

  private static bool IntField(string label, string id, ref int value, string? unit = null)
  {
    var local = value;
    var changed = MarketChrome.Field(label, width => MarketChrome.IntInput(id, ref local, width), unit);
    if (changed)
      value = local;
    return changed;
  }

  private void DrawAutoMarket()
  {
    var c = Plugin.Configuration;
    var total = c.AutoMarketItems.Count;
    var enabledCount = c.AutoMarketItems.Count(entry => entry.Enabled);

    MarketChrome.StatCards(
      new MarketChrome.Stat("Auto-Market list", total.ToString(), "Configured item and quality entries"),
      new MarketChrome.Stat("Enabled entries", enabledCount.ToString(), "Eligible for the next listing pass", total > 0 ? $"/ {total}" : null, total > 0 ? (float)enabledCount / total : null),
      new MarketChrome.Stat("Category routes", c.CategoryRetainerRules.Count.ToString(), "Explicit retainer assignments"));

    MarketChrome.Card("automation", () =>
    {
      MarketChrome.CardHeader("Automation", "Existing switches and limits continue to write the same saved settings.");
      var enabled = c.AutoMarketEnabled;
      if (MarketChrome.Toggle("Enable Auto-Market", ref enabled)) { c.AutoMarketEnabled = enabled; c.Save(); }
      Tip("Master switch. When off, the Auto Market buttons and the AutoRetainer hook do nothing.");

      var markers = c.AutoMarketMarkersEnabled;
      if (MarketChrome.Toggle("Show Auto-Market markers in bags", ref markers)) { c.AutoMarketMarkersEnabled = markers; c.Save(); }
      Tip("Draws a small green dot on every bag stack that is on the Auto-Market list and enabled.\r\nAn item on the list with its tick unticked gets no dot - it would not be listed.");

      var duringAr = c.AutoMarketDuringAutoRetainer;
      if (!AutoRetainerIPC.Installed) ImGui.BeginDisabled();
      if (MarketChrome.Toggle("Run during AutoRetainer ventures", ref duringAr)) { c.AutoMarketDuringAutoRetainer = duringAr; c.Save(); }
      if (!AutoRetainerIPC.Installed) ImGui.EndDisabled();
      TipAlways(AutoRetainerIPC.Installed
        ? "When AutoRetainer cycles a retainer (multi-mode / ventures), claim it after AR's own work, list your items, match prices, hand it back.\r\nOnly enabled retainers (Retainers tab) are claimed."
        : "AutoRetainer is not installed / loaded.");

      var inSweep = c.AutoMarketInPinchAllSweep;
      if (MarketChrome.Toggle("Include in 'Auto Market' sweep", ref inSweep)) { c.AutoMarketInPinchAllSweep = inSweep; c.Save(); }
      Tip("The 'Auto Market' button on the retainer list lists items on every enabled retainer before pinching. Off = that button only pinches.");

      var msgs = c.ShowAutoMarketMessages;
      if (MarketChrome.Toggle("Chat messages", ref msgs)) { c.ShowAutoMarketMessages = msgs; c.Save(); }
    });

    MarketChrome.Card("stock", () =>
    {
      MarketChrome.CardHeader("Stock & listing", "Where items come from, how new listings are priced, and what happens when a listing is missing.");
      var src = (int)c.AutoMarketSource;
      if (ComboField("Stock source", "##source", ref src, ["Bags only", "Retainer inventory only", "Bags + retainer inventory"])) { c.AutoMarketSource = (StockSource)src; c.Save(); }
      Tip("Where items may be taken from. Per-item override available in the table.");

      var retFirst = c.AutoMarketPreferRetainerStockFirst;
      if (MarketChrome.Toggle("Retainer stock first", ref retFirst)) { c.AutoMarketPreferRetainerStockFirst = retFirst; c.Save(); }
      Tip("Sell the retainer's own inventory (venture loot) before touching your bags.");

      var partial = c.AutoMarketListPartialStacks;
      if (MarketChrome.Toggle("List partial stacks", ref partial)) { c.AutoMarketListPartialStacks = partial; c.Save(); }
      Tip("If there isn't a full listing's worth left, list what's there anyway. Off = only full-size listings.");

      var pm = (int)c.AutoMarketPriceMode;
      if (ComboField("New listing price", "##pricemode", ref pm, ["Placeholder, then match on the board", "Universalis first (fallback: placeholder)"])) { c.AutoMarketPriceMode = (NewListingPriceMode)pm; c.Save(); }
      Tip("Placeholder: list at an absurd price, then immediately run the normal price match on that slot (Compare Prices).\r\n" +
          "Universalis first: ask Universalis for the data-center low before listing, so the item goes up already priced. Falls back to placeholder when Universalis has nothing.");

      var reserve = c.AutoMarketReserveSlots;
      if (IntField("Reserve slots", "##reserve", ref reserve)) { c.AutoMarketReserveSlots = Math.Clamp(reserve, 0, 19); c.Save(); }
      Tip("Leave this many of the retainer's 20 market slots empty for manual listings.");

      var pinchAll = c.AutoMarketPinchAllAfter;
      if (MarketChrome.Toggle("Pinch everything after listing", ref pinchAll)) { c.AutoMarketPinchAllAfter = pinchAll; c.Save(); }
      Tip("Off (the default): after listing, only the new listings are priced - much faster.\nOn: re-price ALL of this retainer's listings as well (same as Auto Pinch), which costs a few seconds per existing listing.");

      if (!c.AutoMarketPinchAllAfter)
      {
        var fb = (int)c.AutoMarketPinchFallback;
        if (ComboField("If a new listing can't be found", "##pinchfallback", ref fb,
              ["Re-price every listing", "Leave it at the placeholder and tell me", "Re-price only my Auto-Market items"]))
        { c.AutoMarketPinchFallback = (PinchFallbackMode)fb; c.Save(); }
        Tip("Auto Market finds its new listings by reading your sell list, so this should not come up.\n" +
            "If it ever does:\n" +
            "Re-price every listing (the default): nothing is left unsellable, but listings you never asked us to touch get re-priced.\n" +
            "Leave it at the placeholder: nothing else is touched, but the new listing sits at 999,999,999 gil and will not sell until you price it or run Auto Pinch.\n" +
            "Only my Auto-Market items: re-price just the listings whose item is on the list below, so a listing you made by hand is never touched.");
      }

      var sortIdx = (int)c.AutoMarketSortMode;
      if (ComboField("Listing order", "##sortmode", ref sortIdx, ["List order", "Cheapest first", "Fastest selling first", "Most expensive first"]))
      { c.AutoMarketSortMode = (MarketSortMode)sortIdx; c.Save(); }
      Tip("When the retainer does not have enough free market slots for everything on your Auto-Market list, this decides which items get the slots first.\r\n"
          + "Fastest selling first (the default): one Universalis lookup ranks your items by how many sell per day, so the slots go to what actually moves.\r\n"
          + "Cheapest / most expensive first: same lookup, ranked by current board price.\r\n"
          + "List order: the order of the table below - how it worked before.\r\n"
          + "Items Universalis has no fresh data for keep their list position and sort last, and a failed lookup changes nothing.");
    });

    MarketChrome.Card("value_gate", () =>
    {
      MarketChrome.CardHeader("Value gate", "Low-value stock takes the vendor leg instead of a market slot.");
      var gate = c.AutoMarketValueGateEnabled;
      if (MarketChrome.Toggle("Only list items worth more than", ref gate)) { c.AutoMarketValueGateEnabled = gate; c.Save(); }
      var threshold = (int)Math.Min(c.AutoMarketValueGateThresholdGil, int.MaxValue);
      var thresholdChanged = MarketChrome.Field("Value threshold", width => MarketChrome.IntInput("##gatethreshold", ref threshold, width, 0), "gil, net of fees");
      if (thresholdChanged) { c.AutoMarketValueGateThresholdGil = Math.Max(threshold, 0); c.Save(); }
      Tip("Before listing, Auto-Market checks every enabled item against current Universalis prices. Items whose total sellable value "
          + "(current board price x everything it could sell of that item, after the market's 5% fee) is at or under this number take the vendor leg instead of a market slot - only NQ non-gear items the Item sheet prices as vendorable; the retainer sells them to a vendor in the same session.\r\n"
          + "The value they will fetch is the vendor price (the same number the sell window autofills), which is usually far below the market board price - if in doubt, set the threshold lower.\r\n"
          + "Anything Universalis is unsure about is still LISTED (0.2.8.0): no data, data older than the freshness window below, or no listing of the right quality. The listing is priced by the normal pricing pass (the live board through Auto Pinch by default), and it is never vendored or pulled on an unconfirmed price - only a fresh price from your own world can do that, never a data-center price. With \"Use Universalis data center prices\" on, every price is a data-center price, so nothing is vendored or pulled at all. Vendoring an item on a guess cannot be undone; listing it can.\r\n"
          + "0 = the switch does nothing.");

      if (c.AutoMarketValueGateEnabled)
      {
        var gateFresh = c.AutoMarketGateFreshnessHours;
        var freshChanged = MarketChrome.Field("Only trust gate prices newer than", width => MarketChrome.SliderInt("###sliderGateFreshness", ref gateFresh, 1, 168, width, " hours"));
        if (freshChanged) { c.AutoMarketGateFreshnessHours = Math.Clamp(gateFresh, 1, 168); c.Save(); }
        Tip("Universalis is crowd-sourced and lags. Prices older than this never hold an item back - the item is listed normally. Same idea as the Auto Pinch pre-flight freshness window.");
      }
    });

    MarketChrome.Card("category_routing", () => DrawCategoryRouting(c));
    MarketChrome.Card("quarantined_moves", DrawQuarantinedMoves);
    MarketChrome.Card("add_item", () => DrawAddItem(c));

    if (c.AutoMarketItems.Count == 0)
    {
      MarketChrome.Notice("Empty · search above, or right-click an inventory item and choose 'Add to Auto-Market'.", UiState.Empty);
      return;
    }

    MarketChrome.Card("entries", () => DrawEntriesTable(c));
  }

  private void DrawAddItem(Configuration c)
  {
    MarketChrome.CardHeader("Add item", "Search the marketable item sheet, or use the existing inventory context menu.");
    ImGui.SetNextItemWidth(MarketChrome.Avail());
    ImGui.InputTextWithHint("##search", "type an item name (or right-click an item in your bags)", ref _search, 64);
    MarketChrome.RowsDone();
    MarketChrome.Toggle("HQ##addhq", ref _searchHq);
    Tip("Add the HQ variant. HQ and NQ are separate entries.");

    if (_search != _lastSearch)
    {
      _lastSearch = _search;
      _searchResults = _search.Length >= 2 ? SearchItems(_search) : [];
    }

    if (_searchResults.Count == 0)
      return;

    ImGui.Dummy(new Vector2(0, 4));
    const float rowH = 32f;
    var shown = Math.Min(6, _searchResults.Count);
    ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f));
    var open = ImGui.BeginChild("##searchResults", new Vector2(MarketChrome.Avail(), (shown * rowH) + 24), true);
    ImGui.PopStyleVar(3);
    ImGui.PopStyleColor(2);
    if (open)
    {
      foreach (var (id, name, canHq) in _searchResults)
      {
        var hq = _searchHq && canHq;
        var already = c.GetAutoMarketItem(id, hq) != null;
        if (ImGui.Selectable($"{name}{(hq ? " (HQ)" : "")}{(already ? "  [on list]" : "")}##add{id}", false, ImGuiSelectableFlags.None, new Vector2(0, rowH)))
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

  private static void DrawEntriesTable(Configuration c)
  {
    MarketChrome.CardHeader("Auto-Market entries", "Per-item toggles and limits retain their existing save behavior.", $"{c.AutoMarketItems.Count} entries");
    AutoMarketItem? remove = null;
    var avail = MarketChrome.Avail();
    if (avail < 960)
    {
      DrawEntryCards(c, avail);
      return;
    }
    var flags = ImGuiTableFlags.None;
    var frozenHeight = MarketChrome.FrozenTableHeight(c.AutoMarketItems.Count);
    if (frozenHeight > 0)
      flags |= ImGuiTableFlags.ScrollY;
    if (MarketChrome.BeginTable("##autoMarketTable", 10, flags, new Vector2(avail, frozenHeight)))
    {
      if (frozenHeight > 0)
        ImGui.TableSetupScrollFreeze(0, 1);
      ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 44f);
      ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("Skip routing", ImGuiTableColumnFlags.WidthFixed, 84f);
      ImGui.TableSetupColumn("Stack", ImGuiTableColumnFlags.WidthFixed, 60f);
      ImGui.TableSetupColumn("Keep bags", ImGuiTableColumnFlags.WidthFixed, 66f);
      ImGui.TableSetupColumn("Keep ret.", ImGuiTableColumnFlags.WidthFixed, 62f);
      ImGui.TableSetupColumn("Max/ret.", ImGuiTableColumnFlags.WidthFixed, 60f);
      ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 100f);
      ImGui.TableSetupColumn("Fixed price", ImGuiTableColumnFlags.WidthFixed, 88f);
      ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 72f);
      MarketChrome.TableHeader(("On", false), ("Item", false), ("Skip routing", false), ("Stack", false), ("Keep bags", false), ("Keep ret.", false), ("Max/ret.", false), ("Source", false), ("Fixed price", false), ("", false));

      foreach (var entry in c.AutoMarketItems.OrderBy(e => ItemNameResolver.GetItemName(e.ItemId)).ThenBy(e => e.HQ).ToList())
      {
        ImGui.PushID(entry.Key);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var on = entry.Enabled;
        if (MarketChrome.Toggle("##on", ref on)) { entry.Enabled = on; c.Save(); }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        MarketChrome.CellText($"{ItemNameResolver.GetItemName(entry.ItemId)}{(entry.HQ ? " (HQ)" : "")}");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Item ID: {entry.ItemId}  max stack {ItemNameResolver.MaxStack(entry.ItemId)}");

        ImGui.TableNextColumn();
        var skipRouting = entry.ExcludeFromCategoryRouting;
        if (MarketChrome.Toggle("##skiprouting", ref skipRouting)) { entry.ExcludeFromCategoryRouting = skipRouting; c.Save(); }
        Tip("Keeps this item out of category routing. It still sells normally, from wherever it sits, on every retainer - unaffected by any category-routing rule above.");

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
        var srcIdx = entry.SourceOverride.HasValue ? (int)entry.SourceOverride.Value + 1 : 0;
        if (MarketChrome.Combo("##src", ref srcIdx, ["(global)", "Bags", "Retainer", "Both"], ImGui.GetContentRegionAvail().X))
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
        if (ImGui.Button("Remove"))
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

  /// <summary>Narrow layout of the entries table: one card per entry, the same edits with the same clamps.</summary>
  private static void DrawEntryCards(Configuration c, float avail)
  {
    const float pad = 12f;
    AutoMarketItem? remove = null;
    var line = MarketChrome.Control;
    var sub = MarketChrome.LineHeight(Theme.SecondaryScale);
    var inner = avail - (2 * pad);
    var half = (inner - 8) / 2f;
    foreach (var entry in c.AutoMarketItems.OrderBy(e => ItemNameResolver.GetItemName(e.ItemId)).ThenBy(e => e.HQ).ToList())
    {
      ImGui.PushID(entry.Key);
      var pos = ImGui.GetCursorScreenPos();
      var draw = ImGui.GetWindowDrawList();
      var name = $"{ItemNameResolver.GetItemName(entry.ItemId)}{(entry.HQ ? " (HQ)" : "")}";
      var left = pos.X + pad + 44 + 10;
      var nameLines = MarketChrome.WrapText(name, pos.X + pad + inner - left, 1f, 2);
      var nameH = MathF.Max(line, nameLines.Count * MarketChrome.LineHeight());
      var height = pad + nameH + (4 * (8 + line)) + pad;
      draw.AddRectFilled(pos, pos + new Vector2(avail, height), Theme.Pack(Theme.ShellBar), 8f);
      draw.AddRect(pos, pos + new Vector2(avail, height), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }), 8f);
      var x = pos.X + pad;
      var y = pos.Y + pad;

      ImGui.SetCursorScreenPos(new Vector2(x, y + ((nameH - line) / 2f)));
      var on = entry.Enabled;
      if (MarketChrome.Toggle("##on", ref on)) { entry.Enabled = on; c.Save(); }
      var textTop = y + ((nameH - (nameLines.Count * MarketChrome.LineHeight())) / 2f);
      for (var i = 0; i < nameLines.Count; i++)
        MarketChrome.DrawText(draw, new Vector2(left, textTop + (i * MarketChrome.LineHeight())), nameLines[i], Theme.TextPrimary);
      ImGui.SetCursorScreenPos(new Vector2(left, y));
      ImGui.Dummy(new Vector2(MathF.Max(1, pos.X + pad + inner - left), nameH));
      if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{name}\nItem ID: {entry.ItemId}  max stack {ItemNameResolver.MaxStack(entry.ItemId)}");

      void Label(float lx, float ly, string text) =>
        MarketChrome.DrawText(draw, new Vector2(lx, ly + ((line - sub) / 2f)), text, Theme.TextSecondary, Theme.SecondaryScale);
      const float labelW = 74f;
      const float rightW = 84f;

      // row 2: stack | keep in bags
      y += nameH + 8;
      Label(x, y, "Stack");
      ImGui.SetCursorScreenPos(new Vector2(x + labelW, y));
      ImGui.SetNextItemWidth(half - labelW);
      var stack = entry.StackSize;
      var listingCap = LazyMarketCompanion.AutoMarket.MarketListingCap.For((int)ItemNameResolver.MaxStack(entry.ItemId));
      if (ImGui.InputInt("##stack", ref stack, 0, 0)) { entry.StackSize = Math.Clamp(stack, 0, listingCap); c.Save(); }
      if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Units per listing. 0 = the market's maximum ({listingCap}). The market accepts at most {listingCap} of this item per listing; larger values are clamped.");
      var rx = x + half + 8;
      Label(rx, y, "Keep bags");
      ImGui.SetCursorScreenPos(new Vector2(rx + rightW, y));
      ImGui.SetNextItemWidth(half - rightW);
      var keepB = entry.KeepInBags;
      if (ImGui.InputInt("##keepb", ref keepB, 0, 0)) { entry.KeepInBags = Math.Max(keepB, 0); c.Save(); }
      if (ImGui.IsItemHovered()) ImGui.SetTooltip("Never sell below this many from your bags.");

      // row 3: keep on retainer | max per retainer
      y += line + 8;
      Label(x, y, "Keep ret.");
      ImGui.SetCursorScreenPos(new Vector2(x + labelW, y));
      ImGui.SetNextItemWidth(half - labelW);
      var keepR = entry.KeepInRetainer;
      if (ImGui.InputInt("##keepr", ref keepR, 0, 0)) { entry.KeepInRetainer = Math.Max(keepR, 0); c.Save(); }
      if (ImGui.IsItemHovered()) ImGui.SetTooltip("Never sell below this many from the retainer's own inventory.");
      Label(rx, y, "Max/ret.");
      ImGui.SetCursorScreenPos(new Vector2(rx + rightW, y));
      ImGui.SetNextItemWidth(half - rightW);
      var max = entry.MaxListingsPerRetainer;
      if (ImGui.InputInt("##max", ref max, 0, 0)) { entry.MaxListingsPerRetainer = Math.Max(max, 0); c.Save(); }
      if (ImGui.IsItemHovered()) ImGui.SetTooltip("Max listings of this item on one retainer at once (existing ones count). 0 = no cap.");

      // row 4: source | fixed price
      y += line + 8;
      Label(x, y, "Source");
      ImGui.SetCursorScreenPos(new Vector2(x + labelW, y));
      var srcIdx = entry.SourceOverride.HasValue ? (int)entry.SourceOverride.Value + 1 : 0;
      if (MarketChrome.Combo("##src", ref srcIdx, ["(global)", "Bags", "Retainer", "Both"], half - labelW))
      {
        entry.SourceOverride = srcIdx == 0 ? null : (StockSource)(srcIdx - 1);
        c.Save();
      }
      Label(rx, y, "Fixed price");
      ImGui.SetCursorScreenPos(new Vector2(rx + rightW, y));
      ImGui.SetNextItemWidth(half - rightW);
      var fixedPrice = entry.FixedPrice;
      if (ImGui.InputInt("##fixed", ref fixedPrice, 0, 0)) { entry.FixedPrice = Math.Max(fixedPrice, 0); c.Save(); }
      if (ImGui.IsItemHovered()) ImGui.SetTooltip("List at exactly this unit price instead of matching. 0 = match the board.");

      // row 5: skip routing switch and Remove
      y += line + 8;
      ImGui.SetCursorScreenPos(new Vector2(x, y));
      var skipRouting = entry.ExcludeFromCategoryRouting;
      if (MarketChrome.Toggle("##skiprouting", ref skipRouting)) { entry.ExcludeFromCategoryRouting = skipRouting; c.Save(); }
      Tip("Keeps this item out of category routing. It still sells normally, from wherever it sits, on every retainer - unaffected by any category-routing rule above.");
      Label(x + 44 + 10, y, "Skip routing");
      var removeW = MathF.Max(72, MarketChrome.ButtonWidth("Remove"));
      ImGui.SetCursorScreenPos(new Vector2(x + inner - removeW, y));
      if (ImGui.Button("Remove", new Vector2(removeW, line)))
        remove = entry;

      ImGui.SetCursorScreenPos(pos);
      ImGui.Dummy(new Vector2(avail, height + 8));
      ImGui.PopID();
    }

    if (remove != null)
    {
      c.AutoMarketItems.Remove(remove);
      c.Save();
    }
  }

  /// <summary>
  /// One row per ItemSearchCategory currently represented among the Auto-Market list - not every
  /// category in the game, per the frozen design (Configuration.cs remarks on CategoryRetainerRules).
  /// Each combo defaults to "(any retainer)" and upserts/removes a CategoryRetainerRule on change.
  /// </summary>
  private static void DrawCategoryRouting(Configuration c)
  {
    MarketChrome.CardHeader("Category routing", "Assign a whole market-board category to one retainer.");
    Tip("Assign a whole market-board category to one retainer. Auto-Market only lists an item of a routed category on the retainer it is assigned to - on every other retainer it is left exactly where it is, never vendored, never touched. Each sweep pulls misplaced routed stock out of the open retainer into the bags and deposits assigned bags stock into it, running the final deposit lap so pulled stock reaches its home retainer in the same sweep, boards full or not. The 'Skip routing' checkbox in the table below opts one item out entirely - it keeps selling normally, from wherever it sits, on every retainer.");

    var autoAssign = c.AutoAssignUnroutedCategories;
    if (MarketChrome.Toggle("Auto-assign uncovered categories", ref autoAssign))
    {
      c.AutoAssignUnroutedCategories = autoAssign;
      c.Save();
    }
    Tip("When a sweep finds marked Auto-Market stock in the bags whose market-board category has no rule above, it assigns that category to the sweep-enabled retainer carrying the fewest routed categories and moves the stock there in the same pass. The added row is an ordinary row - reassign or clear it above.");

    var categoryIds = c.AutoMarketItems
      .Select(e => ItemNameResolver.SearchCategoryId(e.ItemId))
      .Where(id => id != 0)
      .Distinct()
      .OrderBy(id => ItemNameResolver.GetSearchCategoryName(id))
      .ToList();

    if (categoryIds.Count == 0)
    {
      MarketChrome.Secondary("No marketable categories on your Auto-Market list yet.");
      return;
    }

    var retainerNames = c.LastKnownRetainerNames;
    foreach (var categoryId in categoryIds)
    {
      ImGui.PushID((int)categoryId + 900000);
      var rule = c.GetCategoryRetainerRule(categoryId);
      var options = new List<string> { "(any retainer)" };
      options.AddRange(retainerNames);
      var currentIndex = rule == null ? 0 : Math.Max(0, options.IndexOf(rule.RetainerName));

      if (ComboField(ItemNameResolver.GetSearchCategoryName(categoryId), "##catroute", ref currentIndex, [.. options]))
      {
        if (currentIndex == 0)
        {
          if (rule != null)
            c.CategoryRetainerRules.Remove(rule);
        }
        else
        {
          var chosen = options[currentIndex];
          if (rule == null)
            c.CategoryRetainerRules.Add(new CategoryRetainerRule { CategoryId = categoryId, RetainerName = chosen });
          else
            rule.RetainerName = chosen;
        }
        c.Save();
      }
      if (retainerNames.Count == 0)
        Tip("Open the retainer list in-game to populate retainer names for this combo.");

      ImGui.PopID();
    }
  }

  /// <summary>Plain-language place for a reconcile key's container (the key itself is unchanged).</summary>
  private static string FriendlyPlace(InventoryType type, int slot)
  {
    var name = type.ToString();
    string place;
    if (name.StartsWith("Inventory", StringComparison.Ordinal) && int.TryParse(name["Inventory".Length..], out var bag))
      place = $"bag page {bag}";
    else if (name.StartsWith("RetainerPage", StringComparison.Ordinal) && int.TryParse(name["RetainerPage".Length..], out var page))
      place = $"retainer inventory page {page}";
    else
      place = name;
    return $"{place}, slot {slot}";
  }

  /// <summary>
  /// 0.1.53.0: the reconcile-ledger quarantine, visible. A stack an earlier sweep moved OK that
  /// was still sitting in place afterwards stays put instead of re-moving every sweep; this list
  /// shows what is held, since when, and why, plus the retry policy. Read-only - entries leave on
  /// their own when the stack lands or moves on, and each held stack re-plans once 24 hours after
  /// its last move (a retry that still does not stick goes back in with a fresh stamp).
  /// 0.1.55.0: the list is far shorter in practice - a slot the mover refilled itself is released
  /// instead of held, which was the source of almost every entry this list used to show.
  /// 0.1.58.0: each row, and the list as a whole, can be released by hand. A hold whose cause was
  /// transient would otherwise sit out its full 24-hour window with the stack visibly not moving.
  /// Releasing drops the skip entry only - nothing is moved from here, and a stack the game
  /// refuses again is back in this list after the next sweep.
  /// </summary>
  private void DrawQuarantinedMoves()
  {
    var snapshot = ReconcileLedgerSource?.Invoke() ?? [];
    if (snapshot.Count == 0)
    {
      MarketChrome.CardHeader("Quarantined moves", "Nothing quarantined. A move the server silently refuses appears here instead of re-firing every sweep.");
      Tip(QuarantineTip);
      return;
    }

    MarketChrome.CardHeader("Quarantined moves", $"{snapshot.Count} {(snapshot.Count == 1 ? "stack" : "stacks")} held. Each retries once 24 hours after its last move.");
    Tip(QuarantineTip);

    var now = DateTime.UtcNow;
    // 0.1.58.0: releasing is deferred to after the list is drawn - removing an entry mid-frame
    // would resize the rows being drawn.
    string? release = null;
    var releaseAll = false;
    if (ImGui.Button("Retry all now##quarantinedRetryAll"))
      releaseAll = true;
    Tip("Drops every hold so the next Auto-Market sweep plans those stacks again. Nothing is moved here - a move the game refuses again goes straight back into this list.");
    MarketChrome.RowsDone();
    ImGui.Dummy(new Vector2(0, 4));

    foreach (var (key, recordedAt) in snapshot)
    {
      var age = now - recordedAt.ToUniversalTime();
      if (age < TimeSpan.Zero)
        age = TimeSpan.Zero;
      var ageText = age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago" : $"{(int)age.TotalMinutes}m ago";
      var heldSince = $"held since {recordedAt.ToLocalTime():yyyy-MM-dd HH:mm} ({ageText})";
      string title;
      string sub;
      if (!RoutingMove.TryParseReconcileKey(key, out var session, out var container, out var slot, out var itemId, out var hq))
      {
        title = key;
        sub = heldSince;
      }
      else
      {
        title = $"{ItemNameResolver.GetItemName(itemId)}{(hq ? " (HQ)" : "")}";
        sub = $"{session}, {FriendlyPlace((InventoryType)container, slot)} · {heldSince}";
      }
      if (MarketChrome.ActionRow(title, sub, $"Retry now##quarantinedRetry{key}"))
        release = key;
    }
    if (releaseAll)
      ReconcileLedgerReleaseAll?.Invoke();
    else if (release != null)
      ReconcileLedgerRelease?.Invoke(release);
  }

  private const string QuarantineTip = "Stacks an earlier sweep moved successfully that were still sitting in the same slot afterwards - the move did not stick server-side, so each sweep leaves them put instead of re-moving them. A slot Auto-Market refilled itself no longer counts as a refused move, so a stack relayed through the same slot is never held here. Anything that is held retries automatically, once, 24 hours after the last move.";

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

    MarketChrome.Columns("pricematching",
      () =>
      {
        MarketChrome.Card("pricing_rules", () =>
        {
          MarketChrome.CardHeader("Pricing rules", $"Current mode: {c.UndercutMode}. Matching and fallback behavior is unchanged.");

          var hq = c.HQ;
          if (MarketChrome.Toggle("Use HQ price", ref hq)) { c.HQ = hq; c.Save(); }
          Tip("If checked, will use the hq price (if item is hq; will fail if there is no HQ price on the MB)");

          var enumValues = Enum.GetNames<UndercutMode>();
          var index = Array.IndexOf(enumValues, c.UndercutMode.ToString());
          if (ComboField("Price Mode", "##undercutModeCombo", ref index, enumValues))
          {
            var value = Enum.Parse<UndercutMode>(enumValues[index]);
            if (value == UndercutMode.Percentage && c.UndercutAmount >= 100)
              c.UndercutAmount = 1;
            c.UndercutMode = value;
            c.Save();
          }
          Tip("Defines whether to match by a fixed Gil amount or use a percentage (0 = exact match)");

          int amount = c.UndercutAmount;
          bool amountChanged;
          if (c.UndercutMode == UndercutMode.FixedAmount)
            amountChanged = MarketChrome.Field("Match amount (0 = exact match)", width => MarketChrome.IntInput("##undercutAmountFixed", ref amount, width), "gil");
          else
            amountChanged = MarketChrome.Field("Match amount (0 = exact match)", width => MarketChrome.SliderInt("##undercutAmountPercentage", ref amount, 1, 99, width, " %"));
          if (amountChanged)
          {
            c.UndercutAmount = c.UndercutMode == UndercutMode.FixedAmount ? Math.Clamp(amount, 0, int.MaxValue) : amount;
            c.Save();
          }
          Tip("Amount below the lowest listing. 0 = list at exactly the lowest price.");

          float maxUndercut = c.MaxUndercutPercentage;
          if (MarketChrome.Field("Max price change percentage", width => MarketChrome.SliderFloat("##maximumUndercutAmountPercentage", ref maxUndercut, 0.1f, 99.9f, width, "0.0", " %")))
          { c.MaxUndercutPercentage = MathF.Round(maxUndercut, 1); c.Save(); }
          Tip("Existing listings are never cut by more than this. Fresh Auto-Market listings (placeholder price) are exempt.");

          var undercutSelf = c.UndercutSelf;
          if (MarketChrome.Toggle("Match Self", ref undercutSelf)) { c.UndercutSelf = undercutSelf; c.Save(); }
          Tip("If checked, your own retainer listings will be matched (instead of skipped)");

          var useUniversalis = c.UseUniversalisDataCenterPrices;
          if (MarketChrome.Toggle("Use Universalis data center prices", ref useUniversalis)) { c.UseUniversalisDataCenterPrices = useUniversalis; c.Save(); }
          Tip("If checked, price checks use the cheapest listing on your current data center from Universalis instead of the in-game Compare Prices window.");

          var saleHistory = c.UseUniversalisSaleHistoryFallback;
          if (MarketChrome.Toggle("Price from recent sales when nothing is on the board", ref saleHistory)) { c.UseUniversalisSaleHistoryFallback = saleHistory; c.Save(); }
          Tip("When a price check finds NOTHING listed on your data center, list at the median of the recent sales from Universalis instead of leaving the listing at the placeholder price.\nThe median, not the average: Universalis' own average is skewed by outliers (one item measured 1,824,207 average against a 53,550 median).");

          if (c.UseUniversalisSaleHistoryFallback)
          {
            int maxAge = c.SaleHistoryMaxAgeDays;
            MarketChrome.Indented(() =>
            {
              if (MarketChrome.Field("Ignore sales older than", width => MarketChrome.IntInput("##saleHistoryMaxAge", ref maxAge, width), "days"))
              {
                c.SaleHistoryMaxAgeDays = Math.Clamp(maxAge, SaleHistoryPricing.MinMaxAgeDays, SaleHistoryPricing.MaxMaxAgeDays);
                c.Save();
              }
              Tip("If the newest sale is older than this, no price is invented: the listing keeps the placeholder price and you get the usual chat message. Some items last sold years ago.");
            });
          }
        });

        MarketChrome.Card("timing", () =>
        {
          MarketChrome.CardHeader("Timing", "How long the market board is given to answer.");
          int mbDelay = c.GetMBPricesDelayMS;
          if (MarketChrome.Field("Market Board Price Check Delay", width => MarketChrome.SliderInt("###sliderMBDelay", ref mbDelay, 1, 10000, width, " ms"))) { c.GetMBPricesDelayMS = mbDelay; c.Save(); }
          Tip("Delay before opening the market board price list. Recommended 3000-4000ms.");

          int keepOpen = c.MarketBoardKeepOpenMS;
          if (MarketChrome.Field("Market Board Keep Open Time", width => MarketChrome.SliderInt("###sliderMBKeepOpen", ref keepOpen, 1, 10000, width, " ms"))) { c.MarketBoardKeepOpenMS = keepOpen; c.Save(); }
          Tip("Time to keep the market board open when fetching prices. Recommended 1000-2000ms.");
        });

        MarketChrome.Card("preflight", () =>
        {
          MarketChrome.CardHeader("Auto Pinch pre-flight", "Which listings Auto Pinch opens.");
          var preflight = c.AutoPinchPreflightEnabled;
          if (MarketChrome.Toggle("Only re-price listings AllaganMarket has flagged", ref preflight)) { c.AutoPinchPreflightEnabled = preflight; c.Save(); }
          Tip("Auto Pinch opens only the listings AllaganMarket marks undercut (red) or stale (yellow) on the retainer sell list, plus any still at the 999,999,999 placeholder price. Everything AllaganMarket does not flag is never touched - including items it has never checked, even once.\n\nThis needs AllaganMarket installed and its own prices working: the plugin reads AllaganMarket's saved price data (no IPC exists), so if AllaganMarket is missing or has nothing cached, nothing but placeholder-priced listings gets re-priced. Untick for the old walk-everything behaviour.");
        }, fill: true);
      },
      () =>
      {
        MarketChrome.Card("messages", () =>
        {
          MarketChrome.CardHeader("Messages & display", "Chat, context-menu, cache, and diagnostic settings.");

          bool chatErrors = c.ShowErrorsInChat;
          if (MarketChrome.Toggle("Show errors in chat", ref chatErrors)) { c.ShowErrorsInChat = chatErrors; c.Save(); }

          bool adjustments = c.ShowPriceAdjustmentsMessages;
          if (MarketChrome.Toggle("Show price adjustments", ref adjustments)) { c.ShowPriceAdjustmentsMessages = adjustments; c.Save(); }

          bool retainerNames = c.ShowRetainerNames;
          if (MarketChrome.Toggle("Show retainer names", ref retainerNames)) { c.ShowRetainerNames = retainerNames; c.Save(); }

          var showCtx = c.ShowInventoryContextMenuEntry;
          if (MarketChrome.Toggle("Show inventory context menu entries", ref showCtx)) { c.ShowInventoryContextMenuEntry = showCtx; c.Save(); }
          Tip("Adds 'Add to Auto-Market' and 'Add price limits' to inventory item right-click menus.");

          int defaultAmount = c.DefaultAmount;
          if (MarketChrome.Field("Default amount", width => MarketChrome.IntInput("##defaultAmount", ref defaultAmount, width), "gil"))
          { c.DefaultAmount = Math.Clamp(defaultAmount, 0, int.MaxValue); c.Save(); }
          Tip("Price to use when no board price can be found (0 = leave the listing alone)");

          MarketChrome.RowsDone();
          ImGui.Dummy(new Vector2(0, 8));
          if (ImGui.Button("Clear retainer cache")) { c.SeenRetainers.Clear(); c.Save(); }
          Tip("Clears the list of your own retainers (used to avoid undercutting yourself).");

          if (c.ImportedFromDagobert)
          {
            ImGui.Dummy(new Vector2(0, 8));
            MarketChrome.Notice("Imported · Settings were imported from Dagobert Price Matcher. You can uninstall that plugin.", UiState.Idle);
          }
        });

        MarketChrome.Card("diagnostics", () =>
        {
          MarketChrome.CardHeader("Advanced / Diagnostics", "Local-only price-decision logging; no pricing behavior changes.");
          var telemetry = c.DecisionTelemetry;
          if (MarketChrome.Toggle("Log price decisions", ref telemetry)) { c.DecisionTelemetry = telemetry; c.Save(); }
          Tip("Off by default. When on, Lazy Market Companion writes a diagnostic line to the Dalamud\n" +
              "plugin log for every price decision it makes - both the prices it sets and the ones it\n" +
              "refuses to set because they would undercut by more than your maximum. Each line carries\n" +
              "the item, quantity, old price, new price, where the price came from and the percentage\n" +
              "change. Lines start with \"" + MarketTelemetry.Prefix + "\".\n\n" +
              "It changes no pricing behaviour and sends nothing anywhere - the lines only go to your\n" +
              "own local plugin log, so you can check afterwards whether matching the board actually\n" +
              "earned more than the fallback price did.\n\n" +
              "Same as /lmc telemetry on|off.");
          MarketChrome.RowsDone();
          ImGui.Dummy(new Vector2(0, 4));
          MarketChrome.Secondary($"Writes \"{MarketTelemetry.Prefix}\" lines to the plugin log. Nothing leaves this computer.");
        }, fill: true);
      });
  }

  // =====================================================================================
  // Min/Max tab (inherited)
  // =====================================================================================

  private static void DrawItemPriceLimits()
  {
    MarketChrome.Card("price_limits", () =>
    {
      MarketChrome.CardHeader("Saved price limits", "Minimum and maximum unit prices per item. 0 = no limit.", $"{Plugin.Configuration.ItemPriceLimits.Count} limits");

      if (Plugin.Configuration.ItemPriceLimits.Count == 0)
      {
        MarketChrome.Notice("Empty · right-click an inventory item and choose 'Add price limits'.", UiState.Empty);
        return;
      }

      ItemPriceLimit? limitToRemove = null;
      if (MarketChrome.BeginTable("##itemPriceLimitsTable", 4, ImGuiTableFlags.None, new Vector2(MarketChrome.Avail(), 0)))
      {
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Min", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("Max", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 96f);
        MarketChrome.TableHeader(("Item", false), ("Min", false), ("Max", false), ("", false));

        foreach (var limit in Plugin.Configuration.ItemPriceLimits
              .OrderBy(limit => ItemNameResolver.GetItemName(limit.ItemId))
              .ThenBy(limit => limit.ItemId)
              .ToList())
        {
          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          ImGui.AlignTextToFramePadding();
          MarketChrome.CellText(ItemNameResolver.GetItemName(limit.ItemId));
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
          if (ImGui.Button($"Remove##itemPriceLimitRemove{limit.ItemId}"))
            limitToRemove = limit;
        }

        ImGui.EndTable();
      }

      if (limitToRemove != null)
      {
        Plugin.Configuration.ItemPriceLimits.Remove(limitToRemove);
        Plugin.Configuration.Save();
      }
    });
  }

  // =====================================================================================
  // Retainers + hotkeys tab (inherited)
  // =====================================================================================

  private static void DrawRetainersAndHotkeys()
  {
    var c = Plugin.Configuration;

    MarketChrome.Card("retainers", () =>
    {
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
        var cached = namesToDisplay.Length > 0 && retainerNameArray == null && !namesUpdated;
        MarketChrome.CardHeader("Retainer selection", cached
          ? "Unchecked retainers are skipped by sweeps and AutoRetainer. Showing the cached list; open the retainer list in-game to refresh it."
          : "Unchecked retainers are skipped by sweeps and AutoRetainer. Open the retainer list to refresh names.");
        Tip("Unchecked retainers are skipped by the sweep buttons and never claimed from AutoRetainer.\r\nOpen the retainer list in-game to refresh names.");

        if (namesToDisplay.Length > 0)
        {
          var columns = MarketChrome.Avail() >= 560 ? 2 : 1;
          MarketChrome.Grid(namesToDisplay.Length, columns, i =>
          {
            string retainerName = namesToDisplay[i];
            bool allDisabled = c.EnabledRetainerNames.Contains(Configuration.ALL_DISABLED_SENTINEL);
            bool enabled = !allDisabled && (c.EnabledRetainerNames.Count == 0 || c.EnabledRetainerNames.Contains(retainerName));

            if (MarketChrome.Toggle($"{retainerName}##retainer{i}", ref enabled))
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
          });
        }
        else
        {
          MarketChrome.Notice("Blocked · Open the retainer list in-game to configure retainer selection.", UiState.Blocked);
        }
      }
    });

    MarketChrome.Card("hotkeys", () =>
    {
      MarketChrome.CardHeader("Hotkeys", "Keyboard actions for posting and pinching.");
      bool enablePostPinchKey = c.EnablePostPinchkey;
      var postKey = Array.IndexOf(_virtualKeyStrings, c.PostPinchKey.ToString());
      if (MarketChrome.ToggleWith("Enable Post Pinch Hotkey", ref enablePostPinchKey, enablePostPinchKey ? 150 : 0,
            enablePostPinchKey ? width => MarketChrome.Combo("##postPinchKeyCombo", ref postKey, _virtualKeyStrings, width) : null))
      {
        c.EnablePostPinchkey = enablePostPinchKey;
        c.Save();
      }
      Tip("Hold this key while posting an item manually to auto-fill the matched price.");
      if (enablePostPinchKey && postKey >= 0 && _virtualKeyStrings[postKey] != c.PostPinchKey.ToString())
      { c.PostPinchKey = Enum.Parse<VirtualKey>(_virtualKeyStrings[postKey]); c.Save(); }

      bool enablePinchKey = c.EnablePinchKey;
      var pinchKey = Array.IndexOf(_virtualKeyStrings, c.PinchKey.ToString());
      if (MarketChrome.ToggleWith("Enable Pinch Hotkey", ref enablePinchKey, enablePinchKey ? 150 : 0,
            enablePinchKey ? width => MarketChrome.Combo("##pinchKeyCombo", ref pinchKey, _virtualKeyStrings, width) : null))
      {
        c.EnablePinchKey = enablePinchKey;
        c.Save();
      }
      Tip("Press this key while the retainer list / sell list is open to start the sweep.");
      if (enablePinchKey && pinchKey >= 0 && _virtualKeyStrings[pinchKey] != c.PinchKey.ToString())
      { c.PinchKey = Enum.Parse<VirtualKey>(_virtualKeyStrings[pinchKey]); c.Save(); }
    });

    // Issue #12: the overlay buttons sit top-right on the retainer windows and can cover the
    // venture currency and the close button, so each one can be hidden here.
    MarketChrome.Card("overlay_buttons", () =>
    {
      MarketChrome.CardHeader("Retainer window buttons", "Hide any overlay action that covers game UI. Hidden actions remain available through the existing commands.");

      var showPinch = c.ShowAutoPinchOverlayButton;
      if (MarketChrome.Toggle("Show 'Auto Pinch'", ref showPinch)) { c.ShowAutoPinchOverlayButton = showPinch; c.Save(); }

      var showAutoMarket = c.ShowAutoMarketOverlayButton;
      if (MarketChrome.Toggle("Show 'Auto Market'", ref showAutoMarket)) { c.ShowAutoMarketOverlayButton = showAutoMarket; c.Save(); }

      var showSweep = c.ShowSweepToBagsOverlayButton;
      if (MarketChrome.Toggle("Show 'Sweep to Bags'", ref showSweep)) { c.ShowSweepToBagsOverlayButton = showSweep; c.Save(); }
    });

    MarketChrome.Card("support", () =>
    {
      MarketChrome.CardHeader("Support", "Write the same local problem report as the existing report command.");
      Plugin.Telemetry?.DrawReportButton();
    });
  }

  private static void Tip(string text)
  {
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip(text);
  }

  private static void TipAlways(string text)
  {
    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
      ImGui.SetTooltip(text);
  }
}
