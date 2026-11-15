using System.Numerics;
using LazyCrucible.Policy;

namespace Dalamud.Interface
{
    public enum FontAwesomeIcon
    {
        Paw,
    }

    public static class HarnessIconText
    {
        // The production icon is supplied by Dalamud's icon font. The harness uses a
        // plain glyph because that font is deliberately outside this test project.
        public static string ToIconString(this FontAwesomeIcon icon) => "P";
    }
}

namespace Dalamud.Interface.Windowing
{
    public struct WindowSizeConstraints
    {
        public Vector2 MinimumSize { get; set; }
        public Vector2 MaximumSize { get; set; }
    }

    public abstract class Window
    {
        protected Window(string name) => Name = name;
        public string Name { get; }
        public WindowSizeConstraints SizeConstraints { get; set; }
        public Vector2? Size { get; set; }
        public ImGuiCond SizeCondition { get; set; }
        public bool IsOpen { get; set; }
        public abstract void Draw();
    }
}

namespace ECommons.DalamudServices
{
    internal static class Svc
    {
        public static HarnessClientState ClientState { get; } = new();
    }

    internal sealed class HarnessClientState
    {
        public uint TerritoryType { get; set; }
    }
}

namespace LazyCrucible
{
    public sealed class Configuration
    {
        public bool AutoRoster { get; set; } = true;
        public bool AutoHorns { get; set; } = true;
        public bool ScoreThreeFamiliars { get; set; }
        public bool YieldToAutoDuty { get; set; } = true;
        public bool AnnouncePicks { get; set; } = true;
        public bool RecordScreens { get; set; } = true;
        public bool AutoFeed { get; set; } = true;
        public bool AutoShop { get; set; } = true;
        public bool AutoTreasure { get; set; } = true;
        public bool AutoSpoils { get; set; } = true;
        public bool AutoCamp { get; set; } = true;
        public ScoreGoal ScoreGoal { get; set; } = ScoreGoal.SurvivalFirst;
        public bool GuideAutoOpen { get; set; } = true;
        public void Save() { }
    }

    public sealed class Plugin
    {
        internal static Configuration Config { get; set; } = new();
        internal static CrucibleGuide Guide { get; set; } = CrucibleGuide.Parse("{}");
        internal static HarnessTelemetry? Telemetry { get; set; } = new();
        internal void ToggleGuide() { }
    }

    internal sealed class HarnessTelemetry
    {
        // The shared LalaTelemetry source is intentionally outside this probe.
        public void DrawReportButton() { }
    }

    internal static class PetSelect
    {
        internal static string? YieldReason;
        internal static string LastSummary = "";
        internal static DateTime LastSummaryAt;
    }

    internal static class SelectionScreens
    {
        internal static string Status = "";
        internal static List<(DateTime At, string Screen, string Text)> Recent { get; } = [];
        internal static List<(string AddonName, string Text)> Suggestions { get; } = [];
    }

    internal static class ExternalDrivers
    {
        internal static string? StallReason;
        internal static CrucibleSurface DrivenSurfaces = CrucibleSurface.All;
    }

    internal static class RunTracker
    {
        internal static int Board { get; set; }
        internal static (int Board, int Battle) Focus { get; set; } = (0, -1);
        internal static List<int> Roster { get; set; } = [];
        internal static Dictionary<int, int> RosterHp { get; set; } = [];
        internal static int PlayerHpPercent { get; set; } = 100;
    }

    internal static class CrucibleGame
    {
        internal static bool RosterLoaded { get; set; }
        internal static int CurrentBattleValue { get; set; } = -1;
        internal static IncomingCast? WarnedCastValue { get; set; }
        internal static Func<int, bool>? CapturePredicate { get; set; }
        internal static bool BeastCaptured(int row) => CapturePredicate?.Invoke(row) ?? true;
        internal static int CurrentBattle() => CurrentBattleValue;
        internal static IncomingCast? WarnedCast() => WarnedCastValue;
    }

    // Minimal unmanaged addon geometry for the real SuggestionOverlay.Draw().
    internal unsafe struct HarnessRootNode
    {
        public ushort Width;
        public ushort Height;
    }

    internal unsafe struct HarnessAddon
    {
        public float Scale;
        public short X;
        public short Y;
        public HarnessRootNode* RootNode;
    }

    internal static unsafe class ScreenReader
    {
        private static IntPtr _addonMemory;
        private static IntPtr _rootMemory;
        private static string _name = "";
        private static bool _visible;

        internal static void SetAddon(string name, short x, short y, ushort width, ushort height, float scale = 1)
        {
            if (_addonMemory == IntPtr.Zero)
            {
                _addonMemory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(HarnessAddon));
                _rootMemory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(HarnessRootNode));
            }
            *(HarnessRootNode*)_rootMemory = new HarnessRootNode { Width = width, Height = height };
            *(HarnessAddon*)_addonMemory = new HarnessAddon
            {
                Scale = scale,
                X = x,
                Y = y,
                RootNode = (HarnessRootNode*)_rootMemory,
            };
            _name = name;
            _visible = true;
        }

        internal static bool TryGet(string addonName, out HarnessAddon* addon)
        {
            if (_visible && addonName == _name)
            {
                addon = (HarnessAddon*)_addonMemory;
                return true;
            }
            addon = null;
            return false;
        }

        internal static void Reset()
        {
            _visible = false;
            _name = "";
            if (_addonMemory != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(_addonMemory);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(_rootMemory);
                _addonMemory = IntPtr.Zero;
                _rootMemory = IntPtr.Zero;
            }
        }
    }
}
