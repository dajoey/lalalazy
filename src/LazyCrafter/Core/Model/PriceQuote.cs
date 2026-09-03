namespace LazyCrafter.Core.Model;

/// <summary>One item's Universalis aggregate at DC scope (Plan §Phase 1 IPriceSource).</summary>
public sealed record PriceQuote(
    uint ItemId,
    long? MinListingNq,
    long? MinListingHq,
    long? MedianNq,
    long? MedianHq,
    long? AvgSaleNq,
    long? AvgSaleHq,
    double VelocityNq,
    double VelocityHq,
    int ListingsCount,
    DateTimeOffset? LastUpload);
