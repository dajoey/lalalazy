using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace LazyGearCollector;

/// <summary>One remembered sighting of a container we can only read opportunistically.</summary>
[Serializable]
public sealed class ContainerSnapshot
{
    /// <summary>Human label, e.g. "Saddlebag" or a retainer's name.</summary>
    public string Label { get; set; } = "";

    /// <summary>ItemId to quantity seen the last time this container was readable.</summary>
    public Dictionary<uint, int> Counts { get; set; } = new();

    /// <summary>When we last successfully read it (UTC).</summary>
    public DateTime SeenUtc { get; set; } = DateTime.MinValue;
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Which collection tab was last open.</summary>
    public string LastCollectionId { get; set; } = "phantom-vision";

    /// <summary>Include opportunistically-cached containers (saddlebag, retainers) in ownership counts.</summary>
    public bool IncludeCachedContainers { get; set; } = true;

    /// <summary>Target tier the progress bars are measured against (0-3). Default is fully upgraded.</summary>
    public int TargetTier { get; set; } = 3;

    /// <summary>Keyed by container key (e.g. "saddlebag", "retainer:12345").</summary>
    public Dictionary<string, ContainerSnapshot> Snapshots { get; set; } = new();

    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
