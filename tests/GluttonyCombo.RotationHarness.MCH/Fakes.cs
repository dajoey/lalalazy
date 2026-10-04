// Fakes.cs — the harness-owned stand-ins for everything the REAL Machinist job files (MCH.cs,
// MCH_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary
// is deliberate (notebook "Offline Harness Approach" section 3):
//   REAL  : every decision line in MCH.cs / MCH_Helper.cs (Invoke branches, hypercharge/wildfire/queen/
//           reassemble/gauss helpers, gauge props, action IDs, Buffs/Debuffs/Traits tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, enemy counts, combat), the Preset enum values, the MCH Config
//           settings defaults, the physical-ranged role layer, openers' WrathOpener base, content/IPC
//           side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Machinist members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision logic —
    ///     presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        MCH_ST_SimpleMode = 31001,
        MCH_AoE_SimpleMode = 31002,
        MCH_ST_AdvancedMode = 31003,
        MCH_AoE_AdvancedMode = 31004,
        MCH_ST_BasicCombo = 31005,
        MCH_DismantleProtection = 31006,
        MCH_DismantleTactician = 31007,
        MCH_Heatblast = 31008,
        MCH_Heatblast_AutoBarrel = 31009,
        MCH_Heatblast_Wildfire = 31010,
        MCH_Heatblast_GaussRound = 31011,
        MCH_AutoCrossbow = 31012,
        MCH_AutoCrossbow_AutoBarrel = 31013,
        MCH_AutoCrossbow_GaussRound = 31014,
        MCH_Overdrive = 31015,
        MCH_BigHitter = 31016,
        MCH_GaussRoundRicochet = 31017,
        MCH_ST_Adv_Opener = 31018,
        MCH_ST_Adv_GaussRicochet = 31019,
        MCH_ST_Adv_QueenOverdrive = 31020,
        MCH_ST_Adv_WildFire = 31021,
        MCH_ST_Adv_Hypercharge = 31022,
        MCH_ST_Adv_Tools_AllowExcavatorPostWildfire = 31023,
        MCH_ST_Adv_Tools_AllowClainsawPostWildfire = 31024,
        MCH_ST_Adv_Reassemble = 31025,
        MCH_ST_Adv_Stabilizer = 31026,
        MCH_ST_Adv_TurretQueen = 31027,
        MCH_ST_Adv_QueenInHypercharge = 31028,
        MCH_ST_Dismantle = 31029,
        MCH_ST_Adv_Tactician = 31030,
        MCH_ST_Adv_SecondWind = 31031,
        MCH_ST_Adv_Interrupt = 31032,
        MCH_ST_Adv_Stabilizer_FullMetalField = 31033,
        MCH_ST_Adv_Tools = 31034,
        MCH_ST_Adv_Heatblast = 31035,
        MCH_AoE_Adv_GaussRicochet = 31036,
        MCH_AoE_Adv_QueenOverdrive = 31037,
        MCH_AoE_Adv_Hypercharge = 31038,
        MCH_AoE_Adv_Reassemble = 31039,
        MCH_AoE_Adv_Stabilizer = 31040,
        MCH_AoE_Adv_Queen = 31041,
        MCH_AoE_Adv_SecondWind = 31042,
        MCH_AoE_Adv_Interrupt = 31043,
        MCH_AoE_Adv_Stabilizer_FullMetalField = 31044,
        MCH_AoE_Adv_FlameThrower = 31045,
        MCH_AoE_Adv_Tools = 31046,
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

    /// <summary>FAKE slices of other jobs' Buffs tables: only the two consts MCH's Tactician check names.</summary>
    internal partial class BRD
    {
        public static class Buffs
        {
            public const ushort Troubadour = 1934; // mirrored from BRD_Helper.cs
        }
    }

    internal partial class DNC
    {
        public static class Buffs
        {
            public const ushort ShieldSamba = 1826; // mirrored from DNC_Helper.cs
        }
    }

    /// <summary>FAKE of ALL/JobClasses.cs PhysicalRanged base (the real one routes through Roles/RoleImplementation).</summary>
    internal class PhysicalRanged
    {
        protected PhysicalRanged() { }

        public static FakePhysRangedRole Role { get; } = new();
    }

    /// <summary>FAKE of the IPhysicalRanged role surface MCH uses; consts mirrored from RoleActions.cs.</summary>
    internal class FakePhysRangedRole
    {
        public uint SecondWind => 7541; // Physical.SecondWind (RoleActions.cs:81)
        public uint HeadGraze => 7551;  // PhysRanged.HeadGraze (RoleActions.cs:107)

        public bool CanSecondWind(int healthpercent) => GluttonyCombo.RotationHarness.MCH.FakeGame.RoleActionReady;

        public bool CanHeadGraze(Combos.Preset preset) => GluttonyCombo.RotationHarness.MCH.FakeGame.RoleActionReady;

        public bool CanHeadGraze(bool simpleMode) => GluttonyCombo.RotationHarness.MCH.FakeGame.RoleActionReady && simpleMode;
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
    /// <summary>FAKE of ALL/Items.cs: only the members the MCH opener lambdas compile against.</summary>
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.MCH.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.MCH.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.MCH.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Machinist files read. Every member routes
    ///     into <see cref="GluttonyCombo.RotationHarness.MCH.FakeGame"/>; defaults make a clean,
    ///     weave-blocked, everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.MCH.FakeGame.EnabledPresets.Contains(preset);

        // ---- player / status / target ----
        public static GluttonyCombo.RotationHarness.MCH.FakePlayer LocalPlayer { get; } = new();

        public static GluttonyCombo.RotationHarness.MCH.FakeTarget CurrentTarget { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.MCH.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.MCH.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.MCH.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.MCH.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.MCH.FakeGame.TargetHPPercent;

        public static bool TargetNeedsPositionals() => GluttonyCombo.RotationHarness.MCH.FakeGame.TargetNeedsPositionals;

        public static bool OnTargetsFlank() => GluttonyCombo.RotationHarness.MCH.FakeGame.OnTargetsFlank;

        public static bool OnTargetsRear() => GluttonyCombo.RotationHarness.MCH.FakeGame.OnTargetsRear;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.MCH.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.MCH.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.MCH.FakeGame.InActionRange;

        // ---- enemy / ally geometry (MCH's Auto Crossbow threshold reads this) ----
        public static int NumberOfEnemiesInRange
            (uint aoeSpell, GluttonyCombo.RotationHarness.MCH.FakeTarget? target = null, bool checkIgnoredList = false) =>
            GluttonyCombo.RotationHarness.MCH.FakeGame.EnemiesInRange;

        public static int NumberOfAlliesInRange
            (uint aoeSpell, GluttonyCombo.RotationHarness.MCH.FakeTarget? target = null) =>
            GluttonyCombo.RotationHarness.MCH.FakeGame.AlliesInRange;

        public static List<object> GetPartyMembers(bool allowCache = true) =>
            Enumerable.Range(0, GluttonyCombo.RotationHarness.MCH.FakeGame.PartySize).Select(_ => new object()).ToList();

        // ---- movement ----
        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.MCH.FakeGame.IsMoving;

        public static TimeSpan TimeStoodStill => GluttonyCombo.RotationHarness.MCH.FakeGame.TimeStoodStill;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.MCH.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.MCH.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.MCH.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static ushort GetMaxCharges(uint actionID) => GetCooldown(actionID).MaxCharges;

        public static uint CalcBestAction(uint original, params uint[] actions) =>
            actions.Length == 0 ? original : actions.MinBy(a => GetCooldownRemainingTime(a))!;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.MCH.FakeGame.AllTraitsKnown;

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.MCH.FakeGame.CanWeave;

        public static bool HasWeaved(int weaveAmount = 1) => GluttonyCombo.RotationHarness.MCH.FakeGame.HasWeavedCount >= weaveAmount;

        public static float ComboTimer => GluttonyCombo.RotationHarness.MCH.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.MCH.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.MCH.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.MCH.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.MCH.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.MCH.FakeGame.GroupDamageIncoming;

        // ---- status-change side channel (MCH's static ctor subscribes; the fake never raises it) ----
        public delegate void OnStatusChangedDelegate(uint statusId, bool onPlayer);

        public static event OnStatusChangedDelegate? OnStatusChanged;

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => GluttonyCombo.RotationHarness.MCH.FakeGauges.Get<T>();
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

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the MCH opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.MCH_ST_Adv_Opener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public virtual List<int> AllowUpgradeSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.MCH.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.MCH.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.MCH.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>FAKE of the UIntExtensions surface (MCH.cs carries `using GluttonyCombo.Extensions;`).</summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial MCH class: the settings class (the real MCH_Config.cs drags ImGui and
    // localization into the compile; the real Draw code is UI, not a rotation decision). Defaults are
    // mirrored one-for-one from MCH_Config.cs #region Variables.
    internal partial class MCH
    {
        internal static class Config
        {
            public static UserInt
                //ST
                MCH_Balance_Content = new("MCH_Balance_Content", 1),
                MCH_SelectedOpener = new("MCH_SelectedOpener"),
                MCH_HaveTarget = new("MCH_HaveTarget"),
                MCH_ST_QueenOverDriveHPThreshold = new("MCH_ST_QueenOverDriveHPThreshold", 1),
                MCH_ST_BarrelStabilizerBossOnlyOption = new("MCH_ST_BarrelStabilizerBossOnlyOption", 1),
                MCH_ST_BarrelStabilizerHPOption = new("MCH_ST_BarrelStabilizerHPOption", 10),
                MCH_ST_BarrelStabilizerHPBossOption = new("MCH_ST_BarrelStabilizerHPBossOption"),
                MCH_ST_WildfireBossOnlyOption = new("MCH_ST_WildfireBossOnlyOption", 1),
                MCH_ST_WildfireHPOption = new("MCH_ST_WildfireHPOption", 25),
                MCH_ST_WildfireHPBossOption = new("MCH_ST_WildfireHPBossOption"),
                MCH_ST_HyperchargeHPBossOption = new("MCH_ST_HyperchargeHPBossOption"),
                MCH_ST_HyperchargeHPOption = new("MCH_ST_HyperchargeHPOption", 25),
                MCH_ST_ReassembleHPBossOption = new("MCH_ST_ReassembleHPBossOption"),
                MCH_ST_Adv_ReassembleChoice = new("MCH_ST_Adv_ReassembleChoice"),
                MCH_ST_ReassembleHPOption = new("MCH_ST_ReassembleHPOption", 25),
                MCH_ST_ToolsHPBossOption = new("MCH_ST_ToolsHPBossOption"),
                MCH_ST_ToolsHPOption = new("MCH_ST_ToolsHPOption", 25),
                MCH_ST_QueenHPOption = new("MCH_ST_QueenHPOption", 25),
                MCH_ST_QueenHPBossOption = new("MCH_ST_QueenHPBossOption"),
                MCH_ST_TurretUsage = new("MCH_ST_TurretUsage", 100),
                MCH_ST_ReassemblePool = new("MCH_ST_ReassemblePool"),
                MCH_ST_GaussRicoManualUse = new("MCH_ST_GaussRicoManualUse"),
                MCH_ST_GaussOnlyOrBoth = new("MCH_ST_GaussOnlyOrBoth"),
                MCH_ST_SecondWindHPThreshold = new("MCH_ST_SecondWindHPThreshold", 40),

                //AoE
                MCH_AoE_ReassemblePool = new("MCH_AoE_ReassemblePool"),
                MCH_AoE_TurretBatteryUsage = new("MCH_AoE_TurretBatteryUsage", 100),
                MCH_AoE_FlamethrowerMovement = new("MCH_AoE_FlamethrowerMovement"),
                MCH_AoE_FlamethrowerHPOption = new("MCH_AoE_FlamethrowerHPOption", 25),
                MCH_AoE_HyperchargeHPThreshold = new("MCH_AoE_HyperchargeHPThreshold", 25),
                MCH_AoE_ReassembleHPThreshold = new("MCH_AoE_ReassembleHPThreshold", 25),
                MCH_AoE_ToolsHPThreshold = new("MCH_AoE_ToolsHPThreshold", 25),
                MCH_AoE_QueenHpThreshold = new("MCH_AoE_QueenHpThreshold", 25),
                MCH_AoE_BarrelStabilizerHPThreshold = new("MCH_AoE_BarrelStabilizerHPThreshold", 25),
                MCH_AoE_QueenOverDriveHPThreshold = new("MCH_AoE_QueenOverDriveHPThreshold", 25),
                MCH_AoE_SecondWindHPThreshold = new("MCH_AoE_SecondWindHPThreshold", 40),

                //Misc
                MCH_GaussRico = new("MCHGaussRico"),
                MCH_DismantledDuration = new("MCH_DismantledDuration");

            public static UserFloat
                MCH_AoE_FlamethrowerTimeStill = new("MCH_AoE_FlamethrowerTimeStill", 2.5f),
                MCH_AoE_HyperchargeToolHold = new("MCH_AoE_HyperchargeToolHold", 8f),
                MCH_ST_WildfireHyperchargeCutoffThreshold = new("MCH_ST_WildfireHyperchargeCutoffThreshold", 9f);

            public static UserBool
                MCH_Opener_Potion = new("MCH_Opener_Potion"),
                MCH_AoE_AirAnchor = new("MCH_AoE_AirAnchor"),
                MCH_Opener_PrepullBlock = new("MCH_Opener_PrepullBlock", true);
        }
    }
}
