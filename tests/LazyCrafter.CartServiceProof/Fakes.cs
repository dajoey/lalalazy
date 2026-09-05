using LazyCrafter.Core;
using LazyCrafter.Core.Model;

// NOTE: this file deliberately compiles into namespaces the real CatalogService.cs imports, so the
// plugin source file builds UNMODIFIED in this plain-net10 harness. If CatalogService.cs grows a new
// Dalamud-facing dependency, grow the matching fake here. Block namespaces because one file feeds three.

namespace Dalamud.Plugin.Services
{
    public interface IPluginLog
    {
        void Debug(string message);
        void Debug(Exception ex, string message);
        void Debug(string template, params object[] values);
        void Information(string message);
        void Information(Exception ex, string message);
        void Information(string template, params object[] values);
        void Warning(string message);
        void Warning(Exception ex, string message);
        void Warning(string template, params object[] values);
        void Error(string message);
        void Error(Exception ex, string message);
        void Error(string template, params object[] values);
    }

    public interface IFramework
    {
        Task RunOnFrameworkThread(Action action);
        Task RunOnFrameworkThread(Func<Task> action);
        Task<T> RunOnFrameworkThread<T>(Func<T> func);
    }
}

namespace LazyCrafter.Adapters
{
    using Dalamud.Plugin.Services;

    public sealed class FakeFramework : IFramework
    {
        /// <summary>Runs the body synchronously on the CALLING thread (the offline stand-in for the framework hop).</summary>
        public Task RunOnFrameworkThread(Action action) { action(); return Task.CompletedTask; }
        public Task RunOnFrameworkThread(Func<Task> action) => action();
        public Task<T> RunOnFrameworkThread<T>(Func<T> func) => Task.FromResult(func());
    }

    /// <summary>
    /// The plugin as CatalogService sees it: config (cart + basis), player state, inventory, prices, game data.
    /// </summary>
    public sealed class FakePluginAdapter
    {
        public Configuration Config = new();
        public FakePlayerState Player = new();
        public FakeAllaganInventory Inventory = new();
        public FakeUniversalis Prices = new();
        public LuminaGameData? GameData;
        public Task GameDataLoad = Task.CompletedTask;
        public bool SavePluginConfig(Configuration config) { config.Saves++; return true; }
    }

    public sealed class FakePlayerState
    {
        public bool LoggedIn = true;
        public IReadOnlyDictionary<uint, int> Jobs = new Dictionary<uint, int> { [10] = 90 };
        public HashSet<uint> Complete = new();
        public IReadOnlyList<RetainerStats> Retainers = Array.Empty<RetainerStats>();
        public IReadOnlySet<uint>? GatheredItems;
        public bool IsRecipeComplete(uint id) => Complete.Contains(id);
        public IReadOnlyDictionary<uint, int> UnlockedJobs() => Jobs;
    }

    /// <summary>
    /// The harness's stand-in for AllaganInventory: count snapshot + degraded flag + the memo-drop that replaced
    /// the raising Invalidate on the dispatch path.
    /// </summary>
    public sealed class FakeAllaganInventory
    {
        public bool Degraded;
        public Func<IEnumerable<uint>, Dictionary<uint, int>> Counts = _ => new Dictionary<uint, int>();
        public int DropMemoCount;
        public Dictionary<uint, int> Snapshot(IEnumerable<uint> itemIds) => Counts(itemIds);
        public void DropMemo() => DropMemoCount++;
        public void Invalidate() { DropMemo(); Changed?.Invoke(); }
        public event Action? Changed;
    }

    /// <summary>
    /// Prices as the cart path exercises them. The 2m45s freeze window is simulated with <see cref="PrimeBlock"/>:
    /// while the real worker is inside Universalis rounds, the fake blocks inside IsStale so the worker cannot
    /// take another turn.
    /// </summary>
    public sealed class FakeUniversalis
    {
        public string Scope = "dc";
        public double Tax = 5;
        public Dictionary<uint, PriceQuote> Quotes = new();
        public ManualResetEventSlim? PrimeBlock;
        public Func<uint, bool>? IsStaleOverride;
        public PriceQuote? Get(uint itemId) => Quotes.TryGetValue(itemId, out var q) ? q : null;
        public bool IsStale(uint itemId)
        {
            PrimeBlock?.Wait();   // simulates being stuck inside the Universalis round
            return IsStaleOverride is { } f ? f(itemId) : false;
        }
    }

    /// <summary>The adapter CatalogService actually reaches through: forwards to the fakes.</summary>
    public sealed class PlayerState(FakePluginAdapter owner)
    {
        public bool IsLoggedIn => owner.Player.LoggedIn;
        public IReadOnlyDictionary<uint, int> UnlockedJobs() => owner.Player.UnlockedJobs();
        public bool IsRecipeComplete(uint id) => owner.Player.IsRecipeComplete(id);
        public IReadOnlyList<RetainerStats> Retainers => owner.Player.Retainers;
        public IReadOnlySet<uint>? GatheredItems => owner.Player.GatheredItems;
    }

    public sealed class AllaganInventory(FakePluginAdapter owner)
    {
        public bool Degraded => owner.Inventory.Degraded;
        public Dictionary<uint, int> Snapshot(IEnumerable<uint> ids) => owner.Inventory.Snapshot(ids);
        public void DropMemo() => owner.Inventory.DropMemo();
    }

    public sealed class UniversalisClient(FakePluginAdapter owner) : IPriceSource
    {
        public string Scope => owner.Prices.Scope;
        public double BestTaxPct => owner.Prices.Tax;
        public PriceQuote? Get(uint id) => owner.Prices.Get(id);
        public bool IsStale(uint id) => owner.Prices.IsStale(id);
        public int RequestsMade => 0;
        public Task<int> PrimeAsync(IEnumerable<uint> itemIds, CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>
    /// Real LuminaGameData only wraps an IGameData plus names; the subset CatalogService's cart path touches is
    /// small, so forward to the fake game data. All members read-only (the real one is filled before the catalog
    /// worker starts and never mutated afterwards).
    /// </summary>
    public sealed class LuminaGameData : IGameData
    {
        private readonly IGameData _data;
        public LuminaGameData(IGameData data) => _data = data;

        public string ItemName(uint itemId) => $"#{itemId}";
        public bool CanBeHq(uint itemId) => false;
        public bool IsDesynthable(uint itemId) => false;
        public string JobAbbr(uint jobId) => jobId.ToString();
        public IEnumerable<RecipeRow> Recipes() => _data.Recipes();
        public bool IsGilVendor(uint itemId, out uint gil) => _data.IsGilVendor(itemId, out gil);
        public bool IsSpecialShop(uint itemId) => _data.IsSpecialShop(itemId);
        public GatherInfo? Gather(uint itemId) => _data.Gather(itemId);
        public bool IsFish(uint itemId) => _data.IsFish(itemId);
        public IEnumerable<VentureRow> Ventures() => _data.Ventures();
        public bool IsMarketable(uint itemId) => _data.IsMarketable(itemId);
        public bool IsDrop(uint itemId) => _data.IsDrop(itemId);
        public CollectableInfo? Collectable(uint itemId) => _data.Collectable(itemId);
        public IReadOnlyList<DesynthResult> Desynth(uint itemId) => _data.Desynth(itemId);
    }
}

namespace LazyCrafter
{
    using LazyCrafter.Adapters;

    public sealed class Configuration
    {
        public RevenueBasis RevenueBasis { get; set; } = RevenueBasis.MinListing;
        public List<CartEntry> Cart { get; set; } = new();
        public sealed class CartEntry { public uint RecipeId; public int Crafts; }

        // The real config persists through the plugin interface; the harness records saves so a test can
        // assert the cart survives a "restart".
        public int Saves;
    }

    public sealed class Plugin
    {
        // The real entry point is heavily Dalamud; the harness's CatalogService uses it only for
        // Pi.SavePluginConfig and property forwarding. One static bridge to the current fake plugin.
        public static FakePluginAdapter? Pi;
        public Configuration Config => Pi!.Config;
        public PlayerState Player => new(Pi!);
        public AllaganInventory Inventory => new(Pi!);
        public UniversalisClient Prices => new(Pi!);
        public LuminaGameData? GameData => Pi!.GameData;
        public Task GameDataLoad => Pi!.GameDataLoad;
        public static bool SavePluginConfig(Configuration config) { config.Saves++; return true; }
    }
}
