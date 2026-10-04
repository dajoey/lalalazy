// Fakes.cs — the harness-owned stand-ins for everything the REAL Summoner job files (SMN.cs,
// SMN_Helper.cs, compiled unchanged from the pinned revision) reach outside themselves. The boundary
// is deliberate (same pattern as the proven VPR spike on rot/harness-spike):
//   REAL  : every decision line in SMN.cs / SMN_Helper.cs (Invoke, TryOGCDSpells/TryMitigation/
//           TrySummonSpells, gauge props, action IDs, Buffs/Traits tables, the opener tables).
//   FAKE  : the game + Dalamud boundary the decisions READ through — CustomComboFunctions (cooldowns,
//           statuses, gauges, target, combat, pet), the Preset enum values, the SMN Config setting
//           defaults (mirrored one-for-one from SMN_Config.cs), the caster role-action layer, the
//           WrathOpener base, content/IPC side channels.
// The fake layer mirrors real accessibility and signatures so the job files compile byte-identical.

using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using System.Collections.Generic;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE of the 10k-line CustomComboPreset enum: only the Summoner members the compiled job files
    ///     reference. Values are NOT the production values (they never matter to decision logic — presets
    ///     are compared by identity / looked up in the enabled set). Do not ship.
    /// </summary>
    public enum Preset
    {
        SMN_ST_Simple_Combo = 40001,
        SMN_AoE_Simple_Combo = 40002,
        SMN_ST_Advanced_Combo = 40003,
        SMN_AoE_Advanced_Combo = 40004,
        SMN_ST_Advanced_Combo_Balance_Opener = 40005,
        SMN_ST_Advanced_Combo_DemiSummons = 40006,
        SMN_AoE_Advanced_Combo_DemiSummons = 40007,
        SMN_ST_Advanced_Combo_DemiSummons_Attacks = 40008,
        SMN_AoE_Advanced_Combo_DemiSummons_Attacks = 40009,
        SMN_ST_Advanced_Combo_DemiSummons_Rekindle = 40010,
        SMN_AoE_Advanced_Combo_DemiSummons_Rekindle = 40011,
        SMN_ST_Advanced_Combo_DemiSummons_Rekindle_Retarget = 40012,
        SMN_AoE_Advanced_Combo_DemiSummons_Rekindle_Retarget = 40013,
        SMN_ST_Advanced_Combo_DemiSummons_LuxSolaris = 40014,
        SMN_AoE_Advanced_Combo_DemiSummons_LuxSolaris = 40015,
        SMN_ST_Advanced_Combo_SearingLight = 40016,
        SMN_AoE_Advanced_Combo_SearingLight = 40017,
        SMN_ST_Advanced_Combo_SearingLight_Burst = 40018,
        SMN_AoE_Advanced_Combo_SearingLight_Burst = 40019,
        SMN_ST_Advanced_Combo_SearingFlash = 40020,
        SMN_AoE_Advanced_Combo_SearingFlash = 40021,
        SMN_ST_Advanced_Combo_EDFester = 40022,
        SMN_AoE_Advanced_Combo_ESPainflare = 40023,
        SMN_ST_Advanced_Combo_oGCDPooling = 40024,
        SMN_AoE_Advanced_Combo_oGCDPooling = 40025,
        SMN_ST_Advanced_Combo_Lucid = 40026,
        SMN_AoE_Advanced_Combo_Lucid = 40027,
        SMN_ST_Advanced_Combo_Radiant = 40028,
        SMN_AoE_Advanced_Combo_Radiant = 40029,
        SMN_ST_Advanced_Combo_RadiantMaintain = 40030,
        SMN_AoE_Advanced_Combo_RadiantMaintain = 40031,
        SMN_ST_Advanced_Combo_Addle = 40032,
        SMN_ST_Advanced_Combo_Egi_AstralFlow = 40033,
        SMN_AoE_Advanced_Combo_Egi_AstralFlow = 40034,
        SMN_ST_Advanced_Combo_EgiSummons_Attacks = 40035,
        SMN_AoE_Advanced_Combo_EgiSummons_Attacks = 40036,
        SMN_ST_Advanced_Combo_DemiEgiMenu_SwiftcastEgi = 40037,
        SMN_AoE_Advanced_Combo_DemiEgiMenu_SwiftcastEgi = 40038,
        SMN_ST_Advanced_Combo_Titan = 40039,
        SMN_ST_Advanced_Combo_Garuda = 40040,
        SMN_ST_Advanced_Combo_Ifrit = 40041,
        SMN_AoE_Advanced_Combo_Titan = 40042,
        SMN_AoE_Advanced_Combo_Garuda = 40043,
        SMN_AoE_Advanced_Combo_Ifrit = 40044,
        SMN_ST_Advanced_Combo_Ruin4 = 40045,
        SMN_AoE_Advanced_Combo_Ruin4 = 40046,
        SMN_ST_Ruin3_Emerald_Ruin3 = 40047,
        SMN_Raise = 40048,
        SMN_Raise_Retarget = 40049,
        SMN_Searing = 40050,
        SMN_Rekindle = 40051,
        SMN_RuinMobility = 40052,
        SMN_EDFester = 40053,
        SMN_EDFester_Ruin4 = 40054,
        SMN_ESPainflare = 40055,
        SMN_ESPainflare_Ruin4 = 40056,
        SMN_CarbuncleReminder = 40057,
        SMN_Egi_AstralFlow = 40058,
        SMN_DemiAbilities = 40059,
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
    ///     FAKE of the ICaster role surface SMN uses; consts mirrored from Roles/RoleActions.cs
    ///     (LucidDreaming 7562, Swiftcast 7561, Addle 7560; MagicBuffs.Swiftcast 167).
    /// </summary>
    internal class FakeCasterRole
    {
        public uint LucidDreaming => 7562;
        public uint Swiftcast => 7561;
        public uint Addle => 7560;

        public FakeCasterBuffs Buffs { get; } = new();

        public bool CanLucidDream(int mpThreshold) => GluttonyCombo.RotationHarness.SMN.FakeGame.RoleActionReady;

        public bool CanSwiftcast() => GluttonyCombo.RotationHarness.SMN.FakeGame.RoleActionReady;

        public bool CanAddle() => GluttonyCombo.RotationHarness.SMN.FakeGame.RoleActionReady;
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
    /// <summary>FAKE of ALL/Items.cs: only the two members the SMN opener lambdas compile against.</summary>
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

        public static implicit operator float(UserFloat o) => GluttonyCombo.RotationHarness.SMN.FakeGame.GetFloat(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.RotationHarness.SMN.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => GluttonyCombo.RotationHarness.SMN.FakeGame.GetBool(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of CustomCombo/Functions/Config.cs UserIntArray: only the members the compiled SMN code
    ///     uses (Count, IndexOf, OrderBy, read indexer), backed by harness state instead of Configuration.
    /// </summary>
    internal class UserIntArray(string configName, int[]? defaults = null) : UserData(configName)
    {
        public int[] Default = defaults ?? [];

        private int[] Arr
        {
            get
            {
                if (!GluttonyCombo.RotationHarness.SMN.FakeGame.IntArrayValues.TryGetValue(ConfigName, out var a))
                {
                    a = (int[])Default.Clone();
                    GluttonyCombo.RotationHarness.SMN.FakeGame.IntArrayValues[ConfigName] = a;
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

    /// <summary>FAKE of UserBoolArray: read indexer only (the SMN code never writes one in a decision).</summary>
    internal class UserBoolArray(string configName, bool[]? defaults = null) : UserData(configName)
    {
        public bool[] Default = defaults ?? [];

        public int Count => Arr.Length;

        private bool[] Arr
        {
            get
            {
                if (!GluttonyCombo.RotationHarness.SMN.FakeGame.BoolArrayValues.TryGetValue(ConfigName, out var a))
                {
                    a = (bool[])Default.Clone();
                    GluttonyCombo.RotationHarness.SMN.FakeGame.BoolArrayValues[ConfigName] = a;
                }

                return a;
            }
        }

        public bool this[int index] => index < Count && Arr[index];

        public override void ResetToDefault() { }
    }

    /// <summary>
    ///     FAKE of the whole CustomComboFunctions surface the Summoner files read. Every member routes into
    ///     <see cref="GluttonyCombo.RotationHarness.SMN.FakeGame"/>; defaults make a clean, level-100
    ///     in-combat, pet-present, not-moving state with everything off cooldown and no buffs.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.SMN.FakeGame.EnabledPresets.Contains(preset);

        // ---- player / status / pet ----
        public static GluttonyCombo.RotationHarness.SMN.FakePlayer LocalPlayer { get; } = new();

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static float GetStatusEffectRemainingTime(uint statusId) => LocalPlayer.Status(statusId, anyOwner: true)?.RemainingTime ?? 0f;

        public static bool InCombat() => GluttonyCombo.RotationHarness.SMN.FakeGame.InCombat;

        public static bool PartyInCombat() => GluttonyCombo.RotationHarness.SMN.FakeGame.PartyInCombat;

        public static bool HasPetPresent() => GluttonyCombo.RotationHarness.SMN.FakeGame.HasPetPresent;

        public static bool IsMoving(bool ignoreConfig = false) => GluttonyCombo.RotationHarness.SMN.FakeGame.IsMoving;

        public static bool InMeleeRange() => GluttonyCombo.RotationHarness.SMN.FakeGame.InMeleeRange;

        public static float GetTargetDistance() => GluttonyCombo.RotationHarness.SMN.FakeGame.TargetDistance;

        public static float PlayerHealthPercentageHp() => GluttonyCombo.RotationHarness.SMN.FakeGame.PlayerHealthPercentageHp;

        // ---- occult (Sprint/Echo instant-cast procs): off, off — SMN branches read but never depend ----
        public static bool HasOccultInstantCast => false;

        public static bool HasOrExpectsOccultDualcast => false;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) =>
            GluttonyCombo.RotationHarness.SMN.FakeGame.HookOverrides.GetValueOrDefault(actionID, actionID);

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.SMN.FakeGame.NotLearned.Contains(actionId);

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            ActionLearned(actionId) && IsOffCooldown(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => GluttonyCombo.RotationHarness.SMN.FakeGame.Cooldown(actionID);

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static float GCDTotal => 2.5f;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static uint GetRemainingCharges(uint actionID) => GetCooldown(actionID).RemainingCharges;

        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.SMN.FakeGame.AllTraitsKnown;

        // ---- weave / combo state ----
        public static bool CanWeave(float estimatedWeaveTime = 0.6f, int? maxWeaves = null) => GluttonyCombo.RotationHarness.SMN.FakeGame.CanWeave;

        public static uint ComboAction => GluttonyCombo.RotationHarness.SMN.FakeGame.ComboActionId;

        public static bool CountdownActive => GluttonyCombo.RotationHarness.SMN.FakeGame.CountdownActive;

        public static float CountdownRemaining => GluttonyCombo.RotationHarness.SMN.FakeGame.CountdownRemaining;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) =>
            GluttonyCombo.RotationHarness.SMN.FakeGame.JustUsedActions.TryGetValue(actionID, out var j) && j.UsedAgo <= j.Window && j.Window <= withinSeconds;

        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.SMN.FakeGame.GroupDamageIncoming;

        // ---- gauges: a REAL Dalamud gauge object over harness-owned memory ----
        public static T GetJobGauge<T>() where T : JobGaugeBase => GluttonyCombo.RotationHarness.SMN.FakeGauges.Get<T>();
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

    /// <summary>FAKE of CustomCombo/SimpleTarget.cs: only the SMN names, as plain null targets.</summary>
    public static class SimpleTarget
    {
        public static class Stack
        {
            public static IGameObject? AllyToRaise => HardTarget;
        }

        public static IGameObject? HardTarget => null;

        public static IGameObject? TargetsTarget => null;

        public static IGameObject? AnyTank => null;

        public static IGameObject? LowestHPPAlly => null;

        public static IGameObject? Self => null;
    }

    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the exact member surface the SMN opener classes declare
    ///     and override. Openers are not exercised by the harness cases; FullOpener always declines.
    /// </summary>
    public class WrathOpener
    {
        public virtual int MinOpenerLevel => 1;

        public virtual int MaxOpenerLevel => 100;

        public virtual Combos.Preset Preset => Combos.Preset.SMN_ST_Advanced_Combo_Balance_Opener;

        internal virtual Functions.UserData ContentCheckConfig => null!;

        internal virtual bool IncludePot => false;

        public virtual bool HasCooldowns() => false;

        public virtual List<Func<uint>> OpenerActions { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public bool LevelChecked =>
            GluttonyCombo.RotationHarness.SMN.FakeGame.Level >= MinOpenerLevel &&
            GluttonyCombo.RotationHarness.SMN.FakeGame.Level <= MaxOpenerLevel;

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
            GluttonyCombo.RotationHarness.SMN.FakeGame.OneButtonRotation;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>FAKE of the UIntExtensions Retarget extension members (Core/ActionRetargeting.cs) the SMN
    ///     small features compile against: registering a retarget is out of the tested boundary.</summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint action, uint replaced, IGameObject? target) => action;
    }

    /// <summary>FAKE of the SimpleTarget filter extensions: identity pass-throughs.</summary>
    public static class SimpleTargetExtensions
    {
        public static IGameObject? IfFriendly(this IGameObject? t) => t;

        public static IGameObject? IfInParty(this IGameObject? t) => t;

        public static IGameObject? IfMissingHP(this IGameObject? t) => t;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    // FAKE part of the partial SMN class: the settings class (the real SMN_Config.cs drags
    // ImGui/localization into the compile; decision logic only reads the defaults). Defaults are
    // mirrored one-for-one from SMN_Config.cs "Options" region.
    internal partial class SMN
    {
        internal static class Config
        {
            public static CustomComboNS.Functions.UserInt
                SMN_ST_Simple_Combo_Gapclose = new("SMN_ST_Simple_Combo_Gapclose"),
                SMN_AoE_Simple_Combo_Gapclose = new("SMN_AoE_Simple_Combo_Gapclose"),
                SMN_ST_Advanced_Combo_AltMode = new("SMN_ST_Advanced_Combo_AltMode"),
                SMN_ST_Lucid = new("SMN_ST_Lucid", 8000),
                SMN_ST_SwiftcastPhase = new("SMN_SwiftcastPhase", 1),
                SMN_ST_CrimsonCycloneMeleeDistance = new("SMN_ST_CrimsonCycloneMeleeDistance", 25),
                SMN_ST_RadiantMaintainHP = new("SMN_ST_RadiantMaintainHP", 90),
                SMN_Opener_SkipSwiftcast = new("SMN_Opener_SkipSwiftcast", 1),
                SMN_AoE_Lucid = new("SMN_AoE_Lucid", 8000),
                SMN_AoE_CrimsonCycloneMeleeDistance = new("SMN_AoE_CrimsonCycloneMeleeDistance", 25),
                SMN_AoE_RadiantMaintainHP = new("SMN_AoE_RadiantMaintainHP", 90),
                SMN_AoE_SwiftcastPhase = new("SMN_AoE_SwiftcastPhase", 1),
                SMN_Balance_Content = new("SMN_Balance_Content", 1);

            public static CustomComboNS.Functions.UserBool
                SMN_Opener_Potion = new("SMN_Opener_Potion"),
                SMN_Opener_PrepullBlock = new("SMN_Opener_PrepullBlock", true);

            public static CustomComboNS.Functions.UserBoolArray
                SMN_ST_Egi_AstralFlow = new("SMN_ST_Egi_AstralFlow"),
                SMN_AoE_Egi_AstralFlow = new("SMN_AoE_Egi_AstralFlow");

            internal static CustomComboNS.Functions.UserIntArray
                SMN_ST_Egi_Priority = new("SMN_ST_Egi_Priority"),
                SMN_AoE_Egi_Priority = new("SMN_AoE_Egi_Priority");
        }
    }
}

// ======================================================================================
namespace GluttonyCombo.Core
{
    /// <summary>Namespace stub: SMN.cs has `using GluttonyCombo.Core;`; its real members are faked elsewhere.</summary>
    internal static class HarnessCoreNamespaceStub;
}
