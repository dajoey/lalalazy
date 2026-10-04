// Fakes.cs — the harness-owned stand-ins for everything the REAL Sage job files (SGE.cs,
// SGE_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary:
//   REAL  : every decision line in SGE.cs / SGE_Helper.cs (Invoke branches, the Eukrasian Dosis
//           refresh gate, the Phlegma charge rules, gauge props, action IDs, Buffs tables, the heal
//           priority logic, the openers' step tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           charges, statuses, gauges, target, combat, movement), the Preset enum values, the SGE
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
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Sage members the compiled job
    ///     files reference. Values are NOT the production values (they never matter to decision logic —
    ///     presets are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        SGE_ST_Simple_DPS = 41001,
        SGE_AoE_Simple_DPS = 41002,
        SGE_ST_Advanced_DPS = 41003,
        SGE_AoE_Advanced_DPS = 41004,
        SGE_ST_Simple_Heal = 41005,
        SGE_AoE_Simple_Heal = 41006,
        SGE_ST_Advanced_Heal = 41007,
        SGE_AoE_Advanced_Heal = 41008,
        SGE_ST_Adv_DPS_Kardia = 41009,
        SGE_ST_Adv_DPS_Opener = 41010,
        SGE_ST_Adv_DPS_AddersgallProtect = 41011,
        SGE_ST_Adv_DPS_Psyche = 41012,
        SGE_ST_Adv_DPS_Lucid = 41013,
        SGE_ST_Adv_DPS_Rhizo = 41014,
        SGE_ST_Adv_DPS_Soteria = 41015,
        SGE_ST_Adv_DPS_EDosis = 41016,
        SGE_ST_Adv_DPS_Phlegma = 41017,
        SGE_ST_Adv_DPS_Movement = 41018,
        SGE_AoE_Adv_DPS_AddersgallProtect = 41019,
        SGE_AoE_Adv_DPS_Psyche = 41020,
        SGE_AoE_Adv_DPS_Lucid = 41021,
        SGE_AoE_Adv_DPS_Rhizo = 41022,
        SGE_AoE_Adv_DPS_Soteria = 41023,
        SGE_AoE_Adv_DPS_EDyskrasia = 41024,
        SGE_AoE_Adv_DPS_Phlegma = 41025,
        SGE_AoE_Adv_DPS_Toxikon = 41026,
        SGE_AoE_Adv_DPS_Pneuma = 41027,
        SGE_ST_Adv_Heal_Kardia = 41028,
        SGE_ST_Adv_Heal_EDiagnosis = 41029,
        SGE_ST_Adv_Heal_Esuna = 41030,
        SGE_ST_Adv_Heal_Lucid = 41031,
        SGE_ST_Adv_Heal_Rhizomata = 41032,
        SGE_ST_Adv_Heal_Soteria = 41070,
        SGE_ST_Adv_Heal_Zoe = 41071,
        SGE_ST_Adv_Heal_Pepsis = 41072,
        SGE_ST_Adv_Heal_Taurochole = 41073,
        SGE_ST_Adv_Heal_Haima = 41074,
        SGE_ST_Adv_Heal_Krasis = 41075,
        SGE_ST_Adv_Heal_Druochole = 41076,
        SGE_ST_Adv_Heal_Kerachole = 41077,
        SGE_ST_Adv_Heal_Physis = 41078,
        SGE_ST_Adv_Heal_Panhaima = 41079,
        SGE_ST_Adv_Heal_Holos = 41080,
        SGE_AoE_Adv_Heal_Lucid = 41033,
        SGE_AoE_Adv_Heal_Rhizomata = 41034,
        SGE_AoE_Adv_Heal_Kerachole = 41035,
        SGE_AoE_Adv_Heal_Ixochole = 41036,
        SGE_AoE_Adv_Heal_Physis = 41037,
        SGE_AoE_Adv_Heal_Holos = 41038,
        SGE_AoE_Adv_Heal_Panhaima = 41039,
        SGE_AoE_Adv_Heal_Pepsis = 41040,
        SGE_AoE_Adv_Heal_Philosophia = 41041,
        SGE_AoE_Adv_Heal_Zoe = 41042,
        SGE_AoE_Adv_Heal_EPrognosis = 41043,
        SGE_Raidwide_Kerachole = 41044,
        SGE_Raidwide_Holos = 41045,
        SGE_Raidwide_EPrognosis = 41046,
        SGE_OverProtect = 41047,
        SGE_OverProtect_Kerachole = 41048,
        SGE_OverProtect_SacredSoil = 41049,
        SGE_OverProtect_Panhaima = 41050,
        SGE_OverProtect_Philosophia = 41051,
        SGE_Raise = 41052,
        SGE_Raise_Retarget = 41053,
        SGE_ZoePneuma = 41054,
        SGE_Rhizo = 41055,
        SGE_Eukrasia = 41056,
        SGE_TauroDruo = 41057,
        SGE_Kardia = 41058,
        SGE_Mit_ST = 41059,
        SGE_Mit_AoE = 41060,
        SGE_Retarget = 41061,
        SGE_Retarget_Diagnosis = 41062,
        SGE_Retarget_EukrasianDiagnosis = 41063,
        SGE_Retarget_Haima = 41064,
        SGE_Retarget_Druochole = 41065,
        SGE_Retarget_Taurochole = 41066,
        SGE_Retarget_Krasis = 41067,
        SGE_Retarget_Kardia = 41068,
        SGE_Retarget_Icarus = 41069,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>
    ///     FAKE of Combos/PvE/ALL/Bursting.cs: only the member the SGE files read after the SGE-1/
    ///     SGE-2 changes (<c>Bursting.PartyIsBursting</c>). The real detector's buff-scan logic is
    ///     outside the tested boundary; here it is a plain case-set flag.
    /// </summary>
    public class Bursting
    {
        public static bool PartyIsBursting => GluttonyCombo.RotationHarness.SGE.FakeGame.PartyIsBurstingFlag;
    }

    /// <summary>FAKE slice of the real partial class All (ALL.cs): the native action ids SGE uses.</summary>
    internal partial class All
    {
        public const uint SingleTargetDPS = 1_000_000; // mirrored from ALL.cs
        public const uint AoEDPS = 1_000_001;          // mirrored from ALL.cs
        public const uint Cease = 1_000_004;           // mirrored from ALL.cs
    }

    /// <summary>FAKE of ALL/JobClasses.cs Healer base: only the Role surface SGE touches.</summary>
    internal class Healer
    {
        protected Healer() { }

        public static FakeHealerRole Role { get; } = new();
    }

    /// <summary>FAKE of the IHealer role surface SGE uses; consts mirrored from Roles/RoleActions.cs.</summary>
    internal class FakeHealerRole
    {
        public uint LucidDreaming => 7562;
        public uint Swiftcast => 7561;
        public uint Esuna => 7568;

        public bool CanLucidDream(int mpThreshold, bool weave = true) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.CanLucidDream;
    }

    /// <summary>
    ///     FAKE of the Scholar class SGE's shield checks compile against: only the members the Sage
    ///     files reference (Galvanize/SacredSoil buff ids and the SacredSoil/Consolation action ids).
    /// </summary>
    internal partial class SCH
    {
        internal static class Buffs
        {
            internal const ushort Galvanize = 297;
            internal const ushort SacredSoil = 297;
        }

        internal const uint SacredSoil = 3604;
        internal const uint Consolation = 24298;
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
    /// <summary>FAKE of ALL/Items.cs: only the members the SGE opener lambdas compile against.</summary>
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
    ///     FAKE of AutoRotation/AutoRotationController.cs: only the raidwide-mit/shield bookkeeping
    ///     SGE_Helper's Raidwide* helpers read. Defaults true = "already used" => helpers decline.
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
    /// <summary>FAKE of Data/ActionWatching.cs: only the GCD counter (kept for shape parity).</summary>
    internal static class ActionWatching
    {
        public static int NumberOfGcdsUsed => GluttonyCombo.RotationHarness.SGE.FakeGame.NumberOfGcdsUsed;
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.SGE.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.SGE.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.SGE.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of Config.cs UserIntArray (Count/IndexOf/Any/OrderBy/indexer, defaults mirrored).</summary>
    internal class UserIntArray(string configName, int[]? defaults = null) : UserData(configName)
    {
        public int[] Default = defaults ?? [];

        private int[] Arr => GluttonyCombo.RotationHarness.SGE.FakeGame.GetIntArray(ConfigName, Default);

        public int Count => Arr.Length;

        public int IndexOf(int item) => System.Array.IndexOf(Arr, item);

        public bool Any(Func<int, bool> func) => Arr.Any(func);

        public IEnumerable<int> OrderBy<TKey>(Func<int, TKey> keySelector) => Arr.OrderBy(keySelector);

        public static implicit operator int[](UserIntArray o) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.GetIntArray(o.ConfigName, o.Default);

        public int this[int index] => index < Arr.Length ? Arr[index] : 0;

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of Config.cs UserBoolArray (Count/Any/All/indexer, out-of-range reads false).</summary>
    internal class UserBoolArray(string configName, bool[]? defaults = null) : UserData(configName)
    {
        public bool[] Default = defaults ?? [];

        private bool[] Arr => GluttonyCombo.RotationHarness.SGE.FakeGame.GetBoolArray(ConfigName, Default);

        public int Count => Arr.Length;

        public bool Any(Func<bool, bool> func) => Arr.Any(func);

        public bool All(Func<bool, bool> func) => Arr.All(func);

        public static implicit operator bool[](UserBoolArray o) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.GetBoolArray(o.ConfigName, o.Default);

        public bool this[int index] => index < Arr.Length && Arr[index];

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Sage files read. Every member routes
    ///     into <see cref="GluttonyCombo.RotationHarness.SGE.FakeGame"/>; defaults make a clean, weaving-open,
    ///     standing-still, everything-ready level-100 state.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.SGE.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status ----
        public static GluttonyCombo.RotationHarness.SGE.FakePlayer LocalPlayer { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static bool InCombat() => GluttonyCombo.RotationHarness.SGE.FakeGame.InCombat;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.SGE.FakeGame.InBossEncounter;

        public static bool TargetIsBoss() => GluttonyCombo.RotationHarness.SGE.FakeGame.TargetIsBoss;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.SGE.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.SGE.FakeGame.TargetHPPercent;

        public static float GetTargetHPPercent(IGameObject? optionalTarget = null, bool includeShield = false, bool forceUsePending = false) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.TargetHPPercent;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.SGE.FakeGame.InMeleeRange;

        public static bool InActionRange(uint actionID) =>
            !GluttonyCombo.RotationHarness.SGE.FakeGame.OutOfRangeActions.Contains(actionID) &&
            GluttonyCombo.RotationHarness.SGE.FakeGame.InActionRange;

        public static bool InActionRange(uint actionID, IGameObject? target) => InActionRange(actionID);

        // ---- movement / party / combat-state ----
        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.SGE.FakeGame.IsMovingFlag;

        public static System.TimeSpan TimeStoodStill => System.TimeSpan.FromSeconds(GluttonyCombo.RotationHarness.SGE.FakeGame.TimeStoodStillSeconds);

        public static bool PartyInCombat() => GluttonyCombo.RotationHarness.SGE.FakeGame.PartyInCombatFlag;

        public static bool IsInParty(int partySize = 2) => GluttonyCombo.RotationHarness.SGE.FakeGame.IsInPartyFlag;

        public static List<GluttonyCombo.Combos.PvE.WrathPartyMember> GetPartyMembers(bool allowCache = true) => [];

        public static float GetPartyAvgHPPercent() => GluttonyCombo.RotationHarness.SGE.FakeGame.PartyAvgHP;

        public static float GetPartyBuffPercent(ushort buff) => 0f;

        public static bool HasOrExpectsOccultInstantCast => GluttonyCombo.RotationHarness.SGE.FakeGame.OccultInstantCast;

        public static bool HasOrExpectsOccultDualcast => GluttonyCombo.RotationHarness.SGE.FakeGame.OccultDualcast;

        // Mirrors the shipped Action.cs wiring (1.0.4.278): incoming-damage detection is
        // combat-only, pinned by the REAL OutOfCombatGate.MayDetectIncomingDamage linked into
        // this harness (see the csproj). The flag below stands for RaidwideCasting /
        // CheckForSharedDamageEffect seeing something; whether it may report at all is the
        // linked gate's answer, not the fake's.
        public static bool GroupDamageIncoming() =>
            GluttonyCombo.AutoRotation.OutOfCombatGate.MayDetectIncomingDamage(GluttonyCombo.RotationHarness.SGE.FakeGame.InCombat) &&
            GluttonyCombo.RotationHarness.SGE.FakeGame.GroupDamageIncoming;

        public static bool GroupDamageIncoming(float? maxTimeRemaining = null) => GroupDamageIncoming();

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.SGE.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.SGE.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static float GetCooldownElapsed(uint actionID) => GetCooldown(actionID).CooldownElapsed;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOriginal(uint actionID) => OriginalHook(actionID) == actionID;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).HasCharges;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static uint GetMaxCharges(uint actionID) => GetCooldown(actionID).MaxCharges;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.SGE.FakeGame.AllTraitsKnown;

        public static IEnumerable<IGameObject> EnemiesInRange(uint actionId) =>
            GluttonyCombo.RotationHarness.SGE.FakeBattleChara.Enemies(
            GluttonyCombo.RotationHarness.SGE.FakeGame.EnemiesInRangeCount);

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.SGE.FakeGame.CanWeave;

        public static float ComboTimer => GluttonyCombo.RotationHarness.SGE.FakeGame.ComboTimer;

        public static uint ComboAction => GluttonyCombo.RotationHarness.SGE.FakeGame.ComboActionId;

        public static IBattleChara? CurrentTarget =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.HardTargetObject as IBattleChara;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.SGE.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.SGE.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool JustUsedOn(uint actionID, IGameObject? target, float variance = 3f) =>
            target is not null && JustUsed(actionID, variance);

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase =>
            GluttonyCombo.RotationHarness.SGE.FakeGauges.Get<T>();

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
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE of the WrathPartyMember shape kept for parity with the other harnesses.</summary>
    public class WrathPartyMember
    {
        public IBattleChara? BattleChara;
        public ClassJob? RealJob;
        public ulong GameObjectId;
        public uint CurrentHp;
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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the members the SGE files compile against.</summary>
    internal static class SimpleTarget
    {
        public static IPlayerCharacter? Self => null;

        public static IGameObject? HardTarget => GluttonyCombo.RotationHarness.SGE.FakeGame.HardTargetObject;

        public static IGameObject? UIMouseOverTarget => GluttonyCombo.RotationHarness.SGE.FakeGame.UiMouseOver;

        public static IGameObject? ModelMouseOverTarget => null;

        public static IBattleChara? FocusTarget => null;

        public static IBattleChara? AnyEnemy => GluttonyCombo.RotationHarness.SGE.FakeGame.HardTargetObject as IBattleChara;

        public static IBattleChara? AnyTank => null;

        public static IBattleChara? AnyLivingTank => null;

        public static IBattleChara? LowestHPPAlly => null;

        public static IBattleChara? DottableEnemy
        (uint dotAction,
            ushort dotDebuff,
            int minHPPercent = 10,
            float reapplyThreshold = 1,
            int maxNumberOfEnemiesInRange = 3) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.DottableEnemyPresent
                ? GluttonyCombo.RotationHarness.SGE.FakeBattleChara.Enemy
                : null;

        public static IBattleChara? DottableEnemy
        (uint dotAction,
            ushort dotDebuff,
            Func<IBattleChara?, int> minHPPercent,
            float reapplyThreshold = 1,
            int maxNumberOfEnemiesInRange = 3) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.DottableEnemyPresent
                ? GluttonyCombo.RotationHarness.SGE.FakeBattleChara.Enemy
                : null;

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
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the SGE opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.SGE_ST_Adv_DPS_Opener;

        internal virtual Functions.UserData? ContentCheckConfig => null;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.SGE.FakeGame.Level <= MaxOpenerLevel;

        internal bool FullOpener(ref uint actionID) => false;

        public static WrathOpener Dummy { get; } = new();
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>FAKE of Core/ActionRetargeting.cs: only the attribute (kept for shape parity).</summary>
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
            GluttonyCombo.RotationHarness.SGE.FakeGame.OneButtonRotation;

        public static bool CustomActionEnabled(CustomActionType type) =>
            GluttonyCombo.RotationHarness.SGE.FakeGame.CustomActionEnabled;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of the UIntExtensions Retarget surface the SGE files compile against (the real ones
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
    ///     FAKE of Extensions/GameObjectExtensions.cs: only the members the SGE files compile against.
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

            public bool IsInParty() => GluttonyCombo.RotationHarness.SGE.FakeGame.IsInPartyFlag;

            public bool IsNotThePlayer() => true;

            public bool IsBoss() => GluttonyCombo.RotationHarness.SGE.FakeGame.TargetIsBoss;

            public bool HasStatus(uint id, bool anyOwner = false) => false;

            public IStatus? Status(uint id, bool anyOwner = false) =>
                GluttonyCombo.RotationHarness.SGE.FakeGame.TargetStatuses.TryGetValue((ushort)id, out var ts) ? ts : null;

            public bool CanApplyStatus(ushort statusId) => GluttonyCombo.RotationHarness.SGE.FakeGame.TargetCanApplyStatus;

            public IGameObject? IfHostile() => obj is not null && GluttonyCombo.RotationHarness.SGE.FakeGame.HasBattleTarget ? obj : null;

            public IGameObject? IfFriendly() => obj;

            public IGameObject? IfCanUseOn(uint actionId) => obj;

            public IGameObject? IfWithinRange(float range) => obj;

            public IGameObject? IfMissingHP(float missingHpp = 99) => obj;
        }
    }

    /// <summary>FAKE of Extensions/StatusExtensions.cs: the two remaining-time members SGE reads.</summary>
    public static class StatusExtensions
    {
        public static float RemainingTimeOrZero(this IStatus? status, bool checkAnimationLock = true) =>
            status?.RemainingTime ?? 0f;

        public static float RemainingTimeOrNaN(this IStatus? status, bool checkAnimationLock = true) =>
            status?.RemainingTime ?? float.NaN;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial SGE class: the settings class (the real SGE_Config.cs Draw drags in
    // ImGui/localization; only the fields are mirrored, one-for-one with their shipped defaults).
    internal partial class SGE
    {
        internal static class Config
        {
            public static CustomComboNS.Functions.UserBool
                SGE_Opener_Potion = new("SGE_Opener_Potion"),
                SGE_Opener_PrepullBlock = new("SGE_Opener_PrepullBlock", true),
                SGE_ST_Adv_DPS_EDosis_TwoTarget = new("SGE_ST_DPS_EDosis_TwoTarget", true),
                SGE_ST_Adv_DPS_EukrasianDosisUptime_BurstRefresh = new("SGE_ST_DPS_EukrasianDosisUptime_BurstRefresh"),
                SGE_ST_Adv_DPS_Phlegma_Burst = new("SGE_ST_DPS_Phlegma_Burst", true),
                SGE_ST_Adv_DPS_Phlegma_PartyBurst = new("SGE_ST_DPS_Phlegma_PartyBurst");

            public static CustomComboNS.Functions.UserBoolArray
                SGE_ST_Adv_DPS_Movement = new("SGE_ST_DPS_Movement", [true, true, true]);

            public static CustomComboNS.Functions.UserInt
                SGE_ST_Adv_DPS_Advanced = new("SGE_ST_DPS_Advanced"),
                SGE_Eukrasia_Mode = new("SGE_Eukrasia_Mode", 2),
                SGE_SelectedOpener = new("SGE_SelectedOpener"),
                SGE_ST_Adv_DPS_Lucid = new("SGE_ST_DPS_Lucid", 6500),
                SGE_ST_Adv_DPS_Rhizo = new("SGE_ST_DPS_Rhizo", 1),
                SGE_ST_Adv_DPS_Phlegma = new("SGE_ST_DPS_Phlegma"),
                SGE_ST_Adv_DPS_EukrasianDosisBossOption = new("SGE_ST_DPS_EukrasianDosisBossOption"),
                SGE_ST_Adv_DPS_EukrasianDosisBossAddsOption = new("SGE_ST_DPS_EukrasianDosisBossAddsOption", 100),
                SGE_ST_Adv_DPS_EukrasianDosisTrashOption = new("SGE_ST_DPS_EukrasianDosisTrashOption", 50),
                SGE_ST_Adv_DPS_AddersgallProtect = new("SGE_ST_DPS_AddersgallProtect", 3),
                SGE_AoE_Adv_DPS_Lucid = new("SGE_AoE_Phlegma_Lucid", 6500),
                SGE_AoE_Adv_DPS_Rhizo = new("SGE_AoE_DPS_Rhizo", 1),
                SGE_AoE_Adv_DPS_AddersgallProtect = new("SGE_AoE_DPS_AddersgallProtect", 3),
                SGE_AoE_Adv_DPS_PneumaBossOption = new("SGE_AoE_DPS_Pneuma_SubOption", 1),
                SGE_Balance_Content = new("SGE_Balance_Content", 1);

            public static CustomComboNS.Functions.UserFloat
                SGE_ST_Adv_DPS_EukrasianDosisUptime_Threshold = new("SGE_ST_DPS_EukrasianDosisUptime_Threshold", 5.0f);

            public static CustomComboNS.Functions.UserIntArray
                SGE_ST_Adv_DPS_Movement_Priority = new("SGE_ST_Movement_Priority");

            public static CustomComboNS.Functions.UserBool
                SGE_ST_Adv_Heal_IncludeShields = new("SGE_ST_Heal_IncludeShields", true),
                SGE_ST_Adv_Heal_KeracholeBossOption = new("SGE_ST_Heal_KeracholeBossOption", true),
                SGE_ST_Adv_Heal_PanhaimaBossOption = new("SGE_ST_Heal_PanhaimaBossOption", true),
                SGE_ST_Adv_Heal_PhysisBossOption = new("SGE_ST_Heal_PhysisBossOption", true),
                SGE_ST_Adv_Heal_HolosBossOption = new("SGE_ST_Heal_HolosBossOption", true),
                SGE_ST_Adv_Heal_HaimaBossOption = new("SGE_ST_Heal_HaimaBossOption"),
                SGE_ST_Adv_Heal_KrasisBossOption = new("SGE_ST_Heal_KrasisBossOption"),
                SGE_ST_Adv_Heal_Haima_TankOnly = new("SGE_ST_Heal_Haima_TankOnly", true),
                SGE_ST_Adv_Heal_Krasis_TankOnly = new("SGE_ST_Heal_Krasis_TankOnly", true),
                SGE_ST_Adv_Heal_Taurochole_TankOnly = new("SGE_ST_Heal_Taurochole_TankOnly", true),
                SGE_AoE_Adv_Heal_KeracholeTrait = new("SGE_AoE_Heal_KeracholeTrait", true);

            public static CustomComboNS.Functions.UserInt
                SGE_Heal_HoldAddersgall = new("SGE_Heal_HoldAddersgall", 1),
                SGE_ST_Adv_Heal_LucidOption = new("SGE_ST_Heal_LucidOption", 6500),
                SGE_ST_Adv_Heal_Zoe = new("SGE_ST_Heal_Zoe", 40),
                SGE_ST_Adv_Heal_Haima = new("SGE_ST_Heal_Haima", 45),
                SGE_ST_Adv_Heal_Krasis = new("SGE_ST_Heal_Krasis", 70),
                SGE_ST_Adv_Heal_Pepsis = new("SGE_ST_Heal_Pepsis", 55),
                SGE_ST_Adv_Heal_Soteria = new("SGE_ST_Heal_Soteria", 70),
                SGE_ST_Adv_Heal_EDiagnosisHP = new("SGE_ST_Heal_EDiagnosisHP", 70),
                SGE_ST_Adv_Heal_Druochole = new("SGE_ST_Heal_Druochole", 55),
                SGE_ST_Adv_Heal_Taurochole = new("SGE_ST_Heal_Taurochole", 50),
                SGE_ST_Adv_Heal_KeracholeHP = new("SGE_ST_Heal_KeracholeHP", 70),
                SGE_ST_Adv_Heal_PhysisHP = new("SGE_ST_Heal_PhysisHP", 70),
                SGE_ST_Adv_Heal_PanhaimaHP = new("SGE_ST_Heal_PanhaimaHP", 55),
                SGE_ST_Adv_Heal_HolosHP = new("SGE_ST_Heal_HolosHP", 55),
                SGE_ST_Adv_Heal_Esuna = new("SGE_ST_Heal_Esuna", 40),
                SGE_AoE_Adv_Heal_LucidOption = new("SGE_AoE_Heal_LucidOption", 6500),
                SGE_AoE_Adv_Heal_ZoeOption = new("SGE_AoE_Heal_PneumaOption", 45),
                SGE_AoE_Adv_Heal_PhysisOption = new("SGE_AoE_Heal_PhysisOption", 80),
                SGE_AoE_Adv_Heal_PhilosophiaOption = new("SGE_AoE_Heal_PhilosophiaOption", 60),
                SGE_AoE_Adv_Heal_PepsisOption = new("SGE_AoE_Heal_PepsisOption", 60),
                SGE_AoE_Adv_Heal_PanhaimaOption = new("SGE_AoE_Heal_PanhaimaOption", 55),
                SGE_AoE_Adv_Heal_KeracholeOption = new("SGE_AoE_Heal_KeracholeOption", 80),
                SGE_AoE_Adv_Heal_IxocholeOption = new("SGE_AoE_Heal_IxocholeOption", 70),
                SGE_AoE_Adv_Heal_HolosOption = new("SGE_AoE_Heal_HolosOption", 65),
                SGE_AoE_Adv_Heal_EPrognosisOption = new("SGE_AoE_Heal_EPrognosisOption", 50),
                SGE_Raidwide_HolosOption = new("SGE_Raidwide_HolosOption", 80),
                SGE_Mit_ST_TaurocholeThreshold = new("SGE_Mit_ST_TaurocholeThreshold", 100),
                SGE_Mit_AoE_PrognosisOption = new("SGE_Mit_AoE_PrognosisOption");

            public static CustomComboNS.Functions.UserIntArray
                SGE_ST_Heals_Priority = new("SGE_ST_Heals_Priority", [7, 10, 11, 5, 6, 1, 8, 12, 3, 2, 9, 4]),
                SGE_AoE_Heals_Priority = new("SGE_AoE_Heals_Priority", [2, 6, 1, 3, 5, 8, 4, 7, 9]);

            public static CustomComboNS.Functions.UserBoolArray
                SGE_ST_Adv_Heal_EDiagnosisOpts = new("SGE_ST_Heal_EDiagnosisOpts"),
                SGE_ST_Adv_Heal_PanhaimaOpts = new("SGE_ST_Heal_PanhaimaOpts"),
                SGE_Mit_ST_Options = new("SGE_Mit_ST_Options"),
                SGE_Mit_AoE_Options = new("SGE_Mit_AoE_Options");
        }
    }
}
