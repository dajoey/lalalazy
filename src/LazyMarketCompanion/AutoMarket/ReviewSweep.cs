using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// Manual "Sweep to Bags" button (0.1.62.0): when pressed on the RetainerList overlay, walk every
// enabled retainer and move Auto-Market-list items that are sitting in that retainer's inventory
// (not on its market board) into the character's bags for manual review. Market-board listings are
// never in the retainer-page snapshot, so they are never candidates. Ineligible stacks (no enabled
// Auto-Market entry for that item+quality) are never candidates. Crystals stay put - MoveItemSlot
// into the crystals containers is unproven, same polarity as the routing mover.
//
// HARD CONSTRAINT: this planner has no timer, idle loop, or config that can fire it. The only
// caller is the RetainerList overlay button. The review must check for that explicitly.

/// <summary>One whole-stack pull from a retainer page into the character's bags.</summary>
public sealed record ReviewSweepOp(int SrcContainer, int SrcSlot, uint ItemId, bool HQ, int Quantity);

/// <summary>A retainer-page stack that was seen and left alone, with why.</summary>
public sealed record ReviewSweepSkip(uint ItemId, bool HQ, int Quantity, string Reason);

/// <summary>Plan for one retainer session. StoppedForSpace is true when further eligible stacks
/// remain but the bags have no empty slot left for another whole stack.</summary>
public sealed record ReviewSweepPlan(
  IReadOnlyList<ReviewSweepOp> Ops,
  IReadOnlyList<ReviewSweepSkip> Skipped,
  IReadOnlyList<string> Notes,
  bool StoppedForSpace);

/// <summary>
/// Decides which retainer-page stacks move to bags for manual review. Eligibility is exactly the
/// enabled Auto-Market list (item id + quality) - the same identity BuildEnabledRules uses. No
/// second definition. Keep floors, category routing, and source overrides do not apply here: the
/// point is to put every list item that is sitting unlisted on a retainer into bags so a person
/// can look at it. Destination capacity is whole-stack empty-slot accounting only; merges at
/// execution time may absorb a stack without a free slot, but the planner never assumes a merge.
/// </summary>
public static class ReviewSweep
{
  // InventoryType.Crystals / RetainerCrystals as raw ints - this file is Dalamud-free.
  private const int CrystalsContainer = 2001;
  private const int RetainerCrystalsContainer = 12001;

  public static ReviewSweepPlan Plan(
    IReadOnlyList<StockStack> stock,
    IReadOnlyCollection<(uint ItemId, bool HQ)> eligible,
    int freeBagSlots,
    string retainerName)
  {
    var ops = new List<ReviewSweepOp>();
    var skipped = new List<ReviewSweepSkip>();
    var notes = new List<string>();
    var keys = new HashSet<(uint, bool)>(eligible);
    var free = Math.Max(freeBagSlots, 0);
    var stoppedForSpace = false;

    foreach (var stack in stock)
    {
      if (stack.Origin != StockOrigin.Retainer)
        continue;
      if (stack.ItemId == 0 || stack.Quantity <= 0)
        continue;

      if (stack.Container == CrystalsContainer || stack.Container == RetainerCrystalsContainer)
      {
        if (keys.Contains((stack.ItemId, stack.HQ)))
          skipped.Add(new ReviewSweepSkip(stack.ItemId, stack.HQ, stack.Quantity, "crystal container"));
        continue;
      }

      if (!keys.Contains((stack.ItemId, stack.HQ)))
      {
        skipped.Add(new ReviewSweepSkip(stack.ItemId, stack.HQ, stack.Quantity, "not on Auto-Market list"));
        continue;
      }

      if (free <= 0)
      {
        skipped.Add(new ReviewSweepSkip(stack.ItemId, stack.HQ, stack.Quantity, "bags full"));
        stoppedForSpace = true;
        continue;
      }

      ops.Add(new ReviewSweepOp(stack.Container, stack.Slot, stack.ItemId, stack.HQ, stack.Quantity));
      free--;
    }

    if (stoppedForSpace)
      notes.Add($"review sweep: bags full on {retainerName} - remaining eligible stacks left in place");

    return new ReviewSweepPlan(ops, skipped, notes, stoppedForSpace);
  }

  /// <summary>Human-readable one-line summary for chat / log. Counts only; names stay with the caller.</summary>
  public static string Summarize(ReviewSweepPlan plan)
  {
    var moved = plan.Ops.Count;
    var ineligible = plan.Skipped.Count(s => s.Reason == "not on Auto-Market list");
    var bagsFull = plan.Skipped.Count(s => s.Reason == "bags full");
    var crystals = plan.Skipped.Count(s => s.Reason == "crystal container");
    return $"moved {moved}, skipped ineligible {ineligible}, left for space {bagsFull}, crystals {crystals}";
  }
}
