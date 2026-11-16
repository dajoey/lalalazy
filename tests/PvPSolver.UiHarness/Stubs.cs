// Game/runtime boundary of the PvP settings window, stubbed for the render harness. Everything the window itself
// draws (shell, chrome, tabs, catalog, decision cores, the fork's settings partial, the UI kit) is real source linked
// by the project file. What is faked here: the ECommons services, the plugin entry points, the live configuration
// object (the plugin's generated Configs members), the rotation option sets, the on/off switch and the config
// serializer attributes.
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using RotationSolver.Basic.Configuration;
using RotationSolver.Basic.Configuration.RotationConfig;
using RotationSolver.Basic.Data;
using RotationSolver.Basic.Rotations;
using RotationSolver.Decisions;
using static RotationSolver.Basic.Configuration.ConfigTypes;

namespace Newtonsoft.Json
{
    internal enum NullValueHandling { Include, Ignore }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class JsonPropertyAttribute : Attribute
    {
        public NullValueHandling NullValueHandling { get; set; }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class JsonIgnoreAttribute : Attribute { }
}

namespace ECommons.ExcelServices
{
    // The member values of the real ECommons enum (game ClassJob row ids) for the jobs and base classes the window names.
    public enum Job : uint
    {
        ADV = 0, GLA = 1, PGL = 2, MRD = 3, LNC = 4, ARC = 5, CNJ = 6, THM = 7,
        PLD = 19, MNK = 20, WAR = 21, DRG = 22, BRD = 23, WHM = 24, BLM = 25, ACN = 26, SMN = 27, SCH = 28,
        ROG = 29, NIN = 30, MCH = 31, DRK = 32, AST = 33, SAM = 34, RDM = 35, BLU = 36, GNB = 37, DNC = 38,
        RPR = 39, SGE = 40, VPR = 41, PCT = 42,
    }
}

namespace ECommons.GameHelpers
{
    internal static class Player
    {
        /// <summary>Whether a character is in the world (the harness sets it per scenario).</summary>
        public static bool Available { get; set; }

        /// <summary>The job the character plays.</summary>
        public static Job Job { get; set; } = Job.ADV;
    }
}

namespace ECommons.Logging
{
    internal static class PluginLog
    {
        public static List<string> Errors { get; } = [];

        public static void Error(string message) => Errors.Add(message);
    }
}

namespace ECommons.DalamudServices
{
    internal static class Svc
    {
        public static HarnessPluginInterface PluginInterface { get; } = new();
    }

    internal sealed class HarnessPluginInterface
    {
        public DirectoryInfo ConfigDirectory { get; } = new(Path.Combine(Path.GetTempPath(), "pvpsolver-harness-config"));
    }
}

namespace RotationSolver
{
    /// <summary>The plugin entry points the Advanced and Display tabs call; they open other windows, which the harness does not draw.</summary>
    internal static class PvPSolverPlugin
    {
        public static List<string> Calls { get; } = [];

        internal static void OpenAdvancedWindow(string? tabName = null) => Calls.Add("advanced:" + tabName);

        internal static void ShowChangelog() => Calls.Add("changelog");

        internal static void OpenFirstStartTutorial() => Calls.Add("tutorial");
    }
}

namespace RotationSolver.UI
{
    /// <summary>The one extension of the plugin's ImGuiHelper that the job options use (same mapping as the real file).</summary>
    internal static class HarnessImGuiHelper
    {
        public static string ToSymbol(this ConfigUnitType unit) => unit switch
        {
            ConfigUnitType.Seconds => " s",
            ConfigUnitType.Degree => " °",
            ConfigUnitType.Pixels => " p",
            ConfigUnitType.Yalms => " y",
            ConfigUnitType.Percent => " %%",
            _ => string.Empty,
        };
    }
}

namespace RotationSolver.Helpers
{
    /// <summary>
    ///     The on/off boundary (DataCenter state, zone and the command pipeline). The wording comes from the real
    ///     <see cref="SwitchStatus"/>; only the three inputs and the zone rule of the real PvpSwitch.Set are reproduced.
    /// </summary>
    internal static class PvpSwitch
    {
        public static bool IsOn { get; set; }

        public static bool InPvpZone { get; set; }

        internal static SwitchView View => SwitchStatus.Describe(IsOn, InPvpZone, Player.Available);

        internal static string? Set(bool on)
        {
            if (on == IsOn)
            {
                return null;
            }

            if (on && !InPvpZone)
            {
                return "PvP Solver only turns on in a PvP zone.";
            }

            IsOn = on;
            return null;
        }
    }
}

namespace RotationSolver.Basic
{
    internal sealed class Service
    {
        public static Configuration.Configs Config { get; set; } = new();
    }
}

namespace RotationSolver.Basic.Configuration
{
    internal sealed class ConditionBoolean
    {
        private readonly bool _default;

        public ConditionBoolean(bool defaultValue, string key)
        {
            _default = defaultValue;
            Value = defaultValue;
            Key = key;
        }

        public bool Value { get; set; }

        public string Key { get; }

        public void ResetValue() => Value = _default;

        public static implicit operator bool(ConditionBoolean condition) => condition.Value;
    }

    /// <summary>
    ///     The members of the plugin's generated Configs class that the PvP settings window reads and writes. Every
    ///     default is the real one (the catalog's defaults are re-checked at start, see Program.VerifyCatalogBindings).
    ///     The fork's own members come from the real Configs.PvpSettings.cs partial.
    /// </summary>
    internal partial class Configs
    {
        public const int CurrentVersion = 12;

        // Fields the real generated code provides and Configs.PvpSettings.cs reads.
        private string _rotationChoice = string.Empty;
        private readonly Dictionary<Job, string> _rotationChoiceDict = [];
        private readonly Dictionary<Job, Dictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, string>>> _rotationConfigurationsDict = [];
        private readonly Dictionary<Job, bool> _ignorePvPInvincibilityDict = [];
        private bool _ignorePvPInvincibility;

        public int SaveCount { get; private set; }

        public SortedSet<Job> DisabledJobs { get; private set; } = [];

        public List<TargetingType> TargetingTypes { get; set; } = [];

        public int TargetingIndex { get; set; }

        private Vector4 _teachingModeColor = new(0f, 1f, 0f, 1f);

        public Vector4 TeachingModeColor
        {
            get => ThrowOnTeachingColorRead ? throw new InvalidOperationException("The teaching colour could not be read.") : _teachingModeColor;
            set => _teachingModeColor = value;
        }

        // Match
        public ConditionBoolean AutoOnPvPMatchStart { get; } = new(true, nameof(AutoOnPvPMatchStart));
        public ConditionBoolean AutoOffPvPMatchEnd { get; } = new(true, nameof(AutoOffPvPMatchEnd));
        public ConditionBoolean AutoOffSwitchClass { get; } = new(true, nameof(AutoOffSwitchClass));
        public ConditionBoolean AutoOffCutScene { get; } = new(true, nameof(AutoOffCutScene));
        public ConditionBoolean AutoOffAfterCombat { get; } = new(true, nameof(AutoOffAfterCombat));
        public float AutoOffAfterCombatTime { get; set; } = 30;

        // Survival
        public float HealthForGuard { get; set; } = 0.15f;
        public ConditionBoolean PvpGuardControl { get; } = new(true, nameof(PvpGuardControl));
        public ConditionBoolean PvpGcdLockControl { get; } = new(true, nameof(PvpGcdLockControl));
        public ConditionBoolean PvpPurifyStun { get; } = new(true, nameof(PvpPurifyStun));
        public ConditionBoolean PvpPurifySilence { get; } = new(true, nameof(PvpPurifySilence));
        public ConditionBoolean PvpPurifyDeepFreeze { get; } = new(true, nameof(PvpPurifyDeepFreeze));
        public ConditionBoolean PvpPurifyMiracleOfNature { get; } = new(true, nameof(PvpPurifyMiracleOfNature));
        public ConditionBoolean PvpPurifyHeavy { get; } = new(false, nameof(PvpPurifyHeavy));
        public ConditionBoolean PvpPurifyBind { get; } = new(false, nameof(PvpPurifyBind));
        public ConditionBoolean PvpAllowSprintWithoutTarget { get; } = new(true, nameof(PvpAllowSprintWithoutTarget));

        // Targeting
        public AoEType AoEType { get; set; } = AoEType.Full;
        public ConditionBoolean AutoBurst { get; } = new(true, nameof(AutoBurst));
        public ConditionBoolean StickyTarget { get; } = new(true, nameof(StickyTarget));
        public float StickyTargetMaxRemaining { get; set; } = 30f;
        public ConditionBoolean IgnorePvPInvincibility { get; } = new(false, nameof(IgnorePvPInvincibility));
        public ConditionBoolean PrioAtomelith { get; } = new(false, nameof(PrioAtomelith));
        public ConditionBoolean PrioBtomelith { get; } = new(false, nameof(PrioBtomelith));

        // Display
        public ConditionBoolean ShowInfoOnDtr { get; } = new(true, nameof(ShowInfoOnDtr));
        public DTRType DTRType { get; set; } = DTRType.DTRNormal;
        public ConditionBoolean ShowInfoOnToast { get; } = new(false, nameof(ShowInfoOnToast));
        public ConditionBoolean ShowControlWindow { get; } = new(false, nameof(ShowControlWindow));
        public ConditionBoolean IsControlWindowLock { get; } = new(false, nameof(IsControlWindowLock));
        public ConditionBoolean ShowNextActionWindow { get; } = new(false, nameof(ShowNextActionWindow));
        public ConditionBoolean ShowCooldownWindow { get; } = new(false, nameof(ShowCooldownWindow));
        public ConditionBoolean ShowActionTimelineWindow { get; } = new(false, nameof(ShowActionTimelineWindow));
        public ConditionBoolean OnlyShowWithHostileOrInDuty { get; } = new(false, nameof(OnlyShowWithHostileOrInDuty));
        public ConditionBoolean TeachingMode { get; } = new(false, nameof(TeachingMode));
        public ConditionBoolean TeachingModeAutoTarget { get; } = new(false, nameof(TeachingModeAutoTarget));
        public ConditionBoolean TeachingModeShowTargetHint { get; } = new(false, nameof(TeachingModeShowTargetHint));
        public ConditionBoolean ReddenDisabledHotbarActions { get; } = new(false, nameof(ReddenDisabledHotbarActions));
        public ConditionBoolean ShowTooltips { get; } = new(true, nameof(ShowTooltips));

        // Advanced
        public ConditionBoolean InDebug { get; } = new(false, nameof(InDebug));

        /// <summary>Test hook: makes the Display tab throw while it draws the highlight colour, to reach the window's own error branch.</summary>
        public bool ThrowOnTeachingColorRead { get; set; }

        public void Save() => SaveCount++;

        public void Backup() { }

        public void Restore() { }
    }
}

namespace RotationSolver.Basic.Rotations
{
    internal interface ICustomRotation
    {
        Job Job { get; }

        IRotationConfigSet Configs { get; }
    }
}

namespace RotationSolver.Basic.Configuration.RotationConfig
{
    /// <summary>Stored values of the rotation options (job and option name to text), standing in for the configuration file.</summary>
    internal static class HarnessOptionStore
    {
        public static ConcurrentDictionary<(Job, string), string> Values { get; } = new();
    }

    internal abstract class RotationConfigBase : IRotationConfig
    {
        private readonly Job _job;

        protected RotationConfigBase(Job job, string name, string displayName, string defaultValue, string parent)
        {
            _job = job;
            Name = name;
            DisplayName = displayName;
            DefaultValue = defaultValue;
            Parent = parent;
        }

        public string Name { get; }

        public string DisplayName { get; }

        public string DefaultValue { get; }

        public CombatType Type => CombatType.PvP;

        public string Parent { get; }

        public object? ParentValue { get; set; }

        public string Value
        {
            get => HarnessOptionStore.Values.TryGetValue((_job, Name), out string? stored) ? stored : DefaultValue;
            set => HarnessOptionStore.Values[(_job, Name)] = value;
        }

        public IEnumerable<IRotationConfig>? Configs { get; set; }

        public bool DoCommand(IRotationConfigSet set, string str) => false;
    }

    internal sealed class RotationConfigBoolean(Job job, string name, string displayName, string defaultValue, string parent)
        : RotationConfigBase(job, name, displayName, defaultValue, parent);

    internal sealed class RotationConfigFloat(Job job, string name, string displayName, string defaultValue, string parent,
        float min, float max, float speed, ConfigUnitType unit)
        : RotationConfigBase(job, name, displayName, defaultValue, parent)
    {
        public float Min => min;

        public float Max => max;

        public float Speed => speed;

        public ConfigUnitType UnitType => unit;
    }

    internal sealed class RotationConfigInt(Job job, string name, string displayName, string defaultValue, string parent, int min, int max)
        : RotationConfigBase(job, name, displayName, defaultValue, parent)
    {
        public int Min => min;

        public int Max => max;
    }

    internal sealed class RotationConfigCombo(Job job, string name, string displayName, string defaultValue, string parent, string[] values)
        : RotationConfigBase(job, name, displayName, defaultValue, parent)
    {
        public string[] DisplayValues => values;
    }

    internal sealed class RotationConfigString(Job job, string name, string displayName, string defaultValue, string parent)
        : RotationConfigBase(job, name, displayName, defaultValue, parent);

    internal sealed class HarnessConfigSet : IRotationConfigSet
    {
        public HashSet<IRotationConfig> Configs { get; } = [];

        public IEnumerator<IRotationConfig> GetEnumerator() => Configs.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace RotationSolver.Updaters
{
    /// <summary>
    ///     Stands in for the plugin's rotation cache. The option sets are read from the REAL PvP rotation sources
    ///     (the [RotationConfig(CombatType.PvP ...)] properties of RebornRotations/PVPRotations/**), so the option
    ///     names, defaults, ranges and parent links shown are the plugin's own.
    /// </summary>
    internal static class RotationUpdater
    {
        /// <summary>Set to make GetRotations throw (the options panel then shows its failure line).</summary>
        public static string? FailWith { get; set; }

        /// <summary>Set to make GetRotations return nothing yet (the options panel then shows its loading line).</summary>
        public static bool NotLoadedYet { get; set; }

        public static string SourceRoot { get; set; } = string.Empty;

        private static readonly Dictionary<Job, ICustomRotation> Cache = [];

        internal static ICustomRotation[] GetRotations(Job job, CombatType type)
        {
            if (FailWith != null)
            {
                throw new InvalidOperationException(FailWith);
            }

            if (NotLoadedYet)
            {
                return [];
            }

            if (!Cache.TryGetValue(job, out ICustomRotation? rotation))
            {
                Cache[job] = rotation = new HarnessRotation(job, SourceRoot);
            }

            return [rotation];
        }

        /// <summary>The option names of every PvP rotation (for the glyph audit).</summary>
        public static IEnumerable<string> AllOptionNames(string sourceRoot)
        {
            foreach (Job job in Enum.GetValues<Job>())
            {
                foreach (IRotationConfig config in new HarnessRotation(job, sourceRoot).Configs.Configs)
                {
                    yield return config.DisplayName;
                }
            }
        }

        private sealed class HarnessRotation : ICustomRotation
        {
            private static readonly Regex Option = new(
                @"(?:\[Range\((?<min>[-\d.]+)f?,\s*(?<max>[-\d.]+)f?,\s*ConfigUnitType\.(?<unit>\w+)(?:,\s*(?<speed>[-\d.]+)f?)?\)\]\s*)?" +
                @"\[RotationConfig\(CombatType\.PvP,\s*Name\s*=\s*""(?<name>[^""]*)""(?:,\s*Parent\s*=\s*nameof\((?<parent>\w+)\))?[^\]]*\]\s*" +
                @"public\s+(?<type>bool|float|int)\s+(?<prop>\w+)\s*\{\s*get;\s*set;\s*\}\s*=\s*(?<def>[^;]+);",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            public HarnessRotation(Job job, string sourceRoot)
            {
                Job = job;
                var set = new HarnessConfigSet();
                Configs = set;
                string folder = Path.Combine(sourceRoot, "src", "PvPSolver", "PvPSolver", "RebornRotations", "PVPRotations");
                if (!Directory.Exists(folder))
                {
                    return;
                }

                foreach (string file in Directory.EnumerateFiles(folder, job + "_*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    foreach (Match match in Option.Matches(File.ReadAllText(file)))
                    {
                        string name = match.Groups["prop"].Value;
                        string display = match.Groups["name"].Value;
                        string parent = match.Groups["parent"].Value;
                        string def = match.Groups["def"].Value.Trim().TrimEnd('f');
                        float min = ParseFloat(match.Groups["min"].Value, 0f);
                        float max = ParseFloat(match.Groups["max"].Value, 1f);
                        float speed = ParseFloat(match.Groups["speed"].Value, 0.005f);
                        _ = Enum.TryParse(match.Groups["unit"].Value, out ConfigUnitType unit);
                        switch (match.Groups["type"].Value)
                        {
                            case "bool":
                                _ = set.Configs.Add(new RotationConfigBoolean(job, name, display,
                                    bool.Parse(def) ? "True" : "False", parent));
                                break;
                            case "float":
                                _ = set.Configs.Add(new RotationConfigFloat(job, name, display,
                                    ParseFloat(def, 0f).ToString(CultureInfo.InvariantCulture), parent, min, max, speed, unit));
                                break;
                            default:
                                _ = set.Configs.Add(new RotationConfigInt(job, name, display,
                                    int.Parse(def, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), parent, (int)min, (int)max));
                                break;
                        }
                    }
                }
            }

            public Job Job { get; }

            public IRotationConfigSet Configs { get; }

            private static float ParseFloat(string text, float fallback) =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }
    }
}
