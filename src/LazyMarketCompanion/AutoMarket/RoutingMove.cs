using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// 0.1.40.0: the routing MOVER (Helm t-joey-1789190796770, Joey chose "build-mover"). Category routing
// as shipped in 0.1.37.0 is a GATE - CategoryRouter.FilterForRetainer decides "may this item list on
// THIS retainer" and an item sitting in the wrong retainer's inventory is skipped there, invisible
// during the right retainer's session, so it never lists anywhere and never says why. That silence is
// what read as "it has not worked once since we set it up". This pass is the missing other half:
// during each retainer's own session it MOVES stock so the division the rules describe actually
// happens -
//   - RetainerToBags: an enabled, marketable, routed item sitting in THIS retainer's pages whose
//     category is assigned to a DIFFERENT retainer is pulled into the player's bags, where the
//     assigned retainer's own session can take it in.
//   - BagsToRetainer: the same item sitting in the player's bags while its ASSIGNED retainer's
//     session is open is deposited into that retainer's pages, ahead of the listing plan.
//
// Both legs run inside the same retainer session Auto-Market already drives (the sell list is open,
// which is when the retainer page + bags containers are loaded), as inventory container moves via
// InventoryManager.MoveItemSlot - the same class of call the listing path already makes. Movement
// runs BEFORE BuildPlan's fresh snapshot, so moved stock participates in this same session's listing
// plan (subject to the gate, which has already run on the pre-move fetch - a moved item with no
// quote lists, same as any uncertainty).
//
// BINDING CONSTRAINTS (from the 0.1.37.0 design, still load-bearing here):
//   - Only automarket-ENABLED, MARKETABLE items are ever touched. No rule, no rule match, sheet
//     miss, or ExcludeFromCategoryRouting checkbox tick -> the stack is never moved (fail open -
//     exactly the polarity the gate uses; routing must never be why an item silently disappears).
//   - KeepInRetainer / KeepInBags floors are respected: only the units ABOVE the keep floor move.
//   - Crystals containers (player Crystals 2001 / RetainerCrystals 12001) are deliberately out of
//     scope: crystals auto-stack across their own containers and MoveItemSlot into them is
//     unproven, so the mover leaves them for the planner, which lists them fine from where they sit.
//   - A full destination is a normal state, not an error: the leg stops with a note and the sweep
//     continues (PullPlan.StoppedForSpace precedent).

/// <summary>Which way a routing move goes. RetainerToBags frees misplaced stock for the assigned
/// retainer's own session; BagsToRetainer deposits assigned stock into the retainer being visited.</summary>
public enum MoveLeg { RetainerToBags, BagsToRetainer }

/// <summary>One whole-stack container-to-container move. SrcContainer is a game InventoryType value
/// as int (StockStack.Container) - the game-side executor re-reads the slot before firing. There is
/// deliberately no Quantity: InventoryManager.MoveItemSlot moves the WHOLE stack (with no quantity
/// argument to split it), so a keep floor cannot be honoured on a move and the planner skips
/// keep-floored stacks instead of half-honouring one.</summary>
public sealed record RoutingMoveOp(MoveLeg Leg, int SrcContainer, int SrcSlot, uint ItemId, bool HQ);

/// <summary>The mover's plan. Notes carry the per-retainer summary lines; StoppedForBags /
/// StoppedForRetainer are true when that leg ended early because its destination filled up - the
/// sweep itself is never stopped by either.</summary>
public sealed record RoutingMovePlan(
  IReadOnlyList<RoutingMoveOp> Ops,
  IReadOnlyList<string> Notes,
  bool StoppedForBags,
  bool StoppedForRetainer);

/// <summary>
/// Decides which stacks move during THIS retainer's session. Pure function of the stock snapshot,
/// the enabled rule list, the category lookups, and the routing rules - the game-side caller
/// (AutoMarketService.PlanRoutingMoves) builds those exactly as ApplyCategoryRouting does, so the
/// mover and the gate can never disagree about which retainer a category belongs to.
/// </summary>
public static class RoutingMove
{
  // InventoryType.Crystals / InventoryType.RetainerCrystals as raw ints - this file is Dalamud-free.
  private const int CrystalsContainer = 2001;
  private const int RetainerCrystalsContainer = 12001;

  public static RoutingMovePlan Plan(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<string, ItemCategoryInfo> categoryByKey,
    IReadOnlyDictionary<string, bool> excludeByKey,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    Func<int> freeBagSlots,
    Func<int> freeRetainerSlots)
  {
    var ops = new List<RoutingMoveOp>();
    var notes = new List<string>();
    if (categoryRules.Count == 0)
      return new RoutingMovePlan(ops, notes, false, false);

    var freeBags = Math.Max(0, freeBagSlots());
    var freeRet = Math.Max(0, freeRetainerSlots());
    var stoppedBags = false;
    var stoppedRet = false;
    var skippedCrystals = 0;

    foreach (var stack in stock)
    {
      if (stack.Container == CrystalsContainer || stack.Container == RetainerCrystalsContainer)
      {
        if (rules.Any(r => r.ItemId == stack.ItemId && r.HQ == stack.HQ))
          skippedCrystals++;
        continue;
      }

      var rule = rules.FirstOrDefault(r => r.ItemId == stack.ItemId && r.HQ == stack.HQ);
      if (rule == null)
        continue;

      var key = $"{stack.ItemId}:{(stack.HQ ? "hq" : "nq")}";
      if (!categoryByKey.TryGetValue(key, out var info))
        continue; // sheet miss: fail open, never move what cannot be positively identified
      if (!info.Marketable)
        continue;
      if (excludeByKey.TryGetValue(key, out var ex) && ex)
        continue; // per-item opt-out: sells normally from wherever it sits, on every retainer

      var mapped = categoryRules.FirstOrDefault(r => r.CategoryId == info.CategoryId);
      if (mapped == null)
        continue; // unrouted category: no movement restriction, so no movement duty either

      if (stack.Origin == StockOrigin.Retainer)
      {
        if (mapped.RetainerName == retainerName)
          continue; // already in the right retainer

        // MarketPull precedent: never strand a stack. A rule that never sells from bags would leave
        // a pulled-out stack sitting unlistable in the bags whenever the assigned retainer's own
        // deposit leg cannot take it (full pages, session ordering) - strictly worse than leaving
        // it listed-elsewhere-blocked in the retainer it sits in, so it stays put.
        if (!rule.SellFromBags)
          continue;

        // MoveItemSlot moves whole stacks: a keep floor that would leave part of the stack behind
        // cannot be expressed, so the stack stays put entirely (the gate still lists nothing of it
        // here - the keep floor and the mover agree by construction).
        if (stack.Quantity <= rule.KeepInRetainer)
          continue;

        if (freeBags <= 0)
        {
          stoppedBags = true;
          continue; // deposits may still be possible - do not break the whole pass
        }

        ops.Add(new RoutingMoveOp(MoveLeg.RetainerToBags, stack.Container, stack.Slot, stack.ItemId, stack.HQ));
        freeBags--;
      }
      else
      {
        if (mapped.RetainerName != retainerName)
          continue; // belongs to a different retainer's session - that retainer takes it in

        // Same stranding guard, deposit side: a RetainerOnly rule lists deposited stock fine, but a
        // bags-only rule's deposit would be pointless (it lists from bags, not the retainer) - and
        // a rule that sells from NEITHER cannot list it anywhere, so the stack never moves.
        if (!rule.SellFromRetainer)
          continue;

        if (stack.Quantity <= rule.KeepInBags)
          continue; // whole-stack move only - see the RetainerToBags branch

        if (freeRet <= 0)
        {
          stoppedRet = true;
          continue;
        }

        ops.Add(new RoutingMoveOp(MoveLeg.BagsToRetainer, stack.Container, stack.Slot, stack.ItemId, stack.HQ));
        freeRet--;
      }
    }

    if (stoppedBags)
      notes.Add("routing move: the player's bags have no free slot for further pull-outs; those items stay in this retainer for now (the sweep continues)");
    if (stoppedRet)
      notes.Add("routing move: this retainer's pages are full, so no further bags stock can be deposited this session (the sweep continues)");
    if (skippedCrystals > 0)
      notes.Add($"routing move: {skippedCrystals} crystal stack(s) were left where they are (crystals move and stack through their own containers; Auto-Market still lists them from there)");

    return new RoutingMovePlan(ops, notes, stoppedBags, stoppedRet);
  }

  /// <summary>The one-line summary for the log/chat announce: "3 pull-out(s), 2 deposit(s)".</summary>
  public static string Summarize(IReadOnlyList<RoutingMoveOp> ops)
  {
    var out_ = ops.Count(o => o.Leg == MoveLeg.RetainerToBags);
    var in_ = ops.Count(o => o.Leg == MoveLeg.BagsToRetainer);
    return $"{out_} pull-out(s) to bags, {in_} deposit(s) from bags";
  }
}
