using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion;

/// <summary>
///     The pure half of LazyMarketCompanion's price-decision telemetry tap (v0.1.4.0):
///     the line format, the flag set and the quantity-resolution rule, with no Dalamud
///     or game types, so <c>tests/LazyMarketCompanion.TelemetryHarness</c> can assert the
///     exact shape of what ships. <see cref="MarketTelemetry"/> is the live half that
///     reads the game state and calls in here.
/// </summary>
/// <remarks>
///     Mirrors <c>GluttonyCombo.Data.ComboTelemetryFormat</c> (<c>CT|</c>) and
///     <c>AutoPotion.PotionTelemetryFormat</c> (<c>PT|</c>) on purpose: one query shape
///     reads every lalalazy decision tap.
/// </remarks>
internal static class MarketTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'MT|%'</c> in ffxivdb. </summary>
    public const string Prefix = "MT|";

    /// <summary> Hard budget for one emitted line. </summary>
    public const int MaxLineLength = 200;

    // ---- price source (field 9) -------------------------------------------------
    // SHORT and STABLE. ffxivdb queries key on these; never rename or repurpose one,
    // only add. Exactly three are reachable from SetNewPrice - see MarketTelemetry.

    /// <summary> Universalis data-centre lookup supplied the candidate price. </summary>
    public const char SrcUniversalis = 'u';

    /// <summary> The in-game market board ("Compare Prices") supplied the candidate price. </summary>
    public const char SrcBoard = 'b';

    /// <summary> Neither did: the configured <c>DefaultAmount</c> fallback was used. </summary>
    public const char SrcDefault = 'd';

    // ---- flags (field 10) ------------------------------------------------------
    // Emitted in this fixed order so a line is byte-stable for a given decision.

    /// <summary> The old price was the Auto-Market placeholder, i.e. this is a brand-new listing being priced for the first time. </summary>
    public const char FlagPlaceholder = 'p';

    /// <summary> The candidate price came from the per-run price cache rather than a fresh lookup. </summary>
    public const char FlagCache = 'c';

    /// <summary> A per-item min/max price limit clamped the candidate price. </summary>
    public const char FlagLimited = 'l';

    /// <summary> The write was ABORTED by the MaxUndercutPercentage guard - the listing kept its old price. </summary>
    public const char FlagAborted = 'a';

    /// <summary> Rendered when no flag applies, so the field is never empty. </summary>
    public const string FlagsNone = "-";

    /// <summary>
    ///     Rendered for <c>cutPct</c> when it cannot be computed because the old price
    ///     was zero. Both prices are already on the line, so a consumer loses nothing;
    ///     what it avoids is a NaN/Infinity token in a numeric column.
    /// </summary>
    public const string CutNotComputable = "-";

    /// <summary>
    ///     Marker the game appends to an item name to mean high quality. Stripped from
    ///     the emitted name because HQ is already its own field.
    /// </summary>
    public const char HqGlyph = '\uE03C';

    /// <summary>
    ///     Builds the flag field in a fixed order (<c>p</c>, <c>c</c>, <c>l</c>, <c>a</c>),
    ///     or <see cref="FlagsNone"/> when none apply.
    /// </summary>
    internal static string BuildFlags(bool placeholder, bool cache, bool limited, bool aborted)
    {
        if (!placeholder && !cache && !limited && !aborted)
            return FlagsNone;

        var sb = new StringBuilder(4);
        if (placeholder) sb.Append(FlagPlaceholder);
        if (cache) sb.Append(FlagCache);
        if (limited) sb.Append(FlagLimited);
        if (aborted) sb.Append(FlagAborted);
        return sb.ToString();
    }

    /// <summary>
    ///     Quantity of the listing being priced, taken from the retainer's market container.
    /// </summary>
    /// <remarks>
    ///     The price dialog (<c>AddonRetainerSell</c>) exposes only the item name and the
    ///     asking price - it carries no quantity - so the number has to come from the market
    ///     container instead. That container is addressed by SLOT, and mapping the sell-list
    ///     ROW we clicked onto a slot is exactly the assumption 0.1.3.0 proved the game does
    ///     not guarantee. So this deliberately does NOT infer a slot: it returns a quantity
    ///     only when (itemId, hq) identifies exactly ONE occupied slot, and 0 - meaning
    ///     "unknown" - whenever the retainer holds two listings of the same item and quality,
    ///     which is the only case where a guess could be wrong. An honest 0 beats a plausible
    ///     wrong number in a table denominated in gil.
    /// </remarks>
    internal static int ResolveQuantity(IReadOnlyList<MarketSlot>? market, uint itemId, bool hq)
    {
        if (market == null || itemId == 0)
            return 0;

        var found = 0;
        var quantity = 0;
        for (var i = 0; i < market.Count; i++)
        {
            var slot = market[i];
            if (slot.ItemId != itemId || slot.HQ != hq)
                continue;

            if (++found > 1)
                return 0; // ambiguous: two listings of the same item+quality, no way to tell which row is open

            quantity = slot.Quantity;
        }

        return found == 1 && quantity > 0 ? quantity : 0;
    }

    /// <summary>
    ///     Builds one telemetry line:
    ///     <c>MT|unixms|itemId|hq|qty|oldPrice|newPriceRaw|newPriceFinal|src|flags|cutPct|item</c>
    ///     (12 pipe-separated fields).
    /// </summary>
    /// <param name="itemId">Resolved item id, or 0 when the open listing could not be identified.</param>
    /// <param name="qty">Units in the listing, or 0 when unknown - see <see cref="ResolveQuantity"/>.</param>
    /// <param name="oldPrice">Unit price the listing carried before this decision (the placeholder, for a new listing).</param>
    /// <param name="newPriceRaw">Candidate unit price as it arrived, BEFORE any per-item min/max clamp.</param>
    /// <param name="newPriceFinal">Unit price after the clamp. On an abort this is the price that WOULD have been written.</param>
    /// <param name="src">One of <see cref="SrcUniversalis"/> / <see cref="SrcBoard"/> / <see cref="SrcDefault"/>.</param>
    /// <param name="flags">From <see cref="BuildFlags"/>.</param>
    /// <param name="cutPct">Change from oldPrice to newPriceFinal, in percent; negative means undercut. Non-finite renders as <see cref="CutNotComputable"/>.</param>
    /// <param name="item">Item name; the only variable-length field, and the only one truncation may cut.</param>
    internal static string BuildLine(
        long unixMs, uint itemId, bool hq, int qty,
        int oldPrice, int newPriceRaw, int newPriceFinal,
        char src, string flags, float cutPct, string? item)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 64);

        sb.Append(Prefix)
          .Append(unixMs.ToString(inv)).Append('|')
          .Append(itemId.ToString(inv)).Append('|')
          .Append(hq ? '1' : '0').Append('|')
          .Append(qty.ToString(inv)).Append('|')
          .Append(oldPrice.ToString(inv)).Append('|')
          .Append(newPriceRaw.ToString(inv)).Append('|')
          .Append(newPriceFinal.ToString(inv)).Append('|')
          .Append(src).Append('|')
          .Append(string.IsNullOrEmpty(flags) ? FlagsNone : flags).Append('|')
          .Append(float.IsFinite(cutPct) ? cutPct.ToString("F1", inv) : CutNotComputable).Append('|');

        // Everything above is what the ffxivdb join needs and is length-bounded by
        // construction; the item name is the only part allowed to be cut short.
        if (sb.Length > MaxLineLength - 1)
        {
            // Defensive: a pathological flag string can never be allowed to blow the
            // budget either. Cut the whole line rather than emit something oversized.
            sb.Length = MaxLineLength - 1;
            return sb.Append('~').ToString();
        }

        if (!string.IsNullOrEmpty(item))
        {
            // The item name is a game string: strip the separator so a translated name
            // containing '|' can never fabricate an extra field, and drop the HQ glyph
            // (a private-use codepoint that renders as junk in a log) since hq is a field.
            foreach (var ch in item)
            {
                if (ch == HqGlyph)
                    continue;
                sb.Append(ch == '|' ? '/' : ch);
            }

            if (sb.Length > MaxLineLength - 1)
            {
                sb.Length = MaxLineLength - 1;
                return sb.Append('~').ToString();
            }
        }

        return sb.ToString();
    }
}
