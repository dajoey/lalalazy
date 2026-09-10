using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// 0.1.32.0: the pull pass. Until now the value gate's Vendor verdict only ever looked at STOCK
// (MarketGate.PotentialSellable over bags/retainer inventory) - an item a rule had already LISTED
// on the board was invisible to it, because GateFetchIds only asked Universalis about stocked
// items and the gate itself never inspected the market container. A below-threshold hand listing,
// or a listing made before the gate/threshold existed, sat on the board forever: nobody bought a
// 1-4 gil stack and nothing ever re-judged it. This pass runs at the TOP of the per-retainer plan
// build (MarketAutomation.BuildListingStepsNow), before the snapshot/plan/vendor leg that already
// exists: for every occupied market slot whose item matches an ENABLED rule and whose gate verdict
// (the SAME MarketGate.Decide the stocked path uses) is Vendor, the slot is withdrawn back to the
// retainer's own inventory - MoveFromRetainerMarketToRetainerInventory, the game's own withdrawal
// call, verified against the live client structs (E8 ?? ?? ?? ?? 45 85 F6 75 22). The pulled stack
// then becomes ordinary unlisted retainer stock, which the UNCHANGED downstream plan/gate/vendor
// leg picks up and vendors like anything else - this file only decides WHICH slots to pull, never
// touches the vendor leg's own logic.

/// <summary>Where a pulled stack lands. RetainerInventory is the normal case; PlayerBags is the
/// one-shot fallback used only when every retainer page is full and the rule also allows bag
/// stock, so a below-threshold listing does not simply get stuck for want of space.</summary>
public enum PullTarget { RetainerInventory, PlayerBags }

/// <summary>One market slot the gate decided to pull, with the listed price for the log line.</summary>
public sealed record PullOp(int Slot, uint ItemId, bool HQ, int Quantity, long ListedPrice, PullTarget Target);

/// <summary>The pull pass's plan. StoppedForSpace is true when the pass ended early because
/// nowhere had room for a further stack - the sweep itself is never stopped by this.</summary>
public sealed record PullPlan(IReadOnlyList<PullOp> Ops, IReadOnlyList<string> Notes, bool StoppedForSpace);

/// <summary>
/// Decides which occupied market slots come back to inventory before this retainer's plan is
/// built. Uncertainty NEVER pulls (mirrors MarketGate's own polarity: a listing with no fresh,
/// quality-matched quote stays exactly where it is); only a confirmed, fresh, below-threshold
/// Vendor verdict pulls. A rule with SellFromRetainer off is never a pull candidate - the pulled
/// stack would land in the retainer's inventory with nothing able to vendor it from there, which
/// would strand the stack worse than leaving it listed.
/// </summary>
public static class MarketPull
{
  public static PullPlan Plan(
    IReadOnlyList<MarketSlot> market,
    IReadOnlyDictionary<int, ulong> pricesBySlot,
    IReadOnlyList<ItemRule> rules,
    IReadOnlyDictionary<uint, ItemQuote>? quotes,
    GateOptions options,
    bool preferHq,
    long nowUnixMs,
    Func<int> freeRetainerPageSlots,
    Func<bool> hasFreeBagSlot)
  {
    var ops = new List<PullOp>();
    var notes = new List<string>();
    if (!options.Enabled || options.ThresholdGil <= 0)
      return new PullPlan(ops, notes, false);

    var freeSlots = freeRetainerPageSlots();
    var playerBagsUsedThisPass = false;
    var stoppedForSpace = false;

    foreach (var slot in market)
    {
      if (slot.ItemId == 0 || slot.Quantity <= 0)
        continue;

      // First matching ENABLED rule for this item+quality. No rule (or a disabled one, which
      // never reaches BuildEnabledRules' output) means the slot is never touched.
      var rule = rules.FirstOrDefault(r => r.ItemId == slot.ItemId && r.HQ == slot.HQ);
      if (rule == null)
        continue;

      // A bags-only rule could never vendor a stack that landed in retainer inventory - pulling
      // it would just strand it there. Leave the listing exactly as it is.
      if (!rule.SellFromRetainer)
        continue;

      ItemQuote? quote = null;
      quotes?.TryGetValue(slot.ItemId, out quote);
      var verdict = MarketGate.Decide(slot.Quantity, quote, rule.HQ, preferHq, options, nowUnixMs);
      if (verdict != GateVerdict.Vendor)
        continue;

      var listedPrice = pricesBySlot.TryGetValue(slot.Slot, out var p) ? (long)p : 0L;

      if (freeSlots > 0)
      {
        ops.Add(new PullOp(slot.Slot, slot.ItemId, slot.HQ, slot.Quantity, listedPrice, PullTarget.RetainerInventory));
        freeSlots--;
        continue;
      }

      // Retainer pages are full. One-shot fallback to the player's own bags, only when the rule
      // also sells from bags (otherwise the stack would be no better off there) and a slot is
      // actually free. "One-shot" keeps this pass from silently filling the player's entire
      // inventory on a single retainer visit.
      if (!playerBagsUsedThisPass && rule.SellFromBags && hasFreeBagSlot())
      {
        ops.Add(new PullOp(slot.Slot, slot.ItemId, slot.HQ, slot.Quantity, listedPrice, PullTarget.PlayerBags));
        playerBagsUsedThisPass = true;
        continue;
      }

      notes.Add("pull: stopping the pull pass for this retainer (nowhere for further stacks to land); the sweep continues");
      stoppedForSpace = true;
      break;
    }

    return new PullPlan(ops, notes, stoppedForSpace);
  }
}
