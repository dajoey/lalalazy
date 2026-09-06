using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Row/slot arithmetic under the container-order assumption — "the sell list shows occupied slots in
/// ascending container order".
///
/// THAT ASSUMPTION IS FALSE and is MEASURED false, so nothing here may be used to decide which row to
/// price. On Joey's client it was wrong on 4 of 4 Auto-Market runs on 2026-09-05 (row 17 held Ice Crystal,
/// rows 3/12 held Heavens' Eye Materia VII and Zormor Stone Lantern, row 19 held Table Orchestrion, row 10
/// held Liquid Glass); 0.1.3.0's guards refused every one and its fallback re-priced the whole retainer,
/// which is the bug users actually saw. <see cref="SellListRows"/> replaced all of it: the row/slot pairing
/// is READ off the addon (<c>SellListReader</c>), never derived.
///
/// What survives here is only what does not depend on the ordering:
/// <see cref="OccupiedCount"/> and <see cref="RowCountAgrees"/> — a sanity check that the list is showing one
/// row per occupied slot, which says nothing about their order. The mapping members are kept because the
/// harness pins the (wrong) behaviour so a future reader cannot quietly resurrect it as a shortcut.
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
  /// Row index of a market slot under the container-order assumption — WRONG on live clients (see the class
  /// remarks). Do not use it to choose a row to price; read the row instead (<see cref="SellListRows"/>).
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
  /// Rows for a set of just-listed slots under the container-order assumption, or null when any slot fails to
  /// map. SUPERSEDED in 0.1.5.0 by <see cref="SellListRows.MatchBySlot"/>, which reads the pairing instead of
  /// deriving it; kept only so the harness can keep pinning what this returns.
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
