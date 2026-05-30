using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Linq;

namespace clib.Extensions;

public static class InventoryTypeExtensions {
    public static InventoryType[] Bags => [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.KeyItems
    ];

    public static InventoryType[] Weapons => [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand
    ];

    public static InventoryType[] LeftSideArmory => [
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets
    ];

    public static InventoryType[] RightSideArmory => [
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings
    ];

    public static InventoryType[] SaddleBag => [
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2
    ];

    public static InventoryType[] Retainer => [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7
    ];

    public static InventoryType[] Armoury => [.. Weapons, .. LeftSideArmory, .. RightSideArmory, InventoryType.ArmorySoulCrystal, InventoryType.EquippedItems];
    public static InventoryType[] FullInventory => [.. Bags, .. Armoury];
    public static InventoryType[] AllPlayer => [.. Bags, .. Armoury, .. SaddleBag];

    public static unsafe int InventoryStartIndex(this InventoryType inventoryType) => inventoryType switch {
        InventoryType.Inventory2 => inventoryType.GetSorter()->ItemsPerPage,
        InventoryType.Inventory3 => inventoryType.GetSorter()->ItemsPerPage * 2,
        InventoryType.Inventory4 => inventoryType.GetSorter()->ItemsPerPage * 3,
        InventoryType.SaddleBag2 => inventoryType.GetSorter()->ItemsPerPage,
        InventoryType.PremiumSaddleBag2 => inventoryType.GetSorter()->ItemsPerPage,
        InventoryType.RetainerPage2 => inventoryType.GetSorter()->ItemsPerPage,
        InventoryType.RetainerPage3 => inventoryType.GetSorter()->ItemsPerPage * 2,
        InventoryType.RetainerPage4 => inventoryType.GetSorter()->ItemsPerPage * 3,
        InventoryType.RetainerPage5 => inventoryType.GetSorter()->ItemsPerPage * 4,
        InventoryType.RetainerPage6 => inventoryType.GetSorter()->ItemsPerPage * 5,
        InventoryType.RetainerPage7 => inventoryType.GetSorter()->ItemsPerPage * 6,
        _ => 0,
    };

    public static bool IsMainInventory(this InventoryType inventoryType) => inventoryType is InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or InventoryType.Inventory4;
    public static bool IsSaddleBag(this InventoryType inventoryType) => SaddleBag.Contains(inventoryType);
    public static bool IsRetainer(this InventoryType inventoryType) => Retainer.Contains(inventoryType);

    public static unsafe ItemHandle[] Items(this InventoryType inventoryType) => InventoryManager.Instance()->GetItems(inventoryType);

    public static unsafe InventoryContainer* GetContainer(this InventoryType inv) => InventoryManager.Instance()->GetInventoryContainer(inv);
    public static unsafe ItemOrderModuleSorter* GetSorter(this InventoryType inv) {
        var m = ItemOrderModule.Instance();
        var sorter = inv switch {
            InventoryType.ArmoryMainHand => m->ArmouryMainHandSorter,
            InventoryType.ArmoryHead => m->ArmouryHeadSorter,
            InventoryType.ArmoryBody => m->ArmouryBodySorter,
            InventoryType.ArmoryHands => m->ArmouryHandsSorter,
            InventoryType.ArmoryLegs => m->ArmouryLegsSorter,
            InventoryType.ArmoryFeets => m->ArmouryFeetSorter,
            InventoryType.ArmoryOffHand => m->ArmouryOffHandSorter,
            InventoryType.ArmoryEar => m->ArmouryEarsSorter,
            InventoryType.ArmoryNeck => m->ArmouryNeckSorter,
            InventoryType.ArmoryWrist => m->ArmouryWristsSorter,
            InventoryType.ArmoryRings => m->ArmouryRingsSorter,
            InventoryType.ArmorySoulCrystal => m->ArmourySoulCrystalSorter,
            InventoryType.SaddleBag1 or InventoryType.SaddleBag2 => m->SaddleBagSorter,
            InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2 => m->PremiumSaddleBagSorter,
            InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or InventoryType.Inventory4 => m->InventorySorter,
            _ => null
        };
        return sorter;
    }

    public static uint GetContainerId(this InventoryType inventoryType) => inventoryType switch {
        InventoryType.EquippedItems => 4,
        InventoryType.KeyItems => 7,
        InventoryType.Inventory1 => 48,
        InventoryType.Inventory2 => 49,
        InventoryType.Inventory3 => 50,
        InventoryType.Inventory4 => 51,
        InventoryType.RetainerPage1 => 52,
        InventoryType.RetainerPage2 => 53,
        InventoryType.RetainerPage3 => 54,
        InventoryType.RetainerPage4 => 55,
        InventoryType.RetainerPage5 => 56,
        InventoryType.ArmoryMainHand => 57,
        InventoryType.ArmoryHead => 58,
        InventoryType.ArmoryBody => 59,
        InventoryType.ArmoryHands => 60,
        InventoryType.ArmoryLegs => 61,
        InventoryType.ArmoryFeets => 62,
        InventoryType.ArmoryOffHand => 63,
        InventoryType.ArmoryEar => 64,
        InventoryType.ArmoryNeck => 65,
        InventoryType.ArmoryWrist => 66,
        InventoryType.ArmoryRings => 67,
        InventoryType.ArmorySoulCrystal => 68,
        InventoryType.SaddleBag1 => 69,
        InventoryType.SaddleBag2 => 70,
        InventoryType.PremiumSaddleBag1 => 71,
        InventoryType.PremiumSaddleBag2 => 72,
        _ => 0
    };
}
