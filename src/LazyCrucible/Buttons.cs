using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyCrucible;

/// <summary>
///     Plain-button clicks for screens whose buttons do not go through an addon callback ("Take all", the treasure
///     choices): the button component's own ButtonClick event is handed to the addon, as a mouse click would. Only
///     callers that passed <see cref="ActuationGuard"/> use this.
/// </summary>
internal static unsafe class Buttons
{
    /// <summary> Click the button component with node id <paramref name="nodeId"/>. False when it or its click event is missing. </summary>
    public static bool ClickNode(AtkUnitBase* addon, uint nodeId)
    {
        var node = addon->UldManager.SearchNodeById(nodeId);
        if (node is null || !node->IsVisible())
            return false;
        var component = node->GetAsAtkComponentNode();
        if (component is null)
            return false;
        var evt = FirstClick(component->AtkResNode.AtkEventManager.Event, -1);
        return evt is not null && Send(addon, evt);
    }

    /// <summary> Click the visible button whose ButtonClick event carries <paramref name="param"/> (searched two levels deep). </summary>
    public static bool ClickByParam(AtkUnitBase* addon, int param)
    {
        var evt = FindByParam(&addon->UldManager, param, 0);
        return evt is not null && Send(addon, evt);
    }

    private static AtkEvent* FirstClick(AtkEvent* evt, int param)
    {
        for (; evt is not null; evt = evt->NextEvent)
            if (evt->State.EventType == AtkEventType.ButtonClick && (param < 0 || evt->Param == param))
                return evt;
        return null;
    }

    private static AtkEvent* FindByParam(AtkUldManager* uld, int param, int depth)
    {
        if (depth > 2)
            return null;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node is null || !node->IsVisible())
                continue;
            var found = FirstClick(node->AtkEventManager.Event, param);
            if (found is not null)
                return found;
            var component = node->GetAsAtkComponentNode();
            if (component is null || component->Component is null)
                continue;
            found = FindByParam(&component->Component->UldManager, param, depth + 1);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static bool Send(AtkUnitBase* addon, AtkEvent* evt)
    {
        var data = new AtkEventData();
        OwnCalls.Depth++;
        try
        {
            addon->ReceiveEvent(AtkEventType.ButtonClick, (int)evt->Param, evt, &data);
        }
        finally
        {
            OwnCalls.Depth--;
        }
        return true;
    }
}
