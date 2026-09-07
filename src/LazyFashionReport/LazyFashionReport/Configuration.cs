using Dalamud.Configuration;

namespace LazyFashionReport;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Last plugin version whose "What's new" popup the player has dismissed (shared LalaChangelog gate).
    /// null/empty = never recorded: the gate records the running version silently and shows nothing.
    /// </summary>
    public string? LastSeenChangelogVersion { get; set; }

    /// <summary>Open the assistant window automatically when the Fashion Report addon opens.</summary>
    public bool AutoOpen { get; set; } = true;

    /// <summary>Filter candidate items down to what you own (bags + glamour dresser + armoire).</summary>
    public bool FilterOwned { get; set; } = true;

    /// <summary>Maximum candidates shown per slot.</summary>
    public int MaxCandidatesPerSlot { get; set; } = 8;

    /// <summary>Diagnostic decision tap (off by default per the decision-taps reference).</summary>
    public bool DecisionTelemetry { get; set; } = false;

    /// <summary>Auto-dress spec v1 step 4: offer a craft via Artisan's public IPC for a better
    /// crowd candidate the player does not own. OFF by default per the spec; buying stays out
    /// of scope for this toggle.</summary>
    public bool FetchMissingCraft { get; set; } = false;
}
