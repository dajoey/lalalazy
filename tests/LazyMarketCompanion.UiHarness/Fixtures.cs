// Scenario data for the UI harness: real market item names, category names, retainer-style names and the
// plugin's own configuration shapes. Names are ordinary in-game item names of realistic length; the numeric
// ids and category ids are harness keys (the windows only print names; ids appear in hover tooltips).
using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;
using LazyMarketCompanion.Inventory;
using Lumina.Excel.Sheets;

/// <summary>One catalogue entry: what the Item sheet would say about it.</summary>
internal sealed record ItemDef(
  uint Id,
  string Name,
  uint Category,
  uint Stack,
  bool CanHq,
  bool Untradable = false,
  ArmourySlot Slot = ArmourySlot.None,
  byte Rarity = 1,
  uint Desynth = 0,
  uint UiCategory = 0)
{
  public Item ToSheetRow() => new(Id, Name, Untradable, CanHq, new SheetRef(Untradable ? 0u : Category));

  public ItemFacts ToFacts() => new(Id, UiCategory, Untradable ? 0u : Category, Untradable, false, Rarity, Slot, Desynth, 1, 1);
}

internal static class Fixtures
{
  // ---- market-board search categories (names as the market board prints them) ----
  public static readonly IReadOnlyDictionary<uint, string> Categories = new Dictionary<uint, string>
  {
    [34] = "Head", [35] = "Hands", [36] = "Feet", [37] = "Earrings",
    [43] = "Medicine", [45] = "Meals", [47] = "Stone", [48] = "Metal", [50] = "Cloth", [53] = "Reagents", [57] = "Materia",
  };

  // ---- the item catalogue (real in-game names; some deliberately long) ----
  public static readonly IReadOnlyList<ItemDef> Items =
  [
    new(39727, "Grade 8 Tincture of Strength", 43, 99, true),
    new(44158, "Grade 4 Gemdraught of Dexterity", 43, 99, true),
    new(44160, "Grade 4 Gemdraught of Mind", 43, 99, true),
    new(44164, "Grade 4 Gemdraught of Intelligence", 43, 99, true),
    new(4551, "Hi-Cordial", 43, 99, true),
    new(5111, "Iron Ore", 47, 999, false),
    new(5114, "Mythrite Ore", 47, 999, false),
    new(12537, "Titanium Ore", 47, 999, false),
    new(39931, "Ra'Kaznar Ore", 47, 999, false),
    new(12542, "Titanium Alloy Ingot", 48, 999, true),
    new(21798, "Palladium Ingot", 48, 999, true),
    new(44024, "Snow Cotton", 50, 999, false),
    new(41878, "Snow Cotton Ushanka of Scouting", 34, 1, true, Slot: ArmourySlot.Head, Rarity: 1, Desynth: 3),
    new(33160, "Diadochos Gloves of Aiming", 35, 1, true, Slot: ArmourySlot.Hands, Rarity: 3, Desynth: 90),
    new(33164, "Diadochos Earrings of Aiming", 37, 1, true, Slot: ArmourySlot.Ears, Rarity: 3, Desynth: 90),
    new(41300, "Savage Aim Materia XII", 57, 99, false),
    new(41302, "Heavens' Eye Materia XII", 57, 99, false),
    new(41308, "Quickarm Materia XII", 57, 99, false),
    new(5502, "Rroneek Steak", 45, 99, true),
    new(36110, "Aethersand of Fire", 53, 999, false),
    // Venture loot that cannot be listed (grey rows in the Inventory tab's venture section).
    new(2803, "Mythrite Sallet of Fending", 0, 1, false, Untradable: true, Slot: ArmourySlot.Head, Rarity: 2, Desynth: 15),
    new(2804, "Titanium Cuirass of Healing", 0, 1, false, Untradable: true, Slot: ArmourySlot.Body, Rarity: 2, Desynth: 22),
    new(2805, "Mythrite Gauntlets of Aiming", 0, 1, false, Untradable: true, Slot: ArmourySlot.Hands, Rarity: 2, Desynth: 18),
    new(2806, "Aetheryte Ticket", 0, 99, false, Untradable: true),
    new(2807, "Gold Saucer Voucher", 0, 99, false, Untradable: true),
    new(2808, "Raptorskin Boots of Gathering", 0, 1, false, Untradable: true, Slot: ArmourySlot.Feet, Rarity: 1, Desynth: 8),
  ];

  public static ItemDef Def(uint id) => Items.First(i => i.Id == id);

  public static string Name(uint id) => Items.FirstOrDefault(i => i.Id == id)?.Name ?? $"Unknown item ({id})";

  public static ItemFacts? Facts(uint id) => Items.FirstOrDefault(i => i.Id == id)?.ToFacts();

  // ---- retainer-style names (a plain word list; the longest is a full 19-character name) ----
  public static readonly string[] RetainerNames =
    ["Larkspur", "Ironbark", "Saltmarsh", "Thistledown", "Brindle", "Quillfeather", "Marrowbone", "Ashenfall Highmarket"];

  public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

  /// <summary>A last-seen stamp that reads as a stable "N h ago" for the hour or so after a run (offset sits mid-unit).</summary>
  public static long AgoMs(double hours) => NowMs - (long)(hours * 3_600_000d);

  // =================================================================================================
  // configuration states
  // =================================================================================================

  /// <summary>A fresh install: defaults only, nothing on any list, no retainer ever seen.</summary>
  public static Configuration FirstRun() => new() { EnablePostPinchkey = true };

  /// <summary>A populated install: a mixed Auto-Market list, price limits, routes and retainer selection.</summary>
  public static Configuration Typical()
  {
    var c = new Configuration
    {
      AutoMarketEnabled = true,
      AutoMarketMarkersEnabled = true,
      AutoMarketDuringAutoRetainer = true,
      AutoMarketSource = StockSource.BagsAndRetainer,
      AutoMarketReserveSlots = 2,
      AutoMarketValueGateEnabled = true,
      AutoMarketValueGateThresholdGil = 25_000,
      AutoMarketGateFreshnessHours = 6,
      UndercutMode = UndercutMode.FixedAmount,
      UndercutAmount = 1,
      MaxUndercutPercentage = 25f,
      UseUniversalisSaleHistoryFallback = true,
      SaleHistoryMaxAgeDays = 30,
      DecisionTelemetry = true,
      ImportedFromDagobert = true,
      EnablePinchKey = true,
      EnablePostPinchkey = true,
      DefaultAmount = 250,
      LastKnownRetainerNames = [.. RetainerNames],
      SeenRetainers = [1001UL, 1002UL, 1003UL, 1004UL, 1005UL, 1006UL, 1007UL, 1008UL],
    };
    // Retainer selection: three of the eight are skipped.
    foreach (var name in RetainerNames.Take(5))
      c.EnabledRetainerNames.Add(name);

    AutoMarketItem Add(uint id, bool hq, bool enabled = true, int stack = 0, int keepBags = 0, int keepRet = 0, int maxList = 0,
      StockSource? source = null, int fixedPrice = 0, bool skipRouting = false)
    {
      var entry = c.GetOrAddAutoMarketItem(id, hq);
      entry.Enabled = enabled;
      entry.StackSize = stack;
      entry.KeepInBags = keepBags;
      entry.KeepInRetainer = keepRet;
      entry.MaxListingsPerRetainer = maxList;
      entry.SourceOverride = source;
      entry.FixedPrice = fixedPrice;
      entry.ExcludeFromCategoryRouting = skipRouting;
      return entry;
    }

    Add(39727, true, stack: 99, keepBags: 5);
    Add(44158, true, stack: 99);
    Add(44160, false, enabled: false);
    Add(44164, true, stack: 50, maxList: 4);
    Add(5114, false, stack: 99, keepRet: 200);
    Add(12537, false, stack: 99, source: StockSource.RetainerOnly);
    Add(39931, false, stack: 99, fixedPrice: 1450);
    Add(12542, true, stack: 99, keepBags: 20);
    Add(44024, false, stack: 99, source: StockSource.BagsOnly);
    Add(41878, true, stack: 1, fixedPrice: 12_500, skipRouting: true);
    Add(41300, false, stack: 99);
    Add(41302, false, stack: 99, enabled: false);
    Add(5502, true, stack: 99, keepBags: 10);
    Add(36110, false, stack: 99, maxList: 6);

    foreach (var (id, min, max) in new (uint, int, int)[]
             { (39727, 4200, 9000), (12542, 380, 0), (44024, 0, 800), (41878, 9500, 18_000), (5114, 55, 0), (41302, 120_000, 400_000) })
      c.ItemPriceLimits.Add(new ItemPriceLimit { ItemId = id, MinPrice = min, MaxPrice = max });

    c.CategoryRetainerRules.Add(new CategoryRetainerRule { CategoryId = 43, RetainerName = "Larkspur" });
    c.CategoryRetainerRules.Add(new CategoryRetainerRule { CategoryId = 57, RetainerName = "Ashenfall Highmarket" });
    c.CategoryRetainerRules.Add(new CategoryRetainerRule { CategoryId = 48, RetainerName = "Saltmarsh" });
    return c;
  }

  // =================================================================================================
  // inventory states (the stand-in InventoryService)
  // =================================================================================================

  private static ArSnapshot ArAvailable()
  {
    var plans = new List<ArEntrustPlan>
    {
      new("11111111-1111-1111-1111-111111111111", "Crafting stock", true, false,
        new HashSet<uint> { 5111, 12537 }, new HashSet<uint> { 47u }, false, true),
      new("22222222-2222-2222-2222-222222222222", "Gear dump", false, false,
        new HashSet<uint> { 41878 }, new HashSet<uint>(), true, false),
    };
    return new ArSnapshot
    {
      Available = true,
      Status = "ok",
      Plans = plans,
      PlanByRetainer = new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["Saltmarsh"] = "11111111-1111-1111-1111-111111111111",
        ["Brindle"] = "22222222-2222-2222-2222-222222222222",
      },
      Im = new ArImLists(
        new HashSet<uint> { 2806 }, new HashSet<uint>(), new HashSet<uint> { 2807 },
        new HashSet<uint> { 2808, 5502 }, new HashSet<uint>(), new HashSet<uint> { 2804 }, new HashSet<uint> { 41300 },
        99, 99, true, true),
      RecordStats = true,
    };
  }

  /// <summary>A character that has visited every retainer: last-seen pages, venture log, bags and a gear batch.</summary>
  public static InventoryService TypicalInventory()
  {
    var s = new InventoryService { Ar = ArAvailable(), Facts = Facts };
    s.ContainerSizes[0] = (35, 31);
    s.ContainerSizes[1] = (35, 35);
    s.ContainerSizes[2] = (35, 22);
    s.ContainerSizes[3] = (35, 9);
    foreach (var (slot, type) in InventoryService.ArmouryPages)
      s.ContainerSizes[(int)type] = slot == ArmourySlot.SoulCrystal ? (25, 4) : (35, slot is ArmourySlot.Head or ArmourySlot.Hands ? 33 : 17);

    var character = new CharacterInventoryState
    {
      Saddlebag = new ContainerSeen { SeenUnixMs = AgoMs(52), Size = 70, Used = 41 },
      PremiumSaddlebag = new ContainerSeen { SeenUnixMs = AgoMs(52), Size = 70, Used = 68 },
    };
    var fills = new (string Name, int Used, double Hours, bool Bell)[]
    {
      ("Larkspur", 118, 3.5, false), ("Ironbark", 171, 7.5, false), ("Saltmarsh", 64, 26.5, false),
      ("Thistledown", 140, 5.5, true), ("Brindle", 175, 52.0, false), ("Quillfeather", 12, 1.6, false),
      ("Marrowbone", 98, 130.0, false),
    };
    foreach (var (name, used, hours, bell) in fills)
    {
      var seen = new RetainerSeen
      {
        Name = name,
        PagesSeenUnixMs = bell ? 0 : AgoMs(hours),
        Capacity = SpaceMath.RetainerCapacity,
        Used = used,
        BellSeenUnixMs = bell ? AgoMs(hours) : 0,
        BellItemCount = bell ? used : -1,
      };
      character.Retainers[name] = seen;
    }
    // The eighth retainer has never been opened or seen at a bell.
    character.Retainers["Ashenfall Highmarket"] = new RetainerSeen { Name = "Ashenfall Highmarket" };

    // Larkspur's pages hold venture loot (all of it listed in the venture log below) plus mixed stock.
    var larkspur = character.Retainers["Larkspur"];
    var stacks = new (int C, int S, uint Id, bool Hq, int Q, bool X)[]
    {
      (10000, 2, 12537, false, 14, false), (10000, 5, 2803, false, 1, false), (10000, 6, 2804, false, 1, false),
      (10000, 9, 2805, false, 1, false), (10001, 0, 2808, false, 1, false), (10001, 3, 2806, false, 3, false),
      (10001, 7, 5114, false, 40, false), (10002, 1, 41878, true, 1, false),
    };
    foreach (var (c, sl, id, hq, q, x) in stacks)
      larkspur.Stacks.Add(new SeenStack { C = c, S = sl, I = id, H = hq, Q = q, X = x });
    larkspur.Used = larkspur.Stacks.Count;

    character.LastGearBatch =
    [
      new GearMoveRecord(41878, true, 0, 12, ArmourySlot.Head, 3201, 20),
      new GearMoveRecord(33160, false, 1, 3, ArmourySlot.Hands, 3203, 7),
      new GearMoveRecord(33164, false, 1, 4, ArmourySlot.Ears, 3207, 2),
    ];
    s.Character = character;

    // The venture log (AutoRetainer statistics): Quick Exploration rewards delivered in the last days.
    var nowSec = NowMs / 1000;
    s.Records["Larkspur"] =
    [
      new VentureRecord(12537, false, nowSec - 2 * 86_400, 14, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(2803, false, nowSec - 3 * 86_400, 1, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(2804, false, nowSec - 3 * 86_400, 1, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(2805, false, nowSec - 4 * 86_400, 1, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(2808, false, nowSec - 5 * 86_400, 1, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(2806, false, nowSec - 5 * 86_400, 3, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(5114, false, nowSec - 6 * 86_400, 12, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(41878, true, nowSec - 7 * 86_400, 1, ArVentureStats.QuickExplorationVentureId),
    ];
    s.Records["Ironbark"] =
    [
      new VentureRecord(5114, false, nowSec - 3_600, 9, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(5111, false, nowSec - 8 * 3_600, 11, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(36110, false, nowSec - 20 * 3_600, 7, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(12537, false, nowSec - 2 * 86_400, 8, ArVentureStats.QuickExplorationVentureId),
      new VentureRecord(5111, false, nowSec - 4 * 86_400, 10, ArVentureStats.QuickExplorationVentureId),
    ];

    // Bags: marketable stock on and off the list, an unmarketable pair and gear for the Armoury mover.
    var bags = new (int C, int S, uint Id, bool Hq, int Q)[]
    {
      (0, 0, 39727, true, 38), (0, 1, 39727, true, 61), (0, 2, 44158, true, 99), (0, 3, 44160, false, 27), (0, 4, 44164, true, 12),
      (0, 5, 5114, false, 412), (0, 6, 12537, false, 87), (0, 7, 39931, false, 54), (0, 8, 12542, true, 31), (0, 9, 21798, false, 140),
      (0, 10, 44024, false, 233), (0, 11, 41300, false, 6), (0, 12, 41878, true, 1), (0, 13, 41302, false, 4), (0, 14, 41308, false, 9),
      (0, 15, 5502, true, 18), (0, 16, 36110, false, 76), (0, 17, 4551, true, 24), (0, 18, 5111, false, 640),
      (0, 19, 2806, false, 7), (0, 20, 2807, false, 2), (1, 0, 33160, false, 1), (1, 1, 33164, false, 1),
    };
    s.Bags = [.. bags.Select(b => new BagStack(b.C, b.S, b.Id, b.Hq, b.Q))];
    s.ArmouryFreeMap = new Dictionary<ArmourySlot, int>
    {
      [ArmourySlot.Head] = 2, [ArmourySlot.Hands] = 2, [ArmourySlot.Ears] = 16, [ArmourySlot.Body] = 14,
    };
    s.Gearsets = new Dictionary<uint, List<int>> { [33164] = [3] };

    s.RecentLines =
    [
      InventoryActionLine.Batch(NowMs - 86_400_000L, "0.2.8.23", "gear", "start", 3, 0, 0, "manual"),
      InventoryActionLine.Move(NowMs - 86_399_000L, "move", "0.2.8.23", "Inventory1:12", "ArmoryHead:20", 41878, true, true, 0, "ok"),
      InventoryActionLine.Move(NowMs - 86_398_000L, "move", "0.2.8.23", "Inventory2:3", "ArmoryHands:7", 33160, false, true, 0, "ok"),
      InventoryActionLine.Move(NowMs - 86_397_000L, "move", "0.2.8.23", "Inventory2:4", "ArmoryEar:2", 33164, false, false, 5, "armoury page changed before the move"),
      InventoryActionLine.Batch(NowMs - 86_396_000L, "0.2.8.23", "gear", "end", 3, 2, 1, "first failure stops the run"),
    ];
    s.SettledRetainerName = string.Empty;
    s.LastIdle = new IdleVerdict(false, ["in a duty", "AutoRetainer is running"]);
    return s;
  }

  /// <summary>A character that has never opened a bell: nothing known, AutoRetainer absent.</summary>
  public static InventoryService BlankInventory()
  {
    var s = new InventoryService { Ar = ArSnapshot.Unavailable("AutoRetainer is not loaded"), Facts = Facts };
    s.LastIdle = new IdleVerdict(false, ["not checked yet"]);
    s.Character = new CharacterInventoryState();
    return s;
  }

}
