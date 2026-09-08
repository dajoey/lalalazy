using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.Automation.LegacyTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyMarketCompanion.AutoMarket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static ECommons.UIHelpers.AtkReaderImplementations.ReaderContextMenu;

namespace LazyMarketCompanion;

/// <summary>
/// The retainer-side automation: overlay buttons on RetainerList / RetainerSellList, the
/// price-match ("Auto Pinch") task chain inherited from Dagobert, the Auto-Market listing
/// chain, and the AutoRetainer postprocess session that runs both unattended.
/// </summary>
internal sealed class MarketAutomation : Window, IDisposable
{
  private readonly MarketBoardHandler _mbHandler;
  private readonly UniversalisPriceProvider _universalisPriceProvider;
  private int? _oldPrice;
  private int? _newPrice;
  private bool _newPriceFromUniversalis;
  // Whether the candidate price came from the per-run cache rather than a fresh lookup. Read only
  // by the decision tap: _newPriceFromUniversalis records where a price ORIGINATED (a cached price
  // keeps the origin it was cached with), so it cannot answer "was this a cache hit" on its own.
  private bool _newPriceFromCache;
  private bool _skipCurrentItem = false;
  private readonly TaskManager _taskManager;
  private Dictionary<string, CachedPrice> _cachedPrices = [];
  private bool _cachedPricesUseUniversalisDataCenterPrices;
  private int _universalisPriceRequestId;
  private bool _disposed;
  private CancellationTokenSource? _universalisPriceRequestCts;

  // Auto-market run bookkeeping
  private readonly List<ListingOp> _listedThisRetainer = [];
  private int _listedTotal;
  private int _listingFailures;
  private int _vendoredThisRun;
  private int _vendorFailedThisRun;
  // The vendored leg is DEFERRED to the end of the retainer session (see EnqueueVendorLegTrigger):
  // the plan is built with the listings and fired after the sell list closes and the bell menu is back.
  private VendorPlan? _vendorPlan;
  private bool _vendorPlanPlaced;
  private int _vendorPlannedCount;
  // 0.1.15.2: set by the vendor leg's own failure path (StopOnVendorFailure) and read by
  // the close steps. A FAILED vendoring leg halts the sweep ON PURPOSE (Joey's pick on Helm
  // t-joey-1788757755566): a failed vendoring action means an unjudged sale is still possible,
  // so nothing further runs until a human looks. The halt is CLEAN - the closing chat line and
  // log line are printed first, then the remaining tasks are discarded without the task
  // manager's timeout abort, which spews "Clearing N remaining tasks because of timeout" and
  // reads like a crash. A trigger with no plan (state-change race) returns false WITHOUT
  // setting this, so a repeat scan of the same retainer does not stop the sweep.
  private bool _vendorStopRequested;
  private string? _vendorStopReason;
  // 0.1.23.0, the Tick NRE: ECommons' legacy TaskManager.Tick calls the step's action and then
  // dereferences CurrentTask for its completion log line. CancelEverything -> _taskManager.Abort()
  // runs INSIDE that invocation and nulls CurrentTask, so Tick's very next line reads
  // CurrentTask.Name on null - the "Object reference not set... at TaskManager.Tick" the stop
  // path logged 3 ms after the 2026-09-07 15:06 halt. The halt therefore does NOT tear the queue
  // down from inside the step: it records the reason, and the per-frame watcher (Draw) performs
  // the Abort/EndArSession on a later frame, when no Tick invocation is on the stack. The NRE was
  // caught by Tick's own catch and the abort still happened - but the fix removes both the
  // exception and the inside-the-iteration teardown.
  private bool _deferredAbortRequested;

  // "Pinch only what I just listed" bookkeeping. Which listings qualify is decided from the market
  // CONTAINER since 0.1.6.0 - a slot this run listed into that is still at the placeholder price - and the
  // UI ROW for each is READ off the open sell list (SellListReader/SellListRows). Neither is inferred from
  // container order; that inference was wrong on 5 of 5 measured runs and its safety net re-priced
  // everything, which is the bug this replaced.
  // _expectedRowItems records what each targeted row was observed to hold so the chain can still refuse a
  // row that turns out to hold something else; _newOnlyPendingSlots is every slot still waiting to be
  // priced, and anything left in it when the pass ends means the reading was wrong after all, which is
  // when AutoMarketPinchFallback decides what happens.
  private readonly Dictionary<int, uint> _expectedRowItems = [];
  private readonly Dictionary<int, int> _rowSlots = [];
  private readonly HashSet<int> _newOnlyPendingSlots = [];
  private int _currentPinchRow = -1;

  // Pre-listing Universalis lookups (UniversalisFirst mode)
  private int? _preListPrice;
  private bool _preListDone;

  // Auto Pinch pre-flight (0.1.9.0). One Universalis request per full-row pinch pass decides which rows are
  // worth opening; see AutoMarket/PinchPreflight.cs. A null decision list - request failed, timed out, or was
  // superseded - means every row is walked, which is exactly what every version before this one did.
  private List<PinchDecision>? _preflightDecisions;
  private bool _preflightDone;
  private int _preflightRequestId;
  private CancellationTokenSource? _preflightCts;

  // AllaganMarket flag set (0.1.16.0): the parsed red/yellow verdicts Auto Pinch goes by, built by
  // BuildAllaganFlags at the start of every full-row pass from AllaganMarket's own files (no IPC exists,
  // so the files are the only surface). Null = nothing is flagged and only placeholders walk.
  private AllaganFlagSet? _allaganFlags;


  // Auto-Market value gate + listing order lookup (0.1.11.0). One Universalis request per retainer,
  // fired before the plan is built so the gate can drop below-threshold items and the sort can order
  // the survivors BEFORE the planner hands out the retainer's scarce free market slots. Same shape as
  // the pre-flight lookup: fire, wait with a time limit, apply - and a null result means the gate and
  // the sort both step aside (everything lists, in list order).
  private Dictionary<uint, ItemQuote>? _gateQuotes;
  private bool _gateQuotesDone;
  private int _gateQuotesRequestId;
  private CancellationTokenSource? _gateQuotesCts;

  // Empty-board sale-history fallback (0.1.8.0). The in-game "Compare Prices" path is synchronous and
  // has already failed by the time SetNewPrice runs, so the fallback lookup is fired from there and the
  // step re-runs until it lands. Keyed by item name so one lookup happens per listing, not per retry.
  private string? _historyRequestItem;
  private bool _historyRequestDone;
  private long _historyRequestDeadline;

  /// <summary>
  /// Why Universalis' sale-history fallback last refused to price an item (stale history, no sales,
  /// request failure - set by <see cref="StartSaleHistoryRequest"/>'s completion handler). Consumed by
  /// the "no price to set" chat message so that message only ever appears with the actual reason
  /// Universalis had no answer. Reset with the per-row lookup state.
  /// </summary>
  private string? _lastUniversalisRefusalReason;

  /// <summary>
  /// How long SetNewPrice may spin waiting for the sale-history lookup. Deliberately SHORTER than the
  /// TaskManager's 10 s per-step limit (AbortOnTimeout is on, so overrunning it kills the whole run) and
  /// shorter than the HttpClient's 8 s timeout, so a slow Universalis costs one unpriced listing rather
  /// than the rest of the sweep.
  /// </summary>
  private const int SaleHistoryWaitMs = 6000;

  // AutoRetainer postprocess session
  private string? _arRetainer;
  private long _arStartedAt;
  private const int ArSessionCapMs = 5 * 60 * 1000;

  public bool IsBusy => _taskManager.IsBusy;
  public string? ActiveAutoRetainerSession => _arRetainer;

  public MarketAutomation()
    : base("Lazy Market Companion##overlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.AlwaysAutoResize, true)
  {
    _mbHandler = new MarketBoardHandler();
    _mbHandler.NewPriceReceived += MBHandler_NewPriceReceived;
    _universalisPriceProvider = new UniversalisPriceProvider();
    _cachedPricesUseUniversalisDataCenterPrices = Plugin.Configuration.UseUniversalisDataCenterPrices;

    Position = new System.Numerics.Vector2(0, 0);
    IsOpen = true;
    ShowCloseButton = false;
    RespectCloseHotkey = false;
    DisableWindowSounds = true;
    SizeConstraints = new WindowSizeConstraints()
    {
      MaximumSize = new System.Numerics.Vector2(0, 0),
    };

    _taskManager = new TaskManager
    {
      TimeLimitMS = 10000,
      AbortOnTimeout = true
    };

    Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, RetainerSellPostSetup);

    if (AutoRetainerIPC.Instance != null)
    {
      AutoRetainerIPC.Instance.OnRetainerPostprocessStep += OnArPostprocessStep;
      AutoRetainerIPC.Instance.OnRetainerReadyToPostprocess += OnArReadyToPostprocess;
    }
  }

  public void Dispose()
  {
    _disposed = true;
    if (AutoRetainerIPC.Instance != null)
    {
      AutoRetainerIPC.Instance.OnRetainerPostprocessStep -= OnArPostprocessStep;
      AutoRetainerIPC.Instance.OnRetainerReadyToPostprocess -= OnArReadyToPostprocess;
    }
    _taskManager.Abort();
    EndArSession("plugin unloading");
    CancelPreflightLookup();
    CancelGateLookup();
    CancelUniversalisPriceRequest();
    _universalisPriceProvider.Dispose();
    Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, RetainerSellPostSetup);
    _mbHandler.NewPriceReceived -= MBHandler_NewPriceReceived;
    _mbHandler.Dispose();
    RemoveTalkAddonListeners();
  }

  // =====================================================================================
  // Draw: overlay buttons + watchdogs
  // =====================================================================================

  public override void Draw()
  {
    try
    {
      ClearCachedPricesIfUniversalisSettingChanged();
      // 0.1.23.0: the stop-on-failure teardown runs here, OUTSIDE any TaskManager.Tick
      // invocation (a step can no longer abort the queue from inside its own execution).
      // Suppression is lifted first so AutoRetainer is never held longer than this frame.
      if (_deferredAbortRequested)
      {
        _deferredAbortRequested = false;
        _taskManager.Abort();
        AutoRetainerIPC.Suppressed(false);
        RemoveTalkAddonListeners();
        EndArSession($"vendoring leg failed: {_vendorStopReason}");
      }
      ArSessionWatchdog();
      DrawForRetainerList();
      DrawForRetainerSellList();
    }
    catch (Exception ex)
    {
      _taskManager.Abort();
      Svc.Log.Error(ex, "[LMC] error while automating");
      if (Plugin.Configuration.ShowErrorsInChat)
        Svc.Chat.PrintError($"[LMC] Error: {ex.Message}");

      RemoveTalkAddonListeners();
      EndArSession("exception");
    }
  }

  private void DrawForRetainerList()
  {
    unsafe
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        if (Plugin.Configuration.EnablePinchKey && Plugin.KeyState[Plugin.Configuration.PinchKey])
          SweepAllRetainers(Plugin.Configuration.AutoMarketInPinchAllSweep);

        var node = addon->UldManager.NodeList[27];
        if (node == null)
          return;

        var oldSize = ImGuiSetup(node);
        DrawButtons(
          ("Auto Pinch", "Re-price every listing on every enabled retainer (match, never undercut).", () => SweepAllRetainers(false)),
          ("Auto Market", "List your always-sell items on every enabled retainer, then price the new listings.\r\nA retainer nothing could be listed on is left alone - use Auto Pinch to re-price existing listings.\r\n(The 'Pinch everything after listing' setting overrides that and re-prices every retainer.)", () => SweepAllRetainers(true)));
        ImGuiPostSetup(oldSize);
      }
    }
  }

  private void DrawForRetainerSellList()
  {
    unsafe
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        if (Plugin.Configuration.EnablePinchKey && Plugin.KeyState[Plugin.Configuration.PinchKey])
          PinchCurrentRetainer();

        var node = addon->UldManager.NodeList[17];
        if (node == null)
          return;

        var oldSize = ImGuiSetup(node);
        DrawButtons(
          ("Auto Pinch", "Re-price every listing of this retainer.", PinchCurrentRetainer),
          ("Auto Market", "List your always-sell items on this retainer, then price the new listings.\r\nIf nothing can be listed, nothing is re-priced - use Auto Pinch for that.", AutoMarketCurrentRetainer));
        ImGuiPostSetup(oldSize);
      }
    }
  }

  private unsafe float ImGuiSetup(AtkResNode* node)
  {
    var position = GetNodePosition(node);
    var scale = GetNodeScale(node);
    var size = new Vector2(node->Width, node->Height) * scale;

    ImGuiHelpers.ForceNextWindowMainViewport();
    ImGuiHelpers.SetNextWindowPosRelativeMainViewport(position);

    ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
    var oldSize = ImGui.GetFont().Scale;
    ImGui.GetFont().Scale *= scale.X;
    ImGui.PushFont(ImGui.GetFont());
    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f.Scale());
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f.Scale(), 3f.Scale()));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f.Scale(), 0f.Scale()));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f.Scale());
    ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, size);
    ImGui.Begin($"###LMCOverlay{node->NodeId}", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
        | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

    return oldSize;
  }

  private static void ImGuiPostSetup(float oldSize)
  {
    ImGui.End();
    ImGui.PopStyleVar(5);
    ImGui.GetFont().Scale = oldSize;
    ImGui.PopFont();
    ImGui.PopStyleColor();
  }

  private void DrawButtons(params (string Label, string Tooltip, Action Run)[] buttons)
  {
    if (_taskManager.IsBusy)
    {
      var label = _arRetainer != null ? "Cancel (AutoRetainer)" : "Cancel";
      if (ImGui.Button(label))
        CancelEverything("cancelled by user");
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Stops the running automation");
      return;
    }

    for (var i = 0; i < buttons.Length; i++)
    {
      var (label, tooltip, run) = buttons[i];
      if (i > 0) ImGui.SameLine();
      var disabled = label == "Auto Market" && !Plugin.Configuration.AutoMarketEnabled;
      if (disabled) ImGui.BeginDisabled();
      if (ImGui.Button(label))
        run();
      if (disabled) ImGui.EndDisabled();
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip(tooltip + "\r\nPlease do not interact with the game while this runs." + (disabled ? "\r\n(Auto-Market is disabled in settings.)" : string.Empty));
    }
  }

  public void CancelEverything(string reason)
  {
    _taskManager.Abort();
    AutoRetainerIPC.Suppressed(false);
    RemoveTalkAddonListeners();
    EndArSession(reason);
  }

  // =====================================================================================
  // Public entry points (buttons, /lmc subcommands)
  // =====================================================================================

  /// <summary>Walk every enabled retainer from the RetainerList: optional auto-market, then pinch.</summary>
  public unsafe void SweepAllRetainers(bool withAutoMarket)
  {
    if (_taskManager.IsBusy)
      return;

    ClearState();
    if (!(GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon)))
      return;

    AutoRetainerIPC.Suppressed(true);
    Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Talk", SkipRetainerDialog);
    Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", SkipRetainerDialog);

    var retainerList = new AddonMaster.RetainerList(addon);
    var retainers = retainerList.Retainers;
    var num = retainers.Length;

    if (Plugin.Configuration.EnabledRetainerNames.Contains(Configuration.ALL_DISABLED_SENTINEL))
    {
      Communicator.PrintAllRetainersDisabled();
      AutoRetainerIPC.Suppressed(false);
      RemoveTalkAddonListeners();
      return;
    }

    var allEnabled = Plugin.Configuration.EnabledRetainerNames.Count == 0;
    var doMarket = withAutoMarket && Plugin.Configuration.AutoMarketEnabled;

    for (var i = 0; i < num; i++)
    {
      var retainerName = retainers[i].Name;
      if (!allEnabled && !Plugin.Configuration.EnabledRetainerNames.Contains(retainerName))
      {
        Svc.Log.Debug($"[LMC] skipping retainer '{retainerName}' (excluded by configuration)");
        continue;
      }
      EnqueueSingleRetainer(i, doMarket);
    }

    _taskManager.Enqueue(RemoveTalkAddonListeners);
    _taskManager.Enqueue(AnnounceRunDone, "AnnounceSweepDone");
    _taskManager.Enqueue(() => AutoRetainerIPC.Suppressed(false));
  }

  /// <summary>Pinch the retainer whose sell list is open.</summary>
  public void PinchCurrentRetainer()
  {
    _mbHandler.PopulateRetainerCache();
    if (_taskManager.IsBusy)
      return;

    ClearState();
    // Same execution order as the sweep's InsertSingleItem path: Insert front-pushes, so inserting rows
    // N-1..0 runs them 0..N-1. Unified onto one helper in 0.1.9.0 so the pre-flight covers all three
    // full-row entry points instead of two of them.
    InsertPinchPass();
  }

  /// <summary>Auto-market + pinch the retainer whose sell list is open.</summary>
  public void AutoMarketCurrentRetainer()
  {
    _mbHandler.PopulateRetainerCache();
    if (_taskManager.IsBusy)
      return;
    if (!Plugin.Configuration.AutoMarketEnabled)
    {
      Communicator.PrintInfo("Auto-Market is disabled in settings.");
      return;
    }

    ClearState();
    _taskManager.Enqueue(() => InsertAutoMarketThenPinch(), "AutoMarketCurrent");
    _taskManager.Enqueue(AnnounceRunDone, "AnnounceDone");
  }

  // =====================================================================================
  // Per-retainer chain (sweep)
  // =====================================================================================

  private void EnqueueSingleRetainer(int index, bool withAutoMarket)
  {
    _taskManager.Enqueue(() => ClickRetainer(index), $"ClickRetainer{index}");
    _taskManager.DelayNext(100);
    _taskManager.Enqueue(ClickSellItems, $"ClickSellItems{index}");
    _taskManager.DelayNext(500);
    if (withAutoMarket)
      _taskManager.Enqueue(() => InsertAutoMarketThenPinch(), $"AutoMarket{index}");
    else
      _taskManager.Enqueue(InsertPinchPass, $"EnqueueAllRetainerItems{index}");
    _taskManager.DelayNext(500);
    _taskManager.Enqueue(CloseRetainerSellList, $"CloseRetainerSellList{index}");
    _taskManager.DelayNext(100);
    _taskManager.Enqueue(CloseRetainer, $"CloseRetainer{index}");
    _taskManager.DelayNext(100);
  }

  private static unsafe bool? ClickRetainer(int index)
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      Communicator.PrintRetainerName(new AddonMaster.RetainerList(addon).Retainers[index].Name);
      ECommons.Automation.Callback.Fire(addon, true, 2, index);
      return true;
    }
    return false;
  }

  /// <summary>
  /// Selects "Sell items in your inventory on the market." from the retainer menu. Matches the
  /// Addon-sheet text (row 2380) so it survives menu reordering; falls back to Dagobert's index 2.
  /// </summary>
  private static unsafe bool? ClickSellItems()
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      var menu = new AddonMaster.SelectString(addon);
      var entries = menu.Entries;
      var wanted = Plugin.MenuText(2380);
      var index = -1;
      for (var i = 0; i < entries.Length; i++)
      {
        if (!string.IsNullOrEmpty(wanted) && entries[i].Text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
        {
          index = i;
          break;
        }
      }
      if (index < 0 && entries.Length > 2)
        index = 2;
      if (index < 0)
        return false;
      entries[index].Select();
      return true;
    }
    return false;
  }

  private static unsafe bool? CloseRetainerSellList()
  {
    // 0.1.26.0: absent = already closed. The vendor leg (inserted ahead of this step) closes the
    // sell list on its way to the bell menu, so the addon is gone by the time this step runs;
    // returning false retried into the time limit and the timeout discarded the session's remaining
    // steps (AR.Announce/Finish), killing the done line that reports the vendored count (the
    // 2026-09-07 18:50 and 21:06 sessions both ended this way after vendoring 5/5 and 1/1).
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      addon->Close(true);
      return true;
    }
    return true;
  }

  private static unsafe bool? CloseRetainer()
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      addon->Close(true);
      return true;
    }
    return false;
  }

  // =====================================================================================
  // Auto-Market chain. Runs as a task while RetainerSellList is open; INSERTS its steps at
  // the front of the queue (reverse order, LegacyTaskManager semantics), then the pinch.
  // =====================================================================================

  private sealed record Step(Func<bool?> Run, string Name, int DelayAfterMs = 0, int TimeLimitMs = 0);

  private void InsertSteps(IReadOnlyList<Step> steps)
  {
    for (var i = steps.Count - 1; i >= 0; i--)
    {
      var s = steps[i];
      if (s.DelayAfterMs > 0)
        _taskManager.InsertDelayNext(s.DelayAfterMs);
      if (s.TimeLimitMs > 0)
        _taskManager.Insert(s.Run, s.TimeLimitMs, false, s.Name);
      else
        _taskManager.Insert(s.Run, s.Name);
    }
  }

  private unsafe bool? InsertAutoMarketThenPinch()
  {
    if (!(GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon)))
      return false;
    if (!AutoMarketService.IsMarketContainerLoaded())
      return false;

    _listedThisRetainer.Clear();
    _expectedRowItems.Clear();
    _rowSlots.Clear();
    _newOnlyPendingSlots.Clear();
    var steps = new List<Step>();

    if (Plugin.Configuration.AutoMarketEnabled)
    {
      // 0.1.11.0: when the value gate or a data-backed listing order is on, fetch the quotes FIRST so
      // BuildPlan can drop below-threshold items and order the survivors before any slot is claimed.
      // Both steps no-op (done immediately) when neither feature is on, keeping the old byte-for-byte
      // behaviour for configs that never touch them.
      var gateNeeded = Plugin.Configuration.AutoMarketValueGateEnabled
                       || Plugin.Configuration.AutoMarketSortMode != MarketSortMode.ListOrder;
      steps.Add(new Step(() => { StartGateLookup(gateNeeded); return true; }, "GateLookup"));
      // 0.1.19.0: 25 s. The 10 s window assumed Universalis answers the gate's one request in
      // under 10 s; on 2026-09-07 the (then 243-id) request 504'd at the gateway on 9 of 10
      // sweeps, GateWait expired, BuildPlan ran with null quotes, and the gate listed
      // below-threshold stock blind while announcing "every item is above the threshold" - the
      // 1-gil listings. The fetch is now stocked-only and chunked (GetRuleQuotes), and this wait
      // covers a slow Universalis day instead of silently blindfolding the gate.
      steps.Add(new Step(() => _gateQuotesDone, "GateWait", TimeLimitMs: 25000));
      steps.Add(new Step(() => BuildListingStepsNow(steps), "BuildPlan"));
    }

    // Pinch afterwards, and how much of the retainer that covers is the ONE decision in
    // AutoMarket/PinchScope.cs - see its remarks for why it is no longer an inline condition.
    // "All" reuses the original per-row chain; "new only" prices just the slots we filled; and a retainer
    // this run listed nothing into is left completely alone (Joey, 2026-09-05: the full retainers "didn't
    // need auto-market b/c they were full. and so it re-pinched all of their items").
    steps.Add(new Step(() =>
    {
      switch (PinchScope.Decide(Plugin.Configuration.AutoMarketPinchAllAfter, _listedThisRetainer.Count))
      {
        case PinchAfterMarket.FullRePass:
          InsertPinchPass();
          break;
        case PinchAfterMarket.NewListingsOnly:
          InsertPinchForNewListings();
          break;
        default:
          Svc.Log.Information("[LMC] pinch: nothing was listed on this retainer, leaving its listings alone");
          break;
      }
      return true;
    }, "PinchAfterMarket", DelayAfterMs: 0));

    InsertSteps(steps);
    return true;
  }

  /// <summary>
  /// The full-row pinch pass - every row of the open sell list - minus the rows a Universalis pre-flight can
  /// show do not need pricing. This is Auto Pinch itself, and the "pinch everything after listing" path.
  ///
  /// WHY (Joey's sweep, 2026-09-06 11:26-11:36): of 39 existing listings re-priced, 17 came out at EXACTLY
  /// the price they already had and 3 moved by a rounding error, at a median 10.5 s per row. He is already
  /// the cheapest on the data centre for those items and "Match Self" is off, so the matched price IS the
  /// price already on the listing. One multi-item Universalis request can see that before any context menu
  /// opens.
  ///
  /// WHAT THIS IS NOT: it is a prediction of what the pricing pass would do, and the pricing pass reads the
  /// in-game Compare Prices window, not Universalis. So every uncertainty walks the row - no data, stale
  /// data, an unreadable row, a request that fails or times out, or the feature being off. A needless walk
  /// costs ten seconds; a wrong skip leaves a listing overpriced until the next sweep.
  /// </summary>
  private bool? InsertPinchPass()
  {
    var rowCount = SellListRowCount();
    if (rowCount < 0)
      return false;

    if (!Plugin.Configuration.AutoPinchPreflightEnabled)
      return EnqueueAllRetainerItems(InsertSingleItem, true);

    var rows = BuildPreflightRows();
    if (rows.Count == 0)
    {
      Svc.Log.Information($"[LMC] pinch pre-flight: the sell list or market container could not be read, walking all {rowCount} row(s)");
      return EnqueueAllRetainerItems(InsertSingleItem, true);
    }

    // AllaganMarket's verdicts, read fresh every pass. Built on the framework thread before the lookup
    // task starts - a ~100 KB CSV read, once per pass, not per row.
    _allaganFlags = BuildAllaganFlags();
    if (_allaganFlags == null)
      Svc.Log.Warning("[LMC] pinch pre-flight: AllaganMarket's data is unavailable, so nothing is flagged and only placeholder-priced listings will be re-priced");

    // Same three-step shape as the pre-listing lookup: fire the request, wait for it with a time limit,
    // then insert the rows that survived. The wait step does not abort the run on timeout - the apply step
    // sees no decisions and walks everything.
    InsertSteps([
      new Step(() => { StartPreflightLookup(rows); return true; }, "PinchPreflightLookup"),
      new Step(() => _preflightDone, "PinchPreflightWait", TimeLimitMs: 10000),
      new Step(() => { ApplyPreflight(rowCount); return true; }, "PinchPreflightApply"),
    ]);
    return true;
  }

  /// <summary>
  /// Every sell-list row with what the game says it holds and what it is priced at right now. Both readings
  /// are the ones the new-only pass already relies on: the row/slot pairing off the addon
  /// (<see cref="SellListReader"/>) and the price off the market container
  /// (<see cref="AutoMarketService.MarketPricesBySlot"/>). A row whose slot could not be read comes back
  /// with item 0 and price 0, which the pre-flight treats as "walk it".
  /// </summary>
  private List<PinchRow> BuildPreflightRows()
  {
    var market = AutoMarketService.SnapshotMarket();
    if (market.Count == 0)
      return [];

    var prices = AutoMarketService.MarketPricesBySlot();
    if (prices.Count == 0)
      return [];

    var sellRows = SellListReader.Read(market);
    if (sellRows.Count == 0)
      return [];

    var placeholder = (long)Math.Max(Plugin.Configuration.AutoMarketPlaceholderPrice, 1);
    var rows = new List<PinchRow>(sellRows.Count);

    foreach (var row in sellRows)
    {
      var slotInfo = row.Slot == MarketRowMap.NoRow ? null : market.FirstOrDefault(m => m.Slot == row.Slot);
      var itemId = slotInfo?.ItemId ?? 0u;
      var hq = slotInfo?.HQ ?? false;
      long current = 0;
      if (row.Slot != MarketRowMap.NoRow && prices.TryGetValue(row.Slot, out var priced))
        current = (long)Math.Min(priced, long.MaxValue);

      rows.Add(new PinchRow(row.Row, row.Slot, itemId, hq, current, current > 0 && current == placeholder));
    }

    return rows;
  }

  /// <summary>
  /// AllaganMarket's verdicts for the player's home world, parsed from its own files at the start of
  /// every pass: MarketPriceCache.csv (the prices its overlay colours from) and AllaganMarket.json
  /// (UndercutBy, ItemUpdatePeriod, UndercutComparison). AllaganMarket exposes no IPC, so these files
  /// are the only way to go by its flags - a format change there breaks this silently, and the
  /// changelog says so. A missing or unreadable file reads as an EMPTY flag set: nothing is flagged,
  /// and the pass walks nothing but placeholder-priced new listings. That is the honest failure mode
  /// of the flags-only rule - LMC now depends on AllaganMarket being installed and having data.
  /// </summary>
  private AllaganFlagSet? BuildAllaganFlags()
  {
    try
    {
      // Dalamud API 15: the local character's home world lives on IPlayerState via the object table
      // (IClientState no longer exposes LocalPlayer). Same reading the sibling plugins use.
      var worldId = Svc.Objects.LocalPlayer?.HomeWorld.RowId ?? 0;
      if (worldId == 0)
        return null;

      // AllaganMarket's config lives in the SIBLING directory of Dalamud's per-plugin config dirs:
      // <pluginConfigs>/AllaganMarket/, not inside LazyMarketCompanion's own directory.
      var dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "..", "AllaganMarket");
      var cacheCsv = File.Exists(Path.Combine(dir, AllaganMarketFlags.CacheFileName))
        ? File.ReadAllText(Path.Combine(dir, AllaganMarketFlags.CacheFileName))
        : string.Empty;
      var settingsJson = File.Exists(Path.Combine(dir, AllaganMarketFlags.SettingsFileName))
        ? File.ReadAllText(Path.Combine(dir, AllaganMarketFlags.SettingsFileName))
        : null;

      var set = AllaganMarketFlags.Parse(cacheCsv, settingsJson, worldId, DateTime.Now);
      Svc.Log.Debug($"[LMC] allagan flags: {set.EntryCount} cache entr(ies) for world {worldId}");
      return set;
    }
    catch (Exception ex)
    {
      Svc.Log.Warning(ex, "[LMC] allagan flags: could not read AllaganMarket's data; nothing is flagged this pass");
      return null;
    }
  }

  private void StartPreflightLookup(IReadOnlyList<PinchRow> rows)
  {
    _preflightDecisions = null;
    _preflightDone = false;

    var itemIds = rows.Where(r => r.ItemId != 0).Select(r => r.ItemId).Distinct().ToList();
    if (itemIds.Count == 0)
    {
      _preflightDone = true;
      return;
    }

    var options = new PinchPreflightOptions(Plugin.Configuration.AutoPinchPreflightEnabled);

    var snapshot = rows.ToList();
    var requestId = ++_preflightRequestId;
    _preflightCts?.Cancel();
    _preflightCts?.Dispose();
    _preflightCts = new CancellationTokenSource();
    var token = _preflightCts.Token;

    _ = Task.Run(async () =>
    {
      List<PinchDecision>? decisions = null;
      try
      {
        // 0.1.16.0: the verdict comes from AllaganMarket's flags alone. Universalis is no longer asked
        // here at all - the old prediction round-trip (up to the 10 s wait) bought a candidate that the
        // flags rule never reads, so the pass starts walking flagged rows immediately instead.
        decisions = PinchPreflight.Decide(snapshot, new Dictionary<uint, ItemQuote>(), options,
          DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _allaganFlags, ApplyItemPriceLimitsById);
      }
      catch (OperationCanceledException) { return; }
      catch (Exception ex) { Svc.Log.Warning(ex, "[LMC] pinch pre-flight failed; the pass will walk nothing but placeholders"); }

      await Svc.Framework.RunOnFrameworkThread(() =>
      {
        if (_disposed || requestId != _preflightRequestId) return;
        _preflightDecisions = decisions;
        _preflightDone = true;
      });
    }, token);
  }

  /// <summary>
  /// Queue the rows the pre-flight kept. The INFO line here is deliberately greppable: it is how this
  /// feature is graded from the client log afterwards, against a pre-fix slice as a control.
  /// </summary>
  private void ApplyPreflight(int rowCount)
  {
    var decisions = _preflightDecisions;
    _preflightDecisions = null;
    _preflightDone = false;

    if (decisions == null || decisions.Count == 0)
    {
      Svc.Log.Information($"[LMC] pinch pre-flight: no flag data, walking all {rowCount} row(s)");
      EnqueueAllRetainerItems(InsertSingleItem, true);
      return;
    }

    Svc.Log.Information($"[LMC] {PinchPreflight.Summarize(decisions)}");

    foreach (var decision in decisions.Where(d => d.Verdict != PinchVerdict.Walk))
      Svc.Log.Debug($"[LMC] pinch pre-flight: skipping row {decision.Row.Row} (slot #{decision.Row.Slot}, item {decision.Row.ItemId}) priced {decision.Row.CurrentPrice} - {decision.Reason}");

    // Insert front-pushes, so the highest row goes in first and the rows are priced lowest-first.
    foreach (var decision in decisions.Where(d => d.Verdict == PinchVerdict.Walk).OrderByDescending(d => d.Row.Row))
      InsertSingleItem(decision.Row.Row);
  }

  /// <summary>
  /// Price ONLY the slots this run just filled, instead of walking the whole retainer.
  ///
  /// TWO QUESTIONS, TWO SOURCES. "Which listings are mine to price?" is answered by the market CONTAINER;
  /// "which UI row is each one on?" is answered by the addon's own per-row slot reading. Neither answer
  /// involves the order of the sell list, and the first involves no text at all.
  ///
  ///   1. <see cref="SellListRows.ScanPlaceholders"/> - a slot qualifies only if this run listed into it AND
  ///      it is still sitting at the Auto-Market placeholder price (999,999,999 gil by default), read back
  ///      through <see cref="AutoMarketService.MarketPricesBySlot"/>
  ///      (<c>InventoryManager.GetRetainerMarketPrice</c>). Joey's own instruction, 2026-09-05: "there has to
  ///      be a way to see what my listings are and select the one with the WILDLY INFLATED PRICE." A listing
  ///      he made by hand is never at that price, so it is not reachable by this pass at all - which is a
  ///      STRONGER guarantee than the name comparison it replaces, because it is a number from the game
  ///      rather than a string from a UI label.
  ///   2. <see cref="SellListRows.MatchBySlot"/> - the row for each target slot, from
  ///      <c>AtkValues[15 + 13n]</c> (see <see cref="SellListReader"/>). This is the part of 0.1.5.0 that
  ///      worked: on the 20:37:48 run it found the row for the new listing correctly.
  ///
  /// WHAT WENT WRONG IN 0.1.5.0, and why the name is no longer load-bearing: the cross-check ran over EVERY
  /// row that had a readable name, and vetoed the whole batch on the first disagreement. At 20:37:48 the
  /// listing for slot #10 was identified correctly and thrown away anyway, because row 0 - a row nobody was
  /// pricing - carried a clipped label ("Snow Cotton Ushanka of Scouting" resolving to "Snow Cotton") that
  /// disagreed with the container. The name is now a corroborator on the rows being priced only, and a name
  /// that cannot be pinned to exactly one item reads as unknown rather than as a different item
  /// (see <see cref="ItemNameMatch"/>).
  ///
  /// The three 0.1.3.0 guards all stay - they are why a stranger's listing has never been re-priced:
  ///   1. here, before any click: every target slot must be matched to a row, all-or-nothing;
  ///   2. per row, once the game has the item open (see <see cref="DelayMarketBoard"/>): the item actually in
  ///      the dialog must be the item that row was observed to hold, or that row is abandoned unpriced;
  ///   3. at the end (see <see cref="VerifyNewListingsPriced"/>): any target slot that never got as far as
  ///      step 2 hands over to <see cref="Configuration.AutoMarketPinchFallback"/>.
  /// </summary>
  private void InsertPinchForNewListings()
  {
    _expectedRowItems.Clear();
    _rowSlots.Clear();
    _newOnlyPendingSlots.Clear();

    // Fixed-price listings are already at their final price; the match pass has nothing to do for them.
    var pending = _listedThisRetainer.Where(o => o.FixedPrice <= 0).ToList();
    if (pending.Count == 0)
    {
      Svc.Log.Information("[LMC] pinch new-only: every new listing has a fixed price, nothing to match");
      return;
    }

    var market = AutoMarketService.SnapshotMarket();
    var placeholder = (ulong)Math.Max(Plugin.Configuration.AutoMarketPlaceholderPrice, 1);

    // STEP 1 - which of these listings still need a price? Straight off the market container: no item names,
    // no sell-list order, no UI text of any kind. A slot the user priced themselves is not at the
    // placeholder, so it is not selectable here however anything else reads.
    var prices = AutoMarketService.MarketPricesBySlot();
    if (prices.Count == 0)
    {
      Svc.Log.Warning("[LMC] pinch new-only: the market container's prices could not be read");
      RunFallback(pending, [], market, "the retainer's listing prices could not be read");
      return;
    }

    var scan = SellListRows.ScanPlaceholders(pending.Select(o => (o.TargetSlot, o.ItemId)), prices, placeholder);

    if (scan.AlreadyPriced.Count > 0)
      Svc.Log.Information($"[LMC] pinch new-only: slot(s) {string.Join(", ", scan.AlreadyPriced.Select(s => "#" + s))} already carry a real price; nothing to do for them");
    if (scan.Foreign.Count > 0)
      Svc.Log.Information($"[LMC] pinch new-only: slot(s) {string.Join(", ", scan.Foreign.Select(s => "#" + s))} are at the placeholder price but were not listed by this run; leaving them alone");

    if (scan.Targets.Count == 0)
    {
      // Every listing this run made is already priced. Nothing to do, and specifically NOT a reason to
      // re-price the retainer: there is no listing stranded at the placeholder to rescue.
      Svc.Log.Information($"[LMC] pinch new-only: all {pending.Count} new listing(s) already carry a real price, nothing to re-price");
      return;
    }

    var targetOps = pending.Where(o => scan.Targets.Any(t => t.Slot == o.TargetSlot)).ToList();
    var listed = scan.Targets;

    // STEP 2 - which UI row is each target on? The market snapshot goes into the reader so a clipped row
    // label is recognised as unreadable instead of being reported as some shorter item it happens to contain.
    var sellRows = SellListReader.Read(market);

    if (sellRows.Count == 0)
    {
      Svc.Log.Warning("[LMC] pinch new-only: the sell list could not be read at all");
      RunFallback(targetOps, sellRows, market, "the sell list could not be read");
      return;
    }

    // Necessary condition kept from 0.1.3.0: one row per occupied slot. It never caught the ordering bug
    // (20 rows == 20 slots every time), but a list showing a different number of rows than the container
    // holds means we are not looking at what we think we are, and nothing below should be trusted.
    if (!MarketRowMap.RowCountAgrees(market, sellRows.Count))
    {
      Svc.Log.Warning($"[LMC] pinch new-only: sell list shows {sellRows.Count} row(s) but {MarketRowMap.OccupiedCount(market)} market slot(s) are occupied");
      RunFallback(targetOps, sellRows, market, $"the sell list shows {sellRows.Count} rows for {MarketRowMap.OccupiedCount(market)} listings");
      return;
    }

    List<RowMatch>? matches;
    string? failure;
    if (SellListRows.HasSlotReadings(sellRows))
    {
      matches = SellListRows.MatchBySlot(sellRows, market, listed, out failure);
    }
    else
    {
      // No slot reading available: fall back to identifying rows by the name they display. Only rows the
      // client has actually rendered carry a name (the list virtualises), so this can legitimately fail on a
      // long list - which is what the fallback policy is for.
      Svc.Log.Warning("[LMC] pinch new-only: the sell list reported no slot for any row; matching by item name instead");
      matches = SellListRows.MatchByName(sellRows, listed, Math.Max(Plugin.Configuration.AutoMarketPlaceholderPrice, 1), out failure);
    }

    if (matches == null)
    {
      Svc.Log.Warning($"[LMC] pinch new-only: could not identify the row(s) holding {string.Join(", ", listed.Select(t => $"slot #{t.Slot} (item {t.ItemId})"))} - {failure}");
      RunFallback(targetOps, sellRows, market, failure ?? "the new listings could not be found on the sell list");
      return;
    }

    foreach (var m in matches)
    {
      _expectedRowItems[m.Row] = m.ItemId;
      _rowSlots[m.Row] = m.Slot;
      _newOnlyPendingSlots.Add(m.Slot);
    }

    Svc.Log.Information($"[LMC] pinch new-only: pricing {matches.Count} new listing(s) instead of all {sellRows.Count} - {string.Join(", ", matches.Select(m => $"row {m.Row}=slot #{m.Slot} (item {m.ItemId}, {(m.Source == RowMatchSource.ObservedSlot ? "read from the list" : "matched by name")})"))}");

    // Insert pushes to the FRONT of the queue, so the backstop goes in first to come out last, and the
    // rows go in highest-first so they are priced lowest-row-first.
    _taskManager.Insert(VerifyNewListingsPriced, "VerifyNewListingsPriced");
    _taskManager.InsertDelayNext(1000);
    foreach (var m in matches.OrderByDescending(m => m.Row))
      InsertSingleItem(m.Row);
  }

  /// <summary>
  /// What happens when a listing this run created cannot be tied to a sell-list row. Before 0.1.5.0 this was
  /// hardcoded to "re-price everything", which is why the feature never once did what it said. It is now the
  /// user's choice, still SHIPPING as that same behaviour so upgrading changes nothing on its own.
  /// </summary>
  private void RunFallback(List<ListingOp> pending, IReadOnlyList<SellListRow> sellRows, IReadOnlyList<MarketSlot> market, string why)
  {
    switch (Plugin.Configuration.AutoMarketPinchFallback)
    {
      case PinchFallbackMode.SkipAndTell:
        Svc.Log.Error($"[LMC] pinch new-only: {why}; leaving {pending.Count} new listing(s) at the placeholder price and touching nothing else (fallback: skip and tell)");
        Communicator.PrintInfo($"couldn't tell which sell-list rows the {pending.Count} new listing(s) landed on, so nothing was re-priced - they are still at the placeholder price. Run Auto Pinch to price them.");
        return;

      case PinchFallbackMode.OwnItemsOnly:
      {
        var own = Plugin.Configuration.AutoMarketItems.Select(i => i.ItemId).ToHashSet();
        var rows = SellListRows.RowsHoldingOwnItems(sellRows, market, own);
        if (rows.Count == 0)
        {
          Svc.Log.Error($"[LMC] pinch new-only: {why}; no row holds an item from your Auto-Market list, so nothing was re-priced (fallback: own items only)");
          Communicator.PrintInfo("couldn't tell which sell-list rows the new listing(s) landed on, and no listing is an item on your Auto-Market list, so nothing was re-priced.");
          return;
        }
        Svc.Log.Error($"[LMC] pinch new-only: {why}; re-pricing the {rows.Count} row(s) holding items from your Auto-Market list (fallback: own items only)");
        Communicator.PrintInfo($"couldn't tell which sell-list rows the new listing(s) landed on, so the {rows.Count} listing(s) of items on your Auto-Market list are being re-priced instead.");
        _taskManager.InsertDelayNext(1000);
        foreach (var row in rows.OrderByDescending(r => r))
          InsertSingleItem(row);
        return;
      }

      default:
        Svc.Log.Error($"[LMC] pinch new-only: {why}; re-pricing every row so nothing is left at the placeholder price (fallback: full re-pass)");
        Communicator.PrintInfo("couldn't tell which sell-list rows the new listing(s) landed on, so every listing is being re-priced (nothing was left at the placeholder price).");
        EnqueueAllRetainerItems(InsertSingleItem, true);
        return;
    }
  }

  /// <summary>
  /// Runs after the new-only pass. Any slot still in <see cref="_newOnlyPendingSlots"/> was never opened with
  /// the right item in it: the row we READ turned out not to hold what the client said, so that listing is
  /// still at the placeholder price. What happens then is the user's choice - see
  /// <see cref="Configuration.AutoMarketPinchFallback"/> and <see cref="RunFallback"/>.
  /// </summary>
  private bool? VerifyNewListingsPriced()
  {
    var expectedRows = _expectedRowItems.Count;
    var stranded = _newOnlyPendingSlots.ToList();
    _expectedRowItems.Clear();
    _rowSlots.Clear();
    _newOnlyPendingSlots.Clear();

    // Diagnostic only: a slot the pass DID handle can still be at the placeholder when no board price was
    // found, and that is already reported to the user - re-pricing it would fail the same way.
    var placeholder = (ulong)Math.Max(Plugin.Configuration.AutoMarketPlaceholderPrice, 1);
    var stillPlaceholder = _listedThisRetainer
      .Where(o => o.FixedPrice <= 0 && AutoMarketService.MarketPrice(o.TargetSlot) == placeholder)
      .Select(o => o.TargetSlot)
      .ToList();
    if (stillPlaceholder.Count > 0)
      Svc.Log.Warning($"[LMC] pinch new-only: slot(s) {string.Join(", ", stillPlaceholder.Select(s => "#" + s))} still hold the placeholder price after the pass");

    if (stranded.Count == 0)
    {
      Svc.Log.Information($"[LMC] pinch new-only: all {expectedRows} new listing(s) were opened on the expected row");
      return true;
    }

    var strandedOps = _listedThisRetainer.Where(o => o.FixedPrice <= 0 && stranded.Contains(o.TargetSlot)).ToList();
    Svc.Log.Error($"[LMC] pinch new-only: {stranded.Count} new listing(s) ({string.Join(", ", stranded.Select(s => "slot #" + s))}) were never reached - the row we opened did not hold the expected item");
    var backstopMarket = AutoMarketService.SnapshotMarket();
    RunFallback(strandedOps, SellListReader.Read(backstopMarket), backstopMarket,
      $"{stranded.Count} new listing(s) were never reached on the row the sell list pointed at");
    return true;
  }

  /// <summary>
  /// Builds the plan with the gate's quotes in hand and front-inserts the listing steps. Runs as a task
  /// step (after GateWait) so the Universalis fetch never blocks the framework thread; InsertSteps from
  /// inside a running task places the listing steps ahead of the pinch/close steps already queued.
  /// </summary>
  private bool? BuildListingStepsNow(List<Step> steps)
  {
    var plan = AutoMarketService.BuildPlan(_gateQuotes);
    foreach (var note in plan.Notes)
      Svc.Log.Information($"[LMC] plan: {note}");

    // 0.1.15.1: the vendor plan is built BEFORE the empty-listing early return, and the trigger is
    // queued on BOTH paths. 0.1.15.0 reached the trigger only past that return, so a retainer with a
    // full market board (0 free slots, the common shape) ended its session with the gate's vendoring
    // decision unexecuted and unannounced (t_bbb89c49). BuildVendoringSteps no-ops unless the gate
    // actually held items back, so runs with no vendor verdicts are byte-for-byte unchanged.
    BuildVendoringSteps();
    if (_vendorPlanPlaced || _vendorPlannedCount > 0)
      EnqueueVendorLegTrigger();

    if (plan.Ops.Count == 0)
    {
      Communicator.PrintInfo(plan.Notes.Count > 0 ? $"Nothing to list ({plan.Notes[0]})." : "Nothing to list.");
      return true;
    }

    Svc.Log.Information($"[LMC] plan: {plan.Ops.Count} listing(s): {string.Join(", ", plan.Ops.Select(o => $"{o.ItemId}{(o.HQ ? "HQ" : "")}x{o.Quantity}->#{o.TargetSlot}"))}");
    var listing = new List<Step>();
    foreach (var op in plan.Ops)
      AddListingSteps(listing, op);
    InsertSteps(listing);

    // 0.1.12.0 vendored these instead of holding them, with the vendor steps inserted alongside the
    // listing steps - but the retainer inventory panel can only open AFTER the sell list closes and
    // the bell menu is back, so 0.1.15.0 queues a trigger that runs the leg at the end of this
    // retainer's session instead (AutoRetainer's own order: menu -> panel -> sell -> close).
    return true;
  }

  /// <summary>
  /// The retainer-vendor leg (0.1.12.0, wired 0.1.15.1). Turns the gate's Vendor-verdict rules into
  /// concrete VendorOps from the CURRENT stock (same snapshot the plan just used). Every op
  /// re-verifies its slot inside ExecuteVendor, so a plan built on a stale snapshot fails safe item
  /// by item. No travel - AgentRetainer is active - and the leg runs inside the same retainer
  /// session Auto-Market already drives. 0.1.15.0 defined this method but never CALLED it: the
  /// vendor plan existed only as dead code, and the full-board path additionally returned before
  /// the trigger was queued (t_bbb89c49).
  /// </summary>
  private void BuildVendoringSteps()
  {
    var held = AutoMarketService.HeldBackRules;
    if (held.Count == 0)
      return;

    var stock = AutoMarketService.SnapshotStock();
    var prices = new Dictionary<uint, (uint, uint)>();
    var preferHq = Plugin.Configuration.HQ;
    foreach (var rule in held)
    {
      if (prices.ContainsKey(rule.ItemId))
        continue;
      prices[rule.ItemId] = AutoMarketService.VendorPrices(rule.ItemId);
    }

    var plan = VendorPlanner.Plan(held, stock, prices, preferHq);
    if (plan.Ops.Count == 0)
    {
      foreach (var note in plan.Notes)
        Svc.Log.Information($"[LMC] plan: {note}");
      return;
    }

    _vendorPlan = plan;
    _vendorPlanPlaced = true;
    Svc.Log.Information($"[LMC] plan: {plan.Ops.Count} vendor op(s): {string.Join(", ", plan.Ops.Select(o => $"{o.ContainerName()}:{o.Slot} item{o.ItemId}x{o.Quantity}"))}");
    long est = 0;
    foreach (var op in plan.Ops)
      est += op.EstGil;
    _vendorPlannedCount = plan.Ops.Count;
    if (Plugin.Configuration.ShowAutoMarketMessages)
      Communicator.PrintInfo($"value gate: vendoring {plan.Ops.Count} stack(s) through the retainer (est {est:N0} gil)");
  }

  /// <summary>
  /// Inserts the trigger that runs the vendor leg at the end of this retainer's session. Called from
  /// BuildListingStepsNow on BOTH the listing and the nothing-to-list path - all three Auto-Market
  /// entry points (sweep, current-retainer button, AutoRetainer postprocess) funnel through it.
  /// 0.1.15.1 semantics: the trigger is INSERTED, so it sits at the front of the remaining queue -
  /// ahead of this session's close steps and ahead of the sweep's other retainers. It closes the
  /// sell list itself if that step has not run yet, then uses the bell menu (the panel the sell
  /// call needs - InventoryRetainer / InventoryRetainerLarge - only opens from the menu; while the
  /// sell list is open there is no menu). If the session ends without the menu returning (the chain
  /// died), the trigger says so in the log and the done line reports the shortfall honestly.
  /// </summary>
  private void EnqueueVendorLegTrigger()
  {
    // 0.1.15.1: INSERT, not Enqueue. Inserting puts the trigger ahead of the steps this session has
    // already queued (pinch close, sell-list close, bell close) and ahead of the sweep's remaining
    // retainers - the leg runs at the END OF THIS retainer's session, which is what the done line's
    // vendored counts assume. Enqueue would have run it after AnnounceRunDone, i.e. after the whole
    // sweep had already reported.
    _taskManager.Insert(RunVendorLegAtSessionEnd, 120000, false, "VendorLeg");
    Svc.Log.Debug("[LMC] vendor leg trigger inserted for the end of this retainer session");
  }

  /// <summary>
  /// Runs at the end of a retainer session, once the bell menu is back. Opens the retainer inventory
  /// panel by selecting the menu's entrust entry (Addon sheet row 2378, the same entry AutoRetainer's
  /// TaskVendorItems uses to reach this panel), waits for the panel, fires one throttled call per
  /// planned op, then closes the panel and the agent. Fails the leg honestly: planned N, executed M,
  /// with per-op reasons in the log.
  /// </summary>
  private unsafe bool? RunVendorLegAtSessionEnd()
  {
    var plan = _vendorPlan;
    var placed = _vendorPlanPlaced;
    _vendorPlanPlaced = false;
    if (!placed || plan == null || plan.Ops.Count == 0)
    {
      // 0.1.16.3: a completed trigger returns true and the task manager just continues the
      // queue; only StopOnVendorFailure stops the sweep, so a no-op cannot halt anything.
      // Returning false here was the 0.1.16.2 defect: false means RETRY, so on a retainer whose
      // gate held no vendor items this dead trigger re-ran every tick for its full 120 s time
      // limit at the FRONT of the queue, stalling the session's own listing steps - the sweep
      // looked hung for two minutes per listing retainer (2026-09-07 10:13-10:15 run).
      // Decision pinned Dalamud-free in VendorMenuGate (harness case 44).
      return AutoMarket.VendorMenuGate.NoPlanTriggerCompletes();
    }

    // 0.1.15.1: the trigger is INSERTED at the front of the queue, ahead of the session's own
    // CloseRetainerSellList/CloseRetainer steps (Enqueue would place it AFTER every remaining
    // retainer and after AnnounceRunDone - the done line would report a shortfall before the leg
    // ever ran). While the sell list is still open the bell menu does not exist yet, so the open
    // step below waits for it; CloseRetainerSellList becomes a no-op (the addon is gone) and
    // CloseRetainer leaves the menu open for us. Vendoring therefore happens INSIDE this
    // retainer's session, before its done line.
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var sellList) && GenericHelpers.IsAddonReady(sellList))
      sellList->Close(true);

    if (Plugin.Configuration.ShowAutoMarketMessages)
      Communicator.PrintInfo($"value gate: vendoring {plan.Ops.Count} stack(s) at the retainer (est {plan.Ops.Sum(o => o.EstGil):N0} gil)");

    // AutoRetainer's own precondition sequence (TaskVendorItems.Enqueue -> WaitUntilInventoryLoaded ->
    // SafeSellSlot -> CloseAgentRetainer): the panel is reached through the bell menu's entrust
    // entry, one SelectString click - not through AgentRetainer.Show.
    // 0.1.15.2: the menu-open step is a WAIT, not a glance (the 0.1.15.1 defect). ClickRetainerEntrust
    // returns false while the bell menu has not come back yet, so the click retries for up to its
    // own time limit - on the 01:45 run the first entry scan happened 135 ms after the sell-list
    // close was queued and the menu had not rendered. Only an exhausted wait or a menu that is
    // really there but has no entrust entry is a failure, and a failure stops the whole sweep
    // (stop-on-failure, Joey's pick on Helm t-joey-1788757755566) with a named, human-readable
    // halt instead of the task manager's timeout abort.
    var steps = new List<Step>();
    steps.Add(new Step(ClickRetainerEntrust, "Vendor.OpenPanel", TimeLimitMs: 10000));
    steps.Add(new Step(() => AutoMarketService.IsRetainerInventoryOpen(), "Vendor.WaitPanel", TimeLimitMs: 10000));
    steps.Add(new Step(() =>
    {
      if (!AutoMarketService.IsRetainerInventoryOpen())
        return StopOnVendorFailure("the retainer bell menu never reopened after the sell list closed (waited 10 s) - the vendoring leg could not run");
      var vendorSteps = new List<Step>();
      foreach (var op in plan.Ops)
      {
        var captured = op;
        vendorSteps.Add(new Step(() =>
        {
          var ok = AutoMarketService.ExecuteVendor(captured);
          if (ok) { _vendoredThisRun++; } else { _vendorFailedThisRun++; }
          return true;
        }, $"Vendor{captured.ContainerName()}:{captured.Slot}", DelayAfterMs: 250));
      }
      vendorSteps.Add(new Step(CloseRetainerInventory, "Vendor.ClosePanel", DelayAfterMs: 200));
      InsertSteps(vendorSteps);
      return true;
    }, "Vendor.Fire", DelayAfterMs: 100));
    InsertSteps(steps);
    return true;
  }

  /// <summary>
  /// Selects the bell menu's "Entrust or withdraw items." entry (Addon sheet row 2378) to open the
  /// retainer inventory panel - AutoRetainer's RetainerHandlers.SelectEntrustItems, matched by the
  /// sheet text so it survives menu reordering. Returns false (retry) while the menu is not on screen.
  /// </summary>
  // The bell-menu mismatch grace window (0.1.23.0, VendorMenuGate.MenuGraceWindowMs): a
  // MenuMissingEntry verdict on the FIRST tick of the menu-open step - the 2026-09-07 15:06
  // failure fired 181 ms after the sell-list close was queued - is treated as a transition, not
  // a verdict: the step retries through the grace window and only the mismatch that SURVIVES it
  // stops the sweep. TickCount64 is used instead of a field so a fresh TaskManager (abort/restart
  // mid-window) restarts the window rather than inheriting a stale one.
  private long _menuMismatchSince;

  private unsafe bool? ClickRetainerEntrust()
  {
    // 0.1.15.2: the menu not being on screen YET is the normal case - this step now runs while the
    // sell-list close is still in flight. Returning false makes the step RETRY until its own time
    // limit (TaskManager's per-step timeout), so the 0.1.15.1 single 135 ms glance is gone. The
    // decision table lives in AutoMarket/VendorMenuGate.cs (Dalamud-free, harness case 42); only a
    // menu that is demonstrably up WITHOUT the entrust entry is a failure, and a failure stops the
    // sweep on purpose - the wait-for-menu step behind this one never runs then, so its own stop
    // hook never fires and the message stays the accurate one.
    // 0.1.23.0: matching goes through VendorMenuGate.MatchMenuEntry (row 2378's "(Slots filled: N)"
    // tail is a live runtime payload - the template says 0, a full retainer renders 20, so the
    // old whole-template StartsWith failed on a PERFECT menu and the 15:06 sweep died on a
    // full-board retainer). And a fresh mismatch waits out MenuGraceWindowMs before the verdict:
    // a wrong-menu failure is static, the first 181 ms of a menu transition are not.
    if (!(GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && GenericHelpers.IsAddonReady(addon)))
      return false;
    var menu = new AddonMaster.SelectString(addon);
    var entries = menu.Entries;
    var wanted = Plugin.MenuText(2378);
    var found = -1;
    for (var i = 0; i < entries.Length; i++)
    {
      if (VendorMenuGate.MatchMenuEntry(entries[i].Text, wanted))
      {
        found = i;
        break;
      }
    }
    var decision = VendorMenuGate.Decide(menuReady: true, sheetTextLoaded: !string.IsNullOrEmpty(wanted), entryFound: found >= 0);
    switch (decision)
    {
      case VendorMenuDecision.WaitForMenu:
        _menuMismatchSince = 0;
        return false; // retry until the step's own time limit
      case VendorMenuDecision.OpenPanel:
        _menuMismatchSince = 0;
        Svc.Log.Debug($"[LMC] vendor: opening retainer inventory via menu entry '{entries[found].Text}'");
        entries[found].Select();
        return true;
      default:
        if (_menuMismatchSince == 0)
          _menuMismatchSince = Environment.TickCount64;
        if (Environment.TickCount64 - _menuMismatchSince < VendorMenuGate.MenuGraceWindowMs)
          return false; // a transition, not yet a verdict - retry through the grace window
        _menuMismatchSince = 0;
        StopOnVendorFailure("the retainer bell menu is open but has no 'Entrust or withdraw items' entry - the vendoring leg cannot open the inventory panel");
        return true;
    }
  }

  /// <summary>
  /// The vendor leg's failure path (0.1.15.2, stop-on-failure). Logs one named ERROR line, says the
  /// same thing in chat, then STOPS THE WHOLE SWEEP ON PURPOSE: the remaining queued tasks (this
  /// session's close steps and every other retainer's chain) are discarded so nothing sells or
  /// re-prices while a vendoring action is unexplained, AutoRetainer's suppression is lifted and
  /// the AR session is closed. This is the deliberate halt Joey picked on Helm
  /// t-joey-1788757755566 - a controlled stop with a human-readable reason, never the task
  /// manager's timeout abort (that one prints "Clearing N remaining tasks because of timeout",
  /// wipes the queue WITHOUT a reason, and reads like a crash), never silence. Returns true so the
  /// failing step itself completes cleanly and the abort that follows is ours, not a timeout's.
  /// </summary>
  private bool StopOnVendorFailure(string reason)
  {
    Svc.Log.Error($"[LMC] vendor: FAILED - {reason}. Stopping the sweep here on purpose (stop-on-failure); no further retainer will be touched.");
    _vendorStopRequested = true;
    _vendorStopReason = reason;
    // 0.1.23.0: this runs INSIDE a TaskManager.Tick invocation. Calling CancelEverything from
    // here aborted the queue mid-iteration and Tick's next line dereferenced the nulled
    // CurrentTask - the TaskManager.Tick NRE 3 ms after this stop on 2026-09-07 15:06. The
    // teardown is deferred to the per-frame watcher instead (see _deferredAbortRequested); the
    // chat line still prints immediately, and _vendorStopRequested already makes every later
    // AutoRetainer ready-event release without touching the retainer.
    _deferredAbortRequested = true;
    if (Plugin.Configuration.ShowAutoMarketMessages)
      Communicator.PrintInfo($"value gate: vendoring stopped the sweep - {reason}");
    return true;
  }

  /// <summary>Closes the retainer inventory panel (both layouts) so the bell menu is the only thing left open.</summary>
  private unsafe bool? CloseRetainerInventory()
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("InventoryRetainer", out var a) && GenericHelpers.IsAddonReady(a))
    {
      a->Close(true);
      return true;
    }
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("InventoryRetainerLarge", out var b) && GenericHelpers.IsAddonReady(b))
    {
      b->Close(true);
      return true;
    }
    return false;
  }

  /// <summary>Fires (or skips) the gate's Universalis request; see the field block for why it exists.</summary>
  private void StartGateLookup(bool needed)
  {
    _gateQuotes = null;
    _gateQuotesDone = false;

    var ids = needed ? AutoMarketService.GateItemIds() : [];
    if (ids.Count == 0)
    {
      _gateQuotesDone = true;
      return;
    }

    var requestId = ++_gateQuotesRequestId;
    _gateQuotesCts?.Cancel();
    _gateQuotesCts?.Dispose();
    _gateQuotesCts = new CancellationTokenSource();
    var token = _gateQuotesCts.Token;

    _ = Task.Run(async () =>
    {
      Dictionary<uint, ItemQuote>? quotes = null;
      try
      {
        quotes = await _universalisPriceProvider.GetRuleQuotes(ids, token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested)
      {
        // A REAL cancel (sweep aborted, or the next retainer's lookup superseded this one).
        // Nothing sets _gateQuotesDone and the step chain is being torn down anyway.
        return;
      }
      catch (Exception ex)
      {
        // 0.1.19.0: this arm now also catches the HttpClient's OWN timeout. A timeout throws
        // TaskCanceledException, which IS an OperationCanceledException - the old bare
        // `catch (OperationCanceledException) { return; }` swallowed exactly that case SILENTLY:
        // no warning, _gateQuotesDone never set, GateWait burning its whole limit on a fetch
        // that had already died, BuildPlan blind. With the `when` filter above, a timeout lands
        // HERE instead - named, logged, and the wait completes with null quotes: a declared
        // blind gate (ApplyValueGate announces it), never a silent one.
        Svc.Log.Warning(ex, "[LMC] gate lookup failed; every item will list in list order");
      }

      await Svc.Framework.RunOnFrameworkThread(() =>
      {
        if (_disposed || requestId != _gateQuotesRequestId) return;
        _gateQuotes = quotes;
        _gateQuotesDone = true;
      });
    }, token);
  }

  private void CancelGateLookup()
  {
    _gateQuotesRequestId++;
    _gateQuotes = null;
    _gateQuotesDone = false;
    _gateQuotesCts?.Cancel();
    _gateQuotesCts?.Dispose();
    _gateQuotesCts = null;
  }

  private void AddListingSteps(List<Step> steps, ListingOp op)
  {
    var mode = Plugin.Configuration.AutoMarketPriceMode;
    var placeholder = (uint)Math.Max(Plugin.Configuration.AutoMarketPlaceholderPrice, 1);

    if (op.FixedPrice <= 0 && mode == NewListingPriceMode.UniversalisFirst)
    {
      steps.Add(new Step(() => { StartPreListLookup(op); return true; }, $"UniversalisLookup{op.TargetSlot}"));
      steps.Add(new Step(() => _preListDone, $"UniversalisWait{op.TargetSlot}", TimeLimitMs: 10000));
    }

    steps.Add(new Step(() =>
    {
      uint price;
      if (op.FixedPrice > 0)
        price = (uint)op.FixedPrice;
      else if (mode == NewListingPriceMode.UniversalisFirst && _preListPrice is > 0)
        price = (uint)ApplyItemPriceLimitsById(op.ItemId, _preListPrice.Value);
      else
        price = placeholder;

      var ok = AutoMarketService.Execute(op, price);
      if (!ok) _listingFailures++;
      _preListPrice = null;
      _preListDone = false;
      return true;
    }, $"List{op.TargetSlot}", DelayAfterMs: 250));

    // Wait for the server to reflect the listing; a miss is logged and the run continues.
    steps.Add(new Step(() =>
    {
      if (!AutoMarketService.IsListed(op))
        return false;
      _listedThisRetainer.Add(op);
      _listedTotal++;
      Communicator.PrintListed(op.ItemId, op.HQ, op.Quantity);
      return true;
    }, $"Listed{op.TargetSlot}", DelayAfterMs: 300, TimeLimitMs: 6000));
  }

  private void StartPreListLookup(ListingOp op)
  {
    _preListPrice = null;
    _preListDone = false;
    var requestId = ++_universalisPriceRequestId;
    _universalisPriceRequestCts?.Cancel();
    _universalisPriceRequestCts = new CancellationTokenSource();
    var token = _universalisPriceRequestCts.Token;
    _ = Task.Run(async () =>
    {
      var price = -1;
      try { price = await _universalisPriceProvider.GetNewPriceById(op.ItemId, op.HQ, token).ConfigureAwait(false); }
      catch (OperationCanceledException) { return; }
      catch (Exception ex) { Svc.Log.Warning(ex, $"[LMC] Universalis pre-list lookup failed for {op.ItemId}"); }

      await Svc.Framework.RunOnFrameworkThread(() =>
      {
        if (_disposed || requestId != _universalisPriceRequestId) return;
        _preListPrice = price;
        _preListDone = true;
      });
    }, token);
  }

  // =====================================================================================
  // AutoRetainer postprocess session
  // =====================================================================================

  private void OnArPostprocessStep(string retainer)
  {
    if (!Plugin.Configuration.AutoMarketDuringAutoRetainer || !Plugin.Configuration.AutoMarketEnabled)
      return;
    if (!IsRetainerEnabled(retainer))
    {
      Svc.Log.Debug($"[LMC] AR step: retainer '{retainer}' not enabled, not claiming");
      return;
    }
    if (Plugin.Configuration.AutoMarketItems.Count == 0)
      return;

    Svc.Log.Information($"[LMC] AR step: claiming postprocess for {retainer}");
    AutoRetainerIPC.Instance?.RequestRetainerPostprocess();
  }

  private void OnArReadyToPostprocess(string retainer)
  {
    // 0.1.15.2, stop-on-failure: a vendor-leg failure stopped the sweep on purpose, so further
    // AutoRetainer postprocess sessions are REFUSED until the user starts something new
    // (ClearState resets the flag). Releasing immediately keeps AutoRetainer's own cycle moving -
    // wedging IT would be a different defect - but this plugin touches nothing until then.
    if (_vendorStopRequested)
    {
      Svc.Log.Warning($"[LMC] AR ready for {retainer} but a vendoring leg stopped the sweep on purpose; releasing without touching the retainer");
      AutoRetainerIPC.Instance?.FinishRetainerPostProcess();
      return;
    }

    if (_taskManager.IsBusy)
    {
      Svc.Log.Warning($"[LMC] AR ready for {retainer} but we are busy; releasing immediately");
      AutoRetainerIPC.Instance?.FinishRetainerPostProcess();
      return;
    }

    _arRetainer = retainer;
    _arStartedAt = Environment.TickCount64;
    ClearState();
    Communicator.PrintRetainerName(retainer);
    Svc.Log.Information($"[LMC] AR session start: {retainer}");

    _taskManager.Enqueue(ClickSellItems, "AR.ClickSellItems");
    _taskManager.DelayNext(500);
    _taskManager.Enqueue(WaitSellListReady, 15000, "AR.WaitSellList");
    _taskManager.Enqueue(() => InsertAutoMarketThenPinch(), "AR.AutoMarket");
    _taskManager.DelayNext(500);
    _taskManager.Enqueue(CloseRetainerSellList, "AR.CloseSellList");
    _taskManager.DelayNext(300);
    _taskManager.Enqueue(WaitSelectStringReady, 10000, "AR.WaitMenu");
    _taskManager.Enqueue(AnnounceRunDone, "AR.Announce");
    _taskManager.Enqueue(() => EndArSession("done"), "AR.Finish");
  }

  private static unsafe bool? WaitSellListReady()
  {
    return GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon)
        && GenericHelpers.IsAddonReady(addon)
        && AutoMarketService.IsMarketContainerLoaded();
  }

  private static unsafe bool? WaitSelectStringReady()
  {
    return GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && GenericHelpers.IsAddonReady(addon);
  }

  /// <summary>If the AR session's chain died (timeout/abort) or overran, release AR so it never hangs on us.</summary>
  private void ArSessionWatchdog()
  {
    if (_arRetainer == null) return;

    if (!_taskManager.IsBusy)
    {
      EndArSession("chain ended without Finish (timeout/abort)");
      return;
    }
    if (Environment.TickCount64 - _arStartedAt > ArSessionCapMs)
    {
      Svc.Log.Warning($"[LMC] AR session for {_arRetainer} exceeded {ArSessionCapMs / 1000}s; aborting");
      _taskManager.Abort();
      EndArSession("session cap");
    }
  }

  private void EndArSession(string reason)
  {
    if (_arRetainer == null) return;
    Svc.Log.Information($"[LMC] AR session end: {_arRetainer} ({reason})");
    _arRetainer = null;
    AutoRetainerIPC.Instance?.FinishRetainerPostProcess();
  }

  private static bool IsRetainerEnabled(string name)
  {
    var set = Plugin.Configuration.EnabledRetainerNames;
    if (set.Contains(Configuration.ALL_DISABLED_SENTINEL)) return false;
    return set.Count == 0 || set.Contains(name);
  }

  // =====================================================================================
  // Pinch chain (inherited from Dagobert Price Matcher)
  // =====================================================================================

  /// <summary>Number of rows the open RetainerSellList is showing, or -1 when it is not available.</summary>
  private static int SellListRowCount() => SellListReader.RowCount();

  private bool? EnqueueAllRetainerItems(Action<int> enqueueFunc, bool reverseOrder)
  {
    var num = SellListRowCount();
    if (num < 0)
      return false;

    if (reverseOrder)
    {
      for (int i = num - 1; i >= 0; i--)
        enqueueFunc(i);
    }
    else
    {
      for (int i = 0; i < num; i++)
        enqueueFunc(i);
    }
    return true;
  }

  private void EnqueueSingleItem(int index)
  {
    _taskManager.Enqueue(() => OpenItemContextMenu(index), $"OpenItemContextMenu{index}");
    _taskManager.DelayNext(100);
    _taskManager.Enqueue(ClickAdjustPrice, $"ClickAdjustPrice{index}");
    _taskManager.DelayNext(100);
    _taskManager.Enqueue(DelayMarketBoard, $"DelayMB{index}");
    _taskManager.Enqueue(ClickComparePrice, $"ClickComparePrice{index}");
    _taskManager.DelayNext(Plugin.Configuration.MarketBoardKeepOpenMS);
    _taskManager.Enqueue(SetNewPrice, $"SetNewPrice{index}");
  }

  private void InsertSingleItem(int index)
  {
    // reverse order because we INSERT
    _taskManager.Insert(SetNewPrice, $"SetNewPrice{index}");
    _taskManager.InsertDelayNext(Plugin.Configuration.MarketBoardKeepOpenMS);
    _taskManager.Insert(ClickComparePrice, $"ClickComparePrice{index}");
    _taskManager.Insert(DelayMarketBoard, $"DelayMB{index}");
    _taskManager.InsertDelayNext(100);
    _taskManager.Insert(ClickAdjustPrice, $"ClickAdjustPrice{index}");
    _taskManager.InsertDelayNext(100);
    _taskManager.Insert(() => OpenItemContextMenu(index), $"OpenItemContextMenu{index}");
  }

  private unsafe bool? OpenItemContextMenu(int itemIndex)
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      // Remembered so the price steps can tell WHICH row they are working on; only the new-only pass has
      // an expectation for a row, the pinch-all pass walks every row and needs no mapping.
      _currentPinchRow = itemIndex;
      Svc.Log.Debug($"[LMC] clicking item {itemIndex}");
      ECommons.Automation.Callback.Fire(addon, true, 0, itemIndex, 1);
      return true;
    }
    return false;
  }

  private unsafe bool? ClickAdjustPrice()
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      var reader = new ReaderContextMenu(addon);
      if (!IsItemMannequin(reader.Entries))
      {
        Svc.Log.Debug("[LMC] clicking adjust price");
        ECommons.Automation.Callback.Fire(addon, true, 0, 0, 0, 0, 0);
      }
      else
      {
        Svc.Log.Debug("[LMC] mannequin item, skipping");
        _skipCurrentItem = true;
        addon->Close(true);
      }
      return true;
    }
    return false;
  }

  private static bool IsItemMannequin(List<ContextMenuEntry> contextMenuEntries)
  {
    return !contextMenuEntries.Any((e) => e.Name.Equals("adjust price", StringComparison.CurrentCultureIgnoreCase)
                                      || e.Name.Equals("preis \u00e4ndern", StringComparison.CurrentCultureIgnoreCase)
                                      || e.Name.Equals("changer le prix", StringComparison.CurrentCultureIgnoreCase));
  }

  private unsafe bool? DelayMarketBoard()
  {
    if (_skipCurrentItem)
      return true;

    if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon) && GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
    {
      var itemName = GetRetainerSellItemName(addon);
      var rawItemName = GetRetainerSellRawItemName(addon);

      // First moment the game tells us what the row we clicked actually holds. If the new-only pass
      // predicted a different item, the sell list is not in market-slot order: abandon this row unpriced
      // rather than re-pricing a listing the user never asked us to touch. The slot stays in
      // _newOnlyPendingSlots, so VerifyNewListingsPriced re-prices the whole retainer afterwards.
      if (!VerifyPinchRow(itemName, rawItemName, addon))
        return true;

      // New row: any sale-history lookup belonging to the previous one is finished with.
      _historyRequestItem = null;
      _historyRequestDone = false;
      _historyRequestDeadline = 0;
      _lastUniversalisRefusalReason = null;

      if (Plugin.Configuration.UseUniversalisDataCenterPrices && _universalisPriceProvider.CanResolveItem(itemName, rawItemName))
        return true;

      if (!_cachedPrices.TryGetValue(itemName, out var cachedPrice) || cachedPrice.Value <= 0)
      {
        Svc.Log.Debug($"[LMC] {itemName} has no cached price, delaying next mb open");
        _taskManager.InsertDelayNext(Plugin.Configuration.GetMBPricesDelayMS);
      }
      return true;
    }
    return false;
  }

  /// <summary>
  /// True when this row may be priced. Always true during a pinch-all pass (no row is predicted). During a
  /// new-only pass, compares the item the game has open against the item the row mapping promised; on a
  /// mismatch it logs, cancels out of the price dialog and marks the row skipped.
  /// </summary>
  private unsafe bool VerifyPinchRow(string itemName, string rawItemName, AddonRetainerSell* addon)
  {
    if (!_expectedRowItems.TryGetValue(_currentPinchRow, out var expectedItemId))
      return true;

    // The expected item is passed in so a CLIPPED label is recognised as unreadable rather than reported as
    // the shorter item its text happens to contain - the failure that vetoed the 20:37:48 run.
    if (!ItemNameResolver.TryGetItemId(itemName, rawItemName, out var actualItemId, expectedItemId))
    {
      // Cannot identify what is open, so cannot prove it is the right listing. Treat exactly like a
      // mismatch: skip it here, and let the backstop re-price the retainer.
      Svc.Log.Error($"[LMC] pinch new-only: row {_currentPinchRow} should hold item {expectedItemId} but the open listing '{itemName}' could not be identified; skipping it rather than risk re-pricing the wrong listing");
      SkipMismatchedRow(addon);
      return false;
    }

    if (actualItemId == expectedItemId)
    {
      // Clear the slot this row was OBSERVED to hold. 0.1.3.0 re-derived it here from container order - the
      // very inference that was wrong 4 of 4 runs - so a correct pass could still leave a slot looking
      // stranded. The pairing recorded when the row was matched is the only thing trusted now.
      if (_rowSlots.TryGetValue(_currentPinchRow, out var slot))
        _newOnlyPendingSlots.Remove(slot);
      return true;
    }

    Svc.Log.Error($"[LMC] pinch new-only: row {_currentPinchRow} holds item {actualItemId} ('{itemName}') but the new listing there should be item {expectedItemId} - the sell list is not in market-slot order; skipping this row rather than re-pricing someone else's listing");
    SkipMismatchedRow(addon);
    return false;
  }

  /// <summary>Back out of the price dialog without touching the price, and no-op the rest of this row's chain.</summary>
  private unsafe void SkipMismatchedRow(AddonRetainerSell* addon)
  {
    _skipCurrentItem = true;
    ECommons.Automation.Callback.Fire(&addon->AtkUnitBase, true, 1); // cancel
    addon->AtkUnitBase.Close(true);
  }

  private unsafe bool? ClickComparePrice()
  {
    if (_skipCurrentItem)
      return true;

    if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon) && GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
    {
      var itemName = GetRetainerSellItemName(addon);
      var rawItemName = GetRetainerSellRawItemName(addon);
      if (_cachedPrices.TryGetValue(itemName, out var cachedPrice) && cachedPrice.Value > 0)
      {
        Svc.Log.Debug($"[LMC] {itemName}: using cached price");
        _newPrice = cachedPrice.Value;
        _newPriceFromUniversalis = cachedPrice.FromUniversalis;
        _newPriceFromCache = true;
        return true;
      }

      if (Plugin.Configuration.UseUniversalisDataCenterPrices && _universalisPriceProvider.CanResolveItem(itemName, rawItemName))
      {
        Svc.Log.Debug($"[LMC] {itemName}: requesting Universalis data center price");
        StartUniversalisPriceRequest(itemName, rawItemName);
        return true;
      }

      Svc.Log.Debug("[LMC] clicking compare prices");
      ECommons.Automation.Callback.Fire(&addon->AtkUnitBase, true, 4);
      return true;
    }
    return false;
  }

  private unsafe bool? SetNewPrice()
  {
    try
    {
      if (_skipCurrentItem)
        return true;

      if (!_newPrice.HasValue)
        return false;

      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ItemSearchResult", out var addon))
        addon->Close(true);

      if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var retainerSell) && GenericHelpers.IsAddonReady(&retainerSell->AtkUnitBase))
      {
        var ui = &retainerSell->AtkUnitBase;
        var itemName = GetRetainerSellItemName(retainerSell);
        _oldPrice = retainerSell->AskingPrice->Value;
        var isPlaceholder = _oldPrice.Value == Plugin.Configuration.AutoMarketPlaceholderPrice;
        var usedDefaultAmount = false;

        // Nothing on the board. Before falling through to DefaultAmount (or to giving up), try the
        // recent-sales median. On by default since 0.1.14.0 (Joey 2026-09-07: the empty-board
        // "no price to set" message should never appear while Universalis is up; the fallback was
        // ratified as option A of the 0.1.8.0 decision card and is now the standard behaviour).
        // The Universalis price path already consulted the sale history in its own request, so this
        // only covers the in-game Compare Prices path.
        string? universalisFailureReason = null;
        if (!(_newPrice > 0) && Plugin.Configuration.UseUniversalisSaleHistoryFallback)
        {
          var historyRawName = GetRetainerSellRawItemName(retainerSell);
          var universalisAlreadyLooked = Plugin.Configuration.UseUniversalisDataCenterPrices
                                         && _universalisPriceProvider.CanResolveItem(itemName, historyRawName);
          if (!universalisAlreadyLooked)
          {
            if (!string.Equals(_historyRequestItem, itemName, StringComparison.Ordinal))
            {
              Svc.Log.Debug($"[LMC] {itemName}: nothing on the board, requesting the recent-sales price");
              _historyRequestItem = itemName;
              _historyRequestDone = false;
              _historyRequestDeadline = Environment.TickCount64 + SaleHistoryWaitMs;
              StartSaleHistoryRequest(itemName, historyRawName);
              return false;
            }

            if (!_historyRequestDone)
            {
              if (Environment.TickCount64 < _historyRequestDeadline)
                return false;

              Svc.Log.Warning($"[LMC] {itemName}: recent-sales lookup did not answer within {SaleHistoryWaitMs} ms; continuing without it");
              CancelUniversalisPriceRequest();
              _historyRequestDone = true;
              universalisFailureReason = $"Universalis did not answer within {SaleHistoryWaitMs / 1000} s";
            }
          }
        }

        if (!(_newPrice > 0))
        {
          if (Plugin.Configuration.DefaultAmount == 0)
          {
            Svc.Log.Warning("[LMC] SetNewPrice: no price to set");
            Communicator.PrintNoPriceToSetError(itemName, isPlaceholder, universalisFailureReason ?? _lastUniversalisRefusalReason);
            ECommons.Automation.Callback.Fire(&retainerSell->AtkUnitBase, true, 1); // cancel
            ui->Close(true);
            return true;
          }
          Svc.Log.Warning("[LMC] SetNewPrice: using default amount");
          _newPrice = Plugin.Configuration.DefaultAmount;
          _newPriceFromUniversalis = false;
          _newPriceFromCache = false;
          usedDefaultAmount = true;
          Communicator.PrintUsingDefaultAmountWarning(itemName, _newPrice.Value);
        }

        var rawItemName = GetRetainerSellRawItemName(retainerSell);
        // Kept for the tap: the candidate as it arrived, before any per-item min/max clamp.
        var rawPrice = _newPrice.Value;
        var limitedPrice = ApplyItemPriceLimits(itemName, rawItemName, _newPrice.Value);
        var wasLimited = limitedPrice != _newPrice.Value;
        if (wasLimited)
        {
          Svc.Log.Debug($"[LMC] {itemName}: price limit adjusted {_newPrice.Value} to {limitedPrice}");
          _newPrice = limitedPrice;
        }

        var cutPercentage = ((float)_newPrice.Value - _oldPrice.Value) / _oldPrice.Value * 100f;
        // A placeholder listing is always allowed to drop to the real price; the max-cut guard is for real listings.
        var priceAccepted = isPlaceholder || cutPercentage >= -Plugin.Configuration.MaxUndercutPercentage;
        if (priceAccepted)
        {
          Svc.Log.Debug("[LMC] setting new price");
          _cachedPrices.TryAdd(itemName, new CachedPrice(_newPrice.Value, _newPriceFromUniversalis));
          retainerSell->AskingPrice->SetValue(_newPrice.Value);
          if (isPlaceholder)
            Communicator.PrintNewListingPriced(itemName, _newPrice.Value, _newPriceFromUniversalis);
          else
            Communicator.PrintPriceUpdate(itemName, _oldPrice.Value, _newPrice.Value, cutPercentage, _newPriceFromUniversalis);
        }
        else
          Communicator.PrintAboveMaxCutError(itemName);

        // Off-by-default decision tap. The flag is read BEFORE anything is gathered: resolving the
        // item id is a linear scan of the Item sheet, so "off" must cost exactly this bool read.
        // Both outcomes are emitted - an abort writes no price and is otherwise almost traceless,
        // which makes it the single most interesting line in the plugin.
        if (Plugin.Configuration.DecisionTelemetry)
          MarketTelemetry.RecordDecision(
            itemName, rawItemName,
            _oldPrice.Value, rawPrice, _newPrice.Value,
            _newPriceFromUniversalis, _newPriceFromCache, usedDefaultAmount,
            wasLimited, isPlaceholder, aborted: !priceAccepted, cutPercentage);

        ECommons.Automation.Callback.Fire(&retainerSell->AtkUnitBase, true, 0); // confirm
        ui->Close(true);
        return true;
      }
      return false;
    }
    finally
    {
      _oldPrice = null;
      _newPrice = null;
      _newPriceFromUniversalis = false;
      _newPriceFromCache = false;
      _skipCurrentItem = false;
    }
  }

  private void MBHandler_NewPriceReceived(object? sender, NewPriceEventArgs e)
  {
    Svc.Log.Debug($"[LMC] new price received: {e.NewPrice}");
    _newPrice = e.NewPrice;
    _newPriceFromUniversalis = false;
    _newPriceFromCache = false;
  }

  private static unsafe string GetRetainerSellItemName(AddonRetainerSell* addon) => addon->ItemName->NodeText.GetText();

  private static unsafe string GetRetainerSellRawItemName(AddonRetainerSell* addon) => addon->ItemName->NodeText.ToString();

  private static int ApplyItemPriceLimits(string itemName, string rawItemName, int price)
  {
    if (!ItemNameResolver.TryGetItemId(itemName, rawItemName, out var itemId))
      return price;
    return ApplyItemPriceLimitsById(itemId, price);
  }

  private static int ApplyItemPriceLimitsById(uint itemId, int price)
  {
    var limit = Plugin.Configuration.GetItemPriceLimit(itemId);
    return limit?.Apply(price) ?? price;
  }

  private void StartUniversalisPriceRequest(string itemName, string rawItemName)
  {
    CancelUniversalisPriceRequest();

    var requestId = ++_universalisPriceRequestId;
    _newPriceFromUniversalis = false;
    _universalisPriceRequestCts = new CancellationTokenSource();
    _ = CompleteUniversalisPriceRequest(itemName, rawItemName, requestId, _universalisPriceRequestCts.Token);
  }

  private async Task CompleteUniversalisPriceRequest(string itemName, string rawItemName, int requestId, CancellationToken cancellationToken)
  {
    var price = -1;
    try
    {
      price = await _universalisPriceProvider.GetNewPrice(itemName, rawItemName, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) { return; }
    catch (Exception ex) { Svc.Log.Warning(ex, $"[LMC] failed to fetch Universalis price for {itemName}"); }

    await Svc.Framework.RunOnFrameworkThread(() =>
    {
      if (_disposed || requestId != _universalisPriceRequestId)
        return;
      Svc.Log.Debug($"[LMC] Universalis price received: {price}");
      _newPrice = price;
      _newPriceFromUniversalis = price > 0;
      _newPriceFromCache = false;
    });
  }

  /// <summary>
  /// Fires the empty-board recent-sales lookup. Mirrors <see cref="StartUniversalisPriceRequest"/> - same
  /// request-id guard, same framework-thread handoff - and lands in the same <c>_newPrice</c>, so a
  /// history price is written, cached, limited and logged exactly like any other candidate.
  /// </summary>
  private void StartSaleHistoryRequest(string itemName, string rawItemName)
  {
    CancelUniversalisPriceRequest();

    var requestId = ++_universalisPriceRequestId;
    _newPriceFromUniversalis = false;
    _universalisPriceRequestCts = new CancellationTokenSource();
    var token = _universalisPriceRequestCts.Token;

    _ = Task.Run(async () =>
    {
      var price = -1;
      try
      {
        price = await _universalisPriceProvider.GetSaleHistoryPrice(itemName, rawItemName, token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) { return; }
      catch (Exception ex) { Svc.Log.Warning(ex, $"[LMC] failed to fetch the recent-sales price for {itemName}"); }

      await Svc.Framework.RunOnFrameworkThread(() =>
      {
        if (_disposed || requestId != _universalisPriceRequestId)
          return;
        Svc.Log.Debug($"[LMC] recent-sales price received: {price}");
        _newPrice = price;
        _newPriceFromUniversalis = price > 0;
        _newPriceFromCache = false;
        _historyRequestDone = true;
        _lastUniversalisRefusalReason = price > 0 ? null
          : UniversalisPriceProvider.LastHistoryRefusal ?? "Universalis returned no usable sale history";
      });
    }, token);
  }

  private void CancelUniversalisPriceRequest()
  {
    _universalisPriceRequestId++;
    _universalisPriceRequestCts?.Cancel();
    _universalisPriceRequestCts?.Dispose();
    _universalisPriceRequestCts = null;
  }

  private unsafe void SkipRetainerDialog(AddonEvent type, AddonArgs args)
  {
    if (!_taskManager.IsBusy)
      RemoveTalkAddonListeners();
    else if (((AtkUnitBase*)args.Addon.Address)->IsVisible)
      new AddonMaster.Talk(args.Addon).Click();
  }

  private void RetainerSellPostSetup(AddonEvent type, AddonArgs args)
  {
    if (_taskManager.IsBusy)
      return;

    if (Plugin.Configuration.EnablePostPinchkey && Plugin.KeyState[Plugin.Configuration.PostPinchKey])
    {
      _taskManager.Enqueue(ClickComparePrice, "ClickComparePricePosted");
      _taskManager.DelayNext(Plugin.Configuration.MarketBoardKeepOpenMS);
      _taskManager.Enqueue(SetNewPrice, "SetNewPricePosted");
    }
  }

  private void RemoveTalkAddonListeners()
  {
    Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Talk", SkipRetainerDialog);
    Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", SkipRetainerDialog);
  }

  private static unsafe Vector2 GetNodePosition(AtkResNode* node)
  {
    var pos = new Vector2(node->X, node->Y);
    var par = node->ParentNode;
    while (par != null)
    {
      pos *= new Vector2(par->ScaleX, par->ScaleY);
      pos += new Vector2(par->X, par->Y);
      par = par->ParentNode;
    }
    return pos;
  }

  private static unsafe Vector2 GetNodeScale(AtkResNode* node)
  {
    if (node == null) return new Vector2(1, 1);
    var scale = new Vector2(node->ScaleX, node->ScaleY);
    while (node->ParentNode != null)
    {
      node = node->ParentNode;
      scale *= new Vector2(node->ScaleX, node->ScaleY);
    }
    return scale;
  }

  /// <summary>
  /// The run's closing chat line, plus the announce-then-fail guard (0.1.15.0): a run that announced
  /// vendoring and then executed none of it (the leg never ran - chain died, menu never came back)
  /// says the shortfall in chat, not just in the log.
  /// </summary>
  private void AnnounceRunDone()
  {
    // 0.1.15.1: with the leg running inside each retainer's session, counters at zero here mean the
    // leg genuinely never executed (chain died before the trigger) - the per-retainer trail is in
    // the log either way.
    if (_vendorPlannedCount > 0 && _vendoredThisRun == 0 && _vendorFailedThisRun == 0)
    {
      Svc.Log.Error($"[LMC] vendor: planned {_vendorPlannedCount} op(s) but the leg never executed");
      if (Plugin.Configuration.ShowAutoMarketMessages)
        Communicator.PrintInfo($"value gate: 0 of {_vendorPlannedCount} planned stack(s) were vendored - the leg did not run (see log)");
    }
    Communicator.PrintSweepDone(_listedTotal, _listingFailures, _vendoredThisRun, 0, _vendorFailedThisRun);
  }

  private void ClearState()
  {
    _newPrice = null;
    _newPriceFromUniversalis = false;
    _newPriceFromCache = false;
    _cachedPrices = [];
    _cachedPricesUseUniversalisDataCenterPrices = Plugin.Configuration.UseUniversalisDataCenterPrices;
    _skipCurrentItem = false;
    _listedThisRetainer.Clear();
    _expectedRowItems.Clear();
    _rowSlots.Clear();
    _newOnlyPendingSlots.Clear();
    _currentPinchRow = -1;
    _listedTotal = 0;
    _listingFailures = 0;
    _vendoredThisRun = 0;
    _vendorFailedThisRun = 0;
    // 0.1.26.0: the planned count is per-run state too. It was the one vendor counter NOT reset
    // here, so after a retainer planned N ops every later retainer's AnnounceRunDone re-tested
    // retainer one's count against its own zeroed counters and printed a FALSE "vendor: planned N
    // op(s) but the leg never executed" ERROR (and the matching chat line) for a run that vendored
    // N/N - each of the three retainers after the 2026-09-07 18:50 session claimed 0 of 5.
    _vendorPlannedCount = 0;
    _vendorPlan = null;
    _vendorStopRequested = false;
    _vendorStopReason = null;
    _deferredAbortRequested = false;
    AutoMarketService.ResetGateHeld();
    _preListPrice = null;
    _preListDone = false;
    CancelPreflightLookup();
    CancelGateLookup();
    CancelUniversalisPriceRequest();
  }

  private void CancelPreflightLookup()
  {
    _preflightRequestId++;
    _preflightDecisions = null;
    _preflightDone = false;
    _preflightCts?.Cancel();
    _preflightCts?.Dispose();
    _preflightCts = null;
  }

  private void ClearCachedPricesIfUniversalisSettingChanged()
  {
    var useUniversalisDataCenterPrices = Plugin.Configuration.UseUniversalisDataCenterPrices;
    if (_cachedPricesUseUniversalisDataCenterPrices == useUniversalisDataCenterPrices)
      return;

    _cachedPrices.Clear();
    _cachedPricesUseUniversalisDataCenterPrices = useUniversalisDataCenterPrices;
  }

  public string DebugState()
  {
    return $"busy={_taskManager.IsBusy} queued={_taskManager.NumQueuedTasks} arSession={_arRetainer ?? "-"} listedTotal={_listedTotal} failures={_listingFailures} " +
           $"marketLoaded={AutoMarketService.IsMarketContainerLoaded()} occupied={AutoMarketService.OccupiedSlotCount()} arInstalled={AutoRetainerIPC.Installed} arSuppressed={AutoRetainerIPC.Suppressed()}";
  }

  private readonly record struct CachedPrice(int Value, bool FromUniversalis);
}
