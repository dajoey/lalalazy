using Dalamud.Configuration;

namespace LazyFateAutomation;

[System.Serializable]
public sealed class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 1;

    public int MaxDuration { get; set; } = 900;
    public int MinTimeRemaining { get; set; } = 120;
    public int MaxProgress { get; set; } = 90;
    public bool SwapZones { get; set; } = true;

    public string DisplayNameFormat { get; set; } = "[{Level}] {Name}";
    public Vector4 BarColour { get; set; } = new(0.404f, 0.259f, 0.541f, 1f);
    public Dictionary<FateType, HashSet<uint>> Blacklist { get; set; } = [];
    public List<FateSortOrder> SortOrder { get; set; } =
    [
        new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
        new() { Criteria = FateSortCriteria.Progress, Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemaining, Descending = false },
        new() { Criteria = FateSortCriteria.Distance, Descending = false },
    ];

    public string SelectedModeId { get; set; } = "None";
    public HashSet<uint> SelectedSwapZones { get; set; } = [];

    public void Save() {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}
