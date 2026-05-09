using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace AutoPotion;

internal class Potion
{
    public uint Id { get; }
    public string Name { get; }
    public uint IconId { get; }
    private readonly float _percent;
    private readonly uint _hpCap;

    public Potion(LuminaItem item)
    {
        Id = item.RowId;
        Name = item.Name.ExtractText();
        IconId = item.Icon;
        var action = item.ItemAction.Value;
        var data = action.DataHQ;
        _percent = data[0] / 100f;
        _hpCap = data[1];
    }

    public uint MaxHealFor(uint maxHp) => Math.Min((uint)(maxHp * _percent), _hpCap);

    public unsafe int InventoryCount(bool hq)
    {
        var inv = InventoryManager.Instance();
        return inv == null ? 0 : inv->GetInventoryItemCount(Id, hq);
    }

    public unsafe bool IsOnCooldown()
    {
        var am = ActionManager.Instance();
        return am != null && am->IsRecastTimerActive(ActionType.Item, Id);
    }

    public unsafe uint GetActionStatus(ulong targetId)
    {
        var am = ActionManager.Instance();
        if (am == null) return uint.MaxValue;
        var hqCount = InventoryCount(true);
        var nqCount = InventoryCount(false);
        if (hqCount == 0 && nqCount == 0) return 583; // No items.
        var useId = hqCount > 0 ? Id + 1_000_000u : Id;
        return am->GetActionStatus(ActionType.Item, useId, targetId);
    }

    public unsafe bool TryUse(ulong targetId)
    {
        var am = ActionManager.Instance();
        if (am == null) return false;
        var hqCount = InventoryCount(true);
        var nqCount = InventoryCount(false);
        if (hqCount == 0 && nqCount == 0) return false;
        var useId = hqCount > 0 ? Id + 1_000_000u : Id;
        return am->UseAction(ActionType.Item, useId, targetId, 65535);
    }
}
