// Fakes.cs — the harness-owned stand-ins for everything the REAL Reaper job files (RPR.cs, RPR_Helper.cs,
// compiled unchanged from the pinned revision) reach outside themselves. Adapted from the round-4 VPR
// spike (branch rot/harness-spike); the boundary is unchanged:
//   REAL  : every decision line in RPR.cs / RPR_Helper.cs (Invoke, weave/GCD helpers, gauge props,
//           action IDs, Buffs/Debuffs tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target + target statuses, enemy count, combat), the Preset enum values,
//           the RPR Config settings defaults, the role-action layer, openers' WrathOpener base,
//           content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Reaper members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        RPR_ST_SimpleMode = 31001,
        RPR_AoE_SimpleMode = 31002,
        RPR_ST_AdvancedMode = 31003,
        RPR_AoE_AdvancedMode = 31004,
        RPR_ST_BasicCombo = 31005,
        RPR_AoE_BasicCombo = 31006,
        RPR_ST_Opener = 31007,
        RPR_ST_SoulSow = 31008,
        RPR_ST_ArcaneCircle = 31009,
        RPR_ST_Enshroud = 31010,
        RPR_ST_TrueNorthDynamic = 31011,
        RPR_ST_Gluttony = 31012,
        RPR_ST_Bloodstalk = 31013,
        RPR_ST_Sacrificium = 31014,
        RPR_ST_Lemure = 31015,
        RPR_ST_Feint = 31016,
        RPR_ST_ArcaneCrest = 31017,
        RPR_ST_ComboHeals = 31018,
        RPR_ST_StunInterupt = 31019,
        RPR_ST_Perfectio = 31020,
        RPR_ST_SoulSlice = 31021,
        RPR_ST_SoD = 31022,
        RPR_ST_GibbetGallows = 31023,
        RPR_ST_PlentifulHarvest = 31024,
        RPR_ST_Communio = 31025,
        RPR_ST_Reaping = 31026,
        RPR_ST_RangedFillerHarvestMoon = 31027,
        RPR_ST_RangedFiller = 31028,
        RPR_AoE_SoulSow = 31029,
        RPR_AoE_ArcaneCircle = 31030,
        RPR_AoE_Enshroud = 31031,
        RPR_AoE_Gluttony = 31032,
        RPR_AoE_GrimSwathe = 31033,
        RPR_AoE_Sacrificium = 31034,
        RPR_AoE_Lemure = 31035,
        RPR_AoE_ComboHeals = 31036,
        RPR_AoE_StunInterupt = 31037,
        RPR_AoE_Perfectio = 31038,
        RPR_AoE_WoD = 31039,
        RPR_AoE_PlentifulHarvest = 31040,
        RPR_AoE_Guillotine = 31041,
        RPR_AoE_Communio = 31042,
        RPR_AoE_Reaping = 31043,
        RPR_AoE_SoulScythe = 31044,
        RPR_ST_BasicCombo_SoD = 31045,
        RPR_AoE_BasicCombo_WoD = 31046,
        RPR_GluttonyBloodSwathe = 31047,
        RPR_GluttonyBloodSwathe_BloodSwatheCombo = 31048,
        RPR_GluttonyBloodSwathe_Enshroud = 31049,
        RPR_GluttonyBloodSwathe_OGCD = 31050,
        RPR_GluttonyBloodSwathe_Sacrificium = 31051,
        RPR_TrueNorthGluttony = 31052,
        RPR_BloodStalkEnshroudCombo = 31053,
        RPR_BloodStalkEnshroudCombo_Enshroud = 31054,
        RPR_BloodStalkEnshroudCombo_BloodSwatheCombo = 31055,
        RPR_Soulsow = 31056,
        RPR_Soulsow_Combat = 31057,
        RPR_ArcaneCirclePlentifulHarvest = 31058,
        RPR_Regress = 31059,
        RPR_EnshroudProtection = 31060,
        RPR_TrueNorthEnshroud = 31061,
        RPR_EnshroudCommunio = 31062,
        RPR_CommunioOnGGG = 31063,
        RPR_LemureOnGGG = 31064,
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

    /// <summary>FAKE of Roles/RoleActions.cs: only the melee members RPR touches.</summary>
    internal static class RoleActions
    {
        public static class Melee
        {
            public const uint LegSweep = 7863;   // mirrored from RoleActions.cs:136

            public static bool CanLegSweep() => GluttonyCombo.RotationHarness.FakeGame.LegSweepReady;
        }
    }

    /// <summary>FAKE of ALL/JobClasses.cs Melee base (the real one routes through Roles/RoleImplementation).</summary>
    internal class Melee
    {
        protected Melee() { }

        public static FakeMeleeRole Role { get; } = new();
    }

    /// <summary>FAKE of the IMelee role surface RPR uses; consts mirrored from RoleActions.cs.</summary>
    internal class FakeMeleeRole
    {
        public uint SecondWind => 7541;
        public uint ArmsLength => 7548;
        public uint LegSweep => 7863;
        public uint Bloodbath => 7542;
        public uint Feint => 7549;
        public uint TrueNorth => 7546;

        public bool CanSecondWind(int healthpercent) => GluttonyCombo.RotationHarness.FakeGame.RoleActionReady;
        public bool CanBloodBath(int healthpercent) => GluttonyCombo.RotationHarness.FakeGame.RoleActionReady;
        public bool CanFeint() => GluttonyCombo.RotationHarness.FakeGame.RoleActionReady;
        public bool CanTrueNorth() => GluttonyCombo.RotationHarness.FakeGame.RoleActionReady;
        public bool CanLegSweep() => GluttonyCombo.RotationHarness.FakeGame.LegSweepReady;
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
    /// <summary>FAKE of ALL/Items.cs: only the two members the RPR opener lambdas compile against.</summary>
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public static implicit operator bool[](UserBoolArray o) => GluttonyCombo.RotationHarness.FakeGame.GetBoolArray(o.ConfigName);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Reaper files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.FakeGame"/>; defaults make a clean, everything-ready
    ///     level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.FakeGame.EnabledPresets.Contains(preset);

        // ---- player / status / target ----
        public static GluttonyCombo.RotationHarness.FakePlayer LocalPlayer { get; } = new();

        public static GluttonyCombo.RotationHarness.FakeTarget CurrentTarget { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.FakeGame.InCombat;

        public static bool PartyInCombat() => GluttonyCombo.RotationHarness.FakeGame.InCombat;

        public static bool IsInParty() => true;

        public static bool IsPlayerTargeted() => false;

        public static bool IsMoving() => GluttonyCombo.RotationHarness.FakeGame.InCombat; // moving only matters out of combat in the tested paths

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.FakeGame.TargetHPPercent;

        public static bool TargetNeedsPositionals() => GluttonyCombo.RotationHarness.FakeGame.TargetNeedsPositionals;

        public static bool OnTargetsFlank() => GluttonyCombo.RotationHarness.FakeGame.OnTargetsFlank;

        public static bool OnTargetsRear() => GluttonyCombo.RotationHarness.FakeGame.OnTargetsRear;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.FakeGame.InActionRange;

        public static int NumberOfEnemiesInRange(uint aoeSpell, object? target = null, bool checkIgnoredList = false) =>
            GluttonyCombo.RotationHarness.FakeGame.EnemyCount;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static uint GetMaxCharges(uint actionID) => GetCooldown(actionID).MaxCharges;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.FakeGame.AllTraitsKnown;

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.FakeGame.CanWeave;

        public static float ComboTimer => GluttonyCombo.RotationHarness.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

        public static bool GroupDamageIncoming(float time) => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => GluttonyCombo.RotationHarness.FakeGauges.Get<T>();
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
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the RPR opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.RPR_ST_Opener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>FAKE namespace holder: RPR.cs has `using GluttonyCombo.Extensions;`.</summary>
    public static class HarnessExtensionsNamespaceStub
    {
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE parts of the partial RPR class: the settings class (real one drags ImGui/localization) and the
    // positional-hint reporter (real one is an IPC side channel, not a rotation decision). Defaults are
    // mirrored one-for-one from RPR_Config.cs "Variables".
    internal partial class RPR
    {
        private static void ReportRPRPositionalHints()
        {
            // no-op: hint reporting is a UI/IPC side channel, out of the tested boundary
        }

        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                RPR_Positional = new("RPR_Positional"),
                RPR_Balance_Content = new("RPR_Balance_Content", 1),
                RPR_ST_ArcaneCircleHPOption = new("RPR_ST_ArcaneCircleHPOption", 25),
                RPR_ST_ArcaneCircleHPBossOption = new("RPR_ST_ArcaneCircleHPBossOption"),
                RPR_SoDRefreshRange = new("RPR_SoDRefreshRange", 6),
                RPR_SoDHPThreshold = new("RPR_SoDHPThreshold"),
                RPR_ManualTN = new("RPR_ManualTN"),
                RPR_ST_SecondWindHPThreshold = new("RPR_ST_SecondWindHPThreshold", 40),
                RPR_ST_BloodbathHPThreshold = new("RPR_ST_BloodbathHPThreshold", 30),
                RPR_WoDHPThreshold = new("RPR_WoDHPThreshold", 40),
                RPR_AoE_ArcaneCircleHPThreshold = new("RPR_AoE_ArcaneCircleHPThreshold", 40),
                RPR_AoE_SecondWindHPThreshold = new("RPR_AoE_SecondWindHPThreshold", 40),
                RPR_AoE_BloodbathHPThreshold = new("RPR_AoE_BloodbathHPThreshold", 30),
                RPR_SoDRefreshRangeBasicCombo = new("RPR_SoDRefreshRangeBasicCombo", 6),
                RPR_WoDRefreshRangeBasicCombo = new("RPR_WoDRefreshRangeBasicCombo", 6);

            public static CustomComboNS.Functions.UserBool
                RPR_Opener_Potion = new("RPR_Opener_Potion"),
                RPR_Opener_PrepullBlock = new("RPR_Opener_PrepullBlock", true),
                RPR_ST_TrueNorthDynamicHoldCharge = new("RPR_ST_TrueNorthDynamicHoldCharge"),
                RPR_ST_EnhancedHarpe = new("RPR_ST_EnhancedHarpe");

            public static CustomComboNS.Functions.UserBoolArray
                RPR_SoulsowOptions = new("RPR_SoulsowOptions");
        }
    }
}
