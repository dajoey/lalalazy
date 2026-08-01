using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;
using ECommons.GameHelpers;

namespace LazyOccultCrescent;

internal static class TowerHelper
{
    public enum TowerType
    {
        Blood,
    }

    public readonly static Dictionary<TowerType, Vector3> TowerPositions = new()
    {
        { TowerType.Blood, new Vector3(63f, 126.5f, 4f) },
    };

    public readonly static Dictionary<TowerType, float> TowerRadii = new()
    {
        { TowerType.Blood, 20f },
    };

    public static bool IsInTowerZone(TowerType type, Vector3 position)
    {
        return Vector3.Distance(TowerPositions[type], position) <= TowerRadii[type];
    }

    public static bool IsNearTowerZone(TowerType type, Vector3 position)
    {
        var distance = Vector3.Distance(TowerPositions[type], position);
        var radius = TowerRadii[type];

        return distance > radius && distance <= radius * 4;
    }

    public static bool IsPlayerNearTower(TowerType type)
    {
        return IsNearTowerZone(type, Player.Position) || IsInTowerZone(type, Player.Position);
    }

    public static int GetPlayersInTowerZone(TowerType type)
    {
        if (!IsPlayerNearTower(type))
        {
            return -1;
        }

        return Svc.Objects.Count(o => o.ObjectKind == ObjectKind.Pc && IsInTowerZone(type, o.Position));
    }

    public static int GetPlayersNearTowerZone(TowerType type)
    {
        if (!IsPlayerNearTower(type))
        {
            return -1;
        }

        return Svc.Objects.Count(o => o.ObjectKind == ObjectKind.Pc && IsNearTowerZone(type, o.Position));
    }
}
