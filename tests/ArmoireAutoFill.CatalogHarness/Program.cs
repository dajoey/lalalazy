using System.Diagnostics;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.CatalogHarness;
using ArmoireAutoFill.Data.Shopping;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;

// Regression harness for the Knightshopper catalog build (tasks-20261008-armoire-0510).
// Runs the REAL pure scan (KnightshopperCatalogCore) against a read-only copy of the
// real game excel sqpack, the same rows the in-game 0.5.1.0 build crashed on:
// NullReferenceException from the Lumina generated ReceiveItems.Item getter (a default
// nested struct from FirstOrDefault over an empty receive list), thrown once per frame
// and aborting every catalog build. Also proves the build-once/back-off gate so a
// failing build is never retried per frame.
var failures = 0;
var sqpack = Environment.GetEnvironmentVariable("ARMOIRE_CATALOG_HARNESS_SQPACK")
             ?? "/home/dajoey/scratch/armoire-catalog-nullref/sqpack";

void Check(string name, System.Action body)
{
    try
    {
        body();
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex);
        return;
    }
    Console.WriteLine($"PASS {name}");
}

var gameData = new GameData(sqpack);

ExcelSheet<T> Sheet<T>() where T : struct, IExcelRow<T>
    => gameData.GetExcelSheet<T>() ?? throw new Exception("GetExcelSheet returned null (sheet not present in the sqpack copy)");

Check("real game sheets load from the sqpack copy", () =>
{
    var special = Sheet<SpecialShop>();
    var gil = Sheet<GilShop>();
    var npcBase = Sheet<ENpcBase>();
    var npcResident = Sheet<ENpcResident>();
    var item = Sheet<Item>();
    var cabinet = Sheet<LuminaCabinet>();
    if (special.Count == 0) throw new Exception("SpecialShop is empty");
    if (gil.Count == 0) throw new Exception("GilShop is empty");
    if (npcBase.Count == 0) throw new Exception("ENpcBase is empty");
    if (npcResident.Count == 0) throw new Exception("ENpcResident is empty");
    if (item.Count == 0) throw new Exception("Item is empty");
    if (cabinet.Count == 0) throw new Exception("Cabinet is empty");
    Console.WriteLine($"  SpecialShop {special.Count} rows, GilShop {gil.Count} rows, ENpcBase {npcBase.Count} rows, "
                      + $"ENpcResident {npcResident.Count} rows, Item {item.Count} rows, Cabinet {cabinet.Count} rows");
});

CatalogSnapshot snapshot = CatalogSnapshot.Empty;
Check("Knightshopper catalog builds from real sheet rows without throwing", () =>
{
    // Same sheet set the in-game builder feeds from Dalamud; GilShopItem is allowed to be
    // null there, so run the real-data path with it present.
    var gilShopItemSheet = gameData.GetSubrowExcelSheet<GilShopItem>();
    var stats = KnightshopperCatalogCore.Build(
        Sheet<GilShop>(), Sheet<SpecialShop>(), gilShopItemSheet,
        Sheet<ENpcBase>(), Sheet<ENpcResident>(), Sheet<Level>(), Sheet<Item>(), Sheet<LuminaCabinet>(), out snapshot);

    if (snapshot.Entries.Count == 0)
        throw new Exception("catalog is empty: no buyable entries built from real sheet rows");
    if (stats.ShopsFailedSpecialShop != 0 || stats.ShopsFailedGilShop != 0)
        throw new Exception($"scan threw inside {stats.ShopsFailedSpecialShop} SpecialShop and "
                            + $"{stats.ShopsFailedGilShop} GilShop shops (first: {stats.FirstFailure})");
    if (stats.ElapsedMs > 500)
        throw new Exception($"build took {stats.ElapsedMs:F0} ms — too heavy for the draw thread");

    var perCurrency = snapshot.Entries.GroupBy(e => e.CurrencyId)
        .OrderBy(g => g.Key)
        .Select(g => $"{CurrencyNames.For(g.Key)} {g.Count()}")
        .ToList();
    Console.WriteLine($"  {snapshot.Entries.Count} entries in {stats.ElapsedMs:F0} ms — {string.Join(", ", perCurrency)}");
    Console.WriteLine($"  shops without vendor link: {snapshot.SkippedUnlinkedShops}, gil-priced SpecialShop entries excluded: "
                      + $"{snapshot.SkippedGilSpecialShops}, unresolvable receive items skipped: {stats.EntriesSkippedUnresolvableItem}, "
                      + $"armoire pieces in excluded shops: {snapshot.UnderlistedItemCount}");

    // The shopping list must be nonempty per currency and carry a Gil group (the largest
    // family in the validated 0.5.1.0 rules) with a per-currency totals table downstream.
    if (!snapshot.Entries.Any(e => e.CurrencyId == 2))
        throw new Exception("no Gil entries: the largest validated family is missing");
    if (snapshot.UnderlistedItemCount <= 0)
        throw new Exception("underlisted count is 0: excluded-shop accounting regressed");
});

Check("failing build is not retried per frame; Refresh runs it exactly once more", () =>
{
    // Simulates the draw loop against the pure gate with an always-failing build.
    var attempts = 0;
    bool AttemptIfGateSays()
    {
        if (!CatalogBuildGate.ShouldAttempt())
            return false;
        attempts++;
        CatalogBuildGate.ConsumeRefresh();
        CatalogBuildGate.MarkFailed(); // the build always fails in this scenario
        return true;
    }

    for (var frame = 0; frame < 120; frame++)
        AttemptIfGateSays();
    if (attempts != 1)
        throw new Exception($"120 frames after a failed build produced {attempts} attempts (expected 1)");

    CatalogBuildGate.RequestRefresh();
    for (var frame = 0; frame < 120; frame++)
        AttemptIfGateSays();
    if (attempts != 2)
        throw new Exception($"120 frames after one Refresh produced {attempts} total attempts (expected 2)");

    // A successful build caches: no further attempts, even after stray refresh requests
    // the UI never sent.
    CatalogBuildGate.MarkBuilt();
    var before = attempts;
    for (var frame = 0; frame < 120; frame++)
        AttemptIfGateSays();
    if (attempts != before)
        throw new Exception($"built gate re-attempted {attempts - before} times in 120 frames (expected 0)");
});

Check("successful build is cached (gate built state never re-attempts)", () =>
{
    CatalogBuildGate.MarkBuilt();
    if (CatalogBuildGate.ShouldAttempt())
        throw new Exception("gate asks for a rebuild although the snapshot is already built");
    if (CatalogBuildGate.Current != CatalogBuildGate.Phase.Built)
        throw new Exception("gate phase is not Built");
});

// ---- Knightshopper acceptance port (tasks-20261008-armoire-import-instructions-wording-01) ----
// Runs the FULL export path (catalog -> shopping list -> KS1 share code -> decode) per
// currency through a port of Knightshopper 1.0.1.6's real import validation (decompiled
// HmcZxNdvtzR1P) over raw sqpack rows + the Level-sheet vendor placement rule its native
// discovery uses. Nothing may be rejected: Knightshopper refuses a WHOLE list when one
// item is unavailable (observed live: "Item 4303 is not available from its shared Gil
// vendor." 4x at 21:44 ET). Anchors are the live-verified acceptance (4868@1001787/262158
// in Joey's Knightshopper Gil config) and the live-verified rejections (4303 from the
// Valentione shops 262424/262631).
Check("exported codes per currency pass Knightshopper's real acceptance rules", () =>
{
    var gilShopSheet = Sheet<GilShop>();
    var gilShopItemSheet = gameData.GetSubrowExcelSheet<GilShopItem>()
        ?? throw new Exception("GilShopItem sheet missing from the sqpack copy");
    var levelSheet = Sheet<Level>();

    // Full export path: every armoire-eligible item NotOwned, so every buyable quadruple
    // the plugin could emit lands in some code.
    var armoire = new List<ShoppingListBuilder.ArmouryEntry>();
    foreach (var row in Sheet<LuminaCabinet>())
        if (row.Item.RowId != 0)
            armoire.Add(new ShoppingListBuilder.ArmouryEntry(row.Item.RowId, ShoppingListBuilder.Ownership.NotOwned));
    var itemNames = new Dictionary<uint, string>();
    foreach (var item in Sheet<Item>())
        itemNames[item.RowId] = item.Name.ExtractText();
    var result = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), snapshot.Entries, itemNames));
    if (result.Currencies.Count == 0)
        throw new Exception("no currency groups: export path produced nothing");

    var rejected = new List<string>();
    var validatedCount = 0;
    foreach (var group in result.Currencies)
    {
        var quadruples = group.Items.Select(c => new Ks1Item(
            c.Entry.ItemId, c.Entry.VendorId, c.Entry.ShopId, 1, c.Entry.SubCurrency)).ToList();
        var code = KnightshopperShare.Encode(group.CurrencyId, "harness", quadruples);
        if (!KnightshopperShare.TryDecode(code, out var decodedCurrency, out _,
                out var decoded, out var decodeError))
            throw new Exception($"currency {group.CurrencyId}: exported code does not round-trip: {decodeError}");
        if (decodedCurrency != group.CurrencyId)
            throw new Exception($"exported code currency {decodedCurrency} != {group.CurrencyId}");

        foreach (var item in decoded)
        {
            var reason = KnightshopperAcceptance.Validate(
                gilShopSheet, gilShopItemSheet, Sheet<SpecialShop>(), Sheet<ENpcBase>(), levelSheet,
                item.ItemId, item.VendorId, item.ShopId, item.SubCurrency, decodedCurrency);
            validatedCount++;
            if (reason != null)
                rejected.Add($"{CurrencyNames.For(decodedCurrency)}: {reason}");
        }
    }
    Console.WriteLine($"  validated {validatedCount} quadruple(s) across {result.Currencies.Count} currency code(s)");
    if (rejected.Count > 0)
        throw new Exception($"Knightshopper would reject {rejected.Count} exported item(s) — first: {rejected[0]}");
});

Check("anchor: item 4303 (Valentione) is absent from the catalog and its shops are rejected", () =>
{
    var gilShopSheet = Sheet<GilShop>();
    var gilShopItemSheet = gameData.GetSubrowExcelSheet<GilShopItem>()
        ?? throw new Exception("GilShopItem sheet missing from the sqpack copy");
    var levelSheet = Sheet<Level>();
    var specialShopSheet = Sheet<SpecialShop>();
    var npcBaseSheet = Sheet<ENpcBase>();

    if (snapshot.Entries.Any(e => e.ItemId == 4303))
    {
        var bad = snapshot.Entries.Where(e => e.ItemId == 4303)
            .Select(e => $"({e.VendorId}, {e.ShopId})").First();
        throw new Exception($"item 4303 still emitted from {bad}: Knightshopper logged 'Item 4303 is not available from its shared Gil vendor.'");
    }

    // The exact observed rejection: the maid/recompense-officer shops are event-spawned.
    foreach (var (vendor, shop) in new[] { (1005608u, 262424u), (1017613u, 262631u) })
    {
        var reason = KnightshopperAcceptance.Validate(gilShopSheet, gilShopItemSheet, specialShopSheet,
            npcBaseSheet, levelSheet, 4303, vendor, shop, -1, 2);
        if (reason == null)
            throw new Exception($"modelled catalog wrongly accepts 4303 from ({vendor}, {shop})");
        Console.WriteLine($"  anchor: 4303@({vendor}, {shop}) rejected — {reason}");
    }
});

Check("anchor: Bango Zango's gil shop (4868@1001787/262158) stays accepted", () =>
{
    var gilShopSheet = Sheet<GilShop>();
    var gilShopItemSheet = gameData.GetSubrowExcelSheet<GilShopItem>()
        ?? throw new Exception("GilShopItem sheet missing from the sqpack copy");
    var reason = KnightshopperAcceptance.Validate(gilShopSheet, gilShopItemSheet, Sheet<SpecialShop>(),
        Sheet<ENpcBase>(), Sheet<Level>(), 4868, 1001787, 262158, -1, 2);
    if (reason != null)
        throw new Exception($"known-good 4868@1001787/262158 rejected: {reason}");
    Console.WriteLine("  anchor: 4868@1001787/262158 accepted (matches Joey's Knightshopper Gil config)");
});

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
