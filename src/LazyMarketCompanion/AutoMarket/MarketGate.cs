using System;
using System.Collections.Generic;
using System.Linq;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// NOTE on the namespace (PriceMath precedent): MarketSortMode is a Configuration property type, so it
// declares the PARENT namespace while this file's collaborators (ItemRule, StockStack, ItemQuote,
// MarketListingCap) live in LazyMarketCompanion.AutoMarket. The file-level using above bridges them
// without adding a using directive to any other file.

/// <summary>How Auto-Market decides which items get the retainer's free market slots when there are not enough for everything.</summary>
public enum MarketSortMode
{
  /// <summary>The order of your Auto-Market list - the behaviour before 0.1.11.0.</summary>
  ListOrder = 0,
  CheapestFirst = 1,
  /// <summary>Shipping default. Universalis sale velocity of the rule's quality: the items that actually sell get the slots first.</summary>
  FastestSellingFirst = 2,
  MostExpensiveFirst = 3,
}

/// <summary>What the value gate decided for one item.</summary>
public enum GateVerdict
{
  /// <summary>List it as normal.</summary>
  List,
  /// <summary>
  /// Below-threshold value: VENDOR the stock through the retainer instead of holding it (0.1.12.0 -
  /// corrected verdict; 0.1.11.0 shipped HoldBack). The retainer sell-items context menu offers
  /// "Have Retainer Sell Items" (Addon sheet row 5480; FCS InventoryContextEvent callbackParam=5):
  /// the retainer vendors it at the vendor price with NO 5% market fee and no market slot consumed,
  /// in the same session Auto-Market already automates. Never fires on an uncertainty.
  /// </summary>
  Vendor,
  /// <summary>
  /// Could not price this item confidently. Keep the stock exactly where it is - not vendored, not
  /// listed, not destroyed. This is the 0.1.11.0 hold-back polarity: every sell is a one-way door,
  /// so "cannot tell" (no data, stale data, no listing of the wanted quality, failed request) always
  /// lands HERE even though vendoring (not listing) would be the reversible side of the vendor call.
  /// </summary>
  HoldBack,
}

/// <summary>Log severity for the gate sight announce (0.2.2.0).</summary>
public enum GateLogLevel
{
  Debug,
  Information,
  Warning,
}

/// <summary>Everything the gate needs from the configuration, so the decision logic sees no Dalamud.</summary>
/// <param name="Enabled">Master switch. Off = every item lists, exactly as before 0.1.11.0.</param>
/// <param name="ThresholdGil">An item must be worth strictly MORE than this many gil, net of the market fee, to be listed. 0 = the gate is present but never holds anything.</param>
/// <param name="FreshnessMs">A Universalis record older than this never holds an item back - the item lists.</param>
public sealed record GateOptions(bool Enabled, long ThresholdGil, long FreshnessMs);

/// <summary>
/// Price and velocity facts about one rule's item, from FRESH Universalis data. null = nothing usable
/// was known (no data, stale, or no listing of the wanted quality); such rules keep their list order
/// at the END of every sort rather than being ranked on a guess.
/// </summary>
public sealed record RuleQuote(long UnitPrice, double VelocityPerDay);

/// <summary>
/// The Auto-Market value gate and listing order (0.1.11.0). Both halves share one Universalis fetch
/// and one rule: UNCERTAINTY ALWAYS LISTS. Vendoring an item on a guess is irreversible; listing an
/// item the gate should have held costs a market slot until it sells. Every "cannot tell" case - no
/// data, stale data, no listing of the wanted quality, a failed request - falls on the reversible side.
/// </summary>
public static class MarketGate
{
  /// <summary>
  /// Which item ids the gate's Universalis fetch should actually ask about (0.1.19.0): every rule
  /// that has stock it could sell from at least one enabled origin, judged with the SAME
  /// PotentialSellable arithmetic the verdict itself uses (including the partial-stack flooring -
  /// the fetch list and the verdict can never disagree about what "sellable" means). An id with no
  /// sellable stock can never be judged below-threshold, so asking about it is pure request weight:
  /// on the 2026-09-07 install this trims the fetch from 243 configured ids to the ~25 stocked
  /// ones, which is the difference between a request Universalis answers and the 504 Gateway
  /// Timeout that blinded every sweep that day.
  /// </summary>
  public static List<uint> GateFetchIds(IReadOnlyList<ItemRule> rules, IReadOnlyList<StockStack> stock, bool listPartialStacks, IReadOnlyList<MarketSlot>? market = null)
  {
    var ids = new List<uint>();
    foreach (var rule in rules)
    {
      if (ids.Contains(rule.ItemId))
        continue;
      if (PotentialSellable(rule, stock, listPartialStacks) <= 0)
        continue;
      ids.Add(rule.ItemId);
    }

    // 0.1.32.0 (t_4d12b8b0, the pull pass): a listed-only item has PotentialSellable == 0 above (it
    // has no STOCK left to sell) and would otherwise never get a quote, so its own gate verdict
    // could never be judged - no quote means uncertainty, and uncertainty never pulls. This ADDS
    // ids for items currently sitting in an occupied market slot under an enabled rule of the same
    // quality; it never removes or changes anything the stocked-only fetch above already asked for.
    if (market != null)
    {
      foreach (var slot in market)
      {
        if (slot.ItemId == 0 || ids.Contains(slot.ItemId))
          continue;
        if (!rules.Any(r => r.ItemId == slot.ItemId && r.HQ == slot.HQ))
          continue;
        ids.Add(slot.ItemId);
      }
    }

    return ids;
  }

  /// <summary>The judged/unpriceable split behind the 0.1.19.0 honest gate announce.</summary>
  public sealed record Sight(int Judged, int Unpriceable);

  /// <summary>
  /// Determines the log severity and message for the gate sight outcome (0.2.2.0, fixing fingerprint 558ab9dd3dd6).
  /// A fully sighted sweep logs at Information. Sweeps where stock has unconfirmed/missing prices
  /// are a handled safe condition (unconfirmed means hold, 0.2.0.0) and log at Debug, NOT Warning,
  /// preventing expected market-data gaps from triggering warning-level telemetry alerts.
  /// </summary>
  public static (GateLogLevel Level, string Message) FormatSightLog(Sight sight, int stockedCount, int rulesCount, long thresholdGil)
  {
    if (sight.Unpriceable == 0)
    {
      return (GateLogLevel.Information,
        $"[LMC] gate: every item is above the {thresholdGil:N0} gil net threshold (checked {sight.Judged} of {rulesCount} enabled item(s), {stockedCount} with stock)");
    }

    return (GateLogLevel.Debug,
      $"[LMC] gate: no price data for {sight.Unpriceable} of {sight.Judged + sight.Unpriceable} item(s) with stock to sell ({stockedCount} of {rulesCount} enabled item(s) have stock) - the {thresholdGil:N0} gil net threshold was NOT checked for those; they list wherever a market slot is free, priced by the normal pricing pass, and are never vendored or pulled without a home-world quote (0.2.8.0)");
  }

  /// <summary>
  /// How many rules the gate actually saw usable price data for (0.1.19.0; STOCKED-ONLY since
  /// 0.1.23.0). A rule is JUDGED only with a fresh, quality-matched quote; everything else (no
  /// data, stale data, wrong quality, or no request at all) is UNPRICEABLE. The gate's old
  /// announce - "every item is above the N gil net threshold" - printed identically whether every
  /// item was judged above or NOTHING was judged at all: on the 2026-09-07 runs the request 504'd,
  /// every rule read unpriceable, the gate listed everything blind, and that same line announced a
  /// clean bill of health it never had. The announce now names the difference; this record is the
  /// Dalamud-free fact it names.
  ///
  /// 0.1.23.0 scope fix: the count now runs over the rules the gate's own fetch asked about -
  /// the ones with stock to sell (PotentialSellable > 0, the same GateFetchIds arithmetic) - not
  /// over the whole enabled list. A rule with nothing to sell never gets a quote (the 0.1.19.0
  /// fetch no longer requests it) and can never be vendored or listed either, so counting it
  /// "unpriceable" claimed blindness about a decision that never happens. On the live config that
  /// over-count made every sweep print the no-data warning - even a fully sighted one - and made
  /// the clean "every item is above the threshold" announce unreachable.
  /// </summary>
  public static Sight CountSight(IReadOnlyList<ItemRule> rules, IReadOnlyDictionary<uint, ItemQuote>? quotes, bool preferHq, long nowUnixMs, long freshnessMs, IReadOnlyList<StockStack>? stock = null, bool listPartialStacks = false)
  {
    var judged = 0;
    var scopedRules = rules;
    if (stock != null)
      scopedRules = rules.Where(r => PotentialSellable(r, stock, listPartialStacks) > 0).ToList();
    foreach (var rule in scopedRules)
    {
      ItemQuote? quote = null;
      quotes?.TryGetValue(rule.ItemId, out quote);
      // 0.2.8.0: a data-center quote cannot judge the threshold question this count reports on
      // (Decide never vendors or pulls on one), so it counts as NOT checked - otherwise a fresh
      // data-center minimum under the threshold read as "judged" and the gate announced that
      // every item was above it, the false clean bill of health this record exists to prevent.
      if (UsableQuote(quote, rule.HQ, preferHq, nowUnixMs, freshnessMs) != null && quote?.DataCenterScope != true)
        judged++;
    }
    return new Sight(judged, scopedRules.Count - judged);
  }


  /// <summary>
  /// A quote the gate can actually decide on, or null. Shared by CountSight (0.1.19.0) and the
  /// verdict path so "judged" can never drift from what Decide actually saw: fresh data, and a
  /// cheapest listing of the quality the pricing pass would use at a positive price.
  /// </summary>
  public static long? UsableQuote(ItemQuote? quote, bool ruleIsHq, bool preferHq, long nowUnixMs, long freshnessMs)
  {
    if (quote == null || !quote.HasData)
      return null;
    if (quote.LastUploadUnixMs <= 0 || nowUnixMs - quote.LastUploadUnixMs > freshnessMs)
      return null;
    var unit = CheapestUnitPrice(quote, ruleIsHq, preferHq);
    if (unit == null || unit <= 0)
      return null;
    return unit;
  }

  /// <summary>
  /// Expected net gil for a quantity at a unit price, after the market's 5% sale fee, floored to whole
  /// gil. This is the number the threshold is compared against, so the threshold means NET gil.
  /// </summary>
  public static long NetRevenue(long unitPrice, long quantity)
  {
    if (unitPrice <= 0 || quantity <= 0)
      return 0;
    return unitPrice * quantity * 95 / 100;
  }

  /// <summary>
  /// How many units of this rule's item Auto-Market could list from the given stock, mirroring the
  /// planner's own arithmetic (per-origin keeps; the per-origin remainder dropped when partial stacks
  /// are off). The gate judges the item's TOTAL sellable value, not the handful of listings that happen
  /// to fit the free slots - a scarce-slot run must not judge an item on a fraction of what it could sell.
  /// </summary>
  public static long PotentialSellable(ItemRule rule, IReadOnlyList<StockStack> stock, bool listPartialStacks)
  {
    var listingSize = Math.Min(rule.StackSize, MarketListingCap.For(rule.ItemMaxStack));
    if (listingSize <= 0)
      return 0;

    long total = 0;
    foreach (var origin in new[] { StockOrigin.Bags, StockOrigin.Retainer })
    {
      var enabled = origin == StockOrigin.Bags ? rule.SellFromBags : rule.SellFromRetainer;
      if (!enabled)
        continue;

      long have = 0;
      for (var i = 0; i < stock.Count; i++)
      {
        var s = stock[i];
        if (s.Origin == origin && s.ItemId == rule.ItemId && s.HQ == rule.HQ)
          have += s.Quantity;
      }

      var keep = origin == StockOrigin.Bags ? rule.KeepInBags : rule.KeepInRetainer;
      var sellable = Math.Max(have - Math.Max(keep, 0), 0);
      if (!listPartialStacks)
        sellable -= sellable % listingSize;
      total += sellable;
    }

    return total;
  }

  /// <summary>
  /// Cheapest listing on the board of the quality the pricing pass would use (HQ only when the listing
  /// is HQ AND the user's "Use HQ price" setting is on - the same selection UniversalisPriceProvider
  /// makes), or null when the quote has nothing usable.
  /// </summary>
  public static long? CheapestUnitPrice(ItemQuote? quote, bool ruleIsHq, bool preferHq)
  {
    if (quote == null || !quote.HasData)
      return null;

    var hqOnly = preferHq && ruleIsHq;
    QuoteListing? cheapest = null;
    foreach (var listing in quote.Listings)
    {
      if (listing.PricePerUnit <= 0 || (hqOnly && !listing.Hq))
        continue;
      if (cheapest == null || listing.PricePerUnit < cheapest.PricePerUnit)
        cheapest = listing;
    }

    return cheapest?.PricePerUnit;
  }

  /// <summary>
  /// The gate for one item AFTER the request has completed successfully: judged on its total sellable
  /// value at the current board price, net of the 5% market fee. An item must be worth STRICTLY more
  /// than the threshold to list - at exactly or under the threshold it is a VENDOR CANDIDATE for the
  /// bounded junk path (0.2.0.0 VendorPolicy).
  ///
  /// 0.2.8.0 - the plugin's job is to LIST what is on the Auto-Market list, so every "cannot tell"
  /// LISTS: an item whose quote is missing, stale, empty or has no listing of the quality is not
  /// known to be junk, and the listing price comes from the live board through Auto Pinch, never
  /// from this quote. 0.2.0.0 made the same cases HOLD, which left 83 marked items unlisted in one
  /// field session. What stays conservative is everything IRREVERSIBLE: only a fresh, positive
  /// quote from the home world itself can produce Vendor (and so a pull). A data-center quote is
  /// the cheapest listing on any of eight worlds, not this world's price (2026-09-28: eight stacks
  /// vendored for about 245 gil that list at about 12,500 gil at home), so it lists and never
  /// vendors. Caller contract: a request that fails, times out or returns no data never calls
  /// this function - such rules take <see cref="DecideUncertain"/>, which also lists.
  /// </summary>
  public static GateVerdict Decide(long sellableQuantity, ItemQuote? quote, bool ruleIsHq, bool preferHq, GateOptions options, long nowUnixMs)
  {
    if (!options.Enabled || options.ThresholdGil <= 0)
      return GateVerdict.List;
    if (sellableQuantity <= 0)
      return GateVerdict.List;
    if (quote == null || !quote.HasData)
      return GateVerdict.List;
    if (quote.LastUploadUnixMs <= 0 || nowUnixMs - quote.LastUploadUnixMs > options.FreshnessMs)
      return GateVerdict.List;

    var unit = CheapestUnitPrice(quote, ruleIsHq, preferHq);
    if (unit == null || unit <= 0)
      return GateVerdict.List;

    if (NetRevenue(unit.Value, sellableQuantity) > options.ThresholdGil)
      return GateVerdict.List;

    // At or under the threshold: a vendor candidate ONLY on the home world's own fresh quote.
    return quote.DataCenterScope ? GateVerdict.List : GateVerdict.Vendor;
  }

  /// <summary>
  /// The wire-side gate (0.1.12.0 split): decides what to do with a rule whose Universalis request
  /// FAILED, TIMED OUT, or was superseded before a verdict could be read. The rule never sat in front
  /// of the priced Decide() in that state - there was no price to judge anything with. 0.2.8.0: it
  /// LISTS (the plugin's job; the price comes from the live board through Auto Pinch), and it can
  /// never vendor or pull, because no verdict of record exists. Kept as its own function so the
  /// contract "uncertainty arrives here without a verdict of record" is visible at the call site,
  /// not reconstructed by inlining a default.
  /// </summary>
  public static GateVerdict DecideUncertain() => GateVerdict.List;

  /// <summary>
  /// Price + velocity for each rule from fresh quotes (see <see cref="RuleQuote"/> for what "not
  /// usable" means). The list is parallel to <paramref name="rules"/>.
  /// </summary>
  public static List<RuleQuote?> RuleQuotes(IReadOnlyList<ItemRule> rules, IReadOnlyDictionary<uint, ItemQuote>? quotes, bool preferHq, long nowUnixMs, long freshnessMs)
  {
    var result = new List<RuleQuote?>(rules.Count);
    foreach (var rule in rules)
    {
      ItemQuote? quote = null;
      quotes?.TryGetValue(rule.ItemId, out quote);
      if (quote == null || !quote.HasData
          || quote.LastUploadUnixMs <= 0
          || nowUnixMs - quote.LastUploadUnixMs > freshnessMs)
      {
        result.Add(null);
        continue;
      }

      var unit = CheapestUnitPrice(quote, rule.HQ, preferHq);
      if (unit == null || unit <= 0)
      {
        // A board with no listing of the wanted quality has no price to rank or judge by.
        result.Add(null);
        continue;
      }

      // Quality-specific velocity: an NQ rule ranked on the combined figure would ride HQ sales it
      // cannot get. Zero is a legitimate reading (it really does not sell), not "unknown".
      var velocity = rule.HQ ? quote.HqVelocityPerDay : quote.NqVelocityPerDay;
      result.Add(new RuleQuote(unit.Value, velocity));
    }

    return result;
  }

  /// <summary>
  /// Order the rules for slot allocation. ListOrder returns the input untouched. The data-backed modes
  /// rank only rules with fresh price data; unknowns keep their relative list order at the END, and
  /// ties keep list order (OrderBy/OrderByDescending are stable). ListOrder with a two-element list
  /// short-circuits, so the no-data path never allocates.
  /// </summary>
  public static List<ItemRule> SortRules(IReadOnlyList<ItemRule> rules, IReadOnlyList<RuleQuote?> quotesByRule, MarketSortMode mode)
  {
    if (mode == MarketSortMode.ListOrder || rules.Count < 2)
      return rules.ToList();

    var indexed = rules.Select((rule, i) => (rule, quote: i < quotesByRule.Count ? quotesByRule[i] : null));
    IEnumerable<ItemRule> ordered = mode switch
    {
      MarketSortMode.CheapestFirst => indexed.OrderBy(x => x.quote?.UnitPrice ?? long.MaxValue).Select(x => x.rule),
      MarketSortMode.MostExpensiveFirst => indexed.OrderByDescending(x => x.quote?.UnitPrice ?? long.MinValue).Select(x => x.rule),
      _ => indexed.OrderByDescending(x => x.quote?.VelocityPerDay ?? -1.0).Select(x => x.rule),
    };
    return ordered.ToList();
  }
}

