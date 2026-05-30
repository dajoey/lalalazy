using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyFateAutomation.Helpers.Enums;

/// <summary>
/// For <see cref="CommandFlag.RepairAllItems"/> and <seealso cref="CommandFlag.RepairAllItemsNPC"/>
/// </summary>
public enum RepairCategory : int {
    MainOffHand = 0,
    HeadBodyArms = 1,
    LegsFeet = 2,
    EarsNeck = 3,
    WristRing = 4,
    Inventory = 5,
}

public static class RepairCategoryExtensions {
    extension(RepairCategory category) {
        public unsafe bool Repair(bool isNpc = false) => RepairManager.Instance()->RepairAllItems(isNpc, (int)category, 0);
    }
}
