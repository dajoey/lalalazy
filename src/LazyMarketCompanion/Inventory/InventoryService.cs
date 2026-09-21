using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lalalazy.Telemetry;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;

namespace LazyMarketCompanion.Inventory;

/// <summary>
/// Game-side half of the Inventory tab. Reads (containers, Item sheet, gearsets, AutoRetainer's files), keeps
/// the last-seen sidecar, and runs the tab's two LIVE actions - vendoring venture loot through the open
/// retainer, and moving gear from the bags to the Armoury Chest - one op per step on the framework thread,
/// every op re-verified against the live container immediately before it fires. Every decision lives in
/// Inventory/Core (Dalamud-free, harness-pinned); this class only gathers facts and executes.
/// </summary>
internal sealed class InventoryService : IDisposable
{
  private static readonly InventoryType[] BagTypes =
    [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

  private static readonly InventoryType[] RetainerPageTypes =
  [
    InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
    InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
  ];

  internal static readonly (ArmourySlot Slot, InventoryType Type)[] ArmouryPages =
  [
    (ArmourySlot.MainHand, InventoryType.ArmoryMainHand), (ArmourySlot.OffHand, InventoryType.ArmoryOffHand),
    (ArmourySlot.Head, InventoryType.ArmoryHead), (ArmourySlot.Body, InventoryType.ArmoryBody),
    (ArmourySlot.Hands, InventoryType.ArmoryHands), (ArmourySlot.Legs, InventoryType.ArmoryLegs),
    (ArmourySlot.Feet, InventoryType.ArmoryFeets), (ArmourySlot.Ears, InventoryType.ArmoryEar),
    (ArmourySlot.Neck, InventoryType.ArmoryNeck), (ArmourySlot.Wrists, InventoryType.ArmoryWrist),
    (ArmourySlot.Rings, InventoryType.ArmoryRings), (ArmourySlot.SoulCrystal, InventoryType.ArmorySoulCrystal),
  ];

  /// <summary>Prices checked longer ago than this never make a stack actionable.</summary>
  public const long QuoteMaxAgeMs = 30 * 60_000L;

  /// <summary>Moves per batch: a manual press, and one idle pass.</summary>
  public const int ManualMoveCap = 20;
  public const int IdleMoveCap = 10;

  private readonly MarketAutomation _automation;
  private readonly UniversalisPriceProvider _prices = new();
  private readonly TelemetryGuard _tickGuard = LalaTelemetry.CreateGuard("inventory.tick", "inventory tab upkeep");
  private readonly object _logLock = new();
  private readonly string _configDir;
  private readonly string _arDir;
  private readonly string _statePath;
  private readonly string _actionsPath;
  private readonly string _version;

  private InventoryState _state;
  private bool _stateDirty;
  private long _stateSaveAt;

  // AutoRetainer file snapshots (published from background reads).
  private volatile ArSnapshot _ar = ArSnapshot.Unavailable("not read yet");
  private DateTime _arMtime = DateTime.MinValue;
  private ulong _arCid;
  private long _arNextCheck;
  private int _arLoading;
  private readonly Dictionary<string, (DateTime Mtime, List<VentureRecord> Records)> _stats = new(StringComparer.Ordinal);
  private int _statsLoading;
  private long _statsNextCheck;
  private ulong _statsCid;

  // Caches (framework thread only).
  private readonly Dictionary<uint, ItemFacts?> _facts = new();
  private Dictionary<uint, List<int>> _gearsets = new();
  private long _gearsetsAt;
  private List<AmEntry> _am = [];
  private long _amAt;

  // Quotes for the venture-loot gate.
  private Dictionary<uint, ItemQuote>? _quotes;
  private long _quotesFetchedMs;
  private int _quotesLoading;
  private string _quotesStatus = "not checked";
  private CancellationTokenSource? _quotesCts;

  // Retainer-session settle tracking (own copy; never shares the routing mover's statics).
  private string _settleName = string.Empty;
  private long _settleNameSince;
  private long _settleFp;
  private long _settleFpSince;
  private long _savedFp;
  private string _savedName = string.Empty;
  private long _nextBellRead;
  private long _nextSaddleRead;

  // The running batch, if any.
  private Batch? _batch;
  private readonly IdleDebounce _idleDebounce = new(20_000, 60_000);
  private long _nextIdleCheck;
  private IdleVerdict _lastIdle = new(false, ["not checked yet"]);
  private readonly List<string> _recentLines = [];

  public InventoryService(MarketAutomation automation)
  {
    _automation = automation;
    _configDir = Plugin.PluginInterface.ConfigDirectory.FullName;
    _arDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.Parent?.FullName ?? _configDir, "AutoRetainer");
    _statePath = Path.Combine(_configDir, InventoryState.FileName);
    _actionsPath = Path.Combine(_configDir, InventoryActionLine.ActionsFileName);
    _version = Plugin.PluginInterface.Manifest.AssemblyVersion?.ToString() ?? "0";
    _state = LoadState();
    Svc.Framework.Update += OnUpdate;
  }

  public void Dispose()
  {
    Svc.Framework.Update -= OnUpdate;
    if (_batch != null)
      EndBatch("plugin unloading");
    _quotesCts?.Cancel();
    _quotesCts?.Dispose();
    _prices.Dispose();
    SaveState(force: true);
  }

  // =====================================================================================
  // Public surface for the tab and the tooltip
  // =====================================================================================

  public ArSnapshot Ar => _ar;
  public string ActionsPath => _actionsPath;
  public IReadOnlyList<string> RecentLines => _recentLines;
  public bool BatchRunning => _batch != null;
  public string BatchStatus => _batch == null ? string.Empty : $"{_batch.What}: {_batch.Index}/{_batch.Count} ({_batch.Trigger})";
  public IdleVerdict LastIdle => _lastIdle;
  public string QuotesStatus => _quotesLoading != 0 ? "checking prices..." : _quotesStatus;
  public long QuotesFetchedMs => _quotesFetchedMs;
  public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

  public ulong ContentId => Svc.PlayerState.ContentId;

  public CharacterInventoryState? Character => ContentId == 0 ? null : _state.For(ContentId);

  public ItemFacts? Facts(uint id)
  {
    if (id == 0)
      return null;
    if (_facts.TryGetValue(id, out var cached))
      return cached;
    ItemFacts? facts = null;
    var sheet = Svc.Data.GetExcelSheet<Item>();
    if (sheet != null && sheet.TryGetRow(id, out var row))
    {
      var slot = ArmourySlot.None;
      if (row.EquipSlotCategory.ValueNullable is { } e)
      {
        ReadOnlySpan<sbyte> cols = [e.MainHand, e.OffHand, e.Head, e.Body, e.Gloves, e.Waist, e.Legs, e.Feet, e.Ears, e.Neck, e.Wrists, e.FingerL, e.FingerR, e.SoulCrystal];
        slot = ArmourySlots.FromEquipSlotCategory(cols);
      }
      facts = new ItemFacts(id, row.ItemUICategory.RowId, row.ItemSearchCategory.RowId, row.IsUntradable, row.IsUnique,
        row.Rarity, slot, (uint)row.Desynth, row.PriceLow, row.PriceMid);
    }
    _facts[id] = facts;
    return facts;
  }

  private Dictionary<string, IReadOnlySet<uint>>? _holdings;
  private long _holdingsAt;

  private Dictionary<string, IReadOnlySet<uint>>? Holdings()
  {
    var now = Environment.TickCount64;
    if (now - _holdingsAt < 2000)
      return _holdings;
    _holdingsAt = now;
    _holdings = Character is { } c ? InventoryState.Holdings(c) : null;
    return _holdings;
  }

  public List<Owner> Owners(uint itemId, bool hq)
  {
    var holdings = Holdings();
    var gs = Gearsets();
    var input = new OwnershipInput(itemId, hq, Facts(itemId), AmEntries(), Plugin.Configuration.CategoryRetainerRules,
      _ar, holdings, gs.TryGetValue(itemId, out var ids) ? ids : []);
    return StackOwnership.Resolve(input);
  }

  /// <summary>Retainer names known from any source, sorted.</summary>
  public List<string> RetainerNames()
  {
    var names = new SortedSet<string>(StringComparer.Ordinal);
    if (Character is { } c)
      foreach (var n in c.Retainers.Keys) names.Add(n);
    foreach (var n in _ar.PlanByRetainer.Keys) names.Add(n);
    return names.ToList();
  }

  public List<VentureRecord> VentureRecords(string retainer)
    => _stats.TryGetValue(retainer, out var s) ? s.Records : [];

  /// <summary>The live retainer whose pages are loaded and settled, or empty.</summary>
  public string SettledRetainer()
  {
    var now = Environment.TickCount64;
    if (string.IsNullOrEmpty(_settleName) || now - _settleNameSince < 2000 || now - _settleFpSince < 1000)
      return string.Empty;
    return _settleName;
  }

  private readonly Dictionary<string, (long At, long QuotesAt, LootPlan Plan, bool Live)> _previews = new(StringComparer.Ordinal);

  /// <summary>The venture-loot plan for a retainer: live pages when that retainer is open and settled, otherwise
  /// its last-seen pages. Cached for a second for the tab; the vendor button always asks for a fresh one.</summary>
  public LootPlan VenturePreview(string retainer, out bool live, bool fresh = false)
  {
    var now = Environment.TickCount64;
    if (!fresh && _previews.TryGetValue(retainer, out var cached) && now - cached.At < 1000 && cached.QuotesAt == _quotesFetchedMs)
    {
      live = cached.Live;
      return cached.Plan;
    }
    var plan = BuildVenturePreview(retainer, out live);
    _previews[retainer] = (now, _quotesFetchedMs, plan, live);
    return plan;
  }

  private LootPlan BuildVenturePreview(string retainer, out bool live)
  {
    var cfg = Plugin.Configuration;
    live = CategoryRouter.RetainerNamesEqual(SettledRetainer(), retainer);
    List<RetainerStack> stacks;
    if (live)
      stacks = ReadRetainerPages();
    else if (Character is { } c && c.Retainers.TryGetValue(retainer, out var seen))
      stacks = InventoryState.StacksOf(seen);
    else
      stacks = [];

    var bags = ReadBags();
    var gs = Gearsets();
    var input = new LootInput(retainer, stacks, VentureRecords(retainer), Facts, AmEntries(), _ar,
      new HashSet<uint>(gs.Keys), bags.Select(b => b.ItemId).ToHashSet(), _quotes);
    var freshness = (long)Math.Clamp(cfg.AutoMarketGateFreshnessHours, 1, 168) * 3_600_000L;
    var opt = new LootOptions(cfg.VentureLootLookbackDays, cfg.VentureLootAllowHq, new HashSet<uint>(cfg.VentureLootOptIns),
      new GateOptions(cfg.AutoMarketValueGateEnabled, Math.Max(cfg.AutoMarketValueGateThresholdGil, 0), freshness),
      cfg.HQ, NowMs, _quotesFetchedMs, QuoteMaxAgeMs);
    return VentureLoot.Classify(input, opt);
  }

  private (long At, GearMovePlan Plan)? _gearPreview;

  /// <summary>The gear mover's plan for the live bags (cap 0 = uncapped, cached a second for the tab). A batch
  /// always plans fresh.</summary>
  public GearMovePlan GearPreview(int cap)
  {
    var now = Environment.TickCount64;
    if (cap == 0 && _gearPreview is { } g && now - g.At < 1000)
      return g.Plan;
    var gs = Gearsets();
    var plan = GearMover.Plan(ReadBags(), Facts, ArmouryFree(), (id, hq) => Owners(id, hq), new HashSet<uint>(gs.Keys), cap);
    if (cap == 0)
      _gearPreview = (now, plan);
    return plan;
  }

  public IReadOnlyList<GearMoveRecord> LastGearBatch => Character?.LastGearBatch ?? [];

  // =====================================================================================
  // Space at a glance
  // =====================================================================================

  public unsafe (int Size, int Used) Container(InventoryType type)
  {
    var m = InventoryManager.Instance();
    var c = m == null ? null : m->GetInventoryContainer(type);
    if (c == null || !c->IsLoaded)
      return (0, 0);
    var used = 0;
    for (var i = 0; i < c->Size; i++)
    {
      var it = c->GetInventorySlot(i);
      if (it != null && it->ItemId != 0) used++;
    }
    return ((int)c->Size, used);
  }

  public Dictionary<ArmourySlot, int> ArmouryFree()
  {
    var free = new Dictionary<ArmourySlot, int>();
    foreach (var (slot, type) in ArmouryPages)
    {
      var (size, used) = Container(type);
      if (size > 0)
        free[slot] = SpaceMath.Free(size, used);
    }
    return free;
  }

  // =====================================================================================
  // Prices (Universalis) for the venture-loot gate
  // =====================================================================================

  public void CheckPrices(IReadOnlyCollection<uint> itemIds)
  {
    if (Interlocked.CompareExchange(ref _quotesLoading, 1, 0) != 0)
      return;
    var ids = itemIds.Distinct().ToList();
    if (ids.Count == 0)
    {
      _quotesLoading = 0;
      _quotesStatus = "nothing to price";
      return;
    }
    _quotesCts?.Cancel();
    _quotesCts?.Dispose();
    _quotesCts = new CancellationTokenSource();
    var token = _quotesCts.Token;
    _ = Task.Run(async () =>
    {
      Dictionary<uint, ItemQuote>? q = null;
      string status;
      try
      {
        q = await _prices.GetRuleQuotes(ids, token).ConfigureAwait(false);
        status = q == null ? "price check failed (nothing is vendored until it succeeds)" : $"{q.Count} of {ids.Count} item(s) priced";
      }
      catch (OperationCanceledException)
      {
        status = "price check cancelled";
      }
      catch (Exception ex)
      {
        LalaTelemetry.Swallowed("inventory.prices", ex, $"{ids.Count} ids");
        status = "price check failed (nothing is vendored until it succeeds)";
      }
      await Svc.Framework.RunOnFrameworkThread(() =>
      {
        if (q != null)
        {
          _quotes = q;
          _quotesFetchedMs = NowMs;
        }
        _quotesStatus = status;
        _quotesLoading = 0;
      }).ConfigureAwait(false);
    }, token);
  }

  // =====================================================================================
  // Opt-ins (unique / untradable / rare rail)
  // =====================================================================================

  public void SetOptIn(uint itemId, bool on, string rail)
  {
    var list = Plugin.Configuration.VentureLootOptIns;
    var changed = on ? !list.Contains(itemId) : list.Remove(itemId);
    if (on && changed)
      list.Add(itemId);
    if (!changed)
      return;
    Plugin.Configuration.Save();
    Emit(InventoryActionLine.OptIn(NowMs, _version, itemId, on, rail));
  }

  // =====================================================================================
  // LIVE action 1: vendor venture loot through the OPEN retainer
  // =====================================================================================

  /// <summary>Why the vendor button is disabled for this retainer right now, or null when it may run.</summary>
  public string? VendorBlocker(string retainer)
  {
    if (_batch != null) return "another Inventory action is running";
    if (_automation.IsBusy || _automation.ActiveAutoRetainerSession != null) return "LMC's retainer automation is running";
    var ar = BusyOf("AutoRetainer", "AutoRetainer.PluginState.IsBusy", false);
    if (ar is PluginBusy.Busy or PluginBusy.Unknown) return ar == PluginBusy.Busy ? "AutoRetainer is busy" : "AutoRetainer's busy state cannot be read";
    if (!AutoMarketService.IsRetainerInventoryOpen()) return "open this retainer's inventory at the bell first";
    if (!CategoryRouter.RetainerNamesEqual(SettledRetainer(), retainer)) return "this retainer's inventory is not the one open (or it is still loading)";
    if (_quotesFetchedMs <= 0 || NowMs - _quotesFetchedMs > QuoteMaxAgeMs) return "check prices first (at most 30 minutes old)";
    return null;
  }

  /// <summary>
  /// Starts vendoring. Only ops that are STILL actionable on a fresh classification of the live pages AND
  /// were in the preview the player pressed the button on are sold; each one is re-read again right before
  /// the sell call (AutoMarketService.ExecuteVendor). The first failure stops the batch.
  /// </summary>
  public void StartVendor(string retainer, IReadOnlyList<VendorOp> shown)
  {
    var blocker = VendorBlocker(retainer);
    if (blocker != null)
    {
      Emit(InventoryActionLine.Batch(NowMs, _version, "vendor", "refused", shown.Count, 0, 0, blocker));
      return;
    }
    var fresh = VentureLoot.VendorOps(VenturePreview(retainer, out var live, fresh: true));
    if (!live)
    {
      Emit(InventoryActionLine.Batch(NowMs, _version, "vendor", "refused", shown.Count, 0, 0, "retainer pages not live"));
      return;
    }
    var shownKeys = shown.Select(o => (o.Container, o.Slot, o.ItemId, o.HQ, o.Quantity)).ToHashSet();
    var ops = fresh.Where(o => shownKeys.Contains((o.Container, o.Slot, o.ItemId, o.HQ, o.Quantity))).ToList();
    if (ops.Count == 0)
    {
      Emit(InventoryActionLine.Batch(NowMs, _version, "vendor", "refused", shown.Count, 0, 0, "nothing still qualifies on a fresh read"));
      return;
    }

    var batch = new Batch("vendor", "manual", ops.Count) { Retainer = retainer };
    batch.Vendor.AddRange(ops);
    Begin(batch);
  }

  private void TickVendor(Batch b, long now)
  {
    if (b.Index >= b.Vendor.Count)
    {
      EndBatch("done");
      return;
    }
    var blocker = _automation.IsBusy || _automation.ActiveAutoRetainerSession != null ? "LMC's retainer automation started"
      : BusyOf("AutoRetainer", "AutoRetainer.PluginState.IsBusy", false) is PluginBusy.Busy or PluginBusy.Unknown ? "AutoRetainer is busy or unreadable"
      : !AutoMarketService.IsRetainerInventoryOpen() ? "the retainer inventory closed"
      : !CategoryRouter.RetainerNamesEqual(AutoMarketService.CurrentRetainerName(), b.Retainer) ? "a different retainer is open"
      : null;
    if (blocker != null)
    {
      EndBatch("stopped: " + blocker);
      return;
    }

    var op = b.Vendor[b.Index];
    // The retainer sells the WHOLE slot, so the slot must hold exactly what was judged: same item, same
    // quality, same quantity. ExecuteVendor re-reads too, but only refuses a SMALLER stack and ignores HQ.
    if (!SlotHoldsExactly(op.Container, op.Slot, op.ItemId, op.HQ, op.Quantity))
    {
      Emit(InventoryActionLine.Vendor(NowMs, _version, b.Retainer, op.ContainerName() + ":" + op.Slot, op.ItemId, op.HQ, op.Quantity, op.EstGil, false,
        "slot no longer holds exactly the judged stack - not sold"));
      b.Index++;
      b.Fail++;
      EndBatch("stopped: a slot changed since the preview (stop-on-failure)");
      return;
    }
    var ok = AutoMarketService.ExecuteVendor(op);
    Emit(InventoryActionLine.Vendor(NowMs, _version, b.Retainer, op.ContainerName() + ":" + op.Slot, op.ItemId, op.HQ, op.Quantity, op.EstGil, ok,
      ok ? "venture loot below the value gate" : "slot changed or sell refused (see log)"));
    b.Index++;
    if (ok)
      b.Ok++;
    else
    {
      b.Fail++;
      EndBatch("stopped on the first failed sale (stop-on-failure)");
      return;
    }
    b.NextAt = now + 400;
  }

  // =====================================================================================
  // LIVE action 2: bags -> Armoury Chest gear mover (+ undo)
  // =====================================================================================

  /// <summary>Why the gear mover may not run right now, or null.</summary>
  public string? MoveBlocker()
  {
    if (_batch != null) return "another Inventory action is running";
    var v = Idle(includeOwnBatch: false);
    return v.Idle ? null : string.Join("; ", v.Why);
  }

  public void StartGearMoves(string trigger)
  {
    var blocker = MoveBlocker();
    var plan = GearPreview(trigger == "idle" ? IdleMoveCap : ManualMoveCap);
    if (blocker != null)
    {
      if (trigger != "idle")
        Emit(InventoryActionLine.Batch(NowMs, _version, "move", "refused", plan.Ops.Count, 0, 0, blocker));
      return;
    }
    if (plan.Ops.Count == 0)
      return;
    var b = new Batch("move", trigger, plan.Ops.Count);
    foreach (var op in plan.Ops)
      b.Moves.Add(new MoveStep(op.SrcContainer, op.SrcSlot, (int)TypeOf(op.Target), -1, op.ItemId, op.Hq, op.Target, null));
    Begin(b);
  }

  public void StartUndo()
  {
    var c = Character;
    var blocker = MoveBlocker();
    if (c == null || c.LastGearBatch.Count == 0)
      return;
    if (blocker != null)
    {
      Emit(InventoryActionLine.Batch(NowMs, _version, "undo", "refused", c.LastGearBatch.Count, 0, 0, blocker));
      return;
    }
    // Pieces no longer where the batch put them (equipped, moved by hand, sold) leave the undo list for good.
    var stillThere = c.LastGearBatch.Where(r => ReadSlot(r.ArmouryContainer, r.ArmouryIndex) is { } now && now.ItemId == r.ItemId && now.Hq == r.Hq).ToList();
    if (stillThere.Count != c.LastGearBatch.Count)
    {
      c.LastGearBatch = stillThere;
      MarkDirty();
    }
    var plan = GearMover.PlanUndo(c.LastGearBatch, ReadSlot, EmptyBagSlots());
    foreach (var n in plan.Notes)
      Svc.Log.Information("[LMC] inventory undo: " + n);
    if (plan.Ops.Count == 0)
    {
      Emit(InventoryActionLine.Batch(NowMs, _version, "undo", "refused", c.LastGearBatch.Count, 0, 0, plan.Notes.FirstOrDefault() ?? "nothing left to undo"));
      return;
    }
    var b = new Batch("undo", "manual", plan.Ops.Count);
    foreach (var u in plan.Ops)
      b.Moves.Add(new MoveStep(u.Record.ArmouryContainer, u.Record.ArmouryIndex, u.DstContainer, u.DstSlot, u.Record.ItemId, u.Record.Hq, u.Record.Target, u.Record));
    Begin(b);
  }

  private unsafe void TickMove(Batch b, long now)
  {
    if (b.Index >= b.Moves.Count)
    {
      EndBatch("done");
      return;
    }
    var step = b.Moves[b.Index];
    var m = InventoryManager.Instance();
    if (m == null)
    {
      EndBatch("stopped: inventory unavailable");
      return;
    }

    if (step.FiredAt == 0)
    {
      // Direction guard: a move only ever goes bags -> Armoury, an undo only Armoury -> bags. Anything else is
      // a planner bug and is refused before any game call.
      var isArmourySrc = ArmouryPages.Any(p => (int)p.Type == step.SrcContainer);
      var isArmouryDst = ArmouryPages.Any(p => (int)p.Type == step.DstContainer);
      var directionOk = b.What == "undo"
        ? isArmourySrc && GearMover.IsBagContainer(step.DstContainer)
        : GearMover.IsBagContainer(step.SrcContainer) && isArmouryDst;
      if (!directionOk)
      {
        Emit(InventoryActionLine.Move(NowMs, b.What, _version, InventoryActionLine.Slot(step.SrcContainer, step.SrcSlot), InventoryActionLine.Slot(step.DstContainer, step.DstSlot), step.ItemId, step.Hq, false, -4, "refused: wrong container direction (planner bug)"));
        EndBatch("stopped: refused a move in the wrong direction");
        return;
      }

      // Gate re-checked before EVERY move, for the manual press as well as the idle pass.
      var v = Idle(includeOwnBatch: false);
      if (!v.Idle)
      {
        EndBatch("stopped: " + string.Join("; ", v.Why));
        return;
      }
      var src = m->GetInventorySlot((InventoryType)step.SrcContainer, step.SrcSlot);
      if (src == null || src->ItemId != step.ItemId || src->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) != step.Hq)
      {
        Emit(InventoryActionLine.Move(NowMs, b.What, _version, InventoryActionLine.Slot(step.SrcContainer, step.SrcSlot), "-", step.ItemId, step.Hq, false, -1, "slot changed since planning - skipped"));
        b.Fail++;
        b.Index++;
        b.NextAt = now + 100;
        return;
      }
      var dstType = (InventoryType)step.DstContainer;
      var dstSlot = step.DstSlot >= 0 && SlotEmpty(dstType, step.DstSlot) ? step.DstSlot : FirstEmpty(b.What == "undo" ? BagTypes : [dstType], out dstType);
      if (dstSlot < 0)
      {
        Emit(InventoryActionLine.Move(NowMs, b.What, _version, InventoryActionLine.Slot(step.SrcContainer, step.SrcSlot), "-", step.ItemId, step.Hq, false, -2, "no empty destination slot"));
        EndBatch("stopped: destination full");
        return;
      }
      // Same call and flag as the routing mover (AutoMarketService.ExecuteRoutingMove): a literal EMPTY
      // destination slot, never an occupied one (an occupied slot swaps the stacks instead of moving).
      var rc = m->MoveItemSlot((InventoryType)step.SrcContainer, (ushort)step.SrcSlot, dstType, (ushort)dstSlot, true);
      step.DstContainer = (int)dstType;
      step.DstSlot = dstSlot;
      step.Rc = rc;
      step.FiredAt = now;
      if (rc != 0)
      {
        Emit(InventoryActionLine.Move(NowMs, b.What, _version, InventoryActionLine.Slot(step.SrcContainer, step.SrcSlot), InventoryActionLine.Slot(step.DstContainer, dstSlot), step.ItemId, step.Hq, false, rc, "move refused by the game"));
        EndBatch($"stopped: move refused (rc={rc})");
        return;
      }
      b.NextAt = now + 250;
      return;
    }

    // Verify: the source is empty and the destination holds the item. Up to ~2.5 s for the server.
    var srcNow = m->GetInventorySlot((InventoryType)step.SrcContainer, step.SrcSlot);
    var dstNow = m->GetInventorySlot((InventoryType)step.DstContainer, step.DstSlot);
    var landed = (srcNow == null || srcNow->ItemId == 0)
      && dstNow != null && dstNow->ItemId == step.ItemId && dstNow->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == step.Hq;
    if (!landed && now - step.FiredAt < 2500)
    {
      b.NextAt = now + 250;
      return;
    }

    Emit(InventoryActionLine.Move(NowMs, b.What, _version, InventoryActionLine.Slot(step.SrcContainer, step.SrcSlot), InventoryActionLine.Slot(step.DstContainer, step.DstSlot),
      step.ItemId, step.Hq, landed, step.Rc, landed ? (b.What == "undo" ? "moved back to the bags" : $"to Armoury {ArmourySlots.Label(step.Target)} ({b.Trigger})") : "did not land"));
    if (!landed)
    {
      b.Fail++;
      EndBatch("stopped: a move did not land");
      return;
    }

    b.Ok++;
    if (b.What == "move")
      b.Records.Add(new GearMoveRecord(step.ItemId, step.Hq, step.SrcContainer, step.SrcSlot, step.Target, step.DstContainer, step.DstSlot));
    else if (step.Undoes != null)
      b.Records.Add(step.Undoes);
    b.Index++;
    b.NextAt = now + 300;
  }

  // =====================================================================================
  // Batches
  // =====================================================================================

  private sealed class MoveStep(int srcContainer, int srcSlot, int dstContainer, int dstSlot, uint itemId, bool hq, ArmourySlot target, GearMoveRecord? undoes)
  {
    public int SrcContainer { get; } = srcContainer;
    public int SrcSlot { get; } = srcSlot;
    public int DstContainer { get; set; } = dstContainer;
    public int DstSlot { get; set; } = dstSlot;
    public uint ItemId { get; } = itemId;
    public bool Hq { get; } = hq;
    public ArmourySlot Target { get; } = target;
    public GearMoveRecord? Undoes { get; } = undoes;
    public long FiredAt { get; set; }
    public int Rc { get; set; }
  }

  private sealed class Batch(string what, string trigger, int count)
  {
    public string What { get; } = what;
    public string Trigger { get; } = trigger;
    public int Count { get; } = count;
    public string Retainer { get; init; } = string.Empty;
    public List<VendorOp> Vendor { get; } = [];
    public List<MoveStep> Moves { get; } = [];
    public List<GearMoveRecord> Records { get; } = [];
    public int Index { get; set; }
    public int Ok { get; set; }
    public int Fail { get; set; }
    public long NextAt { get; set; }
  }

  private void Begin(Batch b)
  {
    _batch = b;
    Emit(InventoryActionLine.Batch(NowMs, _version, b.What, "begin", b.Count, 0, 0, b.Trigger + (b.Retainer.Length > 0 ? " @ " + b.Retainer : string.Empty)));
  }

  public void Cancel() => EndBatch("cancelled by the player");

  private void EndBatch(string why)
  {
    var b = _batch;
    if (b == null)
      return;
    _batch = null;
    Emit(InventoryActionLine.Batch(NowMs, _version, b.What, "end", b.Count, b.Ok, b.Fail, why));

    var c = Character;
    if (c != null && b.Records.Count > 0)
    {
      if (b.What == "move")
      {
        c.LastGearBatch = b.Records.ToList();
        c.LastGearBatchUnixMs = NowMs;
      }
      else if (b.What == "undo")
      {
        c.LastGearBatch = c.LastGearBatch.Where(r => !b.Records.Contains(r)).ToList();
      }
      MarkDirty();
    }

    if (b.Ok > 0 || b.Fail > 0)
      Communicator.PrintInfo($"Inventory: {b.What} {b.Ok}/{b.Count} done{(b.Fail > 0 ? $", {b.Fail} failed" : string.Empty)} - {why}.");
  }

  // =====================================================================================
  // Framework tick
  // =====================================================================================

  private void OnUpdate(Dalamud.Plugin.Services.IFramework _)
  {
    if (!_tickGuard.TryEnter())
      return;
    try
    {
      var now = Environment.TickCount64;
      if (!Svc.ClientState.IsLoggedIn)
      {
        if (_batch != null)
          EndBatch("stopped: logged out");
        return;
      }

      if (_batch is { } b && now >= b.NextAt)
      {
        if (b.What == "vendor") TickVendor(b, now);
        else TickMove(b, now);
      }

      TrackRetainerSession(now);
      ReadBellCounts(now);
      ReadSaddlebags(now);
      RefreshArFiles(now);
      IdleMover(now);

      if (_stateDirty && now >= _stateSaveAt)
        SaveState(force: false);
    }
    catch (Exception ex)
    {
      _tickGuard.Failed(ex);
      if (_batch != null)
        EndBatch("stopped: error (see log)");
    }
  }

  private void IdleMover(long now)
  {
    if (now < _nextIdleCheck)
      return;
    _nextIdleCheck = now + 1000;
    _lastIdle = Idle(includeOwnBatch: true);
    // Idle mode only: never move pieces while the player has the bags or the Armoury Chest open (they may be
    // mid-drag). A manual press is the player's own act and is not held back by this.
    if (_lastIdle.Idle && InventoryWindowOpen())
      _lastIdle = new IdleVerdict(false, ["the inventory or Armoury Chest window is open"]);
    if (!Plugin.Configuration.GearMoverWhenIdle)
    {
      _idleDebounce.Reset();
      return;
    }
    if (_idleDebounce.Update(_lastIdle.Idle, now))
      StartGearMoves("idle");
  }

  private static unsafe bool InventoryWindowOpen()
  {
    foreach (var name in new[] { "Inventory", "InventoryLarge", "InventoryExpansion", "ArmouryBoard" })
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var a) && a->IsVisible)
        return true;
    }
    return false;
  }

  /// <summary>The idle gate's facts, read live.</summary>
  public IdleVerdict Idle(bool includeOwnBatch)
  {
    var cond = Svc.Condition;
    bool Any(params ConditionFlag[] flags) => flags.Any(f => cond[f]);
    var facts = new IdleFacts(
      Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer != null,
      cond[ConditionFlag.InCombat],
      Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95, ConditionFlag.InDeepDungeon),
      Any(ConditionFlag.Crafting, ConditionFlag.PreparingToCraft, ConditionFlag.ExecutingCraftingAction),
      Any(ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction, ConditionFlag.Fishing),
      Any(ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78),
      Any(ConditionFlag.Occupied, ConditionFlag.Occupied30, ConditionFlag.Occupied33, ConditionFlag.Occupied38, ConditionFlag.Occupied39,
        ConditionFlag.OccupiedInEvent, ConditionFlag.OccupiedInQuestEvent, ConditionFlag.OccupiedSummoningBell, ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51, ConditionFlag.TradeOpen, ConditionFlag.LoggingOut, ConditionFlag.Unconscious, ConditionFlag.MeldingMateria,
        ConditionFlag.BeingMoved, ConditionFlag.CreatingCharacter),
      _automation.IsBusy || _automation.ActiveAutoRetainerSession != null || (includeOwnBatch && _batch != null),
      BusyOf("AutoRetainer", "AutoRetainer.PluginState.IsBusy", false),
      BusyOf("AutoDuty", "AutoDuty.IsStopped", true),
      BusyOf("Artisan", "Artisan.IsBusy", false),
      BusyOf("GatherBuddyReborn", "GatherBuddyReborn.IsAutoGatherEnabled", false));
    return IdleGate.Decide(facts);
  }

  private readonly Dictionary<string, ICallGateSubscriber<bool>> _busySubs = new(StringComparer.Ordinal);

  /// <summary>
  /// Another plugin's busy flag over its own IPC. <paramref name="inverted"/> = the call answers "stopped"
  /// (AutoDuty.IsStopped). Not loaded -> NotInstalled; loaded but the call fails -> Unknown (never idle).
  /// </summary>
  private PluginBusy BusyOf(string internalName, string ipc, bool inverted)
  {
    if (!Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == internalName && p.IsLoaded))
      return PluginBusy.NotInstalled;
    try
    {
      if (!_busySubs.TryGetValue(ipc, out var sub))
        _busySubs[ipc] = sub = Svc.PluginInterface.GetIpcSubscriber<bool>(ipc);
      var value = sub.InvokeFunc();
      return (inverted ? !value : value) ? PluginBusy.Busy : PluginBusy.Idle;
    }
    catch
    {
      return PluginBusy.Unknown;
    }
  }

  // =====================================================================================
  // Reads
  // =====================================================================================

  public unsafe List<BagStack> ReadBags()
  {
    var result = new List<BagStack>();
    var m = InventoryManager.Instance();
    if (m == null) return result;
    foreach (var type in BagTypes)
    {
      var c = m->GetInventoryContainer(type);
      if (c == null || !c->IsLoaded) continue;
      for (var i = 0; i < c->Size; i++)
      {
        var it = c->GetInventorySlot(i);
        if (it == null || it->ItemId == 0 || it->Quantity <= 0) continue;
        result.Add(new BagStack((int)type, i, it->ItemId, it->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), (int)it->Quantity));
      }
    }
    return result;
  }

  private unsafe List<RetainerStack> ReadRetainerPages()
  {
    var result = new List<RetainerStack>();
    var m = InventoryManager.Instance();
    if (m == null) return result;
    foreach (var type in RetainerPageTypes)
    {
      var c = m->GetInventoryContainer(type);
      if (c == null || !c->IsLoaded) continue;
      for (var i = 0; i < c->Size; i++)
      {
        var it = c->GetInventorySlot(i);
        if (it == null || it->ItemId == 0 || it->Quantity <= 0) continue;
        result.Add(new RetainerStack((int)type, i, it->ItemId, it->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), (int)it->Quantity,
          it->Flags.HasFlag(InventoryItem.ItemFlags.Collectable)));
      }
    }
    return result;
  }

  private unsafe (uint ItemId, bool Hq)? ReadSlot(int container, int slot)
  {
    var m = InventoryManager.Instance();
    var it = m == null ? null : m->GetInventorySlot((InventoryType)container, slot);
    if (it == null || it->ItemId == 0)
      return null;
    return (it->ItemId, it->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality));
  }

  private unsafe List<(int Container, int Slot)> EmptyBagSlots()
  {
    var result = new List<(int, int)>();
    var m = InventoryManager.Instance();
    if (m == null) return result;
    foreach (var type in BagTypes)
    {
      var c = m->GetInventoryContainer(type);
      if (c == null || !c->IsLoaded) continue;
      for (var i = 0; i < c->Size; i++)
      {
        var it = c->GetInventorySlot(i);
        if (it == null || it->ItemId == 0) result.Add(((int)type, i));
      }
    }
    return result;
  }

  private static unsafe bool SlotHoldsExactly(int container, int slot, uint itemId, bool hq, int quantity)
  {
    var m = InventoryManager.Instance();
    var it = m == null ? null : m->GetInventorySlot((InventoryType)container, slot);
    return it != null && it->ItemId == itemId && it->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == hq && it->Quantity == quantity;
  }

  private static unsafe bool SlotEmpty(InventoryType type, int slot)
  {
    var m = InventoryManager.Instance();
    var c = m == null ? null : m->GetInventoryContainer(type);
    if (c == null || !c->IsLoaded || slot < 0 || slot >= c->Size) return false;
    var it = c->GetInventorySlot(slot);
    return it == null || it->ItemId == 0;
  }

  private static unsafe int FirstEmpty(InventoryType[] types, out InventoryType type)
  {
    type = types.Length > 0 ? types[0] : InventoryType.Inventory1;
    foreach (var t in types)
    {
      var m = InventoryManager.Instance();
      var c = m == null ? null : m->GetInventoryContainer(t);
      if (c == null || !c->IsLoaded) continue;
      for (var i = 0; i < c->Size; i++)
      {
        var it = c->GetInventorySlot(i);
        if (it == null || it->ItemId == 0)
        {
          type = t;
          return i;
        }
      }
    }
    return -1;
  }

  private static InventoryType TypeOf(ArmourySlot slot)
    => ArmouryPages.First(p => p.Slot == slot).Type;

  /// <summary>Gearset references, item id (quality stripped) -> 1-based gearset numbers. Refreshed every 5 s.</summary>
  public unsafe Dictionary<uint, List<int>> Gearsets()
  {
    var now = Environment.TickCount64;
    if (now - _gearsetsAt < 5000)
      return _gearsets;
    _gearsetsAt = now;
    var map = new Dictionary<uint, List<int>>();
    var gm = RaptureGearsetModule.Instance();
    if (gm != null)
    {
      for (var i = 0; i < 100; i++)
      {
        if (!gm->IsValidGearset(i)) continue;
        var gs = gm->GetGearset(i);
        if (gs == null || !gs->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists)) continue;
        foreach (var it in gs->Items)
        {
          var id = it.ItemId % 500_000u; // strips HQ (+1,000,000) and collectable (+500,000)
          if (id == 0) continue;
          if (!map.TryGetValue(id, out var list)) map[id] = list = [];
          if (!list.Contains(i + 1)) list.Add(i + 1);
        }
      }
    }
    _gearsets = map;
    return map;
  }

  private List<AmEntry> AmEntries()
  {
    var now = Environment.TickCount64;
    if (now - _amAt < 1000)
      return _am;
    _amAt = now;
    _am = Plugin.Configuration.AutoMarketItems.Select(e => new AmEntry(e.ItemId, e.HQ, e.Enabled, e.ExcludeFromCategoryRouting)).ToList();
    return _am;
  }

  // =====================================================================================
  // Last-seen capture
  // =====================================================================================

  private unsafe void TrackRetainerSession(long now)
  {
    var name = AutoMarketService.CurrentRetainerName();
    var m = InventoryManager.Instance();
    var allLoaded = m != null;
    if (allLoaded)
    {
      foreach (var t in RetainerPageTypes)
      {
        var pc = m->GetInventoryContainer(t);
        if (pc == null || !pc->IsLoaded)
        {
          allLoaded = false;
          break;
        }
      }
    }
    if (string.IsNullOrEmpty(name) || !allLoaded)
    {
      _settleName = string.Empty;
      return;
    }
    if (!CategoryRouter.RetainerNamesEqual(name, _settleName))
    {
      _settleName = name;
      _settleNameSince = now;
      _settleFp = 0;
      _settleFpSince = now;
    }
    var stacks = ReadRetainerPages();
    long fp = 17;
    foreach (var s in stacks)
      fp = fp * 31 + s.Container * 131 + s.Slot * 7 + s.ItemId * (s.Hq ? 2L : 1L) + s.Quantity;
    if (fp != _settleFp)
    {
      _settleFp = fp;
      _settleFpSince = now;
      return;
    }
    if (SettledRetainer().Length == 0 || (fp == _savedFp && _savedName == name) || Character is not { } c)
      return;

    var capacity = 0;
    foreach (var t in RetainerPageTypes)
      capacity += (int)m->GetInventoryContainer(t)->Size;
    if (!c.Retainers.TryGetValue(name, out var seen))
      c.Retainers[name] = seen = new RetainerSeen { Name = name };
    seen.PagesSeenUnixMs = NowMs;
    seen.Capacity = capacity > 0 ? capacity : SpaceMath.RetainerCapacity;
    seen.Used = stacks.Count;
    seen.Stacks = stacks.Select(s => new SeenStack { C = s.Container, S = s.Slot, I = s.ItemId, H = s.Hq, Q = s.Quantity, X = s.Collectable }).ToList();
    _savedFp = fp;
    _savedName = name;
    MarkDirty();
  }

  private unsafe void ReadBellCounts(long now)
  {
    if (now < _nextBellRead)
      return;
    _nextBellRead = now + 5000;
    if (!(GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon)))
      return;
    var rm = RetainerManager.Instance();
    if (rm == null || Character is not { } c)
      return;
    var count = rm->GetRetainerCount();
    for (var i = 0u; i < count; i++)
    {
      var r = rm->GetRetainerBySortedIndex(i);
      if (r == null || r->RetainerId == 0 || !r->Available) continue;
      var name = r->NameString;
      if (string.IsNullOrEmpty(name)) continue;
      if (!c.Retainers.TryGetValue(name, out var seen))
        c.Retainers[name] = seen = new RetainerSeen { Name = name };
      if (seen.BellItemCount != r->ItemCount || NowMs - seen.BellSeenUnixMs > 600_000)
      {
        seen.BellItemCount = r->ItemCount;
        seen.BellSeenUnixMs = NowMs;
        MarkDirty();
      }
    }
  }

  private void ReadSaddlebags(long now)
  {
    if (now < _nextSaddleRead)
      return;
    _nextSaddleRead = now + 5000;
    if (Character is not { } c)
      return;
    ContainerSeen? Read(InventoryType a, InventoryType b, ContainerSeen? old)
    {
      var (s1, u1) = Container(a);
      var (s2, u2) = Container(b);
      if (s1 + s2 == 0)
        return old;
      if (old != null && old.Size == s1 + s2 && old.Used == u1 + u2 && NowMs - old.SeenUnixMs < 600_000)
        return old;
      MarkDirty();
      return new ContainerSeen { Size = s1 + s2, Used = u1 + u2, SeenUnixMs = NowMs };
    }
    c.Saddlebag = Read(InventoryType.SaddleBag1, InventoryType.SaddleBag2, c.Saddlebag);
    c.PremiumSaddlebag = Read(InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2, c.PremiumSaddlebag);
  }

  // =====================================================================================
  // AutoRetainer files (read off the framework thread, published whole)
  // =====================================================================================

  public void RequestArRefresh() => _arNextCheck = 0;

  private void RefreshArFiles(long now)
  {
    if (now < _arNextCheck)
      return;
    _arNextCheck = now + 10_000;
    var cid = ContentId;
    if (!Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == AutoRetainerIPC.Name && p.IsLoaded))
    {
      _ar = ArSnapshot.Unavailable("AutoRetainer is not loaded");
      _arMtime = DateTime.MinValue;
      return;
    }

    var cfgPath = Path.Combine(_arDir, ArConfigParser.ConfigFileName);
    var mtime = File.Exists(cfgPath) ? File.GetLastWriteTimeUtc(cfgPath) : DateTime.MinValue;
    if ((mtime != _arMtime || cid != _arCid) && Interlocked.CompareExchange(ref _arLoading, 1, 0) == 0)
    {
      _ = Task.Run(() =>
      {
        try
        {
          var snap = mtime == DateTime.MinValue ? ArSnapshot.Unavailable("AutoRetainer config not found") : ArConfigParser.Parse(ReadShared(cfgPath), cid);
          // A mid-write read parses as Unavailable; keep the previous good snapshot and retry on the next check.
          if (snap.Available || !_ar.Available || mtime == DateTime.MinValue)
            _ar = snap;
          if (snap.Available || mtime == DateTime.MinValue)
          {
            _arMtime = mtime;
            _arCid = cid;
          }
        }
        catch (Exception ex)
        {
          LalaTelemetry.Swallowed("inventory.ar-config", ex);
        }
        finally
        {
          _arLoading = 0;
        }
      });
    }

    if (cid != _statsCid)
    {
      _stats.Clear();
      _statsCid = cid;
      _statsNextCheck = 0;
    }
    if (now < _statsNextCheck || cid == 0)
      return;
    _statsNextCheck = now + 30_000;
    var names = RetainerNames();
    if (names.Count == 0 || Interlocked.CompareExchange(ref _statsLoading, 1, 0) != 0)
      return;
    var known = _stats.ToDictionary(kv => kv.Key, kv => kv.Value.Mtime, StringComparer.Ordinal);
    _ = Task.Run(async () =>
    {
      var fresh = new Dictionary<string, (DateTime, List<VentureRecord>)>(StringComparer.Ordinal);
      try
      {
        foreach (var name in names)
        {
          var path = Path.Combine(_arDir, ArVentureStats.FileName(cid, name));
          if (!File.Exists(path)) continue;
          var mt = File.GetLastWriteTimeUtc(path);
          if (known.TryGetValue(name, out var old) && old == mt) continue;
          fresh[name] = (mt, ArVentureStats.Parse(ReadShared(path)));
        }
      }
      catch (Exception ex)
      {
        LalaTelemetry.Swallowed("inventory.ar-stats", ex);
      }
      await Svc.Framework.RunOnFrameworkThread(() =>
      {
        if (cid == _statsCid)
          foreach (var (k, v) in fresh)
            _stats[k] = v;
        _statsLoading = 0;
      }).ConfigureAwait(false);
    });
  }

  /// <summary>Reads a file another plugin may be writing at the same time (never locks it; BOM detected).</summary>
  private static string ReadShared(string path)
  {
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
    return sr.ReadToEnd();
  }

  // =====================================================================================
  // Persistence + audit trail
  // =====================================================================================

  private InventoryState LoadState()
  {
    try
    {
      return File.Exists(_statePath) ? InventoryState.Load(File.ReadAllText(_statePath)) : new InventoryState();
    }
    catch (Exception ex)
    {
      LalaTelemetry.Swallowed("inventory.state-load", ex, "starting with an empty last-seen state");
      return new InventoryState();
    }
  }

  private void MarkDirty()
  {
    if (!_stateDirty)
      _stateSaveAt = Environment.TickCount64 + 5000;
    _stateDirty = true;
  }

  private void SaveState(bool force)
  {
    if (!_stateDirty && !force)
      return;
    try
    {
      Directory.CreateDirectory(_configDir);
      var tmp = _statePath + ".tmp";
      File.WriteAllText(tmp, _state.Save());
      File.Move(tmp, _statePath, overwrite: true);
      _stateDirty = false;
    }
    catch (Exception ex)
    {
      LalaTelemetry.Swallowed("inventory.state-save", ex);
      _stateSaveAt = Environment.TickCount64 + 30_000;
    }
  }

  /// <summary>One IV| line: plugin log, telemetry ring, append-only actions file, and the tab's recent list.</summary>
  private void Emit(string line)
  {
    Svc.Log.Information("{Line:l}", line);
    LalaTelemetry.Record(line);
    _recentLines.Add(line);
    if (_recentLines.Count > 40)
      _recentLines.RemoveAt(0);
    lock (_logLock)
    {
      try
      {
        Directory.CreateDirectory(_configDir);
        File.AppendAllText(_actionsPath, line + Environment.NewLine);
      }
      catch (Exception ex)
      {
        LalaTelemetry.Swallowed("inventory.actions-log", ex, "the IV| line is still in the plugin log");
      }
    }
  }
}
