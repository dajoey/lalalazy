using System.Collections.Generic;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Data;

namespace LazyOccultCrescent.Pathfinding;

public struct ToNode(uint id, float distance, List<Position> path)
{
    public uint Id { get; set; } = id;

    public float Distance { get; set; } = distance;

    public List<Position> Path { get; set; } = path;
}

public struct ToAethernet(Aethernet aethernet, float distance, List<Position> path)
{
    public Aethernet Aethernet { get; set; } = aethernet;

    public float Distance { get; set; } = distance;

    public List<Position> Path { get; set; } = path;
}

public struct NodeDataSchema()
{
    public Dictionary<uint, List<ToNode>> NodeToNodeDistances { get; set; } = [];

    public Dictionary<Aethernet, List<ToNode>> AethernetToNodeDistances { get; set; } = [];

    public Dictionary<uint, List<ToAethernet>> NodeToAethernetDistances { get; set; } = [];
}
