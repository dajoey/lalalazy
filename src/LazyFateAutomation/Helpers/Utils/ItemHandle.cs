using Dalamud.Game.Inventory;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Excel;
using FFXIVClientStructs.Interop;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;

namespace LazyFateAutomation.Helpers.Utils;

// https://github.com/Haselnussbomber/HaselCommon/blob/962b2ac2adaecd59d3fa541bb544bd4ae3a144e9/HaselCommon/Utils/ItemHandle.cs
public class ItemHandle {
    public ItemHandle(uint itemId) {
        ItemId = itemId;
        unsafe {
            ExcelRow = ItemUtil.IsEventItem(ItemId) ? Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(124, ItemId)
                : Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, ItemUtil.GetBaseId(ItemId).ItemId);
        }
    }

    public ItemHandle(ItemLocation itemLocation) {
        ItemLocation = itemLocation;
        unsafe {
            var inventoryItem = itemLocation.GetInventoryItem();
            ItemId = inventoryItem != null ? inventoryItem->GetItemId() : 0;
            ExcelRow = ItemUtil.IsEventItem(ItemId) ? Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(124, ItemId)
                : Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, ItemUtil.GetBaseId(ItemId).ItemId);
        }
    }

    public static unsafe implicit operator ItemHandle(InventoryItem* item) => new((item->Container, (ushort)item->Slot));
    public static unsafe implicit operator ItemHandle(Pointer<InventoryItem> item) => new((item.Value->Container, (ushort)item.Value->Slot));
    public static implicit operator ItemHandle(InventoryItem item) => new((item.Container, (ushort)item.Slot));
    public static implicit operator ItemHandle(GameInventoryItem item) => new(((InventoryType)item.ContainerType, (ushort)item.InventorySlot));
    public static implicit operator ItemHandle(Item item) => new(item.RowId);
    public static implicit operator ItemHandle(RowRef<Item> rowRef) => new(rowRef.RowId);
    public static implicit operator ItemHandle(EventItem eventItem) => new(eventItem.RowId);
    public static implicit operator ItemHandle(RowRef<EventItem> rowRef) => new(rowRef.RowId);
    public static implicit operator ItemHandle(ItemLocation itemLocation) => new(itemLocation);
    public static implicit operator ItemHandle(uint itemId) => new(itemId);
    public static implicit operator uint(ItemHandle itemInfo) => itemInfo.ItemId;

    public uint ItemId { get; }
    public ItemLocation? ItemLocation { get; set; }
    public unsafe ExcelRow* ExcelRow { get; }

    [MemberNotNullWhen(true, nameof(ItemLocation))]
    public unsafe bool TrySetItemLocation(InventoryItem.ItemFlags requiredFlag = InventoryItem.ItemFlags.None) {
        if (ItemLocation is not null) return true;
        foreach (var inv in InventoryType.FullInventory) {
            if (InventoryManager.Instance()->GetInventoryItems(inv).FirstOrDefault(i => i.Value != null && i.Value->ItemId == ItemId && (requiredFlag is InventoryItem.ItemFlags.None || i.Value->Flags.HasFlag(requiredFlag)))
                is { } item && item.Value != null) {
                ItemLocation = new ItemLocation(inv, item.Value->GetSlot());
                return true;
            }
        }
        return false;
    }

    public RowRef<Item> GameData => Svc.Data.GetRef<Item>(ItemUtil.GetBaseId(ItemId).ItemId);
    public bool IsValid => ItemId is not 0;

    public uint BaseItemId => ItemUtil.GetBaseId(ItemId).ItemId;
    public ItemKind ItemKind => ItemUtil.GetBaseId(ItemId).Kind;
    public bool IsNormalItem => ItemUtil.IsNormalItem(ItemId);
    public bool IsCollectible => ItemUtil.IsCollectible(ItemId);
    public bool IsHighQuality => ItemUtil.IsHighQuality(ItemId);
    public bool IsEventItem => ItemUtil.IsEventItem(ItemId);

    public bool HasItem => GetCount() > 0;
    public unsafe bool IsEquipped => InventoryManager.Instance()->GetInventoryItems(InventoryType.EquippedItems).Any(i => i.Value != null && i.Value->ItemId == ItemId);

    public unsafe bool InGearset {
        get {
            var gm = RaptureGearsetModule.Instance();
            for (byte i = 0; i < 100; ++i) {
                if (!gm->IsValidGearset(i)) continue;
                var gearset = gm->GetGearset(i);
                if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                    if (gearset->Items.ToArray().Any(x => ItemUtil.GetBaseId(x.ItemId).ItemId == ItemId)) return true;
            }
            return false;
        }
    }

    public unsafe int GetCount(bool ignoreHq = true)
        => ignoreHq ? InventoryManager.Instance()->GetInventoryItemCount(BaseItemId) + InventoryManager.Instance()->GetInventoryItemCount(ItemId, true)
        : InventoryManager.Instance()->GetInventoryItemCount(ItemId);

    public unsafe bool LowerItemQuality() {
        if (ItemLocation is null) return false;
        if (RaptureAtkModule.Instance()->AgentUpdateFlag.HasFlag(RaptureAtkModule.AgentUpdateFlags.InventoryUpdate)) return false;
        if (!Svc.Condition.CanLowerItemQuality()) return false;
        var item = InventoryManager.Instance()->GetInventorySlot(ItemLocation.Container, ItemLocation.Slot);
        if (!item->IsHighQuality()) return true;
        AgentInventoryContext.Instance()->LowerItemQuality(item, ItemLocation.Container, ItemLocation.Slot, 0);
        return true;
    }

    public unsafe bool CanEquip(out RowRef<LogMessage> errorMsg) {
        var logMessageId = InventoryManager.CanEquip(ItemId,
        PlayerState.Instance()->Race,
        PlayerState.Instance()->Sex,
        PlayerState.Instance()->GetClassJobLevel(-1, false), // -1 will do current job. Any other and the game has to fetch from exd
        PlayerState.Instance()->CurrentClassJobId,
        PlayerState.Instance()->GrandCompany,
        PvPProfile.Instance()->GetPvPRank(),
        ExcelRow);
        errorMsg = Svc.Data.GetRef<LogMessage>((uint)logMessageId);
        return logMessageId is 0 || logMessageId is 703 && GameData.Value.ClassJobCategory.Value.HasJobsAtLevel(GameData.Value.LevelEquip); // 703 = Cannot equip as current class. It shows erronously so we check ourselves
    }

    /// <summary>
    /// Be sure to check <see cref="CanEquip"/> first. This only handles the move operation
    /// </summary>
    /// TODO: ring slots
    public unsafe void Equip() {
        if (ItemLocation is null) return;
        Svc.Log.Debug($"Equipping item [{this}] from {ItemLocation} to {new ItemLocation(InventoryType.EquippedItems, (ushort)GameData.Value.EquipSlot)}");
        InventoryManager.Instance()->MoveItemSlot(ItemLocation.Container, ItemLocation.Slot, InventoryType.EquippedItems, (ushort)GameData.Value.EquipSlot, true);
        // This seems to not work depending on the item. Conditionally uses ODR location? Can't tell, don't care that much
        //if (ItemLocation.Container.GetContainerId() is not 0 and var srcContId && InventoryType.EquippedItems.GetContainerId() is not 0 and var destContId) {
        //    var eis = stackalloc AtkValue[4];
        //    var dropOut = stackalloc AtkValue[32];
        //    eis[0].SetUInt(srcContId);
        //    eis[1].SetUInt(ItemLocation.Slot);
        //    eis[2].SetUInt(destContId);
        //    eis[3].SetUInt(GameData.Value.EquipSlot);
        //    Svc.Log.Debug($"Equipping item [{this}] from {ItemLocation}/{ItemLocation.GetODR()} to {(InventoryType.EquippedItems, GameData.Value.EquipSlot)}");
        //    RaptureAtkModule.Instance()->HandleItemMove(dropOut, eis, 4);
        //}
    }

    public unsafe void MoveTo(InventoryType[] containers) {
        foreach (var cont in containers) {
            if (InventoryManager.Instance()->GetFirstEmptySlot(cont) is { } slot) {
                MoveTo(cont, (ushort)slot);
            }
        }
    }

    private unsafe void MoveTo(InventoryType cont) {
        if (InventoryManager.Instance()->GetFirstEmptySlot(cont) is { } slot) {
            MoveTo(cont, (ushort)slot);
        }
    }

    private unsafe void MoveTo(InventoryType cont, ushort slot) {
        if (ItemLocation is null) return;
        InventoryManager.Instance()->MoveItemSlot(ItemLocation.Container, ItemLocation.Slot, cont, slot, true);
    }

    public override string ToString() => IsValid ? $"[#{ItemId}] {GameData.Value.Name}" : $"{nameof(ItemHandle)}#Invalid";
}
