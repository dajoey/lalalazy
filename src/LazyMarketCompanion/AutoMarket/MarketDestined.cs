using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// 0.2.6.0: the market-destined set - which bags stacks THIS pass's listing plan will take -
/// computed from the SAME gated rule list the real plan uses.
///
/// THE DEFECT THIS REPLACES (2026-09-28 session, item 7488 HQ): 0.2.5.0's
/// ComputeMarketDestined planned WITHOUT the value gate, so an unpriced rule crowded the free
/// market slots first and a PRICED rule later in list order missed the destined set. It kept
/// its routing-deposit op; the listing pass then listed that stack from the bags BEFORE the
/// mover ran (listing steps front-insert ahead of routing moves), the mover's MoveItemSlot
/// hit an already-empty source slot and logged a false "FAILED rc=-1; leaving the stack where
/// it is" for a stack that was already listed on the board. The destined set must mirror the
/// real plan exactly - same category filter, same List-verdict gate, same planner - or the
/// deposit guard excludes the wrong stacks.
/// </summary>
public static class MarketDestined
{
  /// <summary>
  /// The quiet gate filter for the destined probe: the rules whose verdict is List under the
  /// same <see cref="MarketGate.Decide"/> the real ApplyValueGate runs (announces stripped -
  /// this is a probe, the pass announces once through its own path). Null quotes keep the
  /// HoldBack semantics of a blind gate: nothing is destined.
  /// </summary>
  public static List<ItemRule> ListVerdictRules(
    IReadOnlyList<ItemRule> rules,
    IReadOnlyList<StockStack> stock,
    IReadOnlyDictionary<uint, ItemQuote>? quotes,
    bool preferHq,
    bool listPartialStacks,
    long thresholdGil,
    long nowUnixMs,
    long freshnessMs)
  {
    var gateOptions = new GateOptions(true, Math.Max(thresholdGil, 0), freshnessMs);
    var kept = new List<ItemRule>(rules.Count);
    foreach (var rule in rules)
    {
      ItemQuote? quote = null;
      quotes?.TryGetValue(rule.ItemId, out quote);
      var sellable = MarketGate.PotentialSellable(rule, stock, listPartialStacks);
      if (MarketGate.Decide(sellable, quote, rule.HQ, preferHq, gateOptions, nowUnixMs) == GateVerdict.List)
        kept.Add(rule);
    }

    return kept;
  }

  /// <summary>
  /// The item ids of the Bags-origin ops a plan over the given (already gated) rules produces.
  /// This is exactly the set of bags stacks the listing pass is about to take away from the
  /// bags, so the routing mover must not also plan storage deposits for them.
  /// </summary>
  public static HashSet<uint> BagsOriginItemIds(
    IReadOnlyList<ItemRule> gatedRules,
    IReadOnlyList<StockStack> stock,
    IReadOnlyList<MarketSlot> market,
    PlannerOptions options)
    => AutoMarketPlanner.Plan(gatedRules, stock, market, options)
      .Ops.Where(o => o.Origin == StockOrigin.Bags)
      .Select(o => o.ItemId)
      .ToHashSet();
}
