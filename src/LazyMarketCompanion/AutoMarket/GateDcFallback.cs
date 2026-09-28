using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Data-center-scope fallback for the Auto-Market gate's price lookup (0.2.5.0).
///
/// THE MECHANISM THIS EXISTS FOR (diagnosed 2026-09-28 from a live 0.2.4.0 session plus an
/// offline reproduction of the exact gate request): Universalis' <c>lastUploadTime</c> at
/// WORLD scope is the recency of uploads for THAT WORLD only. On a home world where nobody
/// re-uploads an item inside the gate's freshness window, the quote is unusable - hasData or
/// listings may exist but the upload is stale - so the gate correctly HOLDS (unconfirmed
/// means hold) and sellable stock sits in inventory while market slots stay empty. Measured
/// on the live install: 342 of 597 configured ids were stale at world scope (95 of them
/// 6-12 h old, 156 at 12-24 h) while the data-center aggregate carried fresh data - because
/// OTHER worlds of the data center do upload those items. The same measurement showed the
/// data-center aggregate is EXPENSIVE to compute server-side: 50-id DC chunks 504 exactly
/// like the world-scope giant requests of 0.1.18.0, but 8-10-id DC chunks answer in
/// ~0.2-0.3 s when <c>entries=0</c> (the recentHistory aggregation is the cost). The fallback
/// therefore runs small chunks with no history, and its quotes carry zero sale velocity -
/// fallback-priced items rank at the end of the fastest-selling-first order but still take
/// free slots, which is the starvation this fixes.
///
/// THE INVARIANT IS UNCHANGED: a fallback quote is real Universalis data (hasData, a fresh
/// upload time, a positive listing) or it is not used at all. No heuristic price is ever
/// synthesized; a stale data-center quote overwrites nothing and the item stays held. The
/// per-rule quality and value checks in <see cref="MarketGate"/> run on the merged quotes
/// exactly as before.
/// </summary>
public static class GateDcFallback
{
  /// <summary>
  /// Source-level usability: could this quote price SOME rule this pass? hasData, a fresh
  /// upload time, and at least one positive listing of any quality. The rule-level checks
  /// (wanted quality, net threshold) stay in the gate - an NQ-only fallback quote for an
  /// HQ-only rule still holds, correctly.
  /// </summary>
  public static bool SourceUsable(ItemQuote? quote, long nowUnixMs, long freshnessMs)
    => quote != null
       && quote.HasData
       && quote.LastUploadUnixMs > 0
       && nowUnixMs - quote.LastUploadUnixMs <= freshnessMs
       && quote.Listings.Any(l => l.PricePerUnit > 0);

  /// <summary>
  /// The requested ids the primary (world-scope) pass could not produce a source-usable
  /// quote for: missing from the response, hasData=false, a stale upload, or no positive
  /// listing at all. A null quote map (every chunk failed) selects every requested id.
  /// </summary>
  public static List<uint> Select(IReadOnlyList<uint> requestedIds, IReadOnlyDictionary<uint, ItemQuote>? quotes, long nowUnixMs, long freshnessMs)
  {
    var needing = new List<uint>();
    foreach (var id in requestedIds)
    {
      ItemQuote? quote = null;
      quotes?.TryGetValue(id, out quote);
      if (!SourceUsable(quote, nowUnixMs, freshnessMs))
        needing.Add(id);
    }
    return needing;
  }

  /// <summary>
  /// Overwrites entries of <paramref name="destination"/> with data-center quotes ONLY where
  /// the destination entry is missing or source-unusable AND the data-center quote is itself
  /// source-usable. A usable destination entry is never replaced (there is nothing to fix
  /// there); a stale or empty data-center answer changes nothing. Returns how many ids were
  /// recovered.
  /// </summary>
  public static int MergeUsable(Dictionary<uint, ItemQuote> destination, IReadOnlyDictionary<uint, ItemQuote>? dcQuotes, long nowUnixMs, long freshnessMs)
  {
    if (dcQuotes == null || dcQuotes.Count == 0)
      return 0;

    var merged = 0;
    foreach (var kv in dcQuotes)
    {
      destination.TryGetValue(kv.Key, out var current);
      if (SourceUsable(current, nowUnixMs, freshnessMs))
        continue;
      if (!SourceUsable(kv.Value, nowUnixMs, freshnessMs))
        continue;
      destination[kv.Key] = kv.Value;
      merged++;
    }
    return merged;
  }

  /// <summary>The one Information line the fallback emits, naming both counts (0.2.5.0).</summary>
  public static string Summarize(int neededCount, int mergedCount)
    => $"gate: {neededCount} item(s) had no fresh world-scope price data; using data-center-scope quotes for {mergedCount} of them";
}
