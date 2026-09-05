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

  // Pre-listing Universalis lookups (UniversalisFirst mode)
  private int? _preListPrice;
  private bool _preListDone;

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
          ("Auto Market", "Auto-Market then Auto Pinch every enabled retainer.\r\nLists your always-sell items, then matches prices.", () => SweepAllRetainers(true)));
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
          ("Auto Market", "List your always-sell items on this retainer, then match prices.", AutoMarketCurrentRetainer));
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

    // Pinch afterwards. "All" reuses the original per-row chain; "new only" prices just the slots we filled.
    steps.Add(new Step(() =>
    {
      if (Plugin.Configuration.AutoMarketPinchAllAfter || _listedThisRetainer.Count == 0)
      {
        EnqueueAllRetainerItems(InsertSingleItem, true);
      }
      else
      {
        // reverse because Insert
        foreach (var op in _listedThisRetainer.OrderByDescending(o => o.TargetSlot))
        {
          if (op.FixedPrice > 0) continue;
          var row = AutoMarketService.ListIndexOfSlot(op.TargetSlot);
          InsertSingleItem(row);
        }
      }
      return true;
    }, "PinchAfterMarket", DelayAfterMs: 0));

    InsertSteps(steps);
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

  private unsafe bool? EnqueueAllRetainerItems(Action<int> enqueueFunc, bool reverseOrder)
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
      var listNode = (AtkComponentNode*)addon->UldManager.NodeList[10];
      var listComponent = (AtkComponentList*)listNode->Component;
      int num = listComponent->ListLength;
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
    return false;
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

  private static unsafe bool? OpenItemContextMenu(int itemIndex)
  {
    if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
    {
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
          Communicator.PrintUsingDefaultAmountWarning(itemName, _newPrice.Value);
        }

        var rawItemName = GetRetainerSellRawItemName(retainerSell);
        var limitedPrice = ApplyItemPriceLimits(itemName, rawItemName, _newPrice.Value);
        if (limitedPrice != _newPrice.Value)
        {
          Svc.Log.Debug($"[LMC] {itemName}: price limit adjusted {_newPrice.Value} to {limitedPrice}");
          _newPrice = limitedPrice;
        }

        var cutPercentage = ((float)_newPrice.Value - _oldPrice.Value) / _oldPrice.Value * 100f;
        // A placeholder listing is always allowed to drop to the real price; the max-cut guard is for real listings.
        if (isPlaceholder || cutPercentage >= -Plugin.Configuration.MaxUndercutPercentage)
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
      _skipCurrentItem = false;
    }
  }

  private void MBHandler_NewPriceReceived(object? sender, NewPriceEventArgs e)
  {
    Svc.Log.Debug($"[LMC] new price received: {e.NewPrice}");
    _newPrice = e.NewPrice;
    _newPriceFromUniversalis = false;
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
    });
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
    _cachedPrices = [];
    _cachedPricesUseUniversalisDataCenterPrices = Plugin.Configuration.UseUniversalisDataCenterPrices;
    _skipCurrentItem = false;
    _listedThisRetainer.Clear();
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
