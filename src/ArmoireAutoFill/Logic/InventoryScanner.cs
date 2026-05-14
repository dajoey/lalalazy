using ArmoireAutoFill.Data;
using ArmoireAutoFill.Models;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ArmoireAutoFill.Logic;

public class InventoryScanner
{
    public DateTime LastScan { get; private set; } = DateTime.MinValue;
    public int LastInventoryItemsSeen { get; private set; }
    public int LastInventoryHits { get; private set; }
    public int LastArmoireHits { get; private set; }

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

    private readonly CabinetObserver _cabinetObserver;

    public InventoryScanner(CabinetObserver cabinetObserver)
    {
        _cabinetObserver = cabinetObserver;
    }

    public void Scan()
    {
        var ownedItemIds = new HashSet<uint>();

        unsafe
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
            {
                Svc.Log.Warning("[ArmoireAutoFill] InventoryManager null; skipping scan");
                return;
            }

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

        // ArmoireItems are duplicated when one item drops in multiple dungeons —
        // count uniques for the headline diagnostic numbers.
        var seenInInv = new HashSet<uint>();
        var seenInArm = new HashSet<uint>();
        foreach (var item in ArmoireGearDatabase.AllItems)
        {
            if (ownedItemIds.Contains(item.ItemId))
            {
                item.Owned = OwnershipStatus.InInventory;
                seenInInv.Add(item.ItemId);
            }
            else if (_cabinetObserver.IsInArmoire(item.ItemId))
            {
                item.Owned = OwnershipStatus.InArmoire;
                seenInArm.Add(item.ItemId);
            }
            else
            {
                item.Owned = OwnershipStatus.NotOwned;
            }
        }

        LastScan = DateTime.UtcNow;
        LastInventoryItemsSeen = ownedItemIds.Count;
        LastInventoryHits = seenInInv.Count;
        LastArmoireHits = seenInArm.Count;
        Svc.Log.Information(
            $"[ArmoireAutoFill] scan complete: inventory={LastInventoryItemsSeen} items, "
            + $"armoire-eligible breakdown: {seenInInv.Count} in inventory + {seenInArm.Count} in armoire + "
            + $"{ArmoireGearDatabase.MissingCount} missing");
    }
}
