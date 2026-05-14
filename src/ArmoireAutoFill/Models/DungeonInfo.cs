namespace ArmoireAutoFill.Models;

public class DungeonInfo
{
    public uint ContentFinderConditionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public byte Level { get; init; }
    public List<ArmoireItem> Items { get; set; } = [];
    public int MissingCount => Items.Count(i => i.Owned == OwnershipStatus.NotOwned);
    public int OwnedCount => Items.Count(i => i.Owned != OwnershipStatus.NotOwned);
    public bool IsComplete => MissingCount == 0 && Items.Count > 0;
}
