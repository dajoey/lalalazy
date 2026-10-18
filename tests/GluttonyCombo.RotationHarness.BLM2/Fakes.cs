// Fakes.cs — the harness-owned stand-ins for everything the REAL BLM_Helper.cs (compiled
// unchanged) reaches outside itself. Boundary:
//   REAL  : every decision line in BLM_Helper.cs, including the whole UseManaward gate.
//   FAKE  : the game + Dalamud boundary those decisions READ through — CustomComboFunctions
//           (cooldowns, player HP, party, movement, combat), the Preset enum slice, the BLM
//           Config members (bare identifiers via `using static`), GameObjectExtensions
//           (HasShield), the Role layer surface, the opener base, WrathPartyMember, ALL.Items,
//           and the DeepDungeons behemoth knobs. Values mirror real consts where they matter.

using System.Collections.Frozen;
using System.Collections.Generic;
using GluttonyCombo.CustomComboNS.Functions;

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE slice of the real CustomComboPreset enum: only the members BLM_Helper.cs
    ///     references. Values are placeholders (presets are compared by identity / looked up in
    ///     the enabled set); do not ship.
    /// </summary>
    public enum Preset
    {
        BLM_ST_Movement = 42001,
        BLM_ST_Opener = 42002,
        BLM_AoE_Movement = 42003,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE
{
    /// <summary>FAKE of the BLM settings class the helper reads through `using static ...BLM.Config`:
    /// only the members BLM_Helper.cs touches. Defaults mirror the real BLM_Config.cs where the
    /// tested paths read them; the rest are inert placeholders.</summary>
    internal partial class BLM
    {
        internal static class Config
        {
        public static UserInt
BLM_SelectedOpener = new("BLM_SelectedOpener"),
BLM_Balance_Content = new("BLM_Balance_Content", 1),
BLM_ST_LeyLinesCharges = new("BLM_ST_LeyLinesCharges", 1),
BLM_ST_LeyLinesMovement = new("BLM_ST_LeyLinesMovement"),
BLM_ST_LeyLinesHPOption = new("BLM_ST_LeyLinesHPOption", 25),
BLM_ST_LeyLinesHPBossOption = new("BLM_ST_LeyLinesHPBossOption"),
BLM_ST_ThunderBossHPOption = new("BLM_ST_ThunderBossHPOption"),
BLM_ST_ThunderBossAddsHPOption = new("BLM_ST_ThunderBossAddsHPOption", 10),
BLM_ST_ThunderTrashHPOption = new("BLM_ST_ThunderTrashHPOption", 25),
BLM_ST_Triplecast_WhenToUse = new("BLM_ST_Triplecast_WhenToUse", 1),
BLM_ST_TriplecastMovementCharges = new("BLM_ST_TriplecastMovementCharges", 1),
BLM_ST_PolyglotMovement = new("BLM_ST_PolyglotMovement", 1),
BLM_ST_PolyglotSaveUsage = new("BLM_ST_PolyglotSaveUsage"),
BLM_ST_ManawardHPThreshold = new("BLM_ST_ManawardHPThreshold", 25),
BLM_ST_ManawardTrigger = new("BLM_ST_ManawardTrigger"),
BLM_AoE_TriplecastHoldCharges = new("BLM_AoE_TriplecastHoldCharges"),
BLM_AoE_LeyLinesCharges = new("BLM_AoE_LeyLinesCharges"),
BLM_AoE_LeyLinesMovement = new("BLM_AoE_LeyLinesMovement"),
BLM_AoE_LeyLinesOption = new("BLM_AoE_LeyLinesOption", 25),
BLM_AoE_ThunderHP = new("BLM_AoE_ThunderHP", 25),
BLM_B1to3 = new("BLM_B1to3"),
BLM_F1to3 = new("BLM_F1to3"),
BLM_Fire4_FireAndIce = new("BLM_Fire4_FireAndIce");

        public static UserFloat
BLM_ST_LeyLinesTimeStill = new("BLM_ST_LeyLinesTimeStill", 2.5f),
BLM_AoE_LeyLinesTimeStill = new("BLM_AoE_LeyLinesTimeStill", 2.5f),
BLM_ST_ThunderRefresh = new("BLM_ST_ThunderRefresh", 2.5f);

        public static UserBool
BLM_Opener_Potion = new("BLM_Opener_Potion"),
BLM_Opener_PrepullBlock = new("BLM_Opener_PrepullBlock", true),
BLM_AM_FieldMouseover = new("BLM_AM_FieldMouseover"),
BLM_AmplifierXenoCD = new("BLM_AmplifierXenoCD"),
BLM_Fire4_FlareStar = new("BLM_Fire4_FlareStar"),
BLM_Flare_FlareStar = new("BLM_Flare_FlareStar"),
BLM_Fire1_Despair = new("BLM_Fire1_Despair"),
BLM_Blizzard3_Despair = new("BLM_Blizzard3_Despair"),
BLM_Fire4_Fire3 = new("BLM_Fire4_Fire3"),
BLM_ST_ManawardSolo = new("BLM_ST_ManawardSolo");

        public static UserBoolArray
BLM_ST_MovementOption = new("BLM_ST_MovementOption");

        public static UserIntArray
            BLM_ST_MovementPriority = new("BLM_ST_MovementPriority");

    }
    }
}

// ======================================================================================
namespace GluttonyCombo.Data
{
    /// <summary>FAKE of Data/CooldownData.cs: plain settable data instead of the ActionManager memory reads.</summary>
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
        public ushort MaxCharges = 2;
        public uint RemainingCharges = 2;

        public bool HasCharges => MaxCharges > 1;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.ALL
{
    /// <summary>FAKE of ALL/Items.cs: the potion helpers the opener lambdas compile against.</summary>
    internal class Items
    {
        public enum PotionType { Strength, Dex, Vit, Int, Mind }

        public static uint GetStrongestPotionRow(PotionType type, bool inInventory = true) => 33;

        public static uint UseItem(uint item) => item;
    }

    /// <summary>FAKE of the partial All class: the consts the opener lambdas compile against, values mirrored.</summary>
    internal partial class All
    {
        public const uint Cease = 1_000_004; // mirrored from ALL.cs (RotationHarness.NIN fake)
        public const uint Items = 43;
        public const uint Pomanders = 44;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.Content.DeepDungeons
{
    /// <summary>
    ///     FAKE of Combos/PvE/Content/DeepDungeon/DeepDungeon_Behemoth.cs: the PotD behemoth
    ///     meteor-state call the Manaward gate makes, as knobs on FakeGame. The real predicate is
    ///     under test in GluttonyCombo.DeepDungeonHarness; this harness tests the BLM wiring.
    /// </summary>
    internal static class DeepDungeons
    {
        public static bool BehemothMeteorWindow() => GluttonyCombo.RotationHarness.BLM2.FakeGame.BehemothMeteorWindow;

        public static bool BehemothMeteorHold() => GluttonyCombo.RotationHarness.BLM2.FakeGame.BehemothMeteorHold;
    }
}

// ======================================================================================
namespace GluttonyCombo.CustomComboNS.Functions
{
    /// <summary>FAKE of WrathPartyMember (Party.cs): the one member the helper reads.</summary>
    public class WrathPartyMember
    {
        public float TimeSinceBuffApplied(ushort buff) => GluttonyCombo.RotationHarness.BLM2.FakeGame.PartyTimeSinceFirestarter;
    }

    /// <summary>FAKE of UserData/UserInt/UserBool (Config.cs): storage in harness state; implicit string kept.</summary>
    internal abstract class UserData(string configName)
    {
        public string ConfigName = configName;

        public static implicit operator string(UserData o) => o.ConfigName;

        public abstract void ResetToDefault();
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => o.Default;

        public override void ResetToDefault() { }
    }

    internal class UserBool(string configName, bool defaults = false) : UserData(configName)
    {
        public bool Default = defaults;

        public static implicit operator bool(UserBool o) => o.Default;

        public override void ResetToDefault() { }
    }

    internal class UserFloat(string configName, float defaults = 0f) : UserData(configName)
    {
        public float Default = defaults;

        public static implicit operator float(UserFloat o) => o.Default;

        public override void ResetToDefault() { }
    }

    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public bool this[int index] => false;

        public override void ResetToDefault() { }
    }

    internal class UserIntArray(string configName, int[]? defaults = null) : UserData(configName)
    {
        public int[] Default = defaults ?? [];

        public int Count => Default.Length;

        public int this[int index] => index >= 0 && index < Default.Length ? Default[index] : 0;

        public int IndexOf(int item) => Array.IndexOf(Default, item);

        public IEnumerable<int> OrderBy<TKey>(Func<int, TKey> keySelector) => Default.OrderBy(keySelector);

        public static implicit operator int[](UserIntArray o) => o.Default;

        public override void ResetToDefault() { }
    }

    /// <summary>FAKE of Extensions/StatusExtensions.cs (the IBattleChara extension block): compile-only stand-ins.
    /// The Thunder-debuff paths are not exercised; every read is null/zero.</summary>
    public static class StatusExtensions
    {
        public static Dalamud.Game.ClientState.Statuses.IStatus? Status(
            this Dalamud.Game.ClientState.Objects.Types.IBattleChara? chara, uint id, bool anyOwner = false) => null;

        public static bool HasStatus(
            this Dalamud.Game.ClientState.Objects.Types.IBattleChara? chara, uint id, bool anyOwner = false) => false;

        public static float RemainingTimeOrZero(this Dalamud.Game.ClientState.Statuses.IStatus? status, bool checkAnimationLock = true) => 0f;
    }

    /// <summary>
    ///     FAKE of the CustomComboFunctions surface BLM_Helper.cs reads. Defaults: solo, in combat,
    ///     standing still, full HP, Manaward off cooldown, no shield, no incoming damage.
    /// </summary>
    /// <summary>Fake player carrier: HasStatus reads harness state (always false here).</summary>
    public class FakePlayer
    {
        public bool HasStatus(uint id, bool anyOwner = false) => false;
    }

    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.RotationHarness.BLM2.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status ----
        public static FakePlayer LocalPlayer { get; } = new();

        public static Dalamud.Game.ClientState.Objects.Types.IBattleChara? CurrentTarget => null;

        public static float PlayerHealthPercentageHp() => GluttonyCombo.RotationHarness.BLM2.FakeGame.HealthPercent;

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => false;

        public static bool InCombat() => true;

        public static bool InBossEncounter() => false;

        public static bool TargetIsBoss() => false;

        public static bool CountdownActive => false;

        public static float CountdownRemaining => 0f;

        // ---- occult crescent gates (OccultInstantCast.cs; harness always says no) ----
        public static bool HasOrExpectsOccultInstantCast => false;

        public static bool HasOrExpectsOccultDualcast => false;

        public static bool HasBattleTarget() => true;

        public static bool HasTarget() => true;

        public static bool IsMoving() => false;

        public static TimeSpan TimeStoodStill => TimeSpan.FromSeconds(30);

        // ---- party ----
        public static bool IsInParty(int partySize = 2) => GluttonyCombo.RotationHarness.BLM2.FakeGame.InParty && partySize <= 8;

        public static List<WrathPartyMember> GetPartyMembers(bool allowCache = true) =>
            GluttonyCombo.RotationHarness.BLM2.FakeGame.InParty
                ? [new WrathPartyMember(), new WrathPartyMember(), new WrathPartyMember(), new WrathPartyMember()]
                : [];

        public static bool PartyInCombatCheck => true;

        public static int NumberOfEnemiesInRange(uint aoeSpell, object? target = null, bool checkIgnoredList = false) => 1;

        public static bool GroupDamageIncoming(float? maxTimeRemaining = null) => GluttonyCombo.RotationHarness.BLM2.FakeGame.GroupDamageIncoming;

        public static int NumberOfAlliesInRange(uint actionId, object? target = null) => 1;

        // ---- target / range ----
        public static float GetTargetHPPercent(object? optionalTarget = null, bool includeShield = false, bool forceUsePending = false) => 100f;

        public static bool InActionRange(uint actionID) => true;

        public static bool TargetNeedsPositionals() => false;

        // ---- actions / cooldowns ----
        public static uint OriginalHook(uint actionID) => actionID;

        public static bool ActionLearned(uint actionId) => !GluttonyCombo.RotationHarness.BLM2.FakeGame.OnCooldown.Contains(uint.MaxValue) && actionId != 0;

        public static bool ActionReady(uint actionId, bool recastCheck = false, bool castCheck = false) =>
            !GluttonyCombo.RotationHarness.BLM2.FakeGame.OnCooldown.Contains(actionId);

        public static Data.CooldownData GetCooldown(uint actionID) => new()
        {
            IsCooldown = GluttonyCombo.RotationHarness.BLM2.FakeGame.OnCooldown.Contains(actionID),
            CooldownRemaining = GluttonyCombo.RotationHarness.BLM2.FakeGame.OnCooldown.Contains(actionID) ? 60f : 0f,
        };

        public static float GetCooldownRemainingTime(uint actionID) => GetCooldown(actionID).CooldownRemaining;

        public static bool IsOffCooldown(uint actionID) => !GetCooldown(actionID).IsCooldown;

        public static bool IsOnCooldown(uint actionID) => GetCooldown(actionID).IsCooldown;

        public static uint GetRemainingCharges(uint actionID) => 1;

        public static bool HasCharges(uint actionID) => false;

        public static bool JustUsed(uint actionID, float withinSeconds = 2f) => false;

        public static bool JustUsedOn(uint actionId, object? target, float variance = 3f) => false;

        public static float GetCooldownChargeRemainingTime(uint actionID) => GetCooldown(actionID).ChargeCooldownRemaining;

        public static bool WasLastAction(uint actionID) => false;

        public static bool TraitLevelChecked(uint traitId) => true;

        public static T GetJobGauge<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase =>
            (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

        public static int GetResourceCost(uint actionID) => 0;
    }
}

// ======================================================================================
namespace GluttonyCombo.CustomComboNS
{
    /// <summary>
    ///     FAKE of CustomCombo/WrathOpener.cs: the members the BLM opener classes (inside the real
    ///     BLM_Helper.cs) override or read. No opener logic runs in the tested paths.
    /// </summary>
    public abstract class WrathOpener
    {
        public abstract List<Func<uint>> OpenerActions { get; set; }

        public abstract int MinOpenerLevel { get; }

        public abstract int MaxOpenerLevel { get; }

        public virtual Combos.Preset Preset => default;

        internal virtual CustomComboNS.Functions.UserData ContentCheckConfig => throw new NotImplementedException("harness boundary: not exercised");

        internal virtual bool IncludePot => false;

        public virtual List<int> DelayedWeaveSteps { get; set; } = [];

        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];

        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];

        public virtual List<int> AllowUpgradeSteps { get; set; } = [];

        public virtual bool HasCooldowns() => false;

        public bool LevelChecked => true;

        public static readonly WrathOpener Dummy = new HarnessSkeletonOpener();

        private sealed class HarnessSkeletonOpener : WrathOpener
        {
            public override List<Func<uint>> OpenerActions { get; set; } = [];

            public override int MinOpenerLevel => 1;

            public override int MaxOpenerLevel => 1;
        }
    }

    /// <summary>FAKE of ContentCheck.cs: the configured-content check the opener paths read. Never called.</summary>
    internal static class ContentCheck
    {
        public static bool IsInConfiguredContent(int configuredContent, int configListSet) => false;
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    /// <summary>
    ///     FAKE of the GameObjectExtensions surface BLM_Helper touches: HasShield reads harness
    ///     state; the rest are inert stand-ins for compile.
    /// </summary>
    public static class GameObjectExtensions
    {
        public static bool HasShield(this object? o) => GluttonyCombo.RotationHarness.BLM2.FakeGame.HasShield;

        public static bool IsBoss(this object? o) => true;

        public static bool CanApplyStatus(this object? o, uint id) => true;
    }

    /// <summary>FAKE of the UIntExtensions surface: LevelChecked/TraitLevelChecked mirror the real shape.</summary>
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;

        internal static bool LevelChecked(this uint value) => true;

        internal static bool TraitLevelChecked(this uint value) => true;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.ALL
{
    /// <summary>FAKE of Roles/RoleActions.cs + RoleImplementation.cs: the members BLM_Helper touches.</summary>
    internal static class RoleActions
    {
        public static class Buffs
        {
            public const ushort Addle = 866;
        }
    }

    internal class Role
    {
        public static uint Swiftcast => 7561;

        public static class Buffs
        {
            public const ushort Addle = 866;
            public const ushort Swiftcast = 167;
        }

        public static bool CanAddle() => true;
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
        public static bool OneButtonRotationChecker(uint actionId, CustomActionType type, params uint[] originals) => true;
    }
}
