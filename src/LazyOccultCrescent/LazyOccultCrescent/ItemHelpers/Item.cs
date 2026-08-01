using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace LazyOccultCrescent.ItemHelpers;

public unsafe class Item(uint id)
{
    public int Count()
    {
        try
        {
            return InventoryManager.Instance()->GetInventoryItemCount(id);
        }
        catch
        {
            return 0;
        }
    }

    public void Use()
    {
        try
        {
            AgentInventoryContext.Instance()->UseItem(id);
        }
        catch
        {
            // ignored
        }
    }
}
