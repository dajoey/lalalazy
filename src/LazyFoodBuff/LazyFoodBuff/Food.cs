using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace LazyFoodBuff;

internal class Food
{
    public uint Id { get; }
    public string Name { get; }
    public uint IconId { get; }
    public uint ItemFoodRowId { get; }

    // Stat bonuses read from ItemFood sheet.
    // Up to 3 entries; key = BaseParam RowId, value = (nqPercent, nqCap, hqPercent, hqCap).
    public readonly Dictionary<uint, (uint NqPercent, uint NqCap, uint HqPercentBonus, uint HqCapBonus)> Stats = new();

    public Food(Lumina.Excel.Sheets.Item item)
    {
        Id = item.RowId;
        Name = item.Name.ExtractText();
        IconId = item.Icon;

        // Resolve the ItemFood row via the ItemAction sheet.
        // Data[0] = type (always 844 for food), Data[1] = ItemFood row ID.
        var action = item.ItemAction.Value;
        if (action.RowId == 0 || action.Data.Count < 2) return;
        ItemFoodRowId = action.Data[1];
        if (ItemFoodRowId == 0) return;

        var foodSheet = Plugin.Data.GetExcelSheet<ItemFood>();
        if (foodSheet == null || !foodSheet.TryGetRow(ItemFoodRowId, out var foodData)) return;

        // ItemFood.Params is a collection of sub-objects, each containing:
        //   BaseParam (RowRef), IsRelative (bool), Value, Max, ValueHQ, MaxHQ.
        // Value/ValueHQ are percentage bonuses (e.g., 12 = 12%).
        // Max/MaxHQ are flat caps (e.g., 39 = max 39 points).
        // ValueHQ/MaxHQ are ADDITIONAL amounts on top of the NQ value.
        for (int i = 0; i < foodData.Params.Count; i++)
        {
            var entry = foodData.Params[i];
            var paramId = entry.BaseParam.RowId;
            if (paramId == 0) continue;

            var nqPct = (uint)entry.Value;
            var nqCap = (uint)entry.Max;
            var hqPct = (uint)entry.ValueHQ;
            var hqCap = (uint)entry.MaxHQ;

            Stats[paramId] = (nqPct, nqCap, hqPct, hqCap);
        }
    }

    /// <summary>
    /// Get the percentage bonus for a stat. Returns the NQ or HQ total percentage.
    /// HQ total = NQ percent + HQ additional percent.
    /// </summary>
    public uint GetStatPercent(uint baseParamId, bool hq)
    {
        if (!Stats.TryGetValue(baseParamId, out var s)) return 0;
        return hq ? s.NqPercent + s.HqPercentBonus : s.NqPercent;
    }

    /// <summary>
    /// Get the flat cap for a stat. Returns the NQ or HQ total cap.
    /// HQ total = NQ cap + HQ additional cap.
    /// </summary>
    public uint GetStatCap(uint baseParamId, bool hq)
    {
        if (!Stats.TryGetValue(baseParamId, out var s)) return 0;
        return hq ? s.NqCap + s.HqCapBonus : s.NqCap;
    }

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
