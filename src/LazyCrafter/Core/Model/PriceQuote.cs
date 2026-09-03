namespace LazyCrafter.Core.Model;

/// <summary>
/// One item's Universalis aggregate at DC scope (Plan Phase 1 IPriceSource).
/// <para>
/// Invariants the adapter (Phase 3 UniversalisClient) must honour:
/// <list type="bullet">
/// <item><see cref="VelocityNq"/> / <see cref="VelocityHq"/> are finite and &gt;= 0. Universalis <c>aggregated/</c>
/// omits <c>dailySaleVelocity</c> for items with no sales - map a missing value to <c>0</c>, never <c>NaN</c>.
/// Core sanitises anyway (<see cref="ProfitModel.SaneVelocity"/>), but a NaN here is an adapter bug.</item>
/// <item>Price columns are <c>null</c> when the quality has no listing/sale, never <c>0</c>; an item listed only
/// HQ therefore has all NQ columns <c>null</c> and <see cref="ProfitModel.UnitCost"/> falls back to the HQ columns.</item>
/// </list>
/// </para>
/// </summary>
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
