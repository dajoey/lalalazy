// Fakes.cs — the harness-owned stand-ins for everything the REAL Ninja job files (NIN.cs,
// NIN_Helper.cs, compiled unchanged from the branch) reach outside themselves. Derived from the
// VPR spike (rot/harness-spike, tests/GluttonyCombo.RotationHarness/Fakes.cs). The boundary:
//   REAL  : every decision line in NIN.cs / NIN_Helper.cs (Invoke branches, Ninki/mudra helpers,
//           gauge props, action IDs, Buffs/Traits tables, opener classes).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, weave, enemies-in-range), the Preset enum values, the
//           NIN Config settings defaults, the role-action layer, openers' WrathOpener base,
//           ActionWatching.LastAction, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Collections.Generic;
using System.Reflection;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Ninja members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        NIN_ST_SimpleMode = 40001,
        NIN_AoE_SimpleMode = 40002,
        NIN_ST_AdvancedMode = 40003,
        NIN_AoE_AdvancedMode = 40004,
        NIN_ST_AdvancedMode_BalanceOpener = 40005,
        NIN_ST_AdvancedMode_Ninjitsus = 40006,
        NIN_ST_AdvancedMode_Ninjitsus_Hyosho = 40007,
        NIN_ST_AdvancedMode_Ninjitsus_Suiton = 40008,
        NIN_ST_AdvancedMode_Ninjitsus_Raiton = 40009,
        NIN_ST_AdvancedMode_Ninjitsus_Doton = 40058,
        NIN_ST_AdvancedMode_Kassatsu = 40010,
        NIN_ST_AdvancedMode_Bunshin = 40011,
        NIN_ST_AdvancedMode_TenChiJin = 40012,
        NIN_ST_AdvancedMode_TenriJindo = 40013,
        NIN_ST_AdvancedMode_Assassinate = 40014,
        NIN_ST_AdvancedMode_Meisui = 40015,
        NIN_ST_AdvancedMode_Bhavacakra = 40016,
        NIN_ST_AdvancedMode_Mug = 40017,
        NIN_ST_AdvancedMode_TrickAttack = 40018,
        NIN_ST_AdvancedMode_StunInterupt = 40019,
        NIN_ST_AdvancedMode_Feint = 40020,
        NIN_ST_AdvancedMode_SecondWind = 40021,
        NIN_ST_AdvancedMode_ShadeShift = 40022,
        NIN_ST_AdvancedMode_Bloodbath = 40023,
        NIN_ST_AdvancedMode_Raiju = 40024,
        NIN_ST_AdvancedMode_ThrowingDaggers = 40025,
        NIN_ST_AdvancedMode_PhantomKamaitachi = 40026,
        NIN_AoE_AdvancedMode_Ninjitsus = 40027,
        NIN_AoE_AdvancedMode_Ninjitsus_Goka = 40028,
        NIN_AoE_AdvancedMode_Ninjitsus_Huton = 40029,
        NIN_AoE_AdvancedMode_Ninjitsus_Doton = 40030,
        NIN_AoE_AdvancedMode_Ninjitsus_Katon = 40031,
        NIN_AoE_AdvancedMode_Kassatsu = 40032,
        NIN_AoE_AdvancedMode_Bunshin = 40033,
        NIN_AoE_AdvancedMode_TenChiJin = 40034,
        NIN_AoE_AdvancedMode_TenriJindo = 40035,
        NIN_AoE_AdvancedMode_Assassinate = 40036,
        NIN_AoE_AdvancedMode_Meisui = 40037,
        NIN_AoE_AdvancedMode_HellfrogMedium = 40038,
        NIN_AoE_AdvancedMode_Mug = 40039,
        NIN_AoE_AdvancedMode_TrickAttack = 40040,
        NIN_AoE_AdvancedMode_StunInterupt = 40041,
        NIN_AoE_AdvancedMode_SecondWind = 40042,
        NIN_AoE_AdvancedMode_ShadeShift = 40043,
        NIN_AoE_AdvancedMode_Bloodbath = 40044,
        NIN_AoE_AdvancedMode_ThrowingDaggers = 40045,
        NIN_AoE_AdvancedMode_PhantomKamaitachi = 40046,
        NIN_MudraProtection = 40047,
        NIN_ST_AeolianEdgeCombo = 40048,
        NIN_ArmorCrushCombo = 40049,
        NIN_HideMug = 40050,
        NIN_KassatsuChiJin = 40051,
        NIN_KassatsuTrick = 40052,
        NIN_TCJMeisui = 40053,
        NIN_TCJ = 40054,
        NIN_Simple_Mudras = 40055,
        NIN_Simple_Mudras_Alt = 40056,
        NIN_Anti_Rabbit = 40057,
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

    /// <summary>FAKE of Roles/RoleActions.cs: the Melee/Physical members NIN touches, consts mirrored.</summary>
    internal static class RoleActions
    {
        public static class Physical
        {
            public const uint SecondWind = 7541;   // mirrored from RoleActions.cs:81
            public const uint ArmsLength = 7548;   // mirrored from RoleActions.cs:82
        }

        public static class Melee
        {
            public const uint LegSweep = 7863;     // mirrored from RoleActions.cs:136
            public const uint Bloodbath = 7542;    // mirrored from RoleActions.cs:137
            public const uint Feint = 7549;        // mirrored from RoleActions.cs:138
            public const uint TrueNorth = 7546;    // mirrored from RoleActions.cs:139

            public static class Debuffs
            {
                public const ushort Feint = 1195;  // mirrored from RoleActions.cs:164
            }

            public static bool CanLegSweep() => GluttonyCombo.RotationHarness.NIN.FakeGame.LegSweepReady;
        }
    }

    /// <summary>FAKE of ALL/JobClasses.cs Melee base (the real one routes through Roles/RoleImplementation).</summary>
    internal class Melee
    {
        protected Melee() { }

        public static FakeMeleeRole Role { get; } = new();
    }

    /// <summary>FAKE of the IMelee role surface NIN uses; consts mirrored from RoleActions.cs.</summary>
    internal class FakeMeleeRole
    {
        public uint SecondWind => 7541;
        public uint ArmsLength => 7548;
        public uint LegSweep => 7863;
        public uint Bloodbath => 7542;
        public uint Feint => 7549;
        public uint TrueNorth => 7546;

        public bool CanSecondWind(int healthpercent) => GluttonyCombo.RotationHarness.NIN.FakeGame.RoleActionReady;
        public bool CanBloodBath(int healthpercent) => GluttonyCombo.RotationHarness.NIN.FakeGame.RoleActionReady;
        public bool CanFeint() => GluttonyCombo.RotationHarness.NIN.FakeGame.RoleActionReady;
        public bool CanTrueNorth() => GluttonyCombo.RotationHarness.NIN.FakeGame.RoleActionReady;
        public bool CanLegSweep() => GluttonyCombo.RotationHarness.NIN.FakeGame.LegSweepReady;
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
    /// <summary>FAKE of ALL/Items.cs: only the two members opener lambdas compile against.</summary>
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
    /// <summary>FAKE of Data/ContentCheck.cs: only UltimateTerritoryIDs, values mirrored.</summary>
    public class ContentCheck
    {
        public static class UltimateTerritoryIDs
        {
            public const uint FRU = 1238; // mirrored from ContentCheck.cs:27
            public const uint DMU = 1363; // mirrored from ContentCheck.cs:28
        }
    }

    /// <summary>
    ///     FAKE of Data/ActionWatching.cs: only <c>LastAction</c> (the real class's static ctors read
    ///     Lumina sheets and would crash offline). The real member is a static uint property.
    /// </summary>
    public static class ActionWatching
    {
        public static uint LastAction => GluttonyCombo.RotationHarness.NIN.FakeGame.LastAction;
    }
}

// ======================================================================================
namespace GluttonyCombo.Data
{
    /// <summary>FAKE of Data/CooldownData.cs: plain settable data instead of the ActionManager memory reads.
    ///     Cooldown charge math and sheet lookups are therefore OUT of the tested boundary.</summary>
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.NIN.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.NIN.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.NIN.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of UserBoolArray: per-index bool reads keyed off the config name.</summary>
    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public bool this[int index] => GluttonyCombo.RotationHarness.NIN.FakeGame.GetBool($"{ConfigName}[{index}]", false);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Ninja files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.NIN.FakeGame"/>; defaults make a clean, in-combat,
    ///     on-target, everything-learned level-100 state with no buffs and no cooldowns set.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.NIN.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset); // mirrored from Misc.cs:35

        // ---- player / status / target ----
        public static GluttonyCombo.RotationHarness.NIN.FakePlayer LocalPlayer { get; } = new();

        public static GluttonyCombo.RotationHarness.NIN.FakeTarget CurrentTarget => GluttonyCombo.RotationHarness.NIN.FakeGame.CurrentTarget; // real: Target.cs:42

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.NIN.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.NIN.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.NIN.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.NIN.FakeGame.HasBattleTarget;

        public static bool HasTarget() => CurrentTarget is not null; // mirrored from Target.cs:49

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.NIN.FakeGame.TargetHPPercent;

        public static float PlayerHealthPercentageHp() => GluttonyCombo.RotationHarness.NIN.FakeGame.HealthPercent; // real: Target.cs:274

        public static float GetTargetDistance(object? optionalTarget = null, object? optionalSource = null) =>
            GluttonyCombo.RotationHarness.NIN.FakeGame.TargetDistance; // real: Target.cs:367

        public static bool TargetNeedsPositionals() => GluttonyCombo.RotationHarness.NIN.FakeGame.TargetNeedsPositionals;

        public static bool OnTargetsFlank() => GluttonyCombo.RotationHarness.NIN.FakeGame.OnTargetsFlank;

        public static bool OnTargetsRear() => GluttonyCombo.RotationHarness.NIN.FakeGame.OnTargetsRear;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.NIN.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.NIN.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.NIN.FakeGame.InActionRange;

        public static int NumberOfEnemiesInRange(uint aoeSpell, object? target = null, bool checkIgnoredList = false) =>
            GluttonyCombo.RotationHarness.NIN.FakeGame.NumberOfEnemiesInRange(aoeSpell); // real: Target.cs:422

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.NIN.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.NIN.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.NIN.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.NIN.FakeGame.AllTraitsKnown;

        // ---- weave / combo state / timers ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.NIN.FakeGame.CanWeave;

        public static bool CanDelayedWeave(float weaveStart = 1.25f, float weaveEnd = 0.6f, int? maxWeaves = null) =>
            GluttonyCombo.RotationHarness.NIN.FakeGame.CanDelayedWeave; // real: Action.cs:353

        public static bool WasLastAction(uint actionId) => GluttonyCombo.RotationHarness.NIN.FakeGame.LastAction == actionId; // real: Action.cs:213

        public static float ComboTimer => GluttonyCombo.RotationHarness.NIN.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.NIN.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.NIN.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.NIN.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.NIN.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.NIN.FakeGame.GroupDamageIncoming;

        public static TimeSpan CombatEngageDuration() => GluttonyCombo.RotationHarness.NIN.FakeGame.CombatEngageDuration; // real: Timer.cs:53

        public static TimeSpan TimeStoodStill => GluttonyCombo.RotationHarness.NIN.FakeGame.TimeStoodStill; // real: Movement.cs:51

        public delegate void OnStatusChangedDelegate(uint statusId, bool onPlayer);

        public static event OnStatusChangedDelegate? OnStatusChanged; // real: Timer.cs:44 (never raised offline)

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => GluttonyCombo.RotationHarness.NIN.FakeGauges.Get<T>();
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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: plain objects, as in the VPR spike.</summary>
    public static class SimpleTarget
    {
        public static object? UIMouseOverTarget => GluttonyCombo.RotationHarness.NIN.FakeGame.UiMouseOver;

        public static object? ModelMouseOverTarget => null;

        public static object? HardTarget => GluttonyCombo.RotationHarness.NIN.FakeGame.HardTarget;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the NIN opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.NIN_ST_AdvancedMode_BalanceOpener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.NIN.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.NIN.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.NIN.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of the UIntExtensions surface NIN touches: LevelChecked/TraitLevelChecked mirror the
    ///     real implementations (UIntExtensions.cs:11-13); Retarget kept from the VPR spike.
    /// </summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;

        internal static bool LevelChecked(this uint value) => GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions.ActionLearned(value);

        internal static bool TraitLevelChecked(this uint value) => GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions.TraitLevelChecked(value);
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE parts of the partial NIN class: the settings class (real one drags ImGui/localization,
    // NIN_Config.cs) and the positional-hint reporter (real one is an IPC side channel,
    // NIN_PositionalHints.cs). Config defaults are mirrored one-for-one from NIN_Config.cs.
    internal partial class NIN
    {
        private static void ReportNINPositionalHints()
        {
            // no-op: hint reporting is a UI/IPC side channel, out of the tested boundary
        }

        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                NIN_ST_AdvancedMode_BurnKazematoi = new("NIN_ST_AdvancedMode_BurnKazematoi", 10),
                NIN_ST_AdvancedMode_SecondWindThreshold = new("NIN_ST_AdvancedMode_SecondWindThreshold", 40),
                NIN_ST_AdvancedMode_ShadeShiftThreshold = new("NIN_ST_AdvancedMode_ShadeShiftThreshold", 20),
                NIN_ST_AdvancedMode_BloodbathThreshold = new("NIN_ST_AdvancedMode_BloodbathThreshold", 40),
                NIN_ST_AdvancedMode_Mug_Threshold = new("NIN_ST_AdvancedMode_Mug_Threshold", 40),
                NIN_ST_AdvancedMode_Mug_SubOption = new("NIN_ST_AdvancedMode_Mug_SubOption", 0),
                NIN_ST_AdvancedMode_TrickAttack_Threshold = new("NIN_ST_AdvancedMode_TrickAttack_Threshold", 40),
                NIN_ST_AdvancedMode_TrickAttack_SubOption = new("NIN_ST_AdvancedMode_TrickAttack_SubOption", 0),
                NIN_ST_AdvancedMode_Ninjitsus_Suiton_Setup = new("NIN_ST_AdvancedMode_Ninjitsus_Suiton_Setup", 18),
                NIN_ST_AdvancedMode_Ninjitsus_Doton_Threshold = new("NIN_ST_AdvancedMode_Ninjitsus_Doton_Threshold", 40),
                NIN_AoE_AdvancedMode_SecondWindThreshold = new("NIN_AoE_AdvancedMode_SecondWindThreshold", 40),
                NIN_AoE_AdvancedMode_Ninjitsus_Huton_Setup = new("NIN_AoE_AdvancedMode_Ninjitsus_Huton_Setup", 18),
                NIN_AoE_AdvancedMode_Ninjitsus_Doton_Threshold = new("NIN_AoE_AdvancedMode_Ninjitsus_Doton_Threshold", 40),
                NIN_AoE_AdvancedMode_ShadeShiftThreshold = new("NIN_AoE_AdvancedMode_ShadeShiftThreshold", 20),
                NIN_AoE_AdvancedMode_BloodbathThreshold = new("NIN_AoE_AdvancedMode_BloodbathThreshold", 40),
                NIN_AoE_AdvancedMode_Mug_Threshold = new("NIN_AoE_AdvancedMode_Mug_Threshold", 40),
                NIN_AoE_AdvancedMode_Mug_SubOption = new("NIN_AoE_AdvancedMode_Mug_SubOption", 0),
                NIN_AoE_AdvancedMode_TrickAttack_Threshold = new("NIN_AoE_AdvancedMode_TrickAttack_Threshold", 40),
                NIN_AoE_AdvancedMode_TrickAttack_SubOption = new("NIN_AoE_AdvancedMode_TrickAttack_SubOption", 0),
                NIN_Adv_Opener_Selection = new("NIN_Adv_Opener_Selection", 0),
                NIN_Balance_Content = new("NIN_Balance_Content", 1),
                NIN_SimpleMudra_Choice = new("NIN_SimpleMudra_Choice", 1);

            public static CustomComboNS.Functions.UserBool
                NIN_Opener_Potion = new("NIN_Opener_Potion"),
                NIN_Opener_PrepullBlock = new("NIN_Opener_PrepullBlock", true),
                NIN_ST_AdvancedMode_Bhavacakra_Pooling = new("Ninki_BhavaPooling"),
                NIN_ST_AdvancedMode_TrueNorth = new("NIN_ST_AdvancedMode_TrueNorth"),
                NIN_ST_AdvancedMode_ShadeShiftRaidwide = new("NIN_ST_AdvancedMode_ShadeShiftRaidwide"),
                NIN_ST_AdvancedMode_ForkedRaiju = new("NIN_ST_AdvancedMode_ForkedRaiju"),
                NIN_ST_AdvancedMode_Ninjitsus_Raiton_Pooling = new("NIN_ST_AdvancedMode_Ninjitsus_Raiton_Pooling"),
                NIN_ST_AdvancedMode_Ninjitsus_Raiton_Uptime = new("NIN_ST_AdvancedMode_Ninjitsus_Raiton_Uptime"),
                NIN_ST_AdvancedMode_TenChiJin_Auto = new("NIN_ST_AdvancedMode_TenChiJin_Auto"),
                NIN_AoE_AdvancedMode_Ninjitsus_Katon_Pooling = new("NIN_AoE_AdvancedMode_Ninjitsus_Katon_Pooling"),
                NIN_AoE_AdvancedMode_Ninjitsus_Katon_Uptime = new("NIN_AoE_AdvancedMode_Ninjitsus_Katon_Uptime"),
                NIN_AoE_AdvancedMode_TenChiJin_Auto = new("NIN_AoE_AdvancedMode_TenChiJin_Auto"),
                NIN_AoE_AdvancedMode_HellfrogMedium_Pooling = new("Ninki_HellfrogPooling"),
                NIN_AoE_AdvancedMode_ShadeShiftRaidwide = new("NIN_AoE_AdvancedMode_ShadeShiftRaidwide"),
                NIN_HideMug_TrickAfterMug = new("NIN_HideMug_TrickAfterMug"),
                NIN_HideMug_ToggleLevelCheck = new("NIN_HideMug_ToggleLevelCheck"),
                NIN_HideMug_Toggle = new("NIN_HideMug_Toggle"),
                NIN_HideMug_Trick = new("NIN_HideMug_Trick"),
                NIN_HideMug_Mug = new("NIN_HideMug_Mug");

            public static CustomComboNS.Functions.UserBoolArray
                NIN_MudraProtection_Options = new("NIN_MudraProtection_Options");

            public static CustomComboNS.Functions.UserFloat
                NIN_AoE_AdvancedMode_Ninjitsus_Doton_TimeStill = new("NIN_AoE_AdvancedMode_Ninjitsus_Doton_TimeStill", 3f),
                NIN_AoE_AdvancedMode_TCJ_Doton_Timer = new("NIN_AoE_AdvancedMode_TCJ_Doton_Timer", 3f);
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>Namespace stub: NIN.cs may carry `using GluttonyCombo.Core;`; real members are faked elsewhere.</summary>
    internal static class HarnessCoreNamespaceStub;
}
