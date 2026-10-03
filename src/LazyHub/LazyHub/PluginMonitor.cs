using Dalamud.Plugin;
using LazyHub.Core;

namespace LazyHub;

/// <summary>
/// Reads what Dalamud knows about the installed plugins and maps it onto the lalalazy catalog.
/// Everything here runs on the framework thread and only reads; it never loads, unloads or
/// changes another plugin (Dalamud's exposed-plugin interface has no way to do that).
/// </summary>
internal sealed class PluginMonitor(IDalamudPluginInterface pi)
{
    public readonly record struct Row(CatalogEntry Entry, PluginState State);

    /// <summary>One row per catalog entry, in catalog order.</summary>
    public IReadOnlyList<Row> Snapshot()
    {
        var byName = new Dictionary<string, IExposedPlugin>(StringComparer.Ordinal);
        foreach (var p in pi.InstalledPlugins)
        {
            // Two installs can share an InternalName (for example a dev plugin next to an installed one):
            // keep the running one so the row reflects what is actually active.
            if (!byName.TryGetValue(p.InternalName, out var existing) || (p.IsLoaded && !existing.IsLoaded))
                byName[p.InternalName] = p;
        }

        var rows = new List<Row>(Catalog.Plugins.Count);
        foreach (var entry in Catalog.Plugins)
        {
            byName.TryGetValue(entry.InternalName, out var plugin);
            rows.Add(new Row(entry, PluginStatus.Classify(
                installed: plugin != null,
                loaded: plugin?.IsLoaded ?? false,
                testing: plugin?.IsTesting ?? false)));
        }
        return rows;
    }

    public bool IsLoaded(string internalName)
    {
        foreach (var p in pi.InstalledPlugins)
            if (p.IsLoaded && string.Equals(p.InternalName, internalName, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Opens the plugin's main window, or its settings window when it has no main window.</summary>
    public bool Open(string internalName)
    {
        foreach (var p in pi.InstalledPlugins)
        {
            if (!p.IsLoaded || !string.Equals(p.InternalName, internalName, StringComparison.Ordinal)) continue;

            if (p.HasMainUi) { p.OpenMainUi(); return true; }
            if (p.HasConfigUi) { p.OpenConfigUi(); return true; }
        }
        return false;
    }
}
