namespace LazyOccultCrescent.Modules.Data;

public struct MonsterPayload
{
    public string name { get; set; }

    public uint identifier { get; set; }

    public uint level { get; set; }

    public Position position { get; set; }

    public static MonsterPayload Create(Enemy enemy)
    {
        return new MonsterPayload
        {
            name = enemy.Name,
            identifier = enemy.LayoutId,
            position = enemy.Position,
            level = enemy.Rank,
        };
    }
}
