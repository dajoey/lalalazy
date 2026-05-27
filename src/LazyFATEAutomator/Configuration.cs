using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace LazyFATEAutomator;

[Serializable]
public class Configuration : IPluginConfiguration
{
    // BUMP THIS when the schema changes. Migration logic lives in Migrate().
    public const int CurrentSchemaVersion = 2;
    public int Version { get; set; } = CurrentSchemaVersion;

    // FATE filtering thresholds
    public int MinTimeRemaining { get; set; } = 120;   // seconds — FATE skipped if it has less than this left
    public int MaxProgress { get; set; } = 90;         // percent — FATE skipped if already this complete
    public int MaxDuration { get; set; } = 900;        // seconds — FATE skipped if its total duration exceeds this
    public int MaxLevelDelta { get; set; } = 0;        // FATE skipped if (fate.Level - player.Level) > this. 0 = never go above your level.

    // Automation behavior
    public bool SwapZones { get; set; } = false;       // teleport to a fresh zone when current is dry of FATEs
    public bool AutoSyncLevel { get; set; } = true;    // run /levelsync inside FATE if overlevel

    // Display / formatting
    public string FateNameFormat { get; set; } = "[{Level}] {Name}";

    // Blacklisted FATE IDs (manually toggled off)
    public HashSet<uint> BlacklistedFateIds { get; set; } = new();

    // Sort criteria — typed, replaces the magic-string list
    public List<FateSortRule> SortRules { get; set; } = new()
    {
        new() { Criteria = FateSortCriteria.HasBonusWithTwist,    Descending = true },
        new() { Criteria = FateSortCriteria.Progress,             Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus,             Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent,  Descending = true },
        new() { Criteria = FateSortCriteria.Distance,             Descending = false },
    };

    // FATE is "about to expire" if remaining time is < this many seconds — affects urgency sort
    public int MinTimeToPrioritise { get; set; } = 240;

    /// <summary>Called by Plugin ctor before first save. Migrates old configs to the current schema.</summary>
    public void Migrate()
    {
        if (Version >= CurrentSchemaVersion) return;

        if (Version < 1)
        {
            // pre-1: legacy magic-string SortCriteria; we replace with the typed defaults above.
            // Nothing to copy across — the old config is sparse and the defaults are better.
            Version = 1;
        }

        if (Version < 2)
        {
            // v2 added MaxLevelDelta, FateNameFormat, SortRules, MinTimeToPrioritise.
            // Defaults are populated by initializers. Just bump.
            Version = 2;
        }

        // Defensive: never persist obviously bad values
        if (MinTimeRemaining < 30)    MinTimeRemaining = 30;
        if (MaxProgress < 10)         MaxProgress = 10;
        if (MaxProgress > 100)        MaxProgress = 100;
        if (MaxDuration < 60)         MaxDuration = 60;
        if (MinTimeToPrioritise < 30) MinTimeToPrioritise = 30;

        Save();
    }

    public void Save()
    {
        try
        {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "Failed to save Lazy FATE Automator config");
        }
    }
}
