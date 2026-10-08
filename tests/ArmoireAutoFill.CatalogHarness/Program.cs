using System.Diagnostics;
using ArmoireAutoFill.Data;
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
        Sheet<ENpcBase>(), Sheet<ENpcResident>(), Sheet<Item>(), Sheet<LuminaCabinet>(), out snapshot);

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

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
