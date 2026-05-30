using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Extensions;

namespace LazyFateAutomation.Helpers.Utils;

public static class Coords {
    public static Vector3 PixelCoordsToWorldCoords(int x, int z, uint mapId) {
        var map = Svc.Data.GetRef<Sheets.Map>(mapId);
        var scale = map.Value.SizeFactor * 0.01f;
        var wx = PixelCoordToWorldCoord(x, scale, map.Value.OffsetX);
        var wz = PixelCoordToWorldCoord(z, scale, map.Value.OffsetY);
        return new(wx, 0, wz);
    }

    // see: https://github.com/xivapi/ffxiv-datamining/blob/master/docs/MapCoordinates.md
    // see: dalamud MapLinkPayload class
    public static float PixelCoordToWorldCoord(float coord, float scale, short offset) {
        // +1 - networkAdjustment == 0
        // (coord / scale * 2) * (scale / 100) = coord / 50
        // * 2048 / 41 / 50 = 0.999024
        const float factor = 2048.0f / (50 * 41);
        return (coord * factor - 1024f) / scale - offset * 0.001f;
    }

    public static uint? FindClosestAetheryteToFlag(bool includeAethernet = true)
        => FlagMapMarkerExtensions.Get() is { } flag ? FindClosestAetheryte(flag.TerritoryId, flag.Position().ToVector3(), includeAethernet) : null;

    public static uint? FindClosestAetheryte(uint territoryTypeId, Vector3 worldPos, bool includeAethernet = true) {
        if (territoryTypeId == 886) // Firmament
            return 70; // Ishgard
        if (territoryTypeId == 478) // Hinterlands
            return 75; // Idyllshire
        List<Sheets.Aetheryte> aetherytes = [.. Svc.Data.GetExcelSheet<Sheets.Aetheryte>().Where(a => a.Territory.RowId == territoryTypeId && (includeAethernet || a.IsAetheryte)) ?? []];
        // aetherytes tend to not have a Y whereas gates do. Maps are mostly flat so just equalise and ignore Y
        return aetherytes.Count > 0 ? aetherytes.MinBy(a => (worldPos.ToVector2() - AetherytePosition(a).ToVector2()).LengthSquared()).RowId : null;
    }

    public static Vector3 AetherytePosition(uint aetheryteId) => AetherytePosition(Svc.Data.GetRef<Sheets.Aetheryte>(aetheryteId).Value);
    public static Vector3 AetherytePosition(Sheets.Aetheryte a) {
        // stolen from HTA, uses pixel coordinates
        var level = a.Level[0].ValueNullable;
        if (level != null)
            return new(level.Value.X, level.Value.Y, level.Value.Z);
        var marker = Svc.Data.GetSubrowExcelSheet<Sheets.MapMarker>().SelectMany(m => m).FirstOrDefault(m =>
            m.DataType == 3 && m.DataKey.RowId == a.RowId ||
            m.DataType == 4 && m.DataKey.RowId == a.AethernetName.RowId);
        return PixelCoordsToWorldCoords(marker.X, marker.Y, a.Territory.Value.Map.RowId);
    }

    public static bool IsTeleportingFaster(Vector3 dest) {
        if (Svc.Objects.LocalPlayer is not { Position: var pos }) return false;
        const int overhead = 300; // approximately the distance you can travel in the time it takes you to teleport
        return FindClosestAetheryte(Svc.ClientState.TerritoryType, dest) is { } closest && overhead + (dest - AetherytePosition(closest)).Length() < (dest - pos).Length();
    }

    // if aetheryte is 'primary' (i.e. can be teleported to), return it; otherwise (i.e. aethernet shard) find and return primary aetheryte from same group
    public static uint FindPrimaryAetheryte(uint aetheryteId) {
        if (aetheryteId == 0)
            return 0;
        var row = Svc.Data.GetRef<Sheets.Aetheryte>(aetheryteId).Value;
        if (row.IsAetheryte)
            return aetheryteId;
        var primary = Lumina.Extensions.LinqExtensions.FirstOrNull(Svc.Data.GetExcelSheet<Sheets.Aetheryte>(), a => a.AethernetGroup == row.AethernetGroup);
        return primary?.RowId ?? 0;
    }

    public static unsafe (ulong id, Vector3 pos) FindAetheryte(uint id) {
        foreach (var obj in GameObjectManager.Instance()->Objects.IndexSorted)
            if (obj.Value != null && obj.Value->ObjectKind == ObjectKind.Aetheryte && obj.Value->BaseId == id)
                return (obj.Value->GetGameObjectId(), *obj.Value->GetPosition());
        return (0, default);
    }
}
