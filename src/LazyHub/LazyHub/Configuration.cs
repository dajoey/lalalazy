using Dalamud.Configuration;

namespace LazyHub;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Last plugin version whose "What's new" popup was dismissed (shared LalaChangelog gate).
    /// null/empty = never recorded: the gate records the running version silently and shows nothing.
    /// </summary>
    public string? LastSeenChangelogVersion { get; set; }
}
