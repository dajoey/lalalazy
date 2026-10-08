using ArmoireAutoFill.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace ArmoireAutoFill.CatalogHarness;

// Port of Knightshopper 1.0.1.6's import acceptance rules over RAW sqpack rows, for the
// harness. Source: the decompiled plugin (HmcZxNdvtzR1P.cs):
//   * Aalhs91G9ayZRNV(currency, item) — the share item must be in Knightshopper's
//     per-currency catalog AND (VendorId, ShopId[, SubCurrency]) must match one of that
//     item's catalog entries; Gil/Bicolor/MGP/Cosmocredits compare vendor+shop,
//     Hunt/Tomestone/Scrip/Firmament/PVP/OccultCrescent also compare sub-currency;
//     unknown currencies are rejected (`_ => false`).
// The catalog side cannot be dumped without the running game (the native discovery needs
// the game process), so this port models Knightshopper's catalog from the sqpack the way
// its native discovery does (ks-native.dll embeds lumina and reads the Level sheet —
// "LevelPos", ENpcBase/ENpcData, TerritoryType — for vendor positions): a shop exists in
// the modelled catalog when a vendor NPC references the shop row directly via ENpcData
// AND that NPC has a Level-sheet placement (Type 8 = ENpcBase). Evidence anchors from
// live data: Bango Zango 1001787 (placed) — Knightshopper accepts 4868@1001787/262158;
// House Valentione maid 1005608 / recompense officer 1017613 (unplaced) — Knightshopper
// rejects item 4303 from their shops.
// Deliberately independent of KnightshopperCatalogCore so a catalog test cannot pass by
// construction: this file re-reads the raw sheets.
public static class KnightshopperAcceptance
{
    // Mapped SpecialShop cost families, mirroring Knightshopper's per-currency handling
    // and Armoire's emission convention (KnightshopperCatalogCore.SpecialShopCostItems).
    private static readonly Dictionary<uint, (byte CurrencyId, int SubCurrency)> CostItems = new()
    {
        [26807] = (0, -1), // Bicolor Gemstone
        [25] = (5, 0),     // Wolf Mark (PVP)
        [27] = (3, 0),     // Allied Seal (Hunt)
        [10307] = (3, 1),  // Centurio Seal (Hunt)
        [26533] = (3, 2),  // Sack of Nuts (Hunt)
        [29] = (4, -1),    // MGP
    };

    private readonly record struct Sheets(
        ExcelSheet<GilShop> GilShop,
        SubrowExcelSheet<GilShopItem> GilShopItem,
        ExcelSheet<SpecialShop> SpecialShop,
        ExcelSheet<ENpcBase> NpcBase,
        HashSet<uint> PlacedNpcs,
        Dictionary<uint, List<uint>> ShopLinks);

    public static HashSet<uint> LevelPlacedNpcs(ExcelSheet<Level> levelSheet)
    {
        var placed = new HashSet<uint>();
        foreach (var row in levelSheet)
            if (row.Type == 8)
            {
                var obj = row.Object.RowId;
                if (obj != 0)
                    placed.Add(obj);
            }
        return placed;
    }

    // Returns null when the quadruple would be accepted, otherwise the rejection reason
    // (mirrors "Item {id} is not available from its shared {currency} vendor.").
    public static string? Validate(
        ExcelSheet<GilShop> gilShopSheet,
        SubrowExcelSheet<GilShopItem> gilShopItemSheet,
        ExcelSheet<SpecialShop> specialShopSheet,
        ExcelSheet<ENpcBase> npcBaseSheet,
        ExcelSheet<Level> levelSheet,
        uint itemId, uint vendorId, uint shopId, int subCurrency, byte currencyId)
    {
        var placed = LevelPlacedNpcs(levelSheet);
        var links = DirectShopLinks(npcBaseSheet, gilShopSheet, specialShopSheet);
        return Validate(new Sheets(gilShopSheet, gilShopItemSheet, specialShopSheet, npcBaseSheet, placed, links),
            itemId, vendorId, shopId, subCurrency, currencyId);
    }

    private static string? Validate(Sheets s, uint itemId, uint vendorId, uint shopId, int subCurrency, byte currencyId)
    {
        // Vendor must be a direct, Level-placed link of the shop (catalog membership).
        if (!s.ShopLinks.TryGetValue(shopId, out var links) || !links.Contains(vendorId))
            return $"item {itemId}: shop {shopId} has no direct link to vendor {vendorId} (not in the modelled catalog)";
        if (!s.PlacedNpcs.Contains(vendorId))
            return $"item {itemId}: vendor {vendorId} has no Level placement (event-spawned; not in the modelled catalog)";

        switch (currencyId)
        {
            case 2: // Gil
                if (subCurrency != -1)
                    return $"item {itemId}: Gil sub-currency {subCurrency} != -1";
                if (!s.GilShop.TryGetRow(shopId, out _))
                    return $"item {itemId}: shop {shopId} is not a GilShop row";
                if (!GilShopItemContains(s.GilShopItem, shopId, itemId))
                    return $"item {itemId}: not available from its shared Gil vendor (shop {shopId} has no such listing)";
                return null;

            case 7: // Tomestone: CostType 2 special bucket, sub-currency = bucket - 1
                var bucket = subCurrency + 1;
                if (bucket is < 1 or > 3)
                    return $"item {itemId}: tomestone bucket {bucket} out of range";
                if (!SpecialShopReceives(s, shopId, itemId, costType: 2, costItem: (uint)bucket))
                    return $"item {itemId}: not available from its shared Tomestone vendor (shop {shopId})";
                return null;

            case 0: // Bicolor Gemstone
            case 4: // MGP
                if (subCurrency != -1)
                    return $"item {itemId}: sub-currency {subCurrency} != -1 for currency {currencyId}";
                if (!SpecialShopReceivesWithCostItem(s, shopId, itemId, currencyId))
                    return $"item {itemId}: not available from its shared vendor (shop {shopId}, currency {currencyId})";
                return null;

            case 3: // Hunt
            case 5: // PVP
                if (!SpecialShopReceivesWithCostItem(s, shopId, itemId, currencyId, requireSubMatch: true, subCurrency))
                    return $"item {itemId}: not available from its shared vendor (shop {shopId}, currency {currencyId})";
                return null;

            default:
                return $"item {itemId}: currency {currencyId} has no Knightshopper availability case";
        }
    }

    private static Dictionary<uint, List<uint>> DirectShopLinks(
        ExcelSheet<ENpcBase> npcBaseSheet, ExcelSheet<GilShop> gilShopSheet, ExcelSheet<SpecialShop> specialShopSheet)
    {
        var links = new Dictionary<uint, List<uint>>();
        foreach (var npc in npcBaseSheet)
            foreach (var @ref in npc.ENpcData)
            {
                var rowId = @ref.RowId;
                if (rowId == 0) continue;
                var isShop = (rowId >= 262144 && gilShopSheet.TryGetRow(rowId, out _))
                             || specialShopSheet.TryGetRow(rowId, out _);
                if (!isShop) continue;
                if (!links.TryGetValue(rowId, out var list))
                    links[rowId] = list = [];
                list.Add(npc.RowId);
            }
        return links;
    }

    private static bool GilShopItemContains(SubrowExcelSheet<GilShopItem> sheet, uint shopId, uint itemId)
    {
        if (!sheet.TryGetRow(shopId, out var column))
            return false;
        foreach (var sub in column)
            if (sub.Item.RowId == itemId)
                return true;
        return false;
    }

    private static bool SpecialShopReceives(
        Sheets s, uint shopId, uint itemId, byte costType, uint costItem)
    {
        if (!s.SpecialShop.TryGetRow(shopId, out var shop))
            return false;
        foreach (var entry in shop.Item)
        {
            var receive = FirstReceiveItem(entry);
            if (receive == 0 || receive != itemId)
                continue;
            var (actualType, actualItem, _) = FirstCost(entry);
            if (actualType == costType && actualItem == costItem)
                return true;
        }
        return false;
    }

    private static bool SpecialShopReceivesWithCostItem(
        Sheets s, uint shopId, uint itemId, byte currencyId, bool requireSubMatch = false, int subCurrency = 0)
    {
        if (!s.SpecialShop.TryGetRow(shopId, out var shop))
            return false;
        foreach (var entry in shop.Item)
        {
            var receive = FirstReceiveItem(entry);
            if (receive == 0 || receive != itemId)
                continue;
            var (costType, costItemId, _) = FirstCost(entry);
            if (costType != 0 || costItemId == 0)
                continue;
            if (!CostItems.TryGetValue(costItemId, out var family) || family.CurrencyId != currencyId)
                continue;
            if (requireSubMatch && family.SubCurrency != subCurrency)
                continue;
            return true;
        }
        return false;
    }

    private static uint FirstReceiveItem(SpecialShop.ItemStruct entry)
    {
        foreach (var receive in entry.ReceiveItems)
            if (receive.Item.RowId != 0 && receive.ReceiveCount != 0)
                return receive.Item.RowId;
        return 0;
    }

    private static (byte CostType, uint CostItem, uint CostCount) FirstCost(SpecialShop.ItemStruct entry)
    {
        foreach (var cost in entry.ItemCosts)
            if (cost.ItemCost.RowId != 0 || cost.CostType == 2)
                return (cost.CostType, cost.ItemCost.RowId, cost.CurrencyCost);
        return (0, 0, 0);
    }
}
