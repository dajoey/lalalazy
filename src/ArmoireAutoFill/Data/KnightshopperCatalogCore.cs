using System.Diagnostics;
using ArmoireAutoFill.Data.Shopping;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;

namespace ArmoireAutoFill.Data;

// The scan result: buyable entries plus the skip accounting the UI reports.
public sealed record CatalogSnapshot(
    IReadOnlyList<ShopEntry> Entries,
    IReadOnlyDictionary<uint, string> NpcNames,
    int SkippedUnlinkedShops,
    int SkippedGilSpecialShops,
    int UnderlistedItemCount)
{
    public static readonly CatalogSnapshot Empty = new([], new Dictionary<uint, string>(), 0, 0, 0);
}

// What one build attempt scanned and skipped, for the one-per-build log line and any
// failure detail. Returned by the build even when individual shop rows were skipped.
public sealed record CatalogBuildStats(
    int SpecialShopRowsScanned,
    int GilShopRowsScanned,
    int ShopsFailedSpecialShop,
    int ShopsFailedGilShop,
    int EntriesSkippedUnresolvableItem,
    string? FirstFailure,
    double ElapsedMs);

// Pure catalog scan, shared by the in-game builder (KnightshopperCatalogBuilder, which
// feeds it Dalamud's sheets) and the offline harness (tests/ArmoireAutoFill.CatalogHarness,
// which feeds it real sheet rows from a read-only sqpack copy). Must stay free of
// ECommons/Dalamud so it compiles and runs without the game.
//
// Row-reference safety (the 0.5.1.0 in-game crash): the Lumina 3 generated getters for
// row-reference columns (e.g. SpecialShop.ItemStruct.ReceiveItemsStruct.Item) throw
// NullReferenceException when called on the DEFAULT nested struct — the value FirstOrDefault
// returns when a nested collection is empty or matches nothing — because the getter
// dereferences the struct's null ExcelPage before anything else. RowId itself never resolves
// a row (RowRef<T> is a struct carrying the raw id), so the scan below reads ids only on real
// rows, validates them against the Item sheet before emitting, and never calls a generated
// getter on a default struct. One bad shop row is skipped and counted, never fatal.
//
// Catalog rules (evidence-validated 2026-10-07 against Knightshopper 1.0.1.6's import
// validation and real shopping-list data):
//   * Vendor links come ONLY from ENpcBase.ENpcData references that point directly at a
//     shop row id. Handler-encoded references (topic selects, scripted shops) are not
//     resolvable and are skipped — the generated list only ever names shops Knightshopper
//     itself can reach, so an import never fails on an unknown shop.
//   * Gil entries come from GilShop rows only. SpecialShop entries priced in gil are
//     excluded: Knightshopper's gil catalog has not been verified to include them and one
//     unknown shop would reject the whole import.
//   * SpecialShop entries are included when their first cost is one of the mapped currency
//     items below, or when CostType == 2 (special currency bucket, i.e. tomestones, where
//     sub-currency = bucket id - 1).
//   * Quest-locked entries are included (import validation does not check quests; the
//     purchase does) and carry the quest row id so the UI can flag them.
public static class KnightshopperCatalogCore
{
    // Currency item ids observed as SpecialShop costs. Sub-currency rules are
    // family-specific and were validated against real shopping-list data:
    //   * Hunt: ascending cost-item id (27 Allied Seal -> 0, 10307 Centurio Seal -> 1,
    //     26533 Sack of Nuts -> 2).
    //   * PVP: Wolf Mark shops are sub-currency 0.
    //   * Bicolor Gemstone and MGP are "simple" families (Knightshopper's import check
    //     ignores SubCurrency for them); its own lists use -1, so we do too.
    //   * Company seals are intentionally absent: Knightshopper's availability check has
    //     no CompanySeal case, so every seal entry would fail import validation.
    //   * Scrip / Occult Crescent / Firmament shops sell no armoire-eligible items
    //     (verified), so they are not mapped.
    internal static readonly Dictionary<uint, (byte CurrencyId, int SubCurrency)> SpecialShopCostItems = new()
    {
        [26807] = (0, -1), // Bicolor Gemstone
        [25] = (5, 0),     // Wolf Mark
        [27] = (3, 0),     // Allied Seal
        [10307] = (3, 1),  // Centurio Seal
        [26533] = (3, 2),  // Sack of Nuts
        [29] = (4, -1),    // MGP
    };

    internal static CatalogBuildStats Build(
        ExcelSheet<GilShop> gilShopSheet,
        ExcelSheet<SpecialShop> specialShopSheet,
        SubrowExcelSheet<GilShopItem>? gilShopItemSheet,
        ExcelSheet<ENpcBase> npcBaseSheet,
        ExcelSheet<ENpcResident> npcResidentSheet,
        ExcelSheet<Item>? itemSheet,
        ExcelSheet<LuminaCabinet>? cabinetSheet,
        out CatalogSnapshot snapshot)
    {
        var clock = Stopwatch.StartNew();

        // Armoire-eligible item ids, used to report how many pieces the generated list
        // under-counts (shops it deliberately does not name — see the header comment).
        var armoireItems = new HashSet<uint>();
        if (cabinetSheet != null)
            foreach (var row in cabinetSheet)
                if (row.Item.RowId != 0)
                    armoireItems.Add(row.Item.RowId);
        var underlistedItems = new HashSet<uint>();

        // Reverse index: shop row id -> sorted NPC row ids. Only direct references.
        var shopToNpcs = new Dictionary<uint, List<uint>>();
        foreach (var npc in npcBaseSheet)
        {
            foreach (var @ref in npc.ENpcData)
            {
                var rowId = @ref.RowId;
                if (rowId == 0) continue;

                var isShop = (rowId >= 262144 && gilShopSheet.TryGetRow(rowId, out _))
                             || specialShopSheet.TryGetRow(rowId, out _);
                if (!isShop) continue;

                if (!shopToNpcs.TryGetValue(rowId, out var list))
                    shopToNpcs[rowId] = list = [];
                list.Add(npc.RowId);
            }
        }
        foreach (var list in shopToNpcs.Values)
            list.Sort();

        var npcNames = new Dictionary<uint, string>();
        foreach (var npc in npcResidentSheet)
            npcNames[npc.RowId] = npc.Singular.ToString();

        var entries = new List<ShopEntry>();
        var skippedUnlinked = 0;
        var skippedGilSpecial = 0;
        var unresolvableSkips = 0;
        var failedSpecialShopShops = 0;
        var failedGilShopShops = 0;
        string? firstFailure = null;

        // --- SpecialShop: currency-cost entries ---
        foreach (var shop in specialShopSheet)
        {
            try
            {
                if (!shopToNpcs.ContainsKey(shop.RowId))
                {
                    skippedUnlinked++;
                    foreach (var itemEntry in shop.Item)
                    {
                        var skippedReceive = FirstReceivableItem(itemEntry);
                        if (skippedReceive.ItemId != 0)
                            underlistedItems.Add(skippedReceive.ItemId);
                    }
                    continue;
                }

                foreach (var itemEntry in shop.Item)
                {
                    var receive = FirstReceivableItem(itemEntry);
                    if (receive.ItemId == 0)
                        continue;

                    if (itemSheet != null && !itemSheet.TryGetRow(receive.ItemId, out _))
                    {
                        // A receive id with no Item row: never emit it (an unresolvable id
                        // could make Knightshopper's import reject a whole payload).
                        unresolvableSkips++;
                        continue;
                    }

                    var (costType, costItemId, currencyCost) = FirstCost(itemEntry);
                    if (costType == 2)
                    {
                        // Special currency bucket (tomestones): ItemCost.RowId is the bucket id.
                        var bucket = (int)costItemId;
                        if (bucket is < 1 or > 3)
                            continue;
                        entries.Add(new ShopEntry(receive.ItemId, FirstNpc(shopToNpcs, shop.RowId),
                            shop.RowId, bucket - 1, 7, currencyCost, receive.QuestRowId, ShopSource.SpecialShop));
                        continue;
                    }

                    if (costItemId == 0)
                        continue;

                    if (costItemId == 1)
                    {
                        // Gil-priced SpecialShop entry — excluded (see header comment).
                        skippedGilSpecial++;
                        underlistedItems.Add(receive.ItemId);
                        continue;
                    }

                    if (!SpecialShopCostItems.TryGetValue(costItemId, out var family))
                        continue; // not one of Knightshopper's currencies

                    entries.Add(new ShopEntry(receive.ItemId, FirstNpc(shopToNpcs, shop.RowId),
                        shop.RowId, family.SubCurrency, family.CurrencyId, currencyCost,
                        receive.QuestRowId, ShopSource.SpecialShop));
                }
            }
            catch (Exception ex)
            {
                // One bad shop row must never abort the whole catalog.
                failedSpecialShopShops++;
                firstFailure ??= $"SpecialShop row {shop.RowId}: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // --- GilShop: vendor-price entries (sub-currency -1, no price column) ---
        if (gilShopItemSheet != null)
        {
            foreach (var shop in gilShopItemSheet)
            {
                try
                {
                    if (!shopToNpcs.ContainsKey(shop.RowId))
                    {
                        skippedUnlinked++;
                        foreach (var skippedSubrow in shop)
                            if (skippedSubrow.Item.RowId != 0)
                                underlistedItems.Add(skippedSubrow.Item.RowId);
                        continue;
                    }

                    foreach (var subrow in shop)
                    {
                        var item = subrow.Item;
                        if (item.RowId == 0)
                            continue;
                        if (itemSheet == null || !itemSheet.TryGetRow(item.RowId, out var itemRow))
                        {
                            unresolvableSkips++;
                            continue;
                        }
                        var quest = subrow.QuestRequired.FirstOrDefault(q => q.RowId != 0);
                        entries.Add(new ShopEntry(item.RowId, FirstNpc(shopToNpcs, shop.RowId),
                            shop.RowId, -1, 2, itemRow.PriceMid > 0 ? itemRow.PriceMid : null,
                            quest.RowId, ShopSource.GilShop));
                    }
                }
                catch (Exception ex)
                {
                    failedGilShopShops++;
                    firstFailure ??= $"GilShopItem row {shop.RowId}: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        underlistedItems.IntersectWith(armoireItems);
        var underlistedCount = underlistedItems.Count;

        snapshot = new CatalogSnapshot(entries, npcNames, skippedUnlinked, skippedGilSpecial, underlistedCount);
        clock.Stop();
        return new CatalogBuildStats(
            specialShopSheet.Count, gilShopItemSheet?.Count ?? 0,
            failedSpecialShopShops, failedGilShopShops, unresolvableSkips, firstFailure,
            clock.Elapsed.TotalMilliseconds);
    }

    private record struct ReceivableItem(uint ItemId, uint QuestRowId);

    // The receive scan replaces the previous FirstOrDefault over ReceiveItems: iterating real
    // rows and reading raw ids is always safe; the same (Item.RowId != 0 && ReceiveCount != 0)
    // predicate decides, and an empty/matching-none list yields ItemId 0 instead of the
    // default struct whose generated Item getter crashed the build.
    private static ReceivableItem FirstReceivableItem(SpecialShop.ItemStruct itemEntry)
    {
        foreach (var receiveItem in itemEntry.ReceiveItems)
            if (receiveItem.Item.RowId != 0 && receiveItem.ReceiveCount != 0)
                return new ReceivableItem(receiveItem.Item.RowId, itemEntry.Quest.RowId);
        return default;
    }

    // Same default-struct hazard for the cost list: returns (0, 0, null) when no cost row has
    // a nonzero item id or CostType 2 — the same predicate FirstOrDefault expressed before.
    private static (byte CostType, uint CostItemId, uint? CurrencyCost) FirstCost(SpecialShop.ItemStruct itemEntry)
    {
        foreach (var cost in itemEntry.ItemCosts)
            if (cost.ItemCost.RowId != 0 || cost.CostType == 2)
                return (cost.CostType, cost.ItemCost.RowId, cost.CurrencyCost);
        return (0, 0, null);
    }

    private static uint FirstNpc(Dictionary<uint, List<uint>> shopToNpcs, uint shopRowId)
        => shopToNpcs.TryGetValue(shopRowId, out var list) && list.Count > 0 ? list[0] : 0;
}

// Pure build-once/back-off gate: the window calls ShouldAttempt() on every frame while the
// section is open; one failed attempt backs off (no per-frame retry, one log line instead of
// one per frame) until the user presses Refresh. Kept pure so the offline harness can prove
// the back-off.
public static class CatalogBuildGate
{
    public enum Phase { NotAttempted, Built, Failed }

    public static Phase Current { get; private set; } = Phase.NotAttempted;
    public static bool RefreshRequested { get; private set; }

    public static bool ShouldAttempt() => Current == Phase.NotAttempted || RefreshRequested;

    public static void RequestRefresh() => RefreshRequested = true;

    // Called by the executor right before an attempt so one refresh press runs one build.
    public static void ConsumeRefresh() => RefreshRequested = false;

    public static void MarkBuilt()
    {
        Current = Phase.Built;
        RefreshRequested = false;
    }

    public static void MarkFailed()
    {
        Current = Phase.Failed;
        RefreshRequested = false;
    }
}
