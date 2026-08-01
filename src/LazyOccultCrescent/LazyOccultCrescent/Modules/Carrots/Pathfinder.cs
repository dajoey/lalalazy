using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Pathfinding;

namespace LazyOccultCrescent.Modules.Carrots;

public class Pathfinder : BasePathfinder
{
    public Pathfinder(float returnCost = 300f, float teleportCost = 50f) : base(returnCost, teleportCost)
    {
        LoadFile("precomputed_carrot_hunt_data.json");
    }

    protected override uint GetStartingNode(Vector3 start, List<uint> nodes)
    {
        return CarrotData.Data
            .Where(c => nodes.Contains(c.Id))
            .OrderBy(c => Vector3.Distance(start, c.Position))
            .First().Id;
    }
}
