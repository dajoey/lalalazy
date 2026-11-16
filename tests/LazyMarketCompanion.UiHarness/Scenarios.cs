// The scenario table: what state each image sets up, at which size, and how the real window is hosted.
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.DalamudServices;
using LazyMarketCompanion;
using LazyMarketCompanion.Inventory;
using LazyMarketCompanion.Windows;

namespace LazyMarketCompanion.UiHarness;

/// <summary>A scenario's live state: drawn once per frame by the host loop.</summary>
internal interface IScenarioState
{
  void DrawFrame(int frameIndex, Vector2 size);
}

internal sealed record Scenario(string Name, int Width, int Height, Func<IScenarioState> Prepare)
{
  public float? Scale { get; init; }
  public int Frames { get; init; } = 3;
  public Vector2? MousePos { get; init; }
  public (int R, int G, int B) Background { get; init; } = (20, 20, 24);
}

internal static class Scenarios
{
  /// <summary>The Config window's default width and a height that shows the top of a page without scrolling.</summary>
  private const int Wide = 1100;

  public static List<Scenario> Build(bool game)
  {
    var list = new List<Scenario>
    {
      // ---- Config window: Auto-Market page ----
      Config("cfg-first-run-automarket-tall-1100x1900", Wide, 1900, new ConfigSetup { Page = "AutoMarket", Cfg = Fixtures.FirstRun, Inv = Fixtures.BlankInventory }),
      Config("cfg-automarket-typical-1100x760", Wide, 760, new ConfigSetup { Page = "AutoMarket" }),
      Config("cfg-automarket-typical-tall-1100x2900", Wide, 2900, new ConfigSetup { Page = "AutoMarket", Search = "Titanium", Ledger = true }),
      Config("cfg-automarket-entries-1100x3800", Wide, 3800, new ConfigSetup { Page = "AutoMarket", Search = "Titanium", Ledger = true }),
      Config("cfg-automarket-entries-narrow-620x4600", 620, 4600, new ConfigSetup { Page = "AutoMarket" }),
      Config("cfg-automarket-typical-620x420", 620, 420, new ConfigSetup { Page = "AutoMarket" }),
      Config("cfg-automarket-busy-narrow-620x640", 620, 640, new ConfigSetup { Page = "AutoMarket", Busy = true }),
      Config("cfg-automarket-off-no-autoretainer-1100x760", Wide, 760, new ConfigSetup
      {
        Page = "AutoMarket", ArInstalled = false, Edit = c => { c.AutoMarketEnabled = false; c.AutoMarketPinchAllAfter = true; },
      }),
      // ---- Config window: Price Matching, Min/Max Prices ----
      Config("cfg-pricematching-1100x1500", Wide, 1500, new ConfigSetup { Page = "PriceMatching" }),
      Config("cfg-pricematching-narrow-620x1500", 620, 1500, new ConfigSetup
      {
        Page = "PriceMatching", Edit = c => { c.UndercutMode = UndercutMode.Percentage; c.UndercutAmount = 3; c.UseUniversalisSaleHistoryFallback = false; },
      }),
      Config("cfg-pricelimits-1100x760", Wide, 760, new ConfigSetup { Page = "PriceLimits" }),
      Config("cfg-pricelimits-empty-620x420", 620, 420, new ConfigSetup { Page = "PriceLimits", Cfg = Fixtures.FirstRun, Inv = Fixtures.BlankInventory }),
      // ---- Config window: Inventory ----
      Config("cfg-inventory-typical-tall-1100x2300", Wide, 2300, new ConfigSetup { Page = "Inventory", Retainer = "Larkspur", Edit = c => c.VentureLootOptIns = [2803, 2804, 2805, 2808] }),
      Config("cfg-inventory-blocked-narrow-620x1700", 620, 1700, new ConfigSetup { Page = "Inventory", Cfg = Fixtures.FirstRun, Inv = Fixtures.BlankInventory, ArInstalled = false }),
      Config("cfg-inventory-batch-running-1100x760", Wide, 760, new ConfigSetup { Page = "Inventory", Retainer = "Larkspur", Batch = "gear: 2/3 (manual)" }),
      // ---- Config window: Retainers & Hotkeys ----
      Config("cfg-setup-typical-1100x1100", Wide, 1100, new ConfigSetup { Page = "Setup" }),
      Config("cfg-setup-no-retainers-narrow-620x900", 620, 900, new ConfigSetup
      {
        Page = "Setup", Cfg = Fixtures.FirstRun, Inv = Fixtures.BlankInventory,
        Edit = c => { c.EnablePostPinchkey = false; c.ShowSweepToBagsOverlayButton = false; c.ShowAutoMarketOverlayButton = false; },
      }),
      // ---- Auto-Market panel ----
      Panel("panel-docked-typical-1000x800", 1000, 800, new PanelSetup { InventoryRect = new Vector4(60, 40, 420, 640) }),
      Panel("panel-typical-tall-380x3000", 380, 3000, new PanelSetup()),
      Panel("panel-first-run-tall-380x1500", 380, 1500, new PanelSetup { Cfg = Fixtures.FirstRun, Inv = Fixtures.BlankInventory }),
      Panel("panel-busy-narrowest-360x420", 360, 420, new PanelSetup { Busy = true }),
      Panel("panel-busy-narrow-360x640", 360, 640, new PanelSetup { Busy = true }),
      Panel("panel-modal-add-all-380x720", 380, 720, new PanelSetup { Modal = "Add all visible items?", Status = AutoMarket.AutoMarketPanelModel.StatusFilter.NotOnList }),
      // ---- owner tooltip (frameless overlay under the game's item tooltip) ----
      Tooltip("tooltip-owners-several-700x520", 700, 520, 5502, true, Fixtures.TypicalInventory),
      Tooltip("tooltip-owners-nothing-ar-missing-700x420", 700, 420, 4551, false, Fixtures.BlankInventory),
      // ---- retainer-window overlay buttons (frameless, anchored to game windows) ----
      new Scenario("overlay-buttons-all-states-620x360", 620, 360, () => new OverlayState()) { Background = (30, 32, 38) },
    };
    if (game)
      list.Add(Config("scale150-cfg-automarket-typical-1100x760", Wide, 760, new ConfigSetup { Page = "AutoMarket" }) with { Scale = 1.5f });
    return list;
  }

  private static Scenario Config(string name, int width, int height, ConfigSetup setup)
    => new(name, width, height, () => new ConfigState(setup));

  private static Scenario Panel(string name, int width, int height, PanelSetup setup)
    => new(name, width, height, () => new PanelState(setup)) { Background = setup.InventoryRect is null ? (20, 20, 24) : (28, 30, 36) };

  private static Scenario Tooltip(string name, int width, int height, uint itemId, bool hq, Func<InventoryService> inventory)
    => new(name, width, height, () => new TooltipState(itemId, hq, inventory()))
    {
      Background = (30, 32, 38),
      MousePos = new Vector2(150, 150),
    };
}

// =====================================================================================================
// Config window
// =====================================================================================================

internal sealed class ConfigSetup
{
  public string Page { get; init; } = "AutoMarket";
  public Func<Configuration> Cfg { get; init; } = Fixtures.Typical;
  public Func<InventoryService> Inv { get; init; } = Fixtures.TypicalInventory;
  public Action<Configuration>? Edit { get; init; }
  public bool Busy { get; init; }
  public bool ArInstalled { get; init; } = true;
  public string? Search { get; init; }
  public bool Ledger { get; init; }
  public string? Retainer { get; init; }
  public string? Batch { get; init; }
}

internal sealed class ConfigState : IScenarioState
{
  private readonly ConfigWindow _window;

  public ConfigState(ConfigSetup setup)
  {
    var cfg = setup.Cfg();
    setup.Edit?.Invoke(cfg);
    Plugin.Configuration = cfg;
    AutoRetainerIPC.Installed = setup.ArInstalled;
    var inventory = setup.Inv();
    if (setup.Batch is not null)
    {
      inventory.BatchRunning = true;
      inventory.BatchStatus = setup.Batch;
    }
    var tab = new InventoryTab(inventory);
    if (setup.Retainer is not null)
      SetPrivate(tab, "_retainer", setup.Retainer); // the selected retainer row; its last-seen pages are shown

    _window = new ConfigWindow
    {
      IsOpen = true,
      AutomationBusy = () => setup.Busy,
      CancelAutomation = _ => { },
      DrawInventoryTab = tab.Draw,
    };
    if (setup.Ledger)
    {
      // Stamps are floored to the hour so "(5h ago)" stays stable for the hour after a run.
      var hour = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, DateTimeKind.Utc);
      IReadOnlyList<(string Key, DateTime RecordedAtUtc)> ledger =
      [
        ("Larkspur|10000:2:12537:nq", hour.AddHours(-5)),
        ("Saltmarsh|10002:14:5114:nq", hour.AddHours(-9)),
        ("Brindle|3:11:41878:hq", hour.AddMinutes(-40)),
        ("not-a-parsable-key", hour.AddHours(-26)),
      ];
      _window.ReconcileLedgerSource = () => ledger;
      _window.ReconcileLedgerRelease = _ => true;
      _window.ReconcileLedgerReleaseAll = () => 0;
    }

    SetPrivate(_window, "_currentPage", Enum.Parse(typeof(ConfigWindow).GetField("_currentPage", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType, setup.Page));
    if (setup.Search is not null)
      SetPrivate(_window, "_search", setup.Search);
  }

  public void DrawFrame(int frameIndex, Vector2 size)
  {
    _window.PreDraw();
    SceneHost.BeginWindow(_window.WindowName, size, _window.Flags);
    _window.Draw();
    ImGui.End();
    _window.PostDraw();
  }

  internal static void SetPrivate(object target, string field, object value)
  {
    var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} no longer exists; update the harness.");
    info.SetValue(target, value);
  }
}

// =====================================================================================================
// Auto-Market panel
// =====================================================================================================

internal sealed class PanelSetup
{
  public Func<Configuration> Cfg { get; init; } = Fixtures.Typical;
  public Func<InventoryService> Inv { get; init; } = Fixtures.TypicalInventory;
  public bool Busy { get; init; }
  /// <summary>x, y, width, height of the game's inventory window (null = no inventory open; the panel floats).</summary>
  public Vector4? InventoryRect { get; init; }
  public string? Modal { get; init; }
  public AutoMarket.AutoMarketPanelModel.StatusFilter? Status { get; init; }
}

internal sealed class PanelState : IScenarioState
{
  private readonly PanelSetup _setup;
  private readonly AutoMarketPanelWindow _window;

  public PanelState(PanelSetup setup)
  {
    _setup = setup;
    var cfg = setup.Cfg();
    if (setup.Status is { } status)
      cfg.AutoMarketPanelStatus = status;
    Plugin.Configuration = cfg;
    AutoRetainerIPC.Installed = true;
    _window = new AutoMarketPanelWindow(setup.Inv(), () => setup.Busy, _ => { });
    _window.IsOpen = true;
    if (setup.InventoryRect is { } r)
      HarnessAddons.Open("InventoryExpansion", r.X, r.Y, r.Z, r.W);
  }

  public void DrawFrame(int frameIndex, Vector2 size)
  {
    // The panel's own per-frame watcher decides docked or floating from the inventory it can read.
    _window.Watch();
    var position = _window.Position ?? Vector2.Zero;
    if (_setup.InventoryRect is { } r)
    {
      var background = ImGui.GetBackgroundDrawList();
      background.AddRectFilled(new Vector2(r.X, r.Y), new Vector2(r.X + r.Z, r.Y + r.W), ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.21f, 0.25f, 1f)), 6f);
      background.AddRect(new Vector2(r.X, r.Y), new Vector2(r.X + r.Z, r.Y + r.W), ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.42f, 0.48f, 1f)), 6f);
    }

    var panelSize = _setup.InventoryRect is null ? size : new Vector2(380, 720);
    _window.PreDraw();
    SceneHost.BeginWindow(_window.WindowName, panelSize, _window.Flags, position);
    if (_setup.Modal is { } modal && frameIndex == 0)
      ImGui.OpenPopup(modal); // same window scope as the real modal's BeginPopupModal
    _window.Draw();
    ImGui.End();
    _window.PostDraw();
  }
}

// =====================================================================================================
// Owner tooltip
// =====================================================================================================

internal sealed class TooltipState : IScenarioState
{
  private readonly OwnerTooltip _window;

  public TooltipState(uint itemId, bool hq, InventoryService inventory)
  {
    Plugin.Configuration = Fixtures.Typical();
    Plugin.Configuration.InventoryOwnerTooltip = true;
    Svc.GameGui.HoveredItem = hq ? itemId + 1_000_000UL : itemId;
    HarnessAddons.Open("InventoryExpansion", 20, 40, 220, 400);
    HarnessAddons.Open("ItemDetail", 260, 40, 300, 180);
    _window = new OwnerTooltip(inventory);
  }

  public void DrawFrame(int frameIndex, Vector2 size)
  {
    // Plain placeholders for the game's own windows; the real tooltip box is placed under the item detail one.
    var background = ImGui.GetBackgroundDrawList();
    background.AddRectFilled(new Vector2(20, 40), new Vector2(240, 440), ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.21f, 0.25f, 1f)), 6f);
    background.AddRectFilled(new Vector2(260, 40), new Vector2(560, 220), ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.21f, 0.25f, 1f)), 6f);
    _window.Draw();
  }
}

// =====================================================================================================
// Retainer overlay buttons (see OverlayMirror)
// =====================================================================================================

internal sealed class OverlayState : IScenarioState
{
  public OverlayState()
  {
    var cfg = Fixtures.Typical();
    Plugin.Configuration = cfg;
  }

  public void DrawFrame(int frameIndex, Vector2 size)
  {
    var background = ImGui.GetBackgroundDrawList();
    var panel = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.21f, 0.25f, 1f));
    for (var row = 0; row < 3; row++)
      background.AddRectFilled(new Vector2(20, 20 + row * 110), new Vector2(size.X - 20, 110 + row * 110), panel, 6f);

    // Row 1: the retainer list's three buttons. Row 2: the sell list's two, Auto Market disabled in settings. Row 3: a run in progress.
    Plugin.Configuration.AutoMarketEnabled = true;
    OverlayMirror.Draw("###LMCOverlayList", new Vector2(36, 36),
      [("Auto Pinch", "t"), ("Auto Market", "t"), ("Sweep to Bags", "t")], busy: false, arSession: false);
    Plugin.Configuration.AutoMarketEnabled = false;
    OverlayMirror.Draw("###LMCOverlaySell", new Vector2(36, 146),
      [("Auto Pinch", "t"), ("Auto Market", "t")], busy: false, arSession: false);
    Plugin.Configuration.AutoMarketEnabled = true;
    OverlayMirror.Draw("###LMCOverlayBusy", new Vector2(36, 256), [], busy: true, arSession: true);
  }
}

internal static class SceneHost
{
  /// <summary>Hosts a real Dalamud window's Draw the way the game does for a fixed-size, frameless window.</summary>
  public static void BeginWindow(string name, Vector2 size, ImGuiWindowFlags flags, Vector2? position = null)
  {
    ImGui.SetNextWindowPos(position ?? Vector2.Zero);
    ImGui.SetNextWindowSize(size);
    var open = true;
    ImGui.Begin(name, ref open, flags | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
  }
}
