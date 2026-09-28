using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// What the Item sheet says about one UNCONFIGURED bag item, resolved game-side once per pass
/// (AutoMarketService.BuildGreyInfo): its market-board search category, whether it is marketable
/// at all (the same test the bag markers' grey state uses), and its bag stack size (decides the
/// market listing cap).
/// </summary>
public sealed record GreyItemInfo(uint ItemId, uint CategoryId, bool Marketable, int ItemMaxStack);

/// <summary>
/// The bag-filler's configuration surface. <see cref="Enabled"/> mirrors
/// <c>Configuration.AutoMarketBagFillerEnabled</c> (default ON: the feature is the fix for
/// marketable stock that laid in the bags unseen, and must need zero user action).
/// </summary>
public sealed record BagFillerOptions(bool Enabled, int ReserveSlots, int MarketSlotCount);

/// <summary>
/// 0.2.6.0: the bag filler. The round-6 diagnosis (2026-09-28 session): the grey bag markers
/// are "marketable but not on the Auto-Market list", and BOTH planning surfaces were
/// rule-bounded - the listing planner iterated configured rules only and the routing mover
/// required a rule - so off-list marketable bag stock was structurally invisible to every
/// plan and laid in the bags while retainer market slots sat empty. The filler is the last
/// stage of the listing plan: after the CONFIGURED plan has claimed its slots, remaining free
/// slots (beyond the reserve) may be filled by marketable UNCONFIGURED bag stacks.
///
/// THE CONTRACT (design §13, all frozen invariants unchanged):
/// - a filler listing goes up only on a CONFIRMED quote (fresh world or data-center scope,
///   wanted quality, positive price). Unconfirmed means HOLD: the stack stays in the bags and
///   is named in the held set - never a heuristic price, never a guess;
/// - filler stock is NEVER vendored. The bounded junk path belongs to configured rules only;
///   a confirmed below-threshold off-list stack is held in the bags, not sold to a vendor and
///   not listed;
/// - filler stock is never deposited into retainer storage either (the mover stays
///   rule-bounded) - it lists from the bags exactly where it sits, like any SellFromBags rule;
/// - category routing divides filler stock the same way as configured stock: a category
///   mapped to a retainer lists only on that retainer; an unmapped category is unrestricted;
/// - a board already selling the item never gets a second listing of it from the filler;
/// - an unreadable board (short snapshot) claims no slot - fail closed, same direction as
///   HasListingBudget;
/// - the whole stack lists, clamped to the server's per-listing cap; one listing per stack.
/// </summary>
public static class BagFillerPlanner
{
  public static BagFillerResult Plan(
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<ItemRule> configuredRules,
    IReadOnlyDictionary<uint, GreyItemInfo> greyInfo,
    IReadOnlyList<CategoryRetainerRule> categoryRules,
    string retainerName,
    IReadOnlyDictionary<uint, ItemQuote>? quotes,
    IReadOnlyList<MarketSlot> market,
    IReadOnlyList<ListingOp> configuredOps,
    BagFillerOptions options,
    bool preferHq,
    long nowUnixMs,
    long freshnessMs,
    long thresholdGil)
  {
    if (!options.Enabled)
      return new BagFillerResult([], [], [], 0);

    // Fail closed on a board the snapshot could not read: every slot index would look empty
    // and the filler would hand phantom slots to listing ops (HasListingBudget precedent).
    if (market == null || market.Count < options.MarketSlotCount)
      return new BagFillerResult([], [], [], 0);

    var configuredKeys = configuredRules.Select(r => (r.ItemId, r.HQ)).ToHashSet();
    var listedKeys = market.Where(m => m.ItemId != 0).Select(m => (m.ItemId, m.HQ)).ToHashSet();
    var claimedSlots = configuredOps.Select(o => o.TargetSlot).ToHashSet();

    var emptyAll = Enumerable.Range(0, options.MarketSlotCount)
      .Where(i => market.All(m => m.Slot != i || m.ItemId == 0))
      .OrderBy(i => i)
      .ToList();
    // Same budget arithmetic as the configured planner: empty minus reserve, minus what the
    // configured plan already claimed.
    var budget = Math.Max(0, emptyAll.Count - Math.Max(options.ReserveSlots, 0)) - configuredOps.Count;
    if (budget <= 0)
      return new BagFillerResult([], [], [], 0);
    var emptySlots = new Queue<int>(emptyAll.Where(i => !claimedSlots.Contains(i)));

    var heldUnpriced = new SortedSet<uint>();
    var heldBelowThreshold = new SortedSet<uint>();
    var considered = 0;

    // Candidates in deterministic order: fastest-selling first (a DC-scope fallback quote
    // carries zero velocity and ranks last, exactly like the configured listing order), then
    // item id, then container/slot.
    var candidates = new List<(StockStack Stack, GreyItemInfo Info, long UnitPrice, double Velocity)>();
    foreach (var stack in stock)
    {
      if (stack.Origin != StockOrigin.Bags)
        continue;
      if (configuredKeys.Contains((stack.ItemId, stack.HQ)))
        continue; // a configured rule owns this item - never double-managed by the filler

      if (!greyInfo.TryGetValue(stack.ItemId, out var info) || !info.Marketable)
        continue; // sheet miss or not marketable: fail closed, nothing is listed on a guess

      var mapped = categoryRules.FirstOrDefault(r => r.CategoryId == info.CategoryId);
      if (mapped != null && !CategoryRouter.RetainerNamesEqual(mapped.RetainerName, retainerName))
        continue; // this category belongs to another retainer's board

      considered++;

      ItemQuote? quote = null;
      quotes?.TryGetValue(stack.ItemId, out quote);
      var unit = MarketGate.UsableQuote(quote, stack.HQ, preferHq, nowUnixMs, freshnessMs);
      if (unit is not > 0)
      {
        heldUnpriced.Add(stack.ItemId); // unconfirmed means hold - in the bags, named
        continue;
      }

      var qty = Math.Min(stack.Quantity, MarketListingCap.For(info.ItemMaxStack));
      if (MarketGate.NetRevenue(unit.Value, qty) <= thresholdGil)
      {
        heldBelowThreshold.Add(stack.ItemId); // confirmed junk value: held, never vendored, never listed
        continue;
      }

      if (listedKeys.Contains((stack.ItemId, stack.HQ)))
        continue; // this board already sells it - no duplicate listing

      var velocity = stack.HQ ? quote!.HqVelocityPerDay : quote!.NqVelocityPerDay;
      candidates.Add((stack, info, unit.Value, velocity));
    }

    var ops = new List<ListingOp>();
    foreach (var c in candidates.OrderByDescending(c => c.Velocity).ThenBy(c => c.Stack.ItemId).ThenBy(c => c.Stack.Container).ThenBy(c => c.Stack.Slot))
    {
      if (budget <= 0 || emptySlots.Count == 0)
        break;
      var qty = Math.Min(c.Stack.Quantity, MarketListingCap.For(c.Info.ItemMaxStack));
      ops.Add(new ListingOp(StockOrigin.Bags, c.Stack.Container, c.Stack.Slot, emptySlots.Dequeue(), c.Stack.ItemId, c.Stack.HQ, qty, 0));
      budget--;
    }

    return new BagFillerResult(ops, heldUnpriced.ToList(), heldBelowThreshold.ToList(), considered);
  }
}

/// <summary>The filler stage's output: ops to append to the listing plan, held sets for the announce lines, and how many grey stacks were considered.</summary>
public sealed record BagFillerResult(
  IReadOnlyList<ListingOp> Ops,
  IReadOnlyList<uint> HeldUnpricedIds,
  IReadOnlyList<uint> HeldBelowThresholdIds,
  int ConsideredStacks);
