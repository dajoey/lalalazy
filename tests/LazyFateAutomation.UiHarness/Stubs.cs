// Game/runtime boundary of the LazyFateAutomation window, stubbed for the render harness.
// Everything here replaces something that needs a running game: services, FATE memory reads, data sheets, the
// automation task, the navmesh IPC, the plugin object. The window, chrome, configuration, FATE rules and kit are real.
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace ECommons.GameHelpers
{
    /// <summary>The local player: only availability and distance are read by the window.</summary>
    internal static class Player
    {
        public static bool Available { get; set; } = true;
        public static Vector3 Position { get; set; } = Vector3.Zero;
        public static float DistanceTo(Vector3 other) => Vector3.Distance(Position, other);
    }
}

namespace LazyFateAutomation.Helpers.Extensions
{
    internal static class VectorExtensions
    {
        // Real versions randomise a point and snap it to the navmesh; the harness never clicks a FATE row.
        public static Vector3 RandomPoint(this Vector3 center, float radius) => center;
        public static Vector3 OnMesh(this Vector3 position) => position;
    }

    /// <summary>The two drag-and-drop helpers of the plugin's ImGuiExtensions.cs (that file needs Lumina and the icon font).</summary>
    internal static partial class ImGuiExtensions
    {
        extension(ImGui)
        {
            public static void DragDropSource(int index, ReadOnlySpan<byte> payloadType, string? dragPreviewText = null)
            {
                using var source = ImRaii.DragDropSource();
                if (source)
                {
                    if (!string.IsNullOrEmpty(dragPreviewText))
                        ImGui.Text(dragPreviewText);
                    ImGui.SetDragDropPayload(payloadType, BitConverter.GetBytes(index), ImGuiCond.None);
                }
            }

            public static void DragDropTarget(int targetIndex, ReadOnlySpan<byte> payloadType, int listCount, Action<int, int> onReorder)
            {
                using var target = ImRaii.DragDropTarget();
                _ = target;
            }
        }
    }
}

namespace LazyFateAutomation.Helpers.Utils
{
    /// <summary>A recorded game item (collect FATE turn-in item); the real one reads the item sheet.</summary>
    public sealed class ItemHandle(uint itemId, string name)
    {
        public override string ToString() => $"[#{itemId}] {name}";
    }

    /// <summary>
    /// Stand-in for the FATE wrapper that reads game memory. Same public instance properties as the real class
    /// (the FATE hover tooltip lists them by reflection), values supplied by the fixtures.
    /// </summary>
    public sealed class PublicEvent(nint address, FateType fateType, uint id)
    {
        public IntPtr Address { get; } = address;
        public FateType FateType { get; } = fateType;
        public uint Id { get; } = id;
        public Vector3 Position { get; init; }
        public float Radius { get; init; } = 60f;
        public int Progress { get; init; }
        public int Duration { get; init; } = 900;
        public float TimeRemaining { get; init; }
        public int StartTimeEpoch { get; init; } = 1_790_000_000;
        public int EndTimeEpoch => StartTimeEpoch + Duration;
        public bool HasBonus { get; init; }
        public byte Level { get; init; }
        public string Name { get; init; } = "";
        public uint MotivationNpcId { get; init; }
        public IGameObject? MotivationNpc => null;
        public IGameObject? ObjectiveNpc => null;
        public ItemHandle? EventItem { get; init; }
        public FateState State { get; init; } = FateState.Running;
        public bool IsOnMap { get; init; } = true;
        public bool IsPending => State == FateState.Running && TimeRemaining <= 0 || State == FateState.Preparing && !IsOnMap;
        public FateRule Rule { get; init; } = FateRule.Normal;

        /// <summary>What the real class reads from the game's FATE table; set by the fixtures.</summary>
        public static IEnumerable<PublicEvent> Fates { get; set; } = [];
    }
}

namespace LazyFateAutomation
{
    public interface IFateGrindRunState
    {
        int CompletedCount { get; }
        int? RunUntilCompleted { get; }
        int? RemainingUntilCompleted { get; }
        int RelicsCompletedForStep { get; }
    }

    internal interface IFateGrindMode
    {
        string DisplayName { get; }
        IReadOnlySet<uint>? GetAllowedZones();
        string? GetRemainingDisplay(IFateGrindRunState state);
    }

    /// <summary>One mode of the real FateGrindModes list; zones and remaining text come from the fixtures.</summary>
    internal sealed class HarnessGrindMode(string displayName, IReadOnlySet<uint>? zones, Func<IFateGrindRunState, string?> remaining) : IFateGrindMode
    {
        public string DisplayName { get; } = displayName;
        public IReadOnlySet<uint>? GetAllowedZones() => zones;
        public string? GetRemainingDisplay(IFateGrindRunState state) => remaining(state);
    }

    public static class FateGrindModes
    {
        internal static IReadOnlyList<IFateGrindMode> All => Fixtures.GrindModes;
        internal static IFateGrindMode? GetByDisplayName(string displayName) => All.FirstOrDefault(m => m.DisplayName == displayName);
        internal static IFateGrindMode? GetNoneMode() => All.FirstOrDefault(m => m.DisplayName == "None");
    }

    public sealed record FateZone(uint TerritoryId, string Name, uint ExVersionId, string ExVersionName, FateZoneLogic.ZoneClass ZoneClass);

    /// <summary>Same queries as the real zone table; the rows come from the fixtures instead of the game sheets.</summary>
    public static class FateZones
    {
        public static IReadOnlyList<FateZone> All => Fixtures.Zones;

        public static IEnumerable<IGrouping<(uint Id, string Name), FateZone>> ByExpansion
            => from zone in All
               group zone by (Id: zone.ExVersionId, Name: zone.ExVersionName) into g
               orderby g.Key.Id
               select g;

        public static bool HasPrimaryAetheryte(uint territoryId)
            => All.Any(z => z.TerritoryId == territoryId && z.ZoneClass == FateZoneLogic.ZoneClass.FateZone);

        public static IReadOnlySet<uint> CurrencyZones(FateCurrency currency) => currency switch
        {
            FateCurrency.CompanySeals or FateCurrency.BicolorGemstones => All
                .Where(z => FateZoneLogic.ZoneCurrency(z.ExVersionId, z.ZoneClass) == currency)
                .Select(z => z.TerritoryId)
                .ToHashSet(),
            FateCurrency.YokaiMedals => Fixtures.YokaiZoneIds.Where(HasPrimaryAetheryte).ToHashSet(),
            _ => new HashSet<uint>(),
        };
    }

    /// <summary>Plugin object: the window reads nothing from it, the kit and configuration need the static config.</summary>
    public static class Plugin
    {
        public static Configuration Config { get; set; } = new();
    }

    internal sealed class HarnessPluginInterface
    {
        public int Saves { get; private set; }
        public void SavePluginConfig(object config) => Saves++;
    }

    internal sealed class HarnessNavmesh
    {
        public int StopCalls { get; set; }
        public bool Running { get; set; }
        public bool IsRunning() => Running;
        public void Stop() => StopCalls++;
        public bool PathfindAndMoveTo(Vector3 destination, bool fly = false) => true;
    }

    internal sealed class HarnessCondition
    {
        public bool this[ConditionFlag flag] => false;
    }

    /// <summary>Service locator of the plugin; only the members the window and configuration touch.</summary>
    internal static class Svc
    {
        public static HarnessPluginInterface PluginInterface { get; } = new();
        public static HarnessNavmesh Navmesh { get; } = new();
        public static HarnessCondition Condition { get; } = new();
    }

    internal sealed class HarnessAutomation
    {
        public bool Running { get; set; }
        public string Status { get; set; } = "Idle";
    }

    internal static class Service
    {
        public static HarnessNavmesh Navmesh => Svc.Navmesh;
        public static HarnessAutomation Automation { get; } = new();
    }

    /// <summary>
    /// The FATE kit the window talks to. State and zone/mode/eligibility rules follow the real FateToolKit
    /// (the eligibility rule itself is the real FateEligibility); the automation task, IPC, chat and game
    /// reads are replaced by plain fields the scenarios set.
    /// </summary>
    public class FateToolKit : IFateGrindRunState
    {
        public const int PlayerLevel = 94;

        public Configuration Config => Plugin.Config;
        public string CurrentState { get; internal set; } = "Idle";
        public int CompletedCount { get; internal set; }
        public int? RunUntilCompleted { get; internal set; }
        public int? RemainingUntilCompleted => RunUntilCompleted is { } runUntil ? Math.Max(0, runUntil - CompletedCount) : null;
        public int RelicsCompletedForStep => 0;
        internal HashSet<uint> SelectedSwapZones => Config.SelectedSwapZones;
        internal int ZoneSelectorOpened { get; private set; }

        internal string SelectedModeId
        {
            get => Config.SelectedModeId;
            set
            {
                Config.SelectedModeId = value;
                Config.Save();
            }
        }

        internal bool PendingStopWhenSafe { get; set; }

        private bool _running;
        public bool Running
        {
            get => _running;
            internal set
            {
                _running = value;
                PendingStopWhenSafe = false;
                if (!value)
                {
                    CurrentState = "Idle";
                    RunUntilCompleted = null;
                }
                Service.Automation.Running = value;
            }
        }

        internal IFateGrindMode GetCurrentMode()
        {
            var displayName = SelectedModeId;
            if (string.IsNullOrEmpty(displayName))
                return FateGrindModes.GetNoneMode() ?? FateGrindModes.All[0];
            return FateGrindModes.GetByDisplayName(displayName) ?? FateGrindModes.GetNoneMode() ?? FateGrindModes.All[0];
        }

        internal IReadOnlySet<uint>? GetRawSwapZones()
        {
            var modeZones = GetCurrentMode().GetAllowedZones();
            if (modeZones != null) return modeZones;
            if (Config.CurrencyFocus != FateCurrency.None)
            {
                var focusZones = FateZones.CurrencyZones(Config.CurrencyFocus);
                if (focusZones.Count > 0) return focusZones;
            }
            return SelectedSwapZones.Count > 0 ? SelectedSwapZones : null;
        }

        internal bool ModeSuppliesSwapZones => GetCurrentMode().GetAllowedZones() != null;

        internal bool ManualZonesOverridden => ModeSuppliesSwapZones
            || (Config.CurrencyFocus != FateCurrency.None && FateZones.CurrencyZones(Config.CurrencyFocus).Count > 0);

        internal static string CurrencyName(FateCurrency currency) => currency switch
        {
            FateCurrency.CompanySeals => "Company Seals",
            FateCurrency.BicolorGemstones => "Bicolor Gemstones",
            FateCurrency.YokaiMedals => "Yo-kai Medals",
            _ => "None",
        };

        internal void SyncRunningState()
        {
            if (Running && !Service.Automation.Running)
                Running = false;
        }

        internal bool HasSelectedSwapZones => SelectedSwapZones.Count > 0;
        internal void OpenZoneSelector() => ZoneSelectorOpened++;

        public void ToggleRunning()
        {
            RunUntilCompleted = null;
            Running ^= true;
            if (Running)
                CompletedCount = 0;
        }

        internal bool IsBlacklisted(PublicEvent f)
            => Config.Blacklist.TryGetValue(f.FateType, out var set) && set.Contains(f.Id);

        public void ToggleBlacklist(PublicEvent f)
        {
            if (!Config.Blacklist.TryGetValue(f.FateType, out var set))
                Config.Blacklist[f.FateType] = set = [];
            if (!set.Add(f.Id))
                set.Remove(f.Id);
        }

        public bool FateConditions(PublicEvent f)
            => FateEligibility.IsEligible(
                f.Duration, Config.MaxDuration,
                f.Progress, Config.MaxProgress,
                f.TimeRemaining, Config.MinTimeRemaining,
                IsBlacklisted(f), f.IsPending,
                f.Rule, Config.ExcludedFateRules,
                f.Level, PlayerLevel);

        public (bool IsEligible, List<string> FailedConditions) GetFateConditionDetails(PublicEvent f)
        {
            var failed = new List<string>();
            FateEligibility.AppendFailedConditions(
                failed,
                f.Duration, Config.MaxDuration,
                f.Progress, Config.MaxProgress,
                f.TimeRemaining, Config.MinTimeRemaining,
                IsBlacklisted(f), f.IsPending,
                f.Rule, Config.ExcludedFateRules,
                f.Level, PlayerLevel);
            return (failed.Count == 0, failed);
        }

        public IEnumerable<(PublicEvent Fate, bool IsAvailable)> GetOrderedFates()
        {
            var all = PublicEvent.Fates.ToList();
            if (all.Count == 0)
                yield break;
            var available = all.Where(FateConditions).ToList();
            var unavailable = all.Where(f => !FateConditions(f)).ToList();
            foreach (var f in ApplySortOrder(available, Config.SortOrder))
                yield return (f, true);
            foreach (var f in ApplySortOrder(unavailable, Config.SortOrder))
                yield return (f, false);
        }

        // Same keys as the real sort table; the Twist of Fate status is never active in the harness.
        private static IOrderedEnumerable<PublicEvent> ApplySortOrder(IEnumerable<PublicEvent> source, IReadOnlyList<FateSortOrder> sortOrder)
        {
            const int MinTimeToPrioritise = 240;
            IComparable Key(FateSortCriteria criteria, PublicEvent f) => criteria switch
            {
                FateSortCriteria.HasBonusWithTwist => false,
                FateSortCriteria.Progress => f.Progress,
                FateSortCriteria.HasBonus => f.HasBonus,
                FateSortCriteria.TimeRemainingUrgent => f.TimeRemaining is >= 0 and < MinTimeToPrioritise,
                FateSortCriteria.Distance => Player.DistanceTo(f.Position),
                FateSortCriteria.TimeRemaining => f.TimeRemaining is >= 0 and < MinTimeToPrioritise ? f.TimeRemaining : MinTimeToPrioritise,
                FateSortCriteria.Level => f.Level,
                FateSortCriteria.Name => f.Name,
                _ => 0,
            };
            if (!sortOrder.Any())
                return source.OrderBy(_ => 0);
            IOrderedEnumerable<PublicEvent>? ordered = null;
            foreach (var sort in sortOrder)
            {
                var criteria = sort.Criteria;
                ordered = ordered == null
                    ? sort.Descending ? source.OrderByDescending(f => Key(criteria, f)) : source.OrderBy(f => Key(criteria, f))
                    : sort.Descending ? ordered.ThenByDescending(f => Key(criteria, f)) : ordered.ThenBy(f => Key(criteria, f));
            }
            return ordered ?? source.OrderBy(_ => 0);
        }
    }
}
