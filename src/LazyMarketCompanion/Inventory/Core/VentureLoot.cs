using System;
using System.Collections.Generic;
using System.Linq;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion.Inventory;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (Inventory cases).
//
// VENTURE LOOT - the definition (the most conservative one that still covers the measured gap: ~28 random
// Quick Exploration rewards a day landing in retainers with nothing sorting them):
//
//   A stack in one retainer's own inventory pages (RetainerPage1-7; never the crystals page, never the
//   market) whose item AND quality appear in AutoRetainer's venture log for THAT retainer as a Quick
//   Exploration reward (venture 395) within the look-back window (default 30 days), and where the retainer
//   holds NO MORE of that item+quality than those records delivered in the window.
//
// The quantity cap is what makes the definition safe for stackable items: a material the player entrusted
// by hand and that also came from a venture is "mixed" stock, is shown as not-venture-loot, and is never
// sorted. No venture log (AutoRetainer absent, or its "Record Venture Statistics" off) means nothing is
// venture loot - the tab says why.
//
// BUCKETS, after the rails. The rails come first and win: a railed stack is KEEP whatever else is true.
//   Rails (KEEP, with the reason): collectable; ANY LMC Auto-Market entry for the item (either quality -
//   market stock is never sorted); AutoRetainer config unreadable; named by ANY AutoRetainer entrust plan
//   (item list or ItemUICategory, assigned or not); a "duplicates" plan on this retainer while the bags hold
//   a copy; the AutoRetainer protect list; referenced by a gearset; unique / untradable / rare (blue or
//   better) unless the player opted that item in; HQ unless the HQ setting is on; sheet miss.
//   Then, for a MARKETABLE item (the grey-marker set), the Auto-Market value gate decides with the same
//   MarketGate.Decide the listing path uses, over everything of it the retainer holds:
//     List (worth more than the threshold) -> KEEP ("worth listing").
//     no fresh quote / prices not checked recently / gate off -> KEEP (uncertainty never vendors).
//     Vendor (fresh, quality-matched, at or under the threshold) -> eligible for a bucket below.
//   So grey stock only ever leaves KEEP when the gate has PROVEN it is not worth a market slot.
//   Buckets for what is left (unmarketable stock, or marketable stock proven below the threshold):
//     GC DELIVERY - green (uncommon) equippable gear: expert-delivery fodder, worth seals, so it is never
//                   vendored. PREVIEW ONLY in v1 (AutoDuty / AutoRetainer deliver it from the bags).
//     VENDOR      - marketable, proven below the threshold, with an Item-sheet vendor price. LIVE: sold
//                   through the retainer ("Have Retainer Sell Items", AutoMarketService.ExecuteVendor).
//     DESYNTH     - desynthesizable. PREVIEW ONLY in v1 (PandorasBox / AutoRetainer desynthesis).
//     KEEP        - everything else, with the reason.
//   An unmarketable item is never VENDOR: the gate cannot price it, and v1 never vendors what the gate
//   cannot judge.

/// <summary>One occupied retainer-page slot. Container is the game InventoryType value (RetainerPage1-7 = 10000-10006).</summary>
public sealed record RetainerStack(int Container, int Slot, uint ItemId, bool Hq, int Quantity, bool Collectable);

public enum LootBucket { Vendor, GcDelivery, Desynth, Keep }

/// <param name="LookbackDays">Venture records older than this do not count (clamped 1..365).</param>
/// <param name="AllowHq">Sort HQ venture loot too (default off: HQ is kept).</param>
/// <param name="OptIns">Item ids the player explicitly allowed past the unique / untradable / rare rail.</param>
/// <param name="Gate">The Auto-Market value gate (enabled, threshold, freshness) - the same one listing uses.</param>
/// <param name="QuotesFetchedUnixMs">When the quotes in the input were fetched (0 = never).</param>
/// <param name="QuoteMaxAgeMs">Older fetches never make a stack actionable (prices must be re-checked).</param>
public sealed record LootOptions(
  int LookbackDays,
  bool AllowHq,
  IReadOnlySet<uint> OptIns,
  GateOptions Gate,
  bool PreferHq,
  long NowUnixMs,
  long QuotesFetchedUnixMs,
  long QuoteMaxAgeMs);

/// <param name="Records">This retainer's AutoRetainer venture records (another retainer's never count).</param>
/// <param name="BagItemIds">Item ids in the player's bags right now (for AutoRetainer's duplicates rule).</param>
public sealed record LootInput(
  string RetainerName,
  IReadOnlyList<RetainerStack> Stacks,
  IReadOnlyList<VentureRecord> Records,
  Func<uint, ItemFacts?> FactsOf,
  IReadOnlyList<AmEntry> AutoMarket,
  ArSnapshot Ar,
  IReadOnlySet<uint> GearsetItems,
  IReadOnlySet<uint> BagItemIds,
  IReadOnlyDictionary<uint, ItemQuote>? Quotes);

/// <param name="Actionable">Only VENDOR rows can be; true = the live button may sell this stack.</param>
/// <param name="OptInRail">"unique" / "untradable" / "rare" when the only thing keeping it is the opt-in rail.</param>
public sealed record LootRow(RetainerStack Stack, LootBucket Bucket, string Reason, bool Actionable, long EstGil, string? OptInRail, uint Delivered, bool IsLoot);

public sealed record LootPlan(IReadOnlyList<LootRow> Rows, IReadOnlyList<string> Notes)
{
  public int Count(LootBucket b) => Rows.Count(r => r.IsLoot && r.Bucket == b);
}

public static class VentureLoot
{
  public static bool IsRetainerPage(int container) => container is >= 10000 and <= 10006;

  public static LootPlan Classify(LootInput input, LootOptions opt)
  {
    var rows = new List<LootRow>();
    var notes = new List<string>();

    if (!input.Ar.Available)
      notes.Add($"AutoRetainer: {input.Ar.Status}. Entrust plans cannot be checked, so nothing is sorted.");
    else if (!input.Ar.RecordStats)
      notes.Add("AutoRetainer's \"Record Venture Statistics\" is off: no new venture rewards are logged, so new loot is not recognised.");
    if (input.Records.Count == 0)
      notes.Add("No AutoRetainer venture log for this retainer - nothing here is venture loot.");

    var lookbackDays = Math.Clamp(opt.LookbackDays, 1, 365);
    var windowStartSec = opt.NowUnixMs / 1000 - lookbackDays * 86_400L;
    var nowSec = opt.NowUnixMs / 1000;

    var delivered = new Dictionary<(uint, bool), uint>();
    foreach (var r in input.Records)
    {
      if (r.VentureId != ArVentureStats.QuickExplorationVentureId)
        continue;
      if (r.UnixSeconds < windowStartSec || r.UnixSeconds > nowSec + 3_600)
        continue;
      var key = (r.ItemId, r.Hq);
      delivered[key] = (delivered.TryGetValue(key, out var d) ? d : 0u) + r.Amount;
    }

    var pages = input.Stacks.Where(s => IsRetainerPage(s.Container) && s.ItemId != 0 && s.Quantity > 0).ToList();
    var held = new Dictionary<(uint, bool), long>();
    foreach (var s in pages)
    {
      var key = (s.ItemId, s.Hq);
      held[key] = (held.TryGetValue(key, out var h) ? h : 0L) + s.Quantity;
    }

    foreach (var s in pages.OrderBy(p => p.Container).ThenBy(p => p.Slot))
    {
      if (!delivered.TryGetValue((s.ItemId, s.Hq), out var got) || got == 0)
        continue; // not venture loot - not shown at all

      var holds = held[(s.ItemId, s.Hq)];
      if (holds > got)
      {
        rows.Add(new LootRow(s, LootBucket.Keep,
          $"not venture loot: this retainer holds {holds}, Quick Exploration delivered {got} in {lookbackDays} days (mixed stock)",
          false, 0, null, got, false));
        continue;
      }

      rows.Add(ClassifyOne(input, opt, s, got, holds));
    }

    return new LootPlan(rows, notes);
  }

  private static LootRow ClassifyOne(LootInput input, LootOptions opt, RetainerStack s, uint got, long holds)
  {
    LootRow Keep(string reason, string? optIn = null) => new(s, LootBucket.Keep, reason, false, 0, optIn, got, true);

    var facts = input.FactsOf(s.ItemId);
    if (facts == null)
      return Keep("not found in the game data");
    if (s.Collectable)
      return Keep("collectable");
    if (input.AutoMarket.Any(e => e.ItemId == s.ItemId))
      return Keep("on the LMC Auto-Market list - market stock is never sorted");
    if (!input.Ar.Available)
      return Keep("AutoRetainer config unreadable - entrust plans cannot be checked");
    if (input.Ar.AnyPlanNames(s.ItemId, facts.UiCategory))
      return Keep("on an AutoRetainer entrust plan");
    var plan = input.Ar.PlanFor(input.RetainerName);
    if (plan is { Duplicates: true } && input.BagItemIds.Contains(s.ItemId))
      return Keep($"AutoRetainer entrusts duplicates into {input.RetainerName} and the bags hold a copy");
    if (input.Ar.Im.Protect.Contains(s.ItemId))
      return Keep("on the AutoRetainer protect list");
    if (input.GearsetItems.Contains(s.ItemId))
      return Keep("in a gearset");
    if (!opt.OptIns.Contains(s.ItemId))
    {
      if (facts.Unique)
        return Keep("unique - sorted only after a per-item opt-in", "unique");
      if (facts.Untradable)
        return Keep("untradable - sorted only after a per-item opt-in", "untradable");
      if (facts.Rare)
        return Keep("rare (blue or better) - sorted only after a per-item opt-in", "rare");
    }
    if (s.Hq && !opt.AllowHq)
      return Keep("HQ - kept unless \"Sort HQ venture loot\" is on");

    // The value gate: for marketable stock, only a PROVEN below-threshold verdict leaves KEEP.
    var belowGate = false;
    long netOnBoard = 0;
    if (facts.Marketable)
    {
      if (!opt.Gate.Enabled || opt.Gate.ThresholdGil <= 0)
        return Keep("worth-listing check is off (Auto-Market value gate disabled) - uncertainty never vendors");
      var fetchAge = opt.NowUnixMs - opt.QuotesFetchedUnixMs;
      if (input.Quotes == null || opt.QuotesFetchedUnixMs <= 0)
        return Keep("prices not checked yet - uncertainty never vendors");
      if (fetchAge > opt.QuoteMaxAgeMs || fetchAge < 0)
        return Keep("prices were checked too long ago - check again");
      input.Quotes.TryGetValue(s.ItemId, out var quote);
      var unit = MarketGate.UsableQuote(quote, s.Hq, opt.PreferHq, opt.NowUnixMs, opt.Gate.FreshnessMs);
      if (unit == null)
        return Keep("no fresh board price - uncertainty never vendors");
      netOnBoard = MarketGate.NetRevenue(unit.Value, holds);
      var verdict = MarketGate.Decide(holds, quote, s.Hq, opt.PreferHq, opt.Gate, opt.NowUnixMs);
      if (verdict != GateVerdict.Vendor)
        return Keep($"worth listing: ~{netOnBoard:N0} gil net on the board (over the {opt.Gate.ThresholdGil:N0} gil gate)");
      belowGate = true;
    }

    if (facts.Equippable && facts.Rarity == 2)
      return new LootRow(s, LootBucket.GcDelivery,
        "green gear: Grand Company expert delivery candidate (AutoDuty / AutoRetainer deliver it from the bags; LMC does not deliver)",
        false, 0, null, got, true);

    if (belowGate && ItemVendorPrice.Vendorable(facts.PriceLow))
    {
      var unit = ItemVendorPrice.UnitFor(s.Hq, s.Hq, facts.PriceMid, facts.PriceLow, opt.PreferHq);
      var est = ItemVendorPrice.Total(unit, s.Quantity);
      return new LootRow(s, LootBucket.Vendor,
        $"not worth a market slot: ~{netOnBoard:N0} gil net on the board, at or under the {opt.Gate.ThresholdGil:N0} gil gate",
        true, est, null, got, true);
    }

    if (facts.Desynth > 0)
      return new LootRow(s, LootBucket.Desynth,
        "desynthesizable (PandorasBox Desynth All / AutoRetainer desynthesis act on the bags; LMC does not desynthesize)",
        false, 0, null, got, true);

    return belowGate
      ? Keep("below the gate, but the game gives it no vendor price")
      : Keep("cannot be listed on the market; v1 never vendors what the value gate cannot price");
  }

  /// <summary>
  /// The live VENDOR button's ops: actionable VENDOR rows only, and only for real retainer-page slots
  /// (the container check VendorOp itself enforces is repeated here so a planner bug can never produce one).
  /// </summary>
  public static List<VendorOp> VendorOps(LootPlan plan)
  {
    var ops = new List<VendorOp>();
    foreach (var r in plan.Rows)
    {
      if (!r.IsLoot || r.Bucket != LootBucket.Vendor || !r.Actionable || !IsRetainerPage(r.Stack.Container))
        continue;
      var op = new VendorOp(r.Stack.Container, r.Stack.Slot, r.Stack.ItemId, r.Stack.Hq, r.Stack.Quantity, r.EstGil);
      if (op.HasKnownContainer)
        ops.Add(op);
    }
    return ops;
  }
}
