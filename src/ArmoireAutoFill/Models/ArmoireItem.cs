using System.Text.Json.Serialization;

namespace ArmoireAutoFill.Models;

public enum GearSlot
{
    Head,
    Body,
    Hands,
    Legs,
    Feet
}

public enum OwnershipStatus
{
    NotOwned,
    InInventory
}

public class ArmoireItem
{
    public uint ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public GearSlot Slot { get; init; }
    public string? DungeonName { get; set; }
    public uint? ContentFinderConditionId { get; set; }
    public OwnershipStatus Owned { get; set; } = OwnershipStatus.NotOwned;
}
