// Fakes.cs — the harness-owned stand-ins for everything the REAL Dragoon job files (DRG.cs, DRG_Helper.cs,
// compiled unchanged) reach outside themselves. Boundary per notebook "Offline Harness Approach" section 3:
//   REAL  : every decision line in DRG.cs / DRG_Helper.cs (Invoke branches, Life Surge placement, buff and
//           dive holds, openers' step tables, gauge reads, action IDs, Buffs/Traits tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, weave bookkeeping), the Preset enum values, the DRG Config
//           settings defaults, the role-action layer, openers' WrathOpener base, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.
// Adapted from tests/GluttonyCombo.RotationHarness (branch rot/harness-spike) per section 9. DRG-specific
// additions: BaseAnimationLock / HasWeaved / HasWeavedAction (Functions/Action.cs), IsMoving
// (Functions/Movement.cs), NumberOfEnemiesInRange / CurrentTarget (Functions/Target.cs), UserBoolArray
// (Functions/Config.cs), and FakeTarget standing in for the IBattleChara status extensions in
// Extensions/Extensions.cs.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Dragoon members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        DRG_ST_SimpleMode = 30001,
        DRG_AoE_SimpleMode = 30002,
        DRG_ST_AdvancedMode = 30003,
        DRG_AoE_AdvancedMode = 30004,
        DRG_ST_Opener = 30005,
        DRG_ST_Buffs = 30006,
        DRG_ST_BattleLitany = 30007,
        DRG_ST_LanceCharge = 30008,
        DRG_ST_LifeSurge = 30009,
        DRG_ST_Damage = 30010,
        DRG_ST_Mirage = 30011,
        DRG_ST_Geirskogul = 30012,
        DRG_ST_Wyrmwind = 30013,
        DRG_ST_Starcross = 30014,
        DRG_ST_RiseOfTheDragon = 30015,
        DRG_ST_Nastrond = 30016,
        DRG_ST_Feint = 30017,
        DRG_ST_ComboHeals = 30018,
        DRG_ST_StunInterupt = 30019,
        DRG_ST_HighJump = 30020,
        DRG_ST_DragonfireDive = 30021,
        DRG_ST_Stardiver = 30022,
        DRG_ST_RangedUptime = 30023,
        DRG_TrueNorthDynamic = 30024,
        DRG_AoE_Buffs = 30025,
        DRG_AoE_BattleLitany = 30026,
        DRG_AoE_LanceCharge = 30027,
        DRG_AoE_LifeSurge = 30028,
        DRG_AoE_Damage = 30029,
        DRG_AoE_Mirage = 30030,
        DRG_AoE_Geirskogul = 30031,
        DRG_AoE_Wyrmwind = 30032,
        DRG_AoE_Starcross = 30033,
        DRG_AoE_RiseOfTheDragon = 30034,
        DRG_AoE_Nastrond = 30035,
        DRG_AoE_ComboHeals = 30036,
        DRG_AoE_StunInterupt = 30037,
        DRG_AoE_HighJump = 30038,
        DRG_AoE_DragonfireDive = 30039,
        DRG_AoE_Stardiver = 30040,
        DRG_AoE_RangedUptime = 30041,
        DRG_AoE_Disembowel = 30042,
        DRG_HeavensThrust = 30043,
        DRG_ChaoticSpring = 30044,
        DRG_BurstCDFeature = 30045,
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

    /// <summary>FAKE of Roles/RoleActions.cs: only the melee members DRG touches.</summary>
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

    /// <summary>FAKE of the IMelee role surface DRG uses; consts mirrored from RoleActions.cs.</summary>
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
    /// <summary>FAKE of ALL/Items.cs: only the two members the DRG opener lambdas compile against.</summary>
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

    /// <summary>FAKE of UserBoolArray (hold-option arrays); Count 0 = no hold, matching a fresh config.</summary>
    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public int Count => GluttonyCombo.RotationHarness.FakeGame.GetArrayCount(ConfigName);

        public bool this[int index] => GluttonyCombo.RotationHarness.FakeGame.GetArrayBool(ConfigName, index);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Dragoon files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.FakeGame"/>; defaults make a clean, level-100,
    ///     everything-ready state. Members new versus the VPR spike: BaseAnimationLock, HasWeaved,
    ///     HasWeavedAction, IsMoving, NumberOfEnemiesInRange, CurrentTarget (DRG reads all of them).
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
        public const float BaseAnimationLock = 0.6f; // mirrored from Functions/Action.cs:25

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
        public static bool CanWeave(float estimatedWeaveTime = BaseAnimationLock, int? maxWeaves = null) => GluttonyCombo.RotationHarness.FakeGame.CanWeave;

        public static bool HasWeaved(int weaveAmount = 1) => GluttonyCombo.RotationHarness.FakeGame.WeaveCount >= weaveAmount; // Action.cs:312

        public static bool HasWeavedAction(uint actionId) => GluttonyCombo.RotationHarness.FakeGame.WeaveActions.Contains(actionId); // Action.cs:315

        public static float ComboTimer => GluttonyCombo.RotationHarness.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

        // ---- movement / target (DRG reads these; VPR did not) ----
        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.FakeGame.IsMoving; // Movement.cs:19

        public static GluttonyCombo.RotationHarness.FakeTarget? CurrentTarget => GluttonyCombo.RotationHarness.FakeGame.CurrentTarget; // Target.cs:42 (IBattleChara? in the real code)

        public static int NumberOfEnemiesInRange(uint actionID, GluttonyCombo.RotationHarness.FakeTarget? target = null) =>
            GluttonyCombo.RotationHarness.FakeGame.NumberOfEnemiesInRange; // Target.cs:422 (IGameObject? in the real code)

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
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the DRG opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.DRG_ST_Opener;

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
    /// <summary>
    ///     FAKE surface for the Extensions namespace DRG's usings require. The IBattleChara status
    ///     extensions (Status/HasStatus/CanApplyStatus, Extensions/Extensions.cs) are provided by
    ///     FakeTarget/FakePlayer members instead; Retarget is kept for namespace resolution.
    /// </summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE parts of the partial DRG class: the settings class (real one in DRG_Config.cs drags
    // ImGui/localization) and the positional-hint reporter (real one in DRG_PositionalHints.cs is an IPC
    // side channel, not a rotation decision). Defaults are mirrored one-for-one from DRG_Config.cs
    // "#region Variables".
    internal partial class DRG
    {
        private static void ReportDRGPositionalHints()
        {
            // no-op: hint reporting is a UI/IPC side channel, out of the tested boundary
        }

        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                DRG_SelectedOpener = new("DRG_SelectedOpener"),
                DRG_BalanceContent = new("DRG_BalanceContent", 1),
                DRG_ST_BattleLitanyHPOption = new("DRG_ST_BattleLitanyHPOption", 25),
                DRG_ST_BattleLitanyHPBossOption = new("DRG_ST_BattleLitanyHPBossOption"),
                DRG_ST_LanceChargeHPOption = new("DRG_ST_LanceChargeHPOption", 25),
                DRG_ST_LanceChargeHPBossOption = new("DRG_ST_LanceChargeHPBossOption"),
                DRG_ST_GeirskogulBossHPOption = new("DRG_ST_GeirskogulBossHPOption"),
                DRG_ST_GeirskogulBossAddsHPOption = new("DRG_ST_GeirskogulBossAddsHPOption", 10),
                DRG_ST_GeirskogulTrashHPOption = new("DRG_ST_GeirskogulTrashHPOption", 25),
                DRG_ST_DragonfireDiveHPOption = new("DRG_ST_DragonfireDiveHPOption", 25),
                DRG_ST_DragonfireDiveHPBossOption = new("DRG_ST_DragonfireDiveHPBossOption"),
                DRG_ManualTN = new("DRG_ManualTN"),
                DRG_ST_SecondWindHPThreshold = new("DRG_ST_SecondWindHPThreshold", 40),
                DRG_ST_BloodbathHPThreshold = new("DRG_ST_BloodbathHPThreshold", 30),
                DRG_AoE_BattleLitanyHPThreshold = new("DRG_AoE_BattleLitanyHPThreshold", 25),
                DRG_AoE_LanceChargeHPThreshold = new("DRG_AoE_LanceChargeHPThreshold", 25),
                DRG_AoE_GeirskogulHPThreshold = new("DRG_AoE_GeirskogulHPThreshold", 25),
                DRG_AoE_DragonfireDiveHPThreshold = new("DRG_AoE_DragonfireDiveHPThreshold", 25),
                DRG_AoE_SecondWindHPThreshold = new("DRG_AoE_SecondWindHPThreshold", 40),
                DRG_AoE_BloodbathHPThreshold = new("DRG_AoE_BloodbathHPThreshold", 30);

            public static CustomComboNS.Functions.UserBool
                DRG_Opener_Potion = new("DRG_Opener_Potion"),
                DRG_Opener_PrepullBlock = new("DRG_Opener_PrepullBlock", true),
                DRG_ST_DoubleMirage = new("DRG_ST_DoubleMirage"),
                DRG_ChaoticCombo = new("DRG_ChaoticCombo");

            public static CustomComboNS.Functions.UserBoolArray
                DRG_ST_JumpMovingOrInRanged = new("DRG_ST_JumpMovingOrInRanged"),
                DRG_ST_DragonfireDiveMovingOrInRanged = new("DRG_ST_DragonfireDiveMovingOrInRanged"),
                DRG_ST_StardiverMovingOrInRanged = new("DRG_ST_StardiverMovingOrInRanged"),
                DRG_AoE_JumpMovingOrInRanged = new("DRG_AoE_JumpMovingOrInRanged"),
                DRG_AoE_DragonfireDiveMovingOrInRanged = new("DRG_AoE_DragonfireDiveMovingOrInRanged"),
                DRG_AoE_StardiverMovingOrInRanged = new("DRG_AoE_StardiverMovingOrInRanged");
        }
    }
}
