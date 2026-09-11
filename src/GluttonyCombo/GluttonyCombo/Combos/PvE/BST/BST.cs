#region Dependencies

using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Linq;
using GluttonyCombo.CustomComboNS;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using static GluttonyCombo.Combos.PvE.BST.Config;

#endregion

namespace GluttonyCombo.Combos.PvE;

// Beastmaster (Job.BST = 43) ROTATION (t_02fe2681). Builds on the plumbing skeleton
// (t_f719ab97, BST_Gauge.cs / BST_Helper.cs) and the compass/familiar-loop pure logic in
// BST_RotationLogic.cs (proven offline in tests/GluttonyCombo.BSTRotationHarness before this
// file was written, per skill lalalazy-ffxiv references/rotation-logic-and-preset-actions.md).
//
// VERIFICATION STATUS (honest per-rule, 2026-09-10 GATE OVERRIDE - only 61 BT| lines exist,
// spanning mostly pre-combat gauge movement with a couple of real TP/battlehorn transitions;
// no compass chain or Wavering Heart lockout was directly observed in the sample):
//   VERIFIED against live BT| bytes: TP gauge moves (0x00->0x52..0x5f), battlehorn slot
//     1/2 tracked correctly, pet identity resolves (Wespe/Bat/Crab via Svc.Buddies.PetBuddy),
//     bm=44886/av=44884 (unadjusted pre-Kinship, matches "no familiar summoned yet" state).
//   NOT YET VERIFIED against live combat: the compass clockwise selection, the Wavering Heart
//     (state==7) lockout, the L50 Sunstrider/Moonstalker finisher swap, and the familiar
//     Borrow->Tempered->Trick->PartingBlow ordering - the datamine sample never reached a
//     TP>=100 instinctual press or a full familiar loop cycle. These are proven against the
//     RULES OF THE JOB text (card body) via the offline harness, not against real play.
//     Grade the next BT|dec= samples against this file before trusting it un-reviewed.
internal partial class BST : Melee
{
    #region Single Target - Simple Mode

    internal class BST_ST_SimpleMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_ST_SimpleMode;

        protected override uint Invoke(uint actionID)
        {
            if (actionID != SmashAxe)
                return actionID;

            return ChooseAction(advanced: false).ActionId;
        }
    }

    #endregion

    #region Single Target - Advanced Mode

    internal class BST_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_ST_AdvancedMode;

        protected override uint Invoke(uint actionID)
        {
            if (actionID != SmashAxe)
                return actionID;

            return ChooseAction(advanced: true).ActionId;
        }
    }

    #endregion

    #region AoE - Simple Mode

    internal class BST_AoE_SimpleMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_AoE_SimpleMode;

        protected override uint Invoke(uint actionID)
        {
            if (actionID != SmashAxe)
                return actionID;

            // BST has no separate AoE weaponskill combo in the 7.56 datamine - Calamity (the
            // L50 Gale Axe/Volant upgrade under Moonstalker) is the only line-AoE hit, already
            // reachable through the normal compass finisher swap, and Seedsower (Beast Mode)
            // is the AoE DoT/mitigation variant. Route through the same core with AoE-favouring
            // Beast Mode priority (Seedsower over Quelling Wave) until a real AoE combo exists.
            return ChooseAction(advanced: false, preferAoEBeastMode: true).ActionId;
        }
    }

    #endregion

    #region Core Decision

    /// <summary> One decision, in priority order, plus the reason string recorded to BT|dec=. </summary>
    private static (uint ActionId, string Reason) ChooseAction(bool advanced, bool preferAoEBeastMode = false)
    {
        var gauge = Gauge;

        // 1) Beast Mode (oGCD, on cooldown) - interrupt priority over filler.
        if (CanWeave() && TryBeastMode(advanced, preferAoEBeastMode, out var beastAction, out var beastReason))
            return Record(beastAction, beastReason);

        // 2) Familiar loop: Battlehorn -> Borrow -> Tempered Release -> Trick -> Parting Blow.
        //    Battlehorn/Trick/PartingBlow have short (<=2s) casts/recasts - allow them outside
        //    a strict weave window too (delayed weave covers the loop's slower cadence),
        //    otherwise a fresh player with no familiar out could never open the loop.
        if ((CanWeave() || CanDelayedWeave()) && TryFamiliarStep(advanced, out var familiarAction, out var familiarReason))
            return Record(familiarAction, familiarReason);

        // 3) Instinctual weaponskill (own 5s recast, does not share the GCD - weaved).
        if (CanWeave() && TryInstinctual(gauge, out var instinctualAction, out var instinctualReason))
            return Record(instinctualAction, instinctualReason);

        // 4) Shield Charge: gap closer beyond melee range, or overcap filler at max charges.
        if (CanWeave() && TryShieldCharge(out var shieldReason))
            return Record(ShieldCharge, shieldReason);

        // 5) Rally / Rallying Cheer: TP-restoring 90s cooldowns. Mastered Instinct / Natural
        //    Instinct stack statuses were NOT resolved by the datamine (beastmaster-facts.md);
        //    gated on the gauge's own TP reading low plus cooldown ready as the best available
        //    proxy - re-check against the matching stack status once it is identified.
        if (CanWeave() && ActionReady(Rally) && gauge.TPGauge < 100)
            return Record(Rally, "rally:tp-low");

        if (CanWeave() && ActionReady(RallyingCheer) && gauge.FamiliarTPGauge < 100 && gauge.ActiveBattlehorn != 0)
            return Record(RallyingCheer, "rallyingcheer:familiartp-low");

        // 6) GCD chain fallback: Smash Axe -> Axeblade Bite -> Shieldsplitter.
        var gcd = BST_RotationLogic.ChooseGcdChain(ComboAction, ComboTimer > 0, SmashAxe, AxebladeBite, Shieldsplitter);
        return Record(gcd, "gcdchain");
    }

    private static (uint ActionId, string Reason) Record(uint actionId, string reason)
    {
        LastDecisionActionId = actionId;
        LastDecisionReason = reason;
        return (actionId, reason);
    }

    /// <summary> Last decision taken, sampled by <see cref="GluttonyCombo.Data.BeastmasterTelemetry"/> into the BT|dec= field. </summary>
    internal static uint LastDecisionActionId;
    internal static string LastDecisionReason = "";

    #endregion

    #region Instinctual (Compass)

    private static bool TryInstinctual(BeastmasterGaugeOverlay gauge, out uint actionId, out string reason)
    {
        actionId = 0;
        reason = "";

        var baseAction = BST_RotationLogic.ChooseInstinctual(
            gauge.TPGauge, gauge.InstinctualComboState, gauge.CurrentAffinity,
            AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe);

        if (baseAction == 0)
            return false;

        // ActionReady (not a raw cooldown check) - a not-yet-unlocked action reports
        // CooldownRemaining == 0 (it has never been used, so it isn't "on cooldown"),
        // which a bare `GetCooldownRemainingTime(x) > 0` gate reads as ready. ActionReady
        // additionally checks GetActionStatus, which is the actual level gate. Bug found
        // 2026-09-10 (Helm: "trying to use a level 22 ability when I'm level 17") - every
        // gate in this file below used the bare-cooldown pattern instead of ActionReady.
        if (!ActionReady(baseAction))
            return false;

        actionId = AdjustedActionId(baseAction);
        reason = actionId == baseAction
            ? "instinctual:precombo"
            : BST_RotationLogic.FinisherReason(actionId, BrutalRage, HawkishTalons, RisenFall, Calamity);
        return true;
    }

    #endregion

    #region Familiar Loop

    private static bool TryFamiliarStep(bool advanced, out uint actionId, out string reason)
    {
        actionId = 0;
        reason = "";

        var gauge = Gauge;
        var petSummoned = gauge.ActiveBattlehorn != 0;

        // "Used this summon" = used more recently than the most recent Battlehorn press,
        // across all three slots. Both Borrow and Tempered Release are RESET by a fresh
        // summon (RULES OF THE JOB), so this timestamp comparison is exactly that signal.
        var sinceBattlehorn = new[]
        {
            TimeSinceActionUsed(FirstBattlehorn),
            TimeSinceActionUsed(SecondBattlehorn),
            TimeSinceActionUsed(ThirdBattlehorn),
        }.Where(t => t >= 0).DefaultIfEmpty(float.MaxValue).Min();

        var sinceBorrow = TimeSinceActionUsed(Borrow);
        var sinceTempered = TimeSinceActionUsed(TemperedRelease);

        var borrowedThisSummon = petSummoned && sinceBorrow >= 0 && sinceBorrow < sinceBattlehorn;
        var temperedThisSummon = petSummoned && sinceTempered >= 0 && sinceTempered < sinceBattlehorn;

        // Borrow (lv22) and Tempered Release (lv18) are learned at different levels than
        // Trick (lv8) - a sub-22 player's loop must skip straight past whichever of the two
        // it hasn't unlocked yet, or it stalls forever waiting on an ability ActionReady will
        // never report ready (Helm: "Not using trick", 2026-09-10).
        var borrowLearned = LocalPlayer.Level >= GetActionLevel(Borrow);
        var temperedLearned = LocalPlayer.Level >= GetActionLevel(TemperedRelease);

        // Lingering Vantage cannot exist below lv22 (Borrow's own unlock is the floor for any
        // Vantage grant - skill lalalazy-ffxiv references/beastmaster-kit-by-level.md, traits
        // 749/750), so a pre-22 hold-for-vantage config can never be satisfied and must not be
        // allowed to stall the familiar loop forever. Below lv22, Parting Blow always fires the
        // instant Trick spends the familiar's TP, in BOTH modes, regardless of the toggle -
        // previously "!advanced" alone forced holdForVantage=true in Simple Mode no matter
        // the toggle's value (Helm: "still not casting Trick, pet TP just stays at 100%",
        // 2026-09-10 / t_4c7923b0 defect 1 / t_3d88b4fd).
        var holdForVantage = BST_RotationLogic.ComputeHoldForVantage(borrowLearned, BST_HoldPartingBlowForVantage);
        var lingeringVantage = HasStatusEffect(Buffs.LingeringVantage);

        var step = BST_RotationLogic.ChooseFamiliarStep(
            petSummoned, borrowedThisSummon, temperedThisSummon,
            gauge.FamiliarTPGauge, lingeringVantage, holdForVantage,
            borrowLearned, temperedLearned);

        switch (step)
        {
            case BST_RotationLogic.FamiliarStep.Battlehorn:
                var preferredSlot = advanced ? (byte)BST_BattlehornSlotOrder : (byte)0;
                var nextSlot = BST_RotationLogic.NextBattlehornSlot(gauge.KinshipBattlehorn, preferredSlot);
                var battlehornAction = nextSlot switch
                {
                    2 => SecondBattlehorn,
                    3 => ThirdBattlehorn,
                    _ => FirstBattlehorn,
                };

                if (!ActionReady(battlehornAction))
                    return false;

                actionId = battlehornAction;
                reason = $"battlehorn:slot{nextSlot}";
                return true;

            case BST_RotationLogic.FamiliarStep.Borrow:
                if (!ActionReady(Borrow))
                    return false;
                actionId = Borrow;
                reason = "borrow";
                return true;

            case BST_RotationLogic.FamiliarStep.TemperedRelease:
                if (!ActionReady(TemperedRelease))
                    return false;
                actionId = TemperedRelease;
                reason = "temperedrelease";
                return true;

            case BST_RotationLogic.FamiliarStep.Trick:
                if (!ActionReady(Trick))
                    return false;
                actionId = Trick;
                reason = "trick";
                return true;

            case BST_RotationLogic.FamiliarStep.PartingBlow:
                if (!ActionReady(PartingBlow))
                    return false;
                actionId = PartingBlow;
                reason = lingeringVantage ? "partingblow:vantage" : "partingblow:norush";
                return true;

            default:
                return false;
        }
    }

    #endregion

    #region Beast Mode

    private static bool TryBeastMode(bool advanced, bool preferAoE, out uint actionId, out string reason)
    {
        actionId = 0;
        reason = "";

        if (!ActionReady(BeastMode))
            return false;

        var resolved = AdjustedActionId(BeastMode);

        // Beastskin / Vileskin / Scaleskin are mitigation - off by default in autorotation,
        // config-gated (RULES OF THE JOB). Cloud Skim is movement-only, never auto.
        if (resolved is Beastskin or Vileskin or Scaleskin)
        {
            if (advanced && BST_IncludeFamiliarMitigation && PlayerHealthPercentageHp() <= 80)
            {
                actionId = resolved;
                reason = "beastmode:mitigation";
                return true;
            }
            return false;
        }

        if (resolved == CloudSkim)
            return false; // movement only

        if (resolved == SoulCrush)
        {
            // Interrupt: only when the target is actually casting.
            if (CurrentTarget is IBattleChara { IsCasting: true })
            {
                actionId = resolved;
                reason = "beastmode:soulcrush-interrupt";
                return true;
            }
            return false;
        }

        if (resolved == Seedsower)
        {
            actionId = resolved;
            reason = "beastmode:seedsower";
            return true;
        }

        if (resolved == QuellingWave)
        {
            // Seedsower's DoT+damage-down is worth prioritising in the AoE preset; ST prefers
            // Quelling Wave's TP restore.
            if (preferAoE && GetCooldownRemainingTime(Seedsower) <= 0)
                return false;

            actionId = resolved;
            reason = "beastmode:quellingwave";
            return true;
        }

        return false;
    }

    #endregion

    #region Shield Charge

    private static bool TryShieldCharge(out string reason)
    {
        reason = "";

        if (!ActionReady(ShieldCharge))
            return false;

        if (GetRemainingCharges(ShieldCharge) == 0)
            return false;

        if (HasBattleTarget() && GetTargetDistance() > 3f)
        {
            reason = "shieldcharge:gapcloser";
            return true;
        }

        if (GetRemainingCharges(ShieldCharge) >= GetMaxCharges(ShieldCharge))
        {
            reason = "shieldcharge:overcap";
            return true;
        }

        return false;
    }

    #endregion

    #region Native Helpers

    /// <summary> <c>ActionManager.GetAdjustedActionId</c>, safe when the manager is unavailable. </summary>
    private static unsafe uint AdjustedActionId(uint actionId)
    {
        var manager = ActionManager.Instance();
        return manager is null ? actionId : manager->GetAdjustedActionId(actionId);
    }

    #endregion
}
