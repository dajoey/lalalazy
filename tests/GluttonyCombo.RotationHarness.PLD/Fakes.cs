// Fakes.cs — the harness-owned stand-ins for everything the REAL Paladin job files (PLD.cs, PLD_Helper.cs,
// compiled unchanged from the pinned revision) reach outside themselves. The boundary is deliberate:
//   REAL  : every decision line in PLD.cs / PLD_Helper.cs (Invoke, mit/ogcd/gcd helpers, openers' step
//           tables, action IDs, Buffs/Debuffs tables).
//   FAKE  : the game + Dalamud + plugin boundary the decisions READ through — CustomComboFunctions
//           (cooldowns, statuses, gauge, target, combat, movement), the Preset enum values, the PLD Config
//           settings defaults, the Tank role layer, the SimpleTarget/GameObject extension surface, the
//           Retarget extensions, content/IPC side channels, AND the plugin seam
//           GluttonyCombo.P.UIHelper.PresetControlled (Offline Harness Approach section 10's PLD blocker:
//           stubbed to answer "not IPC-controlled", so the mit-options branch evaluates its left term only).
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using System.Collections.Generic;
using System.Reflection;

// ======================================================================================
namespace GluttonyCombo
{
    /// <summary>
    ///     FAKE of the plugin entry seam (real: GluttonyCombo.cs:61 `internal static GluttonyCombo? P`,
    ///     whose UIHelper is Services/IPC/UIHelper.cs). Only the member PLD reads is provided:
    ///     PresetControlled answers null = "not IPC-controlled" offline.
    /// </summary>
    internal static class P
    {
        internal static readonly HarnessUIHelper UIHelper = new();
    }

    internal sealed class HarnessUIHelper
    {
        internal (string controllers, bool enabled, bool autoMode)? PresetControlled(Combos.Preset preset) => null;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Paladin members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        PLD_ST_SimpleMode = 40001,
        PLD_AoE_SimpleMode = 40002,
        PLD_ST_AdvancedMode = 40003,
        PLD_AoE_AdvancedMode = 40004,
        PLD_ST_BasicCombo = 40005,
        PLD_AoE_BasicCombo = 40006,
        PLD_Requiescat_Options = 40007,
        PLD_SpiritsWithin = 40008,
        PLD_ShieldLob_Feature = 40009,
        PLD_RetargetClemency = 40010,
        PLD_RetargetClemency_MO = 40011,
        PLD_RetargetClemency_LowHP = 40012,
        PLD_RetargetSheltron = 40013,
        PLD_RetargetSheltron_MO = 40014,
        PLD_RetargetSheltron_TT = 40015,
        PLD_Mit_OneButton = 40016,
        PLD_Mit_HallowedGround_Max = 40017,
        PLD_Mit_Sheltron = 40018,
        PLD_Mit_Reprisal = 40019,
        PLD_Mit_DivineVeil = 40020,
        PLD_Mit_Rampart = 40021,
        PLD_Mit_Bulwark = 40022,
        PLD_Mit_ArmsLength = 40023,
        PLD_Mit_Sentinel = 40024,
        PLD_Mit_Clemency = 40025,
        PLD_Mit_Party = 40026,
        PLD_Mit_Party_Wings = 40027,
        PLD_RetargetShieldBash = 40028,
        PLD_RetargetCover = 40029,
        PLD_RetargetCover_MO = 40030,
        PLD_RetargetCover_LowHP = 40031,
        PLD_RetargetIntervene = 40032,
        PLD_BlockForWings = 40033,
        PLD_ST_AdvancedMode_BalanceOpener = 40034,
        PLD_ST_AdvancedMode_GoringBlade = 40035,
        PLD_ST_AdvancedMode_FoF = 40036,
        PLD_ST_AdvancedMode_CircleOfScorn = 40037,
        PLD_ST_AdvancedMode_SpiritsWithin = 40038,
        PLD_ST_AdvancedMode_Intervene = 40039,
        PLD_ST_AdvancedMode_ShieldLob = 40040,
        PLD_ST_AdvancedMode_MP_Reserve = 40041,
        PLD_ST_AdvancedMode_Requiescat = 40042,
        PLD_ST_AdvancedMode_Confiteor = 40043,
        PLD_ST_AdvancedMode_HolySpirit = 40044,
        PLD_ST_AdvancedMode_Atonement = 40045,
        PLD_ST_AdvancedMode_BladeOfHonor = 40046,
        PLD_AoE_AdvancedMode_GoringBlade = 40047,
        PLD_AoE_AdvancedMode_FoF = 40048,
        PLD_AoE_AdvancedMode_CircleOfScorn = 40049,
        PLD_AoE_AdvancedMode_SpiritsWithin = 40050,
        PLD_AoE_AdvancedMode_Intervene = 40051,
        PLD_AoE_AdvancedMode_ShieldLob = 40052,
        PLD_AoE_AdvancedMode_MP_Reserve = 40053,
        PLD_AoE_AdvancedMode_Requiescat = 40054,
        PLD_AoE_AdvancedMode_Confiteor = 40055,
        PLD_AoE_AdvancedMode_HolyCircle = 40056,
        PLD_AoE_AdvancedMode_BladeOfHonor = 40057,
        PLD_ST_Interrupt = 40058,
        PLD_AoE_Interrupt = 40059,
        PLD_ST_LowBlow = 40060,
        PLD_AoE_LowBlow = 40061,
        PLD_ST_ShieldBash = 40062,
        PLD_AoE_ShieldBash = 40063,
        PLD_Mitigation_NonBoss = 40064,
        PLD_Mitigation_NonBoss_HallowedGroundEmergency = 40065,
        PLD_Mitigation_NonBoss_Sheltron = 40066,
        PLD_Mitigation_NonBoss_DivineVeil = 40067,
        PLD_Mitigation_NonBoss_HallowedGround = 40068,
        PLD_Mitigation_NonBoss_Sentinel = 40069,
        PLD_Mitigation_NonBoss_ArmsLength = 40070,
        PLD_Mitigation_NonBoss_Reprisal = 40071,
        PLD_Mitigation_NonBoss_Rampart = 40072,
        PLD_Mitigation_NonBoss_Bulwark = 40073,
        PLD_Mitigation_Boss = 40074,
        PLD_Mitigation_Boss_Sentinel = 40075,
        PLD_Mitigation_Boss_Rampart = 40076,
        PLD_Mitigation_Boss_SheltronOvercap = 40077,
        PLD_Mitigation_Boss_SheltronTankbuster = 40078,
        PLD_Mitigation_Boss_Bulwark = 40079,
        PLD_Mitigation_Boss_Reprisal = 40080,
        PLD_Mitigation_Boss_DivineVeil = 40081,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE slice of the real partial class All: Cease plus the Enums PLD's config defaults use.</summary>
    internal partial class All
    {
        public const uint Cease = 1_000_004; // mirrored from ALL.cs:19

        internal static class Enums
        {
            internal enum BossAvoidance // mirrored from ALL.cs:39-43
            {
                Off = 1,
                On = 2
            }

            internal enum PartyRequirement // mirrored from ALL.cs:48-52
            {
                No,
                Yes
            }
        }
    }

    /// <summary>FAKE of Combos/PvE/ALL/JobClasses.cs:14 Tank base (real routes through Roles/RoleImplementation).</summary>
    internal class Tank
    {
        protected Tank() { }

        public static FakeTankRole Role { get; } = new();
    }

    /// <summary>FAKE of the ITank role surface PLD touches; consts mirrored from Roles/RoleActions.cs.</summary>
    internal class FakeTankRole
    {
        public FakeTankBuffs Buffs { get; } = new();
        public FakeTankDebuffs Debuffs { get; } = new();

        public uint SecondWind => 7541;   // mirrored from RoleActions.cs Physical
        public uint ArmsLength => 7548;   // mirrored from RoleActions.cs Physical
        public uint Rampart => 7531;      // mirrored from RoleActions.cs:173
        public uint LowBlow => 7540;      // mirrored from RoleActions.cs:174
        public uint Provoke => 7533;      // mirrored from RoleActions.cs:175
        public uint Interject => 7538;    // mirrored from RoleActions.cs:176
        public uint Reprisal => 7535;     // mirrored from RoleActions.cs:177
        public uint Shirk => 7537;        // mirrored from RoleActions.cs:178

        public bool CanSecondWind(int healthpercent) => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanArmsLength() => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanArmsLength(int enemyCount, CustomComboNS.Functions.UserInt? avoidanceSetting = null) => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanArmsLength(int enemyCount, All.Enums.BossAvoidance avoidanceSetting) => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanRampart(int healthPercent = 100) => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanLowBlow() => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanProvoke() => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanInterject() => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanReprisal(int healthPercent = 101, int? enemyCount = null, bool checkTargetForDebuff = true, IGameObject? target = null) => RotationHarnessPLD.FakeGame.RoleActionReady;
        public bool CanShirk() => RotationHarnessPLD.FakeGame.RoleActionReady;
    }

    internal class FakeTankBuffs
    {
        public ushort ArmsLength => 1209; // mirrored from RoleActions.cs Physical.Buffs.ArmsLength
        public ushort Rampart => 1191;    // mirrored from RoleActions.cs:203
    }

    internal class FakeTankDebuffs
    {
        public ushort Reprisal => 1193;   // mirrored from RoleActions.cs:209
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
    /// <summary>FAKE of ALL/Items.cs: only the members the PLD opener lambdas compile against.</summary>
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
    ///     FAKE of Data/ContentCheck.cs: the ListSet enum and the two IsInConfiguredContent overloads
    ///     PLD compiles against. Offline there is no content, so the answer is always "not in it".
    /// </summary>
    public class ContentCheck
    {
        public enum ListSet { Halved, CasualVSHard, Cored, BossOnly }

        internal static bool IsInConfiguredContent(CustomComboNS.Functions.UserBoolArray configuredContent, ListSet configListSet) => false;

        internal static bool IsInConfiguredContent(CustomComboNS.Functions.UserInt configuredContent, ListSet configListSet) => false;
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

        public static implicit operator float(UserFloat o) => RotationHarnessPLD.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => RotationHarnessPLD.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => RotationHarnessPLD.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of Config.cs UserIntArray: in-memory values seeded with the default (PLD's tested path never reads it).</summary>
    internal class UserIntArray(string configName, int[]? defaults = null) : UserData(configName)
    {
        private int[] _values = (int[])(defaults ?? []).Clone();

        public int[] Default = defaults ?? [];

        public int Count => _values.Length;

        public int IndexOf(int item) => Array.IndexOf(_values, item);

        public bool Any(Func<int, bool> func) => _values.Any(func);

        public IEnumerable<int> OrderBy<TKey>(Func<int, TKey> keySelector) => _values.OrderBy(keySelector);

        public static implicit operator int[](UserIntArray o) => o._values;

        public int this[int index] => _values[index];

        public override void ResetToDefault() => _values = (int[])Default.Clone();
    }

    /// <summary>FAKE of Config.cs UserBoolArray: in-memory values seeded with the default.</summary>
    internal class UserBoolArray(string configName, bool[]? defaults = null) : UserData(configName)
    {
        private bool[] _values = (bool[])(defaults ?? []).Clone();

        public bool[] Default = defaults ?? [];

        public int Count => _values.Length;

        public bool Any(Func<bool, bool> func) => _values.Any(func);

        public bool All(Func<bool, bool> func) => _values.All(func);

        public static implicit operator bool[](UserBoolArray o) => o._values;

        public bool this[int index] => _values[index];

        public override void ResetToDefault() => _values = (bool[])Default.Clone();
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Paladin files read. Every member routes into
    ///     <see cref="RotationHarnessPLD.FakeGame"/>; defaults make a clean, weave-blocked,
    ///     everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => RotationHarnessPLD.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status ----
        public static RotationHarnessPLD.FakePlayer LocalPlayer { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static IGameObject? CurrentTarget => null;

        public static bool PlayerHasAggro => RotationHarnessPLD.FakeGame.PlayerHasAggro;

        public static bool InCombat() => RotationHarnessPLD.FakeGame.InCombat;

        public static bool InBossEncounter() => RotationHarnessPLD.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => RotationHarnessPLD.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => RotationHarnessPLD.FakeGame.HasBattleTarget;

        public static bool HasTarget() => RotationHarnessPLD.FakeGame.HasTarget;

        public static float GetTargetHPPercent() => RotationHarnessPLD.FakeGame.TargetHPPercent;

        public static float GetTargetDistance() => RotationHarnessPLD.FakeGame.TargetDistance;

        public static int PlayerHealthPercentageHp() => RotationHarnessPLD.FakeGame.PlayerHealthPercent;

        public static bool IsInParty() => RotationHarnessPLD.FakeGame.InParty;

        public static bool IsPlayerTargeted() => RotationHarnessPLD.FakeGame.IsPlayerTargeted;

        public static bool IsMoving() => RotationHarnessPLD.FakeGame.IsMoving;

        public static TimeSpan CombatEngageDuration() => RotationHarnessPLD.FakeGame.CombatEngageDuration;

        public static TimeSpan TimeStoodStill => RotationHarnessPLD.FakeGame.TimeStoodStill;

        public static TimeSpan TimeMoving => RotationHarnessPLD.FakeGame.TimeMoving;

        public static bool InMeleeRange() => RotationHarnessPLD.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !RotationHarnessPLD.FakeGame.OutOfRangeActions.Contains(actionID) &&
            RotationHarnessPLD.FakeGame.InActionRange;

        // ---- actions / cooldowns / resources ----
        public static uint OriginalHook(uint actionID) =>
            RotationHarnessPLD.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !RotationHarnessPLD.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => RotationHarnessPLD.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static uint GetResourceCost(uint actionID) => RotationHarnessPLD.FakeGame.ResourceCost;

        public static bool TraitLevelChecked(uint traitId) => RotationHarnessPLD.FakeGame.AllTraitsKnown;

        // ---- enemies / aoe ----
        public static int NumberOfEnemiesInRange(uint actionID) => RotationHarnessPLD.FakeGame.NumberOfEnemies;

        public static float GetAvgEnemyHPPercentInRange(float range) => RotationHarnessPLD.FakeGame.AvgEnemyHPPercentInRange;

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => RotationHarnessPLD.FakeGame.CanWeave;

        public static bool HasWeaved() => RotationHarnessPLD.FakeGame.HasWeaved;

        public static float ComboTimer => RotationHarnessPLD.FakeGame.ComboTimer;

        public static uint ComboAction => RotationHarnessPLD.FakeGame.ComboActionId;

        public static bool CountdownActive => RotationHarnessPLD.FakeGame.CountdownActive;

        public static float CountdownRemaining => RotationHarnessPLD.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            RotationHarnessPLD.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool JustUsedOn(uint actionID, IGameObject? target, float withinSeconds = 2f) => JustUsed(actionID, withinSeconds);

        public static bool CanStunToInterruptEnemy() => RotationHarnessPLD.FakeGame.CanStunToInterruptEnemy;

        public static bool GroupDamageIncoming() => RotationHarnessPLD.FakeGame.GroupDamageIncoming;

        public static bool HasIncomingTankBusterEffect() => RotationHarnessPLD.FakeGame.IncomingTankBuster;

        public static bool HasIncomingTankBusterEffect(out float castTimeRemaining)
        {
            castTimeRemaining = RotationHarnessPLD.FakeGame.IncomingTankBusterAge;
            return RotationHarnessPLD.FakeGame.IncomingTankBuster;
        }

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => RotationHarnessPLD.FakeGauges.Get<T>();
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
    ///     FAKE of CustomCombo/SimpleTarget.cs: only the members the PLD files reference. Every resolver
    ///     returns null offline (no object table); the If* chains in Extensions/GameObjectExtensions.cs
    ///     then yield null, which is exactly what the retarget features expect for "no target".
    /// </summary>
    public static class SimpleTarget
    {
        public static object? UiMouseOver => null;

        public static object? ModelMouseOverTarget => null;

        public static IGameObject? UIMouseOverTarget => null;

        public static IGameObject? HardTarget => null;

        public static IGameObject? TargetsTarget => null;

        public static IGameObject? LowestHPAlly => null;

        public static IGameObject? LowestHPPAlly => null;

        public static IGameObject? FurthestEnemyOver5YalmsAway => null;

        public static IGameObject? FurthestEnemyOver5YalmsAwayNotTargetingPlayer => null;

        public static IGameObject? NearestEnemyOver5YalmsAway => null;

        public static IGameObject? NearestEnemyOver5YalmsAwayNotTargetingPlayer => null;

        public static IGameObject? NearestEnemyToTarget(IGameObject? target, float maximumRangeFromPlayer = 35f) => null;

        public static IGameObject? StunnableEnemy(int reStunCheck = 3) => null;

        public static class Stack
        {
            public static IGameObject? MouseOver => null;
        }
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the PLD opener classes declare
    ///     and override (including AllowUpgradeSteps, which the VPR spike did not need). Openers are not
    ///     exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.PLD_ST_AdvancedMode_BalanceOpener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public virtual List<int> AllowUpgradeSteps { get; set; } = [];

        public bool LevelChecked =>
            RotationHarnessPLD.FakeGame.Level >= MinOpenerLevel &&
            RotationHarnessPLD.FakeGame.Level <= MaxOpenerLevel;

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
            RotationHarnessPLD.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of Extensions/UIntExtensions.cs: only ActionRange (canned; sheet lookups are out of the
    ///     tested boundary). The real Retarget extensions live in Core (faked there), not here.
    /// </summary>
    internal static class UIntExtensions
    {
        internal static float ActionRange(this uint value) => 25f;
    }

    /// <summary>
    ///     FAKE of Extensions/GameObjectExtensions.cs + the IGameObject half of StatusExtensions.cs:
    ///     only the members the PLD files reference, with the real null-safe shapes. Offline the fake
    ///     SimpleTarget never yields a target, so these only ever see null receivers.
    /// </summary>
    public static class GameObjectExtensions
    {
        extension(IGameObject? obj)
        {
            public float HPP => obj is null ? float.NaN : 100f;

            public IGameObject? IfFriendly() => obj;

            public IGameObject? IfHostile() => obj;

            public IGameObject? IfInParty() => obj;

            public IGameObject? IfNotThePlayer() => obj;

            public IGameObject? IfAlive() => obj;

            public IGameObject? IfWithinRange(float range = 25) => obj;

            public bool HasStatus(uint id, bool anyOwner = false) => false;

            public bool CanApplyStatus(uint statusId) => false;
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>
    ///     FAKE of Core/ActionRetargeting.cs UIntExtensions: the three Retarget overloads PLD compiles
    ///     against. Retargeting itself is an IPC side channel out of the tested boundary; the fake
    ///     returns the action unchanged.
    /// </summary>
    internal static class UIntExtensions
    {
        extension(uint action)
        {
            internal uint Retarget(Dalamud.Game.ClientState.Objects.Types.IGameObject? target) => action;

            internal uint Retarget(uint replaced, Dalamud.Game.ClientState.Objects.Types.IGameObject? target) => action;

            internal uint Retarget(uint[] replaced, Dalamud.Game.ClientState.Objects.Types.IGameObject? target) => action;
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial PLD class: the settings class (the real one drags ImGui/localization).
    // Defaults are mirrored one-for-one from PLD_Config.cs lines 359-452.
    internal partial class PLD
    {
        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                //Mitigations
                PLD_ST_MitOptions = new("PLD_ST_MitOptions"),
                PLD_AoE_MitOptions = new("PLD_AoE_MitOptions"),
                PLD_ST_Advanced_MitOptions = new("PLD_ST_Advanced_MitOptions"),
                PLD_AoE_Advanced_MitOptions = new("PLD_AoE_Advanced_MitOptions"),
                PLD_Mitigation_NonBoss_HallowedGround_Health = new("PLD_Mitigation_NonBoss_HallowedGround_Health", 20),
                PLD_Mitigation_NonBoss_DivineVeil_Health = new("PLD_Mitigation_NonBoss_DivineVeil_Health", 80),
                PLD_Mitigation_Boss_SheltronOvercap_Threshold = new("PLD_Mitigation_Boss_SheltronOvercap_Threshold", 100),
                PLD_Mitigation_Boss_SheltronOvercap_HealthThreshold = new("PLD_Mitigation_Boss_SheltronOvercap_HealthThreshold", 100),
                PLD_Mitigation_Boss_SheltronDelay = new("PLD_Mitigation_Boss_SheltronDelay"),

                //ST
                PLD_Balance_Content = new("PLD_Balance_Content", 1),
                PLD_SelectedOpener = new("PLD_SelectedOpener"),
                PLD_ST_AdvancedMode_BalanceOpener_Intervene = new("PLD_ST_AdvancedMode_BalanceOpener_Intervene"),
                PLD_ST_Intervene_Charges = new("PLD_ST_Intervene_Charges"),
                PLD_ST_Intervene_Movement = new("PLD_ST_Intervene_Movement"),
                PLD_ST_Intervene_Distance = new("PLD_ST_Intervene_Distance", 3),
                PLD_ST_MP_Reserve = new("PLD_ST_MP_Reserve", 1000),
                PLD_ST_FoF_BossOption = new("PLD_ST_FoF_BossOption"),
                PLD_ST_FoF_HPOption = new("PLD_ST_FoF_HPOption", 10),
                PLD_ST_ShieldLob_SubOption = new("PLD_ST_ShieldLob_SubOption"),
                PLD_ST_AdvancedMode_GoringBladePrioritize = new("PLD_ST_AdvancedMode_GoringBladePrioritize"),

                //AoE
                PLD_AoE_FoF_HPOption = new("PLD_AoE_FoF_HPOption", 25),
                PLD_AoE_FoF_BossOption = new("PLD_AoE_FoF_BossOption"),
                PLD_AoE_AdvancedMode_GoringBladePrioritize = new("PLD_AoE_AdvancedMode_GoringBladePrioritize"),
                PLD_AoE_Intervene_Charges = new("PLD_AoE_Intervene_Charges"),
                PLD_AoE_Intervene_Movement = new("PLD_AoE_Intervene_Movement"),
                PLD_AoE_Intervene_Distance = new("PLD_AoE_Intervene_Distance", 3),
                PLD_AoE_ShieldLob_SubOption = new("PLD_AoE_ShieldLob_SubOption"),
                PLD_AoE_MP_Reserve = new("PLD_AoE_MP_Reserve", 1000),

                //Standalone
                PLD_Requiescat_SubOption = new("PLD_Requiescat_SubOption"),

                //Retarget
                PLD_RetargetClemency_Health = new("PLD_RetargetClemency_Health", 30),
                PLD_RetargetShieldBash_Strength = new("PLD_RetargetShieldBash_Strength", 3),
                PLD_RetargetCover_Health = new("PLD_RetargetCover_Health", 30),
                PLD_ShieldLob_Feature_SmartTargeting = new("PLD_ShieldLob_Feature_SmartTargeting"),

                //One-Button Mitigation
                PLD_Mit_HallowedGround_Max_Health = new("PLD_Mit_HallowedGround_Max_Health", 20),
                PLD_Mit_DivineVeil_PartyRequirement = new("PLD_Mit_DivineVeil_PartyRequirement", (int)All.Enums.PartyRequirement.Yes),
                PLD_Mit_ArmsLength_Boss = new("PLD_Mit_ArmsLength_Boss", (int)All.Enums.BossAvoidance.On),
                PLD_Mit_ArmsLength_EnemyCount = new("PLD_Mit_ArmsLength_EnemyCount", 5),
                PLD_Mit_Clemency_Health = new("PLD_Mit_Clemency_Health", 40);

            public static CustomComboNS.Functions.UserFloat
                PLD_Mitigation_NonBoss_MitigationThreshold = new("PLD_Mitigation_NonBoss_MitigationThreshold", 20f),
                PLD_Mitigation_Boss_Bulwark_Threshold = new("PLD_Mitigation_Boss_Bulwark_Threshold", 80f),
                PLD_ST_InterveneTimeStill = new("PLD_ST_InterveneTimeStill", 2.5f),
                PLD_AoE_InterveneTimeStill = new("PLD_AoE_InterveneTimeStill", 2.5f);

            public static CustomComboNS.Functions.UserBool
                PLD_Opener_Potion = new("PLD_Opener_Potion"),
                PLD_Opener_PrepullBlock = new("PLD_Opener_PrepullBlock", true),
                PLD_ST_AdvancedMode_CircleOfScorn_ManualPooling = new("PLD_ST_AdvancedMode_CircleOfScorn_ManualPooling"),
                PLD_ST_AdvancedMode_SpiritsWithin_ManualPooling = new("PLD_ST_AdvancedMode_SpiritsWithin_ManualPooling"),
                PLD_ST_AdvancedMode_Intervene_ManualPooling = new("PLD_ST_AdvancedMode_Intervene_ManualPooling"),
                PLD_AoE_AdvancedMode_CircleOfScorn_ManualPooling = new("PLD_AoE_AdvancedMode_CircleOfScorn_ManualPooling"),
                PLD_AoE_AdvancedMode_SpiritsWithin_ManualPooling = new("PLD_AoE_AdvancedMode_SpiritsWithin_ManualPooling"),
                PLD_AoE_AdvancedMode_Intervene_ManualPooling = new("PLD_AoE_AdvancedMode_Intervene_ManualPooling"),
                PLD_RetargetStunLockout = new("PLD_RetargetStunLockout"),
                PLD_Mitigation_Boss_Bulwark_Align = new("PLD_Mitigation_Boss_Bulwark_Align"),
                PLD_Mitigation_Boss_Sentinel_First = new("PLD_Mitigation_Boss_Sentinel_First"),
                PLD_HolySpirit_Standalone = new("PLD_HolySpirit_Standalone"),
                PLD_HolyCircle_Standalone = new("PLD_HolyCircle_Standalone"),
                PLD_SpiritsWithin_SubOption = new("PLD_SpiritsWithin_SubOption"),
                PLD_Requiescat_SubOption_GoringBlade = new("PLD_Requiescat_SubOption_GoringBlade"),
                PLD_ShieldLob_Feature_FieldMO = new("PLD_ShieldLob_Feature_FieldMO"),
                PLD_ShieldLob_Feature_SmartTargeting_NotTargetingPlayer = new("PLD_ShieldLob_Feature_SmartTargeting_NotTargetingPlayer"),
                PLD_ShieldLob_Feature_RangeBasedTargeting = new("PLD_ShieldLob_Feature_RangeBasedTargeting"),
                PLD_ShieldLob_Feature_HolySpirit = new("PLD_ShieldLob_Feature_HolySpirit");

            public static CustomComboNS.Functions.UserIntArray
                PLD_Mit_Priorities = new("PLD_Mit_Priorities");

            public static CustomComboNS.Functions.UserBoolArray
                PLD_Mitigation_Boss_DivineVeil_Difficulty = new("PLD_Mitigation_Boss_DivineVeil_Difficulty", [true, false]),
                PLD_Mitigation_Boss_Reprisal_Difficulty = new("PLD_Mitigation_Boss_Reprisal_Difficulty", [true, false]),
                PLD_Mitigation_Boss_SheltronTankbuster_Difficulty = new("PLD_Mitigation_Boss_SheltronTankbuster_Difficulty", [true, false]),
                PLD_Mitigation_Boss_Sentinel_Difficulty = new("PLD_Mitigation_Boss_Sentinel_Difficulty", [true, false]),
                PLD_Mitigation_Boss_Rampart_Difficulty = new("PLD_Mitigation_Boss_Rampart_Difficulty", [true, false]),
                PLD_Mitigation_Boss_Bulwark_Difficulty = new("PLD_Mitigation_Boss_Bulwark_Difficulty", [true, false]),
                PLD_Mit_HallowedGround_Max_Difficulty = new("PLD_Mit_HallowedGround_Max_Difficulty", [true, false]);

            public static readonly Data.ContentCheck.ListSet
                PLD_Mit_HallowedGround_Max_DifficultyListSet = Data.ContentCheck.ListSet.CasualVSHard,
                PLD_Boss_Mit_DifficultyListSet = Data.ContentCheck.ListSet.CasualVSHard;
        }
    }
}
