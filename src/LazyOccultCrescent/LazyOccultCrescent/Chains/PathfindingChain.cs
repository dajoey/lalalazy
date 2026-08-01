using System.Numerics;
using LazyOccultCrescent.Data;
using Ocelot.Chain;
using Ocelot.IPC;

namespace LazyOccultCrescent.Chains;

public class PathfindingChain : ChainFactory
{
    private readonly EventData data;

    private readonly Vector3 destination;

    private readonly float? maxRadius;

    private readonly float? minRadius;

    private readonly VNavmesh vnav;

    public PathfindingChain(
        VNavmesh vnav,
        Vector3 destination,
        EventData data,
        float? maxRadius = null,
        float? minRadius = null)
    {
        this.vnav = vnav;
        this.destination = destination;
        this.data = data;
        this.maxRadius = maxRadius;
        this.minRadius = minRadius;
    }

    protected override Chain Create(Chain chain)
    {
        return Chain.Create("Pathfinding")
            .Then(PathfindAndMoveToChain.RandomNearby(vnav, destination, maxRadius ?? 1f, minRadius ?? 0f));
    }
}
