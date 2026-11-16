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
  private sealed record MarketPage(PageId Id, string Group, string Title, string Description, string Glyph);

  private static readonly MarketPage[] Pages =
  [
    new(PageId.AutoMarket, "Market", "Auto-Market", "Always-sell items, stock rules, listing limits, and category routing.", "◆"),
    new(PageId.PriceMatching, "Market", "Price Matching", "Board matching, fallbacks, timing, messages, and diagnostics.", "◇"),
    new(PageId.PriceLimits, "Market", "Min/Max Prices", "Per-item minimum and maximum unit prices applied to matched prices.", "↔"),
    new(PageId.Inventory, "Inventory", "Inventory", "Space at a glance, venture loot, ownership, and the gear mover.", "▤"),
    new(PageId.Setup, "Setup", "Retainers & Hotkeys", "Retainer selection, keyboard actions, overlay buttons, and support.", "⚙"),
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

  private void DrawFrame()
  {
    if (_forceTab == Tab.AutoMarket)
      _currentPage = PageId.AutoMarket;
    else if (_forceTab == Tab.PriceLimits)
      _currentPage = PageId.PriceLimits;

    DrawShellBar();
    DrawFeatureRow();
    MarketChrome.PageHeader(CurrentPage.Title, CurrentPage.Description, LiveCount(), CurrentPage.Glyph);

    var busy = AutomationBusy?.Invoke() ?? false;
    var footerHeight = MarketChrome.FooterHeight(ImGui.GetContentRegionAvail().X, busy);
    if (ImGui.BeginChild("##lmcBody", new Vector2(0, MathF.Max(80, ImGui.GetContentRegionAvail().Y - footerHeight)), false))
    {
      switch (_currentPage)
      {
        case PageId.AutoMarket: DrawAutoMarket(); break;
        case PageId.Inventory:
          if (DrawInventoryTab is not null)
            DrawInventoryTab();
          else
            Widgets.InfoBox("Needs setup · inventory services are unavailable.", UiState.NeedsSetup);
          break;
        case PageId.PriceMatching: DrawGeneralConfig(); break;
        case PageId.PriceLimits: DrawItemPriceLimits(); break;
        case PageId.Setup: DrawRetainersAndHotkeys(); break;
      }
    }
    ImGui.EndChild();
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

  private void DrawShellBar()
  {
    var width = ImGui.GetContentRegionAvail().X;
    var origin = ImGui.GetCursorScreenPos();
    var draw = ImGui.GetWindowDrawList();
    var gap = 2 * Theme.Unit;
    var groups = Pages.Select(page => page.Group).Distinct().ToArray();
    var brandSize = new Vector2(MathF.Max(244, (ImGui.CalcTextSize("Lazy Market Companion").X * Theme.TitleScale) + 68), 42);
    var groupSizes = groups.Select(group => new Vector2(MathF.Max(82, ImGui.CalcTextSize(group).X + 34), 34)).ToArray();
    var busy = AutomationBusy?.Invoke() ?? false;
    var state = busy ? UiState.Running : !Plugin.Configuration.AutoMarketEnabled ? UiState.Idle : Plugin.Configuration.AutoMarketItems.Count == 0 ? UiState.Empty : UiState.Ready;
    var statusLabel = busy ? LalaUiKit.Copy.Word(state) : $"{LalaUiKit.Copy.Word(state)} · {Plugin.Configuration.AutoMarketItems.Count} items";
    var statusSize = new Vector2(MathF.Max(94, ImGui.CalcTextSize(statusLabel).X + 43), 34);
    var closeSize = new Vector2(34);
    var leftWidth = brandSize.X + gap + groupSizes.Sum(size => size.X) + ((groupSizes.Length - 1) * gap);
    var singleRow = width >= leftWidth + statusSize.X + closeSize.X + (3 * gap);
    var positions = new Vector2[groups.Length];
    var brandPos = new Vector2(0, singleRow ? 12 : 10);
    float barHeight;
    Vector2 statusPos;
    Vector2 closePos;

    if (singleRow)
    {
      barHeight = 66;
      var x = brandSize.X + gap;
      for (var i = 0; i < groups.Length; i++)
      {
        positions[i] = new Vector2(x, 16);
        x += groupSizes[i].X + gap;
      }
      statusPos = new Vector2(width - statusSize.X - closeSize.X - gap, 16);
      closePos = new Vector2(width - closeSize.X, 16);
    }
    else
    {
      var x = 0f;
      var y = 62f;
      for (var i = 0; i < groups.Length; i++)
      {
        if (x > 0 && x + groupSizes[i].X > width)
        {
          x = 0;
          y += 42;
        }
        positions[i] = new Vector2(x, y);
        x += groupSizes[i].X + gap;
      }
      statusPos = new Vector2(width - statusSize.X - closeSize.X - gap, 14);
      closePos = new Vector2(width - closeSize.X, 14);
      barHeight = y + 42;
    }

    draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
    draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
    if (MarketChrome.BrandButton(draw, origin + brandPos, brandSize))
      _currentPage = PageId.AutoMarket;
    for (var i = 0; i < groups.Length; i++)
    {
      var group = groups[i];
      if (MarketChrome.OutlineButton(draw, "group_" + group, group, origin + positions[i], groupSizes[i], CurrentPage.Group == group))
        _currentPage = Pages.First(page => page.Group == group).Id;
    }
    MarketChrome.StatusPill(draw, origin + statusPos, statusSize, statusLabel, state);
    if (MarketChrome.OutlineButton(draw, "close_window", "×", origin + closePos, closeSize))
      IsOpen = false;
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip("Close");
    ImGui.SetCursorScreenPos(origin);
    ImGui.Dummy(new Vector2(width, barHeight));
  }

  private void DrawFeatureRow()
  {
    var width = ImGui.GetContentRegionAvail().X;
    var origin = ImGui.GetCursorScreenPos();
    var draw = ImGui.GetWindowDrawList();
    var gap = 3 * Theme.Unit;
    var features = Pages.Where(page => page.Group == CurrentPage.Group).ToArray();
    var sizes = features.Select(page => new Vector2(MathF.Min(width, MathF.Max(132, ImGui.CalcTextSize(page.Title).X + 52)), 34)).ToArray();
    var positions = new Vector2[features.Length];
    float x = 0;
    float y = 8;
    for (var i = 0; i < features.Length; i++)
    {
      if (x > 0 && x + sizes[i].X > width)
      {
        x = 0;
        y += 42;
      }
      positions[i] = new Vector2(x, y);
      x += sizes[i].X + gap;
    }
    var barHeight = y + 42;
    draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
    draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
    for (var i = 0; i < features.Length; i++)
      if (MarketChrome.FeatureButton(draw, features[i].Id.ToString(), features[i].Title, origin + positions[i], sizes[i], _currentPage == features[i].Id))
        _currentPage = features[i].Id;
    ImGui.SetCursorScreenPos(origin);
    ImGui.Dummy(new Vector2(width, barHeight));
  }

  private void DrawFooter()
  {
    var busy = AutomationBusy?.Invoke() ?? false;
    var state = busy ? UiState.Running : !Plugin.Configuration.AutoMarketEnabled ? UiState.Idle : Plugin.Configuration.AutoMarketItems.Count == 0 ? UiState.Empty : UiState.Ready;
    var action = busy ? "the current market action" : !Plugin.Configuration.AutoMarketEnabled ? "Auto-Market is off" : Plugin.Configuration.AutoMarketItems.Count == 0 ? "no Auto-Market entries" : "watching the Auto-Market list";
    var detail = busy ? "Stop requests cancellation of the current action." : Plugin.Configuration.AutoMarketEnabled && Plugin.Configuration.AutoMarketItems.Count == 0 ? "Add an item from the Auto-Market page or inventory context menu." : $"{Plugin.Configuration.AutoMarketItems.Count} configured entries · {Plugin.Configuration.ItemPriceLimits.Count} price limits";
    MarketChrome.Footer(state, action, detail, busy ? "Stop" : null, busy ? () => CancelAutomation?.Invoke("cancelled from the config window") : null);
  }

  // =====================================================================================
  // Auto-Market tab
  // =====================================================================================

  private void DrawAutoMarket()
  {
    var c = Plugin.Configuration;

    MarketChrome.StatCards(
      new MarketChrome.Stat("Auto-Market list", c.AutoMarketItems.Count.ToString(), "Configured item and quality entries"),
      new MarketChrome.Stat("Enabled entries", c.AutoMarketItems.Count(entry => entry.Enabled).ToString(), "Eligible for the next listing pass"),
      new MarketChrome.Stat("Category routes", c.CategoryRetainerRules.Count.ToString(), "Explicit retainer assignments"));
    MarketChrome.Section("Automation & stock", "Existing switches and limits continue to write the same saved settings.");

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
    Tip(AutoRetainerIPC.Installed
      ? "When AutoRetainer cycles a retainer (multi-mode / ventures), claim it after AR's own work, list your items, match prices, hand it back.\r\nOnly enabled retainers (Retainers tab) are claimed."
      : "AutoRetainer is not installed / loaded.");

    var inSweep = c.AutoMarketInPinchAllSweep;
    if (MarketChrome.Toggle("Include in 'Auto Market' sweep", ref inSweep)) { c.AutoMarketInPinchAllSweep = inSweep; c.Save(); }
    Tip("The 'Auto Market' button on the retainer list lists items on every enabled retainer before pinching. Off = that button only pinches.");

    ImGui.Separator();

    ImGui.TextUnformatted("Stock source:"); ImGui.NewLine();
    ImGui.SetNextItemWidth(180);
    var src = (int)c.AutoMarketSource;
    if (ImGui.Combo("##source", ref src, ["Bags only", "Retainer inventory only", "Bags + retainer inventory"], 3)) { c.AutoMarketSource = (StockSource)src; c.Save(); }
    Tip("Where items may be taken from. Per-item override available in the table.");

    var retFirst = c.AutoMarketPreferRetainerStockFirst;
    if (MarketChrome.Toggle("Retainer stock first", ref retFirst)) { c.AutoMarketPreferRetainerStockFirst = retFirst; c.Save(); }
    Tip("Sell the retainer's own inventory (venture loot) before touching your bags.");

    var partial = c.AutoMarketListPartialStacks;
    if (MarketChrome.Toggle("List partial stacks", ref partial)) { c.AutoMarketListPartialStacks = partial; c.Save(); }
    Tip("If there isn't a full listing's worth left, list what's there anyway. Off = only full-size listings.");

    ImGui.TextUnformatted("New listing price:"); ImGui.NewLine();
    ImGui.SetNextItemWidth(260);
    var pm = (int)c.AutoMarketPriceMode;
    if (ImGui.Combo("##pricemode", ref pm, ["Placeholder, then match on the board", "Universalis first (fallback: placeholder)"], 2)) { c.AutoMarketPriceMode = (NewListingPriceMode)pm; c.Save(); }
    Tip("Placeholder: list at an absurd price, then immediately run the normal price match on that slot (Compare Prices).\r\n" +
        "Universalis first: ask Universalis for the data-center low before listing, so the item goes up already priced. Falls back to placeholder when Universalis has nothing.");

    ImGui.TextUnformatted("Reserve slots:"); ImGui.NewLine();
    ImGui.SetNextItemWidth(80);
    var reserve = c.AutoMarketReserveSlots;
    if (ImGui.InputInt("##reserve", ref reserve)) { c.AutoMarketReserveSlots = Math.Clamp(reserve, 0, 19); c.Save(); }
    Tip("Leave this many of the retainer's 20 market slots empty for manual listings.");

    var pinchAll = c.AutoMarketPinchAllAfter;
    if (MarketChrome.Toggle("Pinch everything after listing", ref pinchAll)) { c.AutoMarketPinchAllAfter = pinchAll; c.Save(); }
    Tip("Off (the default): after listing, only the new listings are priced - much faster.\nOn: re-price ALL of this retainer's listings as well (same as Auto Pinch), which costs a few seconds per existing listing.");

    var msgs = c.ShowAutoMarketMessages;
    if (MarketChrome.Toggle("Chat messages", ref msgs)) { c.ShowAutoMarketMessages = msgs; c.Save(); }

    if (!c.AutoMarketPinchAllAfter)
    {
      ImGui.TextUnformatted("If a new listing can't be found:"); ImGui.NewLine();
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

    // ---- value gate + listing order (0.1.11.0) ----
    ImGui.TextUnformatted("Listing order:"); ImGui.NewLine();
    ImGui.SetNextItemWidth(220);
    var sortIdx = (int)c.AutoMarketSortMode;
    if (ImGui.Combo("##sortmode", ref sortIdx, ["List order", "Cheapest first", "Fastest selling first", "Most expensive first"], 4))
    { c.AutoMarketSortMode = (MarketSortMode)sortIdx; c.Save(); }
    Tip("When the retainer does not have enough free market slots for everything on your Auto-Market list, this decides which items get the slots first.\r\n"
        + "Fastest selling first (the default): one Universalis lookup ranks your items by how many sell per day, so the slots go to what actually moves.\r\n"
        + "Cheapest / most expensive first: same lookup, ranked by current board price.\r\n"
        + "List order: the order of the table below - how it worked before.\r\n"
        + "Items Universalis has no fresh data for keep their list position and sort last, and a failed lookup changes nothing.");

    var gate = c.AutoMarketValueGateEnabled;
    if (MarketChrome.Toggle("Only list items worth more than", ref gate)) { c.AutoMarketValueGateEnabled = gate; c.Save(); }
    ImGui.NewLine();
    ImGui.SetNextItemWidth(140);
    var threshold = (int)Math.Min(c.AutoMarketValueGateThresholdGil, int.MaxValue);
    if (ImGui.InputInt("##gatethreshold", ref threshold, 0, 0)) { c.AutoMarketValueGateThresholdGil = Math.Max(threshold, 0); c.Save(); }
    ImGui.NewLine();
    ImGui.TextUnformatted("gil, net of fees");
    Tip("Before listing, Auto-Market checks every enabled item against current Universalis prices. Items whose total sellable value "
        + "(current board price x everything it could sell of that item, after the market's 5% fee) is at or under this number take the vendor leg instead of a market slot - only NQ non-gear items the Item sheet prices as vendorable; the retainer sells them to a vendor in the same session.\r\n"
        + "The value they will fetch is the vendor price (the same number the sell window autofills), which is usually far below the market board price - if in doubt, set the threshold lower.\r\n"
        + "Anything Universalis is unsure about is still LISTED (0.2.8.0): no data, data older than the freshness window below, or no listing of the right quality. The listing is priced by the normal pricing pass (the live board through Auto Pinch by default), and it is never vendored or pulled on an unconfirmed price - only a fresh price from your own world can do that, never a data-center price. With \"Use Universalis data center prices\" on, every price is a data-center price, so nothing is vendored or pulled at all. Vendoring an item on a guess cannot be undone; listing it can.\r\n"
        + "0 = the switch does nothing.");

    if (c.AutoMarketValueGateEnabled)
    {
      int gateFresh = c.AutoMarketGateFreshnessHours;
      ImGui.BeginGroup();
      ImGui.Text("Only trust gate prices newer than");
      ImGui.NewLine();
      ImGui.SetNextItemWidth(120);
      if (ImGui.SliderInt("###sliderGateFreshness", ref gateFresh, 1, 168)) { c.AutoMarketGateFreshnessHours = Math.Clamp(gateFresh, 1, 168); c.Save(); }
      ImGui.NewLine();
      ImGui.Text("hours");
      ImGui.EndGroup();
      Tip("Universalis is crowd-sourced and lags. Prices older than this never hold an item back - the item is listed normally. Same idea as the Auto Pinch pre-flight freshness window.");
    }

    ImGui.Separator();

    // ---- category routing ----
    MarketChrome.Card("category_routing", () => DrawCategoryRouting(c));

    // ---- quarantined moves (0.1.53.0) ----
    MarketChrome.Card("quarantined_moves", DrawQuarantinedMoves);

    // ---- add item ----
    MarketChrome.Section("Add item", "Search the marketable item sheet, or use the existing inventory context menu.");
    ImGui.SetNextItemWidth(MathF.Min(420, ImGui.GetContentRegionAvail().X));
    ImGui.InputTextWithHint("##search", "type an item name (or right-click an item in your bags)", ref _search, 64);
    MarketChrome.Toggle("HQ##addhq", ref _searchHq);
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
      Widgets.InfoBox("Empty · search above, or right-click an inventory item and choose 'Add to Auto-Market'.", UiState.Empty);
      return;
    }

    MarketChrome.Section("Auto-Market entries", "Per-item toggles and limits retain their existing save behavior.", $"{c.AutoMarketItems.Count} entries");
    AutoMarketItem? remove = null;
    var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX;
    var tableHeight = MathF.Max(240, MathF.Min(420, ImGui.GetContentRegionAvail().Y));
    if (ImGui.BeginTable("##autoMarketTable", 10, flags, new Vector2(-1, tableHeight)))
    {
      ImGui.TableSetupScrollFreeze(0, 1);
      ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 46f);
      ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("Skip routing", ImGuiTableColumnFlags.WidthFixed, 80f);
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
        if (MarketChrome.Toggle("##on", ref on)) { entry.Enabled = on; c.Save(); }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{ItemNameResolver.GetItemName(entry.ItemId)}{(entry.HQ ? " (HQ)" : "")}");
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

  /// <summary>
  /// One row per ItemSearchCategory currently represented among the Auto-Market list - not every
  /// category in the game, per the frozen design (Configuration.cs remarks on CategoryRetainerRules).
  /// Each combo defaults to "(any retainer)" and upserts/removes a CategoryRetainerRule on change.
  /// </summary>
  private static void DrawCategoryRouting(Configuration c)
  {
    ImGui.TextUnformatted("Category routing:");
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
      Widgets.Secondary("No marketable categories on your Auto-Market list yet.");
      return;
    }

    var retainerNames = c.LastKnownRetainerNames;
    if (ImGui.BeginChild("##categoryRouting", new Vector2(-1, Math.Min(200, 24 * categoryIds.Count + 30)), true))
    {
      foreach (var categoryId in categoryIds)
      {
        ImGui.PushID((int)categoryId + 900000);
        ImGui.TextUnformatted(ItemNameResolver.GetSearchCategoryName(categoryId));
        ImGui.SameLine(220);
        ImGui.SetNextItemWidth(200);

        var rule = c.GetCategoryRetainerRule(categoryId);
        var options = new List<string> { "(any retainer)" };
        options.AddRange(retainerNames);
        var currentIndex = rule == null ? 0 : Math.Max(0, options.IndexOf(rule.RetainerName));

        if (ImGui.Combo("##catroute", ref currentIndex, [.. options], options.Count))
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
    ImGui.EndChild();
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
    ImGui.TextUnformatted("Quarantined moves (did not stick):");
    Tip("Stacks an earlier sweep moved successfully that were still sitting in the same slot afterwards - the move did not stick server-side, so each sweep leaves them put instead of re-moving them. A slot Auto-Market refilled itself no longer counts as a refused move, so a stack relayed through the same slot is never held here. Anything that is held retries automatically, once, 24 hours after the last move.");

    var snapshot = ReconcileLedgerSource?.Invoke() ?? [];
    if (snapshot.Count == 0)
    {
      Widgets.Secondary("Nothing quarantined. A move the server silently refuses appears here instead of re-firing every sweep.");
      return;
    }

    var now = DateTime.UtcNow;
    ImGui.TextUnformatted($"{snapshot.Count} stack(s) held - each retries once 24 hours after its last move:");
    // 0.1.58.0: releasing is deferred to after the list is drawn - removing an entry mid-frame
    // would resize the child window the rows are being drawn into.
    string? release = null;
    var releaseAll = false;
    if (ImGui.Button("Retry all now##quarantinedRetryAll"))
      releaseAll = true;
    Tip("Drops every hold so the next Auto-Market sweep plans those stacks again. Nothing is moved here - a move the game refuses again goes straight back into this list.");
    if (ImGui.BeginChild("##quarantinedMoves", new Vector2(-1, Math.Min(200, 24 * snapshot.Count + 30)), true))
    {
      foreach (var (key, recordedAt) in snapshot)
      {
        var age = now - recordedAt.ToUniversalTime();
        if (age < TimeSpan.Zero)
          age = TimeSpan.Zero;
        var ageText = age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago" : $"{(int)age.TotalMinutes}m ago";
        if (ImGui.SmallButton($"Retry now##quarantinedRetry{key}"))
          release = key;
        ImGui.NewLine();
        if (!RoutingMove.TryParseReconcileKey(key, out var session, out var container, out var slot, out var itemId, out var hq))
        {
          ImGui.TextUnformatted($"{key} - held since {recordedAt:u} ({ageText})");
          continue;
        }
        var place = $"{AutoMarketService.NameOfContainer((InventoryType)container)}#{slot}";
        ImGui.TextUnformatted($"{ItemNameResolver.GetItemName(itemId)}{(hq ? " (HQ)" : "")} - {session} {place} - held since {recordedAt:u} ({ageText})");
      }
    }
    ImGui.EndChild();
    if (releaseAll)
      ReconcileLedgerReleaseAll?.Invoke();
    else if (release != null)
      ReconcileLedgerRelease?.Invoke(release);
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

    MarketChrome.TwoColumns(
      () =>
      {
    MarketChrome.Section("Pricing rules", $"Current mode: {c.UndercutMode}. Matching and fallback behavior is unchanged.");

    var hq = c.HQ;
    if (MarketChrome.Toggle("Use HQ price", ref hq)) { c.HQ = hq; c.Save(); }
    Tip("If checked, will use the hq price (if item is hq; will fail if there is no HQ price on the MB)");

    ImGui.Separator();

    ImGui.BeginGroup();
    ImGui.Text("Price Mode:");
    ImGui.NewLine();
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
    ImGui.NewLine();
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
    ImGui.NewLine();
    ImGui.Text(c.UndercutMode == UndercutMode.FixedAmount ? "Gil" : "%%");
    ImGui.EndGroup();
    Tip("Amount below the lowest listing. 0 = list at exactly the lowest price.");

    ImGui.BeginGroup();
    ImGui.Text("Max price change percentage:");
    ImGui.NewLine();
    float maxUndercut = c.MaxUndercutPercentage;
    ImGui.SetNextItemWidth(160);
    if (ImGui.SliderFloat("##maximumUndercutAmountPercentage", ref maxUndercut, 0.1f, 99.9f, "%.1f")) { c.MaxUndercutPercentage = MathF.Round(maxUndercut, 1); c.Save(); }
    ImGui.NewLine();
    ImGui.Text("%%");
    ImGui.EndGroup();
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
      ImGui.Indent();
      int maxAge = c.SaleHistoryMaxAgeDays;
      ImGui.BeginGroup();
      ImGui.Text("Ignore sales older than");
      ImGui.NewLine();
      ImGui.SetNextItemWidth(120);
      if (ImGui.InputInt("##saleHistoryMaxAge", ref maxAge))
      {
        c.SaleHistoryMaxAgeDays = Math.Clamp(maxAge, SaleHistoryPricing.MinMaxAgeDays, SaleHistoryPricing.MaxMaxAgeDays);
        c.Save();
      }
      ImGui.NewLine();
      ImGui.Text("days");
      ImGui.EndGroup();
      Tip("If the newest sale is older than this, no price is invented: the listing keeps the placeholder price and you get the usual chat message. Some items last sold years ago.");
      ImGui.Unindent();
    }

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

    ImGui.Separator();

    ImGui.Text("Auto Pinch pre-flight");



    var preflight = c.AutoPinchPreflightEnabled;

    if (MarketChrome.Toggle("Only re-price listings AllaganMarket has flagged", ref preflight)) { c.AutoPinchPreflightEnabled = preflight; c.Save(); }

    Tip("Auto Pinch opens only the listings AllaganMarket marks undercut (red) or stale (yellow) on the retainer sell list, plus any still at the 999,999,999 placeholder price. Everything AllaganMarket does not flag is never touched - including items it has never checked, even once.\n\nThis needs AllaganMarket installed and its own prices working: the plugin reads AllaganMarket's saved price data (no IPC exists), so if AllaganMarket is missing or has nothing cached, nothing but placeholder-priced listings gets re-priced. Untick for the old walk-everything behaviour.");

      },
      () =>
      {
    MarketChrome.Section("Messages & display", "Chat, context-menu, cache, and diagnostic settings.");

    bool chatErrors = c.ShowErrorsInChat;
    if (MarketChrome.Toggle("Show errors in chat", ref chatErrors)) { c.ShowErrorsInChat = chatErrors; c.Save(); }

    bool adjustments = c.ShowPriceAdjustmentsMessages;
    if (MarketChrome.Toggle("Show price adjustments", ref adjustments)) { c.ShowPriceAdjustmentsMessages = adjustments; c.Save(); }

    bool retainerNames = c.ShowRetainerNames;
    if (MarketChrome.Toggle("Show retainer names", ref retainerNames)) { c.ShowRetainerNames = retainerNames; c.Save(); }

    int defaultAmount = c.DefaultAmount;
    ImGui.BeginGroup();
    ImGui.Text("Default amount:");
    ImGui.NewLine();
    ImGui.SetNextItemWidth(160);
    if (ImGui.InputInt("##defaultAmount", ref defaultAmount)) { c.DefaultAmount = Math.Clamp(defaultAmount, 0, int.MaxValue); c.Save(); }
    ImGui.NewLine();
    ImGui.Text("Gil");
    ImGui.EndGroup();
    Tip("Price to use when no board price can be found (0 = leave the listing alone)");

    var showCtx = c.ShowInventoryContextMenuEntry;
    if (MarketChrome.Toggle("Show inventory context menu entries", ref showCtx)) { c.ShowInventoryContextMenuEntry = showCtx; c.Save(); }
    Tip("Adds 'Add to Auto-Market' and 'Add price limits' to inventory item right-click menus.");

    ImGui.BeginGroup();
    if (ImGui.Button("Clear retainer cache")) { c.SeenRetainers.Clear(); c.Save(); }
    ImGui.EndGroup();
    Tip("Clears the list of your own retainers (used to avoid undercutting yourself).");

    if (c.ImportedFromDagobert)
    {
      ImGui.Separator();
      Widgets.Secondary("Settings were imported from Dagobert Price Matcher. You can uninstall that plugin.");
    }

    MarketChrome.Card("diagnostics", () =>
    {
      MarketChrome.Section("Advanced / Diagnostics", "Local-only price-decision logging; no pricing behavior changes.");
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
      Widgets.SecondaryWrapped($"Writes \"{MarketTelemetry.Prefix}\" lines to the plugin log. Nothing leaves this computer.");
    });
    });
  }

  // =====================================================================================
  // Min/Max tab (inherited)
  // =====================================================================================

  private static void DrawItemPriceLimits()
  {
    MarketChrome.Section("Saved price limits", "Minimum and maximum unit prices per item. 0 = no limit.");

    if (Plugin.Configuration.ItemPriceLimits.Count == 0)
    {
      Widgets.InfoBox("Empty · right-click an inventory item and choose 'Add price limits'.", UiState.Empty);
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

    MarketChrome.Section("Retainer selection", "Unchecked retainers are skipped by sweeps and AutoRetainer. Open the retainer list to refresh names.");
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

        }

        if (retainerNameArray == null && !namesUpdated)
          Widgets.Secondary("(cached list - open the retainer list to refresh)");
      }
      else
      {
        Widgets.StateText("Open the retainer list in-game to configure retainer selection", UiState.Blocked);
      }
    }

    ImGui.Separator();

    bool enablePostPinchKey = c.EnablePostPinchkey;
    if (MarketChrome.Toggle("Enable Post Pinch Hotkey", ref enablePostPinchKey)) { c.EnablePostPinchkey = enablePostPinchKey; c.Save(); }
    Tip("Hold this key while posting an item manually to auto-fill the matched price.");

    if (enablePostPinchKey)
    {
      ImGui.NewLine();
      ImGui.SetNextItemWidth(140);
      var index = Array.IndexOf(_virtualKeyStrings, c.PostPinchKey.ToString());
      if (ImGui.Combo("##postPinchKeyCombo", ref index, _virtualKeyStrings, _virtualKeyStrings.Length)) { c.PostPinchKey = Enum.Parse<VirtualKey>(_virtualKeyStrings[index]); c.Save(); }
    }

    bool enablePinchKey = c.EnablePinchKey;
    if (MarketChrome.Toggle("Enable Pinch Hotkey", ref enablePinchKey)) { c.EnablePinchKey = enablePinchKey; c.Save(); }
    Tip("Press this key while the retainer list / sell list is open to start the sweep.");

    if (enablePinchKey)
    {
      ImGui.NewLine();
      ImGui.SetNextItemWidth(140);
      var index = Array.IndexOf(_virtualKeyStrings, c.PinchKey.ToString());
      if (ImGui.Combo("##pinchKeyCombo", ref index, _virtualKeyStrings, _virtualKeyStrings.Length)) { c.PinchKey = Enum.Parse<VirtualKey>(_virtualKeyStrings[index]); c.Save(); }
    }

    ImGui.Separator();

    // Issue #12: the overlay buttons sit top-right on the retainer windows and can cover the
    // venture currency and the close button, so each one can be hidden here.
    MarketChrome.Section("Retainer window buttons", "Hide any overlay action that covers game UI. Hidden actions remain available through the existing commands.");

    var showPinch = c.ShowAutoPinchOverlayButton;
    if (MarketChrome.Toggle("Show 'Auto Pinch'", ref showPinch)) { c.ShowAutoPinchOverlayButton = showPinch; c.Save(); }

    var showAutoMarket = c.ShowAutoMarketOverlayButton;
    if (MarketChrome.Toggle("Show 'Auto Market'", ref showAutoMarket)) { c.ShowAutoMarketOverlayButton = showAutoMarket; c.Save(); }

    var showSweep = c.ShowSweepToBagsOverlayButton;
    if (MarketChrome.Toggle("Show 'Sweep to Bags'", ref showSweep)) { c.ShowSweepToBagsOverlayButton = showSweep; c.Save(); }

    MarketChrome.Card("support", () =>
    {
      MarketChrome.Section("Support", "Write the same local problem report as the existing report command.");
      Plugin.Telemetry?.DrawReportButton();
    });
  }

  private static void Tip(string text)
  {
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip(text);
  }
}
