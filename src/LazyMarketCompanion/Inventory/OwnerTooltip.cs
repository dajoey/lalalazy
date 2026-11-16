using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using LalaUiKit;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lalalazy.Telemetry;
using LazyMarketCompanion.Windows;

namespace LazyMarketCompanion.Inventory;

/// <summary>
/// "Who handles this stack": while the game's item tooltip is up for a stack in the bags, a retainer's
/// inventory or the Armoury Chest, a small box directly under it lists every plugin that moves, sells,
/// discards or protects that item - resolved by <see cref="StackOwnership"/>, the same resolution the
/// venture-loot rails and the gear mover use. Read-only: it never touches an item.
///
/// Hover source: the game's own hovered-item id (IGameGui.HoveredItem, HQ = +1,000,000, collectable =
/// +500,000), shown only while the mouse is over one of the inventory grid addons the bag markers already
/// resolve (AutoMarketMarkers.GridNames / RetainerGridNames) or the Armoury Chest - so a hover in chat, the
/// market board or a recipe never gets the box. The markers themselves draw no-input dots and have no hover
/// path of their own; this reuses their addon lists, not their draw loop.
/// </summary>
internal sealed class OwnerTooltip : Window
{
  private static readonly string[] ExtraAddons = ["ArmouryBoard", "InventoryRetainer", "InventoryRetainerLarge", "Inventory", "InventoryLarge", "InventoryExpansion"];

  private readonly InventoryService _service;
  private readonly TelemetryGuard _guard = LalaTelemetry.CreateGuard("inventory.tooltip", "item owner tooltip");
  private ulong _lastRaw;
  private long _lastAt;
  private List<string> _lines = [];

  public OwnerTooltip(InventoryService service)
    : base("Lazy Market Companion##ownertip", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs, true)
  {
    _service = service;
    IsOpen = true;
    ShowCloseButton = false;
    RespectCloseHotkey = false;
    DisableWindowSounds = true;
    SizeConstraints = new WindowSizeConstraints { MaximumSize = new Vector2(0, 0) };
  }

  public override void Draw()
  {
    if (!Plugin.Configuration.InventoryOwnerTooltip || !_guard.TryEnter())
      return;
    try
    {
      DrawBox();
    }
    catch (Exception ex)
    {
      _guard.Failed(ex);
    }
  }

  private unsafe void DrawBox()
  {
    var raw = Svc.GameGui.HoveredItem;
    if (raw == 0 || raw >= 2_000_000)
      return; // nothing hovered, or a key/event item
    var hq = raw >= 1_000_000;
    var id = (uint)(hq ? raw - 1_000_000 : raw >= 500_000 ? raw - 500_000 : raw);
    if (id == 0)
      return;

    // Game-screen coordinates (addon X/Y) are relative to the main viewport; ImGui's mouse is absolute.
    var mouse = ImGui.GetMousePos() - ImGuiHelpers.MainViewport.Pos;
    if (!MouseOverInventory(mouse))
      return;

    var now = Environment.TickCount64;
    if (raw != _lastRaw || now - _lastAt > 1000)
    {
      _lastRaw = raw;
      _lastAt = now;
      _lines = StackOwnership.Lines(_service.Owners(id, hq));
    }

    // Under the game's own item tooltip when it is up; otherwise beside the cursor.
    var pos = mouse + new Vector2(24, 24);
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ItemDetail", out var detail) && detail->IsVisible && detail->RootNode != null)
      pos = new Vector2(detail->X, detail->Y + detail->RootNode->Height * detail->Scale + 4);

    ImGuiHelpers.ForceNextWindowMainViewport();
    ImGuiHelpers.SetNextWindowPosRelativeMainViewport(pos);
    // One container: the window itself is the card (padding, rounding, border), at a fixed content width so the
    // text wraps inside it instead of collapsing to the width of an auto-sizing region.
    const float contentWidth = 280f;
    MarketChrome.PushFrame();
    try
    {
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 14));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
      ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Surface);
      ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
      using var tip = LalaUiKit.Layout.Overlay.Begin("###LMCOwnerTip", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing,
        transparentBackground: false);
      ImGui.PopStyleColor(2);
      ImGui.PopStyleVar(3);

      var origin = ImGui.GetCursorScreenPos();
      MarketChrome.PushRight(origin.X + contentWidth);
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
      ImGui.Dummy(new Vector2(contentWidth, 0));
      var titleScale = Theme.TitleScale;
      var titleHeight = MarketChrome.LineHeight(titleScale);
      MarketChrome.Diamond(ImGui.GetWindowDrawList(), origin + new Vector2(8, titleHeight / 2f), 7, Theme.Pack(Theme.Accent));
      ImGui.SetCursorScreenPos(origin + new Vector2(24, 0));
      Widgets.Title("Who handles this stack");
      ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + titleHeight + 8));
      foreach (var line in _lines)
      {
        var warn = line.Contains("DISCARDS", StringComparison.Ordinal);
        if (warn)
        {
          ImGui.PushStyleColor(ImGuiCol.Text, Copy.MarkColor(UiState.Blocked));
          ImGui.TextWrapped(line);
          ImGui.PopStyleColor();
        }
        else ImGui.TextWrapped(line);
      }
      if (_lines.Count == 0)
        MarketChrome.Notice("Empty · nothing handles this stack.", UiState.Empty);
      if (!_service.Ar.Available)
      {
        ImGui.Dummy(new Vector2(0, 4));
        MarketChrome.Secondary(_service.Ar.Status.StartsWith("AutoRetainer", StringComparison.Ordinal) ? _service.Ar.Status : $"AutoRetainer: {_service.Ar.Status}");
      }
      ImGui.PopTextWrapPos();
      MarketChrome.PopRight();
    }
    finally
    {
      MarketChrome.PopFrame();
    }
  }

  private static unsafe bool MouseOverInventory(Vector2 mouse)
  {
    foreach (var name in AutoMarketMarkers.GridNames.Concat(AutoMarketMarkers.RetainerGridNames).Concat(ExtraAddons))
    {
      if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var a) || !a->IsVisible || a->RootNode == null)
        continue;
      var x = (float)a->X;
      var y = (float)a->Y;
      var w = a->RootNode->Width * a->Scale;
      var h = a->RootNode->Height * a->Scale;
      if (mouse.X >= x && mouse.X <= x + w && mouse.Y >= y && mouse.Y <= y + h)
        return true;
    }
    return false;
  }
}
