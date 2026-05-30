using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyFateAutomation.Helpers.Utils;

public class ItemLocation {
    public InventoryType Container { get; set; } = InventoryType.Invalid;
    public ushort Slot { get; set; } = 0;

    public ItemLocation() { }

    public ItemLocation(InventoryType container, ushort slot) {
        Container = container;
        Slot = slot;
    }

    public unsafe ItemLocation(InventoryItem* item) => SetInventoryItem(item);

    public void Clear() {
        Container = InventoryType.Invalid;
        Slot = 0;
    }

    public void SetContainerAndSlot(InventoryType container, ushort slot) {
        Clear();
        Container = container;
        Slot = slot;
    }

    public unsafe void SetInventoryItem(InventoryItem* item) {
        if (item == null)
            Clear();
        else {
            Container = item->GetInventoryType();
            Slot = item->GetSlot();
        }
    }

    // https://github.com/Zeffuro/AetherBags/blob/master/AetherBags/Extensions/InventoryTypeExtensions.cs
    public unsafe ItemLocation GetODR() {
        var sorter = Container.GetSorter();
        if (sorter == null)
            return new ItemLocation(Container, Slot);

        var startIndex = Container.InventoryStartIndex;
        var sorterIndex = startIndex + Slot;

        if (sorterIndex < 0 || sorterIndex >= sorter->Items.LongCount)
            return new ItemLocation(Container, Slot);

        var entry = sorter->Items[sorterIndex].Value;
        if (entry == null)
            return new ItemLocation(Container, Slot);

        var baseType = Container switch {
            _ when Container.IsMainInventory => InventoryType.Inventory1,
            _ when Container.IsSaddleBag => Container is InventoryType.SaddleBag1 or InventoryType.SaddleBag2
                ? InventoryType.SaddleBag1
                : InventoryType.PremiumSaddleBag1,
            _ when Container.IsRetainer => InventoryType.RetainerPage1,
            _ => Container,
        };

        return new ItemLocation(baseType + entry->Page, entry->Slot);
    }

    public unsafe InventoryItem* GetInventoryItem() {
        if (Container == InventoryType.Invalid)
            return null;

        return InventoryManager.Instance()->GetInventorySlot(Container, Slot);
    }

    public unsafe bool IsEmpty {
        get {
            if (Container != InventoryType.Invalid)
                return false;

            var inventoryItem = GetInventoryItem();
            return inventoryItem == null || inventoryItem->IsEmpty();
        }
    }

    public override string ToString() => $"[c={Container} s={Slot}]";

    public ValueTuple<InventoryType, ushort> AsTuple() => (Container, Slot);
    public static unsafe implicit operator ItemLocation(InventoryItem* item) => new(item);
    public static implicit operator ItemLocation((InventoryType Container, ushort Slot) tuple) => new(tuple.Container, tuple.Slot);
    public static implicit operator ValueTuple<InventoryType, ushort>(ItemLocation itemLoc) => itemLoc.AsTuple();
}
