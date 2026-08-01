using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace LazyOccultCrescent.Data;

public static class ZoneData
{
    // Occult Crescent field zones. Both report TerritoryIntendedUse 61.
    //   1252 = o6b2's predecessor "o6b1", South Horn, patch 7.25, Map 967,  PlaceName 4932
    //   1346 = "o6b2",                  North Horn, patch 7.55, Map 1135, PlaceName 5577
    public const uint SOUTHHORN = 1252;
    public const uint NORTHHORN = 1346;

    // Single source of truth. Modules used to each carry their own [1252] literal;
    // they now read this so adding a third horn is a one-line change.
    public readonly static IReadOnlyList<uint> OccultTerritories = [SOUTHHORN, NORTHHORN];

    public readonly static Dictionary<uint, string> ZoneNames = new()
    {
        { SOUTHHORN, "South Horn" },
        { NORTHHORN, "North Horn" },
    };

    // Surveyed positions. South Horn's came from upstream BOCCHI; North Horn's are
    // learned at runtime by ZoneDiscovery and persisted, because the aetheryte sits
    // in the LGB layout and is not reachable from Excel. A missing entry is not an
    // error - callers fall back to discovery.
    public readonly static Dictionary<uint, Vector3> Aetherytes = new()
    {
        { SOUTHHORN, new Vector3(830.75f, 72.98f, -695.98f) },
        { NORTHHORN, new Vector3(880.00f, 259.74f, 880.06f) },
    };

    public readonly static Dictionary<uint, Vector3> StartingLocations = new()
    {
        { SOUTHHORN, new Vector3(850.33f, 72.99f, -704.07f) },
        // Return drops you at the aetheryte. South Horn's surveyed spawn sits
        // ~21y off its aetheryte; without a North Horn survey the aetheryte
        // itself is the safe approximation. Its ABSENCE was the bug:
        // ReturnChain.GetCostToReturn() throws outright on a missing entry, so
        // every Return in North Horn failed and the Automator fell through to
        // whatever navigation option was left.
        { NORTHHORN, new Vector3(880.00f, 259.74f, 880.06f) },
    };

    public static uint CurrentTerritory
    {
        get => Svc.ClientState.TerritoryType;
    }

    // Zone functions
    public static bool IsInSouthHorn()
    {
        return CurrentTerritory == SOUTHHORN;
    }

    public static bool IsInNorthHorn()
    {
        return CurrentTerritory == NORTHHORN;
    }

    public static bool IsInOccultCrescent()
    {
        return Svc.Objects.LocalPlayer != null && OccultTerritories.Contains(CurrentTerritory);
    }

    public static bool TryGetAetheryte(out Vector3 position)
    {
        if (Aetherytes.TryGetValue(CurrentTerritory, out position))
        {
            return true;
        }

        return ZoneDiscovery.TryGetAetheryte(CurrentTerritory, out position);
    }

    public static bool TryGetStartingLocation(out Vector3 position)
    {
        if (StartingLocations.TryGetValue(CurrentTerritory, out position))
        {
            return true;
        }

        // The aetheryte is a good enough staging point when the exact spawn is unknown.
        return TryGetAetheryte(out position);
    }

    // Tower functions
    // Both towers gate the same way: you are in the field zone but carrying the
    // duty statuses. South Horn -> Forked Tower: Blood, North Horn -> Forked Tower: Magic.
    private static bool IsInTowerInstance()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            return false;
        }

        return player.StatusList.HasAny(
            PlayerStatus.DutiesAsAssigned,
            PlayerStatus.ResurrectionDenied,
            PlayerStatus.ResurrectionRestricted
        ) && IsInOccultCrescent();
    }

    public static bool IsInForkedTowerBlood()
    {
        return IsInTowerInstance() && IsInSouthHorn();
    }

    public static bool IsInForkedTowerMagic()
    {
        return IsInTowerInstance() && IsInNorthHorn();
    }

    public static bool IsInForkedTower()
    {
        return IsInTowerInstance();
    }

    private static string GetCurrentZoneName()
    {
        if (ZoneNames.TryGetValue(CurrentTerritory, out var name))
        {
            return name;
        }

        throw new Exception($"Unknown Zone (territory {CurrentTerritory})");
    }

    public static string GetCurrentZoneDataDirectory()
    {
        var directory = Path.Join(Svc.PluginInterface.AssemblyLocation.DirectoryName, "Data", GetCurrentZoneName().Replace(" ", ""));
        Directory.CreateDirectory(directory);

        return directory;
    }

    // The base camp shard for a given zone. Several call sites used the
    // Aethernet.BaseCamp literal, which is South Horn's - in North Horn that
    // silently pointed at coordinates ~1,500y outside the zone.
    public static Aethernet BaseCampFor(uint territory)
    {
        return territory == NORTHHORN ? Aethernet.NorthHornBaseCamp : Aethernet.BaseCamp;
    }

    public static Aethernet CurrentBaseCamp
    {
        get => BaseCampFor(CurrentTerritory);
    }

    public static bool IsBaseCamp(Aethernet aethernet)
    {
        return aethernet is Aethernet.BaseCamp or Aethernet.NorthHornBaseCamp;
    }

    public static Aethernet GetClosestAethernetShard(Vector3 position)
    {
        return AethernetData.All().OrderBy((data) => Vector3.Distance(position, data.Position)).First()!.Aethernet;
    }

    public static IList<IGameObject> GetNearbyAethernetShards(float range = 4.3f)
    {
        var playerPos = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        var ids = AethernetData.All().Select(datum => datum.BaseId).ToHashSet();

        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => ids.Contains(o.BaseId))
            .Where(o => Vector3.Distance(o.Position, playerPos) <= range)
            .ToList();
    }

    public static bool IsNearAethernetShard(Aethernet aethernet, float range = 4.3f)
    {
        return GetNearbyAethernetShards(range).Any(o => o.BaseId == aethernet.GetData().BaseId);
    }

    public static IList<IGameObject> GetNearbyKnowledgeCrystal(float range = 4.5f)
    {
        var playerPos = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;

        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => o.BaseId == (uint)OccultObjectType.KnowledgeCrystal)
            .Where(o => Vector3.Distance(o.Position, playerPos) <= range)
            .ToList();
    }

    public static bool IsNearKnowledgeCrystal(float range = 4.5f)
    {
        return GetNearbyKnowledgeCrystal(range).Any();
    }
}
