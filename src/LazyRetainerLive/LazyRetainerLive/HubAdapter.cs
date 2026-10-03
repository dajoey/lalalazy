using System;
using Lalalazy.Hub;

namespace LazyRetainerLive;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). The port is left out on purpose:
/// changing it rebinds the listener at once and the dashboard relay depends on the fixed port.
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep, Plugin plugin)
    {
        var c = plugin.Config;
        ep.Toggle("enabled", "Serve retainer data", () => c.Enabled, v => { c.Enabled = v; plugin.SaveConfig(); },
            group: "General", tip: "Serves live retainer and venture data on this computer only.", master: true);
    }
}
