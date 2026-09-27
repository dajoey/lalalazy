using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 131).
//
// 0.1.70.0: the delist pass. Active marketboard listings whose confirmed market net (after the 5%
// market fee, floored) is strictly below the item's vendor price (ItemVendorPrice, as the value
// gate uses) are removed from the market board and routed to the retainer vendor leg ("Have Retainer
// Sell Items"). Listings at or above vendor value are untouched. Uncertainty (null quote, no data,
// stale quote, missing quality quote) NEVER removes (same doctrine as 0.1.69.0: removing blind is the
// inverse error). Items with no Item-sheet vendor price (PriceLow == 0) are untouched.

/// <summary>Decision on an active marketboard listing.</summary>
public enum DelistVerdict
{
  /// <summary>Confirmed market net is strictly below vendor price: remove and route to the vendor leg.</summary>
  Remove,
  /// <summary>Confirmed market net is at or above vendor price: keep the listing on the board.</summary>
  KeepAboveVendor,
  /// <summary>No confirmed market quote: keep the listing on the board (never remove blind on uncertainty).</summary>
  KeepUnpriceable,
  /// <summary>Item has no vendor price in the Item sheet (PriceLow == 0): keep on the board.</summary>
  KeepNoVendorPrice,
}

/// <summary>The result of inspecting one active market slot against the vendor value rule.</summary>
public sealed record DelistInspection(
  MarketSlot Slot,
  DelistVerdict Verdict,
  long MarketNet,
  long VendorValue,
  string Reason);

/// <summary>One market slot to remove from the board to inventory.</summary>
public sealed record DelistOp(
  int Slot,
  uint ItemId,
  bool HQ,
  int Quantity,
  long MarketNet,
  long VendorValue,
  PullTarget Target);

/// <summary>The delist pass plan for a retainer session.</summary>
public sealed record DelistPlan(
  IReadOnlyList<DelistOp> Ops,
  IReadOnlyList<DelistInspection> Inspections,
  IReadOnlyList<string> Notes,
  bool StoppedForSpace)
{
  public int RemovedCount => Ops.Count;
  public int KeptCount => Inspections.Count(i => i.Verdict != DelistVerdict.Remove);
  public int KeptAboveVendorCount => Inspections.Count(i => i.Verdict == DelistVerdict.KeepAboveVendor);
  public int KeptUnpriceableCount => Inspections.Count(i => i.Verdict == DelistVerdict.KeepUnpriceable);
  public int KeptNoVendorPriceCount => Inspections.Count(i => i.Verdict == DelistVerdict.KeepNoVendorPrice);

  public string Summary()
  {
    if (Inspections.Count == 0)
      return "no active listings on this retainer";
    if (RemovedCount == 0 && KeptUnpriceableCount == 0 && KeptNoVendorPriceCount == 0)
      return $"every listing is at or above vendor value (checked {Inspections.Count} listing(s))";
    return $"{RemovedCount} removed to vendor; {KeptCount} left ({KeptAboveVendorCount} above vendor, {KeptUnpriceableCount} unpriced, {KeptNoVendorPriceCount} no vendor price)";
  }
}

public static class MarketDelist
{
  /// <summary>
  /// Inspects one active market slot against the confirmed market quote and the item's vendor price.
  /// Confirmed market net &lt; vendor value -&gt; Remove. Net &gt;= vendor -&gt; KeepAboveVendor.
  /// Unconfirmed/stale quote -&gt; KeepUnpriceable. No vendor price -&gt; KeepNoVendorPrice.
  /// </summary>
  public static DelistInspection Inspect(
    MarketSlot slot,
    ItemQuote? quote,
    (uint PriceMid, uint PriceLow) vendorPrices,
    bool preferHq,
    long nowUnixMs,
    long freshnessMs)
  {
    if (!ItemVendorPrice.Vendorable(vendorPrices.PriceLow))
    {
      return new DelistInspection(slot, DelistVerdict.KeepNoVendorPrice, 0, 0,
        $"item {slot.ItemId}: no Item-sheet vendor price (PriceLow=0); untouched");
    }

    var vendorUnit = ItemVendorPrice.UnitFor(slot.HQ, slot.HQ, vendorPrices.PriceMid, vendorPrices.PriceLow, preferHq);
    if (vendorUnit <= 0)
    {
      return new DelistInspection(slot, DelistVerdict.KeepNoVendorPrice, 0, 0,
        $"item {slot.ItemId}: calculated vendor unit price <= 0; untouched");
    }
    var vendorTotal = ItemVendorPrice.Total(vendorUnit, slot.Quantity);

    var marketUnit = MarketGate.UsableQuote(quote, slot.HQ, preferHq, nowUnixMs, freshnessMs);
    if (marketUnit == null || marketUnit <= 0)
    {
      return new DelistInspection(slot, DelistVerdict.KeepUnpriceable, 0, vendorTotal,
        $"item {slot.ItemId}: no confirmed market price; untouched (never remove blind)");
    }

    var marketNet = MarketGate.NetRevenue(marketUnit.Value, slot.Quantity);
    if (marketNet < vendorTotal)
    {
      return new DelistInspection(slot, DelistVerdict.Remove, marketNet, vendorTotal,
        $"item {slot.ItemId}{(slot.HQ ? " HQ" : "")} x{slot.Quantity}: market net {marketNet:N0}g < vendor value {vendorTotal:N0}g (unit market {marketUnit.Value:N0}g vs vendor {vendorUnit:N0}g); remove to vendor");
    }

    return new DelistInspection(slot, DelistVerdict.KeepAboveVendor, marketNet, vendorTotal,
      $"item {slot.ItemId}{(slot.HQ ? " HQ" : "")} x{slot.Quantity}: market net {marketNet:N0}g >= vendor value {vendorTotal:N0}g; untouched");
  }

  /// <summary>
  /// Plans the delist pass across all active market slots of a retainer.
  /// </summary>
  public static DelistPlan Plan(
    IReadOnlyList<MarketSlot> market,
    IReadOnlyDictionary<uint, (uint PriceMid, uint PriceLow)> vendorPrices,
    IReadOnlyDictionary<uint, ItemQuote>? quotes,
    bool preferHq,
    long nowUnixMs,
    long freshnessMs,
    Func<int> freeRetainerPageSlots,
    Func<bool> hasFreeBagSlot)
  {
    var ops = new List<DelistOp>();
    var inspections = new List<DelistInspection>();
    var notes = new List<string>();

    var freeSlots = freeRetainerPageSlots();
    var playerBagsUsedThisPass = false;
    var stoppedForSpace = false;

    foreach (var slot in market)
    {
      if (slot.ItemId == 0 || slot.Quantity <= 0)
        continue;

      vendorPrices.TryGetValue(slot.ItemId, out var vp);
      ItemQuote? quote = null;
      quotes?.TryGetValue(slot.ItemId, out quote);

      var inspection = Inspect(slot, quote, vp, preferHq, nowUnixMs, freshnessMs);
      inspections.Add(inspection);

      if (inspection.Verdict != DelistVerdict.Remove)
        continue;

      if (freeSlots > 0)
      {
        ops.Add(new DelistOp(slot.Slot, slot.ItemId, slot.HQ, slot.Quantity, inspection.MarketNet, inspection.VendorValue, PullTarget.RetainerInventory));
        freeSlots--;
        continue;
      }

      if (!playerBagsUsedThisPass && hasFreeBagSlot())
      {
        ops.Add(new DelistOp(slot.Slot, slot.ItemId, slot.HQ, slot.Quantity, inspection.MarketNet, inspection.VendorValue, PullTarget.PlayerBags));
        playerBagsUsedThisPass = true;
        continue;
      }

      notes.Add("delist: stopping removal pass for this retainer (nowhere for further stacks to land); remaining under-vendor listings stay on the board");
      stoppedForSpace = true;
      break;
    }

    return new DelistPlan(ops, inspections, notes, stoppedForSpace);
  }
}
