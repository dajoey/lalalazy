#region Dependencies

using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System.Linq;
using GluttonyCombo.AutoRotation;
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

                // 5) Rally / Rallying Cheer: TP-restoring cooldowns that spend ALL accumulated
        //    Mastered / Natural Instinct stacks (Rally = +40 +70/stack player TP,
        //    Rallying Cheer = +30 +70/stack familiar TP). Fire when there are stacks to
        //    spend AND the matching TP pool is low enough to absorb the refund without
        //    wasting it - burning the banked stacks at (near-)full TP, or spending a 90-120s
        //    cooldown for the bare 30/40-point floor cast with zero stacks, both leak value.
        //    Stack counts read from gauge byte 0x10 (MasterInstinct bits 2-3, PetInstinct
        //    bits 0-1), mapped from WrathCombo's WIP Beastmaster work (mrbeastmaster, read
        //    2026-09-11) - an independent implementation of the same gauge that also
        //    re-derives 0x08-0x0F exactly as this overlay does. This REPLACES the old
        //    TP-only proxy (t_32af951a's documented open question), which never fired once
        //    across the 1771-line BT| corpus. If live play shows the 0x10 nibbles never
        //    moving, the proxy question re-opens - grade against the 9th BT| gauge-hex pair (byte 0x10).
        var rally = BST_RotationLogic.ChooseRally(
            masterStacks: gauge.MasterInstinct,
            petStacks: gauge.PetInstinct,
            playerTp: gauge.TPGauge,
            familiarTp: gauge.FamiliarTPGauge,
            familiarOut: gauge.ActiveBattlehorn != 0,
            rallyLearned: LocalPlayer.Level >= GetActionLevel(Rally),
            cheeringLearned: LocalPlayer.Level >= GetActionLevel(RallyingCheer));

        if (rally != 0 && CanWeave() && ActionReady(rally))
            return Record(rally, rally == Rally ? "rally:stacks" : "rallyingcheer:stacks");

// 5b) Quelling Wave: the sole Beast Mode variant that rolls the player's OWN shared GCD
        //     (CooldownGroup 58 - the same group Smash Axe/Axeblade Bite/Shieldsplitter share),
        //     rather than being an independent oGCD like the other seven Kinship variants.
        //     Deliberately checked here, NOT inside TryBeastMode's CanWeave()-gated call at
        //     step 1: CanWeave() is true only while there is still slack before the GCD is next
        //     due, which is roughly the OPPOSITE moment from "the GCD is actually up" that a
        //     GCD-rolling action needs - gating Quelling Wave the same way as its oGCD siblings
        //     made it effectively unreachable through auto-rotation (t_32af951a;
        //     beastmaster-rotation-spec.md section 6 defect 5 follow-up;
        //     beastmaster-kit-by-level.md section 1's CooldownGroup table). Checked immediately
        //     ahead of the GCD chain fallback so it pre-empts Smash Axe/Axeblade Bite/
        //     Shieldsplitter whenever Wave Kinship is active and the shared GCD is ready.
        if (TryQuellingWave(preferAoEBeastMode, out var quellingAction, out var quellingReason))
            return Record(quellingAction, quellingReason);

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

    /// <summary>
    ///     Why the FAMILIAR LOOP declined to act on the last tick that it declined at all
    ///     (t_f987910c fix 4). Sampled by <see cref="GluttonyCombo.Data.BeastmasterTelemetry"/>
    ///     into the BT|fd= field. Carried separately from <see cref="LastDecisionReason"/>
    ///     because the rotation still decides gcdchain/instinctual on a tick where the
    ///     familiar subtree is skipped - writing a decline into dec= would be overwritten
    ///     by the terminal Record() before the collector ever sampled it. Empty when the
    ///     familiar loop acted or had nothing to say.
    /// </summary>
    internal static string FamiliarDeclineReason = "";

    #endregion

    #region Instinctual (Compass)

    private static bool TryInstinctual(BeastmasterGaugeOverlay gauge, out uint actionId, out string reason)
    {
        actionId = 0;
        reason = "";

        // Fixed t_f04d4c83: the open-fresh fallback previously hardcoded Volant (Gale
        // Axe) unconditionally, which is unlearned below lv16 - a sub-16 player with an
        // open TP bar and no compass window fell through to the GCD chain instead of
        // firing an axe they had actually earned. Open at the highest-level LEARNED axe.
        var baseAction = BST_RotationLogic.ChooseInstinctual(
            gauge.TPGauge, gauge.InstinctualComboState, gauge.CurrentAffinity,
            AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe,
            durantLearned: LocalPlayer.Level >= GetActionLevel(MistralAxe),
            eldritchLearned: LocalPlayer.Level >= GetActionLevel(SpinningAxe),
            volantLearned: LocalPlayer.Level >= GetActionLevel(GaleAxe));

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
        FamiliarDeclineReason = "";

        var gauge = Gauge;

        // Pet presence is TWO signals, not one (t_f987910c): the gauge slot byte AND the live
        // pet-buddy object (the SCH/SMN idiom). The gauge byte alone reads "summoned" for a
        // familiar that has died or despawned, which pins the whole loop to steps only a LIVE
        // familiar can take. The object appears when the summon cast STARTS, so while
        // HasPetPresent() is true but the slot byte is still 0 the summon is mid-flight -
        // stay quiet for that gap instead of re-issuing Battlehorn on top of it.
        var gaugeSummoned = gauge.ActiveBattlehorn != 0;
        var petPresent = HasPetPresent();
        var petSummoned = gaugeSummoned && petPresent;
        var summonInFlight = !gaugeSummoned && petPresent;

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

        var summonDecline = "";
        var tried = new List<string>();

        if (summonInFlight)
        {
            // A summon cast is already on its way - re-issuing Battlehorn on top of it
            // would queue a second summon behind it. Report and wait.
            FamiliarDeclineReason = "battlehorn:summon-in-flight";
            return false;
        }

        if (!petSummoned)
        {
            var preferredSlot = advanced ? (byte)BST_BattlehornSlotOrder : (byte)0;

            // Second Battlehorn unlocks L10, Third unlocks L20 - a player below L20 whose
            // rotation state would otherwise land on slot 3 must not be handed a slot they
            // haven't learned (defect 4, beastmaster-rotation-spec.md §6). maxLearnedSlot
            // starts at 1 (First Battlehorn, always learned) and steps up as the two later
            // tiers unlock.
            byte maxLearnedSlot = 1;
            if (LocalPlayer.Level >= GetActionLevel(SecondBattlehorn)) maxLearnedSlot = 2;
            if (LocalPlayer.Level >= GetActionLevel(ThirdBattlehorn)) maxLearnedSlot = 3;

            var nextSlot = BST_RotationLogic.NextBattlehornSlot(gauge.KinshipBattlehorn, preferredSlot, maxLearnedSlot);
            var battlehornAction = nextSlot switch
            {
                2 => SecondBattlehorn,
                3 => ThirdBattlehorn,
                _ => FirstBattlehorn,
            };

            if (!ActionReady(battlehornAction))
            {
                // WHY the resummon is not happening is the observable a grader needs
                // (t_f987910c fix 4): Beast Voice (SecondaryCostType 155) is Battlehorn's
                // own resource, and in combat the recast only begins once the familiar has
                // retreated - name which one blocked it.
                FamiliarDeclineReason = GetCooldownRemainingTime(battlehornAction) > 0
                    ? $"battlehorn:declined-recast-slot{nextSlot}"
                    : $"battlehorn:declined-beastvoice-slot{nextSlot}";
                return false;
            }

            actionId = battlehornAction;
            reason = $"battlehorn:slot{nextSlot}";
            return true;
        }

        // One with Nature (4601) is the Primary cost gate of BOTH Borrow and Tempered
        // Release. Reading it from the game (not inferring it) is what keeps a stale
        // "not used this summon" timestamp from re-offering an ability the game will
        // refuse - the status vanishes seconds after the cast, while the timestamp that
        // says "used" ages out of the action-history cache ~30-60 s into a summon
        // (t_f987910c root cause).
        var oneWithNatureUp = HasStatusEffect(Buffs.OneWithNature);

        var candidates = BST_RotationLogic.ChooseFamiliarCandidates(
            borrowedThisSummon, temperedThisSummon,
            gauge.FamiliarTPGauge, oneWithNatureUp, lingeringVantage, holdForVantage,
            borrowLearned, temperedLearned);

        summonDecline = string.Join(",",
            candidates.Select(c => c.Decline)
                      .Where(s => !string.IsNullOrEmpty(s)));

        // FALL-THROUGH (t_f987910c fix 1): walk DOWN the candidate list when the game
        // refuses a step. One uncastable offered step used to return false here and kill
        // the ENTIRE familiar subtree for the rest of the summon - the D6 stall that left
        // familiar TP pegged at max for 25+ seconds with zero Trick decisions.
        foreach (var (step, _) in candidates.Where(c => c.Step != BST_RotationLogic.FamiliarStep.None))
        {
            switch (step)
            {
                case BST_RotationLogic.FamiliarStep.Borrow:
                    if (!ActionReady(Borrow))
                    {
                        tried.Add("borrow");
                        continue;
                    }
                    actionId = Borrow;
                    reason = "borrow";
                    return true;

                case BST_RotationLogic.FamiliarStep.TemperedRelease:
                    if (!ActionReady(TemperedRelease))
                    {
                        tried.Add("temperedrelease");
                        continue;
                    }
                    actionId = TemperedRelease;
                    reason = "temperedrelease";
                    return true;

                case BST_RotationLogic.FamiliarStep.Trick:
                    if (!ActionReady(Trick))
                    {
                        tried.Add("trick");
                        continue;
                    }
                    actionId = Trick;
                    reason = "trick";
                    return true;

                case BST_RotationLogic.FamiliarStep.PartingBlow:
                    if (!ActionReady(PartingBlow))
                    {
                        tried.Add("partingblow");
                        continue;
                    }
                    actionId = PartingBlow;
                    reason = lingeringVantage ? "partingblow:vantage" : "partingblow:norush";
                    return true;
            }
        }

        // Every candidate the loop offered was refused by the game. Carry the WHY out to
        // the BT| line (fd=) instead of silently eating the tick: the pure-half declines
        // (per-summon state / 4601) when there are any, otherwise which steps the game
        // itself refused (recast / resource).
        FamiliarDeclineReason = summonDecline.Length > 0
            ? summonDecline
            : tried.Count > 0
                ? "familiarloop:refused-" + string.Join("+", tried)
                : "familiarloop:no-eligible-step";
        return false;
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

        // Quelling Wave is handled separately by TryQuellingWave (see ChooseAction step 5b) -
        // it is the sole Kinship variant that rolls the player's own shared GCD rather than
        // being an independent oGCD, so it must NOT be gated by the same CanWeave() wrapper
        // this method's caller applies to the other seven variants (t_32af951a;
        // beastmaster-rotation-spec.md section 6 defect 5 follow-up).
        if (resolved == QuellingWave)
            return false;

        return false;
    }

    /// <summary>
    ///     Quelling Wave: the sole Beast Mode Kinship variant that rolls the player's OWN
    ///     shared GCD (CooldownGroup 58) instead of being an independent oGCD - see
    ///     <see cref="BST_RotationLogic.IsGcdRollingBeastMode"/> and ChooseAction step 5b for
    ///     why this must be checked outside a CanWeave() gate. Resolves the Beast Mode
    ///     placeholder itself (same as TryBeastMode) so this can run standalone regardless of
    ///     whether TryBeastMode's CanWeave()-gated call already declined this tick.
    /// </summary>
    private static bool TryQuellingWave(bool preferAoE, out uint actionId, out string reason)
    {
        actionId = 0;
        reason = "";

        var resolved = AdjustedActionId(BeastMode);

        if (!BST_RotationLogic.IsGcdRollingBeastMode(resolved, QuellingWave))
            return false;

        // Seedsower's DoT+damage-down is worth prioritising in the AoE preset; ST prefers
        // Quelling Wave's TP restore.
        if (preferAoE && GetCooldownRemainingTime(Seedsower) <= 0)
            return false;

        // GCD readiness, not weave-window readiness: ActionReady on the RESOLVED action id
        // (a Spell, CooldownGroup 58) checks the shared-GCD cooldown/queue window directly,
        // which is exactly the gate a GCD-rolling action needs - CanWeave() checks the
        // opposite thing (slack before the GCD is next due) and must not be used here.
        if (!ActionReady(QuellingWave))
            return false;

        actionId = resolved;
        reason = "beastmode:quellingwave";
        return true;
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

        // Policy A (t_8d711ea6): both arms displace - the gap-closer obviously,
        // and the overcharge arm too, since Shield Charge still dashes to the
        // target. Pass the shared safety gate before either may fire.
        if (!MovementGate.Allowed(ShieldCharge, MovementGate.GapCloserLanding()))
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
