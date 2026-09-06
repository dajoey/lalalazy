using System;
using System.Collections.Generic;

namespace LazyMarketCompanion;

/// <summary>
///     One recent sale as Universalis reports it in <c>recentHistory</c>.
/// </summary>
/// <param name="PricePerUnit">Unit price the sale went through at.</param>
/// <param name="UnixSeconds">Sale timestamp, seconds since the epoch.</param>
/// <param name="Hq">Whether the sold item was high quality.</param>
internal readonly record struct SaleHistoryEntry(long PricePerUnit, long UnixSeconds, bool Hq);

/// <summary>Why <see cref="SaleHistoryPricing.Evaluate"/> did or did not produce a price.</summary>
internal enum SaleHistoryOutcome
{
    /// <summary>A price was produced from sales inside the freshness window.</summary>
    Priced,

    /// <summary>Universalis reported no usable sale at all for this item and quality.</summary>
    NoHistory,

    /// <summary>Sales exist, but the newest one is older than the configured window.</summary>
    Stale,
}

/// <param name="Outcome">See <see cref="SaleHistoryOutcome"/>.</param>
/// <param name="UnitPrice">The price to list at; 0 unless <see cref="Outcome"/> is <see cref="SaleHistoryOutcome.Priced"/>.</param>
/// <param name="SampleCount">How many sales the median was taken over (0 when not priced).</param>
/// <param name="NewestUnixSeconds">Timestamp of the newest usable sale, or 0 when there was none.</param>
internal readonly record struct SaleHistoryResult(
    SaleHistoryOutcome Outcome, long UnitPrice, int SampleCount, long NewestUnixSeconds);

/// <summary>
///     The pure half of the "nothing else is on the board" price fallback (v0.1.8.0, asked for by
///     Joey 2026-09-06, option A of the Helm decision card): given Universalis' recent SALES for an
///     item, decide what to list at, or refuse.
/// </summary>
/// <remarks>
///     <para>
///     Why a MEDIAN and not the API's own average: <c>averagePriceHQ</c> is outlier-poisoned. Measured
///     live on 2026-09-06 for item 16644 (empty Aether board): the last 10 data-centre sales ran
///     40,000-200,000 gil, Universalis reported <c>averagePriceHQ</c> 1,824,207, and the median of the
///     same 10 sales was 53,550. The mean is not usable as a listing price.
///     </para>
///     <para>
///     Why a staleness guard: item 30037's newest sale anywhere on the data centre is from JUNE 2022.
///     Pricing off a four-year-old data point is inventing a number, so this refuses instead and the
///     caller leaves the listing at the placeholder with the existing "no board price found" message.
///     </para>
///     <para>
///     Deliberately has no Dalamud, game or HTTP types, so <c>tests/LazyMarketCompanion.Harness</c>
///     asserts the exact arithmetic that ships.
///     </para>
/// </remarks>
internal static class SaleHistoryPricing
{
    /// <summary>Default freshness window in days. Anything whose newest sale is older is refused.</summary>
    public const int DefaultMaxAgeDays = 30;

    /// <summary>How many recent sales to ask Universalis for. Enough to make a median mean something.</summary>
    public const int DefaultEntryCount = 20;

    /// <summary>Bounds for the configurable window, so a stray config value cannot disable the guard.</summary>
    public const int MinMaxAgeDays = 1;

    /// <inheritdoc cref="MinMaxAgeDays"/>
    public const int MaxMaxAgeDays = 365;

    private const long SecondsPerDay = 86400L;

    /// <summary>
    ///     Median unit price of the sales inside the freshness window, or a refusal.
    /// </summary>
    /// <param name="entries">Recent sales as reported by Universalis, in any order.</param>
    /// <param name="nowUnixSeconds">Current time; passed in so the harness can pin it.</param>
    /// <param name="maxAgeDays">Freshness window; clamped to <see cref="MinMaxAgeDays"/>..<see cref="MaxMaxAgeDays"/>.</param>
    /// <param name="hqOnly">
    ///     When true, only HQ sales count. Universalis already filters when asked with <c>hq=true</c>;
    ///     this filters again so the rule is enforced here rather than trusted from the wire.
    /// </param>
    public static SaleHistoryResult Evaluate(
        IReadOnlyList<SaleHistoryEntry>? entries, long nowUnixSeconds, int maxAgeDays, bool hqOnly)
    {
        if (entries == null || entries.Count == 0)
            return new SaleHistoryResult(SaleHistoryOutcome.NoHistory, 0, 0, 0);

        var window = Math.Clamp(maxAgeDays, MinMaxAgeDays, MaxMaxAgeDays);
        var cutoff = nowUnixSeconds - (window * SecondsPerDay);

        var newest = 0L;
        var fresh = new List<long>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];

            // A zero/negative price or a timestamp in the future is corrupt input, not a data point.
            if (e.PricePerUnit <= 0 || e.UnixSeconds <= 0 || e.UnixSeconds > nowUnixSeconds)
                continue;
            if (hqOnly && !e.Hq)
                continue;

            if (e.UnixSeconds > newest)
                newest = e.UnixSeconds;
            if (e.UnixSeconds >= cutoff)
                fresh.Add(e.PricePerUnit);
        }

        if (newest == 0)
            return new SaleHistoryResult(SaleHistoryOutcome.NoHistory, 0, 0, 0);

        // Equivalent to "the newest sale is older than the window", which is how the guard was
        // described on the decision card; expressed on the window contents so the two can never drift.
        if (fresh.Count == 0)
            return new SaleHistoryResult(SaleHistoryOutcome.Stale, 0, 0, newest);

        return new SaleHistoryResult(SaleHistoryOutcome.Priced, Median(fresh), fresh.Count, newest);
    }

    /// <summary>
    ///     Median of the sample. An even count takes the floor of the mean of the two middle values,
    ///     because a listing price is whole gil.
    /// </summary>
    internal static long Median(List<long> prices)
    {
        prices.Sort();
        var n = prices.Count;
        var mid = n / 2;
        var median = (n % 2) == 1 ? prices[mid] : (prices[mid - 1] + prices[mid]) / 2;
        return Math.Max(median, 1);
    }
}
