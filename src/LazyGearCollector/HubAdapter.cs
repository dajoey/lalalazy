using System;
using Lalalazy.Hub;

namespace LazyGearCollector;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub). Both settings are plain config writes
/// followed by Save, as the collector window does. The hub calls from the game's main thread, the same thread
/// as the window and the ownership sweep, so the saved snapshot cache is not mutated underneath a save.
/// </summary>
internal static class HubAdapter
{
    private static readonly string[] Tiers = { "Base", "+1", "+2", "+3" };

    public static void Declare(HubEndpoint ep, Plugin plugin)
    {
        var c = plugin.Config;
        ep.Toggle("include_cached", "Count saddlebag and retainers", () => c.IncludeCachedContainers,
            v => { c.IncludeCachedContainers = v; c.Save(); }, group: "Counting",
            tip: "Include containers seen earlier (saddlebag, retainers) in the ownership counts.");
        ep.Choice("target_tier", "Target tier", Tiers, () => Math.Max(0, Math.Min(3, c.TargetTier)),
            i => { c.TargetTier = Math.Max(0, Math.Min(3, i)); c.Save(); }, group: "Counting",
            tip: "The upgrade tier the progress bars are measured against.");
    }
}
