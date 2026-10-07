using ArmoireAutoFill.Data.Shopping;
using ArmoireAutoFill.Logic;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace ArmoireAutoFill.Data;

// Assembles the shopping-list builder input from live game state: every item the armoire
// can hold (Cabinet sheet), classified by the last inventory scan and the armoire cache.
public static class ArmoireShoppingInput
{
    public static bool TryBuild(InventoryScanner scanner, CabinetObserver cabinet,
        out ShoppingListBuilder.Input input)
    {
        input = null!;

        var cabinetSheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        var itemSheet = Svc.Data.GetExcelSheet<LuminaItem>();
        if (cabinetSheet == null || itemSheet == null)
            return false;
        if (scanner.LastScan == DateTime.MinValue)
            return false;

        var entries = new List<ShoppingListBuilder.ArmouryEntry>();
        var names = new Dictionary<uint, string>();

        foreach (var cabinetRow in cabinetSheet)
        {
            var itemRef = cabinetRow.Item;
            if (!itemRef.IsValid || itemRef.RowId == 0)
                continue;

            var ownership = ShoppingListBuilder.Ownership.NotOwned;
            if (cabinet.IsInArmoire(itemRef.RowId))
                ownership = ShoppingListBuilder.Ownership.InArmoire;
            else if (scanner.LastOwnedItemIds.Contains(itemRef.RowId))
                ownership = ShoppingListBuilder.Ownership.InInventory;

            entries.Add(new ShoppingListBuilder.ArmouryEntry(itemRef.RowId, ownership));
            if (!names.ContainsKey(itemRef.RowId))
                names[itemRef.RowId] = itemRef.Value.Name.ExtractText();
        }

        input = new ShoppingListBuilder.Input(entries, scanner.LastOwnedItemIds,
            KnightshopperCatalogBuilder.Snapshot.Entries, names);
        return true;
    }
}
