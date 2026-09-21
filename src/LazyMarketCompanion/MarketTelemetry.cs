using System;
using ECommons.DalamudServices;
using Lalalazy.Telemetry;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion;

/// <summary>
///     Optional, off-by-default price-decision tap (v0.1.4.0).<br />
///     When <see cref="Configuration.DecisionTelemetry"/> is on, one structured line is
///     written at Information level through the normal plugin logger for every price
///     decision <c>MarketAutomation.SetNewPrice</c> makes - the writes AND the writes it
///     refuses:
///     <c>MT|unixms|itemId|hq|qty|oldPrice|newPriceRaw|newPriceFinal|src|flags|cutPct|item</c>.<br />
///     It rides the existing dalamud.log -&gt; ffxivdb <c>plugin_log_lines</c> harvest (no
///     transport of its own) and is joined to the retainer sale lines already landing in
///     <c>chat_lines</c> (channel <c>retainer</c>, code <c>0047</c>) by item and timestamp,
///     which is what turns "did matching the board actually earn more than the fallback
///     price?" from a guess into a query.
/// </summary>
/// <remarks>
///     <b>Cost when off:</b> no log line, and in particular no
///     <see cref="ItemNameResolver.TryGetItemId"/> (a linear scan of the Item sheet) and no
///     market-container read. Since 2026-09-21 a light copy of the line (itemId 0, qty 0) is
///     still formatted once per price decision - never per frame - and kept only in the
///     in-memory ring that a <c>/lmc report</c> dumps (src/Shared/LalaTelemetry).<br />
///     <b>Volume:</b> one line per price decision, ungated. A retainer run prices tens of
///     listings, so unlike GluttonyCombo (one decision per frame) there is nothing here to
///     rate-limit, and a dropped abort would be the single most interesting line lost.<br />
///     The format, the flag set and the quantity rule live in
///     <see cref="MarketTelemetryFormat"/> so they can be asserted offline by
///     <c>tests/LazyMarketCompanion.TelemetryHarness</c>.
/// </remarks>
internal static class MarketTelemetry
{
    /// <inheritdoc cref="MarketTelemetryFormat.Prefix"/>
    public const string Prefix = MarketTelemetryFormat.Prefix;

    /// <summary>
    ///     Records one price-decision line. <paramref name="log"/> = <c>Plugin.Configuration.DecisionTelemetry</c>:
    ///     when true the line is logged with the resolved item id and quantity; when false only a light
    ///     copy (itemId 0, qty 0 - no Item-sheet scan) goes to the in-memory telemetry ring.
    /// </summary>
    /// <param name="itemName">Display name from the open price dialog.</param>
    /// <param name="rawItemName">Raw (SeString) name from the same dialog; both are needed to resolve the id.</param>
    /// <param name="oldPrice">Asking price the dialog opened with.</param>
    /// <param name="newPriceRaw">Candidate price before the per-item min/max clamp.</param>
    /// <param name="newPriceFinal">Candidate price after the clamp; on an abort, the price that would have been written.</param>
    /// <param name="fromUniversalis">The candidate came from the Universalis data-centre lookup.</param>
    /// <param name="fromCache">The candidate came from the per-run price cache.</param>
    /// <param name="usedDefaultAmount">Neither lookup produced a price and the configured DefaultAmount was substituted.</param>
    /// <param name="limited">A per-item price limit changed the candidate.</param>
    /// <param name="isPlaceholder">The old price was the Auto-Market placeholder (a brand-new listing).</param>
    /// <param name="aborted">The MaxUndercutPercentage guard refused the write.</param>
    /// <param name="cutPercentage">Change from oldPrice to newPriceFinal in percent, as the guard computed it.</param>
    /// <param name="log">Write the full line to the plugin log (the telemetry toggle); false = ring only.</param>
    public static void RecordDecision(
        string itemName, string rawItemName,
        int oldPrice, int newPriceRaw, int newPriceFinal,
        bool fromUniversalis, bool fromCache, bool usedDefaultAmount,
        bool limited, bool isPlaceholder, bool aborted, float cutPercentage,
        bool log = true)
    {
        try
        {
            // The dialog carries no item id and no quantity, so both are recovered here.
            // A failed resolution emits itemId 0 rather than dropping the line: the prices
            // and the item name still carry the decision, and a silent gap in a money table
            // is worse than an unjoinable row that says so.
            var hq = itemName.Contains(MarketTelemetryFormat.HqGlyph, StringComparison.Ordinal)
                  || rawItemName.Contains(MarketTelemetryFormat.HqGlyph, StringComparison.Ordinal);

            uint itemId = 0;
            var qty = 0;
            if (log)
            {
                if (!ItemNameResolver.TryGetItemId(itemName, rawItemName, out itemId))
                    itemId = 0;
                qty = MarketTelemetryFormat.ResolveQuantity(AutoMarketService.SnapshotMarket(), itemId, hq);
            }

            var src = usedDefaultAmount ? MarketTelemetryFormat.SrcDefault
                    : fromUniversalis ? MarketTelemetryFormat.SrcUniversalis
                    : MarketTelemetryFormat.SrcBoard;

            var line = MarketTelemetryFormat.BuildLine(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                itemId, hq, qty,
                oldPrice, newPriceRaw, newPriceFinal,
                src,
                MarketTelemetryFormat.BuildFlags(isPlaceholder, fromCache, limited, aborted),
                cutPercentage,
                itemName);
            if (log)
                Svc.Log.Information(line);
            LalaTelemetry.Record(line);
        }
        catch (Exception ex)
        {
            // A telemetry tap must never be able to break the plugin - but a tap that silently stops
            // is how a report ends up with "zero telemetry". Rate-limited WRN, not Debug.
            LalaTelemetry.Swallowed("telemetry.mt", ex);
        }
    }
}
