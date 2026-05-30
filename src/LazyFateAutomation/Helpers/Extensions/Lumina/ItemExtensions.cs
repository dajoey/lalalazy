using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class ItemExtensions {
    extension(Item item) {
        public InventoryType ArmouryContainer => item.EquipSlotCategory.Value switch {
            { MainHand: 1 } => InventoryType.ArmoryMainHand,
            { OffHand: 1 } => InventoryType.ArmoryOffHand,
            { Head: 1 } => InventoryType.ArmoryHead,
            { Body: 1 } => InventoryType.ArmoryBody,
            { Gloves: 1 } => InventoryType.ArmoryHands,
            { Legs: 1 } => InventoryType.ArmoryLegs,
            { Feet: 1 } => InventoryType.ArmoryFeets,
            { Ears: 1 } => InventoryType.ArmoryEar,
            { Neck: 1 } => InventoryType.ArmoryNeck,
            { Wrists: 1 } => InventoryType.ArmoryWrist,
            { FingerL: 1 } => InventoryType.ArmoryRings,
            { FingerR: 1 } => InventoryType.ArmoryRings,
            { SoulCrystal: 1 } => InventoryType.ArmorySoulCrystal,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, null)
        };

        /// <summary>
        /// The slot index for <see cref="InventoryType.EquippedItems"/>
        /// </summary>
        public uint EquipSlot => item.EquipSlotCategory.Value switch {
            { MainHand: 1 } => 0,
            { OffHand: 1 } => 1,
            { Head: 1 } => 2,
            { Body: 1 } => 3,
            { Gloves: 1 } => 4,
            { Waist: 1 } => 5,
            { Legs: 1 } => 6,
            { Feet: 1 } => 7,
            { Ears: 1 } => 8,
            { Neck: 1 } => 9,
            { Wrists: 1 } => 10,
            { FingerL: 1 } => 11,
            { FingerR: 1 } => 12,
            { SoulCrystal: 1 } => 13,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, null)
        };

        public bool IsMoochable => item.ItemUICategory.RowId is 47 && Svc.Data.FindRow<FishingBaitParameter>(r => r.Item.RowId == item.RowId) is { };
        public bool IsGearCoffer => item.Icon is 26509 or 26557 or 26558 or 26559 or 26560 or 26561 or 26562 or 26564 or 26565 or 26566 or 26567;
        public bool IsAttire => item.ItemUICategory.RowId is 112;

        public RowRef<MirageStoreSetItem> Mirage => MirageStoreSetItem.GetRowRef(item.RowId);
        public ItemHandle Handle => (ItemHandle)item;
    }
}
