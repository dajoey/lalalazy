using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;

namespace clib.Extensions;

public static unsafe class FlagMapMarkerExtensions {
    public static Vector2 Position(this FlagMapMarker fmm) => new(fmm.XFloat, fmm.YFloat);

    public static Vector2? GetPosition() {
        if (AgentMap.Instance() == null || AgentMap.Instance()->FlagMarkerCount == 0)
            return null;
        var flag = AgentMap.Instance()->FlagMapMarkers[0];
        return new(flag.XFloat, flag.YFloat);
    }

    public static uint? GetTerritoryId() {
        if (AgentMap.Instance() == null || AgentMap.Instance()->FlagMarkerCount == 0)
            return null;
        return AgentMap.Instance()->FlagMapMarkers[0].TerritoryId;
    }

    public static FlagMapMarker? Get() {
        if (AgentMap.Instance() == null || AgentMap.Instance()->FlagMarkerCount == 0)
            return null;
        return AgentMap.Instance()->FlagMapMarkers[0];
    }
}
