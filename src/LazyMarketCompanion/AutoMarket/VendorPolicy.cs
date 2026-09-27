namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercised by tests/LazyMarketCompanion.Harness (cases 130, 133, 136).
//
// 0.2.0.0 (docs/AutoMarket-Design.md §4): the ONLY automated vendoring path in the plugin is
// the BOUNDED JUNK PATH, decided here. An item reaches the vendor leg only when:
//
//   1. its market side is CONFIRMED (fresh quote, wanted quality, positive price) AND its
//      total market net is at or under the threshold (default 100 gil) - stock the market
//      has priced as not worth a slot;
//   2. it is NQ - HQ stock is NEVER auto-vendored (every large loss on 2026-09-27 was HQ);
//   3. it is not equippable - gear is NEVER auto-vendored, whatever any price source claims;
//   4. the Item sheet gives it a nonzero PriceLow - an ENABLEMENT check only.
//
// The Item sheet price is NEVER compared against the market value. On 2026-09-27 that
// comparison (0.1.70.0) treated sentinel sheet prices (99,999 gil/unit on items no vendor
// buys at that rate) as a guaranteed payout and vendored market-valuable stock en masse:
// "market net 15,086 gil is under vendor value 399,996 gil" was a real logged decision.
// A vendor payout the game has not confirmed is not a number this plugin may act on; the
// unbounded "vendor pays more than market" comparison therefore does not exist (§1).
public static class VendorPolicy
{
  /// <summary>
  /// Whether one item's stock may take the automated vendor leg this pass. Every parameter is
  /// already resolved by the caller (market side judged by MarketGate, sheet by VendorPrices).
  /// </summary>
  /// <param name="marketConfirmed">The gate holds a fresh, positive, wanted-quality quote.</param>
  /// <param name="marketNet">Net market value (5% fee) of the item's total sellable stock.</param>
  /// <param name="thresholdGil">The value-gate threshold in gil; &lt;= 0 disables the junk path.</param>
  /// <param name="isHq">The rule (and its stock) is high quality.</param>
  /// <param name="isProtectedFromVendor">Caller's protected-class predicate (equippable gear).</param>
  /// <param name="sheetPriceLow">Item sheet PriceLow; enablement only, never a comparison input.</param>
  public static bool JunkPathEligible(
    bool marketConfirmed,
    long marketNet,
    long thresholdGil,
    bool isHq,
    bool isProtectedFromVendor,
    uint sheetPriceLow)
    => marketConfirmed
       && thresholdGil > 0
       && marketNet <= thresholdGil
       && !isHq
       && !isProtectedFromVendor
       && ItemVendorPrice.Vendorable(sheetPriceLow);
}
