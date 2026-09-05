using Dalamud.Configuration;

namespace LazyRetainerLive;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Serve the live retainer snapshot on loopback. Off kills the listener.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Loopback port for GET /retainers. Default 10504 (10501-10503 taken on the game host).</summary>
    public int Port { get; set; } = 10504;

    /// <summary>
    /// Last plugin version whose "What's new" popup the player has dismissed (shared LalaChangelog gate).
    /// null/empty = never recorded: the gate records the running version silently and shows nothing.
    /// </summary>
    public string? LastSeenChangelogVersion { get; set; }
}
