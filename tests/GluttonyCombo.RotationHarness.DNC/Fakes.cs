// Fakes.cs — the harness-owned stand-ins for everything the REAL Dancer job files (DNC.cs,
// DNC_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary
// is deliberate (notebook "Offline Harness Approach" section 3):
//   REAL  : every decision line in DNC.cs / DNC_Helper.cs (Invoke branches, dance-fill/finish logic,
//           the Saber Dance tail blocks, gauge props, action IDs, Buffs tables, the dance-partner
//           resolver, the opener tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, enemy counts, last-action bookkeeping), the Preset enum
//           values, the DNC Config settings defaults, the physical-ranged role layer, the retargeting
//           layer, openers' WrathOpener base, the plugin/IPC and Service side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.
// Nothing in the dance-partner machinery RUNS in the harness cases (it is out-of-combat only); it
// only has to compile, so its object surface resolves to fixed null/false answers.

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Dancer members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision logic —
    ///     presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        // top-level modes / features
        DNC_ST_AdvancedMode = 42001,
        DNC_ST_SimpleMode = 42002,
        DNC_AoE_AdvancedMode = 42003,
        DNC_AoE_SimpleMode = 42004,
        DNC_ST_BasicCombo = 42005,
        DNC_ST_MultiButton = 42006,
        DNC_AoE_MultiButton = 42007,
        DNC_DesirablePartner = 42008,
        DNC_CustomDanceSteps = 42009,
        DNC_DanceFeatures = 42010,
        DNC_FlourishingFanDances = 42011,
        DNC_FanDanceCombos = 42012,
        DNC_Procc_Bladeshower = 42013,
        DNC_Procc_Windmill = 42014,
        DNC_ST_BlockFinishes = 42015,
        DNC_ST_BalanceOpener = 42016,
        DNC_StandardStepCombo = 42017,
        DNC_StandardStep_LastDance = 42018,
        DNC_TechnicalStepCombo = 42019,
        DNC_TechnicalStep_Devilment = 42020,

        // ST Advanced children
        DNC_ST_Adv_SS = 42021,
        DNC_ST_Adv_SS_Prepull = 42022,
        DNC_ST_Adv_TS = 42023,
        DNC_ST_Adv_FM = 42024,
        DNC_ST_Adv_Partner = 42025,
        DNC_ST_Adv_PartnerAuto = 42026,
        DNC_ST_Adv_AutoPartner = 42027,
        DNC_ST_Adv_Devilment = 42028,
        DNC_ST_Adv_Flourish = 42029,
        DNC_ST_Adv_Feathers = 42030,
        DNC_ST_Adv_FanProccs = 42031,
        DNC_ST_Adv_FanProcc3 = 42032,
        DNC_ST_Adv_FanProcc4 = 42033,
        DNC_ST_Adv_Tillana = 42034,
        DNC_ST_Adv_SaberDance = 42035,
        DNC_ST_Adv_LD = 42036,
        DNC_ST_Adv_DawnDance = 42037,
        DNC_ST_Adv_Improvisation = 42038,
        DNC_ST_Adv_Peloton = 42039,
        DNC_ST_Adv_ShieldSamba = 42040,
        DNC_ST_Adv_PanicHeals = 42041,
        DNC_ST_Adv_Interrupt = 42042,
        DNC_ST_EspritOvercap = 42043,
        DNC_ST_FanDance34 = 42044,
        DNC_ST_FanDanceOvercap = 42045,

        // AoE Advanced children
        DNC_AoE_Adv_SS = 42046,
        DNC_AoE_Adv_TS = 42047,
        DNC_AoE_Adv_FM = 42048,
        DNC_AoE_Adv_Partner = 42049,
        DNC_AoE_Adv_Devilment = 42050,
        DNC_AoE_Adv_Flourish = 42051,
        DNC_AoE_Adv_Feathers = 42052,
        DNC_AoE_Adv_FanProccs = 42053,
        DNC_AoE_Adv_FanProcc3 = 42054,
        DNC_AoE_Adv_FanProcc4 = 42055,
        DNC_AoE_Adv_Tillana = 42056,
        DNC_AoE_Adv_SaberDance = 42057,
        DNC_AoE_Adv_LD = 42058,
        DNC_AoE_Adv_DawnDance = 42059,
        DNC_AoE_Adv_Improvisation = 42060,
        DNC_AoE_Adv_PanicHeals = 42061,
        DNC_AoE_Adv_Interrupt = 42062,
        DNC_AoE_EspritOvercap = 42063,
        DNC_AoE_FanDance34 = 42064,
        DNC_AoE_FanDanceOvercap = 42065,

        // smaller-feature children
        DNC_CustomDanceSteps_Conflicts = 42066,
        DNC_Flourishing_FD3 = 42067,
        DNC_FanDance_1to3_Combo = 42068,
        DNC_FanDance_1to4_Combo = 42069,
        DNC_FanDance_2to3_Combo = 42070,
        DNC_FanDance_2to4_Combo = 42071,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.Enums
{
    /// <summary>FAKE slice of Combos/PvE/Enums/OpenerState.cs: mirrored verbatim (DNC.cs:1480 reads it).</summary>
    public enum OpenerState
    {
        OpenerNotReady,
        OpenerReady,
        InOpener,
        OpenerFinished,
        FailedOpener,
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

    /// <summary>FAKE slices of other jobs' Buffs tables: only the two consts DNC's panic-heal checks name.</summary>
    internal partial class BRD
    {
        public static class Buffs
        {
            public const ushort Troubadour = 1934; // mirrored from BRD_Helper.cs
        }
    }

    internal partial class MCH
    {
        public static class Buffs
        {
            public const ushort Tactician = 1951; // mirrored from MCH_Helper.cs
        }
    }

    /// <summary>FAKE of ALL/JobClasses.cs PhysicalRanged base (the real one routes through Roles/RoleImplementation).</summary>
    internal class PhysicalRanged
    {
        protected PhysicalRanged() { }

        public static FakePhysRangedRole Role { get; } = new();
    }

    /// <summary>
    ///     FAKE of the IPhysicalRanged role surface DNC uses; consts mirrored from Roles/RoleActions.cs.
    ///     CanHeadGraze honours the preset so a disabled Interrupt child declines (the real one checks
    ///     the enabled set the same way).
    /// </summary>
    internal class FakePhysRangedRole
    {
        public uint SecondWind => 7541; // Physical.SecondWind (RoleActions.cs:81)
        public uint HeadGraze => 7551;  // PhysRanged.HeadGraze (RoleActions.cs:107)

        public bool CanSecondWind(int healthpercent) => GluttonyCombo.RotationHarness.DNC.FakeGame.RoleActionReady;

        public bool CanHeadGraze(Combos.Preset preset, CustomComboFunctions.WeaveTypes weave = CustomComboFunctions.WeaveTypes.None) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.RoleActionReady &&
            GluttonyCombo.RotationHarness.DNC.FakeGame.EnabledPresets.Contains(preset);

        public bool CanHeadGraze(bool simpleMode, CustomComboFunctions.WeaveTypes weave = CustomComboFunctions.WeaveTypes.None) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.RoleActionReady && simpleMode;
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
    /// <summary>FAKE of ALL/Items.cs: only the members the DNC opener lambdas compile against.</summary>
    internal class Items
    {
        public enum PotionType { Strength, Dex, Vit, Int, Mind }

        public static uint GetStrongestPotionRow(PotionType type, bool inInventory = true) => 33;

        public static uint UseItem(uint item) => item;
    }
}

// ======================================================================================
namespace GluttonyCombo
{
    /// <summary>
    ///     FAKE of the plugin class P: only the IPC surface InAutoMode reads. Auto-rotation is off and
    ///     the combo-state lookup returns null; the dance-partner path that calls it is out-of-combat
    ///     and never reached by the harness cases.
    /// </summary>
    internal static class P
    {
        public static FakeIpcProvider IPC { get; } = new();
    }

    internal sealed class FakeIpcProvider
    {
        public enum ComboStateKeys { None = 0 }

        public bool GetAutoRotationState() => false;

        public Dictionary<ComboStateKeys, bool>? GetComboState(string comboInternalName) => null;
    }
}

// ======================================================================================
namespace GluttonyCombo.Services
{
    /// <summary>FAKE of Services/Service.cs: only the Configuration slice DNC_Helper's custom dance
    ///     steps read (the shipped default array, mirrored from Core/Configuration.cs:470).</summary>
    internal class Service
    {
        public static GluttonyCombo.Core.FakeConfiguration Configuration { get; } = new();
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>FAKE of Core/ActionRetargeting.cs: only the attribute DNC_Helper's DancePartnerResolver carries.</summary>
    public class ActionRetargeting : IDisposable
    {
        public void Dispose() { }

        public sealed class TargetResolverAttribute : Attribute { }
    }

    /// <summary>FAKE slice of Core/Configuration.cs: only the member DNC_Helper reads.</summary>
    internal class FakeConfiguration
    {
        public uint[] DancerDanceCompatActionIDs { get; set; } = [0, 0, 0, 0];
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.DNC.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.DNC.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.DNC.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of Config.cs UserBoolArray (Count/Any/All/indexer, defaults mirrored).</summary>
    internal class UserBoolArray(string configName, bool[]? defaults = null) : UserData(configName)
    {
        public bool[] Default = defaults ?? [];

        private bool[] Arr => GluttonyCombo.RotationHarness.DNC.FakeGame.GetBoolArray(ConfigName, Default);

        public int Count => Arr.Length;

        public bool Any(Func<bool, bool> func) => Arr.Any(func);

        public bool All(Func<bool, bool> func) => Arr.All(func);

        public static implicit operator bool[](UserBoolArray o) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.GetBoolArray(o.ConfigName, o.Default);

        public bool this[int index] => index < Arr.Length && Arr[index];

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of the WrathPartyMember shape DNC_Helper's partner logic compiles against.</summary>
    public class WrathPartyMember
    {
        public ulong GameObjectId;
        public IBattleChara? BattleChara;
        public IGameObject? GameObject;
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Dancer files read. Every member routes
    ///     into <see cref="GluttonyCombo.RotationHarness.DNC.FakeGame"/>; defaults make a clean,
    ///     weave-blocked, everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.DNC.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status / target ----
        public static GluttonyCombo.RotationHarness.DNC.FakePlayer LocalPlayer { get; } = new();

        public static GluttonyCombo.RotationHarness.DNC.FakeTarget CurrentTarget { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.DNC.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.DNC.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.DNC.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.DNC.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.DNC.FakeGame.TargetHPPercent;

        public static float PlayerHealthPercentageHp() => GluttonyCombo.RotationHarness.DNC.FakeGame.PlayerHealthPercent;

        public static bool TargetNeedsPositionals() => GluttonyCombo.RotationHarness.DNC.FakeGame.TargetNeedsPositionals;

        public static bool OnTargetsFlank() => GluttonyCombo.RotationHarness.DNC.FakeGame.OnTargetsFlank;

        public static bool OnTargetsRear() => GluttonyCombo.RotationHarness.DNC.FakeGame.OnTargetsRear;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.DNC.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.DNC.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.DNC.FakeGame.InActionRange;

        // ---- enemy / ally geometry (DNC's EnemyIn15Yalms / AlliesIn8Yalms read these) ----
        public static int NumberOfEnemiesInRange
            (uint aoeSpell, GluttonyCombo.RotationHarness.DNC.FakeTarget? target = null, bool checkIgnoredList = false) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.EnemiesInRange;

        public static int NumberOfAlliesInRange
            (uint aoeSpell, GluttonyCombo.RotationHarness.DNC.FakeTarget? target = null) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.AlliesInRange;

        public static List<WrathPartyMember> GetPartyMembers(bool allowCache = true) => [];

        public static bool IsInParty(int partySize = 2) => GluttonyCombo.RotationHarness.DNC.FakeGame.PartySize >= partySize;

        public static bool IsOccupied() => false;

        public static bool HasCompanionPresent() => false;

        public static bool IsInRange(IGameObject? optionalTarget = null, float range = 25f) => true;

        // ---- movement ----
        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.DNC.FakeGame.IsMoving;

        public static TimeSpan TimeStoodStill => GluttonyCombo.RotationHarness.DNC.FakeGame.TimeStoodStill;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.DNC.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.DNC.FakeGame.Cooldown(actionID);

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

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.DNC.FakeGame.AllTraitsKnown;

        // ---- weave / combo state / last actions ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.DNC.FakeGame.CanWeave;

        public static bool HasWeaved(int weaveAmount = 1) => GluttonyCombo.RotationHarness.DNC.FakeGame.HasWeavedCount >= weaveAmount;

        public static float ComboTimer => GluttonyCombo.RotationHarness.DNC.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.DNC.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.DNC.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.DNC.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool WasLastAction(uint actionId) => GluttonyCombo.RotationHarness.DNC.FakeGame.LastActionId == actionId;

        public static bool WasLastWeaponskill(uint actionId) => GluttonyCombo.RotationHarness.DNC.FakeGame.LastWeaponskillId == actionId;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.DNC.FakeGame.GroupDamageIncoming;

        // FAKE of Timer.cs CombatEngageDuration: method, not property. Two minutes in = past every
        // opener window, so the `< 20 && TechnicalFinish` branches decline.
        public static TimeSpan CombatEngageDuration() => TimeSpan.FromMinutes(2);

        // FAKE of Action.cs CanDelayedWeave (defaults mirror the real signature shape).
        public static bool CanDelayedWeave(float weaveStart = 1.25f, float weaveEnd = 0.6f, int? maxWeaves = null) => false;

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase =>
            GluttonyCombo.RotationHarness.DNC.FakeGauges.Get<T>();

        /// <summary>FAKE of the weave-type enum (real home: CustomCombo/Functions/Action.cs, nested here too
        /// so the job files resolve it through the CustomCombo base exactly as in the plugin).</summary>
        public enum WeaveTypes
        {
            None,
            Weave,
            DelayWeave,
        }
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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the members the DNC files compile against.</summary>
    internal static class SimpleTarget
    {
        public static IGameObject? AnySelfishDPS => null;

        public static IGameObject? AnyMeleeDPS => null;

        public static IGameObject? AnyDPS => null;

        public static IBattleChara? FocusTarget => null;

        public static IGameObject? Chocobo => null;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the DNC opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public static WrathOpener? CurrentOpener { get; set; }

        public virtual Combos.PvE.Enums.OpenerState CurrentState => Combos.PvE.Enums.OpenerState.OpenerNotReady;

        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.DNC_ST_BalanceOpener;

        internal virtual Functions.UserData? ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.DNC.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.DNC.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.DNC.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of the UIntExtensions Retarget surface the DNC files compile against (the real ones
    ///     register with the live plugin's ActionRetargeting singleton; offline they pass the id through).
    /// </summary>
    internal static class UIntExtensions
    {
        internal static uint Retarget(this uint action, IGameObject? target) => action;

        internal static uint Retarget(this uint action, Func<IGameObject?> target) => action;

        internal static uint Retarget(this uint action, uint replaced, IGameObject? target) => action;

        internal static uint Retarget(this uint action, uint replaced, Func<IGameObject?> target) => action;

        internal static uint Retarget(this uint action, uint[] replaced, IGameObject? target) => action;

        internal static uint Retarget(this uint action, uint[] replaced, Func<IGameObject?> target) => action;
    }

    /// <summary>
    ///     FAKE of Extensions/GameObjectExtensions.cs: only the members the DNC files compile against.
    ///     Target-side statuses and debuff checks are OUT of the tested boundary (fixed answers).
    /// </summary>
    public static class GameObjectExtensions
    {
        extension(IGameObject? obj)
        {
            public bool HasDamageDown => false;

            public bool HasRezWeakness(bool checkBrink = true) => false;

            public bool IsNotThePlayer() => true;

            public bool IsInParty() => GluttonyCombo.RotationHarness.DNC.FakeGame.PartySize > 1;

            public bool IsWithinRange(float range = 25) => true;

            public bool CanUseOn(uint actionId) => true;

            public bool HasStatus(uint id, bool anyOwner = false) => false;

            public IStatus? Status(uint id, bool anyOwner = false) => null;

            public bool CanApplyStatus(ushort statusId) => GluttonyCombo.RotationHarness.DNC.FakeGame.TargetCanApplyStatus;

            public bool IsFriendly() => true;

            public bool IsHostile() => false;

            public bool IsBoss() => GluttonyCombo.RotationHarness.DNC.FakeGame.TargetIsBoss;
        }
    }

    /// <summary>FAKE of the id->object lookup half of Extensions/GameObjectExtensions.cs (offline: none).</summary>
    public static class GameObjectLookupExtensions
    {
        public static IGameObject? GetObject(this ulong id) => null;

        public static IGameObject? GetObject(this ulong? id) => null;
    }

    /// <summary>FAKE of Extensions/StatusExtensions.cs: only RemainingTimeOrZero.</summary>
    public static class StatusExtensions
    {
        public static float RemainingTimeOrZero(this IStatus? status, bool checkAnimationLock = true) =>
            status?.RemainingTime ?? 0f;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial DNC class: the settings class (the real DNC_Config.cs drags ImGui and
    // localization into the compile; the real Draw code is UI, not a rotation decision). Defaults are
    // mirrored one-for-one from DNC_Config.cs #region Options, and the config enums verbatim from its
    // #region Constants.
    internal partial class DNC
    {
        internal static class Config
        {
            public enum Openers
            {
                FifteenSecond,
                SevenSecond,
                ThirtySecondTech,
                SevenPlusSecondTech,
                SevenSecondTech,
            }

            public enum IncludeStep
            {
                No,
                Yes,
            }

            public enum TillanaUsageManner
            {
                Normally = 0,
                NormallyPreventDrops = 2,
                FavorOverEsprit = 1,
            }

            public enum AntiDrift
            {
                None,
                TripleWeave,
                Hold,
                Both,
            }

            public enum PartnerShowAction
            {
                Default,
                ClosedPosition,
                SavageBlade,
            }

            public static CustomComboNS.Functions.UserBoolArray
                DNC_ST_OpenerDifficulty = new("DNC_ST_OpenerDifficulty", [false, true]);

            public static CustomComboNS.Functions.UserInt
                DNC_ST_OpenerSelection = new("DNC_ST_OpenerSelection", (int)Openers.FifteenSecond),
                DNCEspritThreshold_ST = new("DNCEspritThreshold_ST", 50),
                DNC_ST_Adv_SSBurstPercent = new("DNC_ST_Adv_SSBurstPercent", 0),
                DNC_ST_ADV_SS_IncludeSS = new("DNC_ST_ADV_SS_IncludeSS", (int)IncludeStep.Yes),
                DNC_ST_ADV_AntiDrift = new("DNC_ST_ADV_AntiDrift", (int)AntiDrift.TripleWeave),
                DNC_ST_ADV_TS_IncludeTS = new("DNC_ST_ADV_TS_IncludeTS", (int)IncludeStep.Yes),
                DNC_ST_Adv_TSBurstPercent = new("DNC_ST_Adv_TSBurstPercent", 0),
                DNC_ST_Adv_FeatherBurstPercent = new("DNC_ST_Adv_FeatherBurstPercent", 0),
                DNC_ST_ADV_TillanaUse = new("DNC_ST_ADV_TillanaUse", (int)TillanaUsageManner.Normally),
                DNC_ST_Adv_SaberThreshold = new("DNC_ST_Adv_SaberThreshold", 50),
                DNC_ST_Adv_PanicHealWaltzPercent = new("DNC_ST_Adv_PanicHealWaltzPercent", 30),
                DNC_ST_Adv_PanicHealWindPercent = new("DNC_ST_Adv_PanicHealWindPercent", 20),
                DNCEspritThreshold_AoE = new("DNCEspritThreshold_AoE", 50),
                DNC_AoE_Adv_SSBurstPercent = new("DNC_AoE_Adv_SSBurstPercent", 40),
                DNC_AoE_Adv_SS_IncludeSS = new("DNC_AoE_Adv_SS_IncludeSS", (int)IncludeStep.Yes),
                DNC_AoE_Adv_TSBurstPercent = new("DNC_AoE_Adv_TSBurstPercent", 40),
                DNC_AoE_Adv_TS_IncludeTS = new("DNC_AoE_Adv_TS_IncludeTS", (int)IncludeStep.Yes),
                DNC_AoE_Adv_SaberThreshold = new("DNC_AoE_Adv_SaberThreshold", 50),
                DNC_AoE_Adv_PanicHealWaltzPercent = new("DNC_AoE_Adv_PanicHealWaltzPercent", 30),
                DNC_AoE_Adv_PanicHealWindPercent = new("DNC_AoE_Adv_PanicHealWindPercent", 20),
                DNC_Partner_ActionToShow = new("DNC_Partner_ActionToShow", (int)PartnerShowAction.Default);

            public static CustomComboNS.Functions.UserBool
                DNC_Opener_Potion = new("DNC_Opener_Potion"),
                DNC_Opener_PrepullBlock = new("DNC_Opener_PrepullBlock", true),
                DNC_ST_OpenerOption_Peloton = new("DNC_ST_OpenerOption_Peloton", true),
                DNC_Partner_FocusOverride = new("DNC_Partner_FocusOverride", false);
        }
    }
}
