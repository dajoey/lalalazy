using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>One listing on the Universalis board for an item.</summary>
public sealed record QuoteListing(long PricePerUnit, bool Hq, bool OwnRetainer);

/// <summary>What Universalis knows about one item right now.</summary>
/// <param name="LastUploadUnixMs">
/// Universalis' own <c>lastUploadTime</c>, in unix MILLISECONDS (not seconds - it is the one field on that
/// API that is not in seconds). 0 means it did not tell us, which is treated as stale.
/// </param>
public sealed record ItemQuote(uint ItemId, bool HasData, long LastUploadUnixMs, IReadOnlyList<QuoteListing> Listings);

/// <summary>One row of the open sell list, as the pre-flight sees it.</summary>
/// <param name="CurrentPrice">The asking price the listing carries right now, read off the market container.</param>
/// <param name="IsPlaceholder">The listing is still at the Auto-Market placeholder price, i.e. it has never been priced.</param>
public sealed record PinchRow(int Row, int Slot, uint ItemId, bool HQ, long CurrentPrice, bool IsPlaceholder);

/// <summary>What the pre-flight decided to do with a row.</summary>
public enum PinchVerdict
{
  /// <summary>Open the row and price it, exactly as every version before 0.1.9.0 always did.</summary>
  Walk,
  /// <summary>The pricing pass would write back the number the listing already has.</summary>
  SkipAlreadyRight,
  /// <summary>The pricing pass would move the price by less than the user's "worth it" threshold.</summary>
  SkipUnderThreshold,
}

/// <summary>A row plus the verdict, the price the pass was predicted to write, and why.</summary>
public sealed record PinchDecision(PinchRow Row, PinchVerdict Verdict, long Candidate, string Reason);

/// <summary>Everything the pre-flight needs from the configuration, so the decision logic sees no Dalamud.</summary>
/// <param name="Enabled">Master switch (<c>AutoPinchPreflightEnabled</c>). Off = every row walks, i.e. pre-0.1.9.0 behaviour.</param>
/// <param name="FreshnessHours">Universalis data older than this always walks the row.</param>
/// <param name="SkipUnderGil">Skip a row whose price would move by fewer than this many gil. 0 = off.</param>
/// <param name="SkipUnderPercent">Skip a row whose price would move by less than this percent of the current price. 0 = off.</param>
/// <param name="PreferHq">The user's <c>HQ</c> setting: an HQ listing is priced off HQ listings only.</param>
public sealed record PinchPreflightOptions(
  bool Enabled,
  int FreshnessHours,
  int SkipUnderGil,
  float SkipUnderPercent,
  bool PreferHq,
  UndercutMode Mode,
  int UndercutAmount,
  bool UndercutSelf);

/// <summary>
/// Decides, BEFORE any context menu is opened, which sell-list rows are worth walking.
///
/// WHY THIS EXISTS. Joey's 2026-09-06 11:26-11:36 Auto Pinch sweep priced 55 rows. 39 of them were existing
/// listings being re-priced and 17 of those 39 (44%) came out at EXACTLY the price they already had, plus 3
/// rounding-error moves (243 -> 242, 400 -> 399, 30971 -> 30951). At a measured median of 10.5 s per row that
/// is about 3 minutes of a 9.5-minute sweep spent writing numbers back unchanged. The cause is not a bug: he
/// is already the cheapest on the data centre for those items and <c>UndercutSelf</c> is off, so the matched
/// price IS his own price. That is precisely the condition this class detects from the Universalis board.
///
/// WHAT IT IS NOT. It is a PREDICTION, not the pricing pass. The pricing pass reads the in-game Compare Prices
/// window; the pre-flight reads Universalis, which is crowd-sourced and lags. When the two disagree the
/// prediction can be wrong, so every rule below resolves uncertainty by WALKING the row. A needless walk costs
/// ten seconds; a wrong skip leaves a listing overpriced until the next sweep.
///
/// The rules, in order:
///   1. a listing still at the placeholder price ALWAYS walks - a stranded new listing must never be skipped;
///   2. an unreadable row (no item id, no price) walks;
///   3. no quote, <c>hasData=false</c>, or no listing of the quality we would price against, walks;
///   4. a quote older than the freshness window walks;
///   5. otherwise the candidate price is computed with <see cref="PriceMath.Candidate"/> - the SAME formula the
///      pricing pass uses, deliberately not a copy of it;
///   6. candidate == current price  =>  skip, nothing would change;
///   7. the move is smaller than the user's gil/percent threshold  =>  skip;
///   8. anything else walks.
/// </summary>
public static class PinchPreflight
{
  /// <param name="rows">Every row of the open sell list with the price the container says it carries.</param>
  /// <param name="quotes">Universalis quotes by item id; a missing item is simply a row that walks.</param>
  /// <param name="nowUnixMs">Current time in unix milliseconds, for the freshness window.</param>
  /// <param name="applyItemLimit">
  /// The user's per-item min/max price limit, applied to the candidate before it is compared with the current
  /// price - otherwise a row whose candidate is clamped back to its current price would look like a change.
  /// Passed as a delegate so this file stays Dalamud-free. Null = no limits.
  /// </param>
  public static List<PinchDecision> Decide(
    IReadOnlyList<PinchRow> rows,
    IReadOnlyDictionary<uint, ItemQuote> quotes,
    PinchPreflightOptions options,
    long nowUnixMs,
    Func<uint, int, int>? applyItemLimit = null)
  {
    var decisions = new List<PinchDecision>(rows.Count);

    foreach (var row in rows)
    {
      // Rule 0 - the feature is off. Every row walks; this is exactly what 0.1.7.0 did.
      if (!options.Enabled)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "pre-flight disabled"));
        continue;
      }

      // Rule 1 - a new listing sitting at the placeholder price is never, under any circumstances, skipped.
      // It has never been priced, so "the price is already right" cannot be true of it, and leaving it
      // stranded at 999,999,999 gil means it silently never sells.
      if (row.IsPlaceholder)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "new listing at the placeholder price"));
        continue;
      }

      // Rule 2 - the row could not be read. Nothing below can be trusted about it.
      if (row.ItemId == 0 || row.CurrentPrice <= 0)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "row could not be read"));
        continue;
      }

      // Rule 3 - Universalis has nothing usable for this item.
      if (!quotes.TryGetValue(row.ItemId, out var quote) || quote == null || !quote.HasData)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "no Universalis data"));
        continue;
      }

      // Rule 4 - the data is too old to predict from. A missing or zero timestamp counts as stale.
      if (quote.LastUploadUnixMs <= 0
          || nowUnixMs - quote.LastUploadUnixMs > (long)Math.Max(options.FreshnessHours, 1) * 3_600_000L)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "Universalis data is stale"));
        continue;
      }

      // Rule 5 - the candidate price, from the same formula the pricing pass uses. Quality selection mirrors
      // UniversalisPriceProvider.GetNewPriceById exactly: HQ listings only when the listing is HQ AND the
      // user's "Use HQ price" setting is on, otherwise the cheapest listing of any quality.
      var hqOnly = options.PreferHq && row.HQ;
      var lowest = quote.Listings
        .Where(l => l.PricePerUnit > 0 && (!hqOnly || l.Hq))
        .OrderBy(l => l.PricePerUnit)
        .FirstOrDefault();

      if (lowest == null)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, hqOnly ? "no HQ listing on the board" : "no listing on the board"));
        continue;
      }

      var candidate = (long)PriceMath.Candidate(lowest.PricePerUnit, lowest.OwnRetainer, options.Mode, options.UndercutAmount, options.UndercutSelf);
      if (applyItemLimit != null)
        candidate = applyItemLimit(row.ItemId, (int)Math.Min(candidate, int.MaxValue));

      // Rule 6 - the pass would write back the number that is already there. This is the 17-of-39 case.
      if (candidate == row.CurrentPrice)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.SkipAlreadyRight, candidate, "already at the price this pass would set"));
        continue;
      }

      // Rule 7 - the move is real but too small to be worth ten seconds.
      var improvement = Math.Abs(candidate - row.CurrentPrice);
      if (options.SkipUnderGil > 0 && improvement < options.SkipUnderGil)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.SkipUnderThreshold, candidate, $"would move {improvement} gil, under the {options.SkipUnderGil} gil threshold"));
        continue;
      }

      if (options.SkipUnderPercent > 0 && improvement * 100.0 < options.SkipUnderPercent * row.CurrentPrice)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.SkipUnderThreshold, candidate, $"would move {improvement} gil on {row.CurrentPrice}, under the {options.SkipUnderPercent:0.##}% threshold"));
        continue;
      }

      // Rule 8 - worth walking.
      decisions.Add(new PinchDecision(row, PinchVerdict.Walk, candidate, $"would move {row.CurrentPrice} -> {candidate}"));
    }

    return decisions;
  }

  /// <summary>
  /// The one INFO line the pass logs. Kept here (and harness-covered) because it is how this feature gets
  /// graded from Joey's client log afterwards: grep <c>pinch pre-flight:</c> and compare walked-vs-total
  /// against a pre-fix slice as a control.
  /// </summary>
  public static string Summarize(IReadOnlyList<PinchDecision> decisions, int freshnessHours)
  {
    var total = decisions.Count;
    var walked = decisions.Count(d => d.Verdict == PinchVerdict.Walk);
    var alreadyRight = decisions.Count(d => d.Verdict == PinchVerdict.SkipAlreadyRight);
    var underThreshold = decisions.Count(d => d.Verdict == PinchVerdict.SkipUnderThreshold);

    var reasons = new List<string>();
    if (alreadyRight > 0) reasons.Add($"{alreadyRight} already at the right price");
    if (underThreshold > 0) reasons.Add($"{underThreshold} under the threshold");

    var skipped = reasons.Count == 0 ? "skipped nothing" : "skipped " + string.Join(", ", reasons);
    return $"pinch pre-flight: walking {walked} of {total} row(s); {skipped} (Universalis data <={freshnessHours}h old)";
  }
}

/// <summary>
/// Parses the Universalis market-data payload into <see cref="ItemQuote"/>s.
///
/// GOTCHA THIS EXISTS FOR, verified live 2026-09-06: the SAME endpoint answers in two different shapes.
/// With several ids it returns <c>{"itemIDs":[..],"items":{"&lt;id&gt;":{...}},"dcName":..}</c>; with exactly ONE
/// id it returns the flat single-item object with no <c>items</c> key at all and <c>itemID</c> at the top
/// level. A retainer with one listing left would otherwise parse as "no data" and quietly get no pre-flight.
/// </summary>
public static class UniversalisQuotes
{
  public static Dictionary<uint, ItemQuote> Parse(string json, IReadOnlyCollection<ulong>? ownRetainerIds)
  {
    var result = new Dictionary<uint, ItemQuote>();
    if (string.IsNullOrWhiteSpace(json))
      return result;

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    if (root.ValueKind != JsonValueKind.Object)
      return result;

    if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
    {
      foreach (var entry in items.EnumerateObject())
      {
        var quote = ReadItem(entry.Value, ownRetainerIds, fallbackId: uint.TryParse(entry.Name, out var keyed) ? keyed : 0u);
        if (quote != null)
          result[quote.ItemId] = quote;
      }
      return result;
    }

    var single = ReadItem(root, ownRetainerIds, fallbackId: 0u);
    if (single != null)
      result[single.ItemId] = single;
    return result;
  }

  private static ItemQuote? ReadItem(JsonElement item, IReadOnlyCollection<ulong>? ownRetainerIds, uint fallbackId)
  {
    if (item.ValueKind != JsonValueKind.Object)
      return null;

    var itemId = fallbackId;
    if (item.TryGetProperty("itemID", out var idElement) && idElement.TryGetUInt32(out var parsedId) && parsedId != 0)
      itemId = parsedId;
    if (itemId == 0)
      return null;

    var hasData = !item.TryGetProperty("hasData", out var hasDataElement) || hasDataElement.ValueKind != JsonValueKind.False;

    long lastUpload = 0;
    if (item.TryGetProperty("lastUploadTime", out var uploadElement) && uploadElement.TryGetInt64(out var parsedUpload))
      lastUpload = parsedUpload;

    var listings = new List<QuoteListing>();
    if (item.TryGetProperty("listings", out var listingsElement) && listingsElement.ValueKind == JsonValueKind.Array)
    {
      foreach (var listing in listingsElement.EnumerateArray())
      {
        if (listing.ValueKind != JsonValueKind.Object)
          continue;

        long price = 0;
        if (listing.TryGetProperty("pricePerUnit", out var priceElement) && priceElement.TryGetInt64(out var parsedPrice))
          price = parsedPrice;

        var hq = listing.TryGetProperty("hq", out var hqElement) && hqElement.ValueKind == JsonValueKind.True;

        // retainerID comes back as a STRING of a 64-bit id; the same ulong.TryParse test the pricing pass
        // uses (UniversalisPriceProvider.GetNewPriceById) decides whether it is one of ours.
        var own = false;
        if (ownRetainerIds is { Count: > 0 }
            && listing.TryGetProperty("retainerID", out var retainerElement))
        {
          var retainerText = retainerElement.ValueKind == JsonValueKind.String
            ? retainerElement.GetString()
            : (retainerElement.ValueKind == JsonValueKind.Number ? retainerElement.GetRawText() : null);
          if (ulong.TryParse(retainerText, out var retainerId))
            own = ownRetainerIds.Contains(retainerId);
        }

        listings.Add(new QuoteListing(price, hq, own));
      }
    }

    return new ItemQuote(itemId, hasData, lastUpload, listings);
  }
}
