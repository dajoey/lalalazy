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

// LGB vendor placement (what KnightshopperVendorPlacement computes in-game from the live
// game files): fixture produced by tools/KnightshopperGroundTruth/PlacedNpcs on the same
// install the sqpack copy and the native catalog fixture come from.
IReadOnlySet<uint> LoadPlacedNpcs()
{
    using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "ks-placed-npcs.json.gz"));
    using var gz = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
    using var doc = System.Text.Json.JsonDocument.Parse(gz);
    return doc.RootElement.GetProperty("placedNpcs").EnumerateArray().Select(e => e.GetUInt32()).ToHashSet();
}
var placedNpcs = LoadPlacedNpcs();

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
        Sheet<ENpcBase>(), Sheet<ENpcResident>(), placedNpcs, Sheet<Item>(), Sheet<LuminaCabinet>(), out snapshot);

    if (snapshot.Entries.Count == 0)
        throw new Exception("catalog is empty: no buyable entries built from real sheet rows");
    if (stats.ShopsFailedSpecialShop != 0 || stats.ShopsFailedGilShop != 0)
        throw new Exception($"scan threw inside {stats.ShopsFailedSpecialShop} SpecialShop and "
                            + $"{stats.ShopsFailedGilShop} GilShop shops (first: {stats.FirstFailure})");
    // The in-game builder runs this on a background task (KnightshopperCatalogBuilder.RequestBuild),
    // never on the draw thread; 2 s is a regression tripwire that survives a loaded CI host
    // (the cold scan measures ~450 ms on an idle one).
    if (stats.ElapsedMs > 2000)
        throw new Exception($"build took {stats.ElapsedMs:F0} ms — far heavier than the measured ~450 ms");

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

// ---- GROUND TRUTH (tasks-20261008-armoire-ks-gil-rejection-rootcause-01) ----
// Why this section exists: the first Armoire acceptance harness (0.5.4.0) ran exported codes
// through a port of Knightshopper's import check whose vendor membership was Armoire's own
// inferred model (Level-sheet placement), so a pass only proved the model agreed with itself -
// and Knightshopper refused 66 of the 76 Gil items that build exported. This section uses
// Knightshopper's OWN catalog instead: the output of its native library run on a real game
// install (fixtures/ks-native-catalog.json.gz), put through a port of its managed import check
// (KnightshopperNativeCatalog). Nothing in it reads Armoire's catalog rules, Level rows or
// ENpcBase links.
var truthDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
var truth = KnightshopperNativeCatalog.Load(Path.Combine(truthDir, "ks-native-catalog.json.gz"));
Console.WriteLine($"  ground truth: {truth.VendorCount} vendors, {truth.ShopCount} shops, {truth.ListingCount} listings - {truth.Source}");

Check("ground truth reproduces the live Knightshopper refusals and acceptance", () =>
{
    // Joey's Knightshopper Gil config (live accepted pair).
    var ok = truth.Validate(2, 4868, 1001787, 262158, -1);
    if (ok != null) throw new Exception($"known-good 4868@1001787/262158 rejected by ground truth: {ok}");
    // Live refusals ('Item N is not available from its shared Gil vendor.'): 4303 from the
    // maid shop (Joey's log, the build before 0.5.4.0) and 13298 from the House Valentione
    // maid 1016441's shop 262587 (0.5.4.0). Ground truth must refuse both pairs...
    foreach (var (item, vendor, shop) in new[] { (4303u, 1005608u, 262424u), (13298u, 1016441u, 262587u) })
        if (truth.Validate(2, item, vendor, shop, -1) == null)
            throw new Exception($"ground truth accepts {item}@({vendor}, {shop}), which Knightshopper refused live");
    // ...and Knightshopper's own catalog does sell both items, at the recompense officers.
    foreach (var item in new[] { 4303u, 13298u })
    {
        var pairs = truth.Pairs(2, item).ToList();
        if (pairs.Count == 0)
            throw new Exception($"Knightshopper's catalog has no Gil vendor for {item}");
        Console.WriteLine($"  Knightshopper's own catalog sells {item} at {string.Join(", ", pairs.Select(p => $"({p.Vendor}, {p.Shop})"))}");
    }
});

Check("the vendor pairs behind the two live refusals are never exported", () =>
{
    foreach (var (item, vendor, shop) in new[] { (4303u, 1005608u, 262424u), (13298u, 1016441u, 262587u) })
        if (snapshot.Entries.Any(e => e.ItemId == item && e.VendorId == vendor && e.ShopId == shop))
            throw new Exception($"catalog still names {item}@({vendor}, {shop}), refused live by Knightshopper");
});

Check("GROUND TRUTH: every exported code, every currency, passes Knightshopper's own catalog", () =>
{
    var gilShopItemSheet = gameData.GetSubrowExcelSheet<GilShopItem>();
    var armoire = new List<ShoppingListBuilder.ArmouryEntry>();
    foreach (var row in Sheet<LuminaCabinet>())
        if (row.Item.RowId != 0)
            armoire.Add(new ShoppingListBuilder.ArmouryEntry(row.Item.RowId, ShoppingListBuilder.Ownership.NotOwned));
    var itemNames = new Dictionary<uint, string>();
    foreach (var item in Sheet<Item>())
        itemNames[item.RowId] = item.Name.ExtractText();
    var result = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), snapshot.Entries, itemNames, int.MaxValue));

    var total = 0;
    var refused = new List<(string Currency, uint Item, string Name, uint Vendor, uint Shop, string Reason)>();
    foreach (var group in result.Currencies)
    {
        var accepted = 0;
        foreach (var c in group.Items)
        {
            total++;
            var reason = truth.Validate(group.CurrencyId, c.Entry.ItemId, c.Entry.VendorId, c.Entry.ShopId, c.Entry.SubCurrency);
            if (reason == null) accepted++;
            else refused.Add((group.CurrencyName, c.Entry.ItemId, c.Name, c.Entry.VendorId, c.Entry.ShopId, reason));
        }
        Console.WriteLine($"  {group.CurrencyName}: {group.Items.Count} items exported, {accepted} accepted by Knightshopper's catalog, {group.Items.Count - accepted} refused");
    }
    Console.WriteLine($"  exported {total} item(s) across {result.Currencies.Count} currency code(s); Knightshopper's own catalog refuses {refused.Count}");
    // The two items Knightshopper refused live must be IN the Gil code now (at a pair its own
    // catalog has), not silently dropped to make the refusals go away.
    var gilGroup = result.Currencies.First(c => c.CurrencyId == 2);
    foreach (var item in new[] { 4303u, 13298u })
    {
        var c = gilGroup.Items.FirstOrDefault(i => i.Entry.ItemId == item)
                ?? throw new Exception($"item {item} is missing from the Gil code instead of being exported at a valid vendor");
        Console.WriteLine($"  anchor: {item} exported at ({c.Entry.VendorId}, {c.Entry.ShopId}), accepted by Knightshopper's catalog");
    }
    var bango = gilGroup.Items.FirstOrDefault(i => i.Entry.ItemId == 4868);
    Console.WriteLine($"  anchor: 4868 (Gysahl Greens) is {(bango == null ? "not an armoire item, so not in the code; checked directly above" : $"exported at ({bango.Entry.VendorId}, {bango.Entry.ShopId})")}");
    foreach (var r in refused)
        Console.WriteLine($"    REFUSED {r.Currency}: item {r.Item} '{r.Name}' @({r.Vendor}, {r.Shop}) - {r.Reason}");
    if (refused.Count > 0)
        throw new Exception($"Knightshopper would refuse {refused.Count} of {total} exported item(s); a code with even one of them is rejected whole");
});

Check("GROUND TRUTH: every catalog entry, every currency, is a pair Knightshopper's catalog has", () =>
{
    // Stronger than the export check: not only the armoire pieces a player is missing today
    // but EVERY (item, vendor, shop, sub-currency) the catalog could ever put in a code, so a
    // future cabinet addition cannot reintroduce a refusal unseen.
    //
    // Known divergence, NOT a regression and not exportable: Knightshopper's own catalog
    // registers the two current-tier tomestone WEAPON shops (1770913 Mathematics IL 750,
    // 1770982 Mnemonics IL 780) but lists none of their items, a listing filter this port
    // does not reproduce. No Cabinet (armoire) item is sold there, which the armoire-only
    // assertion below proves; the allow-list is exact so any other refusal fails the run.
    uint[] knownWeaponShops = [1770913, 1770982];
    var armoireItemIds = new HashSet<uint>();
    foreach (var row in Sheet<LuminaCabinet>())
        if (row.Item.RowId != 0) armoireItemIds.Add(row.Item.RowId);

    var unexpected = new List<string>();
    var allowed = 0;
    foreach (var g in snapshot.Entries.GroupBy(e => e.CurrencyId).OrderBy(g => g.Key))
    {
        var bad = 0;
        foreach (var e in g)
        {
            var reason = truth.Validate(e.CurrencyId, e.ItemId, e.VendorId, e.ShopId, e.SubCurrency);
            if (reason == null) continue;
            bad++;
            if (e.CurrencyId == 7 && Array.IndexOf(knownWeaponShops, e.ShopId) >= 0 && !armoireItemIds.Contains(e.ItemId))
            {
                allowed++;
                continue;
            }
            unexpected.Add($"{CurrencyNames.For(e.CurrencyId)}: item {e.ItemId} @({e.VendorId}, {e.ShopId}, sub {e.SubCurrency}) - {reason}");
        }
        Console.WriteLine($"  {CurrencyNames.For(g.Key)}: {g.Count()} catalog entries, {g.Count() - bad} accepted, {bad} refused");
    }
    Console.WriteLine($"  documented non-exportable divergence (tomestone weapon shops): {allowed} entries");
    foreach (var r in unexpected.Take(40)) Console.WriteLine($"    REFUSED {r}");
    if (unexpected.Count > 0)
        throw new Exception($"Knightshopper's catalog lacks {unexpected.Count} catalog entries beyond the documented weapon shops - first: {unexpected[0]}");
});

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
