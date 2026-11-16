using System.Reflection;
using ArmoireAutoFill.Data.Shopping;
using ArmoireAutoFill.Models;
using Dalamud.Plugin;

// Game and runtime boundary only. Everything here replaces a class that reads game memory, Lumina sheets or the
// Dalamud service container; the UI sources and the pure shopping logic are the real files (see the csproj).

namespace ECommons.DalamudServices
{
    /// <summary>Stand-in for the ECommons service locator; only the plugin-interface IPC probe the window uses.</summary>
    internal static class Svc
    {
        public static HarnessPluginInterface PluginInterface { get; } = new();
    }

    internal sealed class HarnessPluginInterface
    {
        public HarnessIpcSubscriber GetIpcSubscriber<T>(string name) => new(ArmoireAutoFill.UiHarness.Sim.KnightshopperLoaded);
    }

    internal sealed class HarnessIpcSubscriber(bool hasFunction)
    {
        public bool HasFunction { get; } = hasFunction;
    }
}

namespace ArmoireAutoFill
{
    /// <summary>The plugin entry point: the real class owns services; the windows only read the configuration.</summary>
    public sealed class Plugin
    {
        public static Configuration Configuration { get; set; } = new();

        internal static IDalamudPluginInterface PluginInterface { get; } =
            DispatchProxy.Create<IDalamudPluginInterface, NoOpProxy>();
    }

    /// <summary>Answers every interface call with a default value so Configuration.Save() is a no-op.</summary>
    public class NoOpProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } rt } && rt != typeof(void) ? Activator.CreateInstance(rt) : null;
    }
}

namespace ArmoireAutoFill.Logic
{
    /// <summary>Scan results the Collection and Store pages read. Real class walks the inventory containers.</summary>
    public class InventoryScanner
    {
        /// <summary>How long ago the last scan ran; null means never scanned. Age is relative so "Ns ago" text is stable.</summary>
        public TimeSpan? ScanAge { get; set; }
        public DateTime LastScan => ScanAge is { } age ? DateTime.UtcNow - age : DateTime.MinValue;
        public int LastInventoryItemsSeen { get; set; }
        public int LastInventoryHits { get; set; }
        public int LastArmoireHits { get; set; }
        public IReadOnlySet<uint> LastOwnedItemIds { get; set; } = new HashSet<uint>();
        public void Scan() { }
    }

    /// <summary>Armoire contents as the server last reported them. Real class reads the cabinet unlock bits.</summary>
    public sealed class CabinetObserver
    {
        public HashSet<uint> ArmoireIds { get; set; } = [];
        public TimeSpan? SnapshotAge { get; set; }
        public DateTime LastSnapshot => SnapshotAge is { } age ? DateTime.UtcNow - age : DateTime.MinValue;
        public bool CabinetDataAvailable { get; set; }
        public IReadOnlyCollection<uint> CachedArmoireItemIds => ArmoireIds;
        public bool IsInArmoire(uint itemId) => ArmoireIds.Contains(itemId);
    }

    /// <summary>The store-to-armoire automation. Real class sends game packets; the page only reads its status.</summary>
    public sealed class ArmoireAutoStore
    {
        public bool IsStoring { get; set; }
        public int LastStoredCount { get; set; }
        public string LastResultMessage { get; set; } = string.Empty;
        public void StoreAll() { }
    }
}

namespace ArmoireAutoFill.Data
{
    /// <summary>Same public surface as the real database; the real one is built from Lumina sheets at login.</summary>
    public static class ArmoireGearDatabase
    {
        public static List<ArmoireItem> AllItems { get; set; } = [];
        public static List<DungeonInfo> DungeonSets { get; set; } = [];
        public static bool IsLoaded { get; set; }
        public static int TotalItems => AllItems.DistinctBy(i => i.ItemId).Count();
    }

    /// <summary>Same public surface as the real builder, whose state comes from reading shop sheets off-thread.</summary>
    public static class KnightshopperCatalogBuilder
    {
        public static bool IsLoaded { get; set; }
        public static CatalogSnapshot Snapshot { get; set; } = CatalogSnapshot.Empty;
        public static bool SheetsUnavailable { get; set; }
        public static bool BuildFailed { get; set; }
        public static string? BuildFailureDetail { get; set; }
        public static void RequestBuild() { }
        public static void RequestRefresh() { }
    }

    /// <summary>The real one reads the Cabinet and Item sheets and the player's quest and achievement state.</summary>
    public static class ArmoireShoppingInput
    {
        public static bool TryBuild(ArmoireAutoFill.Logic.InventoryScanner scanner, ArmoireAutoFill.Logic.CabinetObserver cabinet,
            out ShoppingListBuilder.Input input)
        {
            input = ArmoireAutoFill.UiHarness.Sim.ShoppingInput!;
            return input is not null && scanner.LastScan != DateTime.MinValue;
        }
    }
}
