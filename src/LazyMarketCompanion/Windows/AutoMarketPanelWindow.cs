using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LalaUiKit;
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
  private readonly Action<string>? _cancelAutomation;
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

  public AutoMarketPanelWindow(Inventory.InventoryService inventory, Func<bool> runInProgress, Action<string>? cancelAutomation)
    : base("Lazy Market Companion Auto-Market##lmcPanel")
  {
    _inventory = inventory;
    _runInProgress = runInProgress;
    _cancelAutomation = cancelAutomation;
    Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
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
    catch (Exception ex)
    {
      // The panel is pure display over config writes; it must never take the plugin down.
      _drawGuard.Failed(ex);
    }
  }

  /// <summary>The shell draws its own full-bleed bands from the window edge, so the window itself carries no padding.</summary>
  public override void PreDraw()
  {
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
  }

  public override void PostDraw()
  {
    ImGui.PopStyleVar();
  }

  private UiState CurrentState(Configuration c)
  {
    return _runInProgress() ? UiState.Running : !c.AutoMarketEnabled ? UiState.Idle : c.AutoMarketItems.Count == 0 ? UiState.Empty : UiState.Ready;
  }

  private void DrawFrame()
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

      MarketChrome.HeaderBand("Auto-Market", CurrentState(c), [], out _, out _, out var closeClicked);
      if (closeClicked)
        IsOpen = false;
      MarketChrome.PageHeader("Bag groups", "Live bag groups, list membership, per-item limits, and bulk actions.", $"{visible.Count} shown · {counts.OnList} on list");
      var busy = _runInProgress();
      var footerHeight = MarketChrome.FooterHeight(ImGui.GetWindowWidth(), busy);
      if (MarketChrome.BeginBody(footerHeight))
      {
        MarketChrome.StatCards(
          new MarketChrome.Stat("On list", counts.OnList.ToString(), "Bag groups with an Auto-Market entry"),
          new MarketChrome.Stat("Not listed", counts.NotListed.ToString(), "Marketable groups available to add"),
          new MarketChrome.Stat("Excluded", counts.Excluded.ToString(), "Entries skipped by category routing"));
        DrawHeader(c, counts);
        DrawFilters(all, counts);
        DrawBulkButtons(bulk);
        DrawTable(c, visible);
        DrawKnobs(c);
      }
      MarketChrome.EndBody();
      if (_pendingPopup == 1)
        ImGui.OpenPopup("Turn Auto-Market on?");
      else if (_pendingPopup == 2)
        ImGui.OpenPopup("Add all visible items?");
      else if (_pendingPopup == 3)
        ImGui.OpenPopup("Remove all visible items?");
      _pendingPopup = 0;
      DrawBulkModals(c, bulk);
      DrawFooter(c, visible.Count);
  }

  private void DrawHeader(Configuration c, AutoMarketPanelModel.Counts counts)
  {
    MarketChrome.Card("panel_dock", () =>
    {
      MarketChrome.CardHeader("Panel placement", $"{counts.OnList} on list · {counts.NotListed} not listed");
      var dock = c.AutoMarketPanelDock;
      if (MarketChrome.Toggle("Dock to inventory", ref dock)) { c.AutoMarketPanelDock = dock; c.Save(); }
      Tip("Place the panel beside the inventory window (right when there is room, else left). Off = the panel floats wherever you dragged it, and opening your bags does not open or close it.");

      if (dock)
      {
        var pinned = c.AutoMarketPanelPinned;
        if (MarketChrome.Toggle("Stay open when the inventory closes", ref pinned)) { c.AutoMarketPanelPinned = pinned; c.Save(); }
        Tip("Off (the default): the panel opens and closes with your inventory.\r\nOn: the panel stays up after the inventory closes - close it by hand or with /lmc panel.");
      }

      var where = _lastMode switch
      {
        PanelDock.DockMode.Right => "Docked right of the inventory",
        PanelDock.DockMode.Left => "Docked left of the inventory",
        _ => _invOpen ? "Floating · no room beside the inventory" : "Floating · inventory not readable or closed",
      };
      MarketChrome.RowsDone();
      ImGui.Dummy(new Vector2(0, 4));
      MarketChrome.Secondary(where);
    });
  }

  private void DrawFooter(Configuration c, int visibleCount)
  {
    var busy = _runInProgress();
    var state = CurrentState(c);
    var action = busy ? "the current market action" : !c.AutoMarketEnabled ? "Auto-Market is off" : c.AutoMarketItems.Count == 0 ? "no Auto-Market entries" : "Auto-Market list ready";
    var detail = busy ? "Stop requests cancellation of the current action." : c.AutoMarketEnabled && c.AutoMarketItems.Count == 0 ? "Add an item from the main window or inventory context menu." : $"{visibleCount} visible groups · {c.AutoMarketItems.Count} configured entries";
    MarketChrome.Footer(state, action, detail, busy ? "Stop" : null, busy ? () => _cancelAutomation?.Invoke("cancelled from the Auto-Market panel") : null);
  }

  // =====================================================================================
  // filters
  // =====================================================================================

  private void DrawFilters(List<AutoMarketPanelModel.Row> all, AutoMarketPanelModel.Counts counts)
  {
    MarketChrome.Card("panel_filters", () =>
    {
      MarketChrome.CardHeader("Filters", "Narrow the live bag groups without changing the underlying list.");
      var statusIdx = (int)_status;
      if (MarketChrome.Field("Status", width => MarketChrome.Combo("##lmcPanelStatus", ref statusIdx, ["All", "On list", "Not on list", "Excluded"], width)))
      {
        _status = (AutoMarketPanelModel.StatusFilter)statusIdx;
        Plugin.Configuration.AutoMarketPanelStatus = _status;
        Plugin.Configuration.Save();
      }
      Tip("Show every marketable stack, only the ones on the Auto-Market list, only the ones missing from it, or only the ones quick-excluded from category routing. This setting is remembered between sessions.");

      var categories = AutoMarketPanelModel.PresentCategories(all).ToList();
      var labels = new List<string> { "Every category" };
      labels.AddRange(categories.Select(catId => $"{ItemNameResolver.GetSearchCategoryName(catId)} ({catId})"));
      var catIdx = _categoryFilter == 0 ? 0 : Math.Max(0, categories.IndexOf(_categoryFilter) + 1);
      if (MarketChrome.Field("Category", width => MarketChrome.Combo("##lmcPanelCategory", ref catIdx, [.. labels], width)))
      {
        if (catIdx == 0)
        {
          _categoryFilter = 0;
          Plugin.Configuration.AutoMarketPanelCategory = 0;
        }
        else
        {
          var catId = categories[catIdx - 1];
          _categoryFilter = catId;
          Plugin.Configuration.AutoMarketPanelCategory = catId;
        }
        Plugin.Configuration.Save();
      }
      Tip("Only stacks in this market-board category. This setting is remembered between sessions; a category the current bags do not have shows as every category until it is picked again.");

      var show = _showUnmarketable;
      if (MarketChrome.Toggle($"Show unmarketable ({counts.NotMarketable})", ref show))
        _showUnmarketable = show;
      Tip("Quest items, gear the sheet calls untradable, anything with no market category: they get no dot and no Auto-Market listing, so they are hidden. This shows and counts them.");

      var search = _search;
      if (MarketChrome.Field("Search", width =>
          {
            ImGui.SetNextItemWidth(width);
            return ImGui.InputTextWithHint("##lmcPanelSearch", "search by item name", ref search, 64);
          }))
        _search = search;
    });
  }

  // =====================================================================================
  // bulk buttons + confirmation modals
  // =====================================================================================

  private void DrawBulkButtons(AutoMarketPanelModel.BulkPlan bulk)
  {
    MarketChrome.Card("bulk_actions", () =>
    {
      MarketChrome.CardHeader("Bulk actions", "Counts come from the rows visible under the current filters.");
      var width = MarketChrome.Avail();
      var addLabel = $"Add all visible ({bulk.Adds.Count})";
      var removeLabel = $"Remove all visible ({bulk.Removes.Count})";
      var sideBySide = MarketChrome.ButtonWidth(addLabel) + MarketChrome.ButtonWidth(removeLabel) + 8 <= width;
      var each = sideBySide ? (width - 8) / 2f : width;
      if (MarketChrome.Button(addLabel, each))
        _pendingPopup = 2; // opened at window scope after the body so the modal shares its id stack
      Tip("Adds every currently visible 'not listed' row to the Auto-Market list (default settings, enabled) - the same entries the inventory context menu's 'Add to Auto-Market' creates. You confirm first.");

      if (sideBySide)
        ImGui.SameLine(0, 8);
      if (MarketChrome.Button(removeLabel, each))
        _pendingPopup = 3;
      Tip("Removes every currently visible on-list row from the Auto-Market list. You confirm first - and the filters above decide what 'visible' means.");
    });
  }

  private int _pendingPopup;

  private void DrawBulkModals(Configuration c, AutoMarketPanelModel.BulkPlan bulk)
  {
    MarketChrome.PushModalStyle("Turn Auto-Market on?");
    var keep1 = true;
    if (MarketChrome.ModalBegan(ImGui.BeginPopupModal("Turn Auto-Market on?", ref keep1, MarketChrome.ModalFlags), "Turn Auto-Market on?", 320))
    {
      ImGui.TextWrapped("Turning Auto-Market on lets it run the next time it is triggered, including during AutoRetainer cycles if that is set up in its settings.");
      switch (MarketChrome.ModalButtons("Turn on", "Keep it off", false))
      {
        case 1:
          c.AutoMarketEnabled = true;
          c.Save();
          ImGui.CloseCurrentPopup();
          break;
        case 2:
          ImGui.CloseCurrentPopup();
          break;
      }
      MarketChrome.EndModal();
    }

    MarketChrome.PushModalStyle("Add all visible items?");
    var keep2 = true;
    if (MarketChrome.ModalBegan(ImGui.BeginPopupModal("Add all visible items?", ref keep2, MarketChrome.ModalFlags), "Add all visible items?", 320))
    {
      ImGui.TextWrapped($"Add {bulk.Adds.Count} {(bulk.Adds.Count == 1 ? "item" : "items")} to the Auto-Market list now?");
      MarketChrome.Secondary("New entries use the default settings (enabled, stack = the market maximum).");
      switch (MarketChrome.ModalButtons("Add", "Cancel", false))
      {
        case 1:
          foreach (var (id, hq) in bulk.Adds)
            c.GetOrAddAutoMarketItem(id, hq);
          c.Save();
          ImGui.CloseCurrentPopup();
          break;
        case 2:
          ImGui.CloseCurrentPopup();
          break;
      }
      MarketChrome.EndModal();
    }

    MarketChrome.PushModalStyle("Remove all visible items?");
    var keep3 = true;
    if (MarketChrome.ModalBegan(ImGui.BeginPopupModal("Remove all visible items?", ref keep3, MarketChrome.ModalFlags), "Remove all visible items?", 320))
    {
      ImGui.TextWrapped($"Remove {bulk.Removes.Count} {(bulk.Removes.Count == 1 ? "item" : "items")} from the Auto-Market list now?");
      MarketChrome.Secondary("Auto-Market will stop touching these items entirely. This does not cancel listings already on the retainers.");
      switch (MarketChrome.ModalButtons("Remove", "Cancel", true))
      {
        case 1:
          foreach (var (id, hq) in bulk.Removes)
          {
            var entry = c.GetAutoMarketItem(id, hq);
            if (entry != null)
              c.AutoMarketItems.Remove(entry);
          }
          c.Save();
          ImGui.CloseCurrentPopup();
          break;
        case 2:
          ImGui.CloseCurrentPopup();
          break;
      }
      MarketChrome.EndModal();
    }
  }

  // =====================================================================================
  // the list: one card per bag group (the Item column keeps the room it needs at any width)
  // =====================================================================================

  private void DrawTable(Configuration c, List<AutoMarketPanelModel.Row> visible)
  {
    MarketChrome.Card("bag_groups", () =>
    {
      MarketChrome.CardHeader("Marketable stacks", "Live quantities and the same per-item edits as the main window.", $"{visible.Count} groups");
      if (visible.Count == 0)
      {
        MarketChrome.Notice(_search.Length > 0 || _categoryFilter != 0 || _status != AutoMarketPanelModel.StatusFilter.All
          ? "Empty · no bag groups match the current filters. Clear the search or choose another filter."
          : "Empty · nothing marketable is in the bags right now.", UiState.Empty);
        return;
      }
      foreach (var row in visible)
      {
        ImGui.PushID($"{row.ItemId}:{(row.Hq ? "hq" : "nq")}");
        DrawBagRow(c, row);
        ImGui.PopID();
      }
    });
  }

  private static void DrawBagRow(Configuration c, AutoMarketPanelModel.Row row)
  {
    const float pad = 12f;
    var entry = row.Kind == AutoMarketPanelModel.RowKind.OnList ? c.GetAutoMarketItem(row.ItemId, row.Hq) : null;
    var onList = entry != null;
    var pos = ImGui.GetCursorScreenPos();
    var width = MarketChrome.Avail();
    var draw = ImGui.GetWindowDrawList();
    var line = MarketChrome.Control;
    var sub = MarketChrome.LineHeight(Theme.SecondaryScale);
    var height = pad + line + 4 + sub + (onList ? 8 + line : 0) + pad;
    draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.ShellBar), 8f);
    draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }), 8f);
    var x = pos.X + pad;
    var inner = width - (2 * pad);
    var y = pos.Y + pad;

    // line 1: switch, name, Add / Remove
    var left = x;
    if (entry != null)
    {
      ImGui.SetCursorScreenPos(new Vector2(x, y));
      var on = entry.Enabled;
      if (MarketChrome.Toggle("##on", ref on)) { entry.Enabled = on; c.Save(); }
      Tip("Ticks and unticks this item on the Auto-Market list - the same switch the main window's table has. Unticked = on the list but not listed.");
      left = x + 44 + 10;
    }
    var buttonLabel = row.Kind == AutoMarketPanelModel.RowKind.NotListed ? "Add" : row.Kind == AutoMarketPanelModel.RowKind.OnList ? "Remove" : null;
    var buttonW = buttonLabel is null ? 0 : MathF.Max(72, MarketChrome.ButtonWidth(buttonLabel));
    var nameRoom = (x + inner) - left - (buttonW > 0 ? buttonW + 10 : 0);
    var name = $"{row.Name}{(row.Hq ? " (HQ)" : "")}";
    var shown = MarketChrome.FitText(name, nameRoom);
    MarketChrome.DrawText(draw, new Vector2(left, y + ((line - MarketChrome.LineHeight()) / 2f)), shown, Theme.TextPrimary);
    ImGui.SetCursorScreenPos(new Vector2(left, y));
    ImGui.Dummy(new Vector2(MathF.Max(1, nameRoom), line));
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip($"{(shown != name ? name + "\n" : string.Empty)}Item ID: {row.ItemId}  max stack {ItemNameResolver.MaxStack(row.ItemId)}  in {row.Stacks} stack(s)");
    if (buttonLabel is not null)
    {
      ImGui.SetCursorScreenPos(new Vector2(x + inner - buttonW, y));
      if (row.Kind == AutoMarketPanelModel.RowKind.NotListed)
      {
        if (ImGui.Button("Add", new Vector2(buttonW, line)))
        {
          c.GetOrAddAutoMarketItem(row.ItemId, row.Hq);
          c.Save();
        }
        Tip("Add to the Auto-Market list (default settings, enabled) - the same as right-clicking the item in your bags.");
      }
      else
      {
        if (ImGui.Button("Remove", new Vector2(buttonW, line)))
        {
          var existing = c.GetAutoMarketItem(row.ItemId, row.Hq);
          if (existing != null)
            c.AutoMarketItems.Remove(existing);
          c.Save();
        }
        Tip("Remove from the Auto-Market list. Does not cancel listings already on the retainers.");
      }
    }

    // line 2: status and quantity; the routing switch on the right for list entries
    y += line + 4;
    var (statusText, statusState) = row.Kind switch
    {
      AutoMarketPanelModel.RowKind.OnList when row.EntryEnabled => ("Ready", UiState.Ready),
      AutoMarketPanelModel.RowKind.OnList => ("Idle", UiState.Idle),
      AutoMarketPanelModel.RowKind.NotListed => ("Empty", UiState.Empty),
      _ => ("unmarketable", UiState.Idle),
    };
    var statusColor = row.Kind is AutoMarketPanelModel.RowKind.OnList or AutoMarketPanelModel.RowKind.NotListed ? Copy.MarkColor(statusState) : Theme.TextSecondary;
    MarketChrome.DrawText(draw, new Vector2(x, y), statusText, statusColor, Theme.SecondaryScale);
    var qtyX = x + MarketChrome.TextWidth(statusText, Theme.SecondaryScale) + 8;
    MarketChrome.DrawText(draw, new Vector2(qtyX, y), $"· {row.Quantity} in bags", Theme.TextSecondary, Theme.SecondaryScale);
    if (entry != null)
    {
      var label = "Skip routing";
      var labelW = MarketChrome.TextWidth(label, Theme.SecondaryScale);
      MarketChrome.DrawText(draw, new Vector2(x + inner - 44 - 8 - labelW, y), label, Theme.TextSecondary, Theme.SecondaryScale);
      ImGui.SetCursorScreenPos(new Vector2(x + inner - 44, y + (sub / 2f) - (MarketChrome.Control / 2f)));
      var skip = entry.ExcludeFromCategoryRouting;
      if (MarketChrome.Toggle("##skiprouting", ref skip)) { entry.ExcludeFromCategoryRouting = skip; c.Save(); }
      Tip("Keeps this item out of category routing. It still sells normally, from wherever it sits, on every retainer.");

      // line 3: the same clamped edits the main window's table makes
      y += sub + 8;
      var half = (inner - 8) / 2f;
      var stackLabelW = MarketChrome.TextWidth("Stack", Theme.SecondaryScale) + 8;
      MarketChrome.DrawText(draw, new Vector2(x, y + ((line - MarketChrome.LineHeight(Theme.SecondaryScale)) / 2f)), "Stack", Theme.TextSecondary, Theme.SecondaryScale);
      ImGui.SetCursorScreenPos(new Vector2(x + stackLabelW, y));
      ImGui.SetNextItemWidth(half - stackLabelW);
      var stack = entry.StackSize;
      var listingCap = MarketListingCap.For((int)ItemNameResolver.MaxStack(row.ItemId));
      if (ImGui.InputInt("##stack", ref stack, 0, 0)) { entry.StackSize = Math.Clamp(stack, 0, listingCap); c.Save(); }
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip($"Units per listing. 0 = the market's maximum ({listingCap}).");

      var keepX = x + half + 8;
      var keepLabelW = MarketChrome.TextWidth("Keep", Theme.SecondaryScale) + 8;
      MarketChrome.DrawText(draw, new Vector2(keepX, y + ((line - MarketChrome.LineHeight(Theme.SecondaryScale)) / 2f)), "Keep", Theme.TextSecondary, Theme.SecondaryScale);
      ImGui.SetCursorScreenPos(new Vector2(keepX + keepLabelW, y));
      ImGui.SetNextItemWidth(half - keepLabelW);
      var keep = entry.KeepInBags;
      if (ImGui.InputInt("##keep", ref keep, 0, 0)) { entry.KeepInBags = Math.Max(keep, 0); c.Save(); }
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Never sell below this many in your bags. 0 = sell everything.");
    }

    ImGui.SetCursorScreenPos(pos);
    ImGui.Dummy(new Vector2(width, height - ImGui.GetStyle().ItemSpacing.Y + 8));
  }

  // =====================================================================================
  // global knobs - label-for-label the Auto-Market tab's controls, writing the same fields
  // =====================================================================================

  private static bool ComboField(string label, string id, ref int index, string[] items)
  {
    var local = index;
    var changed = MarketChrome.Field(label, width => MarketChrome.Combo(id, ref local, items, width));
    if (changed)
      index = local;
    return changed;
  }

  private void DrawKnobs(Configuration c)
  {
    MarketChrome.Card("panel_settings", () =>
    {
    MarketChrome.CardHeader("Auto-Market settings", "The same saved controls as the main window; no additional selling surface.");

    var enabled = c.AutoMarketEnabled;
    var busy = _runInProgress();
    if (busy) ImGui.BeginDisabled();
    if (MarketChrome.Toggle("Enable Auto-Market", ref enabled))
    {
      if (enabled)
        _pendingPopup = 1;
      else
      {
        c.AutoMarketEnabled = false;
        c.Save();
      }
    }
    if (busy) ImGui.EndDisabled();
    TipAlways(busy
      ? "Running. Stop the current action first."
      : "Master switch. When off, the Auto Market buttons and the AutoRetainer hook do nothing.");
    var markers = c.AutoMarketMarkersEnabled;
    if (MarketChrome.Toggle("Show Auto-Market markers in bags", ref markers)) { c.AutoMarketMarkersEnabled = markers; c.Save(); }
    Tip("Draws a small green dot on every bag stack that is on the Auto-Market list and enabled.\r\nAn item on the list with its tick unticked gets no dot - it would not be listed.\r\nThis panel lists the same state as a table, even when the dots do not draw.");

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

    var src = (int)c.AutoMarketSource;
    if (ComboField("Stock source", "##lmcPanelSource", ref src, ["Bags only", "Retainer inventory only", "Bags + retainer inventory"])) { c.AutoMarketSource = (StockSource)src; c.Save(); }
    Tip("Where items may be taken from. Per-item override available in the main window's table. This panel always lists your bags.");

    var retFirst = c.AutoMarketPreferRetainerStockFirst;
    if (MarketChrome.Toggle("Retainer stock first", ref retFirst)) { c.AutoMarketPreferRetainerStockFirst = retFirst; c.Save(); }
    Tip("Sell the retainer's own inventory (venture loot) before touching your bags.");

    var partial = c.AutoMarketListPartialStacks;
    if (MarketChrome.Toggle("List partial stacks", ref partial)) { c.AutoMarketListPartialStacks = partial; c.Save(); }
    Tip("If there isn't a full listing's worth left, list what's there anyway. Off = only full-size listings.");

    var pm = (int)c.AutoMarketPriceMode;
    if (ComboField("New listing price", "##lmcPanelPriceMode", ref pm, ["Placeholder, then match on the board", "Universalis first (fallback: placeholder)"])) { c.AutoMarketPriceMode = (NewListingPriceMode)pm; c.Save(); }
    Tip("Placeholder: list at an absurd price, then immediately run the normal price match on that slot (Compare Prices).\r\n" +
        "Universalis first: ask Universalis for the data-center low before listing, so the item goes up already priced. Falls back to placeholder when Universalis has nothing.");

    var reserve = c.AutoMarketReserveSlots;
    if (MarketChrome.Field("Reserve slots", width => MarketChrome.IntInput("##lmcPanelReserve", ref reserve, width))) { c.AutoMarketReserveSlots = Math.Clamp(reserve, 0, 19); c.Save(); }
    Tip("Leave this many of the retainer's 20 market slots empty for manual listings.");

    var pinchAll = c.AutoMarketPinchAllAfter;
    if (MarketChrome.Toggle("Pinch everything after listing", ref pinchAll)) { c.AutoMarketPinchAllAfter = pinchAll; c.Save(); }
    Tip("Off (the default): after listing, only the new listings are priced - much faster.\nOn: re-price ALL of this retainer's listings as well (same as Auto Pinch), which costs a few seconds per existing listing.");

    var msgs = c.ShowAutoMarketMessages;
    if (MarketChrome.Toggle("Chat messages", ref msgs)) { c.ShowAutoMarketMessages = msgs; c.Save(); }

    if (!c.AutoMarketPinchAllAfter)
    {
      var fb = (int)c.AutoMarketPinchFallback;
      if (ComboField("If a new listing can't be found", "##lmcPanelPinchFallback", ref fb,
            ["Re-price every listing", "Leave it at the placeholder and tell me", "Re-price only my Auto-Market items"]))
      { c.AutoMarketPinchFallback = (PinchFallbackMode)fb; c.Save(); }
      Tip("Auto Market finds its new listings by reading your sell list, so this should not come up.\n" +
          "If it ever does:\n" +
          "Re-price every listing (the default): nothing is left unsellable, but listings you never asked us to touch get re-priced.\n" +
          "Leave it at the placeholder: nothing else is touched, but the new listing sits at 999,999,999 gil and will not sell until you price it or run Auto Pinch.\n" +
          "Only my Auto-Market items: re-price just the listings whose item is on the list in the main window, so a listing you made by hand is never touched.");
    }

    var sortIdx = (int)c.AutoMarketSortMode;
    if (ComboField("Listing order", "##lmcPanelSortMode", ref sortIdx, ["List order", "Cheapest first", "Fastest selling first", "Most expensive first"]))
    { c.AutoMarketSortMode = (MarketSortMode)sortIdx; c.Save(); }
    Tip("When the retainer does not have enough free market slots for everything on your Auto-Market list, this decides which items get the slots first.\r\n"
        + "Fastest selling first (the default): one Universalis lookup ranks your items by how many sell per day, so the slots go to what actually moves.\r\n"
        + "Cheapest / most expensive first: same lookup, ranked by current board price.\r\n"
        + "List order: the order of the main window's table - and the order the 'on list' rows keep here.\r\n"
        + "Items Universalis has no fresh data for keep their list position and sort last, and a failed lookup changes nothing.");

    var gate = c.AutoMarketValueGateEnabled;
    if (MarketChrome.Toggle("Only list items worth more than", ref gate)) { c.AutoMarketValueGateEnabled = gate; c.Save(); }
    var threshold = (int)Math.Min(c.AutoMarketValueGateThresholdGil, int.MaxValue);
    if (MarketChrome.Field("Value threshold", width => MarketChrome.IntInput("##lmcPanelGateThreshold", ref threshold, width, 0), "gil, net of fees")) { c.AutoMarketValueGateThresholdGil = Math.Max(threshold, 0); c.Save(); }
    Tip("Before listing, Auto-Market checks every enabled item against current Universalis prices. Items whose total sellable value "
        + "(current board price x everything it could sell of that item, after the market's 5% fee) is at or under this number take the vendor leg instead of a market slot - only NQ non-gear items the Item sheet prices as vendorable; the retainer sells them to a vendor in the same session.\r\n"
        + "0 = the switch does nothing.");

    if (c.AutoMarketValueGateEnabled)
    {
      int gateFresh = c.AutoMarketGateFreshnessHours;
      if (MarketChrome.Field("Only trust gate prices newer than", width => MarketChrome.SliderInt("##lmcPanelGateFreshness", ref gateFresh, 1, 168, width, " hours"))) { c.AutoMarketGateFreshnessHours = Math.Clamp(gateFresh, 1, 168); c.Save(); }
      Tip("Universalis is crowd-sourced and lags. Prices older than this never hold an item back - the item is listed normally. Same idea as the Auto Pinch pre-flight freshness window.");
    }

    var autoAssign = c.AutoAssignUnroutedCategories;
    if (MarketChrome.Toggle("Auto-assign uncovered categories", ref autoAssign))
    {
      c.AutoAssignUnroutedCategories = autoAssign;
      c.Save();
    }
    Tip("When a sweep finds marked Auto-Market stock in the bags whose market-board category has no rule, it assigns that category to the sweep-enabled retainer carrying the fewest routed categories and moves the stock there in the same pass. Manage the rules themselves in the main window's Category Routing section.");
    });
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
