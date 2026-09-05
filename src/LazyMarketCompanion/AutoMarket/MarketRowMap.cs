using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Maps RetainerSellList UI rows to RetainerMarket container slots, and — the point of this class —
/// lets the caller CHECK that mapping before it clicks anything.
///
/// The pinch chain addresses listings by UI row, but Auto-Market knows its new listings by container
/// slot, so "price only what I just listed" has to translate one into the other. The only translation
/// available is the assumption that the sell list shows occupied slots in ascending container order.
/// That assumption is not guaranteed: the list is the game's, DailyRoutines' equivalent worker carries
/// an explicit sort-order concept for the same list, and nothing in the client tells us the order.
///
/// A wrong translation is silent and expensive rather than loud: in placeholder-then-match mode the new
/// listing keeps its 999,999,999 gil placeholder (so it never sells and nothing errors) while an
/// unrelated listing is re-priced. So every mapping produced here is meant to be verified against what
/// the UI actually shows before a price is written — see <see cref="RowHoldsItem"/>.
/// </summary>
public static class MarketRowMap
{
  /// <summary>Returned by <see cref="RowOfSlot"/> when the slot has no row (empty slot, or out of range).</summary>
  public const int NoRow = -1;

  /// <summary>Occupied slots in ascending container order — the order the sell list is ASSUMED to show.</summary>
  public static IReadOnlyList<MarketSlot> OccupiedInSlotOrder(IReadOnlyList<MarketSlot> market)
    => market.Where(m => m.ItemId != 0).OrderBy(m => m.Slot).ToList();

  /// <summary>How many of the 20 market slots hold something.</summary>
  public static int OccupiedCount(IReadOnlyList<MarketSlot> market)
    => market.Count(m => m.ItemId != 0);

  /// <summary>
  /// Row index of a market slot under the container-order assumption, or <see cref="NoRow"/> when that
  /// slot is empty or absent — an empty slot has no row at all, and guessing one is how you re-price a
  /// stranger's listing.
  /// </summary>
  public static int RowOfSlot(IReadOnlyList<MarketSlot> market, int slot)
  {
    if (!market.Any(m => m.Slot == slot && m.ItemId != 0))
      return NoRow;
    return market.Count(m => m.ItemId != 0 && m.Slot < slot);
  }

  /// <summary>The item the container-order assumption predicts at a UI row; 0 when the row is out of range.</summary>
  public static uint ItemIdAtRow(IReadOnlyList<MarketSlot> market, int row)
  {
    var occupied = OccupiedInSlotOrder(market);
    return row < 0 || row >= occupied.Count ? 0u : occupied[row].ItemId;
  }

  /// <summary>The market slot the container-order assumption predicts at a UI row, or <see cref="NoRow"/>.</summary>
  public static int SlotAtRow(IReadOnlyList<MarketSlot> market, int row)
  {
    var occupied = OccupiedInSlotOrder(market);
    return row < 0 || row >= occupied.Count ? NoRow : occupied[row].Slot;
  }

  /// <summary>
  /// Necessary condition for the mapping to mean anything: the sell list must show exactly one row per
  /// occupied slot. A filtered or partially-built list fails here and must not be addressed by row.
  /// </summary>
  public static bool RowCountAgrees(IReadOnlyList<MarketSlot> market, int uiRowCount)
    => uiRowCount > 0 && uiRowCount == OccupiedCount(market);

  /// <summary>
  /// The check that actually catches a re-sorted list: does the row the UI opened really hold the item
  /// the mapping promised? <paramref name="actualItemId"/> comes from the game (the RetainerSell dialog),
  /// not from the snapshot, so this compares the assumption against observed reality.
  /// </summary>
  public static bool RowHoldsItem(IReadOnlyList<MarketSlot> market, int row, uint actualItemId)
    => actualItemId != 0 && ItemIdAtRow(market, row) == actualItemId;

  /// <summary>
  /// Rows for a set of just-listed slots, in the order they should be priced, or null when any slot fails
  /// to map — one bad slot means the whole ordering assumption is suspect, so the caller falls back to
  /// pricing every row rather than pricing some rows wrongly.
  /// </summary>
  public static List<(int Row, int Slot, uint ItemId)>? RowsForSlots(IReadOnlyList<MarketSlot> market, IEnumerable<(int Slot, uint ItemId)> listed)
  {
    var rows = new List<(int Row, int Slot, uint ItemId)>();
    foreach (var (slot, itemId) in listed)
    {
      var row = RowOfSlot(market, slot);
      if (row == NoRow || ItemIdAtRow(market, row) != itemId)
        return null;
      rows.Add((row, slot, itemId));
    }
    return rows.Count == 0 ? null : rows;
  }
}
