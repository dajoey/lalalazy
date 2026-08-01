namespace LazyOccultCrescent.Modules.Data;

public struct TrapPayload
{
    public uint identifier { get; set; }

    public Position position { get; set; }

    public string tower_hash { get; set; }

    public static TrapPayload Create(Trap trap)
    {
        return new TrapPayload
        {
            identifier = trap.Identifier,
            position = trap.Position,
            tower_hash = trap.Hash,
        };
    }
}
