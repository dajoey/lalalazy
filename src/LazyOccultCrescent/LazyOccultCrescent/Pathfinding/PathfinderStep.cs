using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Pathfinding;

public class PathfinderStep
{
    public PathfinderStepType Type;

    public uint NodeId = 0;

    public Aethernet Aethernet = Aethernet.BaseCamp;

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
        };
    }
}
