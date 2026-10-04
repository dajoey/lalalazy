// Fakes.cs — BRD edition of the harness-owned stand-ins for everything GluttonyCombo's
// real Bard code reads from the game/plugin environment. Pattern and ownership identical
// to the VPR spike (rot/harness-spike): the real BRD.cs / BRD_Helper.cs compile UNCHANGED
// against these; nothing in src/ knows this file exists.
//
// Preset values here are IDENTITY-ONLY (31000-range), NOT the production IDs — they exist
// so IsEnabled(Preset.X) calls resolve offline. The production enum is
// src/GluttonyCombo/GluttonyCombo/Combos/CustomComboPreset.cs; check-preset-ids.py scans
// only that file, so this fake enum cannot collide with or corrupt production IDs.
// Faked members were read off the REAL sources this session (paths in comments) so
// signatures, namespaces and accessibility match what the job files expect. NOTE the real
// namespaces do NOT always match the folders (All.cs lives in Combos/PvE/ALL/ but
// declares namespace GluttonyCombo.Combos.PvE; Items.cs in the same folder DOES declare
// ...PvE.ALL) — each block below states the real home.

// ---- the fake preset enum (real: Combos/CustomComboPreset.cs, public enum Preset) ----
namespace GluttonyCombo.Combos
{
    public enum Preset
    {
        None = 0,
        All = 1,
        BRD_ST_AdvMode = 31001,
        BRD_ST_SimpleMode = 31002,
        BRD_ST_Adv_Balance_Standard = 31003,
        BRD_Adv_Song = 31010,
        BRD_AoE_Adv_Songs = 31011,
        BRD_Adv_Buffs = 31012,
        BRD_AoE_Adv_Buffs = 31013,
        BRD_ST_Adv_oGCD = 31014,
        BRD_AoE_Adv_oGCD = 31015,
        BRD_Adv_Pooling = 31016,
        BRD_AoE_Pooling = 31017,
        BRD_Adv_Interrupt = 31018,
        BRD_AoE_Adv_Interrupt = 31019,
        BRD_Adv_Troubadour = 31020,
        BRD_Adv_NaturesMinne = 31021,
        BRD_ST_SecondWind = 31022,
        BRD_AoE_SecondWind = 31023,
        BRD_ST_Wardens = 31024,
        BRD_AoE_Wardens = 31025,
        BRD_Hidden_Song_Extension = 31026,
        BRD_Adv_DoT = 31027,
        BRD_AoE_Adv_Multidot = 31028,
        BRD_Adv_BuffsEncore = 31029,
        BRD_AoE_BuffsEncore = 31030,
        BRD_ST_ApexArrow = 31031,
        BRD_AoE_ApexArrow = 31032,
        BRD_Adv_ApexPooling = 31033,
        BRD_AoE_ApexPooling = 31034,
        BRD_Adv_BuffsResonant = 31035,
        BRD_AoE_BuffsResonant = 31036,
        BRD_AoE_AdvMode = 31040,
        BRD_AoE_SimpleMode = 31041,
        BRD_StraightShotUpgrade = 31050,
        BRD_StraightShotUpgrade_OGCDs = 31051,
        BRD_DoTMaintainance = 31052,
        BRD_IronJaws = 31053,
        BRD_OneButtonDots = 31054,
        BRD_OneButtonDots_Retargeted = 31055,
        BRD_OneButtonDots_IronJaws = 31056,
        BRD_OneButtonDots_SavageBlade = 31057,
        BRD_AoE_oGCD = 31058,
        BRD_ST_oGCD = 31059,
        BRD_AoE_oGCD_Songs = 31060,
        BRD_ST_oGCD_Songs = 31061,
        BRD_WideVolleyUpgrade = 31062,
        BRD_WideVolleyUpgrade_OGCDs = 31063,
        BRD_WideVolleyUpgrade_Apex = 31064,
        BRD_Buffs = 31065,
        BRD_OneButtonSongs = 31066,
    }

    // real: Preset.FullLineageEnabled() as used by WrathOpener.Enabled
    internal static class HarnessPresetExtensions
    {
        public static bool FullLineageEnabled(this Preset preset) =>
            GluttonyCombo.RotationHarness.FakeGame.EnabledPresets.Contains(preset);
    }
}

// ---- PvE shared bases ---------------------------------------------------------------
// real All: Combos/PvE/ALL/All.cs — folder ALL, namespace GluttonyCombo.Combos.PvE
// real ContentSpecificActions: Combos/PvE/Content/ContentSpecificActions.cs, namespace ...PvE
// real PhysicalRanged: Combos/PvE/ALL/JobClasses.cs, namespace ...PvE
namespace GluttonyCombo.Combos.PvE
{
    internal partial class All
    {
        public static byte Cease = 3;
    }

    public static class ContentSpecificActions
    {
        public static bool TryGet(ref uint actionId, out uint replacementActionId)
        {
            replacementActionId = 0;
            return false; // no content replacement in the offline harness
        }
    }

    // real: Combos/PvE/ALL/JobClasses.cs — class PhysicalRanged with a static Role
    internal class PhysicalRanged
    {
        protected PhysicalRanged() { }

        public static FakePhysicalRangedRole Role { get; } = new();
    }

    internal class FakePhysicalRangedRole
    {
        // real role-action IDs (RoleImplementation.cs): Second Wind 7541, Head Graze 7551
        public uint SecondWind => 7541;
        public uint HeadGraze => 7551;

        public bool CanSecondWind(int healthpercent) =>
            GluttonyCombo.RotationHarness.FakeGame.RoleActionReady;

        public bool CanHeadGraze(bool enabled, GluttonyCombo.CustomComboNS.Functions.WeaveTypes weave =
            GluttonyCombo.CustomComboNS.Functions.WeaveTypes.None) => false; // interrupts never fire in harness cases
    }

    // real: DNC/MCH job files — only the mitigation-buff IDs BRD's party mitigation checks name
    internal partial class DNC
    {
        internal static class Buffs
        {
            public const ushort ShieldSamba = 16012;
        }
    }

    internal partial class MCH
    {
        internal static class Buffs
        {
            public const ushort Tactician = 16889;
        }
    }
}

// ---- potion items (real: Combos/PvE/ALL/Items.cs, namespace GluttonyCombo.Combos.PvE.ALL)
namespace GluttonyCombo.Combos.PvE.ALL
{
    internal partial class Items
    {
        public enum PotionType
        {
            Strength = 1,
            Dex = 2,
            Vit = 3,
            Int = 4,
            Mind = 5,
            Piety = 6,
        }

        // real signatures mirrored; offline there is no inventory, so no potion row exists
        public static uint UseItem(uint item) => item;
        public static uint GetStrongestPotionRow(PotionType type, bool inInventory = true) => 0;
    }
}

// ---- BRD config values (real: Combos/PvE/BRD/BRD_Config.cs — names/defaults mirrored
//      one-for-one; the Draw/UI half of the real file is NOT compiled) ----------------
namespace GluttonyCombo.Combos.PvE
{
    internal partial class BRD
    {
        internal static class Config
        {
            public static GluttonyCombo.CustomComboNS.Functions.UserBool
                BRD_AoE_Wardens_Auto = new("BRD_AoE_Wardens_Auto"),
                BRD_ST_Wardens_Auto = new("BRD_ST_Wardens_Auto"),
                BRD_IronJaws_Apex = new("BRD_IronJaws_Apex"),
                BRD_IronJaws_Alternate = new("BRD_IronJaws_Alternate"),
                BRD_Opener_Potion = new("BRD_Opener_Potion"),
                BRD_Opener_PrepullBlock = new("BRD_Opener_PrepullBlock", true);

            public static GluttonyCombo.CustomComboNS.Functions.UserInt
                BRD_RagingJawsRenewTime = new("ragingJawsRenewTime", 5),
                BRD_STSecondWindThreshold = new("BRD_STSecondWindThreshold", 40),
                BRD_AoESecondWindThreshold = new("BRD_AoESecondWindThreshold", 40),
                BRD_Adv_Opener_Selection = new("BRD_Adv_Opener_Selection", 0),
                BRD_Balance_Content = new("BRD_Balance_Content", 1),
                BRD_Adv_DoT_Refresh = new("BRD_Adv_DoT_Refresh", 4),
                BRD_ST_DPS_DotBossOption = new("BRD_ST_DPS_DotBossOption", 0),
                BRD_ST_DPS_DotBossAddsOption = new("BRD_ST_DPS_DotBossAddsOption", 100),
                BRD_ST_DPS_DotTrashOption = new("BRD_ST_DPS_DotTrashOption", 30),
                BRD_Adv_Buffs_Threshold = new("BRD_Adv_Buffs_Threshold", 30),
                BRD_Adv_Buffs_SubOption = new("BRD_Adv_Buffs_SubOption", 0),
                BRD_AoE_Adv_MultidotBossOption = new("BRD_AoE_Adv_MultidotBossOption", 0),
                BRD_AoE_Adv_MultidotBossAddsOption = new("BRD_AoE_Adv_MultidotBossAddsOption", 100),
                BRD_AoE_Adv_MultidotTrashOption = new("BRD_AoE_Adv_MultidotTrashOption", 30),
                BRD_AoE_Adv_Multidot_Refresh = new("BRD_AoE_Adv_Multidot_Refresh", 4),
                BRD_AoE_Adv_Buffs_Threshold = new("BRD_AoE_Adv_Buffs_Threshold", 30),
                BRD_AoE_Adv_Buffs_SubOption = new("BRD_AoE_Adv_Buffs_SubOption", 0);

            public static GluttonyCombo.CustomComboNS.Functions.UserBoolArray
                BRD_AoE_Adv_Buffs_Options = new("BRD_AoE_Adv_Buffs_Options"),
                BRD_Adv_Buffs_Options = new("BRD_Adv_Buffs_Options"),
                BRD_Adv_DoT_Options = new("BRD_Adv_DoT_Options"),
                BRD_StraightShotUpgrade_OGCDs_Options = new("BRD_StraightShotUpgrade_OGCDs_Options"),
                BRD_WideVolleyUpgrade_OGCDs_Options = new("BRD_WideVolleyUpgrade_OGCDs_Options");
        }
    }
}

// ---- cooldown data + action watching (real: Data/CooldownData.cs, Data/ActionWatching.cs)
namespace GluttonyCombo.Data
{
    internal class CooldownData
    {
        public uint ActionID;
        public uint CooldownTotal;
        public uint CooldownElapsed;
        public float CooldownRemaining;
        public bool IsCooldown;
        public uint MaxCharges;
        public uint RemainingCharges;
        public uint ChargeDuration;
    }

    // real: Data/ActionWatching.cs (nested enum ActionAttackType : uint, static ActionWatching)
    public static class ActionWatching
    {
        public enum ActionAttackType : uint
        {
            Unknown = 0,
            AutoAttack = 1,
            Ability = 2,
            Spell = 3,
        }

        public static ActionAttackType GetAttackType(uint actionId) => ActionAttackType.Ability;
    }
}

// ---- retarget attribute (real: Core/ActionRetargeting.cs) ---------------------------
namespace GluttonyCombo.Core
{
    public class ActionRetargeting
    {
        // the real class is an IDisposable service; BRD only names its nested attribute
        public class TargetResolverAttribute : Attribute { }
    }
}

// ---- native action helper (real: Native/CustomActionManager.cs) ---------------------
namespace GluttonyCombo.Native
{
    // real: Native/CustomActionManager.cs :450
    public enum CustomActionType
    {
        None = 0,
        SingleTargetDPS = 1,
        AoEDPS = 2,
        SingleTargetHeals = 3,
        AoEHeals = 4,
    }

    // real: CustomActionHelper in CustomActionManager.cs; BRD uses only the one-button gate,
    // which answers "is the pressed action one of the originals this combo replaces?"
    public class CustomActionHelper
    {
        public static bool OneButtonRotationChecker(uint actionId, CustomActionType type, params uint[] originals) =>
            originals.Contains(actionId);
    }
}

// ---- CustomCombo + SimpleTarget + WrathOpener (real namespace: GluttonyCombo.CustomComboNS)
namespace GluttonyCombo.CustomComboNS
{
    // real: CustomCombo/CustomCombo.cs — internal abstract partial CustomCombo : CustomComboFunctions.
    // RunInvoke is the harness's outside-facing entry (the real entry is plugin-wired).
    internal abstract partial class CustomCombo : Functions.CustomComboFunctions
    {
        protected internal abstract GluttonyCombo.Combos.Preset Preset { get; }

        protected abstract uint Invoke(uint comboActionID);

        public uint RunInvoke(uint comboActionID) => Invoke(comboActionID);
    }

    // real: CustomCombo/SimpleTarget.cs — the selectors BRD's DoT regions call;
    // harness cases exercise none of them, so they return no target and force fallbacks
    internal static class SimpleTarget
    {
        public static Dalamud.Game.ClientState.Objects.Types.IGameObject? TargetWithDoTLowestRemainingTimer(
            uint action, ushort debuffId) => null;

        public static Dalamud.Game.ClientState.Objects.Types.IGameObject? DottableEnemy(
            uint action, ushort debuff, int maxNumberOfEnemiesInRange = 1) => null;

        public static Dalamud.Game.ClientState.Objects.Types.IGameObject? DottableEnemy(
            uint action, ushort debuff, Func<Dalamud.Game.ClientState.Objects.Types.IGameObject?, int> effectCount,
            int maxNumberOfEnemiesInRange = 1) => null;

        public static Dalamud.Game.ClientState.Objects.Types.IGameObject? BardRefreshableEnemy(
            uint action, ushort debuff, ushort alternateDebuff,
            Func<Dalamud.Game.ClientState.Objects.Types.IGameObject?, int> effectCount,
            int maxNumberOfEnemiesInRange = 1) => null;
    }

    // real: CustomCombo/WrathOpener.cs — member-for-member shape (accessibility, virtual/
    // abstract, tuple property types) so BRDOpenerBase and the four BRD openers compile
    // unchanged. Bodies are harness no-ops: the opener PRESET stays disabled in every case,
    // so only Opener()'s LevelChecked/Enabled gates ever run.
    public enum OpenerState
    {
        OpenerNotReady = 0,
        OpenerReady = 1,
        InOpener = 2,
        OpenerFinished = 3,
        FailedOpener = 4,
    }

    public abstract class WrathOpener
    {
        public virtual OpenerState CurrentState { get; set; }
        public virtual int OpenerStep { get; set; }
        public abstract List<Func<uint>> OpenerActions { get; set; }
        public virtual List<int> DelayedWeaveSteps { get; set; } = [];
        public virtual List<int> VeryDelayedWeaveSteps { get; set; } = [];
        public virtual List<(int[] Steps, Func<float> HoldDelay)> PrepullDelays { get; set; } = [];
        public virtual List<(int[] Steps, Func<bool> Condition)> SkipSteps { get; set; } = [];
        public virtual List<int> AllowUpgradeSteps { get; set; } = [];
        public int DelayedStep;
        public int SkippingStep;
        public uint CurrentOpenerAction { get; set; }
        public uint PreviousOpenerAction { get; set; }
        public abstract int MinOpenerLevel { get; }
        public abstract int MaxOpenerLevel { get; }
        public virtual bool AllowReopener { get; set; }
        internal abstract Functions.UserData? ContentCheckConfig { get; }
        internal abstract bool IncludePot { get; }

        // real reads Svc.PlayerState.EffectiveLevel; harness reads FakeGame.Level
        public bool LevelChecked => GluttonyCombo.RotationHarness.FakeGame.Level >= MinOpenerLevel &&
                                    GluttonyCombo.RotationHarness.FakeGame.Level <= MaxOpenerLevel;

        public abstract GluttonyCombo.Combos.Preset Preset { get; }
        public bool Enabled => GluttonyCombo.RotationHarness.FakeGame.EnabledPresets.Contains(Preset);
        public abstract bool HasCooldowns();
        public bool CacheReady;
        public void ProgressOpener(uint actionId, bool item = false) { }
        public bool FullOpener(ref uint actionId) => false;
        public void ResetOpener(bool stayReady = false) { }
        internal static void SelectOpener() { }
        public static WrathOpener? CurrentOpener { get; set; }
        public static string OpenerStatus() => "";
        public static WrathOpener Dummy { get; } = new DummyOpener();

        private sealed class DummyOpener : WrathOpener
        {
            public override List<Func<uint>> OpenerActions { get; set; } = [];
            public override int MinOpenerLevel => 0;
            public override int MaxOpenerLevel => 0;
            internal override Functions.UserData? ContentCheckConfig => null;
            internal override bool IncludePot => false;
            public override GluttonyCombo.Combos.Preset Preset { get; } = default;
            public override bool HasCooldowns() => false;
        }
    }
}

// ---- user config types + the function facade ----------------------------------------
// real: CustomCombo/Functions/*.cs — internal abstract partial class CustomComboFunctions
// (NOT static: CustomCombo derives from it, which is how the job-file instance methods see
// ActionReady/OriginalHook/IsEnabled unqualified; BRD_Helper's statics use `using static`,
// which is why every member faked here is static). Every member routes to
// FakeGame/FakeGauges/FakePlayer.
namespace GluttonyCombo.CustomComboNS.Functions
{
    public abstract class UserData
    {
        public string ConfigName { get; }
        protected UserData(string configName) => ConfigName = configName;
        public abstract void ResetToDefault();
        public override string ToString() => ConfigName;

        public static implicit operator string(UserData o) => o.ConfigName;
    }

    public class UserFloat(string configName, float defaultV = 0f) : UserData(configName)
    {
        public float Value => defaultV;
        public override void ResetToDefault() { }

        public static implicit operator float(UserFloat o) => o.Value;
    }

    public class UserInt(string configName, int defaultV = 0) : UserData(configName)
    {
        public int Value => defaultV;
        public override void ResetToDefault() { }

        public static implicit operator int(UserInt o) => o.Value;
    }

    public class UserBool(string configName, bool defaultV = false) : UserData(configName)
    {
        public bool Value => defaultV;
        public override void ResetToDefault() { }

        public static implicit operator bool(UserBool o) => o.Value;
    }

    // real: CustomCombo/Functions/Config.cs — indexed multi-choice; backed by FakeGame.BoolArrayValues
    internal class UserBoolArray(string configName) : UserData(configName)
    {
        public bool this[int index] => GluttonyCombo.RotationHarness.FakeGame.GetBoolArray(ConfigName, index);

        public override void ResetToDefault() { }

        public static implicit operator bool[](UserBoolArray o) => [];
    }

    // real: CustomCombo/Functions/Action.cs — enum WeaveTypes
    public enum WeaveTypes
    {
        None = 0,
        NormalWeave = 1,
        DelayWeave = 2,
    }

    internal abstract partial class CustomComboFunctions
    {
        public static uint Level => GluttonyCombo.RotationHarness.FakeGame.Level;
        public static GluttonyCombo.RotationHarness.FakePlayer LocalPlayer { get; } = new();
        public static Dalamud.Game.ClientState.Objects.Types.IGameObject? CurrentTarget => null;
        public static bool ComboTimer => false;
        public static uint ComboAction => 0;

        public static bool IsEnabled(GluttonyCombo.Combos.Preset preset) =>
            GluttonyCombo.RotationHarness.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsEnabled(GluttonyCombo.CustomComboNS.CustomCombo combo) =>
            GluttonyCombo.RotationHarness.FakeGame.EnabledCombos.Contains(combo);

        public static T GetJobGauge<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase =>
            GluttonyCombo.RotationHarness.FakeGauges.Get<T>();

        public static uint OriginalHook(uint actionId) =>
            GluttonyCombo.RotationHarness.FakeGame.HookOverrides.TryGetValue(actionId, out var hooked)
                ? hooked
                : actionId;

        public static bool ActionLearned(uint actionId) => GluttonyCombo.RotationHarness.FakeGame.Level >= 100;
        public static bool TraitLevelChecked(uint traitId) => GluttonyCombo.RotationHarness.FakeGame.Level >= 100;

        public static bool ActionReady(uint actionId) => IsOffCooldown(actionId);

        internal static GluttonyCombo.Data.CooldownData GetCooldown(uint actionId) =>
            GluttonyCombo.RotationHarness.FakeGame.Cooldown(actionId);

        public static bool IsOffCooldown(uint actionId) =>
            !GluttonyCombo.RotationHarness.FakeGame.Cooldown(actionId).IsCooldown;

        public static float GetCooldownRemainingTime(uint actionId) =>
            GluttonyCombo.RotationHarness.FakeGame.Cooldown(actionId).CooldownRemaining;

        public static uint GetRemainingCharges(uint actionId) =>
            GluttonyCombo.RotationHarness.FakeGame.Cooldown(actionId).RemainingCharges;

        public static bool JustUsed(uint actionId, float t = 0.5f) =>
            GluttonyCombo.RotationHarness.FakeGame.JustUsedActions.Contains(actionId);

        public static bool WasLastAction(uint actionId) => false;
        public static bool WasLastAbility(uint actionId) => false;

        public static bool HasStatusEffect(uint statusId, bool anyOwner = false) =>
            LocalPlayer.HasStatus(statusId, anyOwner);

        public static bool CanWeave() => GluttonyCombo.RotationHarness.FakeGame.CanWeave;

        // real: CustomCombo/Functions/Action.cs :353 (weaveStart/weaveEnd/maxWeaves defaults);
        // offline there is no animation state, so no delayed weave is ever possible
        public static bool CanDelayedWeave(float weaveStart = 1.25f, float weaveEnd = 0.6f, int? maxWeaves = null) => false;
        public static bool InCombat() => GluttonyCombo.RotationHarness.FakeGame.InCombat;
        public static bool InBossEncounter() => GluttonyCombo.RotationHarness.FakeGame.InBossEncounter;
        public static bool HasBattleTarget() => GluttonyCombo.RotationHarness.FakeGame.HasBattleTarget;
        public static float GetTargetHPPercent() => GluttonyCombo.RotationHarness.FakeGame.TargetHPPercent;
        public static bool GroupDamageIncoming() => GluttonyCombo.RotationHarness.FakeGame.GroupDamageIncoming;
        public static bool CountdownActive => GluttonyCombo.RotationHarness.FakeGame.CountdownActive;
        public static float CountdownRemaining => GluttonyCombo.RotationHarness.FakeGame.CountdownRemaining;
        public static bool TargetNeedsPositionals() => false;
        public static bool OnTargetsRear() => true;
        public static bool OnTargetsFlank() => true;
        public static bool InMeleeRange() => true;

        public static uint NumberOfEnemiesInRange(uint actionId) => 0;
        public static uint NumberOfAlliesInRange(uint actionId) => 0;
        public static bool InActionRange(uint actionId) => true;
        public static bool InActionRange(uint actionId, Dalamud.Game.ClientState.Objects.Types.IGameObject? target) => true;

        public static List<GluttonyCombo.RotationHarness.FakePartyMember> GetPartyMembers() => [];
    }
}

// ---- extension methods the job files call (real: Extensions/StatusExtensions.cs etc.)
//      Signatures mirror the real file; null-safety is preserved: the real
//      RemainingTimeOrZero opens with `if (status is null) return 0;` and Status/CanApplyStatus
//      treat a null target as no status / not applicable.
namespace GluttonyCombo.Extensions
{
    public static class StatusExtensions
    {
        // target-side status lookup: routes to FakeGame.TargetStatuses (player side uses FakePlayer.Status)
        public static Dalamud.Game.ClientState.Statuses.IStatus? Status(
            this Dalamud.Game.ClientState.Objects.Types.IGameObject? obj, uint statusId, bool anyOwner = false)
        {
            if (obj is null) return null;
            foreach (var s in GluttonyCombo.RotationHarness.FakeGame.TargetStatuses)
            {
                if (s.StatusId != statusId) continue;
                if (!anyOwner && s.SourceId != 0) continue; // SourceId 0 = own status
                return s;
            }
            return null;
        }

        // real one is null-safe; its animation-lock branch reads ActionManager, which has no
        // offline stand-in, so the harness keeps the null-safety and drops that branch
        public static float RemainingTimeOrZero(
            this Dalamud.Game.ClientState.Statuses.IStatus? status, bool checkAnimationLock = true) =>
            status?.RemainingTime ?? 0f;

        public static bool CanApplyStatus(
            this Dalamud.Game.ClientState.Objects.Types.IGameObject? obj, uint statusId) =>
            obj is not null && GluttonyCombo.RotationHarness.FakeGame.TargetCanApplyStatus;

        public static bool IsBoss(this Dalamud.Game.ClientState.Objects.Types.IGameObject? obj) =>
            obj is not null && GluttonyCombo.RotationHarness.FakeGame.TargetIsBoss;

        public static bool IsNotThePlayer(this Dalamud.Game.ClientState.Objects.Types.IGameObject obj) => false;
        public static bool IsCleansable(this Dalamud.Game.ClientState.Objects.Types.IGameObject obj) => false;
    }

    // real: Extensions/UIntExtensions.cs — Retarget only rewrites the target of an action,
    // never the action, so the fakes return the action unchanged
    public static class UIntExtensions
    {
        public static uint Retarget(this uint actionId, object? target) => actionId;
        public static uint Retarget(this uint actionId, uint originalAction, object? target) => actionId;

        public static uint Retarget(
            this uint actionId, uint originalAction,
            Func<Dalamud.Game.ClientState.Objects.Types.IGameObject?> targetResolver) => actionId;

        public static uint Retarget(this uint actionId, uint[] originalActions, object? target) => actionId;
    }
}

// ---- harness-owned player/status/party objects ---------------------------------------
namespace GluttonyCombo.RotationHarness
{
    // implements the REAL Dalamud interface (member list reflected from the dev Dalamud.dll
    // this session) because BRD's Purple/Blue DoT timers are typed IStatus?
    internal sealed record FakeStatus(uint StatusId, float RemainingTime, ushort Param = 0, uint SourceId = 0)
        : Dalamud.Game.ClientState.Statuses.IStatus
    {
        public nint Address => nint.Zero;
        public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Status> GameData => default;
        public Dalamud.Game.ClientState.Objects.Types.IGameObject? SourceObject => null;

        // IStatus : IEquatable<IStatus> — equality against other fake statuses only
        public bool Equals(Dalamud.Game.ClientState.Statuses.IStatus? other) => other is FakeStatus f && Equals(f);
    }

    public sealed class FakePlayer
    {
        public Dalamud.Game.ClientState.Statuses.IStatus? Status(uint statusId, bool anyOwner = false)
        {
            foreach (var s in FakeGame.Statuses)
            {
                if (s.StatusId != statusId) continue;
                if (!anyOwner && s.SourceId != 0) continue; // SourceId 0 = own status
                return s;
            }
            return null;
        }

        public bool HasStatus(uint statusId, bool anyOwner = false) => Status(statusId, anyOwner) is not null;

        public bool HasStatusEffects(uint[] statusIds, bool anyOwner = false, bool matchAll = false)
        {
            foreach (var id in statusIds)
            {
                var has = Status(id, anyOwner) is not null;
                if (matchAll && !has) return false;
                if (!matchAll && has) return true;
            }
            return matchAll;
        }

        public bool HasCleansableDebuff => false;
    }

    public sealed class FakePartyMember
    {
        public Dalamud.Game.ClientState.Objects.Types.IGameObject? BattleChara => null;
    }
}
