using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 57).

/// <summary>
/// Maps a bag grid's DISPLAY slots to the CONTAINER slots the game is actually drawing in them.
///
/// THE DEFECT THIS FIXES (0.1.33.0). Until 0.1.32.0 the markers assumed a bag grid's slot i shows
/// container slot i of that grid's own bag: <c>DrawForGrid</c> read <c>container-&gt;Items[i]</c> and
/// painted the verdict onto <c>grid-&gt;Slots[i]</c>. That is only true when the player's item order
/// happens to be the identity permutation. It is NOT the general case: the four bag pages are ONE
/// 140-slot ordered list as far as the UI is concerned, and the game keeps the display order in a
/// separate structure (ItemOrderModule's InventorySorter) that is free to place any container slot
/// of any page at any display position.
///
/// The visible symptom (Helm t-joey-1788992037468): with 60 stacks packed into the first two
/// on-screen blocks, the plugin still computed 7 stacks for Inventory3 and 3 for Inventory4 - which
/// is exactly what those CONTAINERS hold - and painted their dots on grids 3 and 4, which were
/// displaying nothing at all. Correctly-anchored dots floating on empty cells. The 0.1.31.0 anchor
/// fix was never the problem; the grid-to-slot binding was.
///
/// THE ORDER. ItemOrderModule.InventorySorter holds one entry per player bag slot, in display
/// order. Entry f addresses a container slot as (Page, Slot) - page 0..3 being Inventory1..4. The
/// flat entry index f is split into a display page and a display slot by ItemsPerPage, so the grid
/// showing bag index b draws entries [b*ItemsPerPage .. b*ItemsPerPage+ItemsPerPage-1], in order.
/// Ground truth, two independent production consumers of the same structure:
/// - SimpleTweaks (Caraxi) EquipFromHotbar.FindAndEquip: reads the item at
///   <c>GetInventorySlot(inventoryType + entry-&gt;Page, entry-&gt;Slot)</c> for flat index i, then
///   derives the on-screen position as <c>page = i / sorter-&gt;ItemsPerPage</c>,
///   <c>slot = i % sorter-&gt;ItemsPerPage</c> - display position from the flat index, container
///   address from the entry.
/// - CriticalCommonLib InventoryScanner (the same library GridMap.cs already cites): walks the
///   sort order and buckets by <c>index / 35</c>, reading <c>currentBag-&gt;Items[sort.slotIndex]</c>
///   from the bag named by <c>sort.containerIndex</c>.
/// CCL's own InventoryItem.BagLocation then derives the grid cell as
/// <c>(SortedSlotIndex % 5, SortedSlotIndex / 5)</c> - the SORTED index, never the container slot.
///
/// FAIL-CLOSED, exactly as GridMap does it: an unavailable or too-short order resolves NOTHING and
/// the caller draws no dots at all, rather than falling back to the identity assumption that caused
/// this defect. A wrong dot is worse than no dot.
/// </summary>
public static class SlotOrder
{
  /// <summary>One entry of the game's item order: the container slot it addresses. Page 0..3 == Inventory1..4.</summary>
  public sealed record SortEntry(int Page, int Slot);

  /// <summary>Where one display cell's item really lives: which bag page, and which slot of that page's container.</summary>
  public sealed record Cell(int BagIndex, int ContainerSlot);

  /// <summary>The player's four bag pages.</summary>
  public const int BagCount = 4;

  /// <summary>
  /// Resolve the display slots of ONE grid to the container slots they show.
  ///
  /// <paramref name="entries"/> is the sorter's flat entry list in display order (all four pages);
  /// <paramref name="itemsPerPage"/> is the sorter's own page size; <paramref name="bagIndex"/>
  /// is the bag page this grid displays (from GridMap); <paramref name="gridSlotCount"/> caps the
  /// result at the number of slot nodes the grid addon actually has.
  ///
  /// Returns display slot -&gt; container cell. Empty (fail-closed) when the order is missing, the
  /// page size is not positive, a page is out of range, or the list is too short to cover this
  /// grid's whole page - a partial page would silently mark the wrong cells for its tail.
  /// </summary>
  public static IReadOnlyDictionary<int, Cell> Resolve(
    IReadOnlyList<SortEntry>? entries, int itemsPerPage, int bagIndex, int gridSlotCount)
  {
    var empty = new Dictionary<int, Cell>();
    if (entries == null || itemsPerPage <= 0 || gridSlotCount <= 0)
      return empty;
    if (bagIndex < 0 || bagIndex >= BagCount)
      return empty;

    var start = bagIndex * itemsPerPage;
    // The whole page must be present: a truncated tail would leave real cells unresolved while the
    // resolved head looked healthy, which is precisely the half-right state this class exists to end.
    if (start < 0 || start + itemsPerPage > entries.Count)
      return empty;

    var result = new Dictionary<int, Cell>();
    var count = itemsPerPage < gridSlotCount ? itemsPerPage : gridSlotCount;
    for (var display = 0; display < count; display++)
    {
      var e = entries[start + display];
      if (e == null)
        return empty;
      if (e.Page < 0 || e.Page >= BagCount || e.Slot < 0)
        return empty;
      result[display] = new Cell(e.Page, e.Slot);
    }

    return result;
  }

  /// <summary>
  /// True when this grid's resolved order is the identity (display slot i shows slot i of its own
  /// bag) - i.e. the case the pre-0.1.33.0 code assumed was universal. Reported in the marker log
  /// line so an in-game verify can tell a genuinely unsorted bag from a permuted one.
  /// </summary>
  public static bool IsIdentity(IReadOnlyDictionary<int, Cell> map, int bagIndex)
  {
    foreach (var kv in map)
      if (kv.Value.BagIndex != bagIndex || kv.Value.ContainerSlot != kv.Key)
        return false;
    return true;
  }
}
