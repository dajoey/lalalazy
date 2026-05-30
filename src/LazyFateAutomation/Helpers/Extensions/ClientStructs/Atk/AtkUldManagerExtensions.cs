using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AtkUldManagerExtensions {
    public static T* SearchNodeById<T>(this AtkUldManager atkUldManager, uint nodeId) where T : unmanaged {
        foreach (var node in atkUldManager.Nodes) {
            if (node.Value is not null) {
                if (node.Value->NodeId == nodeId)
                    return (T*)node.Value;
            }
        }

        return null;
    }
}
