using System.Numerics;

namespace LazyRetainerLive;

// Game/runtime boundary only. The window reads these members of the real Plugin and RetainerLiveService;
// everything else (services, hooks, HTTP listener, telemetry, hub) is not compiled into the harness.

internal sealed class RetainerLiveService
{
    /// <summary>The published snapshot the window shows (null = none built yet).</summary>
    public CharInfo? Current { get; set; }

    /// <summary>True while the live game table is readable; false = serving the last-known snapshot.</summary>
    public bool LastTickOk { get; set; }
}

internal sealed class Plugin
{
    public Configuration Config { get; set; } = new();

    /// <summary>Null = the plugin has no service yet (the window then reports no listener error either).</summary>
    public RetainerLiveService? State { get; set; } = new();

    /// <summary>Last listener bind error text; empty = bound.</summary>
    public string HttpError { get; set; } = "";

    public int SaveCount { get; private set; }

    public void SaveConfig() => SaveCount++;
}
