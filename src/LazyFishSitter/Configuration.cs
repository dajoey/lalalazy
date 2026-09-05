using Dalamud.Configuration;

namespace LazyFishSitter;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between checks while fishing. Clamped to 1..10 at read time.</summary>
    public int CheckIntervalSeconds { get; set; } = 2;

    /// <summary>The command run when you are found standing while fishing. Must start with a slash.</summary>
    public string SitCommand { get; set; } = "/sit";
}
