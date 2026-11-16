using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LalaUiKit;
using LazyMarketCompanion.AutoMarket;
using System;
using System.Collections.Generic;
using System.Numerics;
using Lalalazy.Telemetry;

namespace LazyMarketCompanion.Windows;

/// <summary>
/// The Auto-Market panel: a normal-titled window that docks beside the inventory window and lists
/// every marketable stack in the player's bags, whether it is on the Auto-Market list, and quick
/// add/remove + per-item knobs - the LIST answer to the same question the bag dots answer at each
/// slot. Since 0.2.8.25 it shows nothing else: a title line with its close button, then the stacks.
/// Where the panel sits, whether it stays open, the status/category filters that narrow it, the
/// counts and the bulk add/remove actions are the main window's "Inventory panel" card
/// (<c>ConfigWindow.DrawInventoryPanelCard</c>); the global Auto-Market settings were never a
/// second surface and live only in the main window.
///
/// BEHAVIOUR (the task's contract):
/// - Docks beside the inventory window (right when there is room, else left, else floats at a
///   remembered position - <see cref="PanelDock"/>, harness case 161). It must never silently not
///   appear: an inventory window that cannot be read is just the floating case, and the open log
///   line below names what was read.
/// - Opens automatically with the inventory while dock-to-inventory is on; closes with it unless
///   pinned; <c>/lmc panel</c> toggles it any time.
/// - Lists marketable stacks only (<see cref="AutoMarketPanelModel.PanelRows"/>), narrowed by the
///   status and category filters saved in the configuration. Single-row edits are the same config
///   writes the main window's table makes (GetOrAddAutoMarketItem / AutoMarketItems.Remove), so
///   the two can never disagree.
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

  public AutoMarketPanelWindow(Inventory.InventoryService inventory, Func<bool> runInProgress)
    : base("Lazy Market Companion Auto-Market##lmcPanel")
  {
    _inventory = inventory;
    _runInProgress = runInProgress;
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

    // ---- the list, straight from the bags: marketable stacks only, narrowed by the filters saved
    // from the main window's "Inventory panel" card ----
    var all = AutoMarketBagView.ReadRows(_inventory, c);
    var visible = AutoMarketPanelModel.PanelRows(all, c.AutoMarketPanelStatus, c.AutoMarketPanelCategory);
    var filtered = AutoMarketPanelModel.ResolveVisibleStatus(c.AutoMarketPanelStatus) != AutoMarketPanelModel.StatusFilter.All
      || AutoMarketPanelModel.ResolveVisibleCategory(c.AutoMarketPanelCategory, AutoMarketPanelModel.PresentCategories(all)) != 0;

    if (!_loggedOpen)
    {
      _loggedOpen = true;
      var counts = AutoMarketPanelModel.Count(all);
      Svc.Log.Information(
        $"[LMC] Auto-Market panel opened: dock={_lastMode} pos=({windowPos.X:0.#},{windowPos.Y:0.#}) size=({windowSize.X:0.#},{windowSize.Y:0.#})" +
        $" viewport=({ImGui.GetMainViewport().Pos.X:0.#},{ImGui.GetMainViewport().Pos.Y:0.#})+({ImGui.GetMainViewport().Size.X:0.#},{ImGui.GetMainViewport().Size.Y:0.#})" +
        $" inventory={(_invOpen ? $"({_invRect.Pos.X:0.#},{_invRect.Pos.Y:0.#})+({_invRect.Size.X:0.#},{_invRect.Size.Y:0.#}) scale={_invScale:0.00}" : "not readable")}" +
        $" rows: on-list={counts.OnList} not-listed={counts.NotListed} excluded={counts.Excluded} unmarketable={counts.NotMarketable} showing={visible.Count}");
    }

    MarketChrome.HeaderBand("Auto-Market", CurrentState(c), [], out _, out _, out var closeClicked);
    if (closeClicked)
      IsOpen = false;
    if (MarketChrome.BeginBody(0f))
      DrawTable(c, visible, filtered);
    MarketChrome.EndBody();
  }

  // =====================================================================================
  // the list: one card per bag group (the Item column keeps the room it needs at any width)
  // =====================================================================================

  private static void DrawTable(Configuration c, List<AutoMarketPanelModel.Row> visible, bool filtered)
  {
    MarketChrome.Card("bag_groups", () =>
    {
      MarketChrome.CardHeader("Marketable stacks", "Live quantities and the same per-item edits as the main window.", $"{visible.Count} groups");
      if (visible.Count == 0)
      {
        MarketChrome.Notice(filtered
          ? "Empty · no marketable stacks match the status or category filter. Change it in the main window's Inventory panel card."
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
    var inner = width - (2 * pad);
    var x = pos.X + pad;

    // line 1: switch and the full-width name, wrapped to two lines instead of cut
    var left = onList ? x + 44 + 10 : x;
    var nameRoom = pos.X + pad + inner - left;
    var name = $"{row.Name}{(row.Hq ? " (HQ)" : "")}";
    var nameLines = MarketChrome.WrapText(name, nameRoom, 1f, 2, row.Hq ? MarketChrome.HqMark : "");
    var nameH = MathF.Max(onList ? line : 0, nameLines.Count * MarketChrome.LineHeight());
    var height = pad + nameH + 6 + line + (onList ? 8 + line + 8 + line : 0) + pad;
    draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.ShellBar), 8f);
    draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }), 8f);
    var y = pos.Y + pad;

    if (entry != null)
    {
      ImGui.SetCursorScreenPos(new Vector2(x, y + ((nameH - line) / 2f)));
      var on = entry.Enabled;
      if (MarketChrome.Toggle("##on", ref on)) { entry.Enabled = on; c.Save(); }
      Tip("Ticks and unticks this item on the Auto-Market list - the same switch the main window's table has. Unticked = on the list but not listed.");
    }
    var textTop = y + ((nameH - (nameLines.Count * MarketChrome.LineHeight())) / 2f);
    for (var i = 0; i < nameLines.Count; i++)
      MarketChrome.DrawText(draw, new Vector2(left, textTop + (i * MarketChrome.LineHeight())), nameLines[i], Theme.TextPrimary);
    ImGui.SetCursorScreenPos(new Vector2(left, y));
    ImGui.Dummy(new Vector2(MathF.Max(1, nameRoom), nameH));
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip($"{name}\nItem ID: {row.ItemId}  max stack {ItemNameResolver.MaxStack(row.ItemId)}  in {row.Stacks} stack(s)");

    // line 2: status and quantity on the left, Add / Remove on the right
    y += nameH + 6;
    var (statusText, statusState) = row.Kind switch
    {
      AutoMarketPanelModel.RowKind.OnList when row.EntryEnabled => ("Ready", UiState.Ready),
      AutoMarketPanelModel.RowKind.OnList => ("Idle", UiState.Idle),
      AutoMarketPanelModel.RowKind.NotListed => ("Empty", UiState.Empty),
      _ => ("unmarketable", UiState.Idle),
    };
    var statusColor = row.Kind is AutoMarketPanelModel.RowKind.OnList or AutoMarketPanelModel.RowKind.NotListed ? Copy.MarkColor(statusState) : Theme.TextSecondary;
    var textY = y + ((line - sub) / 2f);
    MarketChrome.DrawText(draw, new Vector2(x, textY), statusText, statusColor, Theme.SecondaryScale);
    var qtyX = x + MarketChrome.TextWidth(statusText, Theme.SecondaryScale) + 8;
    MarketChrome.DrawText(draw, new Vector2(qtyX, textY), $"· {row.Quantity} in bags", Theme.TextSecondary, Theme.SecondaryScale);
    var buttonLabel = row.Kind == AutoMarketPanelModel.RowKind.NotListed ? "Add" : row.Kind == AutoMarketPanelModel.RowKind.OnList ? "Remove" : null;
    if (buttonLabel is not null)
    {
      var buttonW = MathF.Max(72, MarketChrome.ButtonWidth(buttonLabel));
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

    if (entry != null)
    {
      // line 3: the same clamped edits the main window's table makes
      y += line + 8;
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

      // line 4: routing switch
      y += line + 8;
      MarketChrome.DrawText(draw, new Vector2(x, y + ((line - sub) / 2f)), "Skip category routing", Theme.TextSecondary, Theme.SecondaryScale);
      ImGui.SetCursorScreenPos(new Vector2(x + inner - 44, y));
      var skip = entry.ExcludeFromCategoryRouting;
      if (MarketChrome.Toggle("##skiprouting", ref skip)) { entry.ExcludeFromCategoryRouting = skip; c.Save(); }
      Tip("Keeps this item out of category routing. It still sells normally, from wherever it sits, on every retainer.");
    }

    ImGui.SetCursorScreenPos(pos);
    ImGui.Dummy(new Vector2(width, height - ImGui.GetStyle().ItemSpacing.Y + 8));
  }

  // =====================================================================================
  // helpers
  // =====================================================================================

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
}
