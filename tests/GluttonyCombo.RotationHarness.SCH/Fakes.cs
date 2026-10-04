// Fakes.cs — the harness-owned stand-ins for everything the REAL Scholar job files (SCH.cs,
// SCH_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary:
//   REAL  : every decision line in SCH.cs / SCH_Helper.cs (Invoke branches, the DoT checker NeedsDoT,
//           raidwide gates, fairy summon logic, opener plumbing, action/buff tables, gauge props).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, movement, pet), the Preset enum values, the SCH Config
//           settings defaults, the healer role layer, the retargeting layer, openers' WrathOpener base,
//           content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.
// Shaped after tests/GluttonyCombo.RotationHarness.AST (SC11 round 6); target-status reading (the Biolysis
// remaining time on the enemy) is the SCH-specific addition: FakeGame.TargetStatuses + FakeTargetStatus.

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Scholar members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision logic —
    ///     presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        SCH_ST_Simple_DPS = 40001,
        SCH_AoE_Simple_DPS = 40002,
        SCH_ST_ADV_DPS = 40003,
        SCH_AoE_ADV_DPS = 40004,
        SCH_Simple_ST_Heal = 40005,
        SCH_Simple_AoE_Heal = 40006,
        SCH_ST_Heal = 40007,
        SCH_AoE_Heal = 40008,
        SCH_ST_ADV_DPS_Balance_Opener = 40009,
        SCH_ST_ADV_DPS_Aetherflow = 40010,
        SCH_ST_ADV_DPS_BanefulImpact = 40011,
        SCH_ST_ADV_DPS_ChainStrat = 40012,
        SCH_ST_ADV_DPS_EnergyDrain = 40013,
        SCH_ST_ADV_DPS_Lucid = 40014,
        SCH_ST_ADV_DPS_Bio = 40015,
        SCH_ST_ADV_DPS_Ruin2Movement = 40016,
        SCH_ST_ADV_DPS_FairyReminder = 40017,
        SCH_AoE_ADV_DPS_FairyReminder = 40018,
        SCH_AoE_ADV_DPS_Aetherflow = 40019,
        SCH_AoE_ADV_DPS_BanefulImpact = 40020,
        SCH_AoE_ADV_DPS_ChainStrat = 40021,
        SCH_AoE_ADV_DPS_EnergyDrain = 40022,
        SCH_AoE_ADV_DPS_Lucid = 40023,
        SCH_AoE_ADV_DPS_DoT = 40024,
        SCH_ST_Heal_Esuna = 40025,
        SCH_ST_Heal_Lucid = 40026,
        SCH_ST_Heal_Lustrate = 40027,
        SCH_ST_Heal_Excogitation = 40028,
        SCH_ST_Heal_Protraction = 40029,
        SCH_ST_Heal_Aetherpact = 40030,
        SCH_ST_Heal_Adloquium = 40031,
        SCH_ST_Heal_WhisperingDawn = 40032,
        SCH_ST_Heal_FeyIllumination = 40033,
        SCH_ST_Heal_FeyBlessing = 40034,
        SCH_ST_Heal_Seraphism = 40035,
        SCH_ST_Heal_Expedient = 40036,
        SCH_ST_Heal_SummonSeraph = 40037,
        SCH_ST_Heal_Consolation = 40038,
        SCH_ST_Heal_Dissipation = 40039,
        SCH_ST_Heal_Aetherflow = 40040,
        SCH_AoE_Heal_Lucid = 40041,
        SCH_AoE_Heal_WhisperingDawn = 40042,
        SCH_AoE_Heal_FeyIllumination = 40043,
        SCH_AoE_Heal_FeyBlessing = 40044,
        SCH_AoE_Heal_Seraphism = 40045,
        SCH_AoE_Heal_Indomitability = 40046,
        SCH_AoE_Heal_SummonSeraph = 40047,
        SCH_AoE_Heal_Consolation = 40048,
        SCH_AoE_Heal_Aetherflow = 40049,
        SCH_AoE_Heal_Dissipation = 40050,
        SCH_Raise = 40051,
        SCH_Raise_Retarget = 40052,
        SCH_FairyReminder = 40053,
        SCH_Dissipation = 40054,
        SCH_Aetherflow = 40055,
        SCH_Aetherflow_Recite = 40056,
        SCH_Aetherflow_Dissipation = 40057,
        SCH_Recitation = 40058,
        SCH_DeploymentTactics = 40059,
        SCH_DeploymentTactics_Recitation = 40060,
        SCH_Fairy_Combo = 40061,
        SCH_Fairy_Combo_Consolation = 40062,
        SCH_Mit_ST = 40063,
        SCH_Mit_AoE = 40064,
        SCH_Retarget = 40065,
        SCH_Retarget_Adloquium = 40066,
        SCH_Retarget_Physick = 40067,
        SCH_Retarget_Lustrate = 40068,
        SCH_Retarget_Excogitation = 40069,
        SCH_Retarget_Aetherpact = 40070,
        SCH_Retarget_DeploymentTactics = 40071,
        SCH_Retarget_Protraction = 40072,
        SCH_Retarget_SacredSoil = 40073,
        SCH_Raidwide_SacredSoil = 40074,
        SCH_Raidwide_Expedient = 40075,
        SCH_Raidwide_Succor = 40076,
        SCH_Consolation = 40077,
        SCH_Lustrate = 40078,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE slice of the real partial class All (ALL.cs): the native action ids SCH uses.</summary>
    internal partial class All
    {
        public const uint SingleTargetDPS = 1_000_000; // mirrored from ALL.cs
        public const uint AoEDPS = 1_000_001;          // mirrored from ALL.cs
        public const uint Cease = 1_000_004;           // mirrored from ALL.cs
    }

    /// <summary>FAKE of ALL/JobClasses.cs Healer base: only the Role surface SCH touches.</summary>
    internal class Healer
    {
        protected Healer() { }

        public static FakeHealerRole Role { get; } = new();
    }

    /// <summary>FAKE of the IHealer role surface SCH uses; consts mirrored from Roles/RoleActions.cs.</summary>
    internal class FakeHealerRole
    {
        public uint LucidDreaming => 7562;
        public uint Swiftcast => 7561;
        public uint Esuna => 7568;

        public bool CanLucidDream(int mpThreshold, bool weave = true) =>
            GluttonyCombo.RotationHarness.FakeGame.CanLucidDream;
    }

    /// <summary>FAKE slice of the Sage class: only the shield-buff ids SCH's ShieldCheck reads.</summary>
    internal partial class SGE
    {
        internal static class Buffs
        {
            internal const ushort EukrasianDiagnosis = 2607,
                                  EukrasianPrognosis = 2609;
        }
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
    /// <summary>FAKE of ALL/Items.cs: only the members the SCH opener lambda compiles against.</summary>
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
    ///     FAKE of AutoRotation/AutoRotationController.cs: only the raidwide bookkeeping SCH_Helper's
    ///     Raidwide* helpers read (mit gate + shield gate). Default true = "already used" => helpers decline.
    /// </summary>
    internal static class AutoRotationController
    {
        public static bool RaidwideMitOnCooldown = true;

        public static bool RaidwideShieldOnCooldown = true;

        public static void MarkRaidwideMitUsed() { }

        public static void MarkRaidwideShieldUsed() { }
    }
}

// ======================================================================================
namespace GluttonyCombo.Data
{
    /// <summary>FAKE of Data/ActionWatching.cs: only the GCD counter SCH's Chain Strat gates read.</summary>
    internal static class ActionWatching
    {
        public static int NumberOfGcdsUsed => GluttonyCombo.RotationHarness.FakeGame.NumberOfGcdsUsed;
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

    /// <summary>FAKE of the WrathPartyMember shape (kept for signature parity; SCH's party list is empty).</summary>
    public class WrathPartyMember
    {
        public IBattleChara? BattleChara;
        public ClassJob? RealJob;
        public ulong GameObjectId;
        public uint CurrentHp;
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Scholar files read. Every member routes
    ///     into <see cref="GluttonyCombo.RotationHarness.FakeGame"/>; defaults make a clean, in-combat,
    ///     not-weaving, fairy-out, level-100 state on a living target.
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

        // ---- movement / party / combat-state ----
        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.FakeGame.IsMovingFlag;

        public static System.TimeSpan TimeStoodStill => System.TimeSpan.FromSeconds(GluttonyCombo.RotationHarness.FakeGame.TimeStoodStillSeconds);

        public static bool PartyInCombat() => GluttonyCombo.RotationHarness.FakeGame.PartyInCombatFlag;

        public static bool IsInParty(int partySize = 2) => GluttonyCombo.RotationHarness.FakeGame.IsInPartyFlag;

        public static List<WrathPartyMember> GetPartyMembers(bool allowCache = true) => [];

        public static float GetPartyAvgHPPercent() => GluttonyCombo.RotationHarness.FakeGame.PartyAvgHP;

        public static float GetPartyBuffPercent(ushort buff) => 0f;

        public static bool HasOrExpectsOccultInstantCast => GluttonyCombo.RotationHarness.FakeGame.OccultInstantCast;

        public static bool HasOrExpectsOccultDualcast => GluttonyCombo.RotationHarness.FakeGame.OccultDualcast;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

        public static bool GroupDamageIncoming(float? maxTimeRemaining = null) => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;

        // ---- pet (the fairy: NeedToSummon / FairyBusy paths) ----
        public static bool HasPetPresent() => GluttonyCombo.RotationHarness.FakeGame.HasPetPresent;

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

        public static bool WasLastAction(uint actionID) => GluttonyCombo.RotationHarness.FakeGame.LastActionId == actionID;

        public static float ComboTimer => GluttonyCombo.RotationHarness.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.FakeGame.ComboActionId;

        public static IBattleChara? CurrentTarget =>
            GluttonyCombo.RotationHarness.FakeGame.HardTargetObject as IBattleChara;

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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the members the SCH files compile against.</summary>
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
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the SCH opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.SCH_ST_ADV_DPS_Balance_Opener;

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
    /// <summary>FAKE of Core/ActionRetargeting.cs: the namespace exists for SCH.cs's `using GluttonyCombo.Core;`.</summary>
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
    ///     FAKE of the UIntExtensions Retarget surface the SCH files compile against (the real ones
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
    ///     FAKE of Extensions/GameObjectExtensions.cs: only the members the SCH files compile against.
    ///     Target-side checks route into FakeGame knobs.
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

            public bool IsBoss() => GluttonyCombo.RotationHarness.FakeGame.TargetIsBoss;

            public bool HasStatus(uint id, bool anyOwner = false) => false;

            public bool CanApplyStatus(ushort statusId) => GluttonyCombo.RotationHarness.FakeGame.TargetCanApplyStatus;

            public IGameObject? IfHostile() => obj is not null && GluttonyCombo.RotationHarness.FakeGame.HasBattleTarget ? obj : null;

            public IGameObject? IfFriendly() => obj;
        }
    }

    /// <summary>
    ///     FAKE of Extensions/StatusExtensions.cs: the IBattleChara Status/HasStatus lookup and the
    ///     IStatus? fluent chains. The real ones read the game's status cache; here the TARGET's status
    ///     list lives in FakeGame.TargetStatuses (the player's in FakeGame.Statuses). The animation-lock
    ///     path of RemainingTimeOrZero reads ActionManager and is OUT of the tested boundary (harness
    ///     statuses always have positive remaining time, matching the real call sites' semantics).
    /// </summary>
    public static class StatusExtensions
    {
        extension(IStatus? status)
        {
            public ushort Stacks => status?.Param ?? 0;

            public float RemainingTimeOrZero(bool checkAnimationLock = true) => status?.RemainingTime ?? 0f;

            public float RemainingTimeOrNaN(bool checkAnimationLock = true) => status?.RemainingTime ?? float.NaN;
        }

        extension(IBattleChara? chara)
        {
            public IStatus? Status(uint id, bool anyOwner = false) =>
                GluttonyCombo.RotationHarness.FakeGame.TargetStatus(id);

            public bool HasStatus(uint id, bool anyOwner = false) =>
                chara.Status(id, anyOwner) is not null;
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial SCH class: the settings class (the real SCH_Config.cs Draw drags in
    // ImGui/localization; only the fields are mirrored, one-for-one with their shipped defaults).
    internal partial class SCH
    {
        internal static class Config
        {
            internal static CustomComboNS.Functions.UserInt
                SCH_ST_DPS_LucidOption = new("SCH_ST_DPS_LucidOption", 6500),
                SCH_AoE_DPS_LucidOption = new("SCH_AoE_LucidOption", 6500),
                SCH_ST_DPS_OpenerOption = new("SCH_ST_DPS_OpenerOption"),
                SCH_ST_DPS_OpenerContent = new("SCH_ST_DPS_OpenerContent", 1),
                SCH_ST_DPS_ChainStratagemOption = new("SCH_ST_DPS_ChainStratagemOption", 10),
                SCH_ST_DPS_BioBossOption = new("SCH_ST_DPS_BioBossOption", 0),
                SCH_ST_DPS_BioBossAddsOption = new("SCH_ST_DPS_BioBossAddsOption", 100),
                SCH_ST_DPS_BioTrashOption = new("SCH_ST_DPS_BioTrashOption", 50),
                SCH_AoE_DPS_ChainStratagemOption = new("SCH_AoE_DPS_ChainStratagemOption", 10),
                SCH_ST_DPS_EnergyDrain = new("SCH_ST_DPS_EnergyDrain", 3),
                SCH_ST_DPS_ChainStratagemSubOption = new("SCH_ST_DPS_ChainStratagemSubOption", 1),
                SCH_AoE_DPS_EnergyDrain = new("SCH_AoE_DPS_EnergyDrain", 3),
                SCH_AoE_DPS_ChainStratagemSubOption = new("SCH_AoE_DPS_ChainStratagemSubOption", 1),
                SCH_AoE_ADV_DPS_DoT_HPThreshold = new("SCH_AoE_ADV_DPS_DoT_HPThreshold", 30),
                SCH_AoE_ADV_DPS_DoT_MaxTargets = new("SCH_AoE_ADV_DPS_DoT_MaxTargets", 4),
                SCH_ST_DPS_Adv_Actions = new("SCH_ST_DPS_Adv_Actions");

            internal static CustomComboNS.Functions.UserBool
                SCH_Opener_Potion = new("SCH_Opener_Potion"),
                SCH_Opener_PrepullBlock = new("SCH_Opener_PrepullBlock", true),
                SCH_ST_ADV_DPS_Bio_TwoTarget = new("SCH_ST_ADV_DPS_Bio_TwoTarget"),
                SCH_ST_DPS_EnergyDrain_Burst = new("SCH_ST_DPS_EnergyDrain_Burst"),
                SCH_AoE_DPS_EnergyDrain_Burst = new("SCH_AoE_DPS_EnergyDrain_Burst"),
                SCH_AoE_DPS_ChainStratagemBanefulOption = new("SCH_AoE_DPS_ChainStratagemBanefulOption"),
                SCH_AoE_Heal_Aetherflow_Indomitability = new("SCH_AoE_Heal_Aetherflow_Indomitability"),
                SCH_AoE_Heal_Dissipation_Indomitability = new("SCH_AoE_Heal_Dissipation_Indomitability"),
                SCH_Raidwide_Succor_Recitation = new("SCH_Raidwide_Succor_Recitation");

            internal static CustomComboNS.Functions.UserFloat
                SCH_ST_DPS_BioUptime_Threshold = new("SCH_ST_DPS_BioUptime_Threshold", 3.0f),
                SCH_AoE_ADV_DPS_DoT_Reapply = new("SCH_AoE_ADV_DPS_DoT_Reapply", 0);

            public static CustomComboNS.Functions.UserInt
                SCH_AoE_Heal_LucidOption = new("SCH_AoE_Heal_LucidOption", 8000),
                SCH_AoE_Heal_SuccorShieldOption = new("SCH_AoE_Heal_SuccorShieldCount", 50),
                SCH_AoE_Heal_WhisperingDawnOption = new("SCH_AoE_Heal_WhisperingDawnOption", 80),
                SCH_AoE_Heal_FeyIlluminationOption = new("SCH_AoE_Heal_FeyIlluminationOption", 80),
                SCH_AoE_Heal_ConsolationOption = new("SCH_AoE_Heal_ConsolationOption", 65),
                SCH_AoE_Heal_FeyBlessingOption = new("SCH_AoE_Heal_FeyBlessingOption", 70),
                SCH_AoE_Heal_SeraphismOption = new("SCH_AoE_Heal_SeraphismOption", 50),
                SCH_AoE_Heal_IndomitabilityOption = new("SCH_AoE_Heal_IndomitabilityOption", 60),
                SCH_AoE_Heal_SummonSeraph = new("SCH_AoE_Heal_SummonSeraph", 50),
                SCH_ST_Heal_LucidOption = new("SCH_ST_Heal_LucidOption", 8000),
                SCH_ST_Heal_AdloquiumOption = new("SCH_ST_Heal_AdloquiumOption", 70),
                SCH_ST_Heal_AdloquiumOption_Emergency = new("SCH_ST_Heal_AdloquiumOption_Emergency", 30),
                SCH_ST_Heal_LustrateOption = new("SCH_ST_Heal_LustrateOption", 55),
                SCH_ST_Heal_ExcogitationOption = new("SCH_ST_Heal_ExcogitationOption", 70),
                SCH_ST_Heal_ProtractionOption = new("SCH_ST_Heal_ProtractionOption", 70),
                SCH_ST_Heal_AetherpactOption = new("SCH_ST_Heal_AetherpactOption", 50),
                SCH_ST_Heal_AetherpactDissolveOption = new("SCH_ST_Heal_AetherpactDissolveOption", 90),
                SCH_ST_Heal_AetherpactFairyGauge = new("SCH_ST_Heal_AetherpactFairyGauge", 50),
                SCH_ST_Heal_WhisperingDawnOption = new("SCH_ST_Heal_WhisperingDawnOption", 55),
                SCH_ST_Heal_FeyIlluminationOption = new("SCH_ST_Heal_FeyIlluminationOption", 55),
                SCH_ST_Heal_FeyBlessingOption = new("SCH_ST_Heal_FeyBlessingOption", 55),
                SCH_ST_Heal_SeraphismOption = new("SCH_ST_Heal_SeraphismOption", 45),
                SCH_ST_Heal_ExpedientOption = new("SCH_ST_Heal_ExpedientOption", 50),
                SCH_ST_Heal_SummonSeraphOption = new("SCH_ST_Heal_SummonSeraphOption", 45),
                SCH_ST_Heal_ConsolationOption = new("SCH_ST_Heal_ConsolationOption", 55),
                SCH_ST_Heal_EsunaOption = new("SCH_ST_Heal_EsunaOption", 40);

            public static CustomComboNS.Functions.UserIntArray
                // ST: Protraction → Excog → Lustrate → Aetherpact → Adlo → FeyBless → Dawn → Illum → Consolation → Seraph → Seraphism → Expedient
                SCH_ST_Heals_Priority = new("SCH_ST_Heals_Priority", [3, 2, 1, 4, 5, 7, 8, 6, 11, 12, 10, 9]),
                // AoE: Illum → Dawn → Bless → Indom → Consolation → Seraph → Seraphism → Succor
                SCH_AoE_Heals_Priority = new("SCH_AoE_Heals_Priority", [2, 1, 3, 5, 7, 4, 6, 8]);

            public static CustomComboNS.Functions.UserBool
                SCH_ST_Heal_IncludeShields = new("SCH_ST_Heal_IncludeShields"),
                SCH_ST_Heal_WhisperingDawnBossOption = new("SCH_ST_Heal_WhisperingDawnBossOption", true),
                SCH_ST_Heal_FeyIlluminationBossOption = new("SCH_ST_Heal_FeyIlluminationBossOption", true),
                SCH_ST_Heal_FeyBlessingBossOption = new("SCH_ST_Heal_FeyBlessingBossOption", true),
                SCH_ST_Heal_ExcogitationBossOption = new("SCH_ST_Heal_ExcogitationBossOption"),
                SCH_ST_Heal_ExcogitationTankOption = new("SCH_ST_Heal_ExcogitationTankOption", true),
                SCH_ST_Heal_ProtractionBossOption = new("SCH_ST_Heal_ProtractionBossOption"),
                SCH_ST_Heal_ProtractionTankOption = new("SCH_ST_Heal_ProtractionTankOption", true),
                SCH_ST_Heal_SeraphismBossOption = new("SCH_ST_Heal_SeraphismBossOption", true),
                SCH_ST_Heal_ExpedientBossOption = new("SCH_ST_Heal_ExpedientBossOption", true),
                SCH_ST_Heal_SummonSeraphBossOption = new("SCH_ST_Heal_SummonSeraphBossOption", true),
                SCH_ST_Heal_ConsolationBossOption = new("SCH_ST_Heal_ConsolationBossOption", true),
                SCH_AoE_Heal_Indomitability_Recitation = new("SCH_AoE_Heal_Indomitability_Recitation");

            public static CustomComboNS.Functions.UserBoolArray
                SCH_ST_Heal_AldoquimOpts = new("SCH_ST_Heal_AldoquimOpts", [true, true, true]),
                SCH_AoE_Heal_Succor_Options = new("SCH_AoE_Heal_Succor_Options", [true, false]);

            internal static CustomComboNS.Functions.UserBool
                SCH_Dissipation_WastePrevention = new("SCH_Dissipation_WastePrevention"),
                SCH_Aetherflow_Recite_Indom = new("SCH_Aetherflow_Recite_Indom"),
                SCH_Aetherflow_Recite_Excog = new("SCH_Aetherflow_Recite_Excog");

            internal static CustomComboNS.Functions.UserInt
                SCH_Aetherflow_Display = new("SCH_Aetherflow_Display"),
                SCH_Aetherflow_Recite_ExcogMode = new("SCH_Aetherflow_Recite_ExcogMode"),
                SCH_Aetherflow_Recite_IndomMode = new("SCH_Aetherflow_Recite_IndomMode"),
                SCH_Recitation_Mode = new("SCH_Recitation_Mode");

            internal static CustomComboNS.Functions.UserBoolArray
                SCH_Retarget_SacredSoilOptions = new("SCH_Retarget_SacredSoilOptions"),
                SCH_Mit_STOptions = new("SCH_Mit_STOptions"),
                SCH_Mit_AoEOptions = new("SCH_Mit_AoEOptions");
        }
    }
}
