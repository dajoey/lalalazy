// Game/runtime boundary stubs for the LazyCurrencySpender render harness.
// Everything here replaces something that needs a running game or Dalamud: services, game data sheets, textures,
// the unmanaged addon/agent structs, the plugin entry class and the data/helper classes that read game state.
// The real windows, CurrencyChrome, UiHelper, StringHelper, Config and the shared UI kit are compiled unchanged.
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using CurrencySpender.Classes;
using CurrencySpender.Configuration;
using CurrencySpender.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace ECommons
{
    internal static class GenericHelpers
    {
        public static void ShellStart(string url) { }

        // The Currency addon the overlay anchors to; set by the harness scenario (null = addon closed).
        internal static unsafe FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* CurrencyAddon;

        public static unsafe bool TryGetAddonByName<T>(string name, out T* addon) where T : unmanaged
        {
            if (name == "Currency" && CurrencyAddon != null)
            {
                addon = (T*)CurrencyAddon;
                return true;
            }
            addon = null;
            return false;
        }

        public static unsafe bool IsAddonReady(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* addon) => addon != null;
    }
}

namespace ECommons.ImGuiMethods
{
    internal static class ImGuiEx
    {
        public static void Text(string text) => ImGui.TextUnformatted(text);
    }
}

namespace ECommons.Logging
{
    internal static class PluginLog
    {
        public static void Debug(string message) { }
        public static void Verbose(string message) { }
        public static void Information(string message) { }
        public static void Error(string message) { }
    }
}

namespace FFXIVClientStructs.FFXIV.Component.GUI
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct AtkUnitBase
    {
        public short X;
        public short Y;
    }
}

namespace FFXIVClientStructs.FFXIV.Client.UI.Agent
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AgentMap
    {
        public uint CurrentMapId;
        public uint CurrentTerritoryId;
        private static AgentMap* _instance;

        public static AgentMap* Instance()
        {
            if (_instance == null)
            {
                _instance = (AgentMap*)NativeMemory.AllocZeroed((nuint)sizeof(AgentMap));
                _instance->CurrentMapId = 11;        // map of the starting city
                _instance->CurrentTerritoryId = 128; // its territory
            }
            return _instance;
        }
    }
}

namespace Lumina.Excel.Sheets
{
    // Data-sheet row: only the name is read by the windows.
    internal readonly struct Item
    {
        public readonly string NameText;
        public Item(string name) => NameText = name;
        public ItemName Name => new(NameText);
        public uint RowId => 0;
        public byte Rarity => 1;
        public bool CanBeHq => false;
    }

    internal readonly struct ItemName
    {
        private readonly string _value;
        public ItemName(string value) => _value = value;
        public override string ToString() => _value;
        public string ExtractText() => _value;
    }
}

namespace CurrencySpender
{
    // Fake Dalamud services: DispatchProxy answers every call with a default, except the few members the windows read.
    public class FakeService : DispatchProxy
    {
        internal static bool LoggedIn = true;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? "";
            if (name == "get_LocalPlayer")
                return LoggedIn ? DispatchProxy.Create<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter, FakeService>() : null;
            var type = targetMethod!.ReturnType;
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }

        internal static T Make<T>() where T : class => DispatchProxy.Create<T, FakeService>();
    }

    internal sealed class HarnessSheet<T>
    {
        internal static Func<uint, string> ItemNames = id => $"Item {id}";

        public T GetRow(uint id) => (T)(object)new Lumina.Excel.Sheets.Item(ItemNames(id));
    }

    internal sealed class HarnessDataManager
    {
        public HarnessSheet<T> GetExcelSheet<T>() => new();
    }

    public class Service
    {
        internal static IObjectTable Objects { get; } = FakeService.Make<IObjectTable>();
        internal static IObjectTable ObjectTable => Objects;
        internal static IPluginLog Log { get; } = FakeService.Make<IPluginLog>();
        internal static IGameGui GameGui { get; } = FakeService.Make<IGameGui>();
        internal static IChatGui Chat { get; } = FakeService.Make<IChatGui>();
        internal static INotificationManager Notification { get; } = FakeService.Make<INotificationManager>();
        internal static IAddonLifecycle AddonLifecycle { get; } = FakeService.Make<IAddonLifecycle>();
        internal static HarnessDataManager DataManager { get; } = new();
    }

    internal sealed class HarnessTaskManager
    {
        public void Enqueue(Func<object?> task) { }
    }

    public sealed class HarnessIcon
    {
        public ImTextureID Handle { get; init; }
    }

    // Stub of the plugin entry class: the real one is an IDalamudPlugin that needs the game. Only the members the
    // windows read exist here; the window objects themselves are the real classes.
    public sealed class Plugin
    {
        public string Name => "Lazy Currency Spender";
        internal static Plugin P = null!;
        internal static Config C => P.config;
        public Config config = new();
        internal WindowSystem ws = new("LazyCurrencySpenderHarness");
        internal MainTabWindow mainTabWindow = null!;
        internal ConfigTabWindow configTabWindow = null!;
        internal SpendingWindow spendingWindow = null!;
        internal DebugTabWindow debugTabWindow = null!;
        internal ConfigWizardWindow configWizard = null!;
        internal CurrencyOverlay currencyOverlay = null!;
        internal string? changelogPath;
        public string Version = "1.3.1";
        public bool Problem;
        internal HarnessTaskManager TaskManager = new();
        public List<TrackedCurrency> Currencies = [];

        public TrackedCurrency? GetCurrency(uint itemId) => Currencies?.FirstOrDefault(c => c.ItemId == itemId);

        public void ToggleSpendingUI(TrackedCurrency currency)
        {
            spendingWindow.GetData(currency);
            spendingWindow.IsOpen = true;
        }

        // Same labels as the real Plugin.CollectableTypeLabels.
        internal static readonly Dictionary<CollectableType, string> CollectableTypeLabels = new()
        {
            { CollectableType.Mount, "Mounts" },
            { CollectableType.Minion, "Minions" },
            { CollectableType.Scroll, "Orchestration Scrolls" },
            { CollectableType.Emote, "Emotes" },
            { CollectableType.Hairstyle, "Hairstyles" },
            { CollectableType.Barding, "Bardings" },
            { CollectableType.RidingMap, "Riding Maps" },
            { CollectableType.Facewear, "Facewear" },
            { CollectableType.FramersKit, "Framer's Kits" },
            { CollectableType.TTCard, "Triple Triad Cards" },
            { CollectableType.Mahjong, "Mahjong Voices" },
            { CollectableType.MasterRecipes, "Master Recipes" },
        };
    }
}

namespace CurrencySpender.Classes
{
    [Flags]
    public enum ItemType { None = 0, Tradeable = 1, Sellable = 2, Collectable = 4, Venture = 8, Currency = 16 }

    public enum CollectableType
    {
        None, Mount, Minion, Scroll, Emote, Hairstyle, Barding, RidingMap, Facewear, FramersKit, TTCard, Mahjong,
        Container, MasterRecipes, FashionAccessory,
    }

    public enum ShopType { FccShop, GCShop, GilShop, SpecialShop, FateShop, EmptyShop, CustomShop }

    public enum CurrencyType { Item, HighQualityItem, Collectable, NonLimitedTomestone, LimitedTomestone }

    public static class KofiBanner
    {
        public static string DonateLink => "https://ko-fi.com/catz1911";
    }

    public class Location
    {
        public uint MapId { get; init; }
        public uint TerritoryId { get; init; }
        public uint NpcId { get; init; }
        public bool NeedsPresence;
        public uint? BackupNpc;
        public record Pos(float X, float Y);
        public Pos Position { get; init; } = new(0, 0);
        public string Zone { get; set; } = "Unknown";
        public static readonly List<Location> Locations = [];
        public MapLinkPayload? GetMapMarker() => null;
        public void Teleport() { }
    }

    public class Shop
    {
        public required ShopType Type { get; init; }
        public uint ShopId { get; init; }
        public string? ShopName { get; set; }
        public uint NpcId { get; init; }
        public string NpcName { get; set; } = "Unknown";
        public required Location Location { get; init; }
        public bool Disabled;
    }

    public class ShopItem
    {
        public ItemType Type { get; set; }
        public CollectableType CollectableType { get; set; }
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public uint Category { get; set; }
        public uint Price { get; set; }
        public uint Currency { get; set; }
        public uint ShopId { get; set; }
        public required Shop Shop { get; set; }
        public bool Disabled;
        public uint CurrentPrice { get; set; }
        public uint AmountCanBuy
        {
            get
            {
                if (Price == 0) return 0;
                var cur = P.GetCurrency(Currency);
                if (cur == null) return 0;
                return (uint)Math.Max(0, cur.CurrentCount) / Price;
            }
        }
        public uint Profit { get; set; }
        public uint HasSoldWeek { get; set; }
    }

    public class TrackedCurrency
    {
        public required CurrencyType Type { get; init; }
        public uint ItemId { get; init; }
        public string Name { get; init; } = "";
        public HarnessIcon Icon { get; init; } = new();
        public List<uint>? Children;
        public int? Price;
        public bool Child;
        public bool Enabled = true;
        public int CurrentCount;
        public int MaxCount;
        public float Percentage => MaxCount != 0 ? CurrentCount * 100 / MaxCount : 0;
    }
}

namespace CurrencySpender.Data
{
    internal static class Generator
    {
        public static List<Shop> shops = new();
        public static List<ShopItem> items = new();
    }

    internal static class ShopGen
    {
        public static void init() { }
    }
}

namespace CurrencySpender.Helpers
{
    internal static class ItemHelper
    {
        public static Dictionary<uint, (uint, uint)> ContainerUnlocked = new();
        internal static HashSet<uint> Unlocked = new();
        public static bool IsUnlocked(uint id) => Unlocked.Contains(id);
        public static bool IsPrereqMet(ShopItem item) => true;
    }

    internal static class PlayerHelper
    {
        public static Dictionary<uint, uint> GCRanks = new() { { 1, 99 }, { 2, 99 }, { 3, 99 } };
        public static bool GCRanksCreated = true;
        public static Dictionary<uint, uint> SharedFateRanks = new();
        public static bool SharedFateRanksMax = true;
        public static void init() { }
        public static void openSharedFate() { }
    }

    internal static class WebHelper
    {
        public static bool CheckAll(uint currency, bool force = false) => true;
    }

    // LowerVersionThan / LastVersion / OpenConfigWizard are the real VersionHelper logic (the real class also reads
    // game memory, so it cannot be compiled here).
    internal static class VersionHelper
    {
        public static void OpenConfigWizard()
        {
            P.configWizard.SetVersion(LastVersion());
            P.configWizard.IsOpen = true;
        }

        public static bool LowerVersionThan(string version, string version2)
        {
            var v1 = version.Split('.');
            var v2 = version2.Split('.');
            int major1 = int.Parse(v1[0]), minor1 = int.Parse(v1[1]), patch1 = int.Parse(v1[2]);
            int major2 = int.Parse(v2[0]), minor2 = int.Parse(v2[1]), patch2 = int.Parse(v2[2]);
            if (major2 < major1) return true;
            if (major2 > major1) return false;
            if (minor2 < minor1) return true;
            if (minor2 > minor1) return false;
            return patch2 < patch1;
        }

        public static string LastVersion()
        {
            var parts = C.Version.Split('.');
            int major = int.Parse(parts[0]), minor = int.Parse(parts[1]), patch = int.Parse(parts[2]);
            patch--;
            if (patch < 0) { patch = 0; minor--; }
            if (minor < 0) { minor = 0; major--; }
            if (major < 0) major = 0;
            return major + "." + minor + "." + patch;
        }
    }
}
