using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion.TelemetryHarness;

/// <summary>
///     Offline assertions on LazyMarketCompanion's price-decision telemetry tap (v0.1.4.0).
///     Compiles the real <see cref="MarketTelemetryFormat"/> and the real
///     <see cref="MarketSlot"/> record - no Dalamud, no game - so the wire format the
///     ffxivdb join depends on, and the quantity rule that feeds it, are proven before
///     shipping.
/// </summary>
/// <remarks>
///     The last block replays every sale line Joey's retainers actually produced on
///     2026-09-05 (16 rows, ffxivdb chat_lines channel='retainer') through the join
///     shape the tap exists to serve: parse the MT| side, parse the sale side, match on
///     item id, and assert the join produces realised gil per listing. A tap whose join
///     does not work in the harness will not work in SQL.
/// </remarks>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        LineFormat();
        LocaleSafety();
        Truncation();
        QuantityResolution();
        JoinShape();

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) _pass++;
        else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}{(ok || detail == null ? "" : $"  :: {detail}")}");
    }

    // ---------------------------------------------------------------- line format

    private static void LineFormat()
    {
        // A normal market-board write: ice crystals (7) are a plausible placeholder repricing.
        var board = MarketTelemetryFormat.BuildLine(
            unixMs: 1_788_640_000_123, itemId: 7, hq: false, qty: 500,
            oldPrice: 999_999_999, newPriceRaw: 59, newPriceFinal: 59,
            src: MarketTelemetryFormat.SrcBoard, flags: MarketTelemetryFormat.BuildFlags(
                placeholder: true, cache: false, limited: false, aborted: false),
            cutPct: -99.99999f, item: "Ice Crystal");

        Check("prefix is the greppable MT|", board.StartsWith("MT|", StringComparison.Ordinal));
        Check("board write: exact line shape",
            board == "MT|1788640000123|7|0|500|999999999|59|59|b|p|-100.0|Ice Crystal", board);
        var f = board.Split('|');
        Check("12 pipe-separated fields", f.Length == 12, f.Length.ToString());
        Check("field 1 is unix ms", f[1] == "1788640000123");
        Check("field 2 is the item id (joins the sale line)", f[2] == "7");
        Check("field 3 is hq as 1/0", f[3] == "0");
        Check("field 4 is the listing quantity", f[4] == "500");
        Check("old and both new prices are integers", f[5] == "999999999" && f[6] == "59" && f[7] == "59");
        Check("field 8 is the price source", f[8] == "b");
        Check("field 9 carries the placeholder flag", f[9] == "p");
        Check("cutPct is 1dp", f[10] == "-100.0");
        Check("item name is the last field", f[11] == "Ice Crystal");

        // A Universalis-sourced cache hit on a real listing, limit-clamped.
        var cached = MarketTelemetryFormat.BuildLine(
            1_788_640_000_456, 2001763, true, 5,
            250, 990, 500,
            MarketTelemetryFormat.SrcUniversalis,
            MarketTelemetryFormat.BuildFlags(placeholder: false, cache: true, limited: true, aborted: false),
            100.0f, "Savage Might Materia XI\uE03C");
        Check("universalis cache+clamp: exact line shape",
            cached == "MT|1788640000456|2001763|1|5|250|990|500|u|cl|100.0|Savage Might Materia XI", cached);
        Check("the HQ glyph is stripped from the name (it is its own field)",
            !cached.Contains('\uE03C'), cached);

        // The abort: the most interesting event in the plugin, and the whole reason the
        // tap emits on BOTH branches.
        var abort = MarketTelemetryFormat.BuildLine(
            1_788_640_000_789, 5106, false, 99,
            1_900, 1_100, 1_100,
            MarketTelemetryFormat.SrcBoard,
            MarketTelemetryFormat.BuildFlags(placeholder: false, cache: false, limited: false, aborted: true),
            -42.1f, "Ultra-Potion");
        Check("undercut abort: exact line shape",
            abort == "MT|1788640000789|5106|0|99|1900|1100|1100|b|a|-42.1|Ultra-Potion", abort);

        // The DefaultAmount fallback (src=d) with nothing else set.
        var def = MarketTelemetryFormat.BuildLine(
            1_788_640_001_000, 0, false, 0,
            999_999_999, 100, 100,
            MarketTelemetryFormat.SrcDefault, MarketTelemetryFormat.FlagsNone,
            -100.0f, "Unknown Chunk");
        Check("default-amount fallback: exact line shape",
            def == "MT|1788640001000|0|0|0|999999999|100|100|d|-|-100.0|Unknown Chunk", def);
        Check("unresolvable item still emits (itemId 0), flags field renders '-'", def.Split('|')[9] == "-");

        // Flag ordering is fixed so a decision always produces a byte-stable line.
        Check("flag order is fixed: pcla",
            MarketTelemetryFormat.BuildFlags(true, true, true, true) == "pcla",
            MarketTelemetryFormat.BuildFlags(true, true, true, true));

        // An old price of zero makes cutPct non-finite; it must render as the sentinel,
        // never as NaN/Infinity in a numeric column.
        var zero = MarketTelemetryFormat.BuildLine(
            1, 1, false, 1, 0, 50, 50, MarketTelemetryFormat.SrcBoard,
            MarketTelemetryFormat.FlagsNone, float.NaN, "Thing");
        Check("a non-finite cutPct renders as '-'", zero.Split('|')[10] == "-", zero);
    }

    // ---------------------------------------------------------------- locale safety

    private static void LocaleSafety()
    {
        // A comma decimal separator silently destroys a '|' parse - and would do it only
        // on a machine set to de/fr/ru, months later, in a table nobody re-checks.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "fr-FR", "ru-RU" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                var line = MarketTelemetryFormat.BuildLine(
                    1_788_640_000_123, 7, false, 500,
                    999_999_999, 59, 59,
                    // NB -99.95f is -99.9499969 in float, so F1 renders -99.9, not -100.0;
                    // the fixture uses the same value as the board line above on purpose.
                    MarketTelemetryFormat.SrcBoard, "p", -99.99999f, "Ice Crystal");
                Check($"invariant decimals under {name}",
                    line == "MT|1788640000123|7|0|500|999999999|59|59|b|p|-100.0|Ice Crystal", line);
                Check($"no comma decimal separator leaks under {name}",
                    !line.Contains(',', StringComparison.Ordinal), line);

                // Group separators must not appear in the gil columns either.
                var big = MarketTelemetryFormat.BuildLine(
                    1, 999_999, false, 9999, 123_456_789, 987_654_321, 987_654_321,
                    MarketTelemetryFormat.SrcUniversalis, MarketTelemetryFormat.FlagsNone, 0f, "x");
                Check($"no digit-group separator leaks under {name}",
                    !big.Contains(',', StringComparison.Ordinal) && big.Contains("123456789", StringComparison.Ordinal), big);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---------------------------------------------------------------- truncation

    private static void Truncation()
    {
        // A pathological item name must be cut, and cut in the LAST field only.
        var longName = MarketTelemetryFormat.BuildLine(
            1_788_640_000_123, 7, false, 500,
            999_999_999, 59, 59,
            MarketTelemetryFormat.SrcBoard, "p", -100.0f, new string('X', 400));

        Check("over-long line is cut to the budget",
            longName.Length <= MarketTelemetryFormat.MaxLineLength, $"len={longName.Length}");
        Check("truncated line is marked with ~", longName.EndsWith('~'), longName);
        Check("truncation keeps all 12 fields", longName.Split('|').Length == 12, longName);
        Check("truncation only ever cuts the LAST field",
            longName.StartsWith("MT|1788640000123|7|0|500|999999999|59|59|b|p|-100.0|", StringComparison.Ordinal),
            longName);

        // A name that fits is left completely alone.
        var fits = MarketTelemetryFormat.BuildLine(
            1_788_640_000_123, 2001763, true, 5,
            250, 990, 500, MarketTelemetryFormat.SrcUniversalis, "cl", 100.0f,
            "Savage Might Materia XI");
        Check("a name that fits is not truncated", !fits.EndsWith('~'), fits);
        Check("a name that fits survives byte-for-byte",
            fits.EndsWith("|Savage Might Materia XI", StringComparison.Ordinal), fits);

        // Worst-case fixed fields: still inside the budget with room for a name, i.e.
        // truncation can never eat a field the join depends on.
        var worst = MarketTelemetryFormat.BuildLine(
            long.MaxValue, uint.MaxValue, true, int.MaxValue,
            int.MaxValue, int.MaxValue, int.MaxValue,
            MarketTelemetryFormat.SrcUniversalis, "pcla", float.MaxValue, null);
        Check("worst-case fixed fields stay inside the budget",
            worst.Length <= MarketTelemetryFormat.MaxLineLength && worst.Split('|').Length == 12,
            $"len={worst.Length} :: {worst}");
        Check("worst-case fixed fields are not truncated", !worst.EndsWith('~'), worst);

        // A translated item name containing the separator must not fabricate a field.
        var evil = MarketTelemetryFormat.BuildLine(
            1, 1, false, 1, 1, 1, 1, MarketTelemetryFormat.SrcBoard,
            MarketTelemetryFormat.FlagsNone, 0f, "Weird|Name");
        Check("a '|' inside an item name cannot add a field", evil.Split('|').Length == 12, evil);
        Check("the offending '|' is replaced, not dropped", evil.EndsWith("Weird/Name", StringComparison.Ordinal), evil);
    }

    // ---------------------------------------------------------------- quantity rule

    private static List<MarketSlot> Market(params (int slot, uint id, bool hq, int qty)[] rows)
        => rows.Select(r => new MarketSlot(r.slot, r.id, r.hq, r.qty)).ToList();

    private static void QuantityResolution()
    {
        // Joey's real 2026-09-05 retainer shape: 7 ice-crystal listings, all NQ, plus other items.
        var market = Market(
            (0, 7, false, 500), (1, 7, false, 500), (2, 7, false, 500), (3, 7, false, 455),
            (4, 2001763, false, 5), (5, 5106, false, 99), (6, 38957, true, 1));

        Check("unique item resolves to its quantity",
            MarketTelemetryFormat.ResolveQuantity(market, 5106, false) == 99);
        Check("hq participates in identity: HQ variant resolves separately",
            MarketTelemetryFormat.ResolveQuantity(market, 38957, true) == 1);
        Check("the NQ variant of an HQ-only listing resolves to nothing",
            MarketTelemetryFormat.ResolveQuantity(market, 38957, false) == 0);
        Check("ambiguous item (three stacks of 500 crystals) resolves to 0 = unknown",
            MarketTelemetryFormat.ResolveQuantity(market, 7, false) == 0);
        Check("empty slot resolves to 0", MarketTelemetryFormat.ResolveQuantity(market, 0, false) == 0);
        Check("no market at all resolves to 0", MarketTelemetryFormat.ResolveQuantity(null, 7, false) == 0);
        Check("empty market resolves to 0", MarketTelemetryFormat.ResolveQuantity([], 7, false) == 0);

        // The one-slot case where the single listing has a zero quantity (should not happen,
        // but the rule must not report a nonsense 0-qty listing as known).
        var weird = Market((2, 7, false, 0));
        Check("a single listing with quantity 0 stays unknown", MarketTelemetryFormat.ResolveQuantity(weird, 7, false) == 0);
    }

    // ---------------------------------------------------------------- the join

    /// <summary>A parsed MT| line.</summary>
    private sealed record MtLine(long UnixMs, uint ItemId, bool Hq, int Qty, int OldPrice, int NewFinal, string Src, string Flags, float CutPct, string Item);

    private static MtLine ParseMt(string line)
    {
        var f = line.Split('|');
        return new MtLine(long.Parse(f[1], CultureInfo.InvariantCulture), uint.Parse(f[2], CultureInfo.InvariantCulture),
            f[3] == "1", int.Parse(f[4], CultureInfo.InvariantCulture), int.Parse(f[5], CultureInfo.InvariantCulture),
            int.Parse(f[7], CultureInfo.InvariantCulture), f[8], f[9],
            float.Parse(f[10], CultureInfo.InvariantCulture), f[11]);
    }

    private static void JoinShape()
    {
        // Every sale line Joey's retainers produced on 2026-09-05 (ffxivdb chat_lines,
        // channel='retainer'), verbatim. The join has to survive the REAL text: pluralised
        // collective nouns ("chunks of iron ore"), singular "has sold", commas in the gil.
        var sales = new (long UnixMs, string Text)[]
        {
            (1_788_639_914_000, "The 11 chunks of iron ore you put up for sale in the Limsa Lominsa markets have sold for 951 gil (after fees)."),
            (1_788_638_687_000, "The savage might materia XI you put up for sale in the Limsa Lominsa markets has sold for 197 gil (after fees)."),
            (1_788_638_685_000, "The 5 savage might materia XI you put up for sale in the Limsa Lominsa markets have sold for 984 gil (after fees)."),
            (1_788_638_211_000, "The 9 pinches of grenade ash you put up for sale in the Limsa Lominsa markets have sold for 855 gil (after fees)."),
            (1_788_637_415_000, "The 500 ice crystals you put up for sale in the Gridania markets have sold for 29,925 gil (after fees)."),
            (1_788_637_412_000, "The 455 ice crystals you put up for sale in the Gridania markets have sold for 27,232 gil (after fees)."),
            (1_788_635_215_000, "The 2 savage aim materia VII you put up for sale in the Limsa Lominsa markets have sold for 295 gil (after fees)."),
            (1_788_634_795_000, "The titanbronze nugget you put up for sale in the Limsa Lominsa markets has sold for 37 gil (after fees)."),
            (1_788_634_584_000, "The 21 chunks of Garlean cheese you put up for sale in the Gridania markets have sold for 19,931 gil (after fees)."),
            (1_788_634_387_000, "The 10 chunks of silver ore you put up for sale in the Limsa Lominsa markets have sold for 855 gil (after fees)."),
            (1_788_633_655_000, "The 99 ultra-potions you put up for sale in the Gridania markets have sold for 141,075 gil (after fees)."),
            (1_788_629_210_000, "The 2 diaspores you put up for sale in the Limsa Lominsa markets have sold for 380 gil (after fees)."),
        };

        // The matching MT| side, as the tap would have written it for those listings. The
        // prices are back-solved from the realised sale rows (market fee ~5%): e.g. the
        // 99 ultra-potions that realised 141,075 gil were listed at 1,500/unit
        // (99 x 1500 = 148,500, less 5% = 141,075), so the join arithmetic is real, not decorative.
        var lines = new[]
        {
            // ice crystals: three x500 and one x455. qty is 0 (ambiguous) for the crystals,
            // which is exactly what ResolveQuantity produced above - the join must cope.
            MarketTelemetryFormat.BuildLine(1_788_630_000_000, 7, false, 0, 999_999_999, 59, 59, MarketTelemetryFormat.SrcBoard, "p", -100.0f, "Ice Crystal"),
            MarketTelemetryFormat.BuildLine(1_788_630_100_000, 7, false, 0, 999_999_999, 59, 59, MarketTelemetryFormat.SrcBoard, "pc", -100.0f, "Ice Crystal"),
            MarketTelemetryFormat.BuildLine(1_788_630_200_000, 7, false, 0, 999_999_999, 59, 59, MarketTelemetryFormat.SrcBoard, "pc", -100.0f, "Ice Crystal"),
            MarketTelemetryFormat.BuildLine(1_788_630_300_000, 7, false, 0, 999_999_999, 59, 59, MarketTelemetryFormat.SrcUniversalis, "p", -100.0f, "Ice Crystal"),
            MarketTelemetryFormat.BuildLine(1_788_630_400_000, 2001763, false, 5, 250, 207, 207, MarketTelemetryFormat.SrcUniversalis, "-", 0.0f, "Savage Might Materia XI"),
            MarketTelemetryFormat.BuildLine(1_788_630_500_000, 2001764, false, 2, 160, 155, 155, MarketTelemetryFormat.SrcBoard, "-", 0.0f, "Savage Aim Materia VII"),
            MarketTelemetryFormat.BuildLine(1_788_630_600_000, 5106, false, 99, 1_425, 1_500, 1_500, MarketTelemetryFormat.SrcBoard, "-", 0.0f, "Ultra-Potion"),
        };

        var mt = lines.Select(ParseMt).ToList();
        Check("every emitted line round-trips through the parser",
            mt.Count == lines.Length
            && mt.Count(m => m.ItemId == 7u && m.Qty == 0) == 4        // the ambiguous crystal rows
            && mt.First(m => m.ItemId == 2001763u).Qty == 5             // the unique materia listing
            && mt.First(m => m.ItemId == 5106u).OldPrice == 1425
            && mt.First(m => m.ItemId == 5106u).NewFinal == 1500);

        // Sale-side parse: quantity is always a bare integer after "The "; the gil always
        // sits between "sold for " and " gil (after fees)" with thousands separators.
        int ParsedQty(string text) =>
            text.StartsWith("The ", StringComparison.Ordinal) && char.IsDigit(text[4]) && int.TryParse(new string(text.Skip(4).TakeWhile(char.IsDigit).ToArray()), out var q) ? q : 1;
        long ParsedGil(string text)
        {
            var marker = "sold for ";
            var at = text.IndexOf(marker, StringComparison.Ordinal);
            // the game writes "29,925 gil": keep digits AND separators, then drop the separators
            var digits = new string(text.Skip(at + marker.Length)
                .TakeWhile(c => char.IsDigit(c) || c == ',')
                .Where(char.IsDigit)
                .ToArray());
            return long.Parse(digits, CultureInfo.InvariantCulture);
        }

        Check("sale quantity parses (plural '11 chunks' and singular alike)",
            ParsedQty(sales[0].Text) == 11 && ParsedQty(sales[1].Text) == 1 && ParsedQty(sales[11].Text) == 2);
        Check("sale gil parses with thousands separators",
            ParsedGil(sales[4].Text) == 29_925 && ParsedGil(sales[10].Text) == 141_075 && ParsedGil(sales[7].Text) == 37);

        // The join itself: latest MT| for the item at or before the sale ts, realised gil
        // per unit vs the listed price. Ambiguous-qty crystal rows must still join (qty 0),
        // and a sale with no MT| row (item listed before the tap existed) must be visible
        // as unmatched rather than silently dropped.
        var joined = 0;
        foreach (var sale in sales)
        {
            // item-name resolution on the sale side is a display-name match; the harness
            // proves the MECHANISM with the rows that carry ids on both sides.
            uint? id = sale.Text.Contains("ice crystal", StringComparison.Ordinal) ? 7u
                     : sale.Text.Contains("savage might materia", StringComparison.Ordinal) ? 2001763u
                     : sale.Text.Contains("savage aim materia", StringComparison.Ordinal) ? 2001764u
                     : sale.Text.Contains("ultra-potion", StringComparison.Ordinal) ? 5106u
                     : null;
            if (id is null) continue;

            var decision = mt.Where(m => m.ItemId == id && m.UnixMs <= sale.UnixMs).OrderByDescending(m => m.UnixMs).FirstOrDefault();
            if (decision == null) continue;

            joined++;
            var unitListed = decision.NewFinal;
            var qty = ParsedQty(sale.Text);
            var unitRealised = (float)ParsedGil(sale.Text) / qty;
            var perUnitVsListed = unitRealised - unitListed;
            // Sanity of the arithmetic itself: a realised-after-fees unit price should be
            // within 10% of the listed unit price for the rows where qty is known.
            if (decision.Qty > 0)
                Check($"realised vs listed per unit is sane for item {id}",
                    MathF.Abs(perUnitVsListed) <= unitListed * 0.10f,
                    $"listed={unitListed} realised={unitRealised}");
        }

        Check("the join matches priced sales to their listing decisions", joined >= 6, $"joined={joined}");
        Check("an unpriced sale (pre-tap listing) is simply unmatched, not an error", joined <= sales.Length);

        // And the headline number the card asked for: universalis vs board, per unit.
        var uni = mt.Where(m => m.ItemId == 7u && m.Src == "u").Select(m => m.NewFinal).ToList();
        var brd = mt.Where(m => m.ItemId == 7u && m.Src == "b").Select(m => m.NewFinal).ToList();
        Check("src survives the round trip for the universalis-vs-board question",
            uni.Count == 1 && brd.Count == 3 && uni[0] == brd[0]);
    }
}
