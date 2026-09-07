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
/// <param name="NqVelocityPerDay">
/// Universalis <c>nqSaleVelocity</c> - NQ sales per day over its history window. Only populated when the
/// request asked for <c>entries</c> &gt; 0 (an entries=0 query zeroes every history field, measured
/// 2026-09-06). Used by the Auto-Market listing order (0.1.11.0); 0 is a legitimate "it does not sell"
/// reading, not "unknown" - freshness is what marks a quote unusable.
/// </param>
/// <param name="HqVelocityPerDay">Same, HQ sales only (<c>hqSaleVelocity</c>).</param>
public sealed record ItemQuote(
  uint ItemId,
  bool HasData,
  long LastUploadUnixMs,
  IReadOnlyList<QuoteListing> Listings,
  double NqVelocityPerDay = 0,
  double HqVelocityPerDay = 0);

/// <summary>One row of the open sell list, as the pre-flight sees it.</summary>
/// <param name="CurrentPrice">The asking price the listing carries right now, read off the market container.</param>
/// <param name="IsPlaceholder">The listing is still at the Auto-Market placeholder price, i.e. it has never been priced.</param>
public sealed record PinchRow(int Row, int Slot, uint ItemId, bool HQ, long CurrentPrice, bool IsPlaceholder);

/// <summary>What the pre-flight decided to do with a row.</summary>
public enum PinchVerdict
{
  /// <summary>Open the row and price it: AllaganMarket's data flags it undercut or stale.</summary>
  Walk,
  /// <summary>AllaganMarket has no verdict for this row (no undercut, not stale) - the row is never touched.</summary>
  SkipNotFlagged,
}

/// <summary>A row plus the verdict, the price the pass was predicted to write, and why.</summary>
public sealed record PinchDecision(PinchRow Row, PinchVerdict Verdict, long Candidate, string Reason);

/// <summary>Everything the pre-flight needs from the configuration, so the decision logic sees no Dalamud.</summary>
/// <param name="Enabled">Master switch (<c>AutoPinchPreflightEnabled</c>). Off = every row walks, i.e. pre-0.1.16.0 behaviour.</param>
public sealed record PinchPreflightOptions(bool Enabled);

/// <summary>
/// Decides, BEFORE any context menu is opened, which sell-list rows are worth walking.
///
/// THE RULE (Joey, binding, 2026-09-07): Auto Pinch goes strictly by AllaganMarket's flags. A row is
/// walked iff AllaganMarket's data flags it stale or undercut; a row with NO AllaganMarket verdict is
/// NEVER walked - no Universalis rescue, no remembered verdict, no walking-to-check. The known and
/// wanted consequence: items AllaganMarket has never opened a market window for render unmarked and
/// stay untouched until AllaganMarket itself flags them.
///
/// The rules, in order:
///   1. the feature is off  =>  every row walks (the master switch, pre-0.1.16.0 behaviour);
///   2. a listing still at the placeholder price ALWAYS walks - a stranded new listing must never be skipped;
///   3. AllaganMarket's data flags the row undercut (red)  =>  walk;
///   4. AllaganMarket's data flags the row stale (yellow)  =>  walk;
///   5. anything else - including a row whose price the board agrees is already right, and a row with no
///      cache entry at all - is SKIPPED, never walked.
///
/// Universalis is still asked for the flagged rows so the log can show the price the pass is predicted
/// to write, but the answer NEVER changes the verdict: the flag is the instruction. The old
/// Universalis-prediction skips (already-right, under-threshold, not-undercut, board memory) are gone:
/// a flagged row walks even when the predicted candidate equals the current price, because AllaganMarket
/// is the authority and its flags are what Joey asked the plugin to follow.
/// </summary>
public static class PinchPreflight
{
  /// <param name="rows">Every row of the open sell list with the price the container says it carries.</param>
  /// <param name="quotes">Universalis quotes by item id; used only for the informational candidate.</param>
  /// <param name="nowUnixMs">Current time in unix milliseconds.</param>
  /// <param name="flags">AllaganMarket's parsed verdict set for the player's home world. Null = nothing is flagged (only placeholders walk).</param>
  /// <param name="applyItemLimit">The user's per-item min/max price limit, applied to the informational candidate only.</param>
  public static List<PinchDecision> Decide(
    IReadOnlyList<PinchRow> rows,
    IReadOnlyDictionary<uint, ItemQuote> quotes,
    PinchPreflightOptions options,
    long nowUnixMs,
    AllaganFlagSet? flags = null,
    Func<uint, int, int>? applyItemLimit = null)
  {
    var decisions = new List<PinchDecision>(rows.Count);

    foreach (var row in rows)
    {
      // Rule 1 - the feature is off. Every row walks; this is exactly what 0.1.15.0 did.
      if (!options.Enabled)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "pre-flight disabled"));
        continue;
      }

      // Rule 2 - a new listing sitting at the placeholder price is never, under any circumstances, skipped.
      // It has never been priced, so leaving it stranded at 999,999,999 gil means it silently never sells.
      if (row.IsPlaceholder)
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, 0, "new listing at the placeholder price"));
        continue;
      }

      // Rule 3 - AllaganMarket red: somebody else's listing is below this one.
      if (flags != null && flags.IsUndercut(row.ItemId, row.HQ, row.CurrentPrice))
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, Candidate(row, quotes, applyItemLimit),
          "AllaganMarket flags this listing undercut"));
        continue;
      }

      // Rule 4 - AllaganMarket yellow: its cached price data for this item is older than its own
      // staleness period. The overlay shows yellow; the pass re-prices it.
      if (flags != null && flags.IsStale(row.ItemId, row.HQ, UnixMsToDateTime(nowUnixMs)))
      {
        decisions.Add(new PinchDecision(row, PinchVerdict.Walk, Candidate(row, quotes, applyItemLimit),
          "AllaganMarket flags this listing's pricing as stale"));
        continue;
      }

      // Rule 5 - unflagged: no AllaganMarket verdict, so the row is never touched - even once, even to
      // check. This is the behaviour Joey asked for by name.
      decisions.Add(new PinchDecision(row, PinchVerdict.SkipNotFlagged, 0, "not flagged by AllaganMarket"));
    }

    return decisions;
  }

  /// <summary>
  /// The price the pass is PREDICTED to write on a walked row, best-effort from the same formula the
  /// pricing pass uses. A missing or stale quote yields 0 = unknown; the walk happens either way and
  /// the pass computes the real price itself exactly as it always has.
  /// </summary>
  private static long Candidate(PinchRow row, IReadOnlyDictionary<uint, ItemQuote> quotes, Func<uint, int, int>? applyItemLimit)
  {
    if (!quotes.TryGetValue(row.ItemId, out var quote) || quote == null || !quote.HasData)
      return 0;

    var eligible = quote.Listings
      .Where(l => l.PricePerUnit > 0 && (!row.HQ || l.Hq))
      .OrderBy(l => l.PricePerUnit)
      .ToList();
    if (eligible.Count == 0)
      return 0;

    var candidate = (long)PriceMath.Candidate(eligible[0].PricePerUnit, eligible[0].OwnRetainer, UndercutMode.FixedAmount, 0, false);
    if (applyItemLimit != null)
      candidate = applyItemLimit(row.ItemId, (int)Math.Min(candidate, int.MaxValue));
    return candidate;
  }

  private static DateTime UnixMsToDateTime(long unixMs)
    // AllaganMarket writes its cache timestamps in the CLIENT's LOCAL time and compares them against
    // DateTime.Now, so the staleness clock is local too - not the UTC instant the unix-ms carries.
    => unixMs <= 0 ? DateTime.Now : DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;

  /// <summary>
  /// The one INFO line the pass logs. Kept here (and harness-covered) because it is how this feature gets
  /// graded from Joey's client log afterwards: grep <c>pinch pre-flight:</c> and compare walked-vs-total.
  /// </summary>
  public static string Summarize(IReadOnlyList<PinchDecision> decisions)
  {
    var total = decisions.Count;
    var walked = decisions.Count(d => d.Verdict == PinchVerdict.Walk);
    var notFlagged = decisions.Count(d => d.Verdict == PinchVerdict.SkipNotFlagged);
    var placeholder = decisions.Count(d => d.Verdict == PinchVerdict.Walk && d.Reason == "new listing at the placeholder price");
    var undercut = decisions.Count(d => d.Verdict == PinchVerdict.Walk && d.Reason.Contains("undercut"));
    var stale = decisions.Count(d => d.Verdict == PinchVerdict.Walk && d.Reason.Contains("stale"));
    var disabled = decisions.Count(d => d.Reason == "pre-flight disabled");

    var reasons = new List<string>();
    if (notFlagged > 0) reasons.Add($"{notFlagged} not flagged by AllaganMarket");
    var skipped = reasons.Count == 0 ? "skipped nothing" : "skipped " + string.Join(", ", reasons);

    var breakdown = disabled == total
      ? " (pre-flight disabled)"
      : $" ({undercut} flagged undercut, {stale} flagged stale, {placeholder} placeholder)";
    return $"pinch pre-flight: walking {walked} of {total} row(s); {skipped}{breakdown}";
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

    // Per-quality sale velocity (0.1.11.0 listing order). Optional in the payload: the pre-flight
    // request asks entries=0 so these come back 0 there, which nothing reads.
    double nqVelocity = 0, hqVelocity = 0;
    if (item.TryGetProperty("nqSaleVelocity", out var nqVelocityElement) && nqVelocityElement.ValueKind == JsonValueKind.Number)
      nqVelocity = nqVelocityElement.GetDouble();
    if (item.TryGetProperty("hqSaleVelocity", out var hqVelocityElement) && hqVelocityElement.ValueKind == JsonValueKind.Number)
      hqVelocity = hqVelocityElement.GetDouble();

    return new ItemQuote(itemId, hasData, lastUpload, listings, nqVelocity, hqVelocity);
  }
}
