using System;
using Lalalazy.Hub;

namespace LazyFishSitter;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). The sit command text is left out on
/// purpose: the plugin sends it as a chat command, so exposing it would let an outside caller run any command.
/// </summary>
internal static class HubAdapter
{
    public static void Declare(HubEndpoint ep, Plugin plugin)
    {
        var c = plugin.Config;
        ep.Toggle("enabled", "Sit while fishing", () => c.Enabled, v => { c.Enabled = v; plugin.SaveConfig(); },
            group: "General", tip: "Sits the character down while fishing.", master: true);
    }
}
