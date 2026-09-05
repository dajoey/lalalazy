using Dalamud.Configuration;

namespace LazyFishSitter;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// DEAD SINCE v0.1.3.0 - kept only so existing config files still deserialize.
    /// The policy now runs every frame; every rate limit that matters is time-based and lives
    /// in <see cref="Core.SitPolicy"/> (10 s between sends, 3 s stand-confirm, 3 sends a trip).
    /// A poll interval only made the plugin step over short standby beats between quick casts.
    /// </summary>
    [Obsolete("Unused since v0.1.3.0; retained for config compatibility only.")]
    public int CheckIntervalSeconds { get; set; } = 2;

    /// <summary>The command run when you are found standing at the standby beat. Must start with a slash.</summary>
    public string SitCommand { get; set; } = "/sit";

    /// <summary>Newest CHANGELOG version the in-game "What's new" popup has shown (shared LalaChangelog gate).</summary>
    public string? LastSeenChangelogVersion { get; set; }
}
