using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;
using Lalalazy.Telemetry;

namespace LazyMarketCompanion.Windows;

/// <summary>
/// The Auto-Market panel (0.2.8.12): a normal-titled window listing every marketable stack in the
/// player's bags, whether it is on the Auto-Market list, and quick add/remove + per-item knobs -
/// the LIST answer to the same question the bag dots answer at each slot (which never rendered
/// reliably for this player; the dots stay available but are no longer the only way to see state).
///
/// BEHAVIOUR (the task's contract):
/// - Docks beside the inventory window (right when there is room, else left, else floats at a
///   remembered position - <see cref="PanelDock"/>, harness case 161). It must never silently not
///   appear: an inventory window that cannot be read is just the floating case, and the open log
///   line below names what was read.
/// - Opens automatically with the inventory while dock-to-inventory is on; closes with it unless
///   pinned; <c>/lmc panel</c> toggles it any time.
/// - The master switch asks for confirmation on turn-ON and stays locked while a run is in
///   progress. The confirmation is the panel's own safety (the main window's switch writes the
///   setting directly, without one); the run lock is the same run the main window checks.
/// - "Add all visible" / "Remove all visible" show the exact count they would touch and confirm
///   first; single-row edits are the same config writes the main window's table makes
///   (GetOrAddAutoMarketItem / AutoMarketItems.Remove), so the two can never disagree.
/// - The global Auto-Market knobs are copied label-for-label from the Auto-Market tab; a change
///   here writes the same field the main window writes. What the main window does not expose is
///   not exposed here either (placeholder price, the routing-move switch, the manual default
///   amount) - the panel is a convenience view, not a second config surface.
///
/// The list covers the player's four bag containers only (InventoryService.ReadBags), which is
/// what Auto-Market's "bags" stock source draws from; retainer stock has its own view (the
/// retainer's sell window) and the armoury/saddlebags are not a stock source in this plugin.
/// Prices and the price/velocity sort orders are deliberately absent: the panel never fetches.
/// </summary>
internal sealed unsafe class AutoMarketPanelWindow : Window, IDisposable
{
  private readonly Inventory.InventoryService _inventory;
  private readonly Func<bool> _runInProgress;
  private readonly TelemetryGuard _drawGuard = LalaTelemetry.CreateGuard("panel.automarket", "Auto-Market panel");

  // ---- panel UI state ----
  // _status and _categoryFilter are working values resolved from the saved configuration every
  // frame (Draw), so the dropdowns remember their setting between sessions; picking in a
  // dropdown writes the configuration. A stale saved category resolves to "every category" for
  // display while the saved value is kept (see AutoMarketPanelModel.ResolveVisibleCategory).
  private AutoMarketPanelModel.StatusFilter _status = AutoMarketPanelModel.StatusFilter.All;
  private string _search = string.Empty;
  private uint _categoryFilter; // 0 = every category
  private bool _showUnmarketable;

  // ---- placement state (Watch + Draw cooperate) ----
  private PanelDock.DockMode _lastMode = PanelDock.DockMode.Floating;
  private Vector2 _lastPlanPos;
  private Vector2 _lastSize = new(380, 720);
  private Vector2 _lastFloating;
  private bool _wasOpen;
  private bool _autoOpened;
  private bool _loggedOpen;

  // ---- the inventory rectangle read in Watch, for the plan and the open log line ----
  private bool _invOpen;
  private PanelDock.Rect _invRect;
  private float _invScale;

  private readonly Dictionary<uint, bool> _marketableCache = [];

  public AutoMarketPanelWindow(Inventory.InventoryService inventory, Func<bool> runInProgress)
    : base("Lazy Market Companion Auto-Market##lmcPanel")
  {
    _inventory = inventory;
    _runInProgress = runInProgress;
    SizeConstraints = new WindowSizeConstraints
    {
      MinimumSize = new Vector2(360, 420),
      MaximumSize = new Vector2(900, 1400),
    };
    PositionCondition = ImGuiCond.None; // when Position is set (docked), it applies every frame
    _lastFloating = new Vector2(Plugin.Configuration.AutoMarketPanelX, Plugin.Configuration.AutoMarketPanelY);
    IsOpen = false;
  }

  /// <summary>Per-frame, BEFORE the window system draws: auto-open/close and the dock plan. Runs whether or not the panel is open - the watcher must see the inventory while the panel is closed.</summary>
  public void Watch()
  {
    var c = Plugin.Configuration;
    _invOpen = TryGetInventoryRect(out _invRect, out _invScale);
    var dock = c.AutoMarketPanelDock;

    // open/close bookkeeping first: a fresh open re-arms the open log line; a close persists
    // where the panel floated so it comes back there.
    if (IsOpen && !_wasOpen)
      _loggedOpen = false;
    if (!IsOpen && _wasOpen)
    {
      SaveFloatingPos();
      _autoOpened = false;
    }
    _wasOpen = IsOpen;

    // auto-open: the inventory appeared while the dock toggle is on (the panel must never open
    // out of nowhere with the dock toggle off).
    if (dock && _invOpen && !IsOpen && !_wasInvOpenSeen)
    {
      IsOpen = true;
      _autoOpened = true;
    }
    _wasInvOpenSeen = _invOpen;

    // auto-close: an auto-opened panel follows the inventory away unless pinned. A panel the user
    // opened by hand (or via /lmc panel) stays.
    if (IsOpen && _autoOpened && !_invOpen && !c.AutoMarketPanelPinned)
    {
      IsOpen = false;
      _autoOpened = false;
    }
    if (!IsOpen)
      _autoOpened = false;

    if (IsOpen)
    {
      var vp = ImGui.GetMainViewport();
      var plan = PanelDock.Place(
        dock,
        _invOpen ? _invRect : (PanelDock.Rect?)null,
        new PanelDock.Rect(vp.Pos, vp.Size),
        _lastSize,
        _lastFloating);
      Position = plan.Mode == PanelDock.DockMode.Floating ? null : plan.Position;
      _lastMode = plan.Mode;
      _lastPlanPos = plan.Position;
    }
    else
    {
      Position = null; // floating, closed: the next open starts free of stale constraints
    }
  }

  private bool _wasInvOpenSeen;

  /// <summary><c>/lmc panel</c>: toggle the panel any time, independent of the inventory. (Named apart from the windowing library's non-virtual Toggle.)</summary>
  public void ToggleViaCommand()
  {
    IsOpen = !IsOpen;
    if (IsOpen)
      _autoOpened = false; // opened by hand: it does not close with the inventory
  }

  public void SaveFloatingPos()
  {
    var c = Plugin.Configuration;
    if (Math.Abs(c.AutoMarketPanelX - _lastFloating.X) < 0.5f && Math.Abs(c.AutoMarketPanelY - _lastFloating.Y) < 0.5f)
      return;
    c.AutoMarketPanelX = _lastFloating.X;
    c.AutoMarketPanelY = _lastFloating.Y;
    c.Save();
  }

  public void Dispose()
  {
    SaveFloatingPos();
  }

  public override void Draw()
  {
    if (!_drawGuard.TryEnter())
      return;

    try
    {
      // inside Begin: the window's real position and size this frame
      var windowPos = ImGui.GetWindowPos();
      var windowSize = ImGui.GetWindowSize();
      _lastSize = windowSize;
      if (_lastMode == PanelDock.DockMode.Floating)
        _lastFloating = windowPos;

      var c = Plugin.Configuration;

      // ---- the list, straight from the bags ----
      var stacks = new List<AutoMarketPanelModel.StackInput>();
      foreach (var b in _inventory.ReadBags())
        stacks.Add(new AutoMarketPanelModel.StackInput(b.Container, b.Slot, b.ItemId, b.Hq, b.Quantity));

      var entries = new List<AutoMarketPanelModel.EntryInput>(c.AutoMarketItems.Count);
      foreach (var e in c.AutoMarketItems)
        entries.Add(new AutoMarketPanelModel.EntryInput(e.ItemId, e.HQ, e.Enabled, e.ExcludeFromCategoryRouting, e.StackSize, e.KeepInBags));

      var all = AutoMarketPanelModel.Build(stacks, entries, ItemNameResolver.GetItemName, ItemNameResolver.SearchCategoryId, IsMarketable);
      var counts = AutoMarketPanelModel.Count(all);

      // The filter dropdowns remember their setting (0.2.8.15): the working values are resolved
      // from the saved configuration every frame, so a plugin reload or game restart restores
      // them. A saved category that is not in the current bags resolves to "every category" for
      // display - it must not hide everything behind an invisible filter - while the saved value
      // itself is kept until another is picked.
      _status = AutoMarketPanelModel.ResolveVisibleStatus(c.AutoMarketPanelStatus);
      _categoryFilter = AutoMarketPanelModel.ResolveVisibleCategory(c.AutoMarketPanelCategory, AutoMarketPanelModel.PresentCategories(all));
      var filters = new AutoMarketPanelModel.Filters(_status, _search, _categoryFilter, _showUnmarketable);
      var visible = AutoMarketPanelModel.Visible(all, filters);
      var bulk = AutoMarketPanelModel.PlanBulk(visible);

      if (!_loggedOpen)
      {
        _loggedOpen = true;
        Svc.Log.Information(
          $"[LMC] Auto-Market panel opened: dock={_lastMode} pos=({windowPos.X:0.#},{windowPos.Y:0.#}) size=({windowSize.X:0.#},{windowSize.Y:0.#})" +
          $" viewport=({ImGui.GetMainViewport().Pos.X:0.#},{ImGui.GetMainViewport().Pos.Y:0.#})+({ImGui.GetMainViewport().Size.X:0.#},{ImGui.GetMainViewport().Size.Y:0.#})" +
          $" inventory={(_invOpen ? $"({_invRect.Pos.X:0.#},{_invRect.Pos.Y:0.#})+({_invRect.Size.X:0.#},{_invRect.Size.Y:0.#}) scale={_invScale:0.00}" : "not readable")}" +
          $" rows: on-list={counts.OnList} not-listed={counts.NotListed} excluded={counts.Excluded} unmarketable={counts.NotMarketable} showing={visible.Count}");
      }

      DrawHeader(c, counts);
      ImGui.Separator();
      DrawFilters(all, counts);
      DrawBulkButtons(bulk);
      DrawTable(c, visible);
      DrawBulkModals(c, bulk);
      ImGui.Separator();
      DrawKnobs(c);
    }
    catch (Exception ex)
    {
      // The panel is pure display over config writes; it must never take the plugin down.
      _drawGuard.Failed(ex);
    }
  }

  // =====================================================================================
  // header: dock toggle, pin, mode
  // =====================================================================================

  private void DrawHeader(Configuration c, AutoMarketPanelModel.Counts counts)
  {
    var dock = c.AutoMarketPanelDock;
    if (ImGui.Checkbox("Dock to inventory", ref dock)) { c.AutoMarketPanelDock = dock; c.Save(); }
    Tip("Place the panel beside the inventory window (right when there is room, else left). Off = the panel floats wherever you dragged it, and opening your bags does not open or close it.");

    if (dock)
    {
      ImGui.SameLine(0, 20);
      var pinned = c.AutoMarketPanelPinned;
      if (ImGui.Checkbox("Stay open when the inventory closes", ref pinned)) { c.AutoMarketPanelPinned = pinned; c.Save(); }
      Tip("Off (the default): the panel opens and closes with your inventory.\r\nOn: the panel stays up after the inventory closes - close it by hand or with /lmc panel.");
    }

    ImGui.SameLine(0, 20);
    var where = _lastMode switch
    {
      PanelDock.DockMode.Right => "docked right of the inventory",
      PanelDock.DockMode.Left => "docked left of the inventory",
      _ => _invOpen ? "floating (no room beside the inventory)" : "floating (inventory not readable or closed)",
    };
    ImGui.TextDisabled(where);
    ImGui.SameLine(0, 20);
    ImGui.TextDisabled($"{counts.OnList} on list, {counts.NotListed} not");
  }

  // =====================================================================================
  // filters
  // =====================================================================================

  private void DrawFilters(List<AutoMarketPanelModel.Row> all, AutoMarketPanelModel.Counts counts)
  {
    var statusIdx = (int)_status;
    ImGui.SetNextItemWidth(120);
    if (ImGui.Combo("##lmcPanelStatus", ref statusIdx, ["All", "On list", "Not on list", "Excluded"], 4))
    {
      _status = (AutoMarketPanelModel.StatusFilter)statusIdx;
      Plugin.Configuration.AutoMarketPanelStatus = _status;
      Plugin.Configuration.Save();
    }
    Tip("Show every marketable stack, only the ones on the Auto-Market list, only the ones missing from it, or only the ones quick-excluded from category routing. This setting is remembered between sessions.");

    ImGui.SameLine(0, 12);
    ImGui.SetNextItemWidth(150);
    if (ImGui.BeginCombo("##lmcPanelCategory", _categoryFilter == 0 ? "Every category" : ItemNameResolver.GetSearchCategoryName(_categoryFilter)))
    {
      if (ImGui.Selectable("Every category", _categoryFilter == 0))
      {
        _categoryFilter = 0;
        Plugin.Configuration.AutoMarketPanelCategory = 0;
        Plugin.Configuration.Save();
      }
      foreach (var catId in AutoMarketPanelModel.PresentCategories(all))
      {
        var label = $"{ItemNameResolver.GetSearchCategoryName(catId)} ({catId})";
        if (ImGui.Selectable(label, _categoryFilter == catId))
        {
          _categoryFilter = catId;
          Plugin.Configuration.AutoMarketPanelCategory = catId;
          Plugin.Configuration.Save();
        }
      }
      ImGui.EndCombo();
    }
    Tip("Only stacks in this market-board category. This setting is remembered between sessions; a category the current bags do not have shows as every category until it is picked again.");

    ImGui.SameLine(0, 12);
    var show = _showUnmarketable;
    if (ImGui.Checkbox($"Show unmarketable ({counts.NotMarketable})", ref show))
      _showUnmarketable = show;
    Tip("Quest items, gear the sheet calls untradable, anything with no market category: they get no dot and no Auto-Market listing, so they are hidden. This shows and counts them.");

    ImGui.SetNextItemWidth(-30);
    ImGui.InputTextWithHint("##lmcPanelSearch", "search by item name", ref _search, 64);
  }

  // =====================================================================================
  // bulk buttons + confirmation modals
  // =====================================================================================

  private void DrawBulkButtons(AutoMarketPanelModel.BulkPlan bulk)
  {
    if (ImGui.Button($"Add all visible ({bulk.Adds.Count})"))
      ImGui.OpenPopup("Add all visible items?"); // the modal's exact id string: ImGui matches the two by hash
    Tip("Adds every currently visible 'not listed' row to the Auto-Market list (default settings, enabled) - the same entries the inventory context menu's 'Add to Auto-Market' creates. You confirm first.");

    ImGui.SameLine(0, 12);
    if (ImGui.Button($"Remove all visible ({bulk.Removes.Count})"))
      ImGui.OpenPopup("Remove all visible items?"); // the modal's exact id string: ImGui matches the two by hash
    Tip("Removes every currently visible on-list row from the Auto-Market list. You confirm first - and the filters above decide what 'visible' means.");
  }

  private void DrawBulkModals(Configuration c, AutoMarketPanelModel.BulkPlan bulk)
  {
    var _unusedAdd = true;
    if (ImGui.BeginPopupModal("Add all visible items?", ref _unusedAdd, ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.TextUnformatted($"Add {bulk.Adds.Count} item(s) to the Auto-Market list now?");
      ImGui.TextDisabled("New entries use the default settings (enabled, stack = the market maximum).");
      ImGui.Separator();
      if (ImGui.Button("Add", new Vector2(120, 0)))
      {
        foreach (var (id, hq) in bulk.Adds)
          c.GetOrAddAutoMarketItem(id, hq);
        c.Save();
        ImGui.CloseCurrentPopup();
      }
      ImGui.SameLine();
      if (ImGui.Button("Cancel", new Vector2(120, 0)))
        ImGui.CloseCurrentPopup();
      ImGui.EndPopup();
    }

    var _unusedRemove = true;
    if (ImGui.BeginPopupModal("Remove all visible items?", ref _unusedRemove, ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.TextUnformatted($"Remove {bulk.Removes.Count} item(s) from the Auto-Market list now?");
      ImGui.TextDisabled("Auto-Market will stop touching these items entirely. This does not cancel listings already on the retainers.");
      ImGui.Separator();
      if (ImGui.Button("Remove", new Vector2(120, 0)))
      {
        foreach (var (id, hq) in bulk.Removes)
        {
          var entry = c.GetAutoMarketItem(id, hq);
          if (entry != null)
            c.AutoMarketItems.Remove(entry);
        }
        c.Save();
        ImGui.CloseCurrentPopup();
      }
      ImGui.SameLine();
      if (ImGui.Button("Cancel", new Vector2(120, 0)))
        ImGui.CloseCurrentPopup();
      ImGui.EndPopup();
    }
  }

  // =====================================================================================
  // the table
  // =====================================================================================

  private void DrawTable(Configuration c, List<AutoMarketPanelModel.Row> visible)
  {
    if (visible.Count == 0)
    {
      ImGui.TextColored(Muted, _search.Length > 0 || _categoryFilter != 0 || _status != AutoMarketPanelModel.StatusFilter.All
        ? "Nothing matches the filters above."
        : "Nothing marketable in your bags right now.");
      return;
    }

    var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings
      | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
    if (!ImGui.BeginTable("##lmcPanelTable", 8, flags, new Vector2(-1, -1)))
      return;

    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 26f);
    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 46f);
    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 88f);
    ImGui.TableSetupColumn("Stack", ImGuiTableColumnFlags.WidthFixed, 58f);
    ImGui.TableSetupColumn("Keep", ImGuiTableColumnFlags.WidthFixed, 58f);
    ImGui.TableSetupColumn("Route", ImGuiTableColumnFlags.WidthFixed, 46f);
    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 56f);
    ImGui.TableHeadersRow();

    foreach (var row in visible)
    {
      ImGui.PushID($"{row.ItemId}:{(row.Hq ? "hq" : "nq")}");
      ImGui.TableNextRow();

      // On: only real for on-list rows; a tick here toggles the entry like the main window's.
      ImGui.TableNextColumn();
      if (row.Kind == AutoMarketPanelModel.RowKind.OnList)
      {
        var entry = c.GetAutoMarketItem(row.ItemId, row.Hq);
        if (entry != null)
        {
          var on = entry.Enabled;
          if (ImGui.Checkbox("##on", ref on)) { entry.Enabled = on; c.Save(); }
          Tip("Ticks and unticks this item on the Auto-Market list - the same switch the main window's table has. Unticked = on the list but not listed.");
        }
      }

      ImGui.TableNextColumn();
      ImGui.TextUnformatted($"{row.Name}{(row.Hq ? " (HQ)" : "")}");
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip($"Item ID: {row.ItemId}  max stack {ItemNameResolver.MaxStack(row.ItemId)}  in {row.Stacks} stack(s)");

      ImGui.TableNextColumn();
      ImGui.TextUnformatted($"{row.Quantity}");

      ImGui.TableNextColumn();
      switch (row.Kind)
      {
        case AutoMarketPanelModel.RowKind.OnList when row.EntryEnabled:
          ImGui.TextColored(Green, row.Excluded ? "on list" : "on list");
          break;
        case AutoMarketPanelModel.RowKind.OnList:
          ImGui.TextColored(Orange, "on list (off)");
          break;
        case AutoMarketPanelModel.RowKind.NotListed:
          ImGui.TextColored(Muted, "not listed");
          break;
        default:
          ImGui.TextDisabled("unmarketable");
          break;
      }

      // Stack + Keep: the same clamped edits the main window's table makes (only for on-list rows).
      ImGui.TableNextColumn();
      if (row.Kind == AutoMarketPanelModel.RowKind.OnList && c.GetAutoMarketItem(row.ItemId, row.Hq) is { } stackEntry)
      {
        ImGui.SetNextItemWidth(-1);
        var stack = stackEntry.StackSize;
        var listingCap = MarketListingCap.For((int)ItemNameResolver.MaxStack(row.ItemId));
        if (ImGui.InputInt("##stack", ref stack, 0, 0)) { stackEntry.StackSize = Math.Clamp(stack, 0, listingCap); c.Save(); }
        if (ImGui.IsItemHovered())
          ImGui.SetTooltip($"Units per listing. 0 = the market's maximum ({listingCap}).");
      }

      ImGui.TableNextColumn();
      if (row.Kind == AutoMarketPanelModel.RowKind.OnList && c.GetAutoMarketItem(row.ItemId, row.Hq) is { } keepEntry)
      {
        ImGui.SetNextItemWidth(-1);
        var keep = keepEntry.KeepInBags;
        if (ImGui.InputInt("##keep", ref keep, 0, 0)) { keepEntry.KeepInBags = Math.Max(keep, 0); c.Save(); }
        if (ImGui.IsItemHovered())
          ImGui.SetTooltip("Never sell below this many in your bags. 0 = sell everything.");
      }

      // Route: the quick-exclude checkbox, same write as the main window's "Skip routing" column.
      ImGui.TableNextColumn();
      if (row.Kind == AutoMarketPanelModel.RowKind.OnList && c.GetAutoMarketItem(row.ItemId, row.Hq) is { } routeEntry)
      {
        var skip = routeEntry.ExcludeFromCategoryRouting;
        if (ImGui.Checkbox("##skiprouting", ref skip)) { routeEntry.ExcludeFromCategoryRouting = skip; c.Save(); }
        Tip("Keeps this item out of category routing. It still sells normally, from wherever it sits, on every retainer.");
      }

      ImGui.TableNextColumn();
      if (row.Kind == AutoMarketPanelModel.RowKind.NotListed)
      {
        if (ImGui.SmallButton("Add"))
        {
          c.GetOrAddAutoMarketItem(row.ItemId, row.Hq);
          c.Save();
        }
        Tip("Add to the Auto-Market list (default settings, enabled) - the same as right-clicking the item in your bags.");
      }
      else if (row.Kind == AutoMarketPanelModel.RowKind.OnList)
      {
        if (ImGui.SmallButton("Remove"))
        {
          var entry = c.GetAutoMarketItem(row.ItemId, row.Hq);
          if (entry != null)
            c.AutoMarketItems.Remove(entry);
          c.Save();
        }
        Tip("Remove from the Auto-Market list. Does not cancel listings already on the retainers.");
      }

      ImGui.PopID();
    }

    ImGui.EndTable();
  }

  // =====================================================================================
  // global knobs - label-for-label the Auto-Market tab's controls, writing the same fields
  // =====================================================================================

  private void DrawKnobs(Configuration c)
  {
    if (!ImGui.CollapsingHeader("Auto-Market settings"))
      return;

    var enabled = c.AutoMarketEnabled;
    var busy = _runInProgress();
    if (busy) ImGui.BeginDisabled();
    if (ImGui.Checkbox("Enable Auto-Market", ref enabled))
    {
      if (enabled)
        ImGui.OpenPopup("Turn Auto-Market on?"); // the modal's exact id string: ImGui matches the two by hash
      else
      {
        c.AutoMarketEnabled = false;
        c.Save();
      }
    }
    if (busy) ImGui.EndDisabled();
    Tip(busy
      ? "A run is in progress. Cancel it first."
      : "Master switch. When off, the Auto Market buttons and the AutoRetainer hook do nothing.");
    var _unusedMaster = true;
    if (ImGui.BeginPopupModal("Turn Auto-Market on?", ref _unusedMaster, ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.TextUnformatted("Turning Auto-Market on lets it run the next time it is triggered, including during AutoRetainer cycles if that is set up in its settings.");
      ImGui.Separator();
      if (ImGui.Button("Turn on", new Vector2(120, 0))) { c.AutoMarketEnabled = true; c.Save(); ImGui.CloseCurrentPopup(); }
      ImGui.SameLine();
      if (ImGui.Button("Keep it off", new Vector2(120, 0)))
        ImGui.CloseCurrentPopup();
      ImGui.EndPopup();
    }

    var markers = c.AutoMarketMarkersEnabled;
    if (ImGui.Checkbox("Show Auto-Market markers in bags", ref markers)) { c.AutoMarketMarkersEnabled = markers; c.Save(); }
    Tip("Draws a small green dot on every bag stack that is on the Auto-Market list and enabled.\r\nAn item on the list with its tick unticked gets no dot - it would not be listed.\r\nThis panel lists the same state as a table, even when the dots do not draw.");

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

    ImGui.TextUnformatted("Stock source:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(180);
    var src = (int)c.AutoMarketSource;
    if (ImGui.Combo("##lmcPanelSource", ref src, ["Bags only", "Retainer inventory only", "Bags + retainer inventory"], 3)) { c.AutoMarketSource = (StockSource)src; c.Save(); }
    Tip("Where items may be taken from. Per-item override available in the main window's table. This panel always lists your bags.");

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
    if (ImGui.Combo("##lmcPanelPriceMode", ref pm, ["Placeholder, then match on the board", "Universalis first (fallback: placeholder)"], 2)) { c.AutoMarketPriceMode = (NewListingPriceMode)pm; c.Save(); }
    Tip("Placeholder: list at an absurd price, then immediately run the normal price match on that slot (Compare Prices).\r\n" +
        "Universalis first: ask Universalis for the data-center low before listing, so the item goes up already priced. Falls back to placeholder when Universalis has nothing.");

    ImGui.SameLine(0, 20);
    ImGui.TextUnformatted("Reserve slots:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(80);
    var reserve = c.AutoMarketReserveSlots;
    if (ImGui.InputInt("##lmcPanelReserve", ref reserve)) { c.AutoMarketReserveSlots = Math.Clamp(reserve, 0, 19); c.Save(); }
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
      ImGui.SetNextItemWidth(280);
      var fb = (int)c.AutoMarketPinchFallback;
      if (ImGui.Combo("##lmcPanelPinchFallback", ref fb,
            ["Re-price every listing", "Leave it at the placeholder and tell me", "Re-price only my Auto-Market items"], 3))
      { c.AutoMarketPinchFallback = (PinchFallbackMode)fb; c.Save(); }
      Tip("Auto Market finds its new listings by reading your sell list, so this should not come up.\n" +
          "If it ever does:\n" +
          "Re-price every listing (the default): nothing is left unsellable, but listings you never asked us to touch get re-priced.\n" +
          "Leave it at the placeholder: nothing else is touched, but the new listing sits at 999,999,999 gil and will not sell until you price it or run Auto Pinch.\n" +
          "Only my Auto-Market items: re-price just the listings whose item is on the list in the main window, so a listing you made by hand is never touched.");
    }

    ImGui.TextUnformatted("Listing order:"); ImGui.SameLine();
    ImGui.SetNextItemWidth(220);
    var sortIdx = (int)c.AutoMarketSortMode;
    if (ImGui.Combo("##lmcPanelSortMode", ref sortIdx, ["List order", "Cheapest first", "Fastest selling first", "Most expensive first"], 4))
    { c.AutoMarketSortMode = (MarketSortMode)sortIdx; c.Save(); }
    Tip("When the retainer does not have enough free market slots for everything on your Auto-Market list, this decides which items get the slots first.\r\n"
        + "Fastest selling first (the default): one Universalis lookup ranks your items by how many sell per day, so the slots go to what actually moves.\r\n"
        + "Cheapest / most expensive first: same lookup, ranked by current board price.\r\n"
        + "List order: the order of the main window's table - and the order the 'on list' rows keep here.\r\n"
        + "Items Universalis has no fresh data for keep their list position and sort last, and a failed lookup changes nothing.");

    var gate = c.AutoMarketValueGateEnabled;
    if (ImGui.Checkbox("Only list items worth more than", ref gate)) { c.AutoMarketValueGateEnabled = gate; c.Save(); }
    ImGui.SameLine();
    ImGui.SetNextItemWidth(140);
    var threshold = (int)Math.Min(c.AutoMarketValueGateThresholdGil, int.MaxValue);
    if (ImGui.InputInt("##lmcPanelGateThreshold", ref threshold, 0, 0)) { c.AutoMarketValueGateThresholdGil = Math.Max(threshold, 0); c.Save(); }
    ImGui.SameLine();
    ImGui.TextUnformatted("gil, net of fees");
    Tip("Before listing, Auto-Market checks every enabled item against current Universalis prices. Items whose total sellable value "
        + "(current board price x everything it could sell of that item, after the market's 5% fee) is at or under this number take the vendor leg instead of a market slot - only NQ non-gear items the Item sheet prices as vendorable; the retainer sells them to a vendor in the same session.\r\n"
        + "0 = the switch does nothing.");

    if (c.AutoMarketValueGateEnabled)
    {
      int gateFresh = c.AutoMarketGateFreshnessHours;
      ImGui.BeginGroup();
      ImGui.Text("Only trust gate prices newer than");
      ImGui.SameLine();
      ImGui.SetNextItemWidth(120);
      if (ImGui.SliderInt("##lmcPanelGateFreshness", ref gateFresh, 1, 168)) { c.AutoMarketGateFreshnessHours = Math.Clamp(gateFresh, 1, 168); c.Save(); }
      ImGui.SameLine();
      ImGui.Text("hours");
      ImGui.EndGroup();
      Tip("Universalis is crowd-sourced and lags. Prices older than this never hold an item back - the item is listed normally. Same idea as the Auto Pinch pre-flight freshness window.");
    }

    var autoAssign = c.AutoAssignUnroutedCategories;
    if (ImGui.Checkbox("Auto-assign uncovered categories", ref autoAssign))
    {
      c.AutoAssignUnroutedCategories = autoAssign;
      c.Save();
    }
    Tip("When a sweep finds marked Auto-Market stock in the bags whose market-board category has no rule, it assigns that category to the sweep-enabled retainer carrying the fewest routed categories and moves the stock there in the same pass. Manage the rules themselves in the main window's Category Routing section.");
  }

  // =====================================================================================
  // helpers
  // =====================================================================================

  /// <summary>The same marketability test the bag markers use, cached per item id per session.</summary>
  private bool IsMarketable(uint itemId)
  {
    if (_marketableCache.TryGetValue(itemId, out var cached))
      return cached;
    var result = false;
    var sheet = Plugin.DataManager?.GetExcelSheet<Item>();
    if (sheet != null && sheet.TryGetRow(itemId, out var item))
      result = !item.IsUntradable && item.ItemSearchCategory.RowId != 0;
    _marketableCache[itemId] = result;
    return result;
  }

  /// <summary>
  /// The inventory window's screen rectangle, from the addon's own root node - the same read the
  /// 0.2.8.11 marker probe verified against eight builds. Tries the tabbed "Inventory" parent and
  /// the expanded "InventoryExpansion" (only one is live at a time). Returns false when neither is
  /// up, the root is missing/hidden, or the rectangle is degenerate - the panel then floats, and
  /// the open log line says what was read.
  /// </summary>
  private bool TryGetInventoryRect(out PanelDock.Rect rect, out float scale)
  {
    rect = default;
    scale = 0f;
    foreach (var name in (ReadOnlySpan<string>)["Inventory", "InventoryExpansion"])
    {
      if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon) || !GenericHelpers.IsAddonReady(addon))
        continue;
      var root = addon->RootNode;
      if (root == null || !root->IsVisible() || root->ScaleX <= 0 || root->ScaleY <= 0)
        continue;
      var pos = new Vector2(root->ScreenX, root->ScreenY);
      var size = new Vector2(root->Width * root->ScaleX, root->Height * root->ScaleY);
      if (size.X <= 0 || size.Y <= 0)
        continue;
      rect = new PanelDock.Rect(pos, size);
      scale = root->ScaleX;
      return true;
    }
    return false;
  }

  private static readonly Vector4 Green = new(0.40f, 0.80f, 0.40f, 1f);
  private static readonly Vector4 Orange = new(0.95f, 0.65f, 0.20f, 1f);
  private static readonly Vector4 Muted = new(0.65f, 0.65f, 0.65f, 1f);

  private static void Tip(string text)
  {
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip(text);
  }
}
