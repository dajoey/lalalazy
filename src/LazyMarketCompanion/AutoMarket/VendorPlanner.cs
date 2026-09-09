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

  /// <summary>
  /// Whether the retainer vendor leg can sell this item AT ALL: the Item sheet must give it a
  /// non-zero PriceLow. This is the SAME predicate <see cref="VendorPlanner.Plan"/> guards on,
  /// and deliberately the only copy of it. Until 0.1.30.0 the value gate announced its vendor
  /// set from the Universalis board price alone, without ever consulting the sheet, so an item
  /// the sheet prices at 0 - Ice Crystal, item 9 - was announced as a vendor target on
  /// essentially every sweep and then named-skipped downstream by Plan's guard ("no Item-sheet
  /// price for 9, leaving it in place" - six times on 2026-09-08, with zero vendored lines for
  /// item 9 in the whole log history). The announced count was wrong before the sweep started:
  /// on 2026-09-08 22:25 "vendoring 5 item(s)" ended as "4 vendored" for exactly this reason.
  /// The gate now calls <see cref="VendorPlanner.SplitVendorable"/> before it announces, so a
  /// change here changes both the announce and the plan, or neither.
  /// </summary>
  public static bool Vendorable(uint priceLow) => priceLow > 0;

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
/// The gate's below-threshold set, split by whether the retainer leg can actually sell each item
/// (0.1.30.0). <see cref="Sellable"/> is what the gate announces and what the leg attempts;
/// <see cref="Unvendorable"/> is named once as "not a vendor candidate" and left exactly where it
/// is - not listed (it is below the threshold, and listing sub-threshold stock is the behaviour
/// the gate exists to stop) and not vendored (the sheet gives no price to sell it at). Splitting
/// rather than dropping keeps the honest announce possible: the run still says what it saw.
/// </summary>
public sealed record VendorSplit(IReadOnlyList<ItemRule> Sellable, IReadOnlyList<ItemRule> Unvendorable);

/// <summary>
/// Maps held-back rules onto concrete stock slots. A VendorOp is only built when the slot is
/// re-read from the game immediately before the call (the caller does that in Execute) - this
/// planner works from the same snapshot the gate judged, and the executor re-verifies every slot.
/// Stacks the player marked Keep stay where they are; the keep amount is honoured per origin, taken
/// from the END of the origin's stack list so the largest stacks vendor first.
/// </summary>
public static class VendorPlanner
{
  /// <summary>
  /// Split the gate's below-threshold rules into the ones the vendor leg can sell and the ones the
  /// Item sheet gives no vendor price for (0.1.30.0). The value gate calls this BEFORE it
  /// announces, so "gate: vendoring N item(s)" names only items the leg will actually attempt and
  /// N is a number the run can reach. <paramref name="priceLowOf"/> is the caller's Item-sheet
  /// lookup (AutoMarketService.VendorPrices(id).PriceLow), passed in so this stays Dalamud-free
  /// and harness-pinned; a missing sheet row must read 0, which is what that lookup already
  /// returns. The predicate is <see cref="ItemVendorPrice.Vendorable"/> - the same one
  /// <see cref="Plan"/> guards on - so the announce and the plan cannot drift apart again.
  /// Order within each list is the caller's order, unchanged.
  /// </summary>
  public static VendorSplit SplitVendorable(IReadOnlyList<ItemRule> belowThreshold, Func<uint, uint> priceLowOf)
  {
    var sellable = new List<ItemRule>();
    var unvendorable = new List<ItemRule>();
    foreach (var rule in belowThreshold)
    {
      if (ItemVendorPrice.Vendorable(priceLowOf(rule.ItemId)))
        sellable.Add(rule);
      else
        unvendorable.Add(rule);
    }
    return new VendorSplit(sellable, unvendorable);
  }

  public static VendorPlan Plan(IReadOnlyList<ItemRule> heldRules, IReadOnlyList<StockStack> stock,
    Dictionary<uint, (uint PriceMid, uint PriceLow)> prices, bool preferHq)
  {
    var ops = new List<VendorOp>();
    var notes = new List<string>();

    foreach (var rule in heldRules)
    {
      // The gate filters this set with the SAME predicate (SplitVendorable ->
      // ItemVendorPrice.Vendorable) before it announces, so since 0.1.30.0 this note should never
      // fire on a normal sweep - it stays as the last line of defence for any other caller that
      // hands Plan an unpriced rule.
      if (!prices.TryGetValue(rule.ItemId, out var price) || !ItemVendorPrice.Vendorable(price.PriceLow))
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
