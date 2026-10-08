using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 161).

/// <summary>
/// The Auto-Market panel's list model (0.2.8.11): every marketable stack in the player's bags,
/// grouped by item and quality, each group graded ON the Auto-Market list / not on it / not
/// marketable, with the quick-exclude flag surfaced, plus the filter, sort and bulk-count logic
/// the panel window renders. Pure data in, pure data out - the window
/// (<c>Windows/AutoMarketPanelWindow.cs</c>) owns every live read and every config write.
///
/// The panel answers the marker question as a list: the same two-state meaning the bag dots use
/// (green = on the list and enabled, grey = marketable but not on it), plus two things a dot
/// cannot show: the quick-exclude flag and entries whose tick is unticked. An entry that exists
/// but is disabled is still ON the list (it has a config row, and the row's knobs are editable
/// here) - the row carries <c>EntryEnabled = false</c> so the panel can say "off". Untradable
/// items with no market category get <see cref="RowKind.NotMarketable"/>: hidden by default and
/// only counted, exactly like the dots' "no dot at all".
///
/// ADD/REMOVE code path: the window calls <c>Configuration.GetOrAddAutoMarketItem</c> (the same
/// method the inventory context menu's "Add to Auto-Market" uses) and the config table's
/// <c>AutoMarketItems.Remove</c> for removals. The <see cref="IsNewEntry"/> / <see cref="HasEntry"/>
/// helpers here exist so the offline harness can pin those semantics; <see cref="PlanBulk"/>
/// counts what "add all visible" / "remove all visible" would touch BEFORE anything changes -
/// the confirmation dialog's numbers are computed from the same rows the panel is showing.
/// </summary>
public static class AutoMarketPanelModel
{
  /// <summary>One inventory stack: container type id, slot, item, quality, quantity.</summary>
  public sealed record StackInput(int Container, int Slot, uint ItemId, bool Hq, int Quantity);

  /// <summary>One Auto-Market list entry: exactly the fields the panel shows and edits.</summary>
  public sealed record EntryInput(uint ItemId, bool Hq, bool Enabled, bool ExcludeFromRouting, int StackSize, int KeepInBags);

  /// <summary>The three states a grouped stack can be in. Same meaning as the bag dots, plus "not marketable" made countable.</summary>
  public enum RowKind
  {
    OnList = 0,
    NotListed = 1,
    NotMarketable = 2,
  }

  /// <summary>One grouped row of the panel list: all stacks of one (item, quality) pair merged.</summary>
  public sealed record Row(
    uint ItemId,
    bool Hq,
    string Name,
    uint CategoryId,
    int Quantity,
    int Stacks,
    RowKind Kind,
    bool Excluded,
    bool EntryEnabled,
    int StackSize,
    int KeepInBags);

  public enum StatusFilter
  {
    All = 0,
    OnList = 1,
    NotOnList = 2,
    Excluded = 3,
  }

  /// <summary>The panel's filters. <see cref="CategoryId"/> 0 = every category.</summary>
  public sealed record Filters(StatusFilter Status, string Search, uint CategoryId, bool ShowNotMarketable)
  {
    public static readonly Filters Default = new(StatusFilter.All, string.Empty, 0, false);
  }

  /// <summary>
  /// Rows by status, counted over EVERY grouped stack BEFORE filtering - the numbers the open log
  /// line carries and the "unmarketable" filter label shows. <see cref="Excluded"/> counts rows
  /// that are on the list AND quick-excluded (a subset of <see cref="OnList"/>).
  /// </summary>
  public sealed record Counts(int OnList, int NotListed, int Excluded, int NotMarketable);

  /// <summary>
  /// Group the stacks by (item, quality) and classify each group against the Auto-Market list and
  /// the marketability set. Returns EVERY row - including not-marketable ones, so
  /// <see cref="Count"/> can count what is hidden - sorted: on-list rows in LIST order (the order
  /// of <paramref name="entries"/> - <c>AutoMarketSortMode.ListOrder</c> semantics; the
  /// price/velocity sort modes rank by live board data this deliberately offline panel never
  /// fetches), then not-listed rows by name, not-marketable rows last. Apply the panel's filters
  /// with <see cref="Visible"/>.
  /// </summary>
  /// <param name="stacks">Every bag stack to consider (already read from the game's containers).</param>
  /// <param name="entries">The Auto-Market list, in config order.</param>
  /// <param name="nameOf">Display name for an item id (the window resolves from the Item sheet).</param>
  /// <param name="categoryIdOf">Market-board search category for an item id (0 = none).</param>
  /// <param name="isMarketable">The same marketability test the markers use (<c>!IsUntradable &amp;&amp; SearchCategory != 0</c>).</param>
  /// <param name="filters">The panel's current filters.</param>
  public static List<Row> Build(
    IReadOnlyList<StackInput> stacks,
    IReadOnlyList<EntryInput> entries,
    Func<uint, string> nameOf,
    Func<uint, uint> categoryIdOf,
    Func<uint, bool> isMarketable)
  {
    var grouped = new Dictionary<(uint ItemId, bool Hq), (int Quantity, int Stacks)>();
    foreach (var s in stacks)
    {
      if (s.Quantity <= 0)
        continue;
      var (qty, count) = grouped.TryGetValue((s.ItemId, s.Hq), out var g) ? g : (0, 0);
      grouped[(s.ItemId, s.Hq)] = (qty + s.Quantity, count + 1);
    }

    var rows = new List<Row>(grouped.Count);
    foreach (var ((itemId, hq), (quantity, stackCount)) in grouped)
    {
      var entry = FindEntry(entries, itemId, hq);
      var marketable = isMarketable(itemId);
      var kind = entry != null
        ? RowKind.OnList
        : marketable ? RowKind.NotListed : RowKind.NotMarketable;
      rows.Add(new Row(
        itemId, hq,
        nameOf(itemId),
        categoryIdOf(itemId),
        quantity, stackCount,
        kind,
        entry?.ExcludeFromRouting ?? false,
        entry?.Enabled ?? false,
        entry?.StackSize ?? 0,
        entry?.KeepInBags ?? 0));
    }

    Sort(rows, entries);

    return rows;
  }

  /// <summary>The rows the panel shows for <paramref name="filters"/>, in the given order.</summary>
  public static List<Row> Visible(IReadOnlyList<Row> rows, Filters filters)
  {
    var result = new List<Row>(rows.Count);
    foreach (var row in rows)
    {
      if (!Passes(row, filters))
        continue;
      result.Add(row);
    }

    return result;
  }

  /// <summary>Rows by status over EVERY grouped stack, before any filter.</summary>
  public static Counts Count(IReadOnlyList<Row> allRows)
  {
    var onList = 0;
    var notListed = 0;
    var excluded = 0;
    var notMarketable = 0;
    foreach (var row in allRows)
    {
      switch (row.Kind)
      {
        case RowKind.OnList:
          onList++;
          if (row.Excluded)
            excluded++;
          break;
        case RowKind.NotListed:
          notListed++;
          break;
        case RowKind.NotMarketable:
          notMarketable++;
          break;
      }
    }

    return new Counts(onList, notListed, excluded, notMarketable);
  }

  /// <summary>
  /// What "add all visible" / "remove all visible" would touch, computed from exactly the rows the
  /// panel is showing: adds for every visible not-listed row, removals for every visible on-list
  /// row. The confirmation dialog shows <see cref="BulkPlan.Adds"/> / <see cref="BulkPlan.Removes"/>.
  /// </summary>
  public static BulkPlan PlanBulk(IReadOnlyList<Row> visibleRows)
  {
    var adds = new List<(uint ItemId, bool Hq)>();
    var removes = new List<(uint ItemId, bool Hq)>();
    foreach (var row in visibleRows)
    {
      if (row.Kind == RowKind.NotListed)
        adds.Add((row.ItemId, row.Hq));
      else if (row.Kind == RowKind.OnList)
        removes.Add((row.ItemId, row.Hq));
    }

    return new BulkPlan(adds, removes);
  }

  public sealed record BulkPlan(IReadOnlyList<(uint ItemId, bool Hq)> Adds, IReadOnlyList<(uint ItemId, bool Hq)> Removes);

  /// <summary>The Auto-Market list entry for an item id + quality, or null. Mirrors Configuration.GetAutoMarketItem.</summary>
  public static EntryInput? FindEntry(IReadOnlyList<EntryInput> entries, uint itemId, bool hq)
  {
    foreach (var e in entries)
      if (e.ItemId == itemId && e.Hq == hq)
        return e;
    return null;
  }

  /// <summary>
  /// Whether adding would CREATE a new entry (true) or the item is already on the list (false).
  /// Mirrors <c>Configuration.GetOrAddAutoMarketItem</c> + the context menu's
  /// "added / already on Auto-Market" distinction: adding twice never duplicates a row.
  /// </summary>
  public static bool IsNewEntry(IReadOnlyList<EntryInput> entries, uint itemId, bool hq)
    => FindEntry(entries, itemId, hq) == null;

  /// <summary>Whether an entry exists to remove (the config table's Remove only acts on one).</summary>
  public static bool HasEntry(IReadOnlyList<EntryInput> entries, uint itemId, bool hq)
    => FindEntry(entries, itemId, hq) != null;

  /// <summary>On-list rows in list order first (a row whose entry is missing from the list sorts by name after them), then the rest by name.</summary>
  private static void Sort(List<Row> rows, IReadOnlyList<EntryInput> entries)
  {
    rows.Sort((a, b) =>
    {
      var ka = OrderKey(a, entries);
      var kb = OrderKey(b, entries);
      var byKind = ka.CompareTo(kb);
      if (byKind != 0)
        return byKind;
      if (ka == 0)
      {
        var byList = ListIndex(a, entries).CompareTo(ListIndex(b, entries));
        if (byList != 0)
          return byList;
      }

      var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
      if (byName != 0)
        return byName;
      if (a.ItemId != b.ItemId)
        return a.ItemId.CompareTo(b.ItemId);
      return a.Hq.CompareTo(b.Hq);
    });
  }

  private static int OrderKey(Row row, IReadOnlyList<EntryInput> entries) => row.Kind switch
  {
    RowKind.OnList => 0,
    RowKind.NotListed => 1,
    _ => 2,
  };

  private static int ListIndex(Row row, IReadOnlyList<EntryInput> entries)
  {
    for (var i = 0; i < entries.Count; i++)
      if (entries[i].ItemId == row.ItemId && entries[i].Hq == row.Hq)
        return i;
    return int.MaxValue;
  }

  private static bool Passes(Row row, Filters filters)
  {
    if (row.Kind == RowKind.NotMarketable && !filters.ShowNotMarketable)
      return false;

    switch (filters.Status)
    {
      case StatusFilter.OnList when row.Kind != RowKind.OnList:
      case StatusFilter.NotOnList when row.Kind != RowKind.NotListed:
      case StatusFilter.Excluded when !row.Excluded:
        return false;
    }

    if (filters.CategoryId != 0 && row.CategoryId != filters.CategoryId)
      return false;

    if (!string.IsNullOrEmpty(filters.Search) && !row.Name.Contains(filters.Search, StringComparison.OrdinalIgnoreCase))
      return false;

    return true;
  }
}
