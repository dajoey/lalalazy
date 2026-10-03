namespace LazyHub.Core;

public enum PluginState
{
    /// <summary>Not in the installed plugin list at all.</summary>
    NotInstalled,

    /// <summary>Installed but not running (disabled, load failed, or waiting).</summary>
    InstalledNotLoaded,

    /// <summary>Running a production build.</summary>
    Loaded,

    /// <summary>Running a testing-channel build.</summary>
    LoadedTesting,
}

/// <summary>Turns what Dalamud reports about an installed plugin into a state, a label and a bar summary.</summary>
public static class PluginStatus
{
    public static PluginState Classify(bool installed, bool loaded, bool testing)
    {
        if (!installed) return PluginState.NotInstalled;
        if (!loaded) return PluginState.InstalledNotLoaded;
        return testing ? PluginState.LoadedTesting : PluginState.Loaded;
    }

    public static string Label(PluginState state) => state switch
    {
        PluginState.NotInstalled => "Not installed",
        PluginState.InstalledNotLoaded => "Not loaded",
        PluginState.Loaded => "Loaded",
        PluginState.LoadedTesting => "Loaded (testing)",
        _ => "Unknown",
    };

    /// <summary>A plugin's windows can only be opened while it is running.</summary>
    public static bool CanOpen(PluginState state) => state is PluginState.Loaded or PluginState.LoadedTesting;

    /// <summary>"loaded/total" for the server info bar, for example "13/15".</summary>
    public static string Summary(IEnumerable<PluginState> states)
    {
        int total = 0, loaded = 0;
        foreach (var s in states)
        {
            total++;
            if (CanOpen(s)) loaded++;
        }
        return $"{loaded}/{total}";
    }
}
