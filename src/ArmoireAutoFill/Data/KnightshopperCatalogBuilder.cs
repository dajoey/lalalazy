using ArmoireAutoFill.Data.Shopping;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace ArmoireAutoFill.Data;

// Builds the "what can Knightshopper buy" catalog from the game's shop sheets.
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
public static class KnightshopperCatalogBuilder
{
    public sealed record CatalogSnapshot(
        IReadOnlyList<ShopEntry> Entries,
        IReadOnlyDictionary<uint, string> NpcNames,
        int SkippedUnlinkedShops,
        int SkippedGilSpecialShops)
    {
        public static readonly CatalogSnapshot Empty = new([], new Dictionary<uint, string>(), 0, 0);
    }

    public static bool IsLoaded { get; private set; }

    public static CatalogSnapshot Snapshot { get; private set; } = CatalogSnapshot.Empty;

    private static readonly object BuildLock = new();

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
    private static readonly Dictionary<uint, (byte CurrencyId, int SubCurrency)> SpecialShopCostItems = new()
    {
        [26807] = (0, -1), // Bicolor Gemstone
        [25] = (5, 0),     // Wolf Mark
        [27] = (3, 0),     // Allied Seal
        [10307] = (3, 1),  // Centurio Seal
        [26533] = (3, 2),  // Sack of Nuts
        [29] = (4, -1),    // MGP
    };

    public static void Build()
    {
        lock (BuildLock)
        {
            if (IsLoaded)
                return;

            try
            {
                Snapshot = BuildSnapshot();
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[ArmoireAutoFill] Knightshopper catalog build failed");
            }
        }
    }

    private static CatalogSnapshot BuildSnapshot()
    {
        var gilShopSheet = Svc.Data.GetExcelSheet<GilShop>();
        var specialShopSheet = Svc.Data.GetExcelSheet<SpecialShop>();
        var npcBaseSheet = Svc.Data.GetExcelSheet<ENpcBase>();
        var npcResidentSheet = Svc.Data.GetExcelSheet<ENpcResident>();
        if (gilShopSheet == null || specialShopSheet == null || npcBaseSheet == null || npcResidentSheet == null)
            return CatalogSnapshot.Empty;

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

        // --- SpecialShop: currency-cost entries ---
        foreach (var shop in specialShopSheet)
        {
            if (!shopToNpcs.ContainsKey(shop.RowId))
            {
                skippedUnlinked++;
                continue;
            }

            foreach (var itemEntry in shop.Item)
            {
                var receive = itemEntry.ReceiveItems.FirstOrDefault(r => r.Item.RowId != 0 && r.ReceiveCount != 0);
                if (receive.Item.RowId == 0)
                    continue;

                var cost = itemEntry.ItemCosts.FirstOrDefault(c => c.ItemCost.RowId != 0 || c.CostType == 2);
                if (cost.CostType == 2)
                {
                    // Special currency bucket (tomestones): ItemCost.RowId is the bucket id.
                    var bucket = (int)cost.ItemCost.RowId;
                    if (bucket is < 1 or > 3)
                        continue;
                    entries.Add(new ShopEntry(receive.Item.RowId, FirstNpc(shopToNpcs, shop.RowId),
                        shop.RowId, bucket - 1, 7, cost.CurrencyCost, itemEntry.Quest.RowId, ShopSource.SpecialShop));
                    continue;
                }

                var costItem = cost.ItemCost.RowId;
                if (costItem == 0)
                    continue;

                if (costItem == 1)
                {
                    // Gil-priced SpecialShop entry — excluded (see header comment).
                    skippedGilSpecial++;
                    continue;
                }

                if (!SpecialShopCostItems.TryGetValue(costItem, out var family))
                    continue; // not one of Knightshopper's currencies

                entries.Add(new ShopEntry(receive.Item.RowId, FirstNpc(shopToNpcs, shop.RowId),
                    shop.RowId, family.SubCurrency, family.CurrencyId, cost.CurrencyCost,
                    itemEntry.Quest.RowId, ShopSource.SpecialShop));
            }
        }

        // --- GilShop: vendor-price entries (sub-currency -1, no price column) ---
        var gilShopItemSheet = Svc.Data.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItemSheet != null)
        {
            foreach (var shop in gilShopItemSheet)
            {
                if (!shopToNpcs.ContainsKey(shop.RowId))
                {
                    skippedUnlinked++;
                    continue;
                }

                foreach (var subrow in shop)
                {
                    var item = subrow.Item;
                    if (item.RowId == 0)
                        continue;
                    var quest = subrow.QuestRequired.FirstOrDefault(q => q.RowId != 0);
                    entries.Add(new ShopEntry(item.RowId, FirstNpc(shopToNpcs, shop.RowId),
                        shop.RowId, -1, 2, null, quest.RowId, ShopSource.GilShop));
                }
            }
        }

        Svc.Log.Information(
            $"[ArmoireAutoFill] Knightshopper catalog: {entries.Count} entries, "
            + $"{shopToNpcs.Count} vendor-linked shops, {skippedUnlinked} shops skipped (no direct vendor link), "
            + $"{skippedGilSpecial} gil-priced SpecialShop entries excluded");

        return new CatalogSnapshot(entries, npcNames, skippedUnlinked, skippedGilSpecial);
    }

    private static uint FirstNpc(Dictionary<uint, List<uint>> shopToNpcs, uint shopRowId)
        => shopToNpcs.TryGetValue(shopRowId, out var list) && list.Count > 0 ? list[0] : 0;
}
