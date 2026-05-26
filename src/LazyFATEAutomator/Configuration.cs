using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace LazyFATEAutomator;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // FATE Filtering Thresholds
    public int MinTimeRemaining { get; set; } = 120; // seconds
    public int MaxProgress { get; set; } = 90; // percentage
    public int MaxDuration { get; set; } = 900; // seconds

    // Automation Behavior
    public bool SwapZones { get; set; } = false;
    public int GemstoneThreshold { get; set; } = 1250; // default warning threshold
    public bool AutoSyncLevel { get; set; } = true;

    // Special Modes
    public bool YokaiGrindMode { get; set; } = false;
    public bool RelicGrindMode { get; set; } = false;

    // Blacklisted FATE IDs
    public HashSet<uint> BlacklistedFateIds { get; set; } = new();

    // Priority Sort Order Preferences
    public List<string> SortCriteria { get; set; } = new()
    {
        "HasTwistOfFate",
        "Progress",
        "HasBonus",
        "Distance"
    };

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
