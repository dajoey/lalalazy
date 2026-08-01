using ECommons.Throttlers;
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

    // Two independent full-table scans, called back to back from the panel every
    // frame, for two counts a human reads about once a second. One pass, cached.
    private readonly static Dictionary<TowerType, (int In, int Near)> counts = new();

    private static void RefreshCounts(TowerType type)
    {
        if (counts.ContainsKey(type) && !EzThrottler.Throttle($"TowerHelper.Count.{type}", 500))
        {
            return;
        }

        var inZone = 0;
        var nearZone = 0;

        foreach (var o in Svc.Objects)
        {
            if (o.ObjectKind != ObjectKind.Pc)
            {
                continue;
            }

            if (IsInTowerZone(type, o.Position))
            {
                inZone++;
            }

            if (IsNearTowerZone(type, o.Position))
            {
                nearZone++;
            }
        }

        counts[type] = (inZone, nearZone);
    }

    public static int GetPlayersInTowerZone(TowerType type)
    {
        if (!IsPlayerNearTower(type))
        {
            return -1;
        }

        RefreshCounts(type);
        return counts[type].In;
    }

    public static int GetPlayersNearTowerZone(TowerType type)
    {
        if (!IsPlayerNearTower(type))
        {
            return -1;
        }

        RefreshCounts(type);
        return counts[type].Near;
    }
}
