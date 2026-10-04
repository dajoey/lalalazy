// Fakes.cs — the harness-owned stand-ins for everything the REAL Monk job files (MNK.cs, MNK_Helper.cs,
// compiled unchanged from the pinned revision) reach outside themselves. Copied from the VPR spike
// (branch rot/harness-spike) and extended with the MNK surface: the MNK Preset members, the MNK Config
// defaults, enemy/ally counts in range, TimeStoodStill, party helpers, GetMaxCharges, UserBoolArray,
// and the opener AllowUpgradeSteps/OpenerStep members. The boundary is deliberate:
//   REAL  : every decision line in MNK.cs / MNK_Helper.cs (Invoke, weave/PB/blitz helpers, gauge props,
//           action IDs, Buffs/Traits tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat), the Preset enum values, the MNK Config settings defaults,
//           the role-action layer, openers' WrathOpener base, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Monk members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        MNK_ST_SimpleMode = 31001,
        MNK_AoE_SimpleMode = 31002,
        MNK_ST_AdvancedMode = 31003,
        MNK_AoE_AdvancedMode = 31004,
        MNK_ST_BasicCombo = 31005,
        MNK_Basic_BeastChakras = 31006,
        MNK_Retarget_Thunderclap = 31007,
        MNK_PerfectBalance = 31008,
        MNK_Brotherhood_Riddle = 31009,
        MNK_PerfectBalanceProtection = 31010,
        MNK_STUseOpener = 31011,
        MNK_STUseMeditation = 31012,
        MNK_STUseFormShift = 31013,
        MNK_STUsePerfectBalance = 31014,
        MNK_STUseBrotherhood = 31015,
        MNK_STUseROF = 31016,
        MNK_STUseBuffs = 31017,
        MNK_STUseROW = 31018,
        MNK_STUseTheForbiddenChakra = 31019,
        MNK_ST_UseMantra = 31020,
        MNK_ST_UseRoE = 31021,
        MNK_ST_Feint = 31022,
        MNK_ST_ComboHeals = 31023,
        MNK_ST_StunInterupt = 31024,
        MNK_STUseMasterfulBlitz = 31025,
        MNK_STUseFiresReply = 31026,
        MNK_STUseWindsReply = 31027,
        MNK_STUseTrueNorth = 31028,
        MNK_AoEUseMeditation = 31029,
        MNK_AoEUseFormShift = 31030,
        MNK_AoEUsePerfectBalance = 31031,
        MNK_AoEUseBuffs = 31032,
        MNK_AoEUseBrotherhood = 31033,
        MNK_AoEUseROF = 31034,
        MNK_AoEUseROW = 31035,
        MNK_AoEUseHowlingFist = 31036,
        MNK_AoE_ComboHeals = 31037,
        MNK_AoE_StunInterupt = 31038,
        MNK_AoEUseMasterfulBlitz = 31039,
        MNK_AoEUseFiresReply = 31040,
        MNK_AoEUseWindsReply = 31041,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE slice of the real partial class All (ALL.cs): only Cease, real value mirrored.</summary>
    internal partial class All
    {
        public const uint Cease = 1_000_004; // mirrored from ALL.cs:19
    }

    /// <summary>FAKE of Roles/RoleActions.cs: only the melee members MNK touches.</summary>
    internal static class RoleActions
    {
        public static class Melee
        {
            public const uint LegSweep = 7863;   // mirrored from RoleActions.cs:136

            public static bool CanLegSweep() => GluttonyCombo.RotationHarness.MNK.FakeGame.LegSweepReady;
        }
    }

    /// <summary>FAKE of ALL/JobClasses.cs Melee base (the real one routes through Roles/RoleImplementation).</summary>
    internal class Melee
    {
        protected Melee() { }

        public static FakeMeleeRole Role { get; } = new();
    }

    /// <summary>FAKE of the IMelee role surface MNK uses; consts mirrored from RoleActions.cs.</summary>
    internal class FakeMeleeRole
    {
        public uint SecondWind => 7541;
        public uint ArmsLength => 7548;
        public uint LegSweep => 7863;
        public uint Bloodbath => 7542;
        public uint Feint => 7549;
        public uint TrueNorth => 7546;

        public bool CanSecondWind(int healthpercent) => GluttonyCombo.RotationHarness.MNK.FakeGame.RoleActionReady;
        public bool CanBloodBath(int healthpercent) => GluttonyCombo.RotationHarness.MNK.FakeGame.RoleActionReady;
        public bool CanFeint() => GluttonyCombo.RotationHarness.MNK.FakeGame.RoleActionReady;
        public bool CanTrueNorth() => GluttonyCombo.RotationHarness.MNK.FakeGame.RoleActionReady;
        public bool CanLegSweep() => GluttonyCombo.RotationHarness.MNK.FakeGame.LegSweepReady;
    }

    /// <summary>FAKE of Combos/PvE/Content/ContentSpecificActions.cs: never offers a content action.</summary>
    public static class ContentSpecificActions
    {
        public static bool TryGet(ref uint actionId, out uint contentAction, bool healing = false)
        {
            contentAction = 0;
            return false;
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.ALL
{
    /// <summary>FAKE of ALL/Items.cs: only the two members the MNK opener lambdas compile against.</summary>
    internal class Items
    {
        public enum PotionType { Strength, Dex, Vit, Int, Mind }

        public static uint GetStrongestPotionRow(PotionType type, bool inInventory = true) => 33;

        public static uint UseItem(uint item) => item;
    }
}

// ======================================================================================
namespace GluttonyCombo.Data
{
    /// <summary>FAKE of ContentCheck.cs: only UltimateTerritoryIDs, values mirrored.</summary>
    public class ContentCheck
    {
        public static class UltimateTerritoryIDs
        {
            public const uint FRU = 1238; // mirrored from ContentCheck.cs:27
            public const uint DMU = 1363; // mirrored from ContentCheck.cs:28
        }
    }

    /// <summary>
    ///     FAKE of Data/CooldownData.cs: plain settable data instead of the ActionManager memory reads.
    ///     Cooldown charge math and sheet lookups are therefore OUT of the tested boundary.
    /// </summary>
    internal class CooldownData
    {
        public uint ActionID;
        public bool IsCooldown;
        public float CooldownTotal = 2.5f;
        public float CooldownRemaining;
        public float CooldownElapsed;
        public float ChargeCooldownRemaining;
        public float CurrentRecast;
        public float BaseCooldownTotal = 2.5f;
        public float BaseCooldown = 2.5f;
        public ushort MaxCharges = 1;
        public uint RemainingCharges = 1;

        public bool HasCharges => MaxCharges > 1;
    }
}

// ======================================================================================
namespace GluttonyCombo.CustomComboNS.Functions
{
    /// <summary>FAKE of the UserData base in CustomCombo/Functions/Config.cs (verbatim shape, no persistence).</summary>
    internal abstract class UserData(string configName)
    {
        public string ConfigName = configName;

        public static implicit operator string(UserData o) => o.ConfigName;

        public static Dictionary<string, UserData> MasterList = new();

        public abstract void ResetToDefault();
    }

    internal class UserFloat(string configName, float defaults = 0f) : UserData(configName)
    {
        public float Default = defaults;

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.MNK.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.MNK.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.MNK.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the UserBoolArray MNK's Beast Chakras preset reads (`MNK_BasicCombo[0]`): indexed
    ///     bool lookup keyed by config name + index, default false (real default is unset = false).
    /// </summary>
    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public static bool Get(UserBoolArray o, int index) =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.GetBool($"{o.ConfigName}[{index}]", false);

        public bool this[int index]
        {
            get => Get(this, index);
            set => GluttonyCombo.RotationHarness.MNK.FakeGame.SetBool($"{ConfigName}[{index}]", value);
        }

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Monk files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.MNK.FakeGame"/>; defaults make a clean, weaving-blocked,
    ///     everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.MNK.FakeGame.EnabledPresets.Contains(preset);

        // ---- player / status ----
        public static GluttonyCombo.RotationHarness.MNK.FakePlayer LocalPlayer { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.MNK.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.MNK.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.MNK.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.MNK.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.MNK.FakeGame.TargetHPPercent;

        public static bool TargetNeedsPositionals() => GluttonyCombo.RotationHarness.MNK.FakeGame.TargetNeedsPositionals;

        public static bool OnTargetsFlank() => GluttonyCombo.RotationHarness.MNK.FakeGame.OnTargetsFlank;

        public static bool OnTargetsRear() => GluttonyCombo.RotationHarness.MNK.FakeGame.OnTargetsRear;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.MNK.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.MNK.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.MNK.FakeGame.InActionRange;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.MNK.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.MNK.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static ushort GetMaxCharges(uint actionID) => GetCooldown(actionID).MaxCharges;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.MNK.FakeGame.AllTraitsKnown;

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.MNK.FakeGame.CanWeave;

        public static float ComboTimer => GluttonyCombo.RotationHarness.MNK.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.MNK.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.MNK.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.MNK.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming(float? maxTimeRemaining = null) => GluttonyCombo.RotationHarness.MNK.FakeGame.GroupDamageIncoming;

        // ---- movement / targeting / party (MNK surface) ----
        public static System.TimeSpan TimeStoodStill =>
            System.TimeSpan.FromSeconds(GluttonyCombo.RotationHarness.MNK.FakeGame.SecondsStoodStill);

        public static IGameObject? CurrentTarget => GluttonyCombo.RotationHarness.MNK.FakeGame.HardTargetGameObject;

        public static int NumberOfEnemiesInRange(uint aoeSpell, IGameObject? target = null, bool checkIgnoredList = false) =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.EnemiesInRange(aoeSpell);

        public static int NumberOfAlliesInRange(uint aoeSpell, IGameObject? target = null) =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.AlliesInRangeCount;

        public static List<object> GetPartyMembers(bool allowCache = true) => GluttonyCombo.RotationHarness.MNK.FakeGame.PartyMembers;

        public static float GetPartyAvgHPPercent() => GluttonyCombo.RotationHarness.MNK.FakeGame.PartyAvgHPPercent;

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => GluttonyCombo.RotationHarness.MNK.FakeGauges.Get<T>();
    }
}

// ======================================================================================
namespace GluttonyCombo.CustomComboNS
{
    /// <summary>FAKE of CustomCombo.cs: just the two abstracts every job combo class overrides.</summary>
    internal abstract partial class CustomCombo : Functions.CustomComboFunctions
    {
        protected internal abstract Combos.Preset Preset { get; }

        protected abstract uint Invoke(uint actionID);

        public uint RunInvoke(uint actionID) => Invoke(actionID);
    }

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the three MNK names, as plain objects.</summary>
    public static class SimpleTarget
    {
        public static object? UIMouseOverTarget => GluttonyCombo.RotationHarness.MNK.FakeGame.UiMouseOver;

        public static object? ModelMouseOverTarget => null;

        public static object? HardTarget => GluttonyCombo.RotationHarness.MNK.FakeGame.HardTarget;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the MNK opener classes declare
    ///     and override (including AllowUpgradeSteps and OpenerStep). Openers are not exercised by the
    ///     harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.MNK_STUseOpener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public virtual List<int> AllowUpgradeSteps { get; set; } = [];

        public int OpenerStep { get; internal set; }

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.MNK.FakeGame.Level <= MaxOpenerLevel;

        internal bool FullOpener(ref uint actionID) => false;

        public static WrathOpener Dummy { get; } = new();
    }
}

// ======================================================================================
namespace GluttonyCombo.Native
{
    /// <summary>FAKE of Native/CustomActionManager.cs: the checker plus its enum.</summary>
    public enum CustomActionType
    {
        None = 0,
        SingleTargetDPS = 1,
        AoEDPS = 2,
        SingleTargetHeals = 3,
        AoEHeals = 4,
    }

    public class CustomActionHelper
    {
        public static bool OneButtonRotationChecker(uint actionId, CustomActionType type, params uint[] originals) =>
            GluttonyCombo.RotationHarness.MNK.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>FAKE of the UIntExtensions.Retarget surface MNK_Retarget_Thunderclap compiles against.</summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE parts of the partial MNK class: the settings class (real one drags ImGui/localization) and the
    // positional-hint reporter (real one is an IPC side channel, not a rotation decision). Defaults are
    // mirrored one-for-one from MNK_Config.cs "Variables" region.
    internal partial class MNK
    {
        private static void ReportMNKPositionalHints()
        {
            // no-op: hint reporting is a UI/IPC side channel, out of the tested boundary
        }

        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                MNK_SelectedOpener = new("MNK_SelectedOpener"),
                MNK_Balance_Content = new("MNK_Balance_Content", 1),
                MNK_ST_BHHPBossOption = new("MNK_ST_BHHPBossOption"),
                MNK_ST_BHHPOption = new("MNK_ST_BHHPOption", 25),
                MNK_ST_RoFHPBossOption = new("MNK_ST_RoFHPBossOption"),
                MNK_ST_RoFHPOption = new("MNK_ST_RoFHPOption", 25),
                MNK_ST_RoWHPBossOption = new("MNK_ST_RoWHPBossOption"),
                MNK_ST_RoWHPOption = new("MNK_ST_RoWHPOption", 25),
                MNK_ManualTN = new("MNK_ManualTN"),
                MNK_ST_EarthsReplyHPThreshold = new("MNK_ST_EarthsReplyHPThreshold", 25),
                MNK_ST_SecondWindHPThreshold = new("MNK_ST_SecondWindHPThreshold", 40),
                MNK_ST_BloodbathHPThreshold = new("MNK_ST_BloodbathHPThreshold", 30),
                MNK_AoE_BuffsHPThreshold = new("MNK_AoE_BuffsHPThreshold", 25),
                MNK_AoE_PerfectBalanceHPThreshold = new("MNK_AoE_PerfectBalanceHPThreshold", 25),
                MNK_AoE_SecondWindHPThreshold = new("MNK_AoE_SecondWindHPThreshold", 40),
                MNK_AoE_BloodbathHPThreshold = new("MNK_AoE_BloodbathHPThreshold", 30),
                MNK_BH_RoF = new("MNK_BH_RoF");

            public static CustomComboNS.Functions.UserBool
                MNK_Opener_Potion = new("MNK_Opener_Potion"),
                MNK_Opener_PrepullBlock = new("MNK_Opener_PrepullBlock", true),
                MNK_Thunderclap_FieldMouseover = new("MNK_Thunderclap_FieldMouseover"),
                MNK_BasicCombo_MasterfulBlitz = new("MNK_BasicCombo_MasterfulBlitz"),
                MNK_BasicCombo_Chakra = new("MNK_BasicCombo_Chakra"),
                MNK_ST_EarthsReply = new("MNK_ST_EarthsReply");

            public static CustomComboNS.Functions.UserBoolArray
                MNK_BasicCombo = new("MNK_BasicCombo");
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>Namespace stub: MNK.cs has `using GluttonyCombo.Core;`; its real members are faked elsewhere.</summary>
    internal static class HarnessCoreNamespaceStub;
}
