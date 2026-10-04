// Fakes.cs — the harness-owned stand-ins for everything the REAL Viper job files (VPR.cs, VPR_Helper.cs,
// compiled unchanged from the pinned revision) reach outside themselves. The boundary is deliberate:
//   REAL  : every decision line in VPR.cs / VPR_Helper.cs (Invoke, weave/coil/reawaken helpers, gauge props,
//           action IDs, Buffs/Traits tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat), the Preset enum values, the VPR Config settings defaults,
//           the role-action layer, openers' WrathOpener base, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Collections.Generic;
using System.Reflection;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Viper members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        VPR_ST_SimpleMode = 30001,
        VPR_AoE_SimpleMode = 30002,
        VPR_ST_AdvancedMode = 30003,
        VPR_AoE_AdvancedMode = 30004,
        VPR_ST_BasicCombo = 30005,
        VPR_Retarget_Slither = 30006,
        VPR_VicewinderCoils = 30007,
        VPR_VicewinderCoils_oGCDs = 30008,
        VPR_VicepitDens = 30009,
        VPR_VicepitDens_oGCDs = 30010,
        VPR_UncoiledTwins = 30011,
        VPR_ReawakenLegacy = 30012,
        VPR_ReawakenLegacyWeaves = 30013,
        VPR_TwinTails = 30014,
        VPR_Legacies = 30015,
        VPR_SerpentsTail = 30016,
        VPR_VicewinderProtection = 30017,
        VPR_ST_Opener = 30018,
        VPR_ST_SerpentsTail = 30019,
        VPR_ST_LegacyWeaves = 30020,
        VPR_ST_UncoiledFuryCombo = 30021,
        VPR_ST_VicewinderWeaves = 30022,
        VPR_ST_SerpentsIre = 30023,
        VPR_ST_Feint = 30024,
        VPR_ST_ComboHeals = 30025,
        VPR_ST_StunInterupt = 30026,
        VPR_ST_VicewinderCombo = 30027,
        VPR_ST_Reawaken = 30028,
        VPR_ST_UncoiledFury = 30029,
        VPR_ST_RangedUptime = 30030,
        VPR_ST_GenerationCombo = 30031,
        VPR_TrueNorthDynamic = 30032,
        VPR_AoE_SerpentsTail = 30033,
        VPR_AoE_ReawakenCombo = 30034,
        VPR_AoE_UncoiledFuryCombo = 30035,
        VPR_AoE_VicepitWeaves = 30036,
        VPR_AoE_SerpentsIre = 30037,
        VPR_AoE_ComboHeals = 30038,
        VPR_AoE_StunInterupt = 30039,
        VPR_AoE_VicepitCombo = 30040,
        VPR_AoE_Reawaken = 30041,
        VPR_AoE_UncoiledFury = 30042,
        VPR_AoE_Vicepit = 30043,
        VPR_ST_Vicewinder = 30044,
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

    /// <summary>FAKE of Roles/RoleActions.cs: only the melee members VPR touches.</summary>
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

    /// <summary>FAKE of the IMelee role surface VPR uses; consts mirrored from RoleActions.cs.</summary>
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
    /// <summary>FAKE of ALL/Items.cs: only the two members the VPR opener lambdas compile against.</summary>
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

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Viper files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.FakeGame"/>; defaults make a clean, weaving-blocked,
    ///     everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.FakeGame.EnabledPresets.Contains(preset);

        // ---- player / status ----
        public static GluttonyCombo.RotationHarness.FakePlayer LocalPlayer { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.FakeGame.InCombat;

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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the three VPR names, as plain objects.</summary>
    public static class SimpleTarget
    {
        public static object? UIMouseOverTarget => GluttonyCombo.RotationHarness.FakeGame.UiMouseOver;

        public static object? ModelMouseOverTarget => null;

        public static object? HardTarget => GluttonyCombo.RotationHarness.FakeGame.HardTarget;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the VPR opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.VPR_ST_Opener;

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
    /// <summary>FAKE of the UIntExtensions.Retarget surface VPR_Retarget_Slither compiles against.</summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE parts of the partial VPR class: the settings class (real one drags ImGui/localization) and the
    // positional-hint reporter (real one is an IPC side channel, not a rotation decision). Defaults are
    // mirrored one-for-one from VPR_Config.cs lines 184-218.
    internal partial class VPR
    {
        private static void ReportVPRPositionalHints(bool vicewinderBuffPrio)
        {
            // no-op: hint reporting is a UI/IPC side channel, out of the tested boundary
        }

        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                VPR_Balance_Content = new("VPR_Balance_Content", 1),
                VPR_ST_UncoiledFuryHoldCharges = new("VPR_ST_UncoiledFuryHoldCharges", 1),
                VPR_ST_UncoiledFuryAlwaysUse = new("VPR_ST_UncoiledFuryAlwaysUse", 5),
                VPR_ST_ReawakenBossHPOption = new("VPR_ST_ReawakenBossHPOption"),
                VPR_ST_ReawakenBossAddsHPOption = new("VPR_ST_ReawakenBossAddsHPOption", 10),
                VPR_ST_ReawakenTrashHPOption = new("VPR_ST_ReawakenTrashHPOption", 25),
                VPR_ST_ReAwakenAlwaysUse = new("VPR_ST_ReAwakenAlwaysUse", 5),
                VPR_ST_SerpentsIreHPOption = new("VPR_ST_SerpentsIreHPOption", 25),
                VPR_ST_SerpentsIreHPBossOption = new("VPR_ST_SerpentsIreHPBossOption"),
                VPR_ManualTN = new("VPR_ManualTN"),
                VPR_ST_SecondWindHPThreshold = new("VPR_ST_SecondWindHPThreshold", 40),
                VPR_ST_BloodbathHPThreshold = new("VPR_ST_BloodbathHPThreshold", 30),
                VPR_AoE_SerpentsIreHPThreshold = new("VPR_AoE_SerpentsIreHPThreshold", 25),
                VPR_AoE_UncoiledFuryAlwaysUse = new("VPR_AoE_UncoiledFuryAlwaysUse", 5),
                VPR_AoE_UncoiledFuryHoldCharges = new("VPR_AoE_UncoiledFuryHoldCharges"),
                VPR_AoE_VicepitRangeCheck = new("VPR_AoE_VicepitRangeCheck"),
                VPR_AoE_VicepitComboRangeCheck = new("VPR_AoE_VicepitComboRangeCheck"),
                VPR_AoE_ReawakenHPThreshold = new("VPR_AoE_ReawakenHPThreshold", 25),
                VPR_AoE_ReawakenRangecheck = new("VPR_AoE_ReawakenRangecheck"),
                VPR_AoE_SecondWindHPThreshold = new("VPR_AoE_SecondWindHPThreshold", 40),
                VPR_AoE_BloodbathHPThreshold = new("VPR_AoE_BloodbathHPThreshold", 30),
                VPR_ReawakenLegacyButton = new("VPR_ReawakenLegacyButton");

            public static CustomComboNS.Functions.UserBool
                VPR_Opener_Potion = new("VPR_Opener_Potion"),
                VPR_Opener_PrepullBlock = new("VPR_Opener_PrepullBlock", true),
                VPR_Opener_ExcludeUF = new("VPR_Opener_ExcludeUF"),
                VPR_TrueNorthVicewinder = new("VPR_TrueNorthVicewinder"),
                VPR_Slither_FieldMouseover = new("VPR_Slither_FieldMouseover"),
                VPR_ST_TrueNorthDynamicHoldCharge = new("VPR_ST_TrueNorthDynamicHoldCharge"),
                VPR_VicewinderBuffPrio = new("VPR_VicewinderBuffPrio");
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>Namespace stub: VPR.cs has `using GluttonyCombo.Core;`; its real members are faked elsewhere.</summary>
    internal static class HarnessCoreNamespaceStub;
}
