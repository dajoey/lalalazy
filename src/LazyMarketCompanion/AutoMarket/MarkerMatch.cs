using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Pure predicate + matching logic for the Auto-Market bag markers. The marker answers exactly one
/// question: is this stack on the Auto-Market list and enabled (so the next Auto-Market run would
/// try to list it)? An entry that exists but is Enabled=false does NOT get a marker: the listing
/// engine (<c>AutoMarketService.BuildPlan</c>) consumes only Enabled entries, so marking it would
/// promise a listing that never happens. HQ and NQ are separate config entries, so they mark
/// independently. Tradability does not gate the marker - a marked untradable item is a config-entry
/// bug and showing it is honest.
/// </summary>
public static class MarkerMatch
{
  /// <summary>One bag stack to test: container slot index, item id and HQ flag, exactly as the client's inventory container reports them.</summary>
  public sealed record Stack(int Slot, uint ItemId, bool Hq);

  /// <summary>The config side of the predicate: one Auto-Market list entry's key fields.</summary>
  public sealed record Entry(uint ItemId, bool Hq, bool Enabled);

  /// <summary>
  /// How a bag slot is visually marked. <see cref="None"/> draws nothing;
  /// <see cref="OnList"/> is the green dot (this stack is on the Auto-Market list and enabled);
  /// <see cref="MarketableNotListed"/> is the grey dot (this stack CAN go on the market board but
  /// is not on the Auto-Market list). Untradable items get <see cref="None"/> - no dot at all.
  /// </summary>
  public enum MarkKind
  {
    None = 0,
    OnList = 1,
    MarketableNotListed = 2,
  }

  /// <summary>True when the stack's Auto-Market entry exists and is enabled - the "marked" state.</summary>
  public static bool IsMarked(IReadOnlyList<Entry> entries, uint itemId, bool hq)
  {
    foreach (var e in entries)
      if (e.ItemId == itemId && e.Hq == hq)
        return e.Enabled;
    return false;
  }

  /// <summary>Every stack of the given set that should carry a marker, keyed by container slot index.</summary>
  public static Dictionary<int, Stack> MarkedStacks(IReadOnlyList<Entry> entries, IEnumerable<Stack> stacks)
  {
    var marked = new Dictionary<int, Stack>();
    foreach (var s in stacks)
      if (IsMarked(entries, s.ItemId, s.Hq))
        marked[s.Slot] = s;
    return marked;
  }

  /// <summary>
  /// Classify every stack: keyed by container slot index, each stack's <see cref="MarkKind"/>.
  /// Recomputed every draw from the live entries and marketability set - no state, no cache.
  /// </summary>
  /// <param name="entries">The Auto-Market list entries (config).</param>
  /// <param name="stacks">The container's stacks.</param>
  /// <param name="marketableItemIds">Item ids that CAN be put on the market board (game-side computed: tradable per the Item sheet).</param>
  public static Dictionary<int, (Stack Stack, MarkKind Kind)> Classify(
    IReadOnlyList<Entry> entries, IEnumerable<Stack> stacks, IReadOnlySet<uint> marketableItemIds)
  {
    var result = new Dictionary<int, (Stack, MarkKind)>();
    foreach (var s in stacks)
    {
      var kind = MarkKind.None;
      if (IsMarked(entries, s.ItemId, s.Hq))
        kind = MarkKind.OnList;
      else if (marketableItemIds.Contains(s.ItemId))
        kind = MarkKind.MarketableNotListed;
      if (kind != MarkKind.None)
        result[s.Slot] = (s, kind);
    }
    return result;
  }
}
