// Stand-in for the game-side InventoryService: it reads no game memory, files or network. Every DECISION the
// Inventory tab and the owner tooltip draw (venture-loot buckets, gear-mover plan, who handles a stack, free-space
// maths) still runs through the real Dalamud-free cores in Inventory/Core; only the facts the service would gather
// (containers, bags, AutoRetainer snapshot, venture log, last-seen pages) are canned by the scenario.
using FFXIVClientStructs.FFXIV.Client.Game;
using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;

namespace LazyMarketCompanion.Inventory;

internal sealed class InventoryService
{
  internal static readonly (ArmourySlot Slot, InventoryType Type)[] ArmouryPages =
  [
    (ArmourySlot.MainHand, InventoryType.ArmoryMainHand), (ArmourySlot.OffHand, InventoryType.ArmoryOffHand),
    (ArmourySlot.Head, InventoryType.ArmoryHead), (ArmourySlot.Body, InventoryType.ArmoryBody),
    (ArmourySlot.Hands, InventoryType.ArmoryHands), (ArmourySlot.Legs, InventoryType.ArmoryLegs),
    (ArmourySlot.Feet, InventoryType.ArmoryFeets), (ArmourySlot.Ears, InventoryType.ArmoryEar),
    (ArmourySlot.Neck, InventoryType.ArmoryNeck), (ArmourySlot.Wrists, InventoryType.ArmoryWrist),
    (ArmourySlot.Rings, InventoryType.ArmoryRings), (ArmourySlot.SoulCrystal, InventoryType.ArmorySoulCrystal),
  ];

  public const long QuoteMaxAgeMs = 30 * 60_000L;
  public const int ManualMoveCap = 20;
  public const int IdleMoveCap = 10;
  public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

  // ---- the scenario's facts ----
  public ArSnapshot Ar { get; set; } = ArSnapshot.Unavailable("not read yet");
  public Func<uint, ItemFacts?> Facts { get; set; } = _ => null;
  public CharacterInventoryState? Character { get; set; }
  public Dictionary<string, List<VentureRecord>> Records { get; } = new(StringComparer.Ordinal);
  public Dictionary<int, (int Size, int Used)> ContainerSizes { get; } = [];
  public List<BagStack> Bags { get; set; } = [];
  public Dictionary<ArmourySlot, int> ArmouryFreeMap { get; set; } = [];
  public Dictionary<uint, List<int>> Gearsets { get; set; } = [];
  public IReadOnlyList<string> RecentLines { get; set; } = [];
  public IdleVerdict LastIdle { get; set; } = new(false, ["not checked yet"]);
  public string SettledRetainerName { get; set; } = string.Empty;
  public long IdleBackoffMinutesLeft { get; set; }
  public bool BatchRunning { get; set; }
  public string BatchStatus { get; set; } = string.Empty;
  public string? VendorBlockerText { get; set; } = "the retainer's inventory is not open";
  public string? MoveBlockerText { get; set; }
  public string ActionsPath { get; } = @"C:\Users\Player\AppData\Roaming\XIVLauncher\pluginConfigs\LazyMarketCompanion\" + InventoryActionLine.ActionsFileName;

  /// <summary>Retainer pages as read live when that retainer's inventory is open (otherwise the last-seen pages are used).</summary>
  public List<RetainerStack> LivePages { get; set; } = [];

  // ---- what the tab and the tooltip call ----
  public void Cancel() => BatchRunning = false;
  public void RequestArRefresh() { }
  public void SetOptIn(uint itemId, bool on, string rail) { }
  public void StartVendor(string retainer, IReadOnlyList<VendorOp> shown) { }
  public void StartGearMoves(string trigger) { }
  public void StartUndo() { }
  public string? VendorBlocker(string retainer) => VendorBlockerText;
  public string? MoveBlocker() => MoveBlockerText;
  public string SettledRetainer() => SettledRetainerName;
  public List<BagStack> ReadBags() => [.. Bags];
  public IReadOnlyList<GearMoveRecord> LastGearBatch => Character?.LastGearBatch ?? [];

  public (int Size, int Used) Container(InventoryType type)
    => ContainerSizes.TryGetValue((int)type, out var c) ? c : (0, 0);

  public Dictionary<ArmourySlot, int> ArmouryFree() => new(ArmouryFreeMap);

  public List<string> RetainerNames()
  {
    var names = new SortedSet<string>(StringComparer.Ordinal);
    if (Character is { } c)
      foreach (var n in c.Retainers.Keys) names.Add(n);
    foreach (var n in Ar.PlanByRetainer.Keys) names.Add(n);
    return [.. names];
  }

  public List<VentureRecord> VentureRecords(string retainer)
    => Records.TryGetValue(retainer, out var r) ? r : [];

  private List<AmEntry> AmEntries()
    => [.. Plugin.Configuration.AutoMarketItems.Select(e => new AmEntry(e.ItemId, e.HQ, e.Enabled, e.ExcludeFromCategoryRouting))];

  public List<Owner> Owners(uint itemId, bool hq)
  {
    var holdings = Character is { } c ? InventoryState.Holdings(c) : null;
    var input = new OwnershipInput(itemId, hq, Facts(itemId), AmEntries(), Plugin.Configuration.CategoryRetainerRules,
      Ar, holdings, Gearsets.TryGetValue(itemId, out var ids) ? ids : []);
    return StackOwnership.Resolve(input);
  }

  /// <summary>Mirrors the service's preview: live pages when that retainer is open, otherwise its last-seen pages; the real classifier decides.</summary>
  public LootPlan VenturePreview(string retainer, out bool live, bool fresh = false)
  {
    var cfg = Plugin.Configuration;
    live = CategoryRouter.RetainerNamesEqual(SettledRetainer(), retainer);
    List<RetainerStack> stacks;
    if (live)
      stacks = LivePages;
    else if (Character is { } c && c.Retainers.TryGetValue(retainer, out var seen))
      stacks = InventoryState.StacksOf(seen);
    else
      stacks = [];

    var input = new LootInput(retainer, stacks, VentureRecords(retainer), Facts, AmEntries(), Ar,
      new HashSet<uint>(Gearsets.Keys), Bags.Select(b => b.ItemId).ToHashSet(), null);
    var freshness = (long)Math.Clamp(cfg.AutoMarketGateFreshnessHours, 1, 168) * 3_600_000L;
    var opt = new LootOptions(cfg.VentureLootLookbackDays, cfg.VentureLootAllowHq, new HashSet<uint>(cfg.VentureLootOptIns),
      new GateOptions(cfg.AutoMarketValueGateEnabled, Math.Max(cfg.AutoMarketValueGateThresholdGil, 0), freshness),
      cfg.HQ, NowMs, 0, QuoteMaxAgeMs);
    return VentureLoot.Classify(input, opt);
  }

  public GearMovePlan GearPreview(int cap)
    => GearMover.Plan(ReadBags(), Facts, ArmouryFree(), (id, hq) => Owners(id, hq), new HashSet<uint>(Gearsets.Keys), cap);
}
