// Runtime boundary stubs for the UI render harness. Everything the real window sources reach for that lives in
// the game, Dalamud services, ECommons, Lumina data sheets or FFXIVClientStructs is replaced here by a small
// stand-in with the same member names, so the REAL window, chrome and tab sources compile unchanged.
// Nothing here draws; the only drawing code under test is the real source.
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Lalalazy.Telemetry;
using LazyMarketCompanion;
using LazyMarketCompanion.AutoMarket;
using LazyMarketCompanion.Inventory;

// ---------------------------------------------------------------------------------------------------------------
// FFXIVClientStructs shapes the windows read (inventory types and addon geometry).
// ---------------------------------------------------------------------------------------------------------------
namespace FFXIVClientStructs.FFXIV.Client.Game
{
  /// <summary>The InventoryType members the plugin's UI names (numeric values match the game's enum).</summary>
  public enum InventoryType : uint
  {
    Inventory1 = 0,
    Inventory2 = 1,
    Inventory3 = 2,
    Inventory4 = 3,
    ArmoryOffHand = 3200,
    ArmoryHead = 3201,
    ArmoryBody = 3202,
    ArmoryHands = 3203,
    ArmoryLegs = 3205,
    ArmoryFeets = 3206,
    ArmoryEar = 3207,
    ArmoryNeck = 3208,
    ArmoryWrist = 3209,
    ArmoryRings = 3300,
    ArmorySoulCrystal = 3400,
    ArmoryMainHand = 3500,
    RetainerPage1 = 10000,
    RetainerPage2 = 10001,
    RetainerPage3 = 10002,
    RetainerPage4 = 10003,
    RetainerPage5 = 10004,
    RetainerPage6 = 10005,
    RetainerPage7 = 10006,
  }
}

namespace FFXIVClientStructs.FFXIV.Component.GUI
{
  /// <summary>Root node geometry: only the fields the plugin's overlay math reads.</summary>
  public unsafe struct AtkResNode
  {
    public ushort Width;
    public ushort Height;
    public float ScaleX;
    public float ScaleY;
    public float ScreenX;
    public float ScreenY;
    public byte Visible;
    public bool IsVisible() => Visible != 0;
  }

  /// <summary>Addon geometry: only the fields the plugin reads (position, scale, root node, visibility).</summary>
  public unsafe struct AtkUnitBase
  {
    public short X;
    public short Y;
    public float Scale;
    public AtkResNode* RootNode;
    public byte VisibleFlag;
    public bool IsVisible => VisibleFlag != 0;
  }
}

// ---------------------------------------------------------------------------------------------------------------
// ECommons surface: addon lookup (backed by a harness addon table), the retainer-list reader and the service log.
// ---------------------------------------------------------------------------------------------------------------
namespace ECommons
{
  using FFXIVClientStructs.FFXIV.Component.GUI;

  /// <summary>Addon lookup backed by <see cref="HarnessAddons"/>: unknown addons are simply not open.</summary>
  public static unsafe class GenericHelpers
  {
    public static bool TryGetAddonByName<T>(string name, out T* addon) where T : unmanaged
    {
      if (HarnessAddons.Table.TryGetValue(name, out var pointer) && pointer != IntPtr.Zero)
      {
        addon = (T*)pointer;
        return true;
      }
      addon = null;
      return false;
    }

    public static bool IsAddonReady(AtkUnitBase* addon) => addon != null && addon->IsVisible;
  }

  /// <summary>The game addons a scenario declares as open. Memory is owned here and freed by <see cref="Clear"/>.</summary>
  public static unsafe class HarnessAddons
  {
    internal static readonly Dictionary<string, IntPtr> Table = new(StringComparer.Ordinal);
    private static readonly List<IntPtr> Owned = [];

    /// <summary>Opens a stub addon: screen position (game pixels), root node size and scale.</summary>
    public static void Open(string name, float x, float y, float width, float height, float scale = 1f)
    {
      var rootMemory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(AtkResNode));
      var addonMemory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(AtkUnitBase));
      Owned.Add(rootMemory);
      Owned.Add(addonMemory);
      *(AtkResNode*)rootMemory = new AtkResNode
      {
        Width = (ushort)MathF.Round(width / scale),
        Height = (ushort)MathF.Round(height / scale),
        ScaleX = scale,
        ScaleY = scale,
        ScreenX = x,
        ScreenY = y,
        Visible = 1,
      };
      *(AtkUnitBase*)addonMemory = new AtkUnitBase
      {
        X = (short)x,
        Y = (short)y,
        Scale = scale,
        RootNode = (AtkResNode*)rootMemory,
        VisibleFlag = 1,
      };
      Table[name] = addonMemory;
    }

    public static void Clear()
    {
      Table.Clear();
      foreach (var pointer in Owned)
        System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer);
      Owned.Clear();
    }
  }
}

namespace ECommons.UIHelpers.AddonMasterImplementations
{
  using FFXIVClientStructs.FFXIV.Component.GUI;

  /// <summary>The retainer-list reader. The harness never opens the in-game RetainerList, so it reads empty.</summary>
  public static class AddonMaster
  {
    public sealed unsafe class RetainerList
    {
      public RetainerList(AtkUnitBase* addon) { }
      public IReadOnlyList<Entry> Retainers { get; } = [];
      public sealed class Entry { public string Name => string.Empty; }
    }
  }
}

namespace ECommons.DalamudServices
{
  /// <summary>The Dalamud services the compiled UI sources touch.</summary>
  internal static class Svc
  {
    public static HarnessLog Log { get; } = new();
    public static HarnessData Data { get; } = new();
    public static HarnessGameGui GameGui { get; } = new();
  }

  internal sealed class HarnessLog
  {
    public void Information(string message) { }
    public void Debug(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
  }

  internal sealed class HarnessGameGui
  {
    /// <summary>The game's hovered-item id (HQ = +1,000,000). Scenarios set it for the owner tooltip.</summary>
    public ulong HoveredItem { get; set; }
  }

  /// <summary>Stand-in for the Item data sheet: the plugin's own catalogue of real market item names.</summary>
  internal sealed class HarnessData
  {
    public HarnessSheet<T> GetExcelSheet<T>() where T : struct => new();
  }

  internal sealed class HarnessSheet<T> : IEnumerable<T> where T : struct
  {
    private static IEnumerable<T> Rows => typeof(T) == typeof(Lumina.Excel.Sheets.Item)
      ? (IEnumerable<T>)(object)Fixtures.Items.Select(i => i.ToSheetRow()).ToList()
      : [];

    public bool TryGetRow(uint rowId, out T row)
    {
      foreach (var candidate in Rows)
      {
        if (candidate is Lumina.Excel.Sheets.Item item && item.RowId == rowId)
        {
          row = candidate;
          return true;
        }
      }
      row = default;
      return false;
    }

    public IEnumerator<T> GetEnumerator() => Rows.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
  }
}

namespace Lumina.Excel.Sheets
{
  public readonly record struct SheetRef(uint RowId);

  /// <summary>The Item sheet columns the plugin's UI reads.</summary>
  public readonly record struct Item(uint RowId, string Name, bool IsUntradable, bool CanBeHq, SheetRef ItemSearchCategory);
}

// ---------------------------------------------------------------------------------------------------------------
// Plugin entry-point statics the windows read.
// ---------------------------------------------------------------------------------------------------------------
namespace LazyMarketCompanion
{
  using LazyMarketCompanion.AutoMarket;

  /// <summary>The bag-grid addon names the owner tooltip hit-tests (same lists as the real marker window).</summary>
  internal static class AutoMarketMarkers
  {
    internal static readonly string[] GridNames =
    [
      "InventoryGrid", "InventoryGrid0", "InventoryGrid1",
      "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    ];

    internal static readonly string[] RetainerGridNames =
      new[] { "RetainerGrid" }.Concat(Enumerable.Range(0, RetainerGridMap.PageCount).Select(i => $"RetainerGrid{i}")).ToArray();
  }

  /// <summary>Item sheet reads, served from the harness catalogue instead of the game's data files.</summary>
  internal static class ItemNameResolver
  {
    public static string GetItemName(uint itemId) => Fixtures.Name(itemId);

    public static uint SearchCategoryId(uint itemId)
      => Fixtures.Items.FirstOrDefault(i => i.Id == itemId) is { Untradable: false } def ? def.Category : 0;

    public static string GetSearchCategoryName(uint categoryId)
      => Fixtures.Categories.TryGetValue(categoryId, out var name) ? name : $"Category {categoryId}";

    public static bool CanBeHq(uint itemId) => Fixtures.Items.Any(i => i.Id == itemId && i.CanHq);

    public static uint MaxStack(uint itemId) => Fixtures.Items.FirstOrDefault(i => i.Id == itemId)?.Stack ?? 1u;

    public static bool IsMarketable(uint itemId) => SearchCategoryId(itemId) != 0;
  }

  /// <summary>Stand-in for the plugin class: configuration, telemetry and data access are the only members the UI reaches.</summary>
  public static class Plugin
  {
    public static Configuration Configuration { get; set; } = new();
    internal static DalamudTelemetry? Telemetry { get; set; } = new();
    internal static ECommons.DalamudServices.HarnessData DataManager { get; } = ECommons.DalamudServices.Svc.Data;
    public static HarnessPluginInterface PluginInterface { get; } = new();

    public sealed class HarnessPluginInterface
    {
      /// <summary>Configuration.Save lands here; the harness never writes a file.</summary>
      public int SaveCount { get; private set; }
      public void SavePluginConfig(Configuration config) => SaveCount++;
    }
  }

  /// <summary>AutoRetainer presence: scenarios choose installed or not.</summary>
  public static class AutoRetainerIPC
  {
    public const string Name = "AutoRetainer";
    public static bool Installed { get; set; } = true;
  }

  /// <summary>The log prefix the Diagnostics card prints (the real constant lives in MarketTelemetryFormat).</summary>
  internal static class MarketTelemetry
  {
    public const string Prefix = MarketTelemetryFormat.Prefix;
  }
}

namespace LazyMarketCompanion.AutoMarket
{
  using FFXIVClientStructs.FFXIV.Client.Game;

  /// <summary>Only the container-name helper the quarantined-moves list prints; the real class drives the game.</summary>
  internal static class AutoMarketService
  {
    public static string NameOfContainer(InventoryType type) => type switch
    {
      >= InventoryType.RetainerPage1 and <= InventoryType.RetainerPage7 => "RetainerPage" + ((int)type - (int)InventoryType.RetainerPage1 + 1),
      >= InventoryType.Inventory1 and <= InventoryType.Inventory4 => "Inventory" + ((int)type + 1),
      _ => type.ToString(),
    };
  }

}

namespace Lalalazy.Telemetry
{
  /// <summary>The report button owner. The button and popup are the real shared ReportPopup; filing a report is a no-op.</summary>
  public sealed class DalamudTelemetry
  {
    private readonly ReportPopup _popup;

    public TelemetryHub Hub { get; }

    public DalamudTelemetry()
    {
      Hub = new TelemetryHub(
        new TelemetryIdentity { Plugin = "LazyMarketCompanion", DisplayName = "Lazy Market Companion", Version = "0.0.0.0" },
        new NullSink());
      _popup = new ReportPopup(this);
    }

    private sealed class NullSink : ITelemetrySink
    {
      public void Write(TelemetryLevel level, string text) { }

      public void Notice(string text) { }
    }

    public string? FileReport(string text) => "RP-0001";

    public void DrawReportButton(string label = "Report a problem") => _popup.DrawButton(label);
  }
}
