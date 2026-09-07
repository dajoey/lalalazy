using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyMarketCompanion.AutoMarket;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LazyMarketCompanion;

/// <summary>
/// At-a-glance Auto-Market markers on the player's bag windows (Helm t-joey-1788794153572): a small
/// green dot at the top-right corner of every bag slot whose stack has an ENABLED Auto-Market entry.
/// The marker answers "is this on the list and will the next run list it" - membership, not
/// "is it being listed this second", so a fully-listed item still shows its marker.
///
/// HOW IT DRAWS: the same positioned-ImGui-overlay machinery MarketAutomation uses for its retainer
/// buttons, applied per grid slot instead of per retainer addon. The bag windows are the
/// InventoryGrid* addons (one per open bag page, 35 DragDrop slots each, pinned from the client
/// structs); each slot's DragDrop component gives the node to position over. What to draw is decided
/// from the game's inventory CONTAINERS (InventoryManager -> Inventory1..4), never from reading
/// anything out of the grid UI - the grid is only a source of screen positions.
///
/// The same grid addons are reused by the game for the retainer's inventory view, where they show the
/// RETAINER's containers, not the player's bags. A marker there would lie, so the whole feature
/// stands down while a retainer inventory window is open.
/// </summary>
internal sealed class AutoMarketMarkers : Window, IDisposable
{
  /// <summary>The player's four base bag pages, in the game's own order.</summary>
  private static readonly InventoryType[] BagTypes =
  [
    InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
  ];

  /// <summary>Grid addons shown while browsing a retainer's inventory - markers lie there, so no draw.</summary>
  private const string RetainerInventoryAddon = "InventoryRetainer";

  /// <summary>The marker colour, as ImGui's ABGR-packed uint (a readable green).</summary>
  private const uint MarkerColorPacked = 0xFF3CE63C; // R=0x3C G=0xE6 B=0x3C A=0xFF

  /// <summary>Dot radius and corner inset, in game-scaled pixels.</summary>
  private const float DotRadius = 4.5f;
  private const float CornerInset = 7f;

  private bool _disposed;
  // Which grid addons already emitted their one INFO line this session (the grading signal).
  private readonly HashSet<string> _loggedAddons = [];
  private readonly List<MarkerMatch.Entry> _entriesScratch = [];

  public AutoMarketMarkers()
    : base("Lazy Market Companion##markers", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs, true)
  {
    Position = new Vector2(0, 0);
    IsOpen = true;
    ShowCloseButton = false;
    RespectCloseHotkey = false;
    DisableWindowSounds = true;
    SizeConstraints = new WindowSizeConstraints()
    {
      MaximumSize = new Vector2(0, 0),
    };
  }

  public override void Draw()
  {
    if (_disposed || !Plugin.Configuration.AutoMarketMarkersEnabled)
      return;

    try
    {
      unsafe
      {
        // The retainer's inventory view reuses these grid addons for the RETAINER's containers.
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(RetainerInventoryAddon, out var retainerAddon)
            && GenericHelpers.IsAddonReady(retainerAddon))
          return;

        BuildEntriesScratch();

        for (var page = 0; page < BagTypes.Length; page++)
        {
          var addonName = page switch
          {
            0 => "InventoryGrid0",
            1 => "InventoryGrid1",
            2 => "InventoryGrid0E",
            3 => "InventoryGrid1E",
            _ => null,
          };
          if (addonName == null
              || !GenericHelpers.TryGetAddonByName<AtkUnitBase>(addonName, out var addon)
              || !GenericHelpers.IsAddonReady(addon))
            continue;

          DrawForGrid(addonName, addon, BagTypes[page]);
        }
      }
    }
    catch (Exception ex)
    {
      // Markers are pure display and must never take the plugin's automation down with them.
      Svc.Log.Error(ex, "[LMC] markers: draw failed (markers suppressed this frame)");
    }
  }

  private void BuildEntriesScratch()
  {
    _entriesScratch.Clear();
    var items = Plugin.Configuration.AutoMarketItems;
    for (var i = 0; i < items.Count; i++)
    {
      var e = items[i];
      _entriesScratch.Add(new MarkerMatch.Entry(e.ItemId, e.HQ, e.Enabled));
    }
  }

  private unsafe void DrawForGrid(string addonName, AtkUnitBase* addon, InventoryType containerType)
  {
    var container = InventoryManager.Instance()->GetInventoryContainer(containerType);
    if (container == null || !container->IsLoaded)
      return;

    var grid = (AddonInventoryGrid*)addon;
    var slotCount = Math.Min(grid->Slots.Length, (int)container->Size);

    // Snapshot the marked-slot set from the CONTAINER first, then draw over the matching slot nodes.
    var stacks = new List<MarkerMatch.Stack>(slotCount);
    for (var i = 0; i < slotCount; i++)
    {
      var item = container->Items + i;
      if (item == null || item->ItemId == 0 || item->Quantity <= 0)
        continue;
      stacks.Add(new MarkerMatch.Stack(i, item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)));
    }

    var marked = MarkerMatch.MarkedStacks(_entriesScratch, stacks);
    if (marked.Count == 0)
      return;

    var drawn = 0;
    for (var i = 0; i < slotCount; i++)
    {
      if (!marked.ContainsKey(i))
        continue;

      var dragDrop = grid->Slots[i].Value;
      if (dragDrop == null)
        continue;

      var componentNode = dragDrop->AtkDragDropInterface.ComponentNode;
      if (componentNode == null)
        continue;

      var node = (AtkResNode*)componentNode;
      var position = GetNodePosition(node);
      var scale = GetNodeScale(node);
      if (scale.X <= 0 || scale.Y <= 0)
        continue;

      var size = new Vector2(node->Width, node->Height) * scale;
      if (size.X <= 0 || size.Y <= 0)
        continue;

      ImGuiHelpers.ForceNextWindowMainViewport();
      ImGuiHelpers.SetNextWindowPosRelativeMainViewport(position + new Vector2(size.X, 0f) - new Vector2(CornerInset, CornerInset));
      ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
      ImGui.Begin($"###LMCMarker{addonName}{i}", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs
        | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.AlwaysUseWindowPadding);
      var drawList = ImGui.GetWindowDrawList();
      var center = ImGui.GetCursorScreenPos() + new Vector2(DotRadius, DotRadius);
      drawList.AddCircleFilled(center, DotRadius * scale.X, MarkerColorPacked);
      ImGui.End();
      ImGui.PopStyleColor();
      drawn++;
    }

    // The one INFO line per grid addon per session - how the in-game verify is graded from ffxivdb.
    if (drawn > 0 && _loggedAddons.Add(addonName))
      Svc.Log.Information($"[LMC] markers: {drawn} marked of {stacks.Count} stacks on {addonName}");
  }

  private static unsafe System.Numerics.Vector2 GetNodePosition(AtkResNode* node)
  {
    var pos = new System.Numerics.Vector2(node->X, node->Y);
    var par = node->ParentNode;
    while (par != null)
    {
      pos *= new System.Numerics.Vector2(par->ScaleX, par->ScaleY);
      pos += new System.Numerics.Vector2(par->X, par->Y);
      par = par->ParentNode;
    }
    return pos;
  }

  private static unsafe System.Numerics.Vector2 GetNodeScale(AtkResNode* node)
  {
    if (node == null) return new System.Numerics.Vector2(1, 1);
    var scale = new System.Numerics.Vector2(node->ScaleX, node->ScaleY);
    while (node->ParentNode != null)
    {
      node = node->ParentNode;
      scale *= new System.Numerics.Vector2(node->ScaleX, node->ScaleY);
    }
    return scale;
  }

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
  }
}
