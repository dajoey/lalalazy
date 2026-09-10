using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LazyMarketCompanion;

/// <summary>
/// At-a-glance Auto-Market markers on the player's bag windows (Helm t-joey-1788794153572): a small
/// dot inside the top-right corner of every bag slot whose stack is market-relevant. Since 0.1.31.0
/// the dot's center is anchored INSIDE the slot's cell; until 0.1.30.0 it hung off the cell's
/// top-right corner and read as a dot on a nearby slot.
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
/// buttons, applied per grid slot instead of per retainer addon. The dot's anchor is pinned in
/// MarkerAnchor (0.1.31.0): center CornerInset px in from the cell's right edge and CornerInset px
/// below its top edge, inside the cell. The bag windows are the
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
/// AND WHICH SLOT of that page a grid CELL is showing is not the cell's own index (0.1.32.0 and
/// earlier assumed it was). The bag UI draws the four pages from the game's own item order
/// (ItemOrderModule's player-inventory sorter), which may put any container slot of any page at any
/// display position. Reading container slot i and painting the result on grid cell i therefore
/// marked the right stacks in the wrong places: with 60 stacks packed into the first two on-screen
/// blocks, the dots computed for Inventory3/Inventory4 landed on two grids that were displaying
/// nothing at all - correctly-anchored dots floating on empty cells (Helm t-joey-1788992037468).
/// Since 0.1.33.0, SlotOrder.cs resolves each grid's display slots to the container slots the game
/// is actually drawing in them, and an unreadable order suppresses that grid's dots entirely.
///
/// The same grid addons are reused by the game for the retainer's inventory view, where they show the
/// RETAINER's containers, not the player's bags. Since 0.1.34.0 the feature no longer stands down
/// there: while InventoryRetainer/InventoryRetainerLarge is open, the SAME two-state marker is drawn
/// against the active retainer's own stock instead of the player's bags (Helm t-joey-1789056199442:
/// "now we need to make the dots work on retainer inventory"). RetainerGridMap.cs resolves the live
/// grid to a retainer PAGE via the retainer addon's TabIndex (0-6, up to 7 pages -
/// InventoryType.RetainerPage1..7, not the player's fixed 4), and the display order for that page
/// comes from ItemOrderModule.GetActiveRetainerSorter() - the same per-frame SlotOrder.Resolve
/// machinery the player path uses, generalised to a 7-page range. Both retainer addons are assumed
/// single-panel/tabbed (mirroring the player's tabbed "Inventory" shape, not the four-grid expanded
/// one) per a MetadataLoadContext field probe - unconfirmed in game, so an E-grid addon appearing
/// live while a retainer window is open resolves to nothing rather than a guessed page.
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

  /// <summary>A retainer's up to seven storage pages, in the game's own order; index == RetainerGridMap page index.</summary>
  private static readonly InventoryType[] RetainerPageTypes =
  [
    InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
    InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
  ];

  /// <summary>Grid addons shown while browsing a retainer's inventory - the RETAINER read path below.</summary>
  private const string RetainerInventoryAddon = "InventoryRetainer";

  /// <summary>The second retainer inventory addon shape (larger capacity); same TabIndex/SetTab shape.</summary>
  private const string RetainerInventoryLargeAddon = "InventoryRetainerLarge";

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

  /// <summary>Dot radius and corner inset, in game-scaled pixels - the anchor arithmetic lives in MarkerAnchor (0.1.31.0).</summary>
  private const float DotRadius = MarkerAnchor.Radius;
  private const float CornerInset = MarkerAnchor.Inset;

  private bool _disposed;
  // Which (grid addon, container) pairs already emitted their one INFO line this session (the grading signal).
  private readonly HashSet<string> _loggedAddons = [];
  // Set once per session when the page gate suppresses every E-grid (the Key Items & Crystals grading signal).
  private readonly HashSet<string> _loggedPageSkip = [];
  // Set once per grid when its display order could not be resolved (fail-closed grading signal).
  private readonly HashSet<string> _loggedOrderMissing = [];
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
        // The retainer's inventory view reuses these same grid addons for the RETAINER's
        // containers (0.1.34.0: this is now a BRANCH, not an early-return - see the class remarks
        // above). InventoryRetainer wins if both are somehow ready; large-capacity retainers use
        // InventoryRetainerLarge instead of a second concurrent panel, never both at once.
        var retainerReady = GenericHelpers.TryGetAddonByName<AtkUnitBase>(RetainerInventoryAddon, out var retainerAddon)
            && GenericHelpers.IsAddonReady(retainerAddon);
        AtkUnitBase* retainerLargeAddon = null;
        var retainerLargeReady = !retainerReady
            && GenericHelpers.TryGetAddonByName<AtkUnitBase>(RetainerInventoryLargeAddon, out retainerLargeAddon)
            && GenericHelpers.IsAddonReady(retainerLargeAddon);

        if (retainerReady || retainerLargeReady)
        {
          DrawRetainerMarkers(retainerReady ? retainerAddon : retainerLargeAddon, retainerReady);
          return;
        }

        // 0.1.25.0 visibility gate. The expanded parent owns every grid as a child addon; switching
        // to the "Key Items & Crystals" page hides the bag grids' ROOT NODE but leaves their addons
        // live, ready, and (misleadingly) AtkUnitBase-IsVisible. Resolve the bindings as before,
        // then skip any binding whose grid's root node is not Visible this frame (per-binding gate
        // in the draw loop below). The gate is unconditional: it never depends on the InventoryExpansion
        // parent resolving this frame, so there is no "parent missing -> draw anyway" arm. A frame
        // where the whole window is closed still resolves normally - TryGetAddonByName simply stops
        // finding the child grids once the parent is gone.

        BuildEntriesScratch();

        // 0.1.33.0: one read of the game's own display order per frame, shared by every grid.
        // Without it the markers assumed display slot i == container slot i, which put correctly
        // computed dots on grids that were displaying nothing.
        var orderSnapshot = ReadSlotOrder();

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

          DrawForGrid(binding.GridName, addon, binding.BagIndex, orderSnapshot);
        }
      }
    }
    catch (Exception ex)
    {
      // Markers are pure display and must never take the plugin's automation down with them.
      Svc.Log.Error(ex, "[LMC] markers: draw failed (markers suppressed this frame)");
    }
  }

  /// <summary>
  /// The retainer-inventory counterpart of the main Draw() body (0.1.34.0). Both retainer addons
  /// (InventoryRetainer / InventoryRetainerLarge) share the same field layout up to and including
  /// TabIndex - confirmed by direct field enumeration, not assumed - so only the cast differs.
  /// </summary>
  private unsafe void DrawRetainerMarkers(AtkUnitBase* retainerAddon, bool isNormalRetainer)
  {
    var tabIndex = isNormalRetainer
      ? ((AddonInventoryRetainer*)retainerAddon)->TabIndex
      : ((AddonInventoryRetainerLarge*)retainerAddon)->TabIndex;

    BuildEntriesScratch();

    // Same per-frame-shared-order idea as the player path (0.1.33.0), against the ACTIVE
    // RETAINER's own sorter instead of the player's InventorySorter. ItemOrderModule tracks which
    // retainer is active via ActiveRetainerId and keys RetainerSorter by that id, so this always
    // reads the sorter for whichever retainer's inventory window is open this frame - never a
    // stale "last retainer interacted with" value.
    var orderSnapshot = ReadRetainerSlotOrder();

    var live = new List<string>();
    foreach (var name in GridNames)
    {
      // E-grids are the player's expanded-armoire-chest mode; no known retainer equivalent exists
      // (both retainer addons expose only a single TabIndex-selected panel, per the field probe in
      // the class remarks). RetainerGridMap.Resolve below only binds the three normal-mode panel
      // names, so a stray live E-grid here is simply never bound to anything.
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var gridAddon)
          && GenericHelpers.IsAddonReady(gridAddon))
        live.Add(name);
    }
    if (live.Count == 0)
      return;

    var bindings = RetainerGridMap.Resolve(live, tabIndex);
    foreach (var binding in bindings)
    {
      if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(binding.GridName, out var addon)
          || !GenericHelpers.IsAddonReady(addon))
        continue;
      if (!addon->RootNode->IsVisible())
        continue;

      DrawForRetainerGrid(binding.GridName, addon, binding.PageIndex, orderSnapshot);
    }
  }

  /// <summary>
  /// Read the ACTIVE RETAINER's item order (0.1.34.0), mirroring ReadSlotOrder for the player. The
  /// retainer may have up to seven storage pages (RetainerGridMap.PageCount) rather than the
  /// player's fixed four - SlotOrder.ResolveForPageCount takes that as an explicit parameter so the
  /// same fail-closed resolution logic serves both without duplicating it.
  /// </summary>
  private static unsafe SlotOrderSnapshot ReadRetainerSlotOrder()
  {
    var module = ItemOrderModule.Instance();
    if (module == null)
      return new SlotOrderSnapshot(null, 0);

    var sorter = module->GetActiveRetainerSorter();
    if (sorter == null || sorter->ItemsPerPage <= 0)
      return new SlotOrderSnapshot(null, 0);

    var count = (int)sorter->Items.LongCount;
    if (count <= 0)
      return new SlotOrderSnapshot(null, 0);

    var entries = new List<SlotOrder.SortEntry>(count);
    for (var i = 0; i < count; i++)
    {
      var entry = sorter->Items[i].Value;
      if (entry == null)
        return new SlotOrderSnapshot(null, 0);
      entries.Add(new SlotOrder.SortEntry(entry->Page, entry->Slot));
    }

    return new SlotOrderSnapshot(entries, sorter->ItemsPerPage);
  }

  /// <summary>
  /// One frame's read of the game's player-inventory item order (0.1.33.0). <see cref="Entries"/> is
  /// the sorter's flat entry list in DISPLAY order across all four bag pages; <see cref="ItemsPerPage"/>
  /// is the sorter's own page size. Null entries mean the order could not be read this frame, and
  /// every grid then draws nothing (fail-closed).
  /// </summary>
  private readonly struct SlotOrderSnapshot(List<SlotOrder.SortEntry>? entries, int itemsPerPage)
  {
    public List<SlotOrder.SortEntry>? Entries { get; } = entries;
    public int ItemsPerPage { get; } = itemsPerPage;
  }

  /// <summary>
  /// Read the player-inventory sorter into a plain list. The sorter is the game's own display order
  /// for the four bag pages: entry f addresses container slot (Page, Slot), and its DISPLAY position
  /// is f split by ItemsPerPage. Two production consumers read it exactly this way - SimpleTweaks'
  /// EquipFromHotbar (page = i / ItemsPerPage, slot = i % ItemsPerPage) and CriticalCommonLib's
  /// InventoryScanner (buckets by index / 35, reads bag[containerIndex].Items[slotIndex]).
  ///
  /// A sort in progress (SortFunctionIndex != -1 / PercentComplete != 100) is NOT rejected: the
  /// entry list stays well-formed while the game re-orders it, and rejecting it would blink every
  /// dot off mid-sort. A missing module or an empty list resolves nothing, and the callers suppress
  /// their markers rather than falling back to the identity assumption that caused this defect.
  /// </summary>
  private static unsafe SlotOrderSnapshot ReadSlotOrder()
  {
    var module = ItemOrderModule.Instance();
    if (module == null)
      return new SlotOrderSnapshot(null, 0);

    var sorter = module->InventorySorter;
    if (sorter == null || sorter->ItemsPerPage <= 0)
      return new SlotOrderSnapshot(null, 0);

    var count = (int)sorter->Items.LongCount;
    if (count <= 0)
      return new SlotOrderSnapshot(null, 0);

    var entries = new List<SlotOrder.SortEntry>(count);
    for (var i = 0; i < count; i++)
    {
      var entry = sorter->Items[i].Value;
      if (entry == null)
        return new SlotOrderSnapshot(null, 0);
      entries.Add(new SlotOrder.SortEntry(entry->Page, entry->Slot));
    }

    return new SlotOrderSnapshot(entries, sorter->ItemsPerPage);
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

  private unsafe void DrawForGrid(string addonName, AtkUnitBase* addon, int bagIndex, SlotOrderSnapshot order)
  {
    var grid = (AddonInventoryGrid*)addon;
    var slotCount = grid->Slots.Length;

    // 0.1.33.0: the grid's DISPLAY slots are resolved to the container slots the game is actually
    // drawing in them (SlotOrder). Until 0.1.32.0 this read container->Items[i] and painted the
    // verdict onto grid->Slots[i], which is only right when the item order is the identity
    // permutation - hence dots on grids that were displaying nothing at all.
    var map = SlotOrder.Resolve(order.Entries, order.ItemsPerPage, bagIndex, slotCount);
    if (map.Count == 0)
    {
      // Fail-closed, as GridMap does: no proven order, no dots. Logged once so an in-game verify
      // can tell "the order was unreadable" apart from "there was nothing to mark".
      if (_loggedOrderMissing.Add(addonName))
        Svc.Log.Information($"[LMC] markers: no item order resolved for {addonName} (bag {bagIndex}) - markers suppressed for it");
      return;
    }

    // Snapshot the classified set from the CONTAINERS, keyed by the DISPLAY slot that shows each
    // stack - so the dot lands on the cell the player is actually looking at.
    var inventory = InventoryManager.Instance();
    if (inventory == null)
      return;

    var stacks = new List<MarkerMatch.Stack>(slotCount);
    foreach (var kv in map)
    {
      var container = inventory->GetInventoryContainer(BagTypes[kv.Value.BagIndex]);
      if (container == null || !container->IsLoaded || kv.Value.ContainerSlot >= (int)container->Size)
        continue;
      var item = container->Items + kv.Value.ContainerSlot;
      if (item == null || item->ItemId == 0 || item->Quantity <= 0)
        continue;
      stacks.Add(new MarkerMatch.Stack(kv.Key, item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)));
    }

    RenderDots(addonName, grid, slotCount, stacks, BagTypes[bagIndex].ToString(), SlotOrder.IsIdentity(map, bagIndex));
  }

  /// <summary>
  /// The retainer counterpart of <see cref="DrawForGrid"/> (0.1.34.0): same slot-order-resolve then
  /// classify-then-draw shape, reading the ACTIVE RETAINER's containers
  /// (InventoryType.RetainerPage1..7) via <see cref="RetainerGridMap"/> / <see cref="SlotOrder.ResolveForPageCount"/>
  /// instead of the player's four bags. <see cref="MarkerMatch.Classify"/> and the draw loop are
  /// item-agnostic - they take stacks and Auto-Market list entries, and neither cares which
  /// container the stack came from - so only the container-read half differs from the player path.
  /// </summary>
  private unsafe void DrawForRetainerGrid(string addonName, AtkUnitBase* addon, int pageIndex, SlotOrderSnapshot order)
  {
    var grid = (AddonInventoryGrid*)addon;
    var slotCount = grid->Slots.Length;

    var map = SlotOrder.ResolveForPageCount(order.Entries, order.ItemsPerPage, pageIndex, slotCount, RetainerGridMap.PageCount);
    if (map.Count == 0)
    {
      if (_loggedOrderMissing.Add(addonName))
        Svc.Log.Information($"[LMC] markers: no item order resolved for {addonName} (retainer page {pageIndex}) - markers suppressed for it");
      return;
    }

    var inventory = InventoryManager.Instance();
    if (inventory == null)
      return;

    var stacks = new List<MarkerMatch.Stack>(slotCount);
    foreach (var kv in map)
    {
      if (kv.Value.BagIndex < 0 || kv.Value.BagIndex >= RetainerPageTypes.Length)
        continue;
      var container = inventory->GetInventoryContainer(RetainerPageTypes[kv.Value.BagIndex]);
      if (container == null || !container->IsLoaded || kv.Value.ContainerSlot >= (int)container->Size)
        continue;
      var item = container->Items + kv.Value.ContainerSlot;
      if (item == null || item->ItemId == 0 || item->Quantity <= 0)
        continue;
      stacks.Add(new MarkerMatch.Stack(kv.Key, item->ItemId, item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)));
    }

    RenderDots(addonName, grid, slotCount, stacks, RetainerPageTypes[pageIndex].ToString(), SlotOrder.IsIdentity(map, pageIndex));
  }

  /// <summary>
  /// Classify and draw dots for one grid's already-read stacks (shared by <see cref="DrawForGrid"/>
  /// and <see cref="DrawForRetainerGrid"/> - the game's grid nodes, the marker anchor math, and the
  /// classify predicate are identical for a player bag and a retainer page; only which container the
  /// stacks came from differs, and that has already happened by the time this runs).
  /// </summary>
  private unsafe void RenderDots(string addonName, AddonInventoryGrid* grid, int slotCount, List<MarkerMatch.Stack> stacks, string containerLabel, bool orderIsIdentity)
  {
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
      // 0.1.31.0: the dot is anchored INSIDE the cell - center inset from the right edge and inset
      // below the top edge (MarkerAnchor.Center). Until 0.1.30.0 the window sat at the cell's
      // top-right corner minus the inset in BOTH axes (top - inset), so the dot's center was
      // 2.5 px ABOVE the cell's top edge and most of the circle hung outside the cell - on the
      // stacked E-grids it read as a dot on the grid above (a different bag), in sparse bags as
      // a dot on the cell above ("seemingly random locations", Helm t-joey-1788992037468).
      ImGuiHelpers.SetNextWindowPosRelativeMainViewport(MarkerAnchor.WindowPosition(position, size));
      ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
      // 0.1.22.0: zero padding/border like MarketAutomation.ImGuiSetup - the default padding shifted every dot a full padding-size off its cell corner onto the neighbour cell (dots on empty slots in half-empty bags). 0.1.31.0: with the anchor now absolute (MarkerAnchor), zeroed padding is belt-and-braces rather than load-bearing.
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
      ImGui.Begin($"###LMCMarker{addonName}{i}", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs
        | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.AlwaysUseWindowPadding);
      var drawList = ImGui.GetWindowDrawList();
      // Absolute anchor, not cursor-relative: the center is exactly MarkerAnchor.Center(position,
      // size) whatever the ImGui style state is (the 0.1.22.0 padding fix made cursor == window
      // pos, but that equality is an assumption about style, not a position).
      var center = MarkerAnchor.Center(position, size);
      drawList.AddCircleFilled(center, DotRadius * scale.X, color);
      ImGui.End();
      ImGui.PopStyleVar(2);
      ImGui.PopStyleColor();
    }

    // The one INFO line per (grid addon, container) pairing per session - how the in-game verify
    // is graded from ffxivdb. Naming the container is the point: it proves WHICH bag/page the dots
    // were computed from, which is exactly what 0.1.17.0 got wrong. 0.1.21.0: the line now
    // separates the two marker colours. 0.1.33.0: it also reports whether this grid's display
    // order was the identity permutation - the assumption that produced dots on empty grids.
    if ((drawnOnList > 0 || drawnNotListed > 0) && _loggedAddons.Add($"{addonName}:{containerLabel}"))
      Svc.Log.Information($"[LMC] markers: {drawnOnList} on-list (green) + {drawnNotListed} marketable not listed (grey) of {stacks.Count} stacks on {addonName} ({containerLabel}), order={(orderIsIdentity ? "identity" : "sorted")}");
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
