using ArmoireAutoFill.Data;
using ArmoireAutoFill.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ArmoireAutoFill.Logic;

public class InventoryScanner
{
    private static readonly InventoryType[] _containers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.ArmorySoulCrystal,
    ];

    public void Scan()
    {
        var ownedItemIds = new HashSet<uint>();

        unsafe
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return;

            foreach (var containerType in _containers)
            {
                var container = inventoryManager->GetInventoryContainer(containerType);
                if (container == null)
                    continue;

                for (int i = 0; i < container->Size; i++)
                {
                    var slot = container->GetInventorySlot(i);
                    if (slot == null)
                        continue;

                    var itemId = slot->ItemId;
                    if (itemId > 0)
                        ownedItemIds.Add(itemId);
                }
            }
        }

        foreach (var item in ArmoireGearDatabase.AllItems)
        {
            item.Owned = ownedItemIds.Contains(item.ItemId)
                ? OwnershipStatus.InInventory
                : OwnershipStatus.NotOwned;
        }
    }
}
