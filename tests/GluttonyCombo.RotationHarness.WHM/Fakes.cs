// Fakes.cs — the harness-owned stand-ins for everything the REAL White Mage job files (WHM.cs,
// WHM_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary:
//   REAL  : every decision line in WHM.cs / WHM_Helper.cs (Invoke branches, weave gates, the Dia
//           refresh decision NeedsDoT, opener plumbing, action/buff tables, heal priority logic).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, movement, GCD count), the Preset enum values, the WHM
//           Config settings defaults, the healer role layer, the retargeting layer, openers'
//           WrathOpener base, content/IPC side channels, and Combos/PvE/ALL/Bursting.cs (the shared
//           party-burst detector the WHM-1 option reads).
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using ECommons.GameFunctions;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the White Mage members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision logic —
    ///     presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        WHM_ST_Simple_DPS = 41001,
        WHM_AoE_Simple_DPS = 41002,
        WHM_SimpleSTHeals = 41003,
        WHM_Simple_AoEHeals = 41004,
        WHM_STHeals = 41005,
        WHM_AoEHeals = 41006,
        WHM_ST_MainCombo = 41007,
        WHM_ST_MainCombo_Opener = 41008,
        WHM_ST_MainCombo_Move_DoT = 41009,
        WHM_ST_MainCombo_DoT = 41010,
        WHM_ST_MainCombo_Assize = 41011,
        WHM_ST_MainCombo_GlareIV = 41012,
        WHM_ST_MainCombo_Misery = 41013,
        WHM_ST_MainCombo_LilyOvercap = 41014,
        WHM_ST_MainCombo_PresenceOfMind = 41015,
        WHM_ST_MainCombo_Lucid = 41016,
        WHM_AoE_DPS = 41017,
        WHM_AoE_DPS_SwiftHoly = 41018,
        WHM_AoE_DPS_Assize = 41019,
        WHM_AoE_DPS_PresenceOfMind = 41020,
        WHM_AoE_DPS_GlareIV = 41021,
        WHM_AoE_DPS_Misery = 41022,
        WHM_AoE_DPS_LilyOvercap = 41023,
        WHM_AoE_MainCombo_DoT = 41024,
        WHM_STHeals_Esuna = 41025,
        WHM_STHeals_Lucid = 41026,
        WHM_STHeals_Benediction = 41027,
        WHM_STHeals_Tetragrammaton = 41028,
        WHM_STHeals_Benison = 41029,
        WHM_STHeals_Aquaveil = 41030,
        WHM_STHeals_Solace = 41031,
        WHM_STHeals_Regen = 41032,
        WHM_STHeals_Temperance = 41033,
        WHM_STHeals_Asylum = 41034,
        WHM_STHeals_LiturgyOfTheBell = 41035,
        WHM_STHeals_ThinAir = 41036,
        WHM_AoEHeals_Lucid = 41037,
        WHM_AoEHeals_Medica2 = 41038,
        WHM_AoEHeals_Cure3 = 41039,
        WHM_AoEHeals_Plenary = 41040,
        WHM_AoEHeals_Temperance = 41041,
        WHM_AoEHeals_Asylum = 41042,
        WHM_AoEHeals_LiturgyOfTheBell = 41043,
        WHM_AoEHeals_Rapture = 41044,
        WHM_AoEHeals_Assize = 41045,
        WHM_AoEHeals_DivineCaress = 41046,
        WHM_AoEHeals_ThinAir = 41047,
        WHM_SolaceMisery = 41048,
        WHM_RaptureMisery = 41049,
        WHM_CureSync = 41050,
        WHM_Raise = 41051,
        WHM_ThinAirRaise = 41052,
        WHM_Raise_Retarget = 41053,
        WHM_Mit_ST = 41054,
        WHM_Mit_AoE = 41055,
        WHM_Retargets = 41056,
        WHM_Re_Cure = 41057,
        WHM_Re_Cure2 = 41058,
        WHM_Re_Solace = 41059,
        WHM_Re_Aquaveil = 41060,
        WHM_Re_Asylum = 41061,
        WHM_Re_LiturgyOfTheBell = 41062,
        WHM_Re_Cure3 = 41063,
        WHM_Re_Benediction = 41064,
        WHM_Re_Tetragrammaton = 41065,
        WHM_Re_Regen = 41066,
        WHM_Re_DivineBenison = 41067,
        WHM_Raidwide_Asylum = 41068,
        WHM_Raidwide_Temperance = 41069,
        WHM_Raidwide_LiturgyOfTheBell = 41070,
        WHM_Raidwide_PlenaryIndulgence = 41071,
        WHM_Raidwide_Medica = 41072,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE slice of the real partial class All (ALL.cs): the native action ids WHM uses.</summary>
    internal partial class All
    {
        public const uint SingleTargetDPS = 1_000_000; // mirrored from ALL.cs
        public const uint AoEDPS = 1_000_001;          // mirrored from ALL.cs
        public const uint Cease = 1_000_004;           // mirrored from ALL.cs
    }

    /// <summary>
    ///     FAKE of Combos/PvE/ALL/Bursting.cs (namespace + member mirrored). The real detector polls
    ///     party buffs through EzThrottler and HasBuff; the only member the WHM code reads is
    ///     <c>PartyIsBursting</c>, which here is harness state set per case. Whether the real detector
    ///     fires is OUT of the tested boundary.
    /// </summary>
    public class Bursting
    {
        public static bool PartyIsBursting => GluttonyCombo.RotationHarness.FakeGame.PartyIsBursting;
    }

    /// <summary>FAKE of ALL/JobClasses.cs Healer base: only the Role surface WHM touches.</summary>
    internal class Healer
    {
        protected Healer() { }

        public static FakeHealerRole Role { get; } = new();
    }

    /// <summary>FAKE of the IHealer role surface WHM uses; consts mirrored from Roles/RoleActions.cs.</summary>
    internal class FakeHealerRole
    {
        public uint LucidDreaming => 7562;
        public uint Swiftcast => 7561;
        public uint Esuna => 7568;

        public FakeHealerRoleBuffs Buffs { get; } = new();

        public bool CanLucidDream(int mpThreshold, bool weave = true) =>
            GluttonyCombo.RotationHarness.FakeGame.CanLucidDream;
    }

    /// <summary>FAKE of the IHealerBuffs surface WHM_Raise reads (Role.Buffs.Swiftcast).</summary>
    internal class FakeHealerRoleBuffs
    {
        public ushort Swiftcast => 1503; // mirrored from the role buff table; value unused offline
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

    /// <summary>FAKE of Combos/PvE/ALL/HealRetargeting.cs: retargeting off, ids pass through.</summary>
    public static class HealRetargeting
    {
        internal static bool RetargetSettingOn => false;

        public static uint RetargetIfEnabled(this uint actionID) => actionID;

        public static uint RetargetIfEnabled(this uint actionID, uint replaced) => actionID;

        public static uint RetargetIfEnabled(this uint actionID, uint[] replaced) => actionID;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.ALL
{
    /// <summary>FAKE of ALL/Items.cs: only the members the WHM opener lambdas compile against.</summary>
    internal class Items
    {
        public enum PotionType { Strength, Dex, Vit, Int, Mind }

        public static uint GetStrongestPotionRow(PotionType type, bool inInventory = true) => 33;

        public static uint UseItem(uint item) => item;
    }
}

// ======================================================================================
namespace GluttonyCombo.AutoRotation
{
    /// <summary>
    ///     FAKE of AutoRotation/AutoRotationController.cs: only the raidwide-mit bookkeeping
    ///     WHM_Helper's Raidwide* helpers read. Default true = raidwide mit "already used" => helpers
    ///     decline.
    /// </summary>
    internal static class AutoRotationController
    {
        public static bool RaidwideMitOnCooldown = true;

        public static void MarkRaidwideMitUsed() { }
    }
}

// ======================================================================================
namespace GluttonyCombo.Data
{
    /// <summary>
    ///     FAKE of Data/ActionWatching.cs: the GCD counter and the combat-action log the WHM files
    ///     read (NumberOfGcdsUsed, CombatActions for AssizeCount / WasLastAction).
    /// </summary>
    internal static class ActionWatching
    {
        public static int NumberOfGcdsUsed => GluttonyCombo.RotationHarness.FakeGame.NumberOfGcdsUsed;

        public static List<(uint ActionID, int ActionType)> CombatActions = [];
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

    /// <summary>
    ///     FAKE of Data/ContentCheck.cs: the difficulty gate the AoE heal helpers compile against.
    ///     Not exercised by the harness cases; declines like out-of-config content.
    /// </summary>
    internal static class ContentCheck
    {
        public enum ListSet { Halved, CasualVSHard, Cored, BossOnly }

        internal static bool IsInConfiguredContent(UserBoolArray configuredContent, ListSet configListSet) =>
            GluttonyCombo.RotationHarness.FakeGame.InConfiguredContent;
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

    /// <summary>FAKE of Config.cs UserIntArray (Count/IndexOf/Any/OrderBy/indexer, defaults mirrored).</summary>
    internal class UserIntArray(string configName, int[]? defaults = null) : UserData(configName)
    {
        public int[] Default = defaults ?? [];

        private int[] Arr => GluttonyCombo.RotationHarness.FakeGame.GetIntArray(ConfigName, Default);

        public int Count => Arr.Length;

        public int IndexOf(int item) => System.Array.IndexOf(Arr, item);

        public bool Any(Func<int, bool> func) => Arr.Any(func);

        public IEnumerable<int> OrderBy<TKey>(Func<int, TKey> keySelector) => Arr.OrderBy(keySelector);

        public static implicit operator int[](UserIntArray o) =>
            GluttonyCombo.RotationHarness.FakeGame.GetIntArray(o.ConfigName, o.Default);

        public int this[int index] => index < Arr.Length ? Arr[index] : 0;

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of Config.cs UserBoolArray (Count/Any/All/indexer, out-of-range reads false).</summary>
    internal class UserBoolArray(string configName, bool[]? defaults = null) : UserData(configName)
    {
        public bool[] Default = defaults ?? [];

        private bool[] Arr => GluttonyCombo.RotationHarness.FakeGame.GetBoolArray(ConfigName, Default);

        public int Count => Arr.Length;

        public bool Any(Func<bool, bool> func) => Arr.Any(func);

        public bool All(Func<bool, bool> func) => Arr.All(func);

        public static implicit operator bool[](UserBoolArray o) =>
            GluttonyCombo.RotationHarness.FakeGame.GetBoolArray(o.ConfigName, o.Default);

        public bool this[int index] => index < Arr.Length && Arr[index];

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the White Mage files read. Every member routes
    ///     into <see cref="GluttonyCombo.RotationHarness.FakeGame"/>; defaults make a clean, weaving-open,
    ///     standing-still, everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status ----
        public static GluttonyCombo.RotationHarness.FakePlayer LocalPlayer { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.FakeGame.TargetHPPercent;

        public static float GetTargetHPPercent(IGameObject? optionalTarget = null, bool includeShield = false, bool forceUsePending = false) =>
            GluttonyCombo.RotationHarness.FakeGame.TargetHPPercent;

        public static float GetTargetDistance(IGameObject? chara = null) => GluttonyCombo.RotationHarness.FakeGame.TargetDistance;

        public static int NumberOfAlliesInRange(uint aoeSpell, IGameObject? target = null) =>
            GluttonyCombo.RotationHarness.FakeGame.AlliesInRange;

        // ---- movement / party / combat-state ----
        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.FakeGame.IsMovingFlag;

        public static System.TimeSpan TimeStoodStill => System.TimeSpan.FromSeconds(GluttonyCombo.RotationHarness.FakeGame.TimeStoodStillSeconds);

        public static bool PartyInCombat() => GluttonyCombo.RotationHarness.FakeGame.PartyInCombatFlag;

        public static bool IsInParty(int partySize = 2) => GluttonyCombo.RotationHarness.FakeGame.IsInPartyFlag;

        public static List<object> GetPartyMembers(bool allowCache = true) => [];

        public static float GetPartyAvgHPPercent() => GluttonyCombo.RotationHarness.FakeGame.PartyAvgHP;

        public static float GetPartyBuffPercent(ushort buff) => 0f;

        public static bool HasOrExpectsOccultInstantCast => GluttonyCombo.RotationHarness.FakeGame.OccultInstantCast;

        public static bool HasOrExpectsOccultDualcast => GluttonyCombo.RotationHarness.FakeGame.OccultDualcast;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

        public static bool GroupDamageIncoming(float? maxTimeRemaining = null) => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

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

        public static bool WasLastAction(uint actionId)
        {
            var actions = Data.ActionWatching.CombatActions;
            return actions.Count > 0 && actions[actions.Count - 1].ActionID == actionId;
        }

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.FakeGame.CanWeave;

        public static float ComboTimer => GluttonyCombo.RotationHarness.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.FakeGame.ComboActionId;

        // NOTE: real CCF types this IBattleChara?; the WHM code only uses IGameObject surface
        // members through it, so the harness-declared type is IGameObject? (see Fakes.cs).
        public static IGameObject? CurrentTarget => GluttonyCombo.RotationHarness.FakeGame.HardTargetObject;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool JustUsedOn(uint actionID, IGameObject? target, float variance = 3f) =>
            target is not null && JustUsed(actionID, variance);

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase =>
            GluttonyCombo.RotationHarness.FakeGauges.Get<T>();

        /// <summary>FAKE of the nested JobRoles sheet-lookup (Misc.cs): no members, the party list is empty.</summary>
        public static class JobRoles
        {
            public static System.Collections.Frozen.FrozenSet<uint> Tank => System.Collections.Frozen.FrozenSet<uint>.Empty;

            public static System.Collections.Frozen.FrozenSet<uint> Healer => System.Collections.Frozen.FrozenSet<uint>.Empty;

            public static System.Collections.Frozen.FrozenSet<uint> Melee => System.Collections.Frozen.FrozenSet<uint>.Empty;

            public static System.Collections.Frozen.FrozenSet<uint> Ranged => System.Collections.Frozen.FrozenSet<uint>.Empty;
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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the members the WHM files compile against.</summary>
    internal static class SimpleTarget
    {
        public static IPlayerCharacter? Self => null;

        public static IGameObject? HardTarget => GluttonyCombo.RotationHarness.FakeGame.HardTargetObject;

        public static IGameObject? UIMouseOverTarget => GluttonyCombo.RotationHarness.FakeGame.UiMouseOver;

        public static IGameObject? ModelMouseOverTarget => null;

        public static IBattleChara? FocusTarget => null;

        public static IBattleChara? AnyEnemy => GluttonyCombo.RotationHarness.FakeGame.HardTargetObject as IBattleChara;

        public static IBattleChara? DottableEnemy
        (uint dotAction,
            ushort dotDebuff,
            int minHPPercent = 10,
            float reapplyThreshold = 1,
            int maxNumberOfEnemiesInRange = 3) => null;

        public static IBattleChara? DottableEnemy
        (uint dotAction,
            ushort dotDebuff,
            Func<IBattleChara?, int> minHPPercent,
            float reapplyThreshold = 1,
            int maxNumberOfEnemiesInRange = 3) => null;

        internal static class Stack
        {
            public static IGameObject? Allies => null;

            public static IGameObject? AllyToHeal => null;

            public static IGameObject? OneButtonHealLogic => null;

            public static IGameObject? AllyToEsuna => null;

            public static IGameObject? AllyToRaise => null;

            public static IBattleChara? MouseOver => null;
        }
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the WHM opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.WHM_ST_MainCombo_Opener;

        internal virtual Functions.UserData? ContentCheckConfig => null;

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
namespace GluttonyCombo.Core
{
    /// <summary>FAKE of Core/ActionRetargeting.cs: only the attribute surface; no retargeting runs.</summary>
    public class ActionRetargeting : IDisposable
    {
        public void Dispose() { }

        public sealed class TargetResolverAttribute : Attribute { }
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

        public static bool CustomActionEnabled(CustomActionType type) =>
            GluttonyCombo.RotationHarness.FakeGame.CustomActionEnabled;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of the UIntExtensions Retarget surface the WHM files compile against (the real ones
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
    ///     FAKE of Extensions/GameObjectExtensions.cs: only the members the WHM files compile against.
    ///     Target-side statuses route into FakeGame.TargetStatuses; debuff applicability is harness
    ///     state (FakeGame.TargetCanApplyStatus). Uses the same C# extension-member syntax as the real
    ///     file (property-style members like HasCleansableDebuff/Role need it).
    /// </summary>
    public static class GameObjectExtensions
    {
        extension(IGameObject? obj)
        {
            public CombatRole Role => CombatRole.NonCombat;

            public bool HasCleansableDebuff => false;

            public bool HasDamageDown => false;

            public bool HasRezWeakness(bool checkBrink = true) => false;

            public bool IsFriendly() => true;

            public bool IsInParty() => GluttonyCombo.RotationHarness.FakeGame.IsInPartyFlag;

            public bool IsNotThePlayer() => true;

            public bool IsBoss() => obj is not null && GluttonyCombo.RotationHarness.FakeGame.TargetIsBoss;

            public bool HasStatus(uint id, bool anyOwner = false) => obj.Status(id, anyOwner) is not null;

            public Dalamud.Game.ClientState.Statuses.IStatus? Status(uint id, bool anyOwner = false)
            {
                if (obj is null) return null;
                foreach (var s in GluttonyCombo.RotationHarness.FakeGame.TargetStatuses)
                {
                    if (s.StatusId != id) continue;
                    if (!anyOwner && s.SourceId != 0) continue; // SourceId 0 = own status
                    return s;
                }
                return null;
            }

            public bool CanApplyStatus(uint statusId) =>
                obj is not null && GluttonyCombo.RotationHarness.FakeGame.TargetCanApplyStatus;

            public IGameObject? IfHostile() =>
                obj is not null && GluttonyCombo.RotationHarness.FakeGame.HasBattleTarget ? obj : null;

            public IGameObject? IfFriendly() => obj;
        }
    }

    /// <summary>
    ///     FAKE of Extensions/StatusExtensions.cs: only RemainingTimeOrZero. The real one is
    ///     null-safe; its animation-lock branch reads ActionManager, which has no offline stand-in,
    ///     so the harness keeps the null-safety and drops that branch.
    /// </summary>
    public static class StatusExtensions
    {
        extension(Dalamud.Game.ClientState.Statuses.IStatus? status)
        {
            public float RemainingTimeOrZero(bool checkAnimationLock = true) => status?.RemainingTime ?? 0f;
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial WHM class: the settings class (the real WHM_Config.cs Draw drags in
    // ImGui/localization; only the fields are mirrored, one-for-one with their shipped defaults).
    internal partial class WHM
    {
        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                WHM_ST_MainCombo_Actions = new("WHM_ST_MainCombo_Actions"),
                WHM_Balance_Content = new("WHM_Balance_Content", 0),
                WHM_ST_DPS_AeroBossOption = new("WHM_ST_DPS_AeroBossOption", 0),
                WHM_ST_DPS_AeroBossAddsOption = new("WHM_ST_DPS_AeroBossAddsOption", 50),
                WHM_ST_DPS_AeroTrashOption = new("WHM_ST_DPS_AeroTrashOption", 50),
                WHM_ST_MainCombo_Misery_Option = new("WHM_ST_MainCombo_Misery_Option", 1),
                WHM_STDPS_LilyOvercap = new("WHM_STDPS_LilyOvercap", 8),
                WHM_STDPS_Lucid = new("WHMLucidDreamingFeature", 6500),
                WHM_AoEDPS_Lucid = new("WHM_AoE_Lucid", 6500),
                WHM_AoE_MainCombo_DoT_HPThreshold = new("WHM_AoE_MainCombo_DoT_HPThreshold", 50),
                WHM_AoE_MainCombo_DoT_MaxTargets = new("WHM_AoE_MainCombo_DoT_MaxTargets", 4),
                WHM_AoE_DPS_Misery_Option = new("WHM_AoE_DPS_Misery_Option", 1),
                WHM_AoEDPS_LilyOvercap = new("WHM_AoEDPS_LilyOvercap", 8),
                WHM_STHeals_RegenHPLower = new("WHM_STHeals_RegenHPLower", 30),
                WHM_STHeals_RegenHPUpper = new("WHM_STHeals_RegenHPUpper", 80),
                WHM_STHeals_BenedictionHP = new("WHM_STHeals_BenedictionHP", 20),
                WHM_STHeals_SolaceHP = new("WHM_STHeals_SolaceHP", 55),
                WHM_STHeals_ThinAir = new("WHM_STHeals_ThinAir", 1),
                WHM_STHeals_TetraHP = new("WHM_STHeals_TetraHP", 55),
                WHM_STHeals_BenisonCharges = new("WHM_STHeals_BenisonCharges", 0),
                WHM_STHeals_BenisonHP = new("WHM_STHeals_BenisonHP", 70),
                WHM_STHeals_AquaveilHP = new("WHM_STHeals_AquaveilHP", 70),
                WHM_STHeals_Lucid = new("WHM_STHeals_Lucid", 6500),
                WHM_STHeals_TemperanceHP = new("WHM_STHeals_TemperanceHP", 45),
                WHM_STHeals_AsylumHP = new("WHM_STHeals_AsylumHP", 60),
                WHM_STHeals_LiturgyOfTheBellHP = new("WHM_STHeals_LiturgyOfTheBellHP", 45),
                WHM_STHeals_Esuna = new("WHM_Cure2_Esuna", 40),
                WHM_AoEHeals_ThinAir = new("WHM_AoE_ThinAir"),
                WHM_AoEHeals_Cure3HP = new("WHM_AoEHeals_Cure3HP", 55),
                WHM_AoEHeals_Cure3Allies = new("WHM_AoEHeals_Cure3Allies", 4),
                WHM_AoEHeals_Cure3MP = new("WHM_AoE_Cure3MP", 6500),
                WHM_AoEHeals_AssizeHP = new("WHM_AoEHeals_AssizeHP", 80),
                WHM_AoEHeals_PlenaryHP = new("WHM_AoEHeals_PlenaryHP", 80),
                WHM_AoEHeals_Lucid = new("WHM_AoEHeals_Lucid", 6500),
                WHM_AoEHeals_Medica2HP = new("WHM_AoEHeals_Medica2HP", 80),
                WHM_AoEHeals_RaptureHP = new("WHM_AoEHeals_RaptureHP", 70),
                WHM_AoEHeals_DivineCaressHP = new("WHM_AoEHeals_DivineCaressHP", 80),
                WHM_AoEHeals_LiturgyHP = new("WHM_AoEHeals_LiturgyHP", 50),
                WHM_AoEHeals_TemperanceHP = new("WHM_AoEHeals_TemperanceHP", 55),
                WHM_AoEHeals_AsylumHP = new("WHM_AoEHeals_AsylumHP", 80),
                WHM_Aquaveil_TetraThreshold = new("WHM_Aquaveil_TetraThreshold", 100);

            public static CustomComboNS.Functions.UserBool
                WHM_Opener_Potion = new("WHM_Opener_Potion"),
                WHM_Opener_PrepullBlock = new("WHM_Opener_PrepullBlock", true),
                WHM_ST_MainCombo_DoT_TwoTarget = new("WHM_ST_MainCombo_DoT_TwoTarget", true),
                WHM_ST_MainCombo_DoT_EarlyInBuffs = new("WHM_ST_MainCombo_DoT_EarlyInBuffs"),
                WHM_STHeals_IncludeShields = new("WHM_STHeals_IncludeShields", false),
                WHM_STHeals_BenedictionWeave = new("WHM_STHeals_BenedictionWeave", false),
                WHM_STHeals_TetraWeave = new("WHM_STHeals_TetraWeave", false),
                WHM_STHeals_TetraBalance = new("WHM_STHeals_TetraBalance", true),
                WHM_STHeals_BenisonWeave = new("WHM_STHeals_BenisonWeave", false),
                WHM_STHeals_BenisonBalance = new("WHM_STHeals_BenisonBalance", true),
                WHM_AoEHeals_AssizeWeave = new("WHM_AoEHeals_AssizeWeave"),
                WHM_AoEHeals_PlenaryWeave = new("WHM_AoEHeals_PlenaryWeave"),
                WHM_AoEHeals_TemperanceWeave = new("WHM_AoEHeals_TemperanceWeave"),
                WHM_AoEHeals_DivineCaressWeave = new("WHM_AoEHeals_DivineCaressWeave"),
                WHM_AoEHeals_LiturgyWeave = new("WHM_AoEHeals_LiturgyWeave"),
                WHM_AoEHeals_AsylumWeave = new("WHM_AoEHeals_AsylumWeave");

            public static CustomComboNS.Functions.UserFloat
                WHM_ST_DPS_AeroUptime_Threshold = new("WHM_ST_DPS_AeroUptime_Threshold", 2),
                WHM_AoE_MainCombo_DoT_Reapply = new("WHM_AoE_MainCombo_DoT_Reapply", 0),
                WHM_STHeals_RegenTimer = new("WHM_STHeals_RegenTimer", 0),
                WHM_AoEHeals_MedicaTime = new("WHM_AoEHeals_MedicaTime");

            public static CustomComboNS.Functions.UserIntArray
                WHM_ST_Heals_Priority = new("WHM_ST_Heals_Priority", [1, 4, 3, 2, 5, 9, 7, 6, 8]),
                WHM_AoE_Heals_Priority = new("WHM_AoE_Heals_Priority", [9, 8, 4, 2, 1, 5, 7, 3, 6]);

            public static CustomComboNS.Functions.UserBoolArray
                WHM_STHeals_AquaveilOptions = new("WHM_STHeals_AquaveilOptions", [false, true, true]),
                WHM_STHeals_TemperanceOptions = new("WHM_STHeals_TemperanceOptions", [false, true]),
                WHM_STHeals_AsylumOptions = new("WHM_STHeals_AsylumOptions", [false, true]),
                WHM_STHeals_LiturgyOfTheBellOptions = new("WHM_STHeals_LiturgyOfTheBellOptions", [false, true]),
                WHM_AoEHeals_LiturgyDifficulty = new("WHM_AoEHeals_LiturgyDifficulty", [true, false]),
                WHM_AoEHeals_TemperanceDifficulty = new("WHM_AoEHeals_TemperanceDifficulty", [true, false]),
                WHM_AoEHeals_AsylumDifficulty = new("WHM_AoEHeals_AsylumDifficulty", [true, false]),
                WHM_AsylumOptions = new("WHM_AsylumOptions", [true, true]),
                WHM_LiturgyOfTheBellOptions = new("WHM_LiturgyOfTheBellOptions", [true, true]),
                WHM_AquaveilOptions = new("WHM_AquaveilOptions", [true, true]);

            public static readonly Data.ContentCheck.ListSet
                WHM_AoEHeals_LiturgyDifficultyListSet = Data.ContentCheck.ListSet.Halved,
                WHM_AoEHeals_TemperanceDifficultyListSet = Data.ContentCheck.ListSet.Halved,
                WHM_AoEHeals_AsylumDifficultyListSet = Data.ContentCheck.ListSet.Halved;
        }
    }
}
