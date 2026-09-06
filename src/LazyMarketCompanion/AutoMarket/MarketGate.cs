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
  /// Expected net value at or under the threshold: do not list it this pass. The stock stays exactly
  /// where it is - nothing is sold, vendored or destroyed. (The original 2026-09-06 ask was to vendor
  /// below-threshold stock through the retainer; the retainer bell menu has no vendor option - a
  /// retainer sells on the market board only - and every sell-to-vendor path in the game goes through
  /// an NPC shop, so the reversible hold-back is what ships.)
  /// </summary>
  HoldBack,
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
  /// The gate for one item, judged on its total sellable value at the current board price, net of the
  /// 5% market fee. An item must be worth STRICTLY more than the threshold to list - at exactly the
  /// threshold it is held. Everything the data cannot answer LISTS: gate off, threshold 0, nothing
  /// sellable, no quote, hasData=false, stale lastUploadTime, or no listing of the wanted quality.
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

    return NetRevenue(unit.Value, sellableQuantity) > options.ThresholdGil
      ? GateVerdict.List
      : GateVerdict.HoldBack;
  }

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
