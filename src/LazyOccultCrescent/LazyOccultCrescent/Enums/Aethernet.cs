using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.Data;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace LazyOccultCrescent.Enums;

// Enum values are PlaceName row ids, which is what ToFriendlyString() resolves against.
//
// South Horn ids came from upstream BOCCHI. North Horn ids were datamined from the
// 7.55 sqpack on 2026-08-01: PlaceName 5571 (North Horn Base Camp) followed by the
// contiguous 5572-5576 sub-area block, which mirrors South Horn's layout exactly and
// matches the six aetheryte EObjs 2015429-2015434 one-for-one.
//
// The EObj BaseId is the load-bearing value (it is what the object table is scanned
// for); the PlaceName is cosmetic. If a name renders oddly in game the pairing order
// below is the thing to correct - positions are self-correcting via ZoneDiscovery.
public enum Aethernet : uint
{
    // South Horn (territory 1252)
    BaseCamp = 4944,
    TheWanderersHaven = 4936,
    CrystallizedCaverns = 4929,
    Eldergrowth = 4930,
    Stonemarsh = 4942,

    // North Horn (territory 1346)
    NorthHornBaseCamp = 5571,
    SinkingSanctuary = 5572,
    SuspendedMasonry = 5573,
    MolderingOutskirts = 5574,
    UnhallowedHamlet = 5575,
    CrownOfKarnak = 5576,
}

public class AethernetData
{
    public readonly static float DISTANCE = 3.8f;

    public Aethernet Aethernet;

    public uint BaseId;

    public uint Territory;

    public Vector3 Position;

    public Vector3 Destination; // Where you end up after teleporting to this shard

    // True when Position/Destination are surveyed constants rather than runtime guesses.
    public bool HasSurveyedPosition;

    private readonly static Dictionary<Aethernet, AethernetData> Table = new()
    {
        // ---- South Horn ----------------------------------------------------
        [Aethernet.BaseCamp] = new AethernetData
        {
            Aethernet = Aethernet.BaseCamp,
            BaseId = 2014664,
            Territory = ZoneData.SOUTHHORN,
            Position = new Vector3(830.75f, 72.98f, -695.98f),
            Destination = new Vector3(835.3f, 73f, -695.9f),
            HasSurveyedPosition = true,
        },
        [Aethernet.TheWanderersHaven] = new AethernetData
        {
            Aethernet = Aethernet.TheWanderersHaven,
            BaseId = 2014665,
            Territory = ZoneData.SOUTHHORN,
            Position = new Vector3(-173.02f, 8.19f, -611.14f),
            Destination = new Vector3(-169.1f, 6.5f, -609.4f),
            HasSurveyedPosition = true,
        },
        [Aethernet.CrystallizedCaverns] = new AethernetData
        {
            Aethernet = Aethernet.CrystallizedCaverns,
            BaseId = 2014666,
            Territory = ZoneData.SOUTHHORN,
            Position = new Vector3(-358.14f, 101.98f, -120.96f),
            Destination = new Vector3(-354.6f, 100f, -120.7f),
            HasSurveyedPosition = true,
        },
        [Aethernet.Eldergrowth] = new AethernetData
        {
            Aethernet = Aethernet.Eldergrowth,
            BaseId = 2014667,
            Territory = ZoneData.SOUTHHORN,
            Position = new Vector3(306.94f, 105.18f, 305.65f),
            // Upstream had X as -302.3 while the shard itself is at +306.94;
            // the LGB layout confirms the positive value. A teleport landing
            // point 600y from its own shard is a sign error, not a survey.
            Destination = new Vector3(302.3f, 103f, 306f),
            HasSurveyedPosition = true,
        },
        [Aethernet.Stonemarsh] = new AethernetData
        {
            Aethernet = Aethernet.Stonemarsh,
            BaseId = 2014744,
            Territory = ZoneData.SOUTHHORN,
            Position = new Vector3(-384.12f, 99.20f, 281.42f),
            Destination = new Vector3(-384f, 97.2f, 278.1f),
            HasSurveyedPosition = true,
        },

        // ---- North Horn ----------------------------------------------------
        // Positions extracted from bg/ex5/03_ocn_o6/btl/o6b2/level/planmap.lgb
        // on 2026-08-01, not guessed and not runtime-discovered. Identity was
        // resolved by fitting the map->world transform against South Horn's five
        // known shards and matching each MapMarker (icon 60959) to its nearest
        // layout object; worst fit error 2.1y, the rest under 1.1y.
        //
        // Running the same extraction over South Horn reproduces upstream's
        // hand-surveyed constants to within 0.03y, which is what makes these
        // trustworthy. ZoneDiscovery remains as a safety net, not the source.
        [Aethernet.NorthHornBaseCamp] = new AethernetData
        {
            Aethernet = Aethernet.NorthHornBaseCamp,
            BaseId = 2015429,
            Territory = ZoneData.NORTHHORN,
            Position = new Vector3(880.00f, 259.74f, 880.06f),
            Destination = new Vector3(880.00f, 259.74f, 880.06f),
            HasSurveyedPosition = true,
        },
        [Aethernet.SinkingSanctuary] = new AethernetData
        {
            Aethernet = Aethernet.SinkingSanctuary,
            BaseId = 2015430,
            Territory = ZoneData.NORTHHORN,
            Position = new Vector3(357.67f, 45.77f, -554.31f),
            Destination = new Vector3(357.67f, 45.77f, -554.31f),
            HasSurveyedPosition = true,
        },
        [Aethernet.SuspendedMasonry] = new AethernetData
        {
            Aethernet = Aethernet.SuspendedMasonry,
            BaseId = 2015431,
            Territory = ZoneData.NORTHHORN,
            Position = new Vector3(-547.25f, 68.00f, 594.40f),
            Destination = new Vector3(-547.25f, 68.00f, 594.40f),
            HasSurveyedPosition = true,
        },
        [Aethernet.MolderingOutskirts] = new AethernetData
        {
            Aethernet = Aethernet.MolderingOutskirts,
            BaseId = 2015432,
            Territory = ZoneData.NORTHHORN,
            Position = new Vector3(-388.57f, 41.22f, -440.52f),
            Destination = new Vector3(-388.57f, 41.22f, -440.52f),
            HasSurveyedPosition = true,
        },
        [Aethernet.UnhallowedHamlet] = new AethernetData
        {
            Aethernet = Aethernet.UnhallowedHamlet,
            BaseId = 2015433,
            Territory = ZoneData.NORTHHORN,
            Position = new Vector3(-13.36f, 3.14f, -40.51f),
            Destination = new Vector3(-13.36f, 3.14f, -40.51f),
            HasSurveyedPosition = true,
        },
        [Aethernet.CrownOfKarnak] = new AethernetData
        {
            Aethernet = Aethernet.CrownOfKarnak,
            BaseId = 2015434,
            Territory = ZoneData.NORTHHORN,
            Position = new Vector3(451.68f, 70.93f, 528.84f),
            Destination = new Vector3(451.68f, 70.93f, 528.84f),
            HasSurveyedPosition = true,
        },
    };

    // Every shard in the game, both zones. Callers that want the current zone want All().
    public static IEnumerable<AethernetData> Every()
    {
        return Table.Values;
    }

    // Shards for the zone the player is standing in. Everything downstream
    // (pathfinding, precompute, teleport UI) is scoped to the current zone,
    // so cross-horn shards must never leak into this list.
    public static IEnumerable<AethernetData> All()
    {
        return AllFor(ZoneData.CurrentTerritory);
    }

    public static IEnumerable<AethernetData> AllFor(uint territory)
    {
        var scoped = Table.Values.Where(d => d.Territory == territory).ToList();

        // Outside a known horn, fall back to South Horn so first-load UI has
        // something coherent to render rather than throwing on First().
        return scoped.Count > 0
            ? scoped
            : Table.Values.Where(d => d.Territory == ZoneData.SOUTHHORN).ToList();
    }

    public static IOrderedEnumerable<AethernetData> AllByDistance()
    {
        return AllByDistance(Player.Position);
    }

    public static IOrderedEnumerable<AethernetData> AllByDistance(Vector3 position)
    {
        return All().OrderBy(a => Vector3.Distance(a.Position, position));
    }

    public static AethernetData GetClosestTo(Vector3 to)
    {
        return All().OrderBy(data => Vector3.Distance(to, data.Position)).First();
    }

    public static AethernetData GetClosestToPlayer()
    {
        return GetClosestTo(Player.Position);
    }

    // Called by ZoneDiscovery when a shard is seen in the object table.
    public static void RecordDiscoveredPosition(uint baseId, Vector3 position)
    {
        var datum = Table.Values.FirstOrDefault(d => d.BaseId == baseId);
        if (datum == null || datum.HasSurveyedPosition)
        {
            return;
        }

        datum.Position = position;

        // Teleport drops you a couple of yalms off the shard itself. Without a
        // survey the shard position is the best available approximation, and it
        // is only used as a pathfinding seed.
        if (datum.Destination == Vector3.Zero)
        {
            datum.Destination = position;
        }
    }

    public float DistanceTo(Vector3 to)
    {
        return Vector3.Distance(to, Position);
    }

    public float DistanceToPlayer()
    {
        return DistanceTo(Player.Position);
    }
}

public static class AethernetExtensions
{
    public static bool IsKnown(this AethernetData datum)
    {
        return datum.Position != Vector3.Zero;
    }

    public static string ToFriendlyString(this Aethernet aethernet)
    {
        var row = Svc.Data.GetExcelSheet<PlaceName>().FirstOrDefault(p => p.RowId == (uint)aethernet);
        var name = row.Name.ToString();

        return string.IsNullOrWhiteSpace(name) ? aethernet.ToString() : name;
    }

    public static AethernetData GetData(this Aethernet aethernet)
    {
        return AethernetData.Every().FirstOrDefault(d => d.Aethernet == aethernet)
               ?? AethernetData.All().First();
    }
}
