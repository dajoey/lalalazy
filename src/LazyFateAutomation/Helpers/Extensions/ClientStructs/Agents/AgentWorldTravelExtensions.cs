using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class AgentWorldTravelExtensions {
    public static void Travel(ref this AgentWorldTravel instance, string world) {
        if (ushort.TryParse(world, out var id))
            Travel(ref instance, id);
        else if (Svc.Data.FindRow<World>(r => r.Name.ToString().Contains(world, StringComparison.OrdinalIgnoreCase)) is { RowId: var rowId })
            Travel(ref instance, (ushort)rowId);
    }
    public static void Travel(ref this AgentWorldTravel instance, ushort destinationWorldId) {
        if (Svc.ClientState.TerritoryType is not (129 or 130 or 132)) return; // is there really no sheet column that indicates this
        if (Svc.Data.GetRow<World>(destinationWorldId)?.DataCenter.RowId != Svc.PlayerState.CurrentWorld.Value.DataCenter.RowId) return;

        instance.DestinationWorldId = destinationWorldId;
        instance.SetupWorldTravelInfo((ushort)Svc.PlayerState.CurrentWorld.RowId, destinationWorldId);
        var retval = new AtkValue();
        Span<AtkValue> values = [new AtkValue { Type = ValueType.Int, Int = 0 }];
        instance.ReceiveEvent(&retval, values.GetPointer(0), (uint)values.Length, 1);
    }
}
