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

  // Empty-board sale-history fallback (0.1.8.0). The in-game "Compare Prices" path is synchronous and
  // has already failed by the time SetNewPrice runs, so the fallback lookup is fired from there and the
  // step re-runs until it lands. Keyed by item name so one lookup happens per listing, not per retry.
  private string? _historyRequestItem;
  private bool _historyRequestDone;
  private long _historyRequestDeadline;

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
    _taskManager.Enqueue(() => Communicator.PrintSweepDone(_listedTotal, _listingFailures), "AnnounceSweepDone");
    _taskManager.Enqueue(() => AutoRetainerIPC.Suppressed(false));
  }

  /// <summary>Pinch the retainer whose sell list is open.</summary>
  public void PinchCurrentRetainer()
  {
    _mbHandler.PopulateRetainerCache();
    if (_taskManager.IsBusy)
      return;

    ClearState();
    EnqueueAllRetainerItems(EnqueueSingleItem, false);
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
    _taskManager.Enqueue(() => Communicator.PrintSweepDone(_listedTotal, _listingFailures), "AnnounceDone");
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
      _taskManager.Enqueue(() => EnqueueAllRetainerItems(InsertSingleItem, true), $"EnqueueAllRetainerItems{index}");
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
      var wanted = Plugin.AddonText(2380);
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
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      addon->Close(true);
      return true;
    }
    return false;
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
      var plan = AutoMarketService.BuildPlan();
      foreach (var note in plan.Notes)
        Svc.Log.Information($"[LMC] plan: {note}");

      if (plan.Ops.Count == 0)
      {
        Communicator.PrintInfo(plan.Notes.Count > 0 ? $"Nothing to list ({plan.Notes[0]})." : "Nothing to list.");
      }
      else
      {
        Svc.Log.Information($"[LMC] plan: {plan.Ops.Count} listing(s): {string.Join(", ", plan.Ops.Select(o => $"{o.ItemId}{(o.HQ ? "HQ" : "")}x{o.Quantity}->#{o.TargetSlot}"))}");
        foreach (var op in plan.Ops)
          AddListingSteps(steps, op);
      }
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
          EnqueueAllRetainerItems(InsertSingleItem, true);
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
    _taskManager.Enqueue(() => Communicator.PrintSweepDone(_listedTotal, _listingFailures), "AR.Announce");
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
        // recent-sales median if it is switched on. The Universalis price path already consulted the
        // sale history in its own request, so this only covers the in-game Compare Prices path.
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
            }
          }
        }

        if (!(_newPrice > 0))
        {
          if (Plugin.Configuration.DefaultAmount == 0)
          {
            Svc.Log.Warning("[LMC] SetNewPrice: no price to set");
            Communicator.PrintNoPriceToSetError(itemName, isPlaceholder);
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
    _preListPrice = null;
    _preListDone = false;
    CancelUniversalisPriceRequest();
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
