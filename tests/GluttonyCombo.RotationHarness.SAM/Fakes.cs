// Fakes.cs — the harness-owned stand-ins for everything the REAL Samurai job files (SAM.cs,
// SAM_Helper.cs, compiled unchanged from the branch) reach outside themselves. Derived from the
// round-6 RotationHarness.NIN Fakes.cs (itself from the VPR spike). The boundary:
//   REAL  : every decision line in SAM.cs / SAM_Helper.cs (Invoke branches, Kenki/Sen/Kaeshi
//           helpers, gauge props, action IDs, Buffs/Traits tables, opener classes).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, weave, enemies-in-range, movement, party), the Preset
//           enum values, the SAM Config settings defaults, the role-action layer, openers'
//           WrathOpener base, ActionWatching.LastAction/NumberOfGcdsUsed, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Collections.Generic;
using System.Reflection;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Samurai members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision
    ///     logic — presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        SAM_ST_SimpleMode = 50001,
        SAM_AoE_SimpleMode = 50002,
        SAM_ST_AdvancedMode = 50003,
        SAM_AoE_AdvancedMode = 50004,
        SAM_ST_Adv_Opener = 50005,
        SAM_ST_Adv_CDs = 50006,
        SAM_ST_Adv_Meikyo = 50007,
        SAM_ST_Adv_Damage = 50008,
        SAM_ST_Adv_Senei = 50009,
        SAM_ST_Adv_Shinten = 50010,
        SAM_ST_Adv_Ikishoten = 50011,
        SAM_ST_Adv_Zanshin = 50012,
        SAM_ST_Adv_Shoha = 50013,
        SAM_ST_Adv_Iaijutsu = 50014,
        SAM_ST_Adv_Tsubame = 50015,
        SAM_ST_Adv_OgiNamikiri = 50016,
        SAM_ST_Adv_Higanbana = 50017,
        SAM_ST_Adv_TenkaGoken = 50018,
        SAM_ST_Adv_Midare = 50019,
        SAM_ST_Adv_Iaijutsu_Movement = 50020,
        SAM_ST_Adv_RangedUptime = 50021,
        SAM_ST_Adv_TrueNorth = 50022,
        SAM_ST_Adv_Yukikaze = 50023,
        SAM_ST_Adv_Kasha = 50024,
        SAM_ST_Adv_Gekko = 50025,
        SAM_ST_Adv_Feint = 50026,
        SAM_ST_Adv_ThirdEye = 50027,
        SAM_ST_Adv_Meditate = 50028,
        SAM_ST_Adv_ComboHeals = 50029,
        SAM_ST_Adv_StunInterrupt = 50030,
        SAM_ST_YukikazeCombo = 50031,
        SAM_ST_KashaCombo = 50032,
        SAM_ST_GekkoCombo = 50033,
        SAM_AoE_OkaCombo = 50034,
        SAM_AoE_MangetsuCombo = 50035,
        SAM_MeikyoSens = 50036,
        SAM_MeikyoShisuiProtection = 50037,
        SAM_Iaijutsu = 50038,
        SAM_Shinten = 50039,
        SAM_Kyuten = 50040,
        SAM_Ikishoten = 50041,
        SAM_GyotenYaten = 50042,
        SAM_SeneiGuren = 50043,
        SAM_OgiShoha = 50044,
        SAM_Iaijutsu_TsubameGaeshi = 50045,
        SAM_Iaijutsu_OgiNamikiri = 50046,
        SAM_Iaijutsu_Shoha = 50047,
        SAM_Shinten_Senei = 50048,
        SAM_Shinten_Shoha = 50049,
        SAM_Shinten_Zanshin = 50050,
        SAM_Shinten_Ikishoten = 50051,
        SAM_Kyuten_Guren = 50052,
        SAM_Kyuten_Shoha = 50053,
        SAM_Kyuten_Zanshin = 50054,
        SAM_Kyuten_Ikishoten = 50055,
        SAM_Ikishoten_Namikiri = 50056,
        SAM_Ikishoten_Shoha = 50057,
        SAM_AoE_Adv_CDs = 50058,
        SAM_AoE_Adv_Meikyo = 50059,
        SAM_AoE_Adv_Damage = 50060,
        SAM_AoE_Adv_Guren = 50061,
        SAM_AoE_Adv_Kyuten = 50062,
        SAM_AoE_Adv_Ikishoten = 50063,
        SAM_AoE_Adv_Zanshin = 50064,
        SAM_AoE_Adv_Shoha = 50065,
        SAM_AoE_Adv_OgiNamikiri = 50066,
        SAM_AoE_Adv_Oka = 50067,
        SAM_AoE_Adv_TenkaGoken = 50068,
        SAM_AoE_Adv_ComboHeals = 50069,
        SAM_AoE_Adv_StunInterrupt = 50070,
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

    /// <summary>FAKE of Roles/RoleActions.cs: the Melee/Physical members SAM touches, consts mirrored.</summary>
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
            public const uint BloodBath = 7542;    // mirrored from RoleActions.cs:137
            public const uint Feint = 7549;        // mirrored from RoleActions.cs:138
            public const uint TrueNorth = 7546;    // mirrored from RoleActions.cs:139

            public static class Debuffs
            {
                public const ushort Feint = 1195;  // mirrored from RoleActions.cs:164
            }

            public static bool CanLegSweep() => GluttonyCombo.RotationHarness.SAM.FakeGame.LegSweepReady;
        }
    }

    /// <summary>FAKE of ALL/JobClasses.cs Melee base (the real one routes through Roles/RoleImplementation).</summary>
    internal class Melee
    {
        protected Melee() { }

        public static FakeMeleeRole Role { get; } = new();
    }

    /// <summary>FAKE of the IMelee role surface SAM uses; consts mirrored from RoleActions.cs.</summary>
    internal class FakeMeleeRole
    {
        public uint SecondWind => 7541;
        public uint ArmsLength => 7548;
        public uint LegSweep => 7863;
        public uint Bloodbath => 7542;
        public uint Feint => 7549;
        public uint TrueNorth => 7546;

        public bool CanSecondWind(int healthpercent) => GluttonyCombo.RotationHarness.SAM.FakeGame.RoleActionReady;
        public bool CanBloodBath(int healthpercent) => GluttonyCombo.RotationHarness.SAM.FakeGame.RoleActionReady;
        public bool CanFeint() => GluttonyCombo.RotationHarness.SAM.FakeGame.RoleActionReady;
        public bool CanTrueNorth() => GluttonyCombo.RotationHarness.SAM.FakeGame.RoleActionReady;
        public bool CanLegSweep() => GluttonyCombo.RotationHarness.SAM.FakeGame.LegSweepReady;
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
    ///     FAKE of Data/ActionWatching.cs: only <c>LastAction</c> and <c>NumberOfGcdsUsed</c> (the
    ///     real class's static ctors read Lumina sheets and would crash offline).
    /// </summary>
    public static class ActionWatching
    {
        public static uint LastAction => GluttonyCombo.RotationHarness.SAM.FakeGame.LastAction;

        public static int NumberOfGcdsUsed => GluttonyCombo.RotationHarness.SAM.FakeGame.NumberOfGcdsUsed;
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.SAM.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.SAM.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.SAM.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of UserBoolArray: per-index bool reads keyed off the config name.</summary>
    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public bool this[int index] => GluttonyCombo.RotationHarness.SAM.FakeGame.GetBool($"{ConfigName}[{index}]", false);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Samurai files read. Every member routes
    ///     into <see cref="GluttonyCombo.RotationHarness.SAM.FakeGame"/>; defaults make a clean,
    ///     in-combat, on-target, everything-learned level-100 state with no buffs and no cooldowns set.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.SAM.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset); // mirrored from Misc.cs:35

        // ---- player / status / target ----
        public static GluttonyCombo.RotationHarness.SAM.FakePlayer LocalPlayer { get; } = new();

        public static GluttonyCombo.RotationHarness.SAM.FakeTarget CurrentTarget => GluttonyCombo.RotationHarness.SAM.FakeGame.CurrentTarget; // real: Target.cs:42

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.SAM.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.SAM.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.SAM.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.SAM.FakeGame.HasBattleTarget;

        public static bool HasTarget() => CurrentTarget is not null; // mirrored from Target.cs:49

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.SAM.FakeGame.TargetHPPercent;

        public static float PlayerHealthPercentageHp() => GluttonyCombo.RotationHarness.SAM.FakeGame.HealthPercent; // real: Target.cs:274

        public static float GetTargetDistance(object? optionalTarget = null, object? optionalSource = null) =>
            GluttonyCombo.RotationHarness.SAM.FakeGame.TargetDistance; // real: Target.cs:367

        public static bool TargetNeedsPositionals() => GluttonyCombo.RotationHarness.SAM.FakeGame.TargetNeedsPositionals;

        public static bool OnTargetsFlank() => GluttonyCombo.RotationHarness.SAM.FakeGame.OnTargetsFlank;

        public static bool OnTargetsFront() => GluttonyCombo.RotationHarness.SAM.FakeGame.OnTargetsFront;

        public static bool OnTargetsRear() => GluttonyCombo.RotationHarness.SAM.FakeGame.OnTargetsRear;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.SAM.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.SAM.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.SAM.FakeGame.InActionRange;

        public static int NumberOfEnemiesInRange(uint aoeSpell, object? target = null, bool checkIgnoredList = false) =>
            GluttonyCombo.RotationHarness.SAM.FakeGame.NumberOfEnemiesInRange(aoeSpell); // real: Target.cs:422

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.SAM.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.SAM.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.SAM.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.SAM.FakeGame.AllTraitsKnown;

        public static float GetAdjustedRecastTime(FFXIVClientStructs.FFXIV.Client.Game.ActionType actionType, uint actionID) =>
            2500f; // ms; GCD in the job files is this / 1000 => 2.5 s. Real: Action.cs recast read.

        // ---- weave / combo state / timers / movement ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.SAM.FakeGame.CanWeave;

        public static bool CanDelayedWeave(float weaveStart = 1.25f, float weaveEnd = 0.6f, int? maxWeaves = null) =>
            false; // real: Action.cs:353; no SAM case uses a delayed weave

        public static bool WasLastAction(uint actionId) => GluttonyCombo.RotationHarness.SAM.FakeGame.LastAction == actionId; // real: Action.cs:213

        public static float ComboTimer => GluttonyCombo.RotationHarness.SAM.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.SAM.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.SAM.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.SAM.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.SAM.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming(float withinSeconds = 2f) => GluttonyCombo.RotationHarness.SAM.FakeGame.GroupDamageIncoming; // real: Timer.cs

        public static TimeSpan CombatEngageDuration() => GluttonyCombo.RotationHarness.SAM.FakeGame.CombatEngageDuration; // real: Timer.cs:53

        public static TimeSpan TimeStoodStill => GluttonyCombo.RotationHarness.SAM.FakeGame.TimeStoodStill; // real: Movement.cs:51

        public static bool IsMoving() => GluttonyCombo.RotationHarness.SAM.FakeGame.IsMoving; // real: Movement.cs

        public static bool IsInParty() => GluttonyCombo.RotationHarness.SAM.FakeGame.IsInParty; // real: Group.cs

        public delegate void OnStatusChangedDelegate(uint statusId, bool onPlayer);

        public static event OnStatusChangedDelegate? OnStatusChanged; // real: Timer.cs:44 (never raised offline)

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => GluttonyCombo.RotationHarness.SAM.FakeGauges.Get<T>();
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
        public static object? UIMouseOverTarget => GluttonyCombo.RotationHarness.SAM.FakeGame.UiMouseOver;

        public static object? ModelMouseOverTarget => null;

        public static object? HardTarget => GluttonyCombo.RotationHarness.SAM.FakeGame.HardTarget;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the SAM opener classes
    ///     declare and override. Openers are not exercised by the harness cases (the opener preset is
    ///     off, as in Joey's config); FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.SAM_ST_Adv_Opener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> AllowUpgradeSteps { get; set; } = []; // real: WrathOpener.cs:137

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public virtual List<int> VeryDelayedWeaveSteps { get; set; } = []; // real: WrathOpener.cs:131

        public virtual bool AllowReopener { get; set; } // real: WrathOpener.cs:160

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.SAM.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.SAM.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.SAM.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of the UIntExtensions surface SAM touches: LevelChecked/TraitLevelChecked mirror the
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
    // FAKE parts of the partial SAM class: the settings class (real one drags ImGui/localization,
    // SAM_Config.cs) and the positional-hint reporter (real one is an IPC side channel). Config
    // defaults are mirrored one-for-one from SAM_Config.cs.
    internal partial class SAM
    {
        private static void ReportSAMPositionalHints(bool gekko, bool kasha)
        {
            // no-op: hint reporting is a UI/IPC side channel, out of the tested boundary
        }

        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                SAM_Balance_Content = new("SAM_Balance_Content", 1),
                SAM_ST_Opener_IncludeGyoten = new("SAM_ST_Opener_IncludeGyoten"),
                SAM_ST_HiganbanaHPOption = new("SAM_ST_HiganbanaHPOption"),
                SAM_ST_HiganbanaAddsHPOption = new("SAM_ST_HiganbanaAddsHPOption", 25),
                SAM_ST_HiganbanaTrashHPOption = new("SAM_ST_HiganbanaTrashHPOption", 100),
                SAM_ST_HiganbanaRefresh = new("SAM_ST_HiganbanaRefresh", 15),
                SAM_ST_ShintenKenkiOvercap = new("SAM_ST_ShintenKenkiOvercap", 65),
                SAM_ST_YukikazeCombo_Prio = new("SAM_ST_YukikazeCombo_Prio", 1),
                SAM_ST_ShintenExecuteHP = new("SAM_ST_ShintenExecuteHP", 5),
                SAM_ST_MeikyoExecuteHP = new("SAM_ST_MeikyoExecuteHP", 5),
                SAM_ST_TrueNorthCharges = new("SAM_ST_TrueNorthCharges"),
                SAM_ST_SecondWindOption = new("SAM_ST_SecondWindOption", 40),
                SAM_ST_BloodbathOption = new("SAM_ST_BloodbathOption", 30),
                SAM_AoE_KyutenKenkiOvercap = new("SAM_AoE_KyutenKenkiOvercap", 50),
                SAM_AoE_SecondWindOption = new("SAM_AoE_SecondWindOption", 40),
                SAM_AoE_BloodbathOption = new("SAM_AoE_BloodbathOption", 30),
                SAM_Gekko_KenkiOvercapAmount = new("SAM_Gekko_KenkiOvercapAmount", 65),
                SAM_Kasha_KenkiOvercapAmount = new("SAM_Kasha_KenkiOvercapAmount", 65),
                SAM_Yukikaze_KenkiOvercapAmount = new("SAM_Yukikaze_KenkiOvercapAmount", 65),
                SAM_Oka_KenkiOvercapAmount = new("SAM_Oka_KenkiOvercapAmount", 50),
                SAM_Mangetsu_KenkiOvercapAmount = new("SAM_Mangetsu_KenkiOvercapAmount", 50);

            public static CustomComboNS.Functions.UserBool
                SAM_ST_Opener_Potion = new("SAM_ST_Opener_Potion"),
                SAM_ST_Opener_PrepullBlock = new("SAM_ST_Opener_PrepullBlock", true),
                SAM_Gekko_KenkiOvercap = new("SAM_Gekko_KenkiOvercap"),
                SAM_Kasha_KenkiOvercap = new("SAM_Kasha_KenkiOvercap"),
                SAM_Yukikaze_KenkiOvercap = new("SAM_Yukikaze_KenkiOvercap"),
                SAM_Yukikaze_Gekko = new("SAM_Yukikaze_Gekko"),
                SAM_Yukikaze_Kasha = new("SAM_Yukikaze_Kasha"),
                SAM_Mangetsu_Oka = new("SAM_Mangetsu_Oka"),
                SAM_ST_Senei_Guren = new("SAM_ST_Senei_Guren"),
                SAM_ST_OgiNamikiri_Movement = new("SAM_ST_OgiNamikiri_Movement"),
                SAM_Oka_KenkiOvercap = new("SAM_Oka_KenkiOvercap"),
                SAM_Mangetsu_KenkiOvercap = new("SAM_Mangetsu_KenkiOvercap"),
                SAM_OgiShohaZanshin = new("SAM_OgiShohaZanshin");

            public static CustomComboNS.Functions.UserFloat
                SAM_ST_MeditateTimeStill = new("SAM_ST_MeditateTimeStill", 2.5f);
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>Namespace stub: kept from the NIN harness in case a job file carries the using; no real members faked here.</summary>
    internal static class HarnessCoreNamespaceStub;
}
