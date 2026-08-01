using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Pathfinding;

public class PathfinderStep
{
    public PathfinderStepType Type;

    public uint NodeId = 0;

    // Defaulted per-zone rather than to South Horn's literal. Steps are only
    // ever built while standing in the zone being pathed.
    public Aethernet Aethernet = ZoneData.CurrentBaseCamp;

    public static PathfinderStep WalkToDestination(uint id)
    {
        return new PathfinderStep
        {
            Type = PathfinderStepType.WalkToNode,
            NodeId = id,
        };
    }

    public static PathfinderStep WalkToAethernet(Aethernet aethernet)
    {
        return new PathfinderStep
        {
            Type = PathfinderStepType.WalkToAethernet,
            Aethernet = aethernet,
        };
    }

    public static PathfinderStep TeleportToAethernet(Aethernet aethernet)
    {
        return new PathfinderStep
        {
            Type = PathfinderStepType.TeleportToAethernet,
            Aethernet = aethernet,
        };
    }

    public static PathfinderStep ReturnToBaseCamp()
    {
        return new PathfinderStep
        {
            Type = PathfinderStepType.ReturnToBaseCamp,
            Aethernet = ZoneData.CurrentBaseCamp,
        };
    }
}
