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

Check("export filter: locked cheapest listing falls back to a buyable one, fully locked items are excluded", () =>
{
    // Synthetic catalog mirroring the live failure shape: the cheapest listing is locked
    // (achievement-gated), a pricier listing of the same item is free, a second item is
    // locked everywhere, a third is always buyable.
    ShopEntry E(uint item, uint vendor, uint shop, byte currency, uint price, uint quest, uint ach) =>
        new(item, vendor, shop, -1, currency, price, quest, ach, ShopSource.GilShop);
    var catalog = new List<ShopEntry>
    {
        E(99991, 1, 100, 2, 100, 0, 876),   // cheapest, achievement-locked
        E(99991, 2, 200, 2, 500, 0, 0),     // pricier, buyable
        E(99992, 3, 300, 2, 37, 67756, 0),  // quest-locked, only listing
        E(99994, 4, 400, 2, 37, 0, 0),      // control, always buyable
    };
    var names = new Dictionary<uint, string> { [99991] = "a", [99992] = "b", [99994] = "c" };
    var armoire = new List<ShoppingListBuilder.ArmouryEntry>
    {
        new(99991, ShoppingListBuilder.Ownership.NotOwned),
        new(99992, ShoppingListBuilder.Ownership.NotOwned),
        new(99994, ShoppingListBuilder.Ownership.NotOwned),
    };

    var locked = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), catalog, names,
        PlayerUnlockState.NothingUnlocked));
    var gil = locked.Currencies.First(c => c.CurrencyId == 2).Items;
    var a = gil.FirstOrDefault(i => i.Entry.ItemId == 99991)
            ?? throw new Exception("fallback to the buyable listing did not happen");
    if (a.Entry.VendorId != 2 || a.Entry.Price != 500)
        throw new Exception($"fallback picked vendor {a.Entry.VendorId} price {a.Entry.Price}, expected the buyable 2/500");
    if (gil.Any(i => i.Entry.ItemId == 99992))
        throw new Exception("fully locked item 99992 reached the code");
    var excluded99992 = locked.Excluded.FirstOrDefault(e => e.ItemId == 99992)
        ?? throw new Exception("fully locked item 99992 is silently missing instead of excluded");
    if (!excluded99992.Reason.StartsWith("locked") || !excluded99992.Reason.Contains("67756"))
        throw new Exception($"locked reason does not name the gate: '{excluded99992.Reason}'");
    if (!gil.Any(i => i.Entry.ItemId == 99994))
        throw new Exception("buyable control item 99994 disappeared");

    var earned = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), catalog, names,
        new PlayerUnlockState(q => q == 67756, a2 => a2 == 876)));
    var gilEarned = earned.Currencies.First(c => c.CurrencyId == 2).Items;
    var aEarned = gilEarned.FirstOrDefault(i => i.Entry.ItemId == 99991)
        ?? throw new Exception("earned achievement did not unlock the cheapest listing");
    if (aEarned.Entry.VendorId != 1 || aEarned.Entry.Price != 100)
        throw new Exception($"with the achievement earned the cheap listing should win, got vendor {aEarned.Entry.VendorId} price {aEarned.Entry.Price}");
    if (!gilEarned.Any(i => i.Entry.ItemId == 99992))
        throw new Exception("completed quest did not unlock item 99992");
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

    // Strictest export state: nothing completed, nothing earned. This is the 0.5.5.0
    // verdict fix (task armoire-0550): Knightshopper aborts the whole buy on the first
    // locked item, so the import must only contain what the player can actually buy now.
    var result = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), snapshot.Entries, itemNames,
        PlayerUnlockState.NothingUnlocked, int.MaxValue));

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

    // Nothing unlocked: every gated listing must be out of the codes. Anchors are the two
    // pieces whose locked listings started this task: 4303 is achievement-gated
    // (GilShopItem.AchievementRequired = 876 'Spreading the Love', Knightshopper's live
    // refusal that reached the buy), 13298 is quest-gated (67756 'A Pair of Hearts').
    var gilGroup = result.Currencies.First(c => c.CurrencyId == 2);
    foreach (var item in new[] { 4303u, 13298u })
    {
        var leaked = gilGroup.Items.FirstOrDefault(i => i.Entry.ItemId == item);
        if (leaked != null)
            throw new Exception($"item {item} is locked for a fresh character but {leaked.Entry.VendorId}/{leaked.Entry.ShopId} reached the import code");
        if (!result.Excluded.Any(e => e.ItemId == item && e.Reason.StartsWith("locked")))
            throw new Exception($"item {item} is neither in the code nor reported as locked-excluded");
    }
    var gatedLeaks = 0;
    foreach (var group2 in result.Currencies)
        foreach (var c in group2.Items)
        {
            if ((c.Entry.QuestRowId > 65535 && !PlayerUnlockState.NothingUnlocked.QuestComplete(c.Entry.QuestRowId))
                || (c.Entry.AchievementRowId != 0 && !PlayerUnlockState.NothingUnlocked.AchievementEarned(c.Entry.AchievementRowId)))
                throw new Exception($"gated item {c.Entry.ItemId} (quest {c.Entry.QuestRowId}, ach {c.Entry.AchievementRowId}) reached the import code");
            gatedLeaks++;
        }
    Console.WriteLine($"  nothing-unlocked state: no gated listing reached the codes ({gatedLeaks} exported candidates all gate-free); locked pieces reported as excluded");

    // Full-progress state: the same two anchors must come back, each at a pair
    // Knightshopper's own catalog accepts (the 0.5.4->0.5.5 ground truth, preserved).
    var unlockedResult = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), snapshot.Entries, itemNames,
        PlayerUnlockState.EverythingUnlocked, int.MaxValue));
    var unlockedGil = unlockedResult.Currencies.First(c => c.CurrencyId == 2);
    foreach (var item in new[] { 4303u, 13298u })
    {
        var c = unlockedGil.Items.FirstOrDefault(i => i.Entry.ItemId == item)
                ?? throw new Exception($"item {item} is missing from the Gil code with all progress done instead of being exported at a valid vendor");
        var reason = truth.Validate(2, c.Entry.ItemId, c.Entry.VendorId, c.Entry.ShopId, c.Entry.SubCurrency);
        if (reason != null)
            throw new Exception($"item {item} exported at ({c.Entry.VendorId}, {c.Entry.ShopId}) but Knightshopper refuses it: {reason}");
        Console.WriteLine($"  anchor: {item} exported at ({c.Entry.VendorId}, {c.Entry.ShopId}), accepted by Knightshopper's catalog");
    }

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

Check("GROUND TRUTH: the PvP family covers the quartermaster shops Knightshopper's catalog has", () =>
{
    // The gap Joey reported (task armoire-pvp-gear-coverage): Trophy Crystal weapons and
    // armour were missing from the shopping list. The shops themselves were never the
    // problem - the crystal and collar quartermasters are placed ENpcBases whose ENpcData
    // directly references the shop rows - the currency-cost map in KnightshopperCatalogCore
    // simply did not know their cost items. Pins the full PvP family shape against the
    // native catalog: crystal quartermaster 1038441 (Trophy Crystal, item 36656, sub 1),
    // collar quartermaster 1024213 (Wolf Collar, item 21067, sub 2).
    var pvp = snapshot.Entries.Where(e => e.CurrencyId == 5).ToList();
    if (pvp.Count == 0)
        throw new Exception("no PvP entries: the Trophy Crystal and Wolf Collar shops never reach the catalog");
    foreach (var shop in new uint[] { 1770588, 1770589, 1770590, 1770591, 1770592, 1770593, 1770648, 1770649, 1770732, 1770972 })
        if (!pvp.Any(e => e.VendorId == 1038441 && e.ShopId == shop && e.SubCurrency == 1))
            throw new Exception($"Trophy Crystal shop {shop} missing at vendor 1038441 sub 1");
    if (!pvp.Any(e => e.VendorId == 1024213 && e.ShopId == 1770594 && e.SubCurrency == 2))
        throw new Exception("Wolf Collar shop 1770594 missing at vendor 1024213 sub 2");

    // The reported class: a Tropaios weapon (Trophy Crystal weapons). Its pair must be one
    // Knightshopper's own catalog accepts, not merely one we believe.
    var weapon = pvp.FirstOrDefault(e => e.ItemId == 36963)
                 ?? throw new Exception("Tropaios Sword 36963 (Trophy Crystal weapon) is not in the catalog");
    var weaponRefusal = truth.Validate(5, weapon.ItemId, weapon.VendorId, weapon.ShopId, weapon.SubCurrency);
    if (weaponRefusal != null)
        throw new Exception($"Knightshopper's catalog refuses the Trophy Crystal pair for 36963: {weaponRefusal}");
    Console.WriteLine($"  PvP: {pvp.Count} entries across {pvp.Select(e => e.ShopId).Distinct().Count()} shops, "
                      + $"subs {string.Join(',', pvp.Select(e => e.SubCurrency).Distinct().OrderBy(x => x))}; "
                      + $"anchor 36963 at ({weapon.VendorId}, {weapon.ShopId}) accepted");
});

Check("GROUND TRUTH: the PvP export code carries Trophy Crystal gear at pairs Knightshopper accepts", () =>
{
    var armoire = new List<ShoppingListBuilder.ArmouryEntry>();
    foreach (var row in Sheet<LuminaCabinet>())
        if (row.Item.RowId != 0)
            armoire.Add(new ShoppingListBuilder.ArmouryEntry(row.Item.RowId, ShoppingListBuilder.Ownership.NotOwned));
    var names = new Dictionary<uint, string>();
    foreach (var item in Sheet<Item>())
        names[item.RowId] = item.Name.ExtractText();

    // Full-progress state: the PvP currency code must exist and carry the anchor weapon.
    var unlocked = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), snapshot.Entries, names, PlayerUnlockState.EverythingUnlocked, int.MaxValue));
    var pvpGroup = unlocked.Currencies.FirstOrDefault(c => c.CurrencyId == 5)
                   ?? throw new Exception("no PvP currency code was generated at full progress");
    var sword = pvpGroup.Items.FirstOrDefault(i => i.Entry.ItemId == 36963)
                ?? throw new Exception("Tropaios Sword 36963 missing from the PvP code at full progress");
    var swordRefusal = truth.Validate(5, sword.Entry.ItemId, sword.Entry.VendorId, sword.Entry.ShopId, sword.Entry.SubCurrency);
    if (swordRefusal != null)
        throw new Exception($"Knightshopper's catalog refuses the exported PvP item: {swordRefusal}");

    // Nothing-unlocked state: no gated PvP listing may reach the code (the 0.5.5.0 rule).
    var locked = ShoppingListBuilder.Build(new ShoppingListBuilder.Input(
        armoire, new HashSet<uint>(), snapshot.Entries, names, PlayerUnlockState.NothingUnlocked, int.MaxValue));
    foreach (var group in locked.Currencies.Where(c => c.CurrencyId == 5))
        foreach (var c in group.Items)
            if (c.Entry.QuestRowId > 65535 || c.Entry.AchievementRowId != 0)
                throw new Exception($"gated PvP item {c.Entry.ItemId} reached the nothing-unlocked code");
    Console.WriteLine($"  PvP code at full progress: {pvpGroup.Items.Count} items; nothing-unlocked state exports "
                      + $"{locked.Currencies.Where(c => c.CurrencyId == 5).Sum(c => c.Items.Count)} PvP items, all gate-free");
});

Check("GROUND TRUTH: every PvP armoire piece Knightshopper sells that the list cannot name is reported left out with a reason", () =>
{
    // Wolf Mark gear sits behind the mark quartermaster, whose ENpcBase data has no direct
    // shop reference (one handler argument instead), so the scan can never name its shops
    // without reproducing Knightshopper's native handler resolution. Those pieces must not
    // vanish silently: each one missing from the catalog has to appear in the per-item
    // left-out report with a stated reason (task armoire-pvp-gear-coverage: no silent gaps).
    var armoire = new HashSet<uint>();
    foreach (var row in Sheet<LuminaCabinet>())
        if (row.Item.RowId != 0) armoire.Add(row.Item.RowId);
    var covered = snapshot.Entries.Where(e => e.CurrencyId == 5).Select(e => e.ItemId).ToHashSet();
    var unreported = new List<uint>();
    foreach (var item in truth.ItemsFor(5))
    {
        if (!armoire.Contains(item) || covered.Contains(item)) continue;
        if (!snapshot.LeftOutPieces.Any(p => p.ItemId == item))
            unreported.Add(item);
    }
    if (unreported.Count > 0)
        throw new Exception($"{unreported.Count} PvP armoire piece(s) are neither covered nor reported left out - first: {unreported[0]}");
    var wolfMark = snapshot.LeftOutPieces.Where(p => p.ItemId == 7360).FirstOrDefault()
                   ?? throw new Exception("Lionsmane Armet 7360 (Wolf Mark gear) is not in the left-out report");
    if (string.IsNullOrWhiteSpace(wolfMark.Reason))
        throw new Exception("left-out piece 7360 carries no reason");
    Console.WriteLine($"  left-out report: {snapshot.LeftOutPieces.Count} piece(s) with reasons; PvP-family unreported: 0");
});

Console.WriteLine(failures == 0 ? "OK" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
