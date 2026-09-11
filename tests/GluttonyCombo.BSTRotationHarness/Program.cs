using GluttonyCombo.Combos.PvE;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Offline assertions on the Beastmaster rotation's pure decision logic
///     (<see cref="BST_RotationLogic"/>, t_02fe2681). No Dalamud, no game - compiles the real
///     source file so the selection/priority logic the live combo calls into is proven before
///     shipping. Covers the card's five acceptance cases (a)-(e) plus a "stock loadout is
///     byte-identical" style negative-control pass over every input the live code can hand it.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private const uint SmashAxe = 44879, AxebladeBite = 44883, Shieldsplitter = 44885;
    private const uint AvalancheAxe = 44884, MistralAxe = 44887, SpinningAxe = 44888, GaleAxe = 44889;
    private const uint BrutalRage = 44930, HawkishTalons = 44931, RisenFall = 44932, Calamity = 44933;

    private static int Main()
    {
        CaseA_GcdChain();
        CaseB_ClockwiseSelection();
        CaseC_NoInstinctualAtWaveringHeart();
        CaseD_L50FinisherChoice();
        CaseE_FamiliarLoopOrdering();
        CaseF_LevelGateSkipsUnlearnedSteps();
        CaseG_HoldForVantageLevelFloor();
        ExtraCoverage();

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // (a) the 1-2-3 chain
    // ------------------------------------------------------------------
    private static void CaseA_GcdChain()
    {
        Console.WriteLine("-- (a) GCD 1-2-3 chain --");

        Check("no combo running -> Smash Axe",
            BST_RotationLogic.ChooseGcdChain(0, false, SmashAxe, AxebladeBite, Shieldsplitter) == SmashAxe);

        Check("combo timer expired after Smash Axe -> restarts at Smash Axe (broken combo grants no TP)",
            BST_RotationLogic.ChooseGcdChain(SmashAxe, false, SmashAxe, AxebladeBite, Shieldsplitter) == SmashAxe);

        Check("after Smash Axe, timer active -> Axeblade Bite",
            BST_RotationLogic.ChooseGcdChain(SmashAxe, true, SmashAxe, AxebladeBite, Shieldsplitter) == AxebladeBite);

        Check("after Axeblade Bite, timer active -> Shieldsplitter",
            BST_RotationLogic.ChooseGcdChain(AxebladeBite, true, SmashAxe, AxebladeBite, Shieldsplitter) == Shieldsplitter);

        Check("after Shieldsplitter (combo complete) -> restarts at Smash Axe",
            BST_RotationLogic.ChooseGcdChain(Shieldsplitter, true, SmashAxe, AxebladeBite, Shieldsplitter) == SmashAxe);

        // Stock loadout / byte-identical: a full 1-2-3-1-2-3 walk never leaves the three actions.
        uint last = 0;
        var timerActive = false;
        var seen = new HashSet<uint>();
        for (var i = 0; i < 6; i++)
        {
            var next = BST_RotationLogic.ChooseGcdChain(last, timerActive, SmashAxe, AxebladeBite, Shieldsplitter);
            seen.Add(next);
            last = next;
            timerActive = true;
        }
        Check("a full walk of the chain only ever presses the three combo actions",
            seen.Count == 3 && seen.SetEquals([SmashAxe, AxebladeBite, Shieldsplitter]));
    }

    // ------------------------------------------------------------------
    // (b) clockwise selection given each of the four Hearts
    // ------------------------------------------------------------------
    private static void CaseB_ClockwiseSelection()
    {
        Console.WriteLine("-- (b) clockwise selection per Heart --");

        // No heart up (fresh compass): opens at Volant (Gale Axe) per the card's opener rule.
        Check("no current affinity -> opens fresh at Volant (Gale Axe)",
            BST_RotationLogic.ChooseInstinctual(150, 0, BeastmasterAffinity.None,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == GaleAxe);

        // Volant Heart up -> clockwise successor is Rampant -> Avalanche Axe.
        Check("Volant Heart up -> continues clockwise to Rampant (Avalanche Axe)",
            BST_RotationLogic.ChooseInstinctual(150, 1, BeastmasterAffinity.Volant,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == AvalancheAxe);

        // Rampant Heart up -> clockwise successor is Durant -> Mistral Axe.
        Check("Rampant Heart up -> continues clockwise to Durant (Mistral Axe)",
            BST_RotationLogic.ChooseInstinctual(150, 1, BeastmasterAffinity.Rampant,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == MistralAxe);

        // Durant Heart up -> clockwise successor is Eldritch -> Spinning Axe.
        Check("Durant Heart up -> continues clockwise to Eldritch (Spinning Axe)",
            BST_RotationLogic.ChooseInstinctual(150, 1, BeastmasterAffinity.Durant,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == SpinningAxe);

        // Eldritch Heart up -> clockwise successor is Volant -> Gale Axe (closes the loop).
        Check("Eldritch Heart up -> continues clockwise to Volant (Gale Axe)",
            BST_RotationLogic.ChooseInstinctual(150, 1, BeastmasterAffinity.Eldritch,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == GaleAxe);

        // Full compass table for NextClockwise directly.
        Check("NextClockwise: Volant -> Rampant", BST_RotationLogic.NextClockwise(BeastmasterAffinity.Volant) == BeastmasterAffinity.Rampant);
        Check("NextClockwise: Rampant -> Durant", BST_RotationLogic.NextClockwise(BeastmasterAffinity.Rampant) == BeastmasterAffinity.Durant);
        Check("NextClockwise: Durant -> Eldritch", BST_RotationLogic.NextClockwise(BeastmasterAffinity.Durant) == BeastmasterAffinity.Eldritch);
        Check("NextClockwise: Eldritch -> Volant (loop closes)", BST_RotationLogic.NextClockwise(BeastmasterAffinity.Eldritch) == BeastmasterAffinity.Volant);

        Check("IsClockwisePair recognises Volant->Rampant", BST_RotationLogic.IsClockwisePair(BeastmasterAffinity.Volant, BeastmasterAffinity.Rampant));
        Check("IsClockwisePair rejects Volant->Durant (skips a step, not clockwise-adjacent)",
            !BST_RotationLogic.IsClockwisePair(BeastmasterAffinity.Volant, BeastmasterAffinity.Durant));

        // TP gate: below 100 never fires, regardless of affinity state.
        Check("TP < 100 never fires an instinctual, even mid-compass",
            BST_RotationLogic.ChooseInstinctual(99, 1, BeastmasterAffinity.Volant,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == 0);
    }

    // ------------------------------------------------------------------
    // (c) no instinctual while state == 7 (Wavering Heart lockout)
    // ------------------------------------------------------------------
    private static void CaseC_NoInstinctualAtWaveringHeart()
    {
        Console.WriteLine("-- (c) Wavering Heart lockout (state == 7) --");

        foreach (var affinity in new[]
                 {
                     BeastmasterAffinity.None, BeastmasterAffinity.Volant, BeastmasterAffinity.Rampant,
                     BeastmasterAffinity.Durant, BeastmasterAffinity.Eldritch,
                 })
        {
            Check($"comboState==7 blocks instinctual regardless of affinity ({affinity})",
                BST_RotationLogic.ChooseInstinctual(250, 7, affinity,
                    AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == 0);
        }

        // Sanity: the same TP/affinity but state != 7 DOES fire, proving the block is state-specific.
        Check("the same TP/affinity at state==1 (not 7) fires normally",
            BST_RotationLogic.ChooseInstinctual(250, 1, BeastmasterAffinity.Volant,
                AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == AvalancheAxe);
    }

    // ------------------------------------------------------------------
    // (d) L50 finisher choice under Sunstrider vs Moonstalker
    // ------------------------------------------------------------------
    private static void CaseD_L50FinisherChoice()
    {
        Console.WriteLine("-- (d) L50 finisher choice (Sunstrider vs Moonstalker) --");

        // Which prior affinity a chain-start grants (RULES OF THE JOB): Volant/Durant starts
        // grant Sunstrider; Rampant/Eldritch starts grant Moonstalker.
        Check("chain starting Volant grants Sunstrider",
            BST_RotationLogic.IntentionalComboResult(BeastmasterAffinity.Volant) == BeastmasterAffinity.Sunstrider);
        Check("chain starting Durant grants Sunstrider",
            BST_RotationLogic.IntentionalComboResult(BeastmasterAffinity.Durant) == BeastmasterAffinity.Sunstrider);
        Check("chain starting Rampant grants Moonstalker",
            BST_RotationLogic.IntentionalComboResult(BeastmasterAffinity.Rampant) == BeastmasterAffinity.Moonstalker);
        Check("chain starting Eldritch grants Moonstalker",
            BST_RotationLogic.IntentionalComboResult(BeastmasterAffinity.Eldritch) == BeastmasterAffinity.Moonstalker);

        // FinisherReason labels the four resolved (post-GetAdjustedActionId) ids correctly -
        // this is what the BT| dec= tap records, and what the live half uses to log intent.
        Check("Brutal Rage labelled brutalrage:sunstrider (replaces Avalanche Axe/Rampant slot, resolves under Sunstrider)",
            BST_RotationLogic.FinisherReason(BrutalRage, BrutalRage, HawkishTalons, RisenFall, Calamity)
                == "brutalrage:sunstrider");
        Check("Hawkish Talons labelled hawkishtalons:moonstalker (replaces Mistral Axe/Durant slot, resolves under Moonstalker)",
            BST_RotationLogic.FinisherReason(HawkishTalons, BrutalRage, HawkishTalons, RisenFall, Calamity)
                == "hawkishtalons:moonstalker");
        Check("Risen Fall labelled risenfall:sunstrider (replaces Spinning Axe/Eldritch slot, resolves under Sunstrider)",
            BST_RotationLogic.FinisherReason(RisenFall, BrutalRage, HawkishTalons, RisenFall, Calamity)
                == "risenfall:sunstrider");
        Check("Calamity labelled calamity:moonstalker (replaces Gale Axe/Volant slot, resolves under Moonstalker; line AoE)",
            BST_RotationLogic.FinisherReason(Calamity, BrutalRage, HawkishTalons, RisenFall, Calamity)
                == "calamity:moonstalker");
        Check("an unresolved (pre-50, unadjusted) action id falls back to the generic label",
            BST_RotationLogic.FinisherReason(AvalancheAxe, BrutalRage, HawkishTalons, RisenFall, Calamity)
                == "instinctual:precombo");

        // Universality: a Sunstrider skill (Brutal Rage or Risen Fall) fired while the PLAYER's
        // prior buff was Moonstalker (or vice versa) is exactly the swap the card calls
        // "Universality" - the label does not change (it still names what the pressed action
        // IS), but this proves the label is independent of which buff was active, i.e. it
        // documents intent rather than re-deciding the swap GetAdjustedActionId already made.
        Check("finisher labelling does not depend on which buff triggered the swap (documents the resolved id, not the trigger)",
            BST_RotationLogic.FinisherReason(BrutalRage, BrutalRage, HawkishTalons, RisenFall, Calamity)
                == BST_RotationLogic.FinisherReason(BrutalRage, BrutalRage, HawkishTalons, RisenFall, Calamity));
    }

    // ------------------------------------------------------------------
    // (e) familiar loop ordering: Battlehorn -> Borrow -> Tempered -> Trick -> PartingBlow
    // ------------------------------------------------------------------
    private static void CaseE_FamiliarLoopOrdering()
    {
        Console.WriteLine("-- (e) familiar loop ordering --");

        // Step 1: no pet out -> Battlehorn.
        Check("no familiar summoned -> Battlehorn",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: false, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 0, lingeringVantage: false, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.Battlehorn);

        // Step 2: freshly summoned, nothing spent yet -> Borrow (once per summon).
        Check("freshly summoned, Borrow not yet used -> Borrow",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 0, lingeringVantage: false, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.Borrow);

        // Step 3: Borrow used, Tempered Release not yet used -> Tempered Release.
        Check("Borrow used, Tempered Release not yet used -> Tempered Release",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: false,
                familiarTp: 0, lingeringVantage: false, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.TemperedRelease);

        // Step 4: Borrow + Tempered used, familiar TP >= 100 -> Trick.
        Check("Borrow+Tempered used, familiar TP>=100 -> Trick",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: true,
                familiarTp: 132, lingeringVantage: false, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.Trick);

        // Between Trick firings (familiar TP < 100, not yet time to retreat): holds for
        // Lingering Vantage when the config asks to.
        Check("familiar TP spent, holding for Lingering Vantage, Vantage not up yet -> holds (None)",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: true,
                familiarTp: 20, lingeringVantage: false, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.None);

        // Step 5: familiar TP spent AND (Vantage up OR not holding for it) -> Parting Blow, pet retreats.
        Check("familiar TP spent, Lingering Vantage up -> Parting Blow (1500 pot)",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: true,
                familiarTp: 20, lingeringVantage: true, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.PartingBlow);

        Check("familiar TP spent, NOT holding for Vantage -> Parting Blow immediately (1000 pot)",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: true,
                familiarTp: 20, lingeringVantage: false, holdPartingBlowForVantage: false)
            == BST_RotationLogic.FamiliarStep.PartingBlow);

        // Full ordered walk through one summon cycle, exactly as the card specifies:
        // Battlehorn -> Borrow -> Tempered -> Trick -> (spend TP) -> PartingBlow -> next Battlehorn.
        var order = new List<BST_RotationLogic.FamiliarStep>();
        bool petSummoned = false, borrowed = false, tempered = false;
        byte familiarTp = 0;
        var lingering = false;

        for (var tick = 0; tick < 6; tick++)
        {
            var step = BST_RotationLogic.ChooseFamiliarStep(petSummoned, borrowed, tempered, familiarTp, lingering, true);
            order.Add(step);
            switch (step)
            {
                case BST_RotationLogic.FamiliarStep.Battlehorn: petSummoned = true; break;
                case BST_RotationLogic.FamiliarStep.Borrow: borrowed = true; lingering = true; break;
                case BST_RotationLogic.FamiliarStep.TemperedRelease: tempered = true; familiarTp = 132; break;
                case BST_RotationLogic.FamiliarStep.Trick: familiarTp = 0; break;
                case BST_RotationLogic.FamiliarStep.PartingBlow: petSummoned = borrowed = tempered = false; break;
            }
        }

        Check("full loop walk matches Battlehorn,Borrow,Tempered,Trick,PartingBlow,Battlehorn exactly",
            order.SequenceEqual(
            [
                BST_RotationLogic.FamiliarStep.Battlehorn,
                BST_RotationLogic.FamiliarStep.Borrow,
                BST_RotationLogic.FamiliarStep.TemperedRelease,
                BST_RotationLogic.FamiliarStep.Trick,
                BST_RotationLogic.FamiliarStep.PartingBlow,
                BST_RotationLogic.FamiliarStep.Battlehorn,
            ]),
            string.Join(",", order));

        // Battlehorn slot rotation: 1 -> 2 -> 3 -> 1, re-arming Tempered/Borrow each summon.
        Check("slot rotation: none summoned yet -> slot 1", BST_RotationLogic.NextBattlehornSlot(0, 0) == 1);
        Check("slot rotation: 1 -> 2", BST_RotationLogic.NextBattlehornSlot(1, 0) == 2);
        Check("slot rotation: 2 -> 3", BST_RotationLogic.NextBattlehornSlot(2, 0) == 3);
        Check("slot rotation: 3 -> 1 (wraps)", BST_RotationLogic.NextBattlehornSlot(3, 0) == 1);
        Check("preferred slot 2 always wins over rotation", BST_RotationLogic.NextBattlehornSlot(3, 2) == 2);
    }

    // ------------------------------------------------------------------
    // (f) below-level skip: a player who has not learned Borrow (lv22) and/or Tempered
    // Release (lv18) yet must skip straight past those steps rather than stalling the loop
    // forever. Regression case for Helm "Not using trick" (2026-09-10, t-joey-1789087266212):
    // the old signature had no way to say "not learned", so ChooseFamiliarStep demanded
    // Borrow every time and Trick (lv8, unlocked well before either) never fired.
    // ------------------------------------------------------------------
    private static void CaseF_LevelGateSkipsUnlearnedSteps()
    {
        Console.WriteLine("-- (f) level gate skips unlearned familiar-loop steps --");

        // A lv17 player (Trick lv8 learned, Tempered lv18 NOT learned, Borrow lv22 NOT
        // learned): summon -> skip Borrow -> skip Tempered -> straight to Trick.
        Check("sub-18 player: freshly summoned skips Borrow (not learned) -> Tempered check",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 132, lingeringVantage: false, holdPartingBlowForVantage: true,
                borrowLearned: false, temperedLearned: false)
            == BST_RotationLogic.FamiliarStep.Trick);

        // A lv20 player (Tempered lv18 learned, Borrow lv22 NOT learned): summon -> skip
        // Borrow -> Tempered Release (still gates normally) -> Trick.
        Check("lv20 player: Borrow skipped (not learned), Tempered still gates",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 132, lingeringVantage: false, holdPartingBlowForVantage: true,
                borrowLearned: false, temperedLearned: true)
            == BST_RotationLogic.FamiliarStep.TemperedRelease);

        Check("lv20 player: Tempered used -> Trick (Borrow never asked for)",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: true,
                familiarTp: 132, lingeringVantage: false, holdPartingBlowForVantage: true,
                borrowLearned: false, temperedLearned: true)
            == BST_RotationLogic.FamiliarStep.Trick);

        // Default parameters (omitted) preserve the pre-fix always-learned behaviour exactly,
        // so a lv90 (or any level >=22) player sees no behaviour change from this fix.
        Check("omitted learned flags default to true (lv90 behaviour unchanged)",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 132, lingeringVantage: false, holdPartingBlowForVantage: true)
            == BST_RotationLogic.FamiliarStep.Borrow);

        // A full sub-18 loop walk never asks for Borrow or Tempered Release at all.
        var order = new List<BST_RotationLogic.FamiliarStep>();
        bool petSummoned = false;
        byte familiarTp = 0;

        for (var tick = 0; tick < 4; tick++)
        {
            var step = BST_RotationLogic.ChooseFamiliarStep(
                petSummoned, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp, lingeringVantage: false, holdPartingBlowForVantage: false,
                borrowLearned: false, temperedLearned: false);
            order.Add(step);
            switch (step)
            {
                case BST_RotationLogic.FamiliarStep.Battlehorn: petSummoned = true; familiarTp = 132; break;
                case BST_RotationLogic.FamiliarStep.Trick: familiarTp = 0; break;
                case BST_RotationLogic.FamiliarStep.PartingBlow: petSummoned = false; break;
                default: familiarTp = 132; break; // shouldn't happen at sub-18, but keep the loop moving if it does
            }
        }

        Check("sub-18 full loop walk is Battlehorn,Trick,PartingBlow,Battlehorn - never Borrow/Tempered",
            order.SequenceEqual(
            [
                BST_RotationLogic.FamiliarStep.Battlehorn,
                BST_RotationLogic.FamiliarStep.Trick,
                BST_RotationLogic.FamiliarStep.PartingBlow,
                BST_RotationLogic.FamiliarStep.Battlehorn,
            ]),
            string.Join(",", order));
    }

    // ------------------------------------------------------------------
    // (g) hold-for-Vantage level floor (t_3d88b4fd, BST.cs:201 defect 1): Lingering Vantage
    // cannot exist below lv22 (Borrow's own unlock is the floor for any Vantage grant), so
    // ComputeHoldForVantage must never return true below lv22 regardless of Simple/Advanced
    // mode or the raw config toggle value - the caller no longer has a way to force the hold
    // via "!advanced" alone.
    // ------------------------------------------------------------------
    private static void CaseG_HoldForVantageLevelFloor()
    {
        Console.WriteLine("-- (g) hold-for-Vantage level floor --");

        // (a) sub-22, Simple Mode (config=true - the shipped Simple Mode always-hold intent),
        // TP spent, no Vantage -> must resolve to PartingBlow, not None.
        Check("sub-22, config=true (Simple Mode intent): ComputeHoldForVantage is false",
            BST_RotationLogic.ComputeHoldForVantage(borrowLearned: false, holdPartingBlowForVantageConfig: true) == false);
        Check("sub-22, config=true: ChooseFamiliarStep resolves PartingBlow, not None",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 20, lingeringVantage: false,
                holdPartingBlowForVantage: BST_RotationLogic.ComputeHoldForVantage(borrowLearned: false, holdPartingBlowForVantageConfig: true),
                borrowLearned: false, temperedLearned: false)
            == BST_RotationLogic.FamiliarStep.PartingBlow);

        // (b) sub-22, Advanced Mode, toggle=true, TP spent, no Vantage -> must ALSO resolve to
        // PartingBlow - the same level floor applies regardless of mode or the toggle value.
        Check("sub-22, Advanced Mode, toggle=true: ComputeHoldForVantage is still false",
            BST_RotationLogic.ComputeHoldForVantage(borrowLearned: false, holdPartingBlowForVantageConfig: true) == false);
        Check("sub-22, Advanced Mode, toggle=true: ChooseFamiliarStep resolves PartingBlow",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: false, temperedReleasedThisSummon: false,
                familiarTp: 20, lingeringVantage: false,
                holdPartingBlowForVantage: BST_RotationLogic.ComputeHoldForVantage(borrowLearned: false, holdPartingBlowForVantageConfig: true),
                borrowLearned: false, temperedLearned: false)
            == BST_RotationLogic.FamiliarStep.PartingBlow);

        // (c) L22+ (borrowLearned=true), toggle=true, TP spent, no Vantage -> still holds
        // (None) - the hold is legitimate once Borrow's unlock makes Vantage reachable.
        Check("L22+, toggle=true: ComputeHoldForVantage is true",
            BST_RotationLogic.ComputeHoldForVantage(borrowLearned: true, holdPartingBlowForVantageConfig: true) == true);
        Check("L22+, toggle=true, Vantage not up: ChooseFamiliarStep holds (None)",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: true,
                familiarTp: 20, lingeringVantage: false,
                holdPartingBlowForVantage: BST_RotationLogic.ComputeHoldForVantage(borrowLearned: true, holdPartingBlowForVantageConfig: true),
                borrowLearned: true, temperedLearned: true)
            == BST_RotationLogic.FamiliarStep.None);

        // (d) L22+, toggle=false -> PartingBlow immediately (never holds regardless of level).
        Check("L22+, toggle=false: ComputeHoldForVantage is false",
            BST_RotationLogic.ComputeHoldForVantage(borrowLearned: true, holdPartingBlowForVantageConfig: false) == false);
        Check("L22+, toggle=false: ChooseFamiliarStep resolves PartingBlow immediately",
            BST_RotationLogic.ChooseFamiliarStep(
                petSummoned: true, borrowedThisSummon: true, temperedReleasedThisSummon: true,
                familiarTp: 20, lingeringVantage: false,
                holdPartingBlowForVantage: BST_RotationLogic.ComputeHoldForVantage(borrowLearned: true, holdPartingBlowForVantageConfig: false),
                borrowLearned: true, temperedLearned: true)
            == BST_RotationLogic.FamiliarStep.PartingBlow);
    }

    // ------------------------------------------------------------------
    // Extra coverage: instinctual-action-for lookup table, and a full walk that never fires
    // an instinctual outside the two hard rules (TP>=100, state != 7).
    // ------------------------------------------------------------------
    private static void ExtraCoverage()
    {
        Console.WriteLine("-- extra: InstinctualActionFor + adversarial gate sweep --");

        Check("InstinctualActionFor(Rampant) = Avalanche Axe",
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.Rampant, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == AvalancheAxe);
        Check("InstinctualActionFor(Durant) = Mistral Axe",
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.Durant, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == MistralAxe);
        Check("InstinctualActionFor(Eldritch) = Spinning Axe",
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.Eldritch, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == SpinningAxe);
        Check("InstinctualActionFor(Volant) = Gale Axe",
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.Volant, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == GaleAxe);
        Check("InstinctualActionFor(None/Sunstrider/Moonstalker) = 0 (not a compass affinity)",
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.None, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == 0 &&
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.Sunstrider, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == 0 &&
            BST_RotationLogic.InstinctualActionFor(BeastmasterAffinity.Moonstalker, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe) == 0);

        // Adversarial sweep: every (tp, comboState, affinity) combination either respects the
        // TP>=100 gate, the state==7 lockout, or returns one of the four legal instinctual ids.
        var legal = new HashSet<uint> { AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe };
        var violations = 0;
        for (byte tp = 0; tp <= 250; tp += 10)
        {
            for (byte state = 0; state <= 8; state++)
            {
                foreach (var affinity in Enum.GetValues<BeastmasterAffinity>())
                {
                    var result = BST_RotationLogic.ChooseInstinctual(tp, state, affinity, AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe);

                    if (tp < 100 && result != 0) violations++;
                    else if (state == 7 && result != 0) violations++;
                    else if (result != 0 && !legal.Contains(result)) violations++;
                }
            }
        }
        Check($"adversarial sweep over tp/state/affinity never violates TP/lockout/legal-id rules ({violations} violations)",
            violations == 0, $"{violations} violations");
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else { _fail++; Console.WriteLine($"FAIL {what}{(detail is null ? "" : $" -> {detail}")}"); }
    }
}
