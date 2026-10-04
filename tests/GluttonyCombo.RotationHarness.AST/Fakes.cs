// Fakes.cs — the harness-owned stand-ins for everything the REAL Astrologian job files (AST.cs,
// AST_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary:
//   REAL  : every decision line in AST.cs / AST_Helper.cs (Invoke branches, weave/pool/opener helpers,
//           gauge props, action IDs, Buffs tables, the card targeting resolver, the heal priority logic).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, movement, GCD count), the Preset enum values, the AST
//           Config settings defaults, the healer role layer, the retargeting layer, openers'
//           WrathOpener base, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

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
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Astrologian members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision logic —
    ///     presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        AST_ST_Simple_DPS = 40001,
        AST_AOE_Simple_DPS = 40002,
        AST_ST_DPS = 40003,
        AST_AOE_DPS = 40004,
        AST_Simple_ST_Heals = 40005,
        AST_Simple_AoE_Heals = 40006,
        AST_ST_Heals = 40007,
        AST_AoE_Heals = 40008,
        AST_ST_DPS_Opener = 40009,
        AST_ST_DPS_EarthlyStar = 40010,
        AST_ST_DPS_StellarDetonation = 40011,
        AST_ST_DPS_Move_DoT = 40012,
        AST_ST_DPS_CombustUptime = 40013,
        AST_DPS_LightSpeed = 40014,
        AST_DPS_LightSpeedHold = 40015,
        AST_DPS_LightspeedBurst = 40016,
        AST_DPS_Divination = 40017,
        AST_DPS_AutoDraw = 40018,
        AST_DPS_AutoPlay = 40019,
        AST_DPS_CardPool = 40020,
        AST_DPS_LordPool = 40021,
        AST_DPS_LazyLord = 40022,
        AST_DPS_Lucid = 40023,
        AST_DPS_Oracle = 40024,
        AST_AOE_LightSpeed = 40025,
        AST_AOE_LightSpeedHold = 40026,
        AST_AOE_LightspeedBurst = 40027,
        AST_AOE_Divination = 40028,
        AST_AOE_AutoDraw = 40029,
        AST_AOE_AutoPlay = 40030,
        AST_AOE_CardPool = 40031,
        AST_AOE_LordPool = 40032,
        AST_AOE_LazyLord = 40033,
        AST_AOE_Lucid = 40034,
        AST_AOE_Oracle = 40035,
        AST_AOE_DPS_EarthlyStar = 40036,
        AST_AOE_DPS_StellarDetonation = 40037,
        AST_AOE_DPS_MacroCosmos = 40038,
        AST_AOE_DPS_DoT = 40039,
        AST_ST_Heals_Esuna = 40040,
        AST_ST_Heals_Lucid = 40041,
        AST_ST_Heals_CelestialIntersection = 40042,
        AST_ST_Heals_EssentialDignity = 40043,
        AST_ST_Heals_EssentialDignity_Emergency = 40044,
        AST_ST_Heals_Exaltation = 40045,
        AST_ST_Heals_Bole = 40046,
        AST_ST_Heals_Arrow = 40047,
        AST_ST_Heals_Ewer = 40048,
        AST_ST_Heals_Spire = 40049,
        AST_ST_Heals_AspectedBenefic = 40050,
        AST_ST_Heals_CelestialOpposition = 40051,
        AST_ST_Heals_CollectiveUnconscious = 40052,
        AST_ST_Heals_SoloLady = 40053,
        AST_ST_Heals_NeutralSect = 40054,
        AST_AoE_Heals_Lucid = 40055,
        AST_AoE_Heals_LazyLady = 40056,
        AST_AoE_Heals_CelestialOpposition = 40057,
        AST_AoE_Heals_Horoscope = 40058,
        AST_AoE_Heals_HoroscopeHeal = 40059,
        AST_AoE_Heals_NeutralSect = 40060,
        AST_AoE_Heals_StellarDetonation = 40061,
        AST_AoE_Heals_Aspected = 40062,
        AST_AoE_Heals_Helios = 40063,
        AST_AoE_Heals_CollectiveUnconscious = 40064,
        AST_AoE_Heals_NeutralSect_SunSign = 40065,
        AST_Cards_QuickTargetCards = 40066,
        AST_Benefic = 40067,
        AST_Lightspeed_Protection = 40068,
        AST_Raise_Alternative = 40069,
        AST_Raise_Alternative_Retarget = 40070,
        AST_Mit_ST = 40071,
        AST_Mit_AoE = 40072,
        AST_Retargets = 40073,
        AST_Retargets_Benefic = 40074,
        AST_Retargets_AspectedBenefic = 40075,
        AST_Retargets_EssentialDignity = 40076,
        AST_Retargets_Exaltation = 40077,
        AST_Retargets_Synastry = 40078,
        AST_Retargets_CelestialIntersection = 40079,
        AST_Retargets_HealCards = 40080,
        AST_Retargets_EarthlyStar = 40081,
        AST_Raidwide_CollectiveUnconscious = 40082,
        AST_Raidwide_NeutralSect = 40083,
        AST_Raidwide_AspectedHelios = 40084,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE slice of the real partial class All (ALL.cs): the native action ids AST uses.</summary>
    internal partial class All
    {
        public const uint SingleTargetDPS = 1_000_000; // mirrored from ALL.cs
        public const uint AoEDPS = 1_000_001;          // mirrored from ALL.cs
        public const uint Cease = 1_000_004;           // mirrored from ALL.cs
    }

    /// <summary>FAKE of ALL/JobClasses.cs Healer base: only the Role surface AST touches.</summary>
    internal class Healer
    {
        protected Healer() { }

        public static FakeHealerRole Role { get; } = new();
    }

    /// <summary>FAKE of the IHealer role surface AST uses; consts mirrored from Roles/RoleActions.cs.</summary>
    internal class FakeHealerRole
    {
        public uint LucidDreaming => 7562;
        public uint Swiftcast => 7561;
        public uint Esuna => 7568;

        public bool CanLucidDream(int mpThreshold, bool weave = true) =>
            GluttonyCombo.RotationHarness.FakeGame.CanLucidDream;
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
    /// <summary>FAKE of ALL/Items.cs: only the members the AST opener lambdas compile against.</summary>
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
    ///     FAKE of AutoRotation/AutoRotationController.cs: only the raidwide-mit bookkeeping AST_Helper's
    ///     Raidwide* helpers read. Default true = raidwide mit "already used" => helpers decline.
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
    /// <summary>FAKE of Data/ActionWatching.cs: only the GCD counter AST_Helper.WaitGCDs reads.</summary>
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

    /// <summary>FAKE of the WrathPartyMember shape AST_Helper's CardTarget consumes.</summary>
    public class WrathPartyMember
    {
        public IBattleChara? BattleChara;
        public ClassJob? RealJob;
        public ulong GameObjectId;
        public uint CurrentHp;
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Astrologian files read. Every member routes
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

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.FakeGame.InActionRange;

        public static bool InActionRange(uint actionID, IGameObject? target) => InActionRange(actionID);

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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the members the AST files compile against.</summary>
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
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the AST opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.AST_ST_DPS_Opener;

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
    /// <summary>FAKE of Core/ActionRetargeting.cs: only the attribute AST_Helper's CardResolver carries.</summary>
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
    ///     FAKE of the UIntExtensions Retarget surface the AST files compile against (the real ones
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
    ///     FAKE of Extensions/GameObjectExtensions.cs: only the members the AST files compile against.
    ///     Target-side statuses and debuff checks are OUT of the tested boundary (fixed answers).
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

            public IStatus? Status(uint id, bool anyOwner = false) => null;

            public bool CanApplyStatus(ushort statusId) => GluttonyCombo.RotationHarness.FakeGame.TargetCanApplyStatus;

            public IGameObject? IfHostile() => obj is not null && GluttonyCombo.RotationHarness.FakeGame.HasBattleTarget ? obj : null;

            public IGameObject? IfFriendly() => obj;
        }
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
    // FAKE part of the partial AST class: the settings class (the real AST_Config.cs Draw drags in
    // ImGui/localization; only the fields are mirrored, one-for-one with their shipped defaults).
    /// <summary>FAKE of Combos/PvE/ALL/Bursting.cs: only the member the AST files read after
    /// the AST-2 change (Bursting.PartyIsBursting); constant false keeps the round-6 cases
    /// exactly as they were (the early-refresh gate stays closed).</summary>
    public class Bursting
    {
        public static bool PartyIsBursting => false;
    }

    internal partial class AST
    {
        internal static class Config
        {
            public static CustomComboNS.Functions.UserIntArray
                // ST: EmerED → NeutralSect → Exalt → CI → ED → Bole → Arrow → Spire → Ewer → Aspected → CelOpp → CU → Lady
                AST_ST_SimpleHeals_Priority = new("AST_ST_SimpleHeals_Priority", [4, 5, 3, 6, 7, 9, 8, 10, 11, 12, 13, 1, 2]),
                // AoE: NeutralSect → Horoscope → Lady → CelOpp → CU → HoroscopeHeal → Stellar → Aspected → Helios
                AST_AoE_SimpleHeals_Priority = new("AST_AoE_SimpleHeals_Priority", [3, 4, 2, 6, 1, 7, 8, 9, 5]);

            public static CustomComboNS.Functions.UserInt
                //HEALS
                AST_ST_SimpleHeals_Spire = new("AST_ST_SimpleHeals_Spire", 70),
                AST_ST_SimpleHeals_Ewer = new("AST_ST_SimpleHeals_Ewer", 60),
                AST_ST_SimpleHeals_Arrow = new("AST_ST_SimpleHeals_Arrow", 70),
                AST_ST_SimpleHeals_Bole = new("AST_ST_SimpleHeals_Bole", 70),
                AST_ST_SimpleHeals_CelestialIntersection = new("AST_ST_SimpleHeals_CelestialIntersection", 70),
                AST_ST_SimpleHeals_CelestialIntersectionCharges = new("AST_ST_SimpleHeals_CelestialIntersectionCharges", 0),
                AST_ST_SimpleHeals_EssentialDignity = new("AST_ST_SimpleHeals_EssentialDignity", 55),
                AST_ST_SimpleHeals_Exaltation = new("AST_ST_SimpleHeals_Exaltation", 70),
                AST_ST_SimpleHeals_Esuna = new("AST_ST_SimpleHeals_Esuna", 40),
                AST_ST_SimpleHeals_AspectedBeneficHigh = new("AST_ST_SimpleHeals_AspectedBeneficHigh", 80),
                AST_ST_SimpleHeals_AspectedBeneficLow = new("AST_ST_SimpleHeals_AspectedBeneficLow", 40),
                AST_ST_SimpleHeals_AspectedBeneficRefresh = new("AST_ST_SimpleHeals_AspectedBeneficRefresh", 3),
                AST_ST_SimpleHeals_CollectiveUnconscious = new("AST_ST_SimpleHeals_CollectiveUnconscious", 55),
                AST_ST_SimpleHeals_CelestialOpposition = new("AST_ST_SimpleHeals_CelestialOpposition", 55),
                AST_ST_SimpleHeals_SoloLady = new("AST_ST_SimpleHeals_SoloLady", 55),
                AST_ST_SimpleHeals_EmergencyED_Threshold = new("AST_ST_SimpleHeals_EmergencyED_Threshold", 30),
                AST_ST_Heals_NeutralSect_Threshold = new("AST_ST_Heals_NeutralSect_Threshold", 50),
                AST_ST_Heals_LucidDreaming = new("AST_ST_Heals_LucidDreaming", 6500),
                AST_AoE_Heals_LucidDreaming = new("AST_AoE_Heals_LucidDreaming", 6500),
                AST_AoE_SimpleHeals_AltMode = new("AST_AoE_SimpleHeals_AltMode", 1),
                AST_AoE_SimpleHeals_LazyLady = new("AST_AoE_SimpleHeals_LazyLady", 70),
                AST_AoE_SimpleHeals_Horoscope = new("AST_AoE_SimpleHeals_Horoscope", 80),
                AST_AoE_SimpleHeals_CelestialOpposition = new("AST_AoE_SimpleHeals_CelestialOpposition", 80),
                AST_AoE_SimpleHeals_CollectiveUnconscious = new("AST_AoE_SimpleHeals_CollectiveUnconscious", 55),
                AST_AoE_SimpleHeals_NeutralSect = new("AST_AoE_SimpleHeals_NeutralSect", 60),
                AST_AoE_SimpleHeals_HoroscopeHeal = new("AST_AoE_SimpleHeals_HoroscopeHeal", 65),
                AST_AoE_SimpleHeals_StellarDetonation = new("AST_AoE_SimpleHeals_StellarDetonation", 60),
                AST_AoE_SimpleHeals_Aspected = new("AST_AoE_SimpleHeals_Aspected", 80),
                AST_AoE_SimpleHeals_Helios = new("AST_AoE_SimpleHeals_Helios", 80),
                AST_Mit_ST_EssentialDignityThreshold = new("AST_Mit_ST_EssentialDignityThreshold", 80),

                //DPS
                AST_ST_DPS_Opener_SkipStar = new("AST_ST_DPS_Opener_SkipStar"),
                AST_ST_DPS_DivinationOption = new("AST_ST_DPS_DivinationOption"),
                AST_ST_DPS_AltMode = new("AST_ST_DPS_AltMode"),
                AST_ST_DPS_LucidDreaming = new("AST_ST_DPS_LucidDreaming", 8000),
                AST_ST_DPS_LightSpeedOption = new("AST_ST_DPS_LightSpeedOption"),
                AST_ST_DPS_CombustBossOption = new("AST_ST_DPS_CombustBossOption", 0),
                AST_ST_DPS_CombustBossAddsOption = new("AST_ST_DPS_CombustBossAddsOption", 80),
                AST_ST_DPS_CombustTrashOption = new("AST_ST_DPS_CombustTrashOption", 50),
                AST_ST_DPS_DivinationSubOption = new("AST_ST_DPS_DivinationSubOption", 0),
                AST_ST_DPS_Balance_Content = new("AST_ST_DPS_Balance_Content", 1),
                AST_ST_DPS_EarthlyStarSubOption = new("AST_ST_DPS_EarthlyStarSubOption", 0),
                AST_ST_DPS_StellarDetonation_Threshold = new("AST_ST_DPS_StellarDetonation_Threshold", 0),
                AST_ST_DPS_StellarDetonation_SubOption = new("AST_ST_DPS_StellarDetonation_SubOption", 0),
                AST_AOE_LucidDreaming = new("AST_AOE_LucidDreaming", 8000),
                AST_AOE_DivinationSubOption = new("AST_AOE_DivinationSubOption", 0),
                AST_AOE_DivinationOption = new("AST_AOE_DivinationOption"),
                AST_AOE_LightSpeedOption = new("AST_AOE_LightSpeedOption"),
                AST_AOE_DPS_EarthlyStarSubOption = new("AST_AOE_DPS_EarthlyStarSubOption", 0),
                AST_AOE_DPS_StellarDetonation_Threshold = new("AST_AOE_DPS_StellarDetonation_Threshold", 0),
                AST_AOE_DPS_StellarDetonation_SubOption = new("AST_AOE_DPS_StellarDetonation_SubOption", 0),
                AST_AOE_DPS_MacroCosmos_SubOption = new("AST_AOE_DPS_MacroCosmos_SubOption", 0),
                AST_AOE_DPS_DoT_HPThreshold = new("AST_AOE_DPS_DoT_HPThreshold", 30),
                AST_AOE_DPS_DoT_MaxTargets = new("AST_AOE_DPS_DoT_MaxTargets", 4),
                AST_QuickTarget_Override = new("AST_QuickTarget_Override", 0);

            public static CustomComboNS.Functions.UserBool
                //HEALS
                AST_ST_SimpleHeals_IncludeShields = new("AST_ST_SimpleHeals_IncludeShields"),
                AST_ST_SimpleHeals_WeaveDignity = new("AST_ST_SimpleHeals_WeaveDignity"),
                AST_ST_SimpleHeals_WeaveIntersection = new("AST_ST_SimpleHeals_WeaveIntersection"),
                AST_ST_SimpleHeals_WeaveEwer = new("AST_ST_SimpleHeals_WeaveEwer"),
                AST_ST_SimpleHeals_WeaveSpire = new("AST_ST_SimpleHeals_WeaveSpire"),
                AST_ST_SimpleHeals_WeaveEmergencyED = new("AST_ST_SimpleHeals_WeaveEmergencyED"),
                AST_AoE_SimpleHeals_WeaveLady = new("AST_AoE_SimpleHeals_WeaveLady"),
                AST_AoE_SimpleHeals_WeaveOpposition = new("AST_AoE_SimpleHeals_WeaveOpposition"),
                AST_AoE_SimpleHeals_WeaveCollectiveUnconscious = new("AST_AoE_SimpleHeals_WeaveCollectiveUnconscious"),
                AST_AoE_SimpleHeals_WeaveHoroscope = new("AST_AoE_SimpleHeals_WeaveHoroscope"),
                AST_AoE_SimpleHeals_WeaveNeutralSect = new("AST_AoE_SimpleHeals_WeaveNeutralSect"),
                AST_AoE_SimpleHeals_WeaveHoroscopeHeal = new("AST_AoE_SimpleHeals_WeaveHoroscopeHeal"),
                AST_AoE_SimpleHeals_WeaveStellarDetonation = new("AST_AoE_SimpleHeals_WeaveStellarDetonation"),
                //DPS
                AST_ST_DPS_CombustUptime_TwoTarget = new("AST_ST_DPS_CombustUptime_TwoTarget"),
                AST_ST_DPS_CombustUptime_BurstRefresh = new("AST_ST_DPS_CombustUptime_BurstRefresh"),
                AST_ST_DPS_OverwriteHealCards = new("AST_ST_DPS_OverwriteHealCards"),
                AST_AOE_DPS_OverwriteHealCards = new("AST_AOE_DPS_OverwriteHealCards"),
                AST_QuickTarget_Manuals = new("AST_QuickTarget_Manuals", true),
                AST_Opener_Potion = new("AST_Opener_Potion"),
                AST_Opener_PrepullBlock = new("AST_Opener_PrepullBlock", true);

            public static CustomComboNS.Functions.UserFloat
                AST_AOE_DPS_DoT_Reapply = new("AST_AOE_DPS_DoT_Reapply", 2),
                AST_ST_DPS_CombustUptime_Threshold = new("AST_ST_DPS_CombustUptime_Threshold");

            public static CustomComboNS.Functions.UserBoolArray
                AST_ST_SimpleHeals_CelestialOppositionOptions = new("AST_ST_SimpleHeals_CelestialOppositionOptions", [false, true]),
                AST_ST_SimpleHeals_CollectiveUnconsciousOptions = new("AST_ST_SimpleHeals_CollectiveUnconsciousOptions", [false, true]),
                AST_ST_SimpleHeals_SoloLadyOptions = new("AST_ST_SimpleHeals_SoloLadyOptions", [false, true]),
                AST_ST_Heals_NeutralSectOptions = new("AST_ST_Heals_NeutralSectOptions", [false, true]),
                AST_ST_SimpleHeals_ExaltationOptions = new("AST_ST_SimpleHeals_ExaltationOptions", [false, true, true]),
                AST_ST_SimpleHeals_BoleOptions = new("AST_ST_SimpleHeals_BoleOptions", [false, true]),
                AST_ST_SimpleHeals_ArrowOptions = new("AST_ST_SimpleHeals_ArrowOptions", [false, true]),
                AST_Mit_ST_Options = new("AST_Mit_ST_Options"),
                AST_EarthlyStarOptions = new("AST_EarthlyStarOptions");
        }
    }
}
