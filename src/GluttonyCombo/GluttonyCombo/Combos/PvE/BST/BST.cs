#region Dependencies

using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Native;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using static GluttonyCombo.Combos.PvE.BST.Config;

#endregion

namespace GluttonyCombo.Combos.PvE;

// Beastmaster (Job.BST = 43) - REBUILT 2026-09-16 (fork-owned; see wiki Projects/FFXIV Mods, BST rebuild).
//
// This file is the LIVE half only: it reads the game into BST_RotationLogic.BstState and presses what
// BST_RotationLogic.Decide returns. All decisions live in BST_RotationLogic.cs, which the offline
// harness (tests/GluttonyCombo.BSTRotationHarness) runs through a familiar-lifecycle simulator at
// every level 1-50. Keep game reads here and rules there.
//
// Root causes this rebuild fixes, proven from Joey's 2026-09-16 17:48 and 18:01 sessions:
//  1. The old loop pressed Tempered Release the moment a familiar arrived. On wespe that is Final Sting,
//     which retreats the familiar ("sacrificing the pet moments after summoning it").
//  2. The next horn was picked from the Borrow-latched gauge nibble (always 0 below L22), so it kept
//     choosing slot 1 while slot 1 sat in its ~90 s in-combat lockout, and never resummoned.
internal partial class BST : Melee
{
    #region Presets

    internal class BST_ST_SimpleMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_ST_SimpleMode;

        protected override uint Invoke(uint actionID)
        {
            if (!CustomActionHelper.OneButtonRotationChecker(actionID, CustomActionType.SingleTargetDPS, SmashAxe))
                return actionID;

            return Run(BST_RotationLogic.BstSettings.Defaults(aoe: false));
        }
    }

    internal class BST_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_ST_AdvancedMode;

        protected override uint Invoke(uint actionID)
        {
            if (!CustomActionHelper.OneButtonRotationChecker(actionID, CustomActionType.SingleTargetDPS, SmashAxe))
                return actionID;

            return Run(AdvancedSettings(aoe: false));
        }
    }

    internal class BST_AoE_SimpleMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_AoE_SimpleMode;

        protected override uint Invoke(uint actionID)
        {
            if (!CustomActionHelper.OneButtonRotationChecker(actionID, CustomActionType.AoEDPS, AxebladeBite))
                return actionID;

            return Run(BST_RotationLogic.BstSettings.Defaults(aoe: true));
        }
    }

    internal class BST_AoE_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_AoE_AdvancedMode;

        protected override uint Invoke(uint actionID)
        {
            if (!CustomActionHelper.OneButtonRotationChecker(actionID, CustomActionType.AoEDPS, AxebladeBite))
                return actionID;

            return Run(AdvancedSettings(aoe: true));
        }
    }

    #endregion

    #region Run

    private static BST_RotationLogic.BstSettings AdvancedSettings(bool aoe) => new()
    {
        AoE = aoe,
        MinFamiliarStaySeconds = BST_MinFamiliarStay,
        AllowPetlessCycling = BST_AllowPetlessCycling,
        UseFinalStingAsExit = BST_FinalStingAsExit,
        AllowDisplacingRelease = BST_AllowDisplacingRelease,
        AllowSleepRelease = BST_AllowSleepRelease,
        BorrowWhileReleaseRecasts = BST_BorrowWhileReleaseRecasts,
        SummonBeforeCombat = BST_SummonBeforeCombat,
        RefreshBetweenPulls = BST_RefreshBetweenPulls,
        UseBeastskin = BST_UseBeastskin,
        UseVileskin = BST_UseVileskin,
        UseSeedsower = BST_UseSeedsower,
        UseScaleskin = BST_UseScaleskin,
        UseSoulCrush = BST_UseSoulCrush,
        UseQuellingWaveRanged = BST_UseQuellingWaveRanged,
        UseShieldCharge = BST_UseShieldCharge,
        UseRally = BST_UseRally,
    };

    private static uint Run(in BST_RotationLogic.BstSettings cfg)
    {
        if (LocalPlayer is null)
            return SmashAxe;

        var state = ReadState();
        var decision = BST_RotationLogic.Decide(state, cfg);

        LastDecisionActionId = decision.ActionId;
        LastDecisionReason = decision.Reason;
        FamiliarDeclineReason = decision.Declines;
        LastSlotBeasts = $"{state.Slot1Beast}.{state.Slot2Beast}.{state.Slot3Beast}";

        return decision.ActionId;
    }

    /// <summary> Last decision taken, sampled by <see cref="GluttonyCombo.Data.BeastmasterTelemetry"/> into BT|dec=. </summary>
    internal static uint LastDecisionActionId;
    internal static string LastDecisionReason = "";

    /// <summary> Why the engine declined familiar / combo steps on the last tick (BT|fd=). </summary>
    internal static string FamiliarDeclineReason = "";

    /// <summary> XBMPet rows assigned to horns 1.2.3 on the last tick (BT|sl=). </summary>
    internal static string LastSlotBeasts = "";

    #endregion

    #region Live state

    private static int _lastSlot;
    private static long _familiarArrivedTick;
    private static long _petHeartTick;

    private static float SecondsSince(long tick) =>
        tick == 0 ? float.MaxValue : (Environment.TickCount64 - tick) / 1000f;

    private static float SinceUsed(uint actionId)
    {
        var t = TimeSinceActionUsed(actionId);
        return t < 0 ? float.MaxValue : t;
    }

    private static ushort HeartStatusFor(BeastmasterAffinity affinity) => affinity switch
    {
        BeastmasterAffinity.Volant => Buffs.VolantHeart,
        BeastmasterAffinity.Rampant => Buffs.RampantHeart,
        BeastmasterAffinity.Durant => Buffs.DurantHeart,
        BeastmasterAffinity.Eldritch => Buffs.EldritchHeart,
        _ => 0,
    };

    private static bool HasAnyStatus(ushort from, ushort to)
    {
        for (var id = from; id <= to; id++)
            if (HasStatusEffect(id))
                return true;
        return false;
    }

    internal static unsafe BST_RotationLogic.BstState ReadState()
    {
        var gauge = Gauge;
        var now = Environment.TickCount64;
        var player = LocalPlayer!;

        int slot = gauge.ActiveBattlehorn;
        if (slot != _lastSlot)
        {
            if (slot != 0)
                _familiarArrivedTick = now;
            _lastSlot = slot;
        }

        var s = new BST_RotationLogic.BstState
        {
            Level = player.Level,
            InCombat = InCombat(),
            HasHostileTarget = HasBattleTarget(),
            PlayerIsCasting = player.IsCasting,
            IsMoving = IsMoving(),
            PlayerTargetedByEnemy = IsPlayerTargeted(),

            PlayerTp = gauge.TPGauge,
            FamiliarTp = gauge.FamiliarTPGauge,
            ActiveSlot = slot,
            ComboState = (BeastmasterAffinity)gauge.InstinctualComboState,
            LastAffinity = gauge.CurrentAffinity,
            KinshipSlot = gauge.KinshipBattlehorn,
            MasterStacks = gauge.MasterInstinct,
            NaturalStacks = gauge.PetInstinct,

            GcdReady = RemainingGCD <= BaseActionQueue,
            CanWeave = CanWeave(),
            SinceSummon = slot != 0 ? SecondsSince(_familiarArrivedTick) : 0f,
            SinceHornPress = Math.Min(SinceUsed(FirstBattlehorn), Math.Min(SinceUsed(SecondBattlehorn), SinceUsed(ThirdBattlehorn))),
            SinceTrick = SinceUsed(Trick),
            SinceTempered = Math.Min(SinceUsed(TemperedRelease), SinceUsed(TemperedReleaseTargeted)),
            SinceBorrow = SinceUsed(Borrow),
            TemperedRecastRemaining = GetCooldownRemainingTime(TemperedRelease),

            LastComboAction = ComboAction,
            ComboTimerActive = ComboTimer > 0,
        };

        if (s.HasHostileTarget)
        {
            s.TargetDistance = GetTargetDistance();
            s.TargetHpPercent = GetTargetHPPercent();
            s.TargetInterruptible = CanInterruptEnemy();
        }
        else
        {
            s.TargetDistance = float.MaxValue;
            s.TargetHpPercent = 100f;
        }

        s.EnemiesWithin6y = s.InCombat ? NumberOfEnemiesInRange(Seedsower) : 0;

        // Horn assignments: ActionManager.BeastmasterPets = XBMPet row per horn (ClientStructs 385b0821,
        // present in Dalamud 15.0.3.4). All zeros means unreadable or nothing assigned: fall back to
        // ActionReady alone rather than refusing every horn.
        var manager = ActionManager.Instance();
        if (manager is not null)
        {
            var pets = manager->BeastmasterPets;
            s.Slot1Beast = pets.Length > 0 ? pets[0] : 0;
            s.Slot2Beast = pets.Length > 1 ? pets[1] : 0;
            s.Slot3Beast = pets.Length > 2 ? pets[2] : 0;
            s.SlotBeastsKnown = s.Slot1Beast != 0 || s.Slot2Beast != 0 || s.Slot3Beast != 0;
        }

        // Pet object: counts as present only while the familiar is out or a summon is in flight (object
        // spawns at cast start, gauge slot ~1 s later). A RETREATING familiar's object lingers ~3.5 s after
        // the gauge clears; that must not delay the next summon (players resummon ~0.9 s after Parting
        // Blow, live 2026-09-09 19:08:36.988 -> 19:08:37.879).
        var petObject = Svc.Buddies.PetBuddy?.GameObject;
        if (petObject is not null)
        {
            s.PetObjectBeast = BST_Beasts.RowFromBNpcBase(petObject.BaseId);
            s.PetObjectPresent = slot != 0 || s.SinceHornPress < 4f;
        }

        // Player statuses
        s.OneWithNature = HasStatusEffect(Buffs.OneWithNature);
        s.LingeringVantage = HasStatusEffect(Buffs.LingeringVantage);
        s.WaveringHeart = HasStatusEffect(Buffs.WaveringHeart);
        if (HasStatusEffect(Buffs.Sunstrider))
            s.SunMoon = BeastmasterAffinity.Sunstrider;
        else if (HasStatusEffect(Buffs.Moonstalker))
            s.SunMoon = BeastmasterAffinity.Moonstalker;
        s.SunOrMoonActive = s.SunMoon != BeastmasterAffinity.None;
        s.KinshipHeld = HasAnyStatus(Buffs.BeastKinship, Buffs.AshKinship) || HasAnyStatus(Buffs.BeastKinshipHeld, Buffs.AshKinshipHeld);

        // Last player axe (base axes carry a compass affinity; the L50 finishers do not).
        (uint Id, BeastmasterAffinity Affinity)[] axes =
        [
            (AvalancheAxe, BeastmasterAffinity.Rampant), (MistralAxe, BeastmasterAffinity.Durant),
            (SpinningAxe, BeastmasterAffinity.Eldritch), (GaleAxe, BeastmasterAffinity.Volant),
            (BrutalRage, BeastmasterAffinity.None), (HawkishTalons, BeastmasterAffinity.None),
            (RisenFall, BeastmasterAffinity.None), (Calamity, BeastmasterAffinity.None),
        ];
        s.SinceAxe = float.MaxValue;
        foreach (var (id, affinity) in axes)
        {
            var since = SinceUsed(id);
            if (since < s.SinceAxe)
            {
                s.SinceAxe = since;
                s.LastAxeAffinity = affinity;
            }
        }

        // The pet's Heart: after a Trick press, the Heart status of the pet's affinity refreshes to ~7 s
        // when the pet's skill lands (live lag p50 1.0 s, p99 2.5 s).
        var beast = BST_RotationLogic.ActiveBeast(s);
        if (beast is { } b && s.SinceTrick < BST_RotationLogic.PetHeartTimeoutSeconds + 1f && s.SinceAxe > s.SinceTrick)
        {
            var trickTick = now - (long)(s.SinceTrick * 1000f);
            var heart = HeartStatusFor(b.TrickAffinity);
            if (_petHeartTick < trickTick && heart != 0 && GetStatusEffect(heart) is { RemainingTime: > 6.2f })
                _petHeartTick = now;
        }
        s.SincePetHeart = SecondsSince(_petHeartTick);

        // Readiness (ActionReady covers level sync, recast and resources)
        s.ReadyHorn1 = ActionReady(FirstBattlehorn);
        s.ReadyHorn2 = ActionReady(SecondBattlehorn);
        s.ReadyHorn3 = ActionReady(ThirdBattlehorn);
        s.ReadyTrick = ActionReady(Trick);
        s.ReadyTempered = ActionReady(TemperedRelease);
        s.ReadyBorrow = ActionReady(Borrow);
        s.ReadyParting = ActionReady(PartingBlow);
        s.ReadyAxe = s.Level >= BST_RotationLogic.LvAvalancheAxe && GetCooldownRemainingTime(AvalancheAxe) <= BaseActionQueue;
        s.ReadyRally = ActionReady(Rally);
        s.ReadyCheer = ActionReady(RallyingCheer);

        var resolvedBeastMode = AdjustedActionId(BeastMode);
        s.BeastModeResolved = resolvedBeastMode == BeastMode ? 0 : resolvedBeastMode;
        s.ReadyBeastMode = s.BeastModeResolved != 0 && ActionReady(s.BeastModeResolved);

        s.ShieldChargeCharges = (int)GetRemainingCharges(ShieldCharge);
        s.ShieldChargeMax = GetMaxCharges(ShieldCharge);
        s.ReadyShieldCharge = ActionReady(ShieldCharge) && MovementGate.Allowed(ShieldCharge, MovementGate.GapCloserLanding());

        return s;
    }

    /// <summary> <c>ActionManager.GetAdjustedActionId</c>, safe when the manager is unavailable. </summary>
    private static unsafe uint AdjustedActionId(uint actionId)
    {
        var manager = ActionManager.Instance();
        return manager is null ? actionId : manager->GetAdjustedActionId(actionId);
    }

    #endregion
}
