using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.Interop;
using System.Collections.Generic;
using System.Linq;

namespace clib.Extensions;

public static unsafe class InventoryManagerExtensions {
    public static int GetEmptySlots(this ref InventoryManager instance, params InventoryType[] inventories) {
        var am = InventoryManager.Instance();
        if (inventories.Length == 0)
            return (int)am->GetEmptySlotsInBag();
        return inventories.ToList().Sum(i => am->GetInventoryItems(i).Count(item => item.Value->ItemId == 0));
    }

    public static List<ItemHandle> GetHqItems(this ref InventoryManager instance, params InventoryType[] inventories) {
        var am = InventoryManager.Instance();
        if (inventories.Length == 0) {
            return [.. InventoryTypeExtensions.FullInventory.SelectMany(inv => am->GetInventoryItems(inv))
                .Where(item => item.Value->ItemId != 0 && item.Value->Flags == InventoryItem.ItemFlags.HighQuality)
                .Select(item => (ItemHandle)item)];
        }
        return [.. inventories.SelectMany(inv => am->GetInventoryItems(inv))
            .Where(item => item.Value->ItemId != 0 && item.Value->Flags == InventoryItem.ItemFlags.HighQuality)
            .Select(item => (ItemHandle)item)];
    }

    public static Pointer<InventoryItem>[] GetInventoryItems(this ref InventoryManager instance, InventoryType container) {
        var inv = instance.GetInventoryContainer(container);
        if (inv == null) return [];
        var items = new Pointer<InventoryItem>[inv->Size];
        for (var i = 0; i < inv->Size; i++)
            items[i] = inv->GetInventorySlot(i);
        return items;
    }

    public static ItemHandle[] GetItems(this ref InventoryManager instance, InventoryType container) {
        var inv = instance.GetInventoryContainer(container);
        if (inv == null) return [];
        var items = new ItemHandle[inv->Size];
        for (var i = 0; i < inv->Size; i++)
            items[i] = inv->GetInventorySlot(i);
        return items;
    }

    public static int? GetFirstEmptySlot(this ref InventoryManager instance, InventoryType container) {
        var inv = instance.GetInventoryContainer(container);
        if (inv == null) return null;
        for (var i = 0; i < inv->Size; i++) {
            if (inv->GetInventorySlot(i) is var item && (item == null || item->IsEmpty()))
                return i;
        }
        return null;
    }
}
