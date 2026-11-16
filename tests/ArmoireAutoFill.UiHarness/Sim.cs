using System.Text.Json;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Data.Shopping;
using ArmoireAutoFill.Logic;
using ArmoireAutoFill.Models;

namespace ArmoireAutoFill.UiHarness;

/// <summary>
/// Fixture data (real game names and shop rows, see Fixtures) and the fake game state the scenarios put the stubs in.
/// Ownership and player progress are synthesized from item ids so every run draws the same pixels.
/// </summary>
internal static class Sim
{
    public static bool KnightshopperLoaded;
    public static ShoppingListBuilder.Input? ShoppingInput;
    public static InventoryScanner Scanner = new();
    public static CabinetObserver Cabinet = new();
    public static ArmoireAutoStore AutoStore = new();

    private sealed record Dungeon(uint Cfc, string Name, byte Level, (uint Id, GearSlot Slot)[] Items);

    private static readonly Lazy<Fixture> Data = new(Fixture.Load);

    private sealed class Fixture
    {
        public uint[] ItemIds = [];
        public Dictionary<uint, string> Names = [];
        public List<Dungeon> Dungeons = [];
        public List<ShopEntry> Catalog = [];
        public Dictionary<uint, string> Npcs = [];
        public (int Unlinked, int Unplaced, int Gil, int Underlisted, int LeftOut) Counts;
        public List<LeftOutPiece> LeftOut = [];

        public static Fixture Load()
        {
            using var stream = typeof(Fixture).Assembly.GetManifestResourceStream("armoire-real-data.json")
                               ?? throw new InvalidOperationException("The embedded real-data fixture is missing.");
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            var f = new Fixture { ItemIds = [.. root.GetProperty("itemIds").EnumerateArray().Select(e => e.GetUInt32())] };
            var names = root.GetProperty("itemNames").EnumerateArray().Select(e => e.GetString()!).ToArray();
            for (var i = 0; i < f.ItemIds.Length; i++)
                f.Names[f.ItemIds[i]] = names[i];
            foreach (var d in root.GetProperty("dungeons").EnumerateArray())
                f.Dungeons.Add(new Dungeon(d.GetProperty("cfc").GetUInt32(), d.GetProperty("name").GetString()!,
                    (byte)d.GetProperty("level").GetInt32(),
                    [.. d.GetProperty("items").EnumerateArray().Select(i => (i[0].GetUInt32(), (GearSlot)i[1].GetInt32()))]));
            foreach (var e in root.GetProperty("catalog").EnumerateArray())
            {
                var price = e[5].GetInt64();
                f.Catalog.Add(new ShopEntry(e[0].GetUInt32(), e[1].GetUInt32(), e[2].GetUInt32(), e[3].GetInt32(),
                    (byte)e[4].GetInt32(), price < 0 ? null : (uint)price, e[6].GetUInt32(), e[7].GetUInt32(), (ShopSource)e[8].GetInt32()));
            }
            foreach (var p in root.GetProperty("npcNames").EnumerateObject())
                f.Npcs[uint.Parse(p.Name)] = p.Value.GetString()!;
            var s = root.GetProperty("snapshot");
            f.Counts = (s.GetProperty("skippedUnlinkedShops").GetInt32(), s.GetProperty("skippedUnplacedVendors").GetInt32(),
                s.GetProperty("skippedGilSpecialShops").GetInt32(), s.GetProperty("underlistedItemCount").GetInt32(),
                s.GetProperty("leftOutItemCount").GetInt32());
            var reasons = root.GetProperty("leftOutReasons").EnumerateArray().Select(e => e.GetString()!).ToArray();
            foreach (var l in root.GetProperty("leftOut").EnumerateArray())
                f.LeftOut.Add(new LeftOutPiece(l[0].GetUInt32(), reasons[l[1].GetInt32()]));
            return f;
        }
    }

    /// <summary>Every name the UI can draw from the fixture, for the glyph audit.</summary>
    public static IEnumerable<string> FixtureStrings()
    {
        var f = Data.Value;
        return f.Names.Values.Concat(f.Dungeons.Select(d => d.Name)).Concat(f.Npcs.Values);
    }

    // Deterministic 0..99 per id and salt.
    private static int Roll(uint id, uint salt) => (int)(((ulong)(id ^ salt) * 2654435761UL >> 7) % 100);

    private static IReadOnlySet<uint> CompleteDungeonIds(Fixture f)
        => f.Dungeons.Where(d => d.Items.Length <= 20).SelectMany(d => d.Items.Select(i => i.Id)).ToHashSet();

    private enum Look { FirstRun, Typical, WaitingCabinet, AllComplete }

    // ---- scenario state -----------------------------------------------------------------------------------------

    public static void Reset()
    {
        KnightshopperLoaded = false;
        ShoppingInput = null;
        Scanner = new InventoryScanner();
        Cabinet = new CabinetObserver();
        AutoStore = new ArmoireAutoStore();
        Plugin.Configuration = new Configuration();
        ArmoireGearDatabase.AllItems = [];
        ArmoireGearDatabase.DungeonSets = [];
        ArmoireGearDatabase.IsLoaded = false;
        KnightshopperCatalogBuilder.IsLoaded = false;
        KnightshopperCatalogBuilder.SheetsUnavailable = false;
        KnightshopperCatalogBuilder.BuildFailed = false;
        KnightshopperCatalogBuilder.BuildFailureDetail = null;
        KnightshopperCatalogBuilder.Snapshot = CatalogSnapshot.Empty;
    }

    /// <summary>The gear database is built but nothing has been scanned and no cabinet data has arrived (first login).</summary>
    public static void FirstRun() => Apply(Look.FirstRun);

    /// <summary>A scanned inventory, a live cabinet snapshot, most pieces owned, a few dungeons complete.</summary>
    public static void Typical() => Apply(Look.Typical);

    /// <summary>Scanned, but the server has not sent cabinet data yet (Blocked).</summary>
    public static void WaitingForCabinet() => Apply(Look.WaitingCabinet);

    /// <summary>Every dungeon in the database is complete.</summary>
    public static void AllComplete() => Apply(Look.AllComplete);

    public static void GearDatabaseMissing()
    {
        Typical();
        ArmoireGearDatabase.IsLoaded = false;
        ArmoireGearDatabase.AllItems = [];
        ArmoireGearDatabase.DungeonSets = [];
        Scanner.LastInventoryHits = 0; // the real scanner counts hits against the database
        Scanner.LastArmoireHits = 0;
    }

    private static void Apply(Look look)
    {
        var f = Data.Value;
        var complete = CompleteDungeonIds(f);
        var dungeons = new List<DungeonInfo>();
        foreach (var d in f.Dungeons)
        {
            var info = new DungeonInfo { ContentFinderConditionId = d.Cfc, Name = d.Name, Level = d.Level };
            foreach (var (id, slot) in d.Items)
                info.Items.Add(new ArmoireItem
                {
                    ItemId = id,
                    Name = f.Names[id],
                    Slot = slot,
                    DungeonName = d.Name,
                    ContentFinderConditionId = d.Cfc,
                    Owned = look switch
                    {
                        Look.FirstRun => OwnershipStatus.NotOwned,
                        Look.AllComplete => Roll(id, 5) < 10 ? OwnershipStatus.InInventory : OwnershipStatus.InArmoire,
                        Look.WaitingCabinet => Roll(id, 5) < 14 ? OwnershipStatus.InInventory : OwnershipStatus.NotOwned,
                        _ => complete.Contains(id) || Roll(id, 1) < 80
                            ? (Roll(id, 5) < 10 ? OwnershipStatus.InInventory : OwnershipStatus.InArmoire)
                            : OwnershipStatus.NotOwned,
                    },
                });
            dungeons.Add(info);
        }

        ArmoireGearDatabase.DungeonSets = dungeons;
        ArmoireGearDatabase.AllItems = [.. dungeons.SelectMany(d => d.Items)];
        ArmoireGearDatabase.IsLoaded = true;

        var distinct = ArmoireGearDatabase.AllItems.DistinctBy(i => i.ItemId).ToList();
        var inArmoire = distinct.Where(i => i.Owned == OwnershipStatus.InArmoire).Select(i => i.ItemId).ToHashSet();
        var inInventory = distinct.Where(i => i.Owned == OwnershipStatus.InInventory).Select(i => i.ItemId).ToHashSet();
        if (look == Look.FirstRun)
            return;

        Scanner.ScanAge = TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(20);
        Scanner.LastInventoryItemsSeen = 214;
        Scanner.LastInventoryHits = inInventory.Count;
        Scanner.LastArmoireHits = inArmoire.Count;
        Scanner.LastOwnedItemIds = inInventory;
        if (look == Look.WaitingCabinet)
            return;

        Cabinet.CabinetDataAvailable = true;
        Cabinet.SnapshotAge = TimeSpan.FromSeconds(42);
        Cabinet.ArmoireIds = inArmoire;
        Scanner.LastArmoireHits = inArmoire.Count;
    }

    // ---- shopping -------------------------------------------------------------------------------------------------

    /// <summary>The catalog build finished: real shop rows, real vendor names, the real skip accounting.</summary>
    public static void CatalogBuilt(bool knightshopperLoaded)
    {
        var f = Data.Value;
        KnightshopperLoaded = knightshopperLoaded;
        KnightshopperCatalogBuilder.IsLoaded = true;
        KnightshopperCatalogBuilder.Snapshot = new CatalogSnapshot(f.Catalog, f.Npcs, f.Counts.Unlinked, f.Counts.Unplaced,
            f.Counts.Gil, f.Counts.Underlisted, f.Counts.LeftOut, f.LeftOut);

        // Ownership over the whole armoire (the Shopping page works on every Cabinet item, not only dungeon drops).
        var fromDungeons = ArmoireGearDatabase.AllItems.GroupBy(i => i.ItemId)
            .ToDictionary(g => g.Key, g => g.First().Owned);
        var entries = new List<ShoppingListBuilder.ArmouryEntry>();
        var owned = new HashSet<uint>();
        foreach (var id in f.ItemIds)
        {
            var ownership = fromDungeons.TryGetValue(id, out var o)
                ? o switch
                {
                    OwnershipStatus.InArmoire => ShoppingListBuilder.Ownership.InArmoire,
                    OwnershipStatus.InInventory => ShoppingListBuilder.Ownership.InInventory,
                    _ => ShoppingListBuilder.Ownership.NotOwned,
                }
                : Roll(id, 9) switch
                {
                    < 86 => ShoppingListBuilder.Ownership.InArmoire,
                    < 90 => ShoppingListBuilder.Ownership.InInventory,
                    _ => ShoppingListBuilder.Ownership.NotOwned,
                };
            entries.Add(new ShoppingListBuilder.ArmouryEntry(id, ownership));
            if (ownership != ShoppingListBuilder.Ownership.NotOwned)
                owned.Add(id);
        }

        ShoppingInput = new ShoppingListBuilder.Input(entries, owned, f.Catalog, f.Names,
            new PlayerUnlockState(q => q % 4 != 0, a => a % 3 != 0));
    }

    /// <summary>Every armoire piece is owned, so the Shopping page reports nothing to shop for.</summary>
    public static void NothingMissing()
    {
        CatalogBuilt(true);
        var all = ShoppingInput!.ArmoireItems.Select(e => e with { Owned = ShoppingListBuilder.Ownership.InArmoire }).ToList();
        ShoppingInput = ShoppingInput with { ArmoireItems = all };
    }

    public static void CatalogBuilding() { /* Reset() already leaves the builder neither loaded nor failed. */ }

    public static void CatalogSheetsUnavailable()
    {
        KnightshopperCatalogBuilder.SheetsUnavailable = true;
        KnightshopperCatalogBuilder.BuildFailed = true; // the real builder marks the gate failed when sheets are missing
        KnightshopperCatalogBuilder.BuildFailureDetail = "game data sheets are not available";
    }

    public static void CatalogBuildFailed()
    {
        KnightshopperCatalogBuilder.BuildFailed = true;
        KnightshopperCatalogBuilder.BuildFailureDetail = "InvalidOperationException: no NPC placements were readable from the game's layer files, so Knightshopper's vendors cannot be matched";
    }

    /// <summary>The text the real "Copy import code" button would put under the shopping list.</summary>
    public static string CopiedMessage()
    {
        var group = new ShoppingResultProbe().FirstCurrency()
                    ?? throw new InvalidOperationException("The fixture produced no shopping list.");
        return KnightshopperInstructions.CopiedMessage(group.CurrencyId, group.Items.Count, $"Armoire fill ({group.CurrencyName})");
    }

    private sealed class ShoppingResultProbe
    {
        public CurrencyCandidates? FirstCurrency()
            => ShoppingListBuilder.Build(ShoppingInput!).Currencies.OrderByDescending(c => c.Items.Count).FirstOrDefault();
    }

    // ---- store ------------------------------------------------------------------------------------------------------

    public static void StoreRunning()
    {
        Typical();
        AutoStore.IsStoring = true;
        Scanner.ScanAge = TimeSpan.FromMilliseconds(200); // inside the 2 s scan cooldown: "Scan cooldown: 2s left."
    }

    public static void StoreFinished()
    {
        Typical();
        Cabinet.CabinetDataAvailable = false; // armoire UI not open: the requirement row says "Open at an inn"
        AutoStore.LastStoredCount = 12;
        AutoStore.LastResultMessage = "Stored 12 items to the armoire (3 skipped: in a gearset).";
        Plugin.Configuration.AutoStoreIncludeArmory = true;
    }
}
