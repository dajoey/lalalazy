using Dalamud.Configuration;
using Newtonsoft.Json;
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

    // ObjectCreationHandling.Replace: without this, Dalamud's Newtonsoft settings REUSE the field
    // initializer's list on every deserialize (ObjectCreationHandling.Auto keeps an existing
    // non-null collection and Adds to it instead of replacing it), so every plugin load appended
    // another copy of these 6 defaults onto the saved list. A live config was found at 72 entries
    // (12x the 6-entry default) after months of updates - harmless to ranking since the duplicates
    // are consecutive and identical, but pure waste. DedupeSortOrder() below cleans up configs that
    // already grew before this fix; this attribute stops it from growing again.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
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
    public HashSet<uint> ExcludedSwapZones = [];
    public string SelectedModeId = "None";

    // When false (default) DBG/TRC scope tracing is NOT written to LazyFateAutomation.log.
    // Set true in LazyFateAutomation.json to capture full debug logs for troubleshooting.
    public bool VerboseFileLogging = false;

    // 0.0.3.1: prioritize Forlorn Maidens / the Forlorn (rare bonus mobs that spawn mid-FATE and
    // despawn quickly if not killed) over normal FATE trash. Default on; absent in an existing
    // config deserializes as true (bool default), so this is a no-op for pre-0.0.3.1 installs.
    public bool PrioritizeForlornMaidens = true;

    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }

    /// <summary>
    /// Removes duplicate sort criteria (keeps the first occurrence), fixing lists grown by the
    /// ObjectCreationHandling append bug fixed in 0.0.3.1 (see the [JsonProperty] comment on
    /// SortOrder above). Safe to run on every load; a no-op once the list has no duplicate
    /// Criteria values. Returns true if it changed anything (caller should then Save()).
    /// </summary>
    public bool DedupeSortOrder() {
        var seen = new HashSet<FateSortCriteria>();
        var deduped = new List<FateSortOrder>();
        foreach (var entry in SortOrder) {
            if (seen.Add(entry.Criteria))
                deduped.Add(entry);
        }
        if (deduped.Count == SortOrder.Count)
            return false;
        SortOrder = deduped;
        return true;
    }

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
