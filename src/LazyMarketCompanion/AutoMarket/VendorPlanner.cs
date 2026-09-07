using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercises by tests/LazyMarketCompanion.Harness (case 37).
//
// 0.1.12.0: the retainer-vendor leg's planner (GateVerdict itself lives in MarketGate.cs, where the
// battery lives). A held-back item is still sellable at the retainer bell with ZERO travel - the
// retainer sell-items context menu offers "Have Retainer Sell Items" (Addon sheet row 5480, FCS
// InventoryContextEvent callbackParam=5 "Have Retainer Sell Items"): the retainer vendors the stack
// at the NPC shop price with no market fee and no market slot consumed. Production references:
// AutoRetainer RetainerItemCommand.HaveRetainerSellItem (InventorySpaceManager SafeSellSlot -
// slot-addressed, headless), SimpleTweaksPlugin QuickSellItems (Addon row 5480).
//
// 0.1.15.0: VendorOp.Container carries the game InventoryType (RetainerPage1-7 = 10000+, Inventory1-4
// = 0-3), not the planner's StockOrigin. The 0.1.12.0 build assigned `(int)origin` here - a value of 0
// or 1 - which ExecuteVendor then cast to (InventoryType): Retainer stock addressed Inventory2 (1) and
// bag stock Inventory1 (0). The pre-call slot re-read read the WRONG container, found no matching
// stack, and every op aborted safely - the 0/7 no-op Joey's 2026-09-07 23:20 run hit. The StockStack
// already carries the real container id (`(int)type` in AutoMarketService.Snapshot); the op now gets it.

/// <summary>One stacks worth of vendoring: a source container slot confirmed to still hold the item.</summary>
/// <remarks>Container is the game's InventoryType value (see AutoMarketService.Snapshot); the log
/// renders its NAME via <see cref="VendorOp.ContainerName"/>, never a raw number - the 0.1.12.0
/// defect hid behind "slot 1:10" for three weeks.</remarks>
public sealed record VendorOp(int Container, int Slot, uint ItemId, bool HQ, int Quantity, long EstGil)
{
  /// <summary>
  /// Human-readable container name for log lines ("RetainerPage1", "Inventory1", "Crystals"),
  /// Dalamud-free so the harness can pin it. Mirrors the FFXIVClientStructs InventoryType names for
  /// the containers vendoring can address; anything else renders as "Unknown(n)" - and
  /// <see cref="HasKnownContainer"/> refuses those long before a log line is written.
  /// </summary>
  public string ContainerName() => Container switch
  {
    >= 0 and <= 3 => $"Inventory{Container + 1}",
    2001 => "Crystals",
    >= 10000 and <= 10006 => $"RetainerPage{Container - 10000 + 1}",
    12001 => "RetainerCrystals",
    _ => $"Unknown({Container})",
  };

  /// <summary>
  /// True when the container id is one the retainer's item command can actually address: the seven
  /// retainer pages, the player's four bag pages, or the crystals containers. Anything else is a
  /// planner bug and the op is refused before any game call - the fail-safe the 0.1.12.0 build was
  /// missing when a raw origin enum value (0/1) could pass as a bag id.
  /// </summary>
  public bool HasKnownContainer =>
    Container is >= 10000 and <= 10006   // RetainerPage1-7
    or >= 0 and <= 3                     // Inventory1-4
    or 2001                              // Crystals
    or 12001;                            // RetainerCrystals
}

/// <summary>
/// What vendoring could earn for one item, so the chat line can name a number. The vendor price is
/// the client's own autofill source for a new market listing - Item sheet PriceMid (row item/priceMid),
/// read by the caller. NQ price for NQ stock; with "prefer HQ pricing" on, an HQ stack is priced at
/// PriceMid and an NQ stack at PriceLow, mirroring MarketGate quality rules.
/// </summary>
public static class ItemVendorPrice
{
  /// <summary>Unit vendor price for one stock stack of an item. No sheet row means 0 and callers turn that into HoldBack.</summary>
  public static long UnitFor(bool itemHq, bool stockHq, uint priceMid, uint priceLow, bool preferHq)
  {
    if (priceLow == 0)
      return 0;
    if (stockHq)
      return preferHq && priceMid > 0 ? priceMid : priceLow;
    if (itemHq)
      return 0; // the stock this rule lists from does not match the rule's own quality
    return priceLow;
  }

  /// <summary>Total estimate for a qty at a unit price; caps at int.MaxValue so a UI number never overflows.</summary>
  public static long Total(long unit, long quantity)
  {
    if (unit <= 0 || quantity <= 0)
      return 0;
    var total = unit * quantity;
    return total > int.MaxValue ? int.MaxValue : total;
  }
}

/// <summary>The vendoring plan: which slots of which containers to feed the retainer, newest-price first.</summary>
public sealed record VendorPlan(IReadOnlyList<VendorOp> Ops, IReadOnlyList<string> Notes);

/// <summary>
/// Maps held-back rules onto concrete stock slots. A VendorOp is only built when the slot is
/// re-read from the game immediately before the call (the caller does that in Execute) - this
/// planner works from the same snapshot the gate judged, and the executor re-verifies every slot.
/// Stacks the player marked Keep stay where they are; the keep amount is honoured per origin, taken
/// from the END of the origin's stack list so the largest stacks vendor first.
/// </summary>
public static class VendorPlanner
{
  public static VendorPlan Plan(IReadOnlyList<ItemRule> heldRules, IReadOnlyList<StockStack> stock,
    Dictionary<uint, (uint PriceMid, uint PriceLow)> prices, bool preferHq)
  {
    var ops = new List<VendorOp>();
    var notes = new List<string>();

    foreach (var rule in heldRules)
    {
      if (!prices.TryGetValue(rule.ItemId, out var price) || price.PriceLow == 0)
      {
        notes.Add($"vendor: no Item-sheet price for {rule.ItemId}, leaving it in place");
        continue;
      }

      foreach (var origin in new[] { StockOrigin.Retainer, StockOrigin.Bags })
      {
        var enabled = origin == StockOrigin.Bags ? rule.SellFromBags : rule.SellFromRetainer;
        if (!enabled)
          continue;

        // stacks of this rule's item+quality in this origin, slot-ascending
        var stacks = new List<StockStack>();
        foreach (var s in stock)
          if (s.Origin == origin && s.ItemId == rule.ItemId && s.HQ == rule.HQ)
            stacks.Add(s);
        if (stacks.Count == 0)
          continue;

        long keep = origin == StockOrigin.Bags ? rule.KeepInBags : rule.KeepInRetainer;

        // whole stacks first; the remainder of a partially-kept stack only moves when partials are on
        long remainingKeep = keep;
        foreach (var s in stacks)
        {
          var qty = s.Quantity;
          if (remainingKeep > 0)
          {
            var leave = Math.Min((long)qty, remainingKeep);
            remainingKeep -= leave;
            qty -= (int)leave;
          }
          if (qty <= 0)
            continue;

          var unit = ItemVendorPrice.UnitFor(rule.HQ, rule.HQ, price.PriceMid, price.PriceLow, preferHq);
          var est = ItemVendorPrice.Total(unit, (long)qty);
          // 0.1.15.0: the op carries the stack's real container id (RetainerPage1-7 / Inventory1-4 /
          // crystals), NOT the origin enum - see the file header for the 0.1.12.0 defect this fixes.
          ops.Add(new VendorOp(s.Container, s.Slot, rule.ItemId, rule.HQ, (int)Math.Min(qty, int.MaxValue), est));
        }
      }
    }

    return new VendorPlan(ops, notes);
  }
}
