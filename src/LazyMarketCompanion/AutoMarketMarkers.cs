using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LazyMarketCompanion;

/// <summary>
/// At-a-glance Auto-Market markers on the player's bag windows (Helm t-joey-1788794153572): a small
/// dot at the top-right corner of every bag slot whose stack is market-relevant.
///
/// TWO STATES, one per kind of stack (0.1.21.0, per Joey: "if it be put on the marketboard at all
/// ever, it should have an indicator on it saying whether it's on my automarket list or not"):
/// - GREEN dot: this stack is on the Auto-Market list (enabled) - the next Auto-Market run would
///   list it. This is the original 0.1.17.0 marker meaning, unchanged.
/// - GREY dot: this stack COULD be put on the market board but is NOT on the Auto-Market list.
/// - NO dot: the item cannot be put on the market board at all (untradable, no search category).
/// Marketability is the same test the inventory context menu uses
/// (<c>!item.IsUntradable &amp;&amp; item.ItemSearchCategory.RowId != 0</c>, read once per item id
/// per draw from the Item sheet). On/off-listing is keyed on item id and HQ flag exactly as the
/// config stores it, so HQ and NQ of the same item can disagree - correctly.
/// The marker answers "is this on the list", not "is it being listed this second", so a
/// fully-listed item still shows its green marker.
///
/// HOW IT DRAWS: the same positioned-ImGui-overlay machinery MarketAutomation uses for its retainer
/// buttons, applied per grid slot instead of per retainer addon. The bag windows are the
/// InventoryGrid* addons (35 DragDrop slots each, pinned from the client structs); each slot's
/// DragDrop component gives the node to position over. What to draw is decided from the game's
/// inventory CONTAINERS (InventoryManager -> Inventory1..4), never from reading anything out of
/// the grid UI - the grid is only a source of screen positions.
///
/// WHICH container a grid is showing is NOT a property of its name (0.1.17.0 assumed it was):
/// the expanded view pairs each E-grid with a page by name identity, while the tabbed view's
/// single panel follows the parent Inventory window's selected tab. GridMap.cs owns that pairing;
/// anything it cannot resolve draws nothing at all.
///
/// The same grid addons are reused by the game for the retainer's inventory view, where they show the
/// RETAINER's containers, not the player's bags. A marker there would lie, so the whole feature
/// stands down while a retainer inventory window is open.
///
/// VISIBILITY (0.1.29.0): the 7.x "Inventory" window (AddonInventoryExpansion) owns all grids as
/// child addons - the four E-grids, key-items "event" grids, and the crystal grid. A page switch
/// toggles ChildAddonInfo control flags (+0x40/+0x41) and unit bookkeeping bytes, but NEVER clears
/// the hidden grids' root-node NodeFlags.Visible (live AddonInventoryExpansion.SetTab disassembly,
/// 2026-09-09). Because of this, node visibility cannot distinguish between pages, causing
/// 0.1.24.0-0.1.28.0 to draw bag dots over the "Key Items & Crystals" page.
/// Since 0.1.29.0, the gate reads the page state directly from the parent expansion window's
/// TabIndex (+0x340; 0 = Items, 1 = Key Items & Crystals). The expanded bag grids are admitted only
/// when the parent window is live, ready, and on tab 0 (fail-closed: an unresolvable parent or
/// any other page suppresses all E-grid markers). The root-node Visible check remains only as a
/// secondary guard.
/// </summary>
internal sealed class AutoMarketMarkers : Window, IDisposable
{
  /// <summary>The player's four base bag pages, in the game's own order; index == GridMap bag index.</summary>
  private static readonly InventoryType[] BagTypes =
  [
    InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
  ];

  /// <summary>Every bag-grid addon name the client structs register, both display modes.</summary>
  private static readonly string[] GridNames =
  [
    "InventoryGrid", "InventoryGrid0", "InventoryGrid1",
    "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
  ];

  /// <summary>Grid addons shown while browsing a retainer's inventory - markers lie there, so no draw.</summary>
  private const string RetainerInventoryAddon = "InventoryRetainer";

  /// <summary>The tabbed-mode parent window whose TabIndex says which bag the panel shows.</summary>
  private const string ParentInventoryAddon = "Inventory";

  /// <summary>
  /// The 7.x expanded parent (the window titled "Inventory" with the Items / Key Items &amp; Crystals
  /// tab headers; AddonInventoryExpansion in ClientStructs). It owns every grid as a child addon and
  /// hides a hidden page's grids at the NODE level, not the addon level.
  /// </summary>
  private const string InventoryExpansionAddon = "InventoryExpansion";

  /// <summary>Green dot: this stack is on the Auto-Market list (ImGui ABGR-packed; a readable green).</summary>
  private const uint OnListColorPacked = 0xFF3CE63C; // R=0x3C G=0xE6 B=0x3C A=0xFF

  /// <summary>Grey dot: marketable but not on the Auto-Market list (ImGui ABGR-packed; a muted grey).</summary>
  private const uint MarketableNotListedColorPacked = 0xFF84888C; // R=0x8C G=0x88 B=0x84 A=0xFF

  /// <summary>Dot radius and corner inset, in game-scaled pixels.</summary>
  private const float DotRadius = 4.5f;
  private const float CornerInset = 7f;

  private bool _disposed;
  // Which (grid addon, container) pairs already emitted their one INFO line this session (the grading signal).
  private readonly HashSet<string> _loggedAddons = [];
  // Set once per session when the page gate suppresses every E-grid (the Key Items & Crystals grading signal).
  private readonly HashSet<string> _loggedPageSkip = [];
  private readonly List<MarkerMatch.Entry> _entriesScratch = [];
  // Item ids seen stable-market this draw, carried across draws so a stable call is made once per id.
  private readonly HashSet<uint> _marketableScratch = [];

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

        // 0.1.25.0 visibility gate. The expanded parent owns every grid as a child addon; switching
        // to the "Key Items & Crystals" page hides the bag grids' ROOT NODE but leaves their addons
        // live, ready, and (misleadingly) AtkUnitBase-IsVisible. Resolve the bindings as before,
        // then skip any binding whose grid's root node is not Visible this frame (per-binding gate
        // in the draw loop below). The gate is unconditional: it never depends on the InventoryExpansion
        // parent resolving this frame, so there is no "parent missing -> draw anyway" arm. A frame
        // where the whole window is closed still resolves normally - TryGetAddonByName simply stops
        // finding the child grids once the parent is gone.

        BuildEntriesScratch();

        // Collect the live grids first: which container a grid shows is decided by MODE, not by
        // name alone (GridMap.cs) - the expanded view pairs E-grids by name identity, the tabbed
        // view binds every live panel to the parent window's selected tab.
        var live = new List<string>();
        foreach (var name in GridNames)
        {
          if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var gridAddon)
              && GenericHelpers.IsAddonReady(gridAddon))
            live.Add(name);
        }
        if (live.Count == 0)
          return;

        int? tabIndex = null;
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(ParentInventoryAddon, out var parent)
            && GenericHelpers.IsAddonReady(parent))
          tabIndex = ((AddonInventory*)parent)->TabIndex;

        // 0.1.29.0 page gate. The four E-grids are child addons of the expanded "Inventory" window and
        // exist only on its Items page - but a page switch never clears their root-node Visible flag
        // (live SetTab disassembly: it flips ChildAddonInfo/unit bookkeeping flags instead), so node
        // visibility cannot tell the pages apart and 0.1.24.0-0.1.28.0 drew the bag dots over whatever
        // page was displayed. The window's own TabIndex (+0x340; 0 = Items, 1 = Key Items & Crystals)
        // is the only reliable page state. Fail-closed: a parent that cannot be resolved or is not
        // ready while E-grids are live suppresses every E-grid - no page proof, no dots.
        var bindings = GridMap.Resolve(live, tabIndex);
        var anyExpanded = false;
        foreach (var b in bindings)
          if (GridMap.IsExpandedGrid(b.GridName))
          {
            anyExpanded = true;
            break;
          }

        var bagsPage = true;
        if (anyExpanded)
        {
          var parentReady = GenericHelpers.TryGetAddonByName<AtkUnitBase>(InventoryExpansionAddon, out var expansion)
              && GenericHelpers.IsAddonReady(expansion);
          bagsPage = parentReady && GridMap.ExpandedBagsPageShown(parentReady, ((AddonInventoryExpansion*)expansion)->TabIndex);
          if (!bagsPage && _loggedPageSkip.Add(InventoryExpansionAddon))
            Svc.Log.Information("[LMC] markers: expanded inventory not on the Items page - bag-grid markers suppressed");
        }

        foreach (var binding in bindings)
        {
          if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(binding.GridName, out var addon)
              || !GenericHelpers.IsAddonReady(addon))
            continue;

          // THE 0.1.25.0 GATE: a hidden child grid keeps its addon alive and its container loaded,
          // but its root node's Visible flag is off while the player is on another page. Drawing
          // over it put the dots on whatever page was actually displayed (the Key Items & Crystals
          // bleed-through). AtkResNode::IsVisible reads NodeFlags.Visible down the root node -
          // the same flag the game itself toggles per page. UNCONDITIONAL per resolved binding:
          // 0.1.24.0 keyed this on the InventoryExpansion parent being live, which let a frame with
          // a missing/not-ready parent draw over every resolved grid un-checked.
          if (!addon->RootNode->IsVisible())
            continue;

          // 0.1.29.0: on any page other than Items, an E-grid wears no dots at all.
          if (GridMap.IsExpandedGrid(binding.GridName) && !bagsPage)
            continue;

          DrawForGrid(binding.GridName, addon, BagTypes[binding.BagIndex]);
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

  /// <summary>
  /// Game-side half of the second state: is this item marketable at all? Same test the inventory
  /// context menu uses (Plugin.cs): not untradable AND it has a market-board search category. The
  /// Item sheet read is per unique id per container, and only for stacks not already on the list
  /// (an on-list item is green whatever its tradability - a config-entry bug is shown, not hidden,
  /// exactly as 0.1.17.0 decided).
  /// </summary>
  private void BuildMarketableScratch(IEnumerable<MarkerMatch.Stack> stacks)
  {
    _marketableScratch.Clear();
    var sheet = Plugin.DataManager?.GetExcelSheet<Item>();
    if (sheet == null)
      return;
    foreach (var s in stacks)
    {
      if (_marketableScratch.Contains(s.ItemId))
        continue;
      if (!sheet.TryGetRow(s.ItemId, out var item))
        continue;
      if (!item.IsUntradable && item.ItemSearchCategory.RowId != 0)
        _marketableScratch.Add(s.ItemId);
    }
  }

  private unsafe void DrawForGrid(string addonName, AtkUnitBase* addon, InventoryType containerType)
  {
    var container = InventoryManager.Instance()->GetInventoryContainer(containerType);
    if (container == null || !container->IsLoaded)
      return;

    var grid = (AddonInventoryGrid*)addon;
    var slotCount = Math.Min(grid->Slots.Length, (int)container->Size);

    // Snapshot the classified-slot set from the CONTAINER first, then draw over the matching slot nodes.
    var stacks = new List<MarkerMatch.Stack>(slotCount);
    for (var i = 0; i < slotCount; i++)
    {
      var item = container->Items + i;
      if (item == null || item->ItemId == 0 || item->Quantity <= 0)
        continue;
      stacks.Add(new MarkerMatch.Stack(i, item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)));
    }

    // Marketability feeds the grey state: one sheet read per unique id in this container. An on-list
    // item is green whether or not the sheet calls it tradable (a config-entry bug is shown, not
    // hidden - the 0.1.17.0 honesty rule); only the GREY state requires real marketability.
    BuildMarketableScratch(stacks);
    var classified = MarkerMatch.Classify(_entriesScratch, stacks, _marketableScratch);
    if (classified.Count == 0)
      return;

    var drawnOnList = 0;
    var drawnNotListed = 0;
    for (var i = 0; i < slotCount; i++)
    {
      if (!classified.TryGetValue(i, out var entry))
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

      var color = entry.Kind switch
      {
        MarkerMatch.MarkKind.OnList => OnListColorPacked,
        MarkerMatch.MarkKind.MarketableNotListed => MarketableNotListedColorPacked,
        _ => 0u,
      };
      if (color == 0u)
        continue;
      if (entry.Kind == MarkerMatch.MarkKind.OnList) drawnOnList++; else drawnNotListed++;

      ImGuiHelpers.ForceNextWindowMainViewport();
      ImGuiHelpers.SetNextWindowPosRelativeMainViewport(position + new Vector2(size.X, 0f) - new Vector2(CornerInset, CornerInset));
      ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
      // 0.1.22.0: zero padding/border like MarketAutomation.ImGuiSetup - the default padding shifted every dot a full padding-size off its cell corner onto the neighbour cell (dots on empty slots in half-empty bags).
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
      ImGui.Begin($"###LMCMarker{addonName}{i}", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs
        | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.AlwaysUseWindowPadding);
      var drawList = ImGui.GetWindowDrawList();
      var center = ImGui.GetCursorScreenPos() + new Vector2(DotRadius, DotRadius);
      drawList.AddCircleFilled(center, DotRadius * scale.X, color);
      ImGui.End();
      ImGui.PopStyleVar(2);
      ImGui.PopStyleColor();
    }

    // The one INFO line per (grid addon, container) pairing per session - how the in-game verify
    // is graded from ffxivdb. Naming the container is the point: it proves WHICH bag the dots
    // were computed from, which is exactly what 0.1.17.0 got wrong. 0.1.21.0: the line now
    // separates the two marker colours.
    if ((drawnOnList > 0 || drawnNotListed > 0) && _loggedAddons.Add($"{addonName}:{containerType}"))
      Svc.Log.Information($"[LMC] markers: {drawnOnList} on-list (green) + {drawnNotListed} marketable not listed (grey) of {stacks.Count} stacks on {addonName} ({containerType})");
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
