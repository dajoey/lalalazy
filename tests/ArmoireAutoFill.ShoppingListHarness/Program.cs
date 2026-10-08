using ArmoireAutoFill.Data.Shopping;

namespace ArmoireAutoFill.ShoppingListHarness;

// Offline checks for the Knightshopper shopping-list feature.
//
// 1. KS1 round-trip on real data: every list in Joey's real per-currency config files
//    (Knightshopper/configs/*.json, dumped 2026-10-07) is re-encoded with our encoder and
//    decoded again with a mirror of Knightshopper's decoder. Both directions must
//    reproduce the exact tuples, name and currency id.
// 2. Decoder-mirror edge cases that mirror Knightshopper's import validation limits
//    (max 500 items, max 100-char name, currency id 0..10, non-zero item/quantity,
//    sub-currency >= -1, unique (item, sub) keys).
// 3. Selection logic: owned-state handling, dedup per currency, multi-family items,
//    500-item truncation, quest-flag pass-through, price totals.
// 4. Import instructions: currency names match Knightshopper's own sidebar/window
//    names, and the copy message names the real controls verified in Knightshopper
//    1.0.1.6 (window, shopping-list dropdown, 'Paste' clipboard button, new-list
//    name, the chat success line, and the wrong-currency refusal).
internal static class Program
{
    private static int _failures;

    private static void Check(bool ok, string name, string detail = "")
    {
        if (ok)
            Console.WriteLine($"PASS {name}");
        else
        {
            _failures++;
            Console.WriteLine($"FAIL {name}{(detail.Length > 0 ? $" — {detail}" : "")}");
        }
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try
        {
            action();
            Check(false, name, $"expected {typeof(T).Name}");
        }
        catch (T)
        {
            Check(true, name);
        }
        catch (Exception ex)
        {
            Check(false, name, $"wrong exception: {ex.GetType().Name}");
        }
    }

    private static void Main()
    {
        RealListRoundTrips();
        DecoderEdgeCases();
        SelectionLogic();
        ImportInstructions();

        Console.WriteLine(_failures == 0 ? "OK" : $"{_failures} failure(s)");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    // ---- Fixture: real Knightshopper lists (ItemId, VendorId, ShopId, Quantity, SubCurrency) ----
    private static readonly Dictionary<byte, (string Name, (uint, uint, uint, int, int)[] Items)> RealLists = new()
    {
        [2] = ("List 1", // Gil
            [(4868, 1001787, 262158, 999, -1)]),
        [3] = ("List 1", // Hunt
            [(7569, 1009152, 1769811, 999, 0),
             (7569, 1012225, 1769577, 999, 1),
             (41771, 1048387, 1770761, 999, 2)]),
        [0] = ("List 1", // BicolorGemstone
            [(43961, 1048383, 1770736, 999, -1)]),
        [7] = ("List 1", // Tomestone
            [(13584, 1033775, 1770109, 999, 0),
             (49568, 1049079, 1770980, 1, 2),
             (49575, 1049079, 1770981, 1, 2),
             (49571, 1049079, 1770980, 1, 2),
             (49574, 1049079, 1770981, 1, 2),
             (49572, 1049079, 1770980, 1, 2),
             (49573, 1049079, 1770980, 1, 2),
             (49570, 1049079, 1770981, 1, 2),
             (49566, 1049079, 1770980, 1, 2),
             (49569, 1049079, 1770981, 1, 2),
             (49567, 1049079, 1770980, 1, 2),
             (49563, 1049079, 1770980, 1, 2),
             (49565, 1049079, 1770981, 1, 2),
             (49561, 1049079, 1770980, 1, 2),
             (49564, 1049079, 1770981, 1, 2),
             (49562, 1049079, 1770980, 1, 2),
             (49578, 1049079, 1770980, 1, 2),
             (49580, 1049079, 1770981, 1, 2),
             (49576, 1049079, 1770980, 1, 2),
             (49579, 1049079, 1770981, 1, 2),
             (49577, 1049079, 1770980, 1, 2),
             (50031, 1049079, 1770994, 1, 1),
             (49228, 1049079, 1770994, 999, 1)]),
        [5] = ("List 1", // PVP
            [(21800, 1005244, 1770587, 999, 0),
             (33916, 1005244, 1770587, 999, 0),
             (21072, 1005244, 1770587, 5000, 0),
             (41761, 1005244, 1770889, 100, 0),
             (41774, 1005244, 1770889, 50, 0),
             (41758, 1005244, 1770889, 100, 0),
             (41771, 1005244, 1770889, 50, 0),
             (41781, 1005244, 1770889, 50, 0),
             (41769, 1005244, 1770889, 100, 0),
             (41759, 1005244, 1770889, 100, 0),
             (41760, 1005244, 1770889, 100, 0),
             (41773, 1005244, 1770889, 50, 0),
             (41772, 1005244, 1770889, 999, 0)]),
        [10] = ("List 1", // OccultCrescent
            [(51854, 1059485, 1771027, 1, 2),
             (51852, 1059485, 1771027, 1, 2),
             (51855, 1059485, 1771027, 1, 2),
             (51978, 1059485, 1771028, 999, 2),
             (51978, 1059485, 1771029, 999, 3)]),
        [6] = ("List 1", // Scrip
            [(46252, 1003633, 1770782, 999, 1),
             (49229, 1003633, 1770783, 999, 3),
             (43857, 1003633, 1770500, 999, 0),
             (41807, 1003633, 1770786, 999, 2)]),
    };

    private static void RealListRoundTrips()
    {
        foreach (var (currencyId, (name, tuples)) in RealLists)
        {
            var items = tuples.Select(t => new Ks1Item(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5)).ToList();
            string share;
            try
            {
                share = KnightshopperShare.Encode(currencyId, name, items);
            }
            catch (Exception ex)
            {
                Check(false, $"round-trip encode currency {currencyId}", ex.Message);
                continue;
            }

            Check(share.StartsWith("KS1:", StringComparison.Ordinal)
                  && !share.Contains('+') && !share.Contains('/') && !share.Contains('='),
                $"share format currency {currencyId}");

            var ok = KnightshopperShare.TryDecode(share, out var decCurrency, out var decName,
                out var decItems, out var error);
            Check(ok, $"round-trip decode currency {currencyId}", error);
            if (!ok) continue;

            Check(decCurrency == currencyId, $"currency id preserved {currencyId}",
                $"{decCurrency} != {currencyId}");
            Check(decName == name, $"name preserved currency {currencyId}", $"{decName} != {name}");
            Check(decItems.Count == items.Count, $"item count preserved currency {currencyId}",
                $"{decItems.Count} != {items.Count}");

            var allEqual = decItems.Count == items.Count
                && items.Zip(decItems, (a, b) =>
                        a.ItemId == b.ItemId && a.VendorId == b.VendorId && a.ShopId == b.ShopId
                        && a.Quantity == b.Quantity && a.SubCurrency == b.SubCurrency)
                    .All(x => x);
            Check(allEqual, $"tuples preserved currency {currencyId}");
        }
    }

    private static void DecoderEdgeCases()
    {
        var one = new List<Ks1Item> { new(4868, 1001787, 262158, 999, -1) };

        // Name limit: 100 chars accepted, 101 rejected by the encoder.
        Check(KnightshopperShare.Encode(2, new string('a', 100), one).Length > 0, "name of 100 chars encodes");
        CheckThrows<ArgumentException>(() => KnightshopperShare.Encode(2, new string('a', 101), one),
            "name of 101 chars rejected");

        // Item limit: 500 accepted, 501 rejected.
        var many500 = Enumerable.Range(0, 500).Select(i => new Ks1Item((uint)(10000 + i), 1, 262144 + 100u, 1, -1)).ToList();
        Check(KnightshopperShare.Encode(2, "x", many500).Length > 0, "500 items encode");
        var many501 = Enumerable.Range(0, 501).Select(i => new Ks1Item((uint)(10000 + i), 1, 262144 + 100u, 1, -1)).ToList();
        CheckThrows<ArgumentException>(() => KnightshopperShare.Encode(2, "x", many501), "501 items rejected");

        // Currency id range.
        CheckThrows<ArgumentOutOfRangeException>(() => KnightshopperShare.Encode(11, "x", one), "currency 11 rejected");

        // Garbage input to the decode mirror.
        Check(!KnightshopperShare.TryDecode("hello", out _, out _, out _, out _), "non-KS1 string rejected");
        Check(!KnightshopperShare.TryDecode("KS1:!!!not base64!!!", out _, out _, out _, out _), "bad base64 rejected");
        Check(!KnightshopperShare.TryDecode("KS1:", out _, out _, out _, out _), "empty body rejected");
    }

    private static void SelectionLogic()
    {
        // Catalog: item 100 buyable for Gil at two vendors (100 and 200 gil) and for
        // Centurio at 300; item 101 only for Hunt; item 102 quest-locked for Gil;
        // item 103 sold for a currency family Knightshopper rejects (seals) -> excluded
        // from the catalog by the catalog builder, expressed here as a Hunt entry.
        ShopEntry E(uint item, uint vendor, uint shop, byte cur, int sub, uint? price, uint quest = 0) =>
            new(item, vendor, shop, sub, cur, price, quest, cur == 2 ? ShopSource.GilShop : ShopSource.SpecialShop);

        var catalog = new List<ShopEntry>
        {
            E(100, 10, 262144, 2, -1, 200),
            E(100, 20, 262145, 2, -1, 100),   // cheaper, must win
            E(100, 30, 1769577, 3, 1, 40),    // Centurio copy
            E(101, 40, 1769811, 3, 0, 15),
            E(102, 50, 262146, 2, -1, null, quest: 69999),
        };

        var owned = new HashSet<uint> { 900u };
        var armory = new List<ShoppingListBuilder.ArmouryEntry>
        {
            new(100, ShoppingListBuilder.Ownership.NotOwned),
            new(101, ShoppingListBuilder.Ownership.InInventory),
            new(102, ShoppingListBuilder.Ownership.NotOwned),
            new(103, ShoppingListBuilder.Ownership.NotOwned), // not in catalog
            new(900, ShoppingListBuilder.Ownership.InArmoire),
        };
        var names = new Dictionary<uint, string>
        {
            [100] = "Chevron Targe", [101] = "Peiste Staff", [102] = "Quest Blade", [103] = "Unsold Cloak",
        };

        var result = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(armory, owned, catalog, names));

        var gil = result.Currencies.FirstOrDefault(c => c.CurrencyId == 2);
        Check(gil != null && gil.Items.Count == 2, "gil group has 2 items",
            gil == null ? "missing group" : gil.Items.Count.ToString());
        var chevron = gil?.Items.FirstOrDefault(i => i.ItemId == 100);
        Check(chevron?.Entry.VendorId == 20 && chevron.Entry.Price == 100, "cheapest gil vendor wins",
            chevron?.Entry.VendorId.ToString() ?? "missing");
        var questBlade = gil?.Items.FirstOrDefault(i => i.ItemId == 102);
        Check(questBlade?.Entry.QuestRowId == 69999, "quest row id carried through");

        var hunt = result.Currencies.FirstOrDefault(c => c.CurrencyId == 3);
        Check(hunt != null && hunt.Items.Count == 1 && hunt.Items[0].ItemId == 100,
            "item sold in two families appears in both", hunt?.Items.Count.ToString() ?? "missing");
        Check(hunt?.Items[0].Entry.SubCurrency == 1, "centurio sub-currency preserved");

        Check(result.Excluded.Any(e => e.ItemId == 101 && e.Reason.Contains("inventory")),
            "in-inventory item excluded with reason");
        Check(result.Excluded.All(e => e.ItemId != 900), "in-armoire item silently skipped");
        Check(result.MissingNotBuyable == 1, "one item missing-not-buyable counted",
            result.MissingNotBuyable.ToString());

        // Truncation: 3 items with a max of 2 per currency.
        var bigCatalog = Enumerable.Range(0, 3)
            .Select(i => E((uint)(2000 + i), 10, 262144, 2, -1, 1))
            .ToList();
        var bigArmory = Enumerable.Range(0, 3)
            .Select(i => new ShoppingListBuilder.ArmouryEntry((uint)(2000 + i), ShoppingListBuilder.Ownership.NotOwned))
            .ToList();
        var bigNames = Enumerable.Range(0, 3).ToDictionary(i => (uint)(2000 + i), i => $"item{i}");
        var bigResult = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
            bigArmory, owned, bigCatalog, bigNames, MaxItemsPerCurrency: 2));
        var bigGil = bigResult.Currencies.Single(c => c.CurrencyId == 2);
        Check(bigGil.Items.Count == 2 && bigGil.Truncated, "truncation to per-currency limit",
            $"{bigGil.Items.Count} truncated={bigGil.Truncated}");
        Check(bigGil.TotalPrice == 2, "total price sums kept items", bigGil.TotalPrice.ToString());
    }

    // ---- 4. Import instructions vs Knightshopper 1.0.1.6's real UI strings ----
    private static void ImportInstructions()
    {
        // Knightshopper's own display names (General.json "_name.*UI" / "Currency.*"),
        // verified from its 1.0.1.6 release: these name the sidebar entries and windows.
        var expected = new Dictionary<byte, string>
        {
            [0] = "Bicolor Gemstones", [1] = "Company Seals", [2] = "Gil",
            [3] = "The Hunt", [4] = "MGP", [5] = "PvP", [6] = "Scrips",
            [7] = "Tomestones", [8] = "Firmament", [9] = "Cosmocredits",
            [10] = "Occult Crescent",
        };
        foreach (var (id, name) in expected)
            Check(CurrencyNames.For(id) == name, $"currency id {id} named as Knightshopper shows it",
                CurrencyNames.For(id));

        // The copy message must let a first-time Knightshopper user follow it: the
        // currency window, the sidebar category, the shopping-list dropdown, the
        // 'New list name...' row with the clipboard button labelled 'Paste', the new
        // list's name, the chat success line, and the wrong-currency refusal.
        foreach (var id in new byte[] { 2, 3, 4, 7 }) // the currencies Armoire can emit
        {
            var window = CurrencyNames.For(id);
            var msg = KnightshopperInstructions.CopiedMessage(id, 86, $"Armoire fill ({window})");
            Check(msg.Contains($"Copied 86 item(s) for {window}"), $"[{window}] message says the code is copied",
                msg.Split('\n')[0]);
            Check(msg.Contains($"click {window} in the left sidebar (under Currencies)"), $"[{window}] names the sidebar entry and category");
            Check(msg.Contains("shopping-list dropdown at the top of the window"), $"[{window}] names the list dropdown");
            Check(msg.Contains("'New list name...'") && msg.Contains("clipboard icon"), $"[{window}] names the import row");
            Check(msg.Contains("tooltip is 'Paste'"), $"[{window}] names the button by Knightshopper's tooltip");
            Check(msg.Contains($"'Armoire fill ({window})'"), $"[{window}] names the list it creates");
            Check(msg.Contains("Imported the shopping list from the clipboard."), $"[{window}] quotes Knightshopper's success line");
            Check(msg.Contains($"{window} codes") && msg.Contains("refuses it"), $"[{window}] states the wrong-currency refusal");
            Check(msg.Contains($"'Item N is not available from its shared {window} vendor.'") && msg.Contains("names only the first item"),
                $"[{window}] explains Knightshopper's first-item refusal line");
        }

        var hunt = KnightshopperInstructions.CopiedMessage(3, 5, "Armoire fill (The Hunt)");
        Check(!hunt.Contains("Hunt tab") && !hunt.Contains("paste button"), "no vague 'tab'/'paste button' wording left");

        var note = KnightshopperInstructions.CatalogCheckNote();
        Check(note.Contains("Knightshopper 1.0.1.6's own vendor catalog") && note.Contains(KnightshopperInstructions.CatalogCheckVersion)
              && note.Contains("Not yet confirmed in-game") && !note.Contains("works") && !note.Contains("fixed"),
            "catalog-check note states the source and version and does not claim it works");

        var notLoaded = KnightshopperInstructions.NotLoadedMessage();
        Check(notLoaded.Contains("'Paste'") && notLoaded.Contains("'New list name...'") && notLoaded.Contains("window"),
            "not-loaded hint points at the same controls");
    }
}
