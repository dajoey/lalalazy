using Dalamud.Configuration;
using System.Collections.Generic;
using System.Numerics;
using LazyFateAutomation.Helpers.Utils;

namespace LazyFateAutomation;

public class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 0;

    // Config options from FateToolKitConfig
    public int MaxDuration = 900;
    public int MinTimeRemaining = 120;
    public int MaxProgress = 90;
    public bool SwapZones = true;

    public string DisplayNameFormat = "[{Level}] {Name}";
    public Vector4 BarColour = new(0.404f, 0.259f, 0.541f, 1f);
    public Dictionary<FateType, HashSet<uint>> Blacklist = [];
    public List<FateSortOrder> SortOrder =
    [
        new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
        new() { Criteria = FateSortCriteria.Progress, Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemaining, Descending = false },
        new() { Criteria = FateSortCriteria.Distance, Descending = false },
    ];

    // Standalone plugin specific fields that are loaded/saved
    public HashSet<uint> SelectedSwapZones = [];
    public string SelectedModeId = "None";

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
