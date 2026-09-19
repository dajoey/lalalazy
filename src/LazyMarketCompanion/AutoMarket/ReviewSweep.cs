using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// Manual "Sweep to Bags" button: when pressed on the RetainerList overlay, walk every enabled
// retainer and move MARKETABLE items that are NOT on the enabled Auto-Market list - the same
// stacks the bag markers shade grey ("marketable not listed") - into the character's bags for
// manual review. Those stacks have no automation looking after them: Auto-Market will never
// list them, so a person has to decide (list by hand, enroll, or discard).
//
// 0.1.62.0 shipped the opposite target (enabled-list stock) and was graded broken for it: with
// a full market board every enrolled stack is unlisted, so one press pulled the ENTIRE sell
// backlog into bags, and the routing mover deposited all of it straight back on the next
// session. Enrolled stock is now an explicit never-touch rail: anything the Auto-Market list
// names (a superset of everything it could ever have listed - market-board listings live in the
// RetainerMarket container and are never in the retainer-page snapshot at all) stays put.
//
// Crystals stay put - MoveItemSlot into the crystals containers is unproven, same polarity as
// the routing mover.
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
/// Decides which retainer-page stacks move to bags for manual review. Eligible is exactly the
/// marker system's grey state (MarkerMatch.MarkKind.MarketableNotListed): the item CAN go on the
/// market board and has no enabled Auto-Market entry for its item+quality. No second definition.
/// Quality is part of the enrollment identity but not of marketability: an HQ stack of an item
/// whose NQ is enrolled is still grey and still moves. Keep floors, category routing, and source
/// overrides do not apply here: the point is to put every unmanaged marketable stack into bags so
/// a person can look at it. Destination capacity is whole-stack empty-slot accounting only;
/// merges at execution time may absorb a stack without a free slot, but the planner never
/// assumes a merge.
/// </summary>
public static class ReviewSweep
{
  // InventoryType.Crystals / RetainerCrystals as raw ints - this file is Dalamud-free.
  private const int CrystalsContainer = 2001;
  private const int RetainerCrystalsContainer = 12001;

  public static ReviewSweepPlan Plan(
    IReadOnlyList<StockStack> stock,
    IReadOnlyCollection<(uint ItemId, bool HQ)> enrolled,
    IReadOnlySet<uint> marketableItemIds,
    int freeBagSlots,
    string retainerName)
  {
    var ops = new List<ReviewSweepOp>();
    var skipped = new List<ReviewSweepSkip>();
    var notes = new List<string>();
    var keys = new HashSet<(uint, bool)>(enrolled);
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
        skipped.Add(new ReviewSweepSkip(stack.ItemId, stack.HQ, stack.Quantity, "crystal container"));
        continue;
      }

      // Never-touch rail: anything the enabled Auto-Market list names stays with its automation.
      // This is a superset of "currently listed" - market-board stock never appears in the
      // retainer-page snapshot at all.
      if (keys.Contains((stack.ItemId, stack.HQ)))
      {
        skipped.Add(new ReviewSweepSkip(stack.ItemId, stack.HQ, stack.Quantity, "on Auto-Market list"));
        continue;
      }

      if (!marketableItemIds.Contains(stack.ItemId))
      {
        skipped.Add(new ReviewSweepSkip(stack.ItemId, stack.HQ, stack.Quantity, "not marketable"));
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
      notes.Add($"review sweep: bags full on {retainerName} - remaining marketable stacks left in place");

    return new ReviewSweepPlan(ops, skipped, notes, stoppedForSpace);
  }

  /// <summary>Human-readable one-line summary for chat / log. Counts only; names stay with the caller.</summary>
  public static string Summarize(ReviewSweepPlan plan)
  {
    var moved = plan.Ops.Count;
    var onList = plan.Skipped.Count(s => s.Reason == "on Auto-Market list");
    var notMarketable = plan.Skipped.Count(s => s.Reason == "not marketable");
    var bagsFull = plan.Skipped.Count(s => s.Reason == "bags full");
    var crystals = plan.Skipped.Count(s => s.Reason == "crystal container");
    return $"moved {moved}, stayed on Auto-Market list {onList}, not marketable {notMarketable}, left for space {bagsFull}, crystals {crystals}";
  }
}
