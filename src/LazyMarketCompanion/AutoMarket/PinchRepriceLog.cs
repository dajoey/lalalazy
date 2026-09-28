using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// The Information-level line emitted for every price the pinch walk actually writes
/// (0.2.5.0). Until now the only per-decision record was the optional MT| decision tap
/// (gated on the DecisionTelemetry flag) - with the flag off, a placeholder listing that
/// failed to reprice was almost traceless, and "did the placeholder->market reprice land?"
/// could not be answered from the plugin log. This line is unconditional: item, the price
/// it replaced, the price it set, and the source the price came from.
/// </summary>
public static class PinchRepriceLog
{
  public const string SourceUniversalis = "universalis";
  public const string SourceComparePrices = "compare-prices";
  public const string SourceCache = "cache";
  public const string SourceDefault = "default";

  /// <param name="itemId">Best-effort resolved id; 0 is printed as-is when unresolved.</param>
  /// <param name="oldPrice">The asking price the listing carried before this write.</param>
  /// <param name="newPrice">The price being written now.</param>
  /// <param name="source">One of the <c>Source*</c> labels.</param>
  /// <param name="wasPlaceholder">True when the old price was the Auto-Market placeholder (a new listing getting its first real price).</param>
  public static string Format(uint itemId, string itemName, long oldPrice, long newPrice, string source, bool wasPlaceholder)
    => $"pinch reprice: item {itemId} '{itemName}' {oldPrice} -> {newPrice} gil (source: {source}; {(wasPlaceholder ? "new listing priced, was at the placeholder price" : "reprice")})";
}
