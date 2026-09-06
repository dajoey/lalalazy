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
/// only ~10 of 20 rows have a live renderer at any scroll position) or the name could not be pinned to
/// exactly one item. 0 means "unknown", never "wrong": see <see cref="ItemNameMatch"/>.
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
/// Which of the retainer's 20 market slots this run is allowed to price, decided from the CONTAINER, with no
/// UI text involved at all. See <see cref="SellListRows.ScanPlaceholders"/>.
/// </summary>
/// <param name="Targets">Slots this run listed into that are still sitting at the placeholder price.</param>
/// <param name="AlreadyPriced">Slots this run listed into that already carry a real price - nothing to do.</param>
/// <param name="Foreign">
/// Slots at the placeholder price that this run did NOT list into. Never priced, never counted; carried only
/// so the log can say they were seen and deliberately left alone.
/// </param>
public sealed record PlaceholderScan(
  IReadOnlyList<(int Slot, uint ItemId)> Targets,
  IReadOnlyList<int> AlreadyPriced,
  IReadOnlyList<int> Foreign);

/// <summary>
/// Decides which listings the Auto-Market pass is allowed to re-price, and which UI row each one is on.
///
/// TWO SEPARATE QUESTIONS, answered by two different sources, deliberately:
///
///   WHICH LISTINGS ARE MINE?  <see cref="ScanPlaceholders"/> - the market CONTAINER. A slot qualifies only
///   if this run listed into it AND it is still sitting at the Auto-Market placeholder price
///   (999,999,999 gil by default). No item names, no UI text, no ordering. This is Joey's own instruction
///   (2026-09-05): "there has to be a way to see what my listings are and select the one with the WILDLY
///   INFLATED PRICE". A listing someone made by hand is never at that price, so it is not reachable at all.
///
///   WHICH ROW IS IT ON?  <see cref="MatchBySlot"/> - the addon's own per-row slot reading
///   (AtkValues[15 + 13n], see <c>SellListReader</c>). Exact, covers all 20 rows including ones scrolled out
///   of view, and duplicates of the same item are not a special case.
///
/// The row's visible NAME is a corroborator on the rows being priced, and nothing more. It may never veto
/// the batch over an unrelated row: on 2026-09-05 at 20:37:48 the slot reading found the row for the new
/// listing correctly and the pass was thrown away anyway, because row 0 - a row nobody was pricing - had its
/// clipped label ("Snow Cotton Ushanka of Scouting" read as "Snow Cotton") disagree with the container.
///
/// <see cref="MatchByName"/> remains the fallback for a client that reports no slot for any row.
/// </summary>
public static class SellListRows
{
  /// <summary>True when at least one row carries a usable slot reading.</summary>
  public static bool HasSlotReadings(IReadOnlyList<SellListRow> rows)
    => rows.Any(r => r.Slot != MarketRowMap.NoRow);

  /// <summary>
  /// Split the slots this run listed into by what the market container says their price is now.
  ///
  /// This is the primary identification and the safety guarantee in one step. A slot is a target only when
  /// BOTH hold: this run listed into it, and it still carries the placeholder price. So a listing that was
  /// already priced is dropped (there is nothing to fix), and a listing this run did not create can never be
  /// selected however the sell list is sorted, however its name renders, and whatever else is at 999,999,999.
  /// </summary>
  /// <param name="planned">The slot/item pairs this run listed, in the order they should be reported.</param>
  /// <param name="pricesBySlot">Unit price per market slot as the container reports it; a slot missing from
  /// the dictionary is treated as unreadable, which is NOT the placeholder and therefore not a target.</param>
  /// <param name="placeholderPrice">The Auto-Market placeholder price a new listing is born at.</param>
  public static PlaceholderScan ScanPlaceholders(
    IEnumerable<(int Slot, uint ItemId)> planned,
    IReadOnlyDictionary<int, ulong> pricesBySlot,
    ulong placeholderPrice)
  {
    var targets = new List<(int Slot, uint ItemId)>();
    var alreadyPriced = new List<int>();
    var plannedSlots = new HashSet<int>();

    foreach (var (slot, itemId) in planned)
    {
      plannedSlots.Add(slot);
      if (pricesBySlot.TryGetValue(slot, out var price) && price == placeholderPrice)
        targets.Add((slot, itemId));
      else
        alreadyPriced.Add(slot);
    }

    // Diagnostic only. These are listings sitting at the placeholder price that this run did not create -
    // a previous run that was interrupted, or a price the user typed themselves. They are deliberately NOT
    // priced: "it was at the placeholder" is not on its own permission to touch a listing.
    var foreign = pricesBySlot
      .Where(kv => kv.Value == placeholderPrice && !plannedSlots.Contains(kv.Key))
      .Select(kv => kv.Key)
      .OrderBy(s => s)
      .ToList();

    return new PlaceholderScan(targets, alreadyPriced, foreign);
  }

  /// <summary>
  /// Find the UI row for each slot being priced, using the slot each row reports.
  ///
  /// Cross-checking is SCOPED to the rows actually being priced. Those are the only rows whose identity can
  /// cause a wrong write, and a name wobble on any other row says nothing about them - the addon's slot
  /// reading is per-row and independent (one 13-value block per row). Before 0.1.6.0 this loop ran over every
  /// row with a readable name and returned null on the first disagreement, so one unrelated misread row threw
  /// away a batch that had been identified perfectly.
  ///
  /// What stays global is the duplicate-slot check: two rows claiming one slot means the layout itself was
  /// misread, and that does invalidate every reading. It also subsumes "another row claims a slot we are
  /// pricing", so a second claimant on a target slot can never slip past.
  ///
  /// Returns null if any op cannot be matched, or if a row being priced fails its cross-check. Never
  /// half-applies: one bad op refuses the whole batch.
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

    var matches = new List<RowMatch>();
    foreach (var (slot, itemId) in listed)
    {
      var row = rows.FirstOrDefault(r => r.Slot == slot);
      if (row == null)
      {
        failure = $"no sell-list row is showing slot #{slot} (item {itemId})";
        return null;
      }

      // Corroboration, on this row only. A row whose name did not resolve (0) is not evidence of anything -
      // the list virtualises and labels get clipped - so it is accepted on the slot reading alone. A name
      // that resolved to a DIFFERENT item is a real disagreement about a row we are about to write to, and
      // that still refuses the batch.
      if (row.ItemIdFromName != 0 && row.ItemIdFromName != itemId)
      {
        failure = $"row {row.Row} is slot #{slot} but shows item {row.ItemIdFromName}, not the item {itemId} just listed there";
        return null;
      }

      var inSlot = market.FirstOrDefault(m => m.Slot == slot)?.ItemId ?? 0u;
      if (inSlot != 0 && inSlot != itemId)
      {
        failure = $"the market container says slot #{slot} holds item {inSlot}, not the item {itemId} listed there";
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
