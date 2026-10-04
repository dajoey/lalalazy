// Fakes.cs — the harness-owned stand-ins for everything the REAL Red Mage job files (RDM.cs,
// RDM_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary
// is deliberate (same pattern as the proven VPR spike and the nine round-6 per-job harnesses):
//   REAL  : every decision line in RDM.cs / RDM_Helper.cs (Invoke handlers, gauge props, action IDs,
//           Buffs/Traits tables, the opener tables, UseVerStone/UseVerFire/UseInstantCastST logic).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, party), the Preset enum values, the RDM Config setting
//           defaults (mirrored one-for-one from RDM_Config.cs), the caster role-action layer, the
//           vendored-ECommons surface (Player/Job/GetRole/CombatRole), the WrathOpener base,
//           content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.Objects.Types;
using System.Collections.Generic;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Red Mage members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        RDM_ST_SimpleMode = 41001,
        RDM_AoE_SimpleMode = 41002,
        RDM_ST_DPS = 41003,
        RDM_AoE_DPS = 41004,
        RDM_Balance_Opener = 41005,
        RDM_ST_Manafication = 41006,
        RDM_AoE_Manafication = 41007,
        RDM_ST_Embolden = 41008,
        RDM_AoE_Embolden = 41009,
        RDM_ST_ContreSixte = 41010,
        RDM_AoE_ContreSixte = 41011,
        RDM_ST_Fleche = 41012,
        RDM_AoE_Fleche = 41013,
        RDM_ST_Engagement = 41014,
        RDM_AoE_Engagement = 41015,
        RDM_ST_Engagement_Pooling = 41016,
        RDM_AoE_Engagement_Pooling = 41017,
        RDM_ST_Engagement_Saving = 41018,
        RDM_AoE_Engagement_Saving = 41019,
        RDM_ST_Corpsacorps = 41020,
        RDM_AoE_Corpsacorps = 41021,
        RDM_ST_Prefulgence = 41022,
        RDM_AoE_Prefulgence = 41023,
        RDM_ST_ViceOfThorns = 41024,
        RDM_AoE_ViceOfThorns = 41025,
        RDM_ST_Lucid = 41026,
        RDM_AoE_Lucid = 41027,
        RDM_ST_Acceleration = 41028,
        RDM_AoE_Acceleration = 41029,
        RDM_ST_Acceleration_Movement = 41030,
        RDM_AoE_Acceleration_Movement = 41031,
        RDM_ST_Swiftcast = 41032,
        RDM_AoE_Swiftcast = 41033,
        RDM_ST_SwiftcastMovement = 41034,
        RDM_AoE_SwiftcastMovement = 41035,
        RDM_ST_Addle = 41036,
        RDM_ST_MagickBarrier = 41037,
        RDM_ST_VerCure = 41038,
        RDM_AoE_VerCure = 41039,
        RDM_ST_HolyFlare = 41040,
        RDM_AoE_HolyFlare = 41041,
        RDM_ST_MeleeCombo = 41042,
        RDM_AoE_MeleeCombo = 41043,
        RDM_ST_MeleeCombo_GapCloser = 41044,
        RDM_AoE_MeleeCombo_GapCloser = 41045,
        RDM_ST_MeleeCombo_IncludeReprise = 41046,
        RDM_ST_MeleeCombo_IncludeRiposte = 41047,
        RDM_ST_MeleeCombo_MeleeCheck = 41048,
        RDM_AoE_MeleeCombo_Target = 41049,
        RDM_ST_ThunderAero = 41050,
        RDM_AoE_ThunderAero = 41051,
        RDM_ST_FireStone = 41052,
        RDM_RetargetVercure = 41053,
        RDM_RetargetVercure_MO = 41054,
        RDM_RetargetVercure_LowHP = 41055,
        RDM_Raise = 41056,
        RDM_Raise_Retarget = 41057,
        RDM_Raise_Vercure = 41058,
        RDM_VerAero = 41059,
        RDM_VerThunder = 41060,
        RDM_VerAero2 = 41061,
        RDM_VerThunder2 = 41062,
        RDM_Riposte = 41063,
        RDM_Riposte_GapCloser = 41064,
        RDM_Riposte_Weaves = 41065,
        RDM_Riposte_Finisher = 41066,
        RDM_Riposte_NoWaste = 41067,
        RDM_Moulinet = 41068,
        RDM_Moulinet_GapCloser = 41069,
        RDM_Moulinet_Weaves = 41070,
        RDM_Moulinet_Finisher = 41071,
        RDM_Moulinet_NoWaste = 41072,
        RDM_CorpsDisplacement = 41073,
        RDM_EmboldenProtection = 41074,
        RDM_EmboldenManafication = 41075,
        RDM_MagickProtection = 41076,
        RDM_MagickBarrierAddle = 41077,
        RDM_OGCDs = 41078,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE slice of the real partial class All (ALL.cs): only Cease, real value mirrored.</summary>
    internal partial class All
    {
        public const uint Cease = 1_000_004; // mirrored from ALL.cs
    }

    /// <summary>FAKE of ALL/JobClasses.cs Caster base (the real one routes through Roles/RoleImplementation).</summary>
    internal class Caster
    {
        protected Caster() { }

        public static FakeCasterRole Role { get; } = new();
    }

    /// <summary>
    ///     FAKE of the ICaster role surface RDM uses; consts mirrored from Roles/RoleActions.cs
    ///     (LucidDreaming 7562, Swiftcast 7561, Addle 7560; MagicBuffs.Swiftcast 167).
    /// </summary>
    internal class FakeCasterRole
    {
        public uint LucidDreaming => 7562;

        public uint Swiftcast => 7561;

        public uint Addle => 7560;

        public FakeCasterBuffs Buffs { get; } = new();

        public bool CanLucidDream(int mpThreshold) => GluttonyCombo.RotationHarness.RDM.FakeGame.RoleActionReady;

        public bool CanSwiftcast() => GluttonyCombo.RotationHarness.RDM.FakeGame.RoleActionReady;

        public bool CanAddle() => GluttonyCombo.RotationHarness.RDM.FakeGame.RoleActionReady;
    }

    internal class FakeCasterBuffs
    {
        public ushort Swiftcast => 167; // mirrored from RoleActions.cs MagicBuffs
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
    /// <summary>FAKE of ALL/Items.cs: only the members the RDM opener lists compile against.</summary>
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.RDM.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.RDM.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.RDM.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of CustomCombo/Functions/Config.cs UserIntArray (read indexer, Count, IndexOf, OrderBy).</summary>
    internal class UserIntArray(string configName, int[]? defaults = null) : UserData(configName)
    {
        public int[] Default = defaults ?? [];

        private int[] Arr
        {
            get
            {
                if (!GluttonyCombo.RotationHarness.RDM.FakeGame.IntArrayValues.TryGetValue(ConfigName, out var a))
                {
                    a = (int[])Default.Clone();
                    GluttonyCombo.RotationHarness.RDM.FakeGame.IntArrayValues[ConfigName] = a;
                }

                return a;
            }
        }

        public int Count => Arr.Length;

        public int IndexOf(int item)
        {
            var arr = Arr;
            for (var i = 0; i < arr.Length; i++)
                if (arr[i] == item)
                    return i;
            return -1;
        }

        public IEnumerable<int> OrderBy<TKey>(Func<int, TKey> keySelector) => Arr.OrderBy(keySelector);

        public int this[int index] => index < Count ? Arr[index] : 0;

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of UserBoolArray: read indexer only (the RDM code never writes one in a decision).</summary>
    internal class UserBoolArray(string configName, bool[]? defaults = null) : UserData(configName)
    {
        public bool[] Default = defaults ?? [];

        public int Count => Arr.Length;

        private bool[] Arr
        {
            get
            {
                if (!GluttonyCombo.RotationHarness.RDM.FakeGame.BoolArrayValues.TryGetValue(ConfigName, out var a))
                {
                    a = (bool[])Default.Clone();
                    GluttonyCombo.RotationHarness.RDM.FakeGame.BoolArrayValues[ConfigName] = a;
                }

                return a;
            }
        }

        public bool this[int index] => index < Count && Arr[index];

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Red Mage files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.RDM.FakeGame"/>; defaults make a clean, level-100
    ///     in-combat, boss-target, not-moving state with everything off cooldown and no buffs.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.RDM.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status ----
        public static GluttonyCombo.RotationHarness.RDM.FakePlayer LocalPlayer { get; } = new();

        public static GluttonyCombo.RotationHarness.RDM.FakePlayer CurrentTarget { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static float GetStatusEffectRemainingTime(uint statusId) => LocalPlayer.Status(statusId, anyOwner: true).RemainingTimeOrZero();

        public static bool InCombat() => GluttonyCombo.RotationHarness.RDM.FakeGame.InCombat;

        public static bool PartyInCombat() => GluttonyCombo.RotationHarness.RDM.FakeGame.PartyInCombat;

        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.RDM.FakeGame.IsMoving;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.RDM.FakeGame.InMeleeRange;

        public static float GetTargetDistance() => GluttonyCombo.RotationHarness.RDM.FakeGame.TargetDistance;

        public static float PlayerHealthPercentageHp() => GluttonyCombo.RotationHarness.RDM.FakeGame.PlayerHealthPercentageHp;

        // ---- target ----
        public static bool HasTarget() => GluttonyCombo.RotationHarness.RDM.FakeGame.HasBattleTarget;

        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.RDM.FakeGame.HasBattleTarget;

        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.RDM.FakeGame.TargetHPPercent;

        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.RDM.FakeGame.InBossEncounter;

        public static bool InActionRange(uint actionId, IGameObject? optionalTarget = null) => true;

        // ---- party ----
        public static List<GluttonyCombo.Extensions.WrathPartyMember> GetPartyMembers(bool allowCache = true) => [];

        public static int NumberOfAlliesInRange(uint actionId) => GluttonyCombo.RotationHarness.RDM.FakeGame.NumberOfAlliesInRange;

        // ---- movement ----
        public static TimeSpan TimeStoodStill => GluttonyCombo.RotationHarness.RDM.FakeGame.TimeStoodStill;

        // ---- occult (Sprint/Echo instant-cast windows): off by default ----
        public static bool HasOccultInstantCast => GluttonyCombo.RotationHarness.RDM.FakeGame.HasOccultInstantCastFlag;

        public static bool HasFreeInstantCasts => GluttonyCombo.RotationHarness.RDM.FakeGame.HasFreeInstantCastsFlag;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.RDM.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.RDM.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.RDM.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static bool HasCharges(uint actionID) => GetCooldown(actionID).RemainingCharges > 0;

        public static bool ActionsReady(uint[] actionIds) => true;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.RDM.FakeGame.AllTraitsKnown;

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.RDM.FakeGame.CanWeave;

        public static uint ComboAction => GluttonyCombo.RotationHarness.RDM.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.RDM.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.RDM.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.RDM.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.RDM.FakeGame.GroupDamageIncoming;

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase => GluttonyCombo.RotationHarness.RDM.FakeGauges.Get<T>();
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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the RDM names, as plain null targets.</summary>
    public static class SimpleTarget
    {
        public static class Stack
        {
            public static IGameObject? AllyToRaise => HardTarget;

            public static IGameObject? AllyToHeal => HardTarget;
        }

        public static IGameObject? HardTarget => null;

        public static IGameObject? UIMouseOverTarget => null;

        public static IGameObject? TargetsTarget => null;

        public static IGameObject? AnyTank => null;

        public static IGameObject? LowestHPPAlly => null;

        public static IGameObject? LowestHPPAllyIfMissingHP => null;

        public static IGameObject? Self => null;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the RDM opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.RDM_Balance_Opener;

        internal virtual Functions.UserData? ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual int OpenerStep { get; set; }

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.RDM.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.RDM.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.RDM.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE stand-in for the Wrath SDK party member (the real GetPartyMembers returns
    ///     List&lt;WrathPartyMember&gt;); only GetRole is ever called on it by the compiled RDM code.
    /// </summary>
    public class WrathPartyMember
    {
    }

    /// <summary>FAKE of Extensions/BattleCharaExtensions.cs GetRole: routed from harness state.</summary>
    public static class BattleCharaExtensions
    {
        public static ECommons.GameFunctions.CombatRole GetRole(this WrathPartyMember chara) =>
            GluttonyCombo.RotationHarness.RDM.FakeGame.PartyMemberRole;
    }

    /// <summary>FAKE of Extensions/GameObjectExtensions.cs HasStatus: reads the fake status list.</summary>
    public static class GameObjectExtensions
    {
        public static bool HasStatus(this IGameObject? obj, uint id, bool anyOwner = false) =>
            obj is not null && GluttonyCombo.RotationHarness.RDM.FakeGame.Statuses.Any(s => s.Id == id && (anyOwner || s.Own));
    }

    /// <summary>FAKE of the UIntExtensions Retarget extension members (Core/ActionRetargeting.cs) the RDM
    ///     small features compile against: registering a retarget is out of the tested boundary.</summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint action, IGameObject? target) => action;

        public static uint Retarget(this uint action, uint replaced, IGameObject? target) => action;

        public static uint Retarget(this uint action, uint replaced, Func<IGameObject?> targetResolver) => action;
    }

    /// <summary>FAKE of the SimpleTarget filter extensions: identity pass-throughs.</summary>
    public static class SimpleTargetExtensions
    {
        public static IGameObject? IfFriendly(this IGameObject? t) => t;

        public static IGameObject? IfInParty(this IGameObject? t) => t;

        public static IGameObject? IfMissingHP(this IGameObject? t) => t;

        public static IGameObject? IfAlive(this IGameObject? t) => t;
    }
}

// ======================================================================================
// FAKE of the vendored ECommons surface the RDM files touch (src/GluttonyCombo/ECommons/):
// GameHelpers.Player (Job + Object.IsCasting), ExcelServices.Job.RDM,
// GameFunctions.CombatRole. Real shapes mirrored from the vendored files.
namespace ECommons.GameFunctions
{
    public enum CombatRole
    {
        NonCombat, Tank, Healer, DPS,
    }
}

namespace ECommons.GameHelpers
{
    public static class Player
    {
        public static ECommons.ExcelServices.Job Job => ECommons.ExcelServices.Job.RDM;

        public static FakePlayerObject Object { get; } = new();
    }

    public sealed class FakePlayerObject
    {
        public bool IsCasting => false;
    }
}

namespace ECommons.ExcelServices
{
    public enum Job
    {
        RDM,
        Other,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial RDM class: the settings class (the real RDM_Config.cs drags
    // ImGui/localization into the compile; decision logic only reads the defaults). Defaults are
    // mirrored one-for-one from RDM_Config.cs "Options" region.
    internal partial class RDM
    {
        internal static class Config
        {
            public static CustomComboNS.Functions.UserBool
                RDM_ST_ThunderAero_Pull = new("RDM_ST_ThunderAero_Pull", true),
                RDM_Opener_Potion = new("RDM_Opener_Potion"),
                RDM_Opener_PrepullBlock = new("RDM_Opener_PrepullBlock", true),
                RDM_VerAero_Dynamic = new("RDM_VerAero_Dynamic", true),
                RDM_VerThunder_Dynamic = new("RDM_VerThunder_Dynamic", true),
                RDM_VerAero2_Dynamic = new("RDM_VerAero2_Dynamic", true),
                RDM_VerThunder2_Dynamic = new("RDM_VerThunder2_Dynamic", true),
                // RDM-2 opt-in options (default off), mirrored one-for-one from RDM_Config.cs.
                RDM_ST_Manafication_OnCooldown = new("RDM_ST_Manafication_OnCooldown"),
                RDM_AoE_Manafication_OnCooldown = new("RDM_AoE_Manafication_OnCooldown");

            public static CustomComboNS.Functions.UserInt
                RDM_ST_Lucid_Threshold = new("RDM_LucidDreaming_Threshold", 6500),
                RDM_AoE_Lucid_Threshold = new("RDM_AoE_Lucid_Threshold", 6500),
                RDM_BalanceOpener_Content = new("RDM_BalanceOpener_Content", 1),
                RDM_ST_Acceleration_Charges = new("RDM_ST_Acceleration_Charges", 0),
                RDM_AoE_Acceleration_Charges = new("RDM_AoE_Acceleration_Charges", 0),
                RDM_ST_Corpsacorps_Distance = new("RDM_ST_Corpsacorps_Distance", 25),
                RDM_ST_Corpsacorps_Time = new("RDM_ST_Corpsacorps_Time", 0),
                RDM_ST_GapCloseCorpsacorps_Time = new("RDM_ST_GapCloseCorpsacorps_Time", 0),
                RDM_ST_VerCureThreshold = new("RDM_ST_VerCureThreshold", 40),
                RDM_ST_VerCureEmergencyThreshold = new("RDM_ST_VerCureEmergencyThreshold", 30),
                RDM_ST_MeleeCombo_IncludeReprise_Distance = new("RDM_ST_MeleeCombo_IncludeReprise_Distance", 5),
                RDM_ST_Embolden_Threshold = new("RDM_ST_Embolden_Threshold", 20),
                RDM_ST_Embolden_SubOption = new("RDM_ST_Embolden_SubOption"),
                RDM_ST_Manafication_Threshold = new("RDM_ST_Manafication_Threshold", 20),
                RDM_ST_Manafication_SubOption = new("RDM_ST_Manafication_SubOption"),
                RDM_AoE_Corpsacorps_Distance = new("RDM_AoE_Corpsacorps_Distance", 25),
                RDM_AoE_Corpsacorps_Time = new("RDM_AoE_Corpsacorps_Time", 0),
                RDM_AoE_GapCloseCorpsacorps_Time = new("RDM_AoE_GapCloseCorpsacorps_Time", 0),
                RDM_AoE_VerCureThreshold = new("RDM_AoE_VerCureThreshold", 40),
                RDM_AoE_VerCureEmergencyThreshold = new("RDM_AoE_VerCureEmergencyThreshold", 30),
                RDM_AoE_Embolden_Threshold = new("RDM_AoE_Embolden_Threshold", 20),
                RDM_AoE_Embolden_SubOption = new("RDM_AoE_Embolden_SubOption"),
                RDM_AoE_Manafication_Threshold = new("RDM_AoE_Manafication_Threshold", 20),
                RDM_AoE_Manafication_SubOption = new("RDM_AoE_Manafication_SubOption"),
                RDM_Opener_Selection = new("RDM_Opener_Selection", 0),
                RDM_Riposte_Weaves_Options_EngagementCharges = new("RDM_Riposte_Weaves_Options_EngagementCharges", 0),
                RDM_Riposte_Weaves_Options_CorpsCharges = new("RDM_Riposte_Weaves_Options_CorpsCharges", 0),
                RDM_Riposte_Weaves_Options_Corpsacorps_Distance = new("RDM_Riposte_Weaves_Options_Corpsacorps_Distance", 25),
                RDM_Moulinet_Weaves_Options_EngagementCharges = new("RDM_Moulinet_Weaves_Options_EngagementCharges", 0),
                RDM_Moulinet_Weaves_Options_CorpsCharges = new("RDM_Moulinet_Weaves_Options_CorpsCharges", 0),
                RDM_Moulinet_Weaves_Options_Corpsacorps_Distance = new("RDM_Moulinet_Weaves_Options_Corpsacorps_Distance", 25),
                RDM_OGCDs_Options_CorpsCharges = new("RDM_OGCDs_Options_CorpsCharges", 0),
                RDM_OGCDs_Options_EngagementCharges = new("RDM_OGCDs_Options_EngagementCharges", 0),
                RDM_OGCDs_Options_Corpsacorps_Distance = new("RDM_OGCDs_Options_Corpsacorps_Distance", 25),
                RDM_RetargetVercure_Health = new("RDM_RetargetVercure_Health", 50),
                RDM_MagickProtectionDuration = new("RDM_MagickProtectionDuration"),
                RDM_AddleDuration = new("RDM_AddleDuration");

            internal static CustomComboNS.Functions.UserBoolArray
                RDM_OGCDs_Options = new("RDM_OGCDs_Options"),
                RDM_Riposte_Weaves_Options = new("RDM_Riposte_Weaves_Options"),
                RDM_Moulinet_Weaves_Options = new("RDM_Moulinet_Weaves_Options"),
                RDM_VerAero_Options = new("RDM_VerAero_Options"),
                RDM_VerThunder_Options = new("RDM_VerThunder_Options"),
                RDM_VerAero2_Options = new("RDM_VerAero2_Options"),
                RDM_VerThunder2_Options = new("RDM_VerThunder2_Options");
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>Namespace stub: RDM.cs has `using GluttonyCombo.Core;`; no member is used.</summary>
    internal static class HarnessCoreNamespaceStub;
}

namespace GluttonyCombo.AutoRotation
{
    /// <summary>Namespace stub: RDM.cs has `using GluttonyCombo.AutoRotation;`; no member is used.</summary>
    internal static class HarnessAutoRotationNamespaceStub;
}

namespace GluttonyCombo.Combos.PvE.Enums
{
    /// <summary>Namespace stub: RDM.cs has `using GluttonyCombo.Combos.PvE.Enums;`; no member is used.</summary>
    internal static class HarnessPvEEnumsNamespaceStub;
}
