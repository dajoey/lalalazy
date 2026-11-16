using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

// Game/runtime boundary of the Skyward tracker window. Everything the window reads from the live game goes through
// these stand-ins; the window and chrome sources themselves are compiled unchanged.

namespace Dalamud.Interface
{
    /// <summary>Shadows the Dalamud type of the same name: the real one needs a running Dalamud for the icon font.</summary>
    public static class UiBuilder
    {
        public static ImFontPtr IconFont { get; set; }
    }
}

namespace LazySkywardTracker
{
    public sealed class HarnessClientState
    {
        public bool IsLoggedIn { get; set; }
    }

    /// <summary>
    /// Stand-in for the plugin entry point. The achievement table below mirrors Plugin.cs; Checks.cs parses
    /// Plugin.cs and fails when the two drift.
    /// </summary>
    public sealed class Plugin
    {
        internal static HarnessClientState ClientState { get; } = new();

        public static readonly uint[] AchievementIds = [2491, 2494, 2497, 2500, 2503, 2506, 2509, 2512, 2515, 2518, 2521];

        public static readonly Dictionary<uint, (string Job, string AchName)> SkywardAchievements = new()
        {
            { 2491, ("Carpenter", "Skyward Saw III") },
            { 2494, ("Blacksmith", "Skyward Smithy III") },
            { 2497, ("Armorer", "Skyward Hammer III") },
            { 2500, ("Goldsmith", "Skyward Gemstone III") },
            { 2503, ("Leatherworker", "Skyward Knife III") },
            { 2506, ("Weaver", "Skyward Needle III") },
            { 2509, ("Alchemist", "Skyward Science III") },
            { 2512, ("Culinarian", "Skyward Skillet III") },
            { 2515, ("Miner", "Skyward Sledgehammer III") },
            { 2518, ("Botanist", "Skyward Scythe III") },
            { 2521, ("Fisher", "Skyward Rod III") },
        };

        public static readonly Dictionary<uint, (uint Current, uint Max)> ProgressCache = new();

        /// <summary>Achievement ids the fake game reports as completed (the real one reads a bitfield).</summary>
        public static readonly HashSet<uint> Completed = new();

        /// <summary>False when the progress hook never came up; the real value is set by Plugin.cs, scenarios set it here.</summary>
        public static bool IsTracking { get; set; } = true;

        /// <summary>How many times the window asked for an achievement's completion; the window must read each job once per frame.</summary>
        public static int CompletedReads { get; private set; }

        public static bool IsAchievementCompleted(uint achievementId)
        {
            CompletedReads++;
            return Completed.Contains(achievementId);
        }

        public InventoryScanner Scanner { get; } = new();

        public int RefreshRequests { get; private set; }

        public void RequestAllProgress() => RefreshRequests++;
    }

    /// <summary>Stand-in for the inventory scan: returns whatever the scenario set up. The projection shapes are the real ones.</summary>
    public sealed class InventoryScanner
    {
        public static readonly Dictionary<uint, InventoryProjection> Projections = new();

        public sealed class InventoryProjection
        {
            public uint TotalPoints { get; set; }
            public List<ItemBreakdown> Items { get; } = new();
        }

        public readonly record struct ItemBreakdown(string ItemName, uint Quantity, uint PerItemPoints, uint TotalPoints);

        public int Invalidations { get; private set; }

        public Dictionary<uint, InventoryProjection> ScanInventory() => Projections;

        public void InvalidateCache() => Invalidations++;
    }
}
