using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// One RetainerSellList row exactly as the client showed it. Nothing here is inferred: every field is
/// either read out of the addon or left explicitly unknown.
/// </summary>
/// <param name="Row">The UI row index the pinch chain would click.</param>
/// <param name="Slot">
/// The RetainerMarket container slot the addon says this row is showing, or <see cref="MarketRowMap.NoRow"/>
/// when it could not be read. This is the whole point of the class: the game tells us the row/slot
/// pairing, so nothing has to assume the list is in container order.
/// </param>
/// <param name="ItemIdFromName">
/// Item id resolved from the row's visible name, or 0 when the row was not rendered (the list virtualises -
/// only ~10 of 20 rows have a live renderer at any scroll position) or the name did not resolve.
/// </param>
/// <param name="AskingPrice">The row's unit price as displayed, or 0 when it was not readable.</param>
public sealed record SellListRow(int Row, int Slot, uint ItemIdFromName, long AskingPrice);

/// <summary>Where a matched row came from - kept so the log line can say how sure we are.</summary>
public enum RowMatchSource
{
  /// <summary>The addon told us this row shows this market slot.</summary>
  ObservedSlot,
  /// <summary>The row's visible item name identified it (used only when the slot reading is unavailable).</summary>
  ObservedName,
}

/// <summary>A just-listed op tied to the UI row that actually holds it.</summary>
public sealed record RowMatch(int Row, int Slot, uint ItemId, RowMatchSource Source);

/// <summary>
/// Turns observed RetainerSellList rows into "price THIS row" instructions for the just-listed slots.
///
/// 0.1.3.0 computed the row from the market container instead ("the list shows occupied slots in ascending
/// container order") and then verified the guess. On Joey's client that guess was wrong on 4 of 4 measured
/// runs (2026-09-05), the verification correctly refused every time, and the failure path re-priced the whole
/// retainer - i.e. exactly the behaviour the feature existed to remove. So the row is now READ, never derived:
///
///   1. <see cref="MatchBySlot"/> - the addon's own row-to-slot pairing. Exact, covers all 20 rows including
///      the ones that are scrolled out of view, and duplicates of the same item are not a special case.
///   2. <see cref="MatchByName"/> - fallback for when the slot reading is unavailable: identify the row by the
///      item name it displays, disambiguating two rows of the same item by asking price (a row still at the
///      Auto-Market placeholder is by construction one this run just created).
///
/// Both are observations. Neither is allowed to half-apply: one unmatched op refuses the whole batch, because
/// a single failure means the reading itself is suspect.
/// </summary>
public static class SellListRows
{
  /// <summary>True when at least one row carries a usable slot reading.</summary>
  public static bool HasSlotReadings(IReadOnlyList<SellListRow> rows)
    => rows.Any(r => r.Slot != MarketRowMap.NoRow);

  /// <summary>
  /// Match by the slot the addon reports for each row, cross-checked against the market snapshot: where a
  /// row's name was also readable, it must name the item the container holds in that slot, or the reading is
  /// not trusted at all. Returns null if any op cannot be matched, or if any cross-check fails.
  /// </summary>
  public static List<RowMatch>? MatchBySlot(
    IReadOnlyList<SellListRow> rows,
    IReadOnlyList<MarketSlot> market,
    IEnumerable<(int Slot, uint ItemId)> listed,
    out string? failure)
  {
    failure = null;

    // A slot must appear on exactly one row; two rows claiming one slot means we misread the layout.
    var duplicated = rows.Where(r => r.Slot != MarketRowMap.NoRow)
      .GroupBy(r => r.Slot).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    if (duplicated.Count > 0)
    {
      failure = $"the sell list reports slot(s) {string.Join(", ", duplicated.Select(s => "#" + s))} on more than one row";
      return null;
    }

    // Every row we could read a NAME for must agree with what the container says is in the slot it claims.
    // One disagreement and the whole reading is suspect - this is the check that would catch a layout change.
    foreach (var row in rows.Where(r => r.Slot != MarketRowMap.NoRow && r.ItemIdFromName != 0))
    {
      var inSlot = market.FirstOrDefault(m => m.Slot == row.Slot)?.ItemId ?? 0u;
      if (inSlot != 0 && inSlot != row.ItemIdFromName)
      {
        failure = $"row {row.Row} says it is slot #{row.Slot} (item {inSlot}) but it is showing item {row.ItemIdFromName}";
        return null;
      }
    }

    var matches = new List<RowMatch>();
    foreach (var (slot, itemId) in listed)
    {
      var row = rows.FirstOrDefault(r => r.Slot == slot);
      if (row == null)
      {
        failure = $"no sell-list row is showing slot #{slot} (item {itemId})";
        return null;
      }
      if (row.ItemIdFromName != 0 && row.ItemIdFromName != itemId)
      {
        failure = $"row {row.Row} is slot #{slot} but shows item {row.ItemIdFromName}, not the item {itemId} just listed there";
        return null;
      }
      matches.Add(new RowMatch(row.Row, slot, itemId, RowMatchSource.ObservedSlot));
    }

    return matches.Count == 0 ? null : matches;
  }

  /// <summary>
  /// Fallback used only when no row reports a slot: identify each just-listed op by the item name the row
  /// displays. Two rows of the same item are separated by asking price - a row still holding the Auto-Market
  /// placeholder is one this run created. An op whose item appears on several equally plausible rows is
  /// refused rather than guessed, and no two ops may claim the same row.
  /// </summary>
  public static List<RowMatch>? MatchByName(
    IReadOnlyList<SellListRow> rows,
    IEnumerable<(int Slot, uint ItemId)> listed,
    long placeholderPrice,
    out string? failure)
  {
    failure = null;
    var matches = new List<RowMatch>();
    var taken = new HashSet<int>();

    foreach (var (slot, itemId) in listed)
    {
      var candidates = rows.Where(r => r.ItemIdFromName == itemId && !taken.Contains(r.Row)).ToList();
      if (candidates.Count == 0)
      {
        failure = $"no visible sell-list row shows item {itemId} (listed into slot #{slot})";
        return null;
      }

      if (candidates.Count > 1)
      {
        // Only a placeholder-priced row can be one this run created. This discriminator does not exist in
        // UniversalisFirst mode (the listing is born at a real price), which is why an unresolved duplicate
        // falls through to the caller's fallback policy rather than being guessed at.
        var atPlaceholder = candidates.Where(r => r.AskingPrice == placeholderPrice).ToList();
        if (atPlaceholder.Count != 1)
        {
          failure = $"item {itemId} (slot #{slot}) is on {candidates.Count} rows and {atPlaceholder.Count} of them still hold the placeholder price, so the new one cannot be told apart";
          return null;
        }
        candidates = atPlaceholder;
      }

      var row = candidates[0];
      taken.Add(row.Row);
      matches.Add(new RowMatch(row.Row, slot, itemId, RowMatchSource.ObservedName));
    }

    return matches.Count == 0 ? null : matches;
  }

  /// <summary>
  /// Rows whose item is on the user's own Auto-Market list. Used by the <c>OwnItemsOnly</c> fallback policy:
  /// when a just-listed row cannot be identified, re-price only listings of items the user told us to sell,
  /// which can never touch a listing they made by hand. A row is included only when its item is KNOWN - a row
  /// with no readable slot and no readable name is left alone.
  /// </summary>
  public static List<int> RowsHoldingOwnItems(
    IReadOnlyList<SellListRow> rows,
    IReadOnlyList<MarketSlot> market,
    IReadOnlyCollection<uint> ownItemIds)
  {
    var result = new List<int>();
    foreach (var row in rows)
    {
      var itemId = row.ItemIdFromName;
      if (itemId == 0 && row.Slot != MarketRowMap.NoRow)
        itemId = market.FirstOrDefault(m => m.Slot == row.Slot)?.ItemId ?? 0u;
      if (itemId != 0 && ownItemIds.Contains(itemId))
        result.Add(row.Row);
    }
    return result;
  }
}
