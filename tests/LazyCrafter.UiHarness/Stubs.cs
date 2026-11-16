// Stubs for the game/runtime boundary only. Everything the windows draw from is the real source (window, chrome,
// panels, shared UI kit, Core, catalog records, configuration); these classes stand in for the services the plugin
// entry point wires to the client: the catalog worker, the dispatcher and its hand-offs, game data sheets, the price
// client, the inventory bridge and the player state. Each one exposes exactly the members the UI reads and holds
// plain settable state that Scenarios.cs fills in.
using LazyCrafter.Adapters;
using LazyCrafter.Adapters.Dispatch;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter
{
    /// <summary>Chat output the UI writes to on a click; the harness never clicks, so it only records.</summary>
    public sealed class HarnessChat
    {
        public List<string> Lines { get; } = [];
        public void Print(string message) => Lines.Add(message);
        public void PrintError(string message) => Lines.Add("ERROR " + message);
    }

    public sealed class HarnessFramework
    {
        public void RunOnFrameworkThread(Action action) { }
    }

    /// <summary>Stand-in for the plugin entry point: only the members the UI reads.</summary>
    public sealed class Plugin
    {
        internal static HarnessChat ChatGui { get; } = new();
        internal static HarnessFramework Framework { get; } = new();

        public Configuration Config { get; set; } = new();
        public CatalogService Catalog { get; } = new();
        public DispatchService Dispatch { get; } = new();
        public LuminaGameData? GameData { get; set; } = new();
        public Task GameDataLoad { get; set; } = Task.CompletedTask;
        public UniversalisClient Prices { get; } = new();
        public PlayerState Player { get; } = new();
        public AllaganInventory Inventory { get; } = new();

        public void SaveConfig() { }
    }
}

namespace LazyCrafter.Catalog
{
    public sealed class CatalogService
    {
        public CatalogSnapshot Snapshot { get; set; } = CatalogSnapshot.Empty;
        public CatalogView View { get; set; } = CatalogView.Empty;
        public string Status { get; set; } = "";
        public string? LastError { get; set; }
        public bool Busy { get; set; }
        public Dictionary<uint, long> UnitCosts { get; } = [];

        public void Invalidate() { }
        public void Request(ViewRequest request) { }
        public void Pin(uint recipeId) { }
        public void RefreshPrices() { }
        public void AddToCart(uint recipeId, int crafts) { }
        public void SetCartQuantity(uint recipeId, int crafts) { }
        public void RemoveFromCart(uint recipeId) { }
        public void ClearCart() { }
        public long? UnitCost(uint itemId) => UnitCosts.TryGetValue(itemId, out var cost) ? cost : null;
    }
}

namespace LazyCrafter.Adapters
{
    public sealed class LuminaGameData
    {
        public Dictionary<uint, string> Names { get; } = [];
        public Dictionary<uint, string> Jobs { get; } = [];
        public List<string> Failures { get; } = [];
        public int DropCount { get; set; }
        public int DesynthSourceCount { get; set; }

        public IReadOnlyList<string> SupplementalFailures => Failures;
        public string ItemName(uint itemId) => Names.TryGetValue(itemId, out var name) ? name : $"#{itemId}";
        public string JobAbbr(uint jobId) => Jobs.TryGetValue(jobId, out var abbr) ? abbr : jobId.ToString();
        public IReadOnlyList<SpecialShopOffer> SpecialShopOffers(uint itemId) => Array.Empty<SpecialShopOffer>();
    }

    public sealed class UniversalisClient
    {
        public string Scope { get; set; } = "";
        public bool ScopeIsWorld { get; set; }
        public int CacheSize { get; set; }
        public double BestTaxPct { get; set; } = 5;
        public int RequestsMade { get; set; }
        public int Failures { get; set; }
    }

    public sealed class PlayerState
    {
        public static readonly uint[] CrafterJobs = [8, 9, 10, 11, 12, 13, 14, 15];

        public string HomeWorldName { get; set; } = "";
        public string DataCenterName { get; set; } = "";
        public string? RetainerHint { get; set; }
        public IReadOnlyList<RetainerStats> Retainers { get; set; } = [];
    }

    public sealed class AllaganInventory
    {
        public bool Degraded { get; set; }
        public Dictionary<uint, int> InBags { get; } = [];
        public Dictionary<uint, List<StoredElsewhere>> Stored { get; } = [];

        public int CountInBags(uint itemId) => InBags.TryGetValue(itemId, out var count) ? count : 0;
        public IReadOnlyList<StoredElsewhere> StoredWhere(uint itemId) =>
            Stored.TryGetValue(itemId, out var places) ? places : Array.Empty<StoredElsewhere>();
        public int? CurrencyBalance(uint itemId) => null;
        public void Invalidate() { }
    }

    /// <summary>Same shape as the real guard: only the pin record and the install/override reads the Settings page uses.</summary>
    public sealed class ReflectionGuard
    {
        public enum MemberKind { Field, Property, Method, StaticProperty, StaticMethod, TypeOnly }
        public sealed record Member(string Type, string Name, MemberKind Kind, Type[]? Parameters = null);
        public sealed record Pin(string InternalName, Version MinVersion, Version MaxVerified, string VerifiedAgainst, IReadOnlyList<Member> Members);

        public Dictionary<string, Version> Installed { get; } = [];
        public HashSet<string> NotLoaded { get; } = [];
        public Dictionary<string, Version> OverridesMap { get; } = [];

        public IReadOnlyDictionary<string, Version> Overrides => OverridesMap;
        public Version? InstalledVersion(string internalName, out bool loaded)
        {
            loaded = Installed.ContainsKey(internalName) && !NotLoaded.Contains(internalName);
            return Installed.TryGetValue(internalName, out var version) ? version : null;
        }
    }

    public sealed class VendorLocator
    {
        public sealed record Location(uint ItemId, uint NpcId, string NpcName, uint TerritoryId, string TerritoryName, uint MapId, Vector2 MapCoords,
            uint AetheryteId, string AetheryteName, Vector2 AetheryteMapCoords, float MapDistance);

        public List<string> Failures { get; } = [];
        public IReadOnlyList<string> SupplementalFailures => Failures;

        public IReadOnlyList<(Location Where, IReadOnlyList<(uint ItemId, int Quantity)> Items)> Plan(
            IReadOnlyList<(uint ItemId, int Quantity)> wanted, out IReadOnlyList<(uint ItemId, int Quantity)> unlocated, VendorContext? context = null)
        {
            unlocated = wanted;
            return [];
        }

        public IReadOnlyList<SpecialShopCandidate> SpecialShopCandidates(uint itemId, IReadOnlyList<SpecialShopOffer> offers) =>
            Array.Empty<SpecialShopCandidate>();
    }

    public sealed class DispatchService
    {
        public enum Phase { Idle, Retrieve, WaitRetrieve, Ventures, Gathers, WaitGather, Crafts, WaitClientFree, WaitCraftStart, WaitCraftEnd, Knightshopper, Done, Failed, BatchRetrieve, BatchWait, Blocked }

        public ArtisanDispatch Artisan { get; } = new();
        public LifestreamDispatch Lifestream { get; } = new();
        public PriceMatchDispatch PriceMatch { get; } = new();
        public ReflectionGuard Guard { get; } = new();
        public VendorLocator Vendors { get; } = new();
        public VendorLocator? VendorsIfBuilt { get; set; }
        public Phase Current { get; set; } = Phase.Idle;
        public string Status { get; set; } = "idle";
        public bool Running => Current is not (Phase.Idle or Phase.Done or Phase.Failed or Phase.Blocked);
        public RunSnapshot Snapshot { get; set; } = RunSnapshot.Empty;
        public bool CanResume => Snapshot.CanResume;

        public void Stop(string why = "stopped by user") { }
        public bool Resume() => false;
        public void DispatchCart() { }
        public DispatchPlan.Plan? PlanFor() => null;
        public VendorContext Here() => VendorContext.Unknown;
        public RecipeRow? RecipeFor(uint itemId, uint preferJob) => null;
        public void CraftOne(uint recipeId, int crafts) { }
        public void GatherOne(uint itemId, int quantity) { }
        public void VentureOne(uint itemId, int quantity) { }
        public void RetrieveOne(uint itemId, int quantity) { }
        public void VendorOne(uint itemId, int quantity) { }
        public void MarketOne(uint itemId, int quantity) { }
    }
}

namespace LazyCrafter.Adapters.Dispatch
{
    public sealed class ArtisanDispatch { public bool Installed { get; set; } = true; }
    public sealed class PriceMatchDispatch { public bool Installed { get; set; } = true; }

    public sealed class LifestreamDispatch
    {
        public bool Installed { get; set; } = true;
        public string? GoToMarket(IReadOnlyList<(uint ItemId, int Quantity)> items, Func<uint, string> itemName, Func<uint, long?> unitPrice, bool teleport = true, Func<uint, string>? also = null) => null;
        public string? GoToVendor(VendorLocator.Location where, IReadOnlyList<(uint ItemId, int Quantity)> items, Func<uint, string> itemName, bool teleport = true) => null;
        public string? GoToCurrencyShop(uint territoryId, uint mapId, float mapX, float mapY, string what, bool teleport = false, uint aetheryteId = 0, string aetheryteName = "") => null;
    }

    // The pins carry the same internal names, version windows and verified-against notes as the real hand-offs; the
    // member lists are sized like the real ones because only their count reaches a tooltip.
    public static class GbrDispatch
    {
        public static readonly ReflectionGuard.Pin Pin = new("GatherBuddyReborn", new Version(7, 5, 0), new Version(7, 6, 0),
            "GBR 7.5.0 source; installed 7.5.5", Members());
        internal static IReadOnlyList<ReflectionGuard.Member> Members() => Enumerable.Range(0, 9).Select(i => new ReflectionGuard.Member("", "m" + i, ReflectionGuard.MemberKind.Field)).ToList();
    }

    public static class ArcDispatch
    {
        public static readonly ReflectionGuard.Pin Pin = new("ARControl", new Version(8, 5), new Version(8, 8),
            "ARC 8.6 source and tag 8.5", GbrDispatch.Members());
    }

    public static class RetainerFetch
    {
        public static readonly ReflectionGuard.Pin Pin = new("Artisan", new Version(4, 0, 5), new Version(4, 1),
            "Artisan 4.0.5.19", GbrDispatch.Members());
    }
}
