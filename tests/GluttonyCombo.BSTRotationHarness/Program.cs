using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Offline proof for the rebuilt Beastmaster engine (BST_RotationLogic, 2026-09-16).
///     Part 1: unit cases on the pure rules, including a replay of the 2026-09-16 17:48 test-session failure.
///     Part 2: a familiar-lifecycle SIMULATOR (<see cref="Sim"/>) built from the live-proven mechanics
///     (research/bst-live-evidence.md) that runs the real engine through 300 s fights at every level
///     1-50 with several loadouts, and asserts the safety invariants on every run.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static int _canary;

    private static int Main(string[] args)
    {
        var verbose = args.Contains("-v");

        Compass();
        ReleasePolicy();
        HornSelection();
        ComboOrdering();
        Replay_2026_09_16_1748();
        NoResummonBugFixed();
        VantageNeverStalls();
        AxesOnlyOnGcdReadyTicks();
        Level50FinisherAndRallyGate();
        OutOfCombat();
        CrucibleDataChecks();
        CrucibleRules();
        CrucibleSurvival();
        MasterBoardFights();
        CrucibleTargetingAndAdvisor();
        CrucibleHornWarning();
        GuideNeedsInRotation();
        DispelClass();
        ArmAndAimTheInterrupt();
    InterruptEconomy();
        AnswerAmmoCleanseDispel();
        ShieldChargeOvercap();
        CrucibleTactics();
        DashLandingSafety();
        PartingBlowExitBaseline();
        PartingBlowAoeGate();
        FallbackCandidates();
        LearnedFromTheRuns();
        TankbusterSnarlOnly();
        CrucibleDegreeAware();
        OptionalRecallHeldForTankbuster();

        SimulateAllLevels(verbose);
        SimulateCrucible(verbose);

        Console.WriteLine(_fail == 0
            ? _canary > 0 ? $"OK ({_pass} checks, canary failed as expected)" : $"OK ({_pass} checks)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    // ================================================================== unit cases

    private static void Compass()
    {
        Console.WriteLine("-- compass --");
        foreach (var a in new[] { BeastmasterAffinity.Volant, BeastmasterAffinity.Rampant, BeastmasterAffinity.Durant, BeastmasterAffinity.Eldritch })
        {
            Check($"CW then CCW returns {a}", CounterClockwise(Clockwise(a)) == a);
            Check($"four CW steps return {a}", Clockwise(Clockwise(Clockwise(Clockwise(a)))) == a);
        }
        Check("Volant -> Rampant clockwise", Clockwise(BeastmasterAffinity.Volant) == BeastmasterAffinity.Rampant);
        Check("Eldritch -> Volant clockwise", Clockwise(BeastmasterAffinity.Eldritch) == BeastmasterAffinity.Volant);
        Check("axe levels 4/8/14/16", AxeLevel(BeastmasterAffinity.Rampant) == 4 && AxeLevel(BeastmasterAffinity.Durant) == 8
                                       && AxeLevel(BeastmasterAffinity.Eldritch) == 14 && AxeLevel(BeastmasterAffinity.Volant) == 16);
        Check("no axe before L4", AnyLearnedAxe(3) == 0 && AnyLearnedAxe(4) == BST.AvalancheAxe);
    }

    private static void ReleasePolicy()
    {
        Console.WriteLine("-- release policy (per beast) --");
        var cfg = BstSettings.Defaults();

        var exits = BST_Beasts.All.Skip(1).Where(b => (b.Release & BeastmasterReleaseTraits.Exit) != 0).Select(b => b.Row).ToArray();
        Check("exactly one beast's release retreats the familiar: wespe (row 10)", exits.SequenceEqual(new byte[] { 10 }), string.Join(",", exits));
        Check("all 50 rows present and ordered", BST_Beasts.All.Skip(1).Select((b, i) => b.Row == i + 1).All(x => x));
        Check("BNpcBase 18925 -> wespe row 10", BST_Beasts.RowFromBNpcBase(18925) == 10);
        Check("BNpcBase 18930 -> crab row 15", BST_Beasts.RowFromBNpcBase(18930) == 15);
        Check("BNpcBase outside the block -> 0", BST_Beasts.RowFromBNpcBase(18915) == 0 && BST_Beasts.RowFromBNpcBase(18966) == 0);

        Check("wespe -> HoldForExit (never an opener)", PlanRelease(BST_Beasts.ByRow(10), cfg) == ReleasePlan.HoldForExit);
        Check("wespe with Final Sting exit off -> Blocked", PlanRelease(BST_Beasts.ByRow(10), cfg with { UseFinalStingAsExit = false }) == ReleasePlan.Blocked);
        Check("lamb (sleep) -> Blocked by default", PlanRelease(BST_Beasts.ByRow(3), cfg) == ReleasePlan.Blocked);
        Check("puk (knockback) -> Blocked by default", PlanRelease(BST_Beasts.ByRow(14), cfg) == ReleasePlan.Blocked);
        Check("puk allowed when displacing releases are allowed", PlanRelease(BST_Beasts.ByRow(14), cfg with { AllowDisplacingRelease = true }) == ReleasePlan.Use);
        Check("gigantoad (draw-in) -> Blocked by default", PlanRelease(BST_Beasts.ByRow(31), cfg) == ReleasePlan.Blocked);
        Check("Cu Sith -> Use", PlanRelease(BST_Beasts.ByRow(1), cfg) == ReleasePlan.Use);
        Check("raptor -> Use", PlanRelease(BST_Beasts.ByRow(34), cfg) == ReleasePlan.Use);
        Check("unknown beast -> Blocked (never gamble on wespe)", PlanRelease(null, cfg) == ReleasePlan.Blocked);
        Check("Flying Trap Trick flagged as knockback", BST_Beasts.ByRow(20)!.Value.TrickKnocksBack);
    }

    private static BstState BaseState(int level)
    {
        return new BstState
        {
            Level = level,
            InCombat = true,
            HasHostileTarget = true,
            TargetDistance = 3f,
            TargetHpPercent = 100f,
            EnemiesWithin6y = 1,
            GcdReady = false,
            CanWeave = true,
            SinceSummon = 0f,
            SinceHornPress = 60f,
            SinceTrick = float.MaxValue,
            SinceTempered = float.MaxValue,
            SinceBorrow = float.MaxValue,
            SinceAxe = float.MaxValue,
            SincePetHeart = float.MaxValue,
            SincePartingBlow = float.MaxValue,
            ShieldChargeMax = 1,
        };
    }

    private static void HornSelection()
    {
        Console.WriteLine("-- horn selection --");
        var s = BaseState(9);
        s.ReadyHorn1 = s.ReadyHorn2 = s.ReadyHorn3 = true;
        s.Slot1Beast = 1; s.Slot2Beast = 34; s.Slot3Beast = 26; s.SlotBeastsKnown = true;
        Check("L9: only horn 1 is learned", PickReadyHorn(s, 1) == 0 && PickReadyHorn(s, 0) == 1);
        s.Level = 10;
        Check("L10: horn 2 learned", PickReadyHorn(s, 1) == 2);
        Check("L10: horn 3 not learned", PickReadyHorn(s with { ReadyHorn2 = false }, 1) == 0);
        s.Level = 20;
        Check("L20: horn 3 learned", PickReadyHorn(s with { ReadyHorn2 = false }, 1) == 3);
        Check("empty slot is skipped when the slot table is readable", PickReadyHorn(s with { Slot2Beast = 0 }, 1) == 3);
        Check("unreadable slot table falls back to readiness only", PickReadyHorn(s with { Slot2Beast = 0, SlotBeastsKnown = false }, 1) == 2);
        Check("slot on lockout is skipped", PickReadyHorn(s with { ReadyHorn1 = false }, 0) == 2);
    }

    private static void ComboOrdering()
    {
        Console.WriteLine("-- combo ordering by level --");
        Check("L7: no Trick -> None", ChooseComboOrder(7, BeastmasterAffinity.Rampant, 0, 0) == ComboOrder.None);
        Check("L8 Rampant pet: Trick -> Mistral (CW learned)", ChooseComboOrder(8, BeastmasterAffinity.Rampant, 0, 0) == ComboOrder.TrickFirst);
        Check("L8 Volant pet: Trick -> Avalanche", ChooseComboOrder(8, BeastmasterAffinity.Volant, 0, 0) == ComboOrder.TrickFirst);
        Check("L8 Durant pet: Avalanche -> Trick (CW Spinning not learned)", ChooseComboOrder(8, BeastmasterAffinity.Durant, 0, 0) == ComboOrder.AxeFirst);
        Check("L14 Durant pet: Trick -> Spinning", ChooseComboOrder(14, BeastmasterAffinity.Durant, 0, 0) == ComboOrder.TrickFirst);
        Check("L8 Eldritch pet: Mistral -> Trick", ChooseComboOrder(8, BeastmasterAffinity.Eldritch, 0, 0) == ComboOrder.AxeFirst);
        Check("L16 Eldritch pet: Trick -> Gale", ChooseComboOrder(16, BeastmasterAffinity.Eldritch, 0, 0) == ComboOrder.TrickFirst);
        Check("L40 banks one Natural once Mastered is at 2", ChooseComboOrder(40, BeastmasterAffinity.Rampant, 2, 0) == ComboOrder.AxeFirst);
        Check("L40 with Natural banked -> Trick first", ChooseComboOrder(40, BeastmasterAffinity.Rampant, 2, 1) == ComboOrder.TrickFirst);
        Check("L39 never banks Natural", ChooseComboOrder(39, BeastmasterAffinity.Rampant, 2, 0) == ComboOrder.TrickFirst);
    }

    /// <summary>
    ///     2026-09-16 17:48 test session: L18-21, wespe in horn 1, crab in horn 2. Old build: summon -> Tempered
    ///     Release 0.6 s later -> Final Sting -> familiar gone -> horn 1 locked -> never resummoned.
    /// </summary>
    private static void Replay_2026_09_16_1748()
    {
        Console.WriteLine("-- replay 2026-09-16 17:48 (wespe Final Sting on summon) --");
        var cfg = BstSettings.Defaults();
        var s = BaseState(20);
        s.Slot1Beast = 10; s.Slot2Beast = 15; s.Slot3Beast = 0; s.SlotBeastsKnown = true;
        s.ActiveSlot = 1; s.PetObjectPresent = true; s.PetObjectBeast = 10;
        s.OneWithNature = true; s.ReadyTempered = true; s.ReadyParting = true; s.ReadyHorn2 = true;
        s.SinceSummon = 0.9f; s.SinceHornPress = 1.9f;

        var d = Decide(s, cfg);
        Check("0.9 s after the wespe arrives: NOT Tempered Release", d.ActionId != BST.TemperedRelease, $"{d.ActionId}:{d.Reason} [{d.Declines}]");
        Check("0.9 s after the wespe arrives: NOT Parting Blow", d.ActionId != BST.PartingBlow, $"{d.ActionId}:{d.Reason}");

        for (var t = 0.9f; t < 10f; t += 0.5f)
        {
            var dd = Decide(s with { SinceSummon = t, SinceHornPress = t + 1f }, cfg);
            if (dd.ActionId is BST.TemperedRelease or BST.PartingBlow)
            {
                Check($"no wespe exit before the minimum stay (fired at {t:0.0} s)", false, dd.Reason);
                break;
            }
        }

        var late = Decide(s with { SinceSummon = 10.5f, SinceHornPress = 11.5f }, cfg);
        Check("after the minimum stay with horn 2 ready: Final Sting as the exit", late.ActionId == BST.TemperedRelease && late.Reason == "exit:finalsting", $"{late.ActionId}:{late.Reason} [{late.Declines}]");

        var lastHorn = Decide(s with { SinceSummon = 30f, SinceHornPress = 31f, ReadyHorn2 = false }, cfg);
        Check("no other horn ready: wespe stays (never petless)", lastHorn.ActionId is not (BST.TemperedRelease or BST.PartingBlow), $"{lastHorn.Reason} [{lastHorn.Declines}]");
    }

    private static void NoResummonBugFixed()
    {
        Console.WriteLine("-- resummon from any ready horn --");
        var cfg = BstSettings.Defaults();
        var s = BaseState(20);
        s.Slot1Beast = 10; s.Slot2Beast = 15; s.SlotBeastsKnown = true;
        s.ActiveSlot = 0; s.PetObjectPresent = false; s.KinshipSlot = 0;
        s.ReadyHorn1 = false; s.ReadyHorn2 = true;
        var d = Decide(s, cfg);
        Check("horn 1 locked, horn 2 ready -> Second Battlehorn", d.ActionId == BST.SecondBattlehorn, $"{d.ActionId}:{d.Reason} [{d.Declines}]");

        var arriving = Decide(s with { PetObjectPresent = true }, cfg);
        Check("summon in flight (pet object, no gauge slot) -> no summon on top of it", arriving.ActionId is not (BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn), arriving.Reason);
        var justPressed = Decide(s with { SinceHornPress = 1f }, cfg);
        Check("horn pressed 1 s ago -> no second summon", justPressed.ActionId is not (BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn), justPressed.Reason);
    }

    private static void VantageNeverStalls()
    {
        Console.WriteLine("-- Lingering Vantage floor (L44) --");
        var cfg = BstSettings.Defaults();
        foreach (var level in new[] { 22, 30, 43 })
        {
            var s = BaseState(level);
            s.Slot1Beast = 1; s.Slot2Beast = 34; s.Slot3Beast = 26; s.SlotBeastsKnown = true;
            s.ActiveSlot = 1; s.PetObjectPresent = true; s.PetObjectBeast = 1;
            s.OneWithNature = false; s.ReadyParting = true; s.ReadyHorn2 = true;
            s.SinceSummon = 15f; s.SinceTempered = 12f; s.LingeringVantage = false;
            var d = Decide(s, cfg);
            Check($"L{level}: Parting Blow without Vantage (it cannot exist below 44)", d.ActionId == BST.PartingBlow, $"{d.Reason} [{d.Declines}]");
        }
    }

    private static void AxesOnlyOnGcdReadyTicks()
    {
        Console.WriteLine("-- axes are Weaponskills: offered only when the GCD is ready --");
        var cfg = BstSettings.Defaults();
        var s = BaseState(5);
        s.PlayerTp = 150; s.ReadyAxe = true; s.GcdReady = false; s.CanWeave = true;
        Check("L5, TP 150, weave window -> no axe", Decide(s, cfg).ActionId != BST.AvalancheAxe);
        Check("L5, TP 150, GCD ready -> Avalanche Axe (no familiar possible)", Decide(s with { GcdReady = true, CanWeave = false }, cfg).ActionId == BST.AvalancheAxe);
    }

    private static void Level50FinisherAndRallyGate()
    {
        Console.WriteLine("-- L50 finisher and Rally gating (Universality combo) --");
        var cfg = BstSettings.Defaults();

        // 1. Rally gating: at L50, Rally is held until Sun/Moon is active to avoid dumping 250 TP on bare axes
        var s50 = BaseState(50);
        s50.MasterStacks = 3; s50.ReadyRally = true; s50.PlayerTp = 0; s50.SunOrMoonActive = false;
        Check("L50, 3 MasterStacks, 0 TP, Sun/Moon inactive -> Rally held", ChooseRally(s50) == 0);

        var s50Sun = s50 with { SunOrMoonActive = true, SunMoon = BeastmasterAffinity.Sunstrider };
        Check("L50, 3 MasterStacks, 0 TP, Sunstrider active -> Rally used", ChooseRally(s50Sun) == BST.Rally);

        var s50Moon = s50 with { SunOrMoonActive = true, SunMoon = BeastmasterAffinity.Moonstalker };
        Check("L50, 3 MasterStacks, 0 TP, Moonstalker active -> Rally used", ChooseRally(s50Moon) == BST.Rally);

        var s50Full = s50Sun with { PlayerTp = 250 };
        Check("L50, 3 MasterStacks, 250 TP, Sunstrider active -> Rally not wasted", ChooseRally(s50Full) == 0);

        var s49 = BaseState(49);
        s49.MasterStacks = 3; s49.ReadyRally = true; s49.PlayerTp = 0; s49.SunOrMoonActive = false;
        Check("L49, 3 MasterStacks, 0 TP, Sun/Moon inactive -> Rally used (pre-50 behavior)", ChooseRally(s49) == BST.Rally);

        // 2. Finisher offering in Sun/Moon window:
        // Must offer finisher even during GCD roll (GcdReady = false) so button does not revert to gcdchain
        var sFinisher = BaseState(50);
        sFinisher.PlayerTp = 250; sFinisher.ReadyAxe = true; sFinisher.TargetDistance = 3f;
        sFinisher.SunOrMoonActive = true; sFinisher.SunMoon = BeastmasterAffinity.Moonstalker;

        // Moonstalker active -> Risen Fall (replaces SpinningAxe)
        var dMoonGcdReady = Decide(sFinisher with { GcdReady = true }, cfg);
        Check("L50 Moonstalker, GCD ready -> Spinning Axe (Risen Fall)", dMoonGcdReady.ActionId == BST.SpinningAxe && dMoonGcdReady.Reason == "finisher:risenfall-universality");

        var dMoonGcdRolling = Decide(sFinisher with { GcdReady = false, CanWeave = false }, cfg);
        Check("L50 Moonstalker, GCD rolling -> still Spinning Axe (Risen Fall, never gcdchain)", dMoonGcdRolling.ActionId == BST.SpinningAxe && dMoonGcdRolling.Reason == "finisher:risenfall-universality");

        // Sunstrider active -> Hawkish Talons (replaces MistralAxe)
        var sSunFinisher = sFinisher with { SunMoon = BeastmasterAffinity.Sunstrider };
        var dSunGcdReady = Decide(sSunFinisher with { GcdReady = true }, cfg);
        Check("L50 Sunstrider, GCD ready -> Mistral Axe (Hawkish Talons)", dSunGcdReady.ActionId == BST.MistralAxe && dSunGcdReady.Reason == "finisher:hawkishtalons-universality");

        var dSunGcdRolling = Decide(sSunFinisher with { GcdReady = false, CanWeave = false }, cfg);
        Check("L50 Sunstrider, GCD rolling -> still Mistral Axe (Hawkish Talons, never gcdchain)", dSunGcdRolling.ActionId == BST.MistralAxe && dSunGcdRolling.Reason == "finisher:hawkishtalons-universality");

        // Insufficient TP (< 250) -> finisher NOT offered
        var dLowTp = Decide(sFinisher with { PlayerTp = 100, GcdReady = false }, cfg);
        Check("L50 Moonstalker, 100 TP -> finisher NOT offered", dLowTp.ActionId != BST.SpinningAxe);
    }

    private static void OutOfCombat()
    {
        Console.WriteLine("-- out of combat --");
        var cfg = BstSettings.Defaults();
        var s = BaseState(20);
        s.InCombat = false; s.Slot1Beast = 1; s.Slot2Beast = 34; s.SlotBeastsKnown = true; s.ReadyHorn1 = s.ReadyHorn2 = true;
        Check("hostile target, no familiar -> pre-pull summon horn 1", Decide(s, cfg).ActionId == BST.FirstBattlehorn);
        Check("no hostile target -> nothing summoned", Decide(s with { HasHostileTarget = false }, cfg).ActionId == BST.SmashAxe);
        var spent = s with { ActiveSlot = 1, PetObjectPresent = true, PetObjectBeast = 1, OneWithNature = false, SinceHornPress = 30f };
        Check("spent One with Nature -> swap to horn 2 for a fresh one", Decide(spent, cfg).ActionId == BST.SecondBattlehorn);
        Check("summon-before-combat off -> no pre-pull summon", Decide(s, cfg with { SummonBeforeCombat = false }).ActionId == BST.SmashAxe);
    }


    // ================================================================== Crucible of the Unbroken

    private static void CrucibleDataChecks()
    {
        Console.WriteLine("-- crucible data (7.56 sheets + graded upstream runs) --");
        Check("territory 1339-1343 -> boards 1-5", Enumerable.Range(0, 5).All(i => BST_CrucibleData.BoardOfTerritory((uint)(1339 + i)) == i + 1));
        Check("other territories -> 0", BST_CrucibleData.BoardOfTerritory(1338) == 0 && BST_CrucibleData.BoardOfTerritory(1344) == 0 && BST_CrucibleData.BoardOfTerritory(0) == 0);
        Check("5 boards, each row's territory maps back to it",
            BST_CrucibleData.Boards.Length == 5 && BST_CrucibleData.Boards.Select((b, i) => b.Board == i + 1 && BST_CrucibleData.BoardOfTerritory(b.Territory) == i + 1).All(x => x));
        Check("122 panel enemies", BST_CrucibleData.Enemies.Length == 122 && BST_CrucibleData.EnemyCount == 122);
        Check("BNpcName ids are unique", BST_CrucibleData.Enemies.Select(e => e.NameId).Distinct().Count() == 122);
        Check("names repeat across ids: bone bishop 14532 (board 1) / 14567 (board 3)",
            BST_CrucibleData.Enemy(14532)?.Name == BST_CrucibleData.Enemy(14567)?.Name && BST_CrucibleData.Enemy(14532)?.Board == 1 && BST_CrucibleData.Enemy(14567)?.Board == 3);
        var battles = BST_CrucibleData.Enemies.Select(e => (e.Board, e.Battle)).Distinct().ToArray();
        Check("45 battles; per-board counts match the board rows",
            battles.Length == 45 && BST_CrucibleData.BattleCount == 45 && BST_CrucibleData.Boards.All(b => battles.Count(x => x.Board == b.Board) == b.Battles));
        Check("every battle has a subrow-0 enemy", battles.All(b => BST_CrucibleData.Enemies.Any(e => e.Board == b.Board && e.Battle == b.Battle && e.Sub == 0)));
        Check("bone knight: blunt, interrupt + dispel (Ossify)", BST_CrucibleData.Enemy(14531) is { Weakness: CrucibleWeakness.Blunt, Needs: CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel });
        Check("banemite: ice, cleanse (Deadly Thrust poison)", BST_CrucibleData.Enemy(14536) is { Weakness: CrucibleWeakness.Ice, Needs: CrucibleNeeds.Cleanse });
        Check("First Board battle 0 is the boss, Pas de Seul", BST_CrucibleData.BattleLabel(1, 0) == "Pas de Seul");
        Check("First Board battle 1 needs interrupt, dispel and cleanse",
            BST_CrucibleData.BattleNeeds(1, 1) == (CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse));
        Check("Ice / Blaze Spikes dispellable, Paralyzing Spikes / Needles Out not",
            BST_CrucibleData.DispellableBuffs.Contains(2528) && BST_CrucibleData.DispellableBuffs.Contains(5465)
            && !BST_CrucibleData.DispellableBuffs.Contains(5434) && !BST_CrucibleData.DispellableBuffs.Contains(5145)
            && BST_CrucibleData.StanceStatuses.SetEquals(new uint[] { 5434, 2528, 5465, 5145 }));
        Check("do-not-attack: zu eggs 14575/14576, morpho 14656",
            BST_CrucibleData.DoNotAttack.ContainsKey(14575) && BST_CrucibleData.DoNotAttack.ContainsKey(14576) && BST_CrucibleData.DoNotAttack.ContainsKey(14656));
        Check("Directional Parry is 680; 2552 only on the bone knight", BST_CrucibleData.ParryStatuses.SetEquals(new uint[] { 680 })
            && BST_CrucibleData.BoneKnightParryStatus == 2552 && BST_CrucibleData.BoneKnightNameIds.Contains(14531));
        Check("invulnerable: Burning Ward 4175 on the ogre", BST_CrucibleData.InvulnerableStatuses.Contains(4175));
        Check("damage immunity classifier: Ymir Vulnerability Down is immune; Paralyzing Spikes is not",
            BST_CrucibleData.IsDamageImmunityStatus(2198)
            && !BST_CrucibleData.IsDamageImmunityStatus(5434));
        Check("tankbusters: Deadly Thrust 46906; Erratic Blaster castbar 49188 lands 0.3 s after it (measured, 6 of 6 casts)",
            BST_CrucibleData.Tankbusters.Contains(46906) && BST_CrucibleData.Tankbusters.Contains(49188) && BST_CrucibleData.TankbusterHitDelay(49188) == 0.3f);
        Check("tankbusters: Borgny Salivous Snap 48822 (BMR SingleTargetCast)",
            BST_CrucibleData.Tankbusters.Contains(48822));
        Check("cleave-auto bosses: Pas de Seul, Siren, Guttler, Lauda",
            BST_CrucibleData.CleaveAutoBosses.SetEquals(new uint[] { 14541, 14583, 14592, 14693 }));
        Check("Third Board priority adds: crawling, flowertender, golem, bone bishop",
            new uint[] { 14586, 14589, 14581, 14567 }.All(id => BST_CrucibleData.PriorityAdds.Contains(id)));
        Check("Crucible priority adds: exact game-data set, 56 ids across all boards (treant sapling + diremite, Strix Plume, boogyman Light Sprite, King Ahriman's Final Hourglass added)",
            BST_CrucibleData.PriorityAdds.SetEquals(new uint[] {
                14532, 14537, 14539, 14542, 14543, 14748,
                14548, 14553, 14558, 14559,
                14567, 14573, 14574, 14581, 14585, 14586, 14589, 14591, 14595,
                14598,
                14605, 14607, 14610, 14620, 14621, 14622, 14624, 14625, 14629,
                14632, 14639, 14640, 14641, 14643, 14644, 14645, 14646, 14647, 14648, 14649,
                14653, 14659, 14661, 14662, 14671, 14672, 14673,
                14676, 14677, 14680, 14681, 14683, 14689, 14691, 14692, 14699,
            }));
        Check("priority-add order overlay: crawling before shambling; biloko before sapling before diremite",
            BST_CrucibleData.PriorityAddRank(14586) < BST_CrucibleData.PriorityAddRank(14585)
            && BST_CrucibleData.PriorityAddRank(14622) < BST_CrucibleData.PriorityAddRank(14620)
            && BST_CrucibleData.PriorityAddRank(14620) < BST_CrucibleData.PriorityAddRank(14621));
        Check("priority-add order overlay: thanatos, grenade, M2 bomb, mogmugger each first",
            BST_CrucibleData.PriorityAddRank(14595) < BST_CrucibleData.PriorityAddRank(14591)
            && BST_CrucibleData.PriorityAddRank(14624) < BST_CrucibleData.PriorityAddRank(14625)
            && BST_CrucibleData.PriorityAddRank(14640) < BST_CrucibleData.PriorityAddRank(14639)
            && BST_CrucibleData.PriorityAddOrder.Any(w => w.Any(t => t.Contains(14683u))));
        Check("every ordered id is a priority add; unranked ids share the last tier",
            BST_CrucibleData.PriorityAddOrder.SelectMany(w => w).SelectMany(t => t).All(id => BST_CrucibleData.PriorityAdds.Contains(id))
            && BST_CrucibleData.PriorityAddRank(14537) == int.MaxValue);
    }

    /// <summary>
    ///     The empty-horn chat warning names the need-first picks (2026-10-02): the abilities the fight needs choose the
    ///     horns, never the point score. A roster with one Soulkin among familiars that out-score it still gets the
    ///     Soulkin when the panel says the fight interrupts. Gluttony installs no guide, so the model is panel-only.
    /// </summary>
    private static void CrucibleHornWarning()
    {
        Console.WriteLine("-- crucible empty-horn warning --");
        Check("harness runs panel-only, like Gluttony (no guide installed)", CrucibleNeedModel.Extras is null);
        var soulkin = Enumerable.Range(1, BST_Beasts.Count).Where(r => BST_Beasts.All[r].Kin == BeastmasterKinType.Soulkin).ToList();
        const int coblyn = 7;
        Check("coblyn is a Soulkin", soulkin.Contains(coblyn));
        var owned = Enumerable.Range(1, BST_Beasts.Count).Where(r => !soulkin.Contains(r) || r == coblyn).ToHashSet();
        bool Captured(int r) => owned.Contains(r);

        foreach (var (board, battle) in new[] { (4, 8), (1, 0) })
        {
            var tag = $"horn warning b{board}/{battle}";
            Check($"{tag}: the panel calls for an interrupt", (CrucibleNeedModel.For(board, battle).Required & CrucibleNeeds.Interrupt) != 0);
            var warned = BST_CrucibleLogic.HornWarningPicks(board, battle, Captured);
            var expected = BST_CrucibleNeedFirst.SelectCaptured(board, battle, Captured).Picks;
            Check($"{tag}: picks equal the need-first picks", warned.Select(p => (p.Row, p.Why)).SequenceEqual(expected.Select(p => (p.Row, p.Why))),
                string.Join(",", warned.Select(p => p.Row)) + " vs " + string.Join(",", expected.Select(p => p.Row)));
            Check($"{tag}: the lone Soulkin gets a horn", warned.Any(p => p.Row == coblyn), string.Join(",", warned.Select(p => p.Row)));
            Check($"{tag}: control, the point score alone leaves the Soulkin out", BST_CrucibleAdvisor.Pick(board, battle, Captured).All(p => p.Row != coblyn));
        }

        Check("horn warning: nothing captured names no familiar", BST_CrucibleLogic.HornWarningPicks(4, 8, _ => false).Count == 0);
    }

    /// <summary>
    ///     The rotation reads the SAME Required / Useful ability needs as LazyCrucible's picker and guide window (research-to-behaviour
    ///     audit, 2026-10-02): before, the research's counters were visible to the picker but the rotation only knew the enemy panel,
    ///     so a need only the guide named (Chimera's poison, the disputed Might / Sand Tempest / Grab and Grow) was never answered
    ///     in the fight. Gluttony installs the generated guide table at load; the harness does the same.
    /// </summary>
    private static void GuideNeedsInRotation()
    {
        Console.WriteLine("-- guide needs in the rotation --");
        BST_CrucibleGuideNeeds.Install();
        try
        {
            var chimera = CrucibleNeedModel.For(5, 8);
            Check("Chimera (5.8): the panel and the guide's three agreeing sources both call for the cleanse, so it is Required",
                (BST_CrucibleData.BattleNeeds(5, 8) & CrucibleNeeds.Cleanse) != 0 && (chimera.Required & CrucibleNeeds.Cleanse) != 0, chimera.Required.ToString());
            var golem = CrucibleNeedModel.For(3, 5);
            Check("Lakhamu + Golem (3.5): the panel is silent; the guide's disputed Might dispel is Useful, not Required (the blind cleanse was disproved by the logs and removed)",
                BST_CrucibleData.BattleNeeds(3, 5) == CrucibleNeeds.None && golem.Required == CrucibleNeeds.None && golem.Useful == CrucibleNeeds.Dispel,
                $"req {golem.Required} useful {golem.Useful}");
            var boogyman = CrucibleNeedModel.For(5, 3);
            Check("Boogyman (5.3): the panel is silent; the guide's disputed Ripples of Gloom cleanse is Useful",
                BST_CrucibleData.BattleNeeds(5, 3) == CrucibleNeeds.None && boogyman.Required == CrucibleNeeds.None && (boogyman.Useful & CrucibleNeeds.Cleanse) != 0,
                $"req {boogyman.Required} useful {boogyman.Useful}");
            Check("the rotation reads Required needs and Useful interrupts / cleanses, never a Useful dispel: Chimera cleanse, Lakhamu nothing (the Might dispel is Useful), Boogyman cleanse, Treant interrupt (not the disputed Grab and Grow dispel)",
                BST_CrucibleLogic.FightNeeds(5, 8) == CrucibleNeeds.Cleanse
                && BST_CrucibleLogic.FightNeeds(3, 5) == CrucibleNeeds.None
                && BST_CrucibleLogic.FightNeeds(5, 3) == CrucibleNeeds.Cleanse
                && BST_CrucibleLogic.FightNeeds(4, 8) == CrucibleNeeds.Interrupt,
                $"{BST_CrucibleLogic.FightNeeds(5, 8)} / {BST_CrucibleLogic.FightNeeds(3, 5)} / {BST_CrucibleLogic.FightNeeds(5, 3)} / {BST_CrucibleLogic.FightNeeds(4, 8)}");
            Check("a panel need stays: Strix Piece still needs the dispel (and nothing else)", BST_CrucibleLogic.FightNeeds(4, 1) == CrucibleNeeds.Dispel);
            Check("a fight with neither panel nor guide needs reads none (1.2)", BST_CrucibleLogic.FightNeeds(1, 2) == CrucibleNeeds.None);

            // The chain: a need only the guide knows reaches the rotation and brings out the answering familiar.
            var cfg = BstSettings.Defaults();
            var live = CrucibleState(50) with { SinceSummon = 12f, ReadyParting = false, SinceHornPress = 30f };
            var golemFight = live with
            {
                CrucibleBoard = 3, CrucibleBattle = 5, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(3, 5),
                Slot1Beast = 1, Slot2Beast = 11, Slot3Beast = 34, ActiveSlot = 1, PetObjectBeast = 1,
                ReadyHorn2 = true, TargetHasDispellableBuff = true, GcdReady = true,
            };
            Check("Golem Might on the target (3.5, a dispel only the guides name and the sheet denies), vulture on a ready horn: no swap, no horn spent on a guess",
                Decide(golemFight, cfg).ActionId is not (BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn),
                $"{Decide(golemFight, cfg).Reason} [{Decide(golemFight, cfg).Declines}]");
            var ripples = live with
            {
                CrucibleBoard = 5, CrucibleBattle = 3, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(5, 3),
                Slot1Beast = 1, Slot2Beast = 19, Slot3Beast = 34, ActiveSlot = 1, PetObjectBeast = 1,
                ReadyHorn2 = true, PlayerHasCleansableDebuff = true, GcdReady = true,
            };
            Check("Ripples of Gloom blind flagged cleansable on the character (5.3, a cleanse only the guide names), bat on a ready horn: the rotation blows its horn",
                Decide(ripples, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:answer-cleanse-slot2" },
                $"{Decide(ripples, cfg).Reason} [{Decide(ripples, cfg).Declines}]");
        }
        finally
        {
            CrucibleNeedModel.Extras = null;
        }
    }

    /// <summary>
    ///     Interrupts (research-to-behaviour audit, 2026-10-02). The logs show interruptible casts in six fights (Fanaticism, Ossify,
    ///     Rallying Cheer, Dreadwash, Caustic Vomit, Natural Nurture: 108 casts); the rotation interrupted a handful, and nearly every
    ///     one it did was a cast that began while Soul Crush was already held. So: arm the Soul Crush at the open (the Soulkin comes out
    ///     first when the fight needs an interrupt), and aim at the caster once it is armed (the interruptible cast is often not on the
    ///     current target: Progenitrix's Massive Explosion while grenades are the priority, the Younger Tablitaur's Rallying Cheer
    ///     while the pair rule keeps the Elder).
    /// </summary>
    private static void ArmAndAimTheInterrupt()
    {
        Console.WriteLine("-- arm and aim the interrupt --");
        var cfg = BstSettings.Defaults();
        // No familiar out, all three horns ready: a cu sith (not a Soulkin) on horn 1, a coblyn (Soulkin) on horn 2, a dodo on horn 3.
        var open = CrucibleState(30) with
        {
            CrucibleBoard = 3, CrucibleBattle = 2, CrucibleNeeds = CrucibleNeeds.Interrupt, ActiveSlot = 0, PetObjectPresent = false, PetObjectBeast = 0,
            SinceHornPress = 30f, SinceSummon = 0f, ReadyHorn1 = true, ReadyHorn2 = true, ReadyHorn3 = true, Slot1Beast = 1, Slot2Beast = 7, Slot3Beast = 34,
            GcdReady = true,
        };
        var usual = Decide(open with { CrucibleNeeds = CrucibleNeeds.None }, cfg);
        Check("control: with no need the usual choice is not the Soulkin's horn", usual.ActionId != BST.SecondBattlehorn && usual.Reason.StartsWith("summon:slot", StringComparison.Ordinal), usual.Reason);
        Check("fight needs an interrupt, nothing out, Soul Crush not held: the Soulkin's horn first",
            Decide(open, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "summon:slot2" }, $"{Decide(open, cfg).Reason}");
        Check("... Soul Crush already held: the usual choice",
            Decide(open with { KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true }, cfg).ActionId == usual.ActionId);
        Check("... a dispel-only fight does not pull the Soulkin forward", Decide(open with { CrucibleNeeds = CrucibleNeeds.Dispel }, cfg).ActionId == usual.ActionId);
        Check("... the Soulkin is below the swap line: not preferred", Decide(open with { Slot2PetHp = 30f }, cfg).ActionId == usual.ActionId);
        Check("... Soul Crush is switched off: the usual choice", Decide(open, cfg with { UseSoulCrush = false }).ActionId == usual.ActionId);

        var armedState = open with { ActiveSlot = 2, PetObjectPresent = true, PetObjectBeast = 7, KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true };
        Check("armed means: Soul Crush held and ready, the fight needs an interrupt, the option is on",
            BST_CrucibleLogic.InterruptArmed(armedState, cfg)
            && !BST_CrucibleLogic.InterruptArmed(armedState with { ReadyBeastMode = false }, cfg)
            && !BST_CrucibleLogic.InterruptArmed(armedState with { KinshipHeld = false }, cfg)
            && !BST_CrucibleLogic.InterruptArmed(armedState with { BeastModeResolved = BST.QuellingWave }, cfg)
            && !BST_CrucibleLogic.InterruptArmed(armedState with { CrucibleNeeds = CrucibleNeeds.Dispel }, cfg)
            && !BST_CrucibleLogic.InterruptArmed(armedState, cfg with { UseSoulCrush = false })
            && !BST_CrucibleLogic.InterruptArmed(armedState with { CrucibleBoard = 0 }, cfg));

        List<int> Aim(bool armed, params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c, armed);
        BST_CrucibleLogic.TargetCandidate K(uint nameId, float hp, bool casting, bool immune = false) => new(nameId, hp, false, immune, casting);
        Check("Progenitrix casts Massive Explosion while grenades live: unarmed the grenade stays the target",
            Aim(false, K(14623, 100f, true), K(14624, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("... armed: the interrupt goes to the Progenitrix", Aim(true, K(14623, 100f, true), K(14624, 100f, false)).SequenceEqual(new[] { 0 }));
        Check("Younger Tablitaur casts Rallying Cheer, Elder 80% / Younger 50%: unarmed the pair rule keeps the Elder, armed aims at the Younger",
            Aim(false, K(14555, 80f, false), K(14556, 50f, true)).SequenceEqual(new[] { 0 })
            && Aim(true, K(14555, 80f, false), K(14556, 50f, true)).SequenceEqual(new[] { 1 }));
        Check("armed, nobody casting an interruptible cast: nothing changes",
            Aim(true, K(14623, 100f, false), K(14624, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("armed, the caster is damage immune: never aimed at", Aim(true, K(14623, 100f, true, immune: true), K(14624, 100f, false)).SequenceEqual(new[] { 1 }));
        BST_CrucibleLogic.TargetCandidate Q(uint nameId, float hp, uint castId) => new(nameId, hp, false, false, false, castId);
        const uint soulDouse = 50693, oogle = 49214;
        Check("Bone Bishop packs (3.1): of three bishops the one casting Soul Douse is the target (guide: kill the casting Bishop within 5 s)",
            Aim(false, Q(14567, 100f, 0), Q(14567, 90f, soulDouse), Q(14567, 80f, 0)).SequenceEqual(new[] { 1 }));
        Check("... nobody casting it: every bishop stays a candidate", Aim(false, Q(14567, 100f, 0), Q(14567, 90f, 0), Q(14567, 80f, 0)).SequenceEqual(new[] { 0, 1, 2 }));
        Check("Boogyman wave (5.3): the Deepeye casting Oogle comes before the bomb, the kill order resumes when the cast ends",
            Aim(false, Q(14640, 100f, 0), Q(14639, 100f, oogle)).SequenceEqual(new[] { 1 })
            && Aim(false, Q(14640, 100f, 0), Q(14639, 100f, 0)).SequenceEqual(new[] { 0 }));
        Check("a caster to kill never overrides an armed interrupt on another caster",
            Aim(true, Q(14567, 100f, soulDouse), new BST_CrucibleLogic.TargetCandidate(14542, 100f, false, false, true, 46928)).SequenceEqual(new[] { 1 }));
        Check("armed, two interruptible casters: both stay candidates",
            Aim(true, K(14542, 100f, true), K(14543, 100f, true), K(14541, 100f, false)).SequenceEqual(new[] { 0, 1 }));
    }

    /// <summary>
    ///     Interrupt economy (fix round 2026-10-03, graded from the 2026-10-02 evening). Soul Crush is a 30 s
    ///     kinship and the Soulkin's One with Nature is spent by the very borrow that arms it, so each summon buys
    ///     one 30 s window. That evening: on the healer fight 12 of 41 heals finished casting and every one of them
    ///     started with Soul Crush not held (held kinship replaced by a discretionary borrow or simply expired,
    ///     bm= 44902 -> 44896/44886). So: hold the Soulkin's One with Nature until a cast is actually up, never
    ///     borrow over a held Soul Crush on an interrupt fight, and say why out loud when a re-arm is impossible.
    /// </summary>
    private static void InterruptEconomy()
    {
        Console.WriteLine("-- interrupt economy: hold the re-arm ammo, keep Soul Crush, name the impossible --");
        var cfg = BstSettings.Defaults();

        // The Soulkin (coblyn, row 7) is out with One with Nature up on an interrupt fight; nothing held.
        var soulOut = CrucibleState(30) with
        {
            CrucibleBoard = 3, CrucibleBattle = 2, CrucibleNeeds = CrucibleNeeds.Interrupt,
            ActiveSlot = 2, Slot2Beast = 7, PetObjectBeast = 7, Slot1Beast = 1, Slot3Beast = 34,
            OneWithNature = true, ReadyTempered = true, ReadyBorrow = true,
            KinshipHeld = false, BeastModeResolved = BST.BeastMode,
            SinceSummon = 20f, SinceHornPress = 21f, CanWeave = true, HasHostileTarget = true, TargetDistance = 3f,
        };

        // Hold: no cast up, the ammo survives for the next one.
        var hold = Decide(soulOut, cfg);
        Check("no cast up: the Soulkin's One with Nature is held for the interrupt, not spent",
            hold.ActionId != BST.TemperedRelease && hold.ActionId != BST.Borrow && hold.Declines.Contains("own:held-for-interrupt"),
            $"{hold.ActionId}:{hold.Reason} [{hold.Declines}]");

        // Spend when it matters: a cast is live, borrow arms Soul Crush now (existing rule, the control).
        var borrow = Decide(soulOut with { TargetInterruptible = true }, cfg);
        Check("a cast starts: the Soulkin's One with Nature goes to Borrow and arms the interrupt",
            borrow is { ActionId: BST.Borrow, Reason: "crucible:borrow-soulkin" }, $"{borrow.ActionId}:{borrow.Reason}");

        // Armed (Soul Crush held): the hold lifts (already armed, the release may spend) but a different
        // familiar's kinship must never replace it mid-window.
        var kinHeld = soulOut with
        {
            ActiveSlot = 1, Slot1Beast = 1, PetObjectBeast = 1, Slot2Beast = 7,
            OneWithNature = true, ReadyTempered = false, TemperedRecastRemaining = 20f, ReadyBorrow = true,
            KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true, KinshipSlot = 2,
            TargetInterruptible = false,
        };
        var keep = Decide(kinHeld, cfg with { BorrowWhileReleaseRecasts = true });
        Check("Soul Crush held, Tempered on recast: no discretionary borrow over it",
            keep.ActionId != BST.Borrow && keep.Declines.Contains("own:borrow-keep-soulcrush"), $"{keep.ActionId}:{keep.Reason} [{keep.Declines}]");
        Check("... same fight, nothing held: the usual release/borrow economy is untouched",
            Decide(kinHeld with { KinshipHeld = false, BeastModeResolved = BST.BeastMode }, cfg with { BorrowWhileReleaseRecasts = true }).Declines.Contains("own:borrow-keep-soulcrush") == false);

        // The impossible case said out loud: cast live, the Soulkin is out, its One with Nature spent,
        // no other Soulkin horn ready. Today this is silent (gcdchain); it must name the engine limit.
        var spent = soulOut with { TargetInterruptible = true, OneWithNature = false, ReadyTempered = false, ReadyHorn2 = false, ReadyHorn3 = true, Slot3Beast = 34 };
        var cannot = Decide(spent, cfg);
        Check("cast live, Soulkin out with One with Nature spent, no other Soulkin horn: the limit is named",
            cannot.Declines.Contains("crucible:answer-interrupt-cannot-rearm"), $"{cannot.ActionId}:{cannot.Reason} [{cannot.Declines}]");
        Check("... a ready Soulkin horn exists instead: the answer swap fires (existing rule)",
            Decide(spent with { Slot3Beast = 7 }, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-interrupt-slot3" });
    }

    /// <summary>
    ///     Answer ammo, the rest of the class (2026-10-03 morning run, board 5 Flauros piece, character died). The
    ///     fix round held the Soulkin's One with Nature on interrupt fights; the same economy governs the bat's
    ///     Ultrasonics (the cleanse) and the vulture's Bloodcurdling Caw (the dispel): each summon buys one release,
    ///     and a discretionary Tempered or borrow spends it. Live, 2026-10-03 11:08:42 the bat was summoned on the
    ///     cleanse fight and its One with Nature went to own:tempered + borrow-while-tempered-recasts 1.4 s later;
    ///     11:09:31 the Paralysis landed and stayed (Ultrasonics needs that One with Nature), hp 28 -&gt; 4, the
    ///     character died at 11:15:19 — silently, because TryCleanse logs no decline. So: hold the answering
    ///     release while the thing it answers is not live, never borrow over a held answer kinship (Scouring Ash,
    ///     Quelling Wave — the held-Soul-Crush guard generalised), and name the impossible re-arm for all three needs.
    /// </summary>
    private static void AnswerAmmoCleanseDispel()
    {
        Console.WriteLine("-- answer ammo: hold the bat's and the vulture's release, keep held answers, name the impossible --");
        var cfg = BstSettings.Defaults();

        // The bat (row 19) is out with One with Nature up on a cleanse fight (board 5, the Flauros piece);
        // nothing held, no debuff on the character yet. This is 11:08:43 in the log, one second after the summon.
        var batOut = CrucibleState(30) with
        {
            CrucibleBoard = 5, CrucibleBattle = 1, CrucibleNeeds = CrucibleNeeds.Cleanse,
            ActiveSlot = 2, Slot2Beast = 19, PetObjectBeast = 19, Slot1Beast = 1, Slot3Beast = 34,
            OneWithNature = true, ReadyTempered = true, ReadyBorrow = true,
            KinshipHeld = false, BeastModeResolved = BST.BeastMode,
            SinceSummon = 20f, SinceHornPress = 21f, CanWeave = true, HasHostileTarget = true, TargetDistance = 3f,
        };

        // Hold: no debuff up, the ammo survives for the one that lands 49 s later.
        var hold = Decide(batOut, cfg);
        Check("no debuff up: the bat's One with Nature is held for the cleanse, not spent",
            hold.ActionId != BST.TemperedRelease && hold.ActionId != BST.Borrow && hold.Declines.Contains("own:held-for-cleanse"),
            $"{hold.ActionId}:{hold.Reason} [{hold.Declines}]");

        // Spend when it matters: the debuff is live, Ultrasonics fires now (existing rule, the control).
        Check("a cleansable debuff lands: the bat's One with Nature goes to Ultrasonics",
            Decide(batOut with { PlayerHasCleansableDebuff = true }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:cleanse-ultrasonics" },
            $"{Decide(batOut with { PlayerHasCleansableDebuff = true }, cfg).ActionId}:{Decide(batOut with { PlayerHasCleansableDebuff = true }, cfg).Reason}");

        // The vulture (row 11) on the Strix dispel fight, between Ultimate Focus windows.
        var vultureOut = batOut with
        {
            CrucibleBoard = 4, CrucibleBattle = 1, CrucibleNeeds = CrucibleNeeds.Dispel,
            ActiveSlot = 2, Slot2Beast = 11, PetObjectBeast = 11,
        };
        var vHold = Decide(vultureOut, cfg);
        Check("Strix between buffs: the vulture's One with Nature is held for the dispel, not spent",
            vHold.ActionId != BST.TemperedRelease && vHold.ActionId != BST.Borrow && vHold.Declines.Contains("own:held-for-dispel"),
            $"{vHold.ActionId}:{vHold.Reason} [{vHold.Declines}]");
        Check("... Ultimate Focus lands: Bloodcurdling Caw answers (existing rule)",
            Decide(vultureOut with { TargetHasDispellableBuff = true }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:dispel-caw" });

        // A held answer kinship is never replaced by a discretionary borrow — the held-Soul-Crush guard, generalised.
        var ashHeld = batOut with
        {
            ActiveSlot = 1, Slot1Beast = 1, PetObjectBeast = 1, Slot2Beast = 19,
            OneWithNature = true, ReadyTempered = false, TemperedRecastRemaining = 20f, ReadyBorrow = true,
            KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true, KinshipSlot = 2,
        };
        Check("Scouring Ash held, Tempered on recast: no discretionary borrow over it",
            Decide(ashHeld, cfg with { BorrowWhileReleaseRecasts = true }).Declines.Contains("own:borrow-keep-scouringash"));
        var waveHeld = vultureOut with
        {
            ActiveSlot = 1, Slot1Beast = 1, PetObjectBeast = 1, Slot2Beast = 11,
            OneWithNature = true, ReadyTempered = false, TemperedRecastRemaining = 20f, ReadyBorrow = true,
            KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, KinshipSlot = 2,
        };
        Check("Quelling Wave held on the dispel fight: the same guard",
            Decide(waveHeld, cfg with { BorrowWhileReleaseRecasts = true }).Declines.Contains("own:borrow-keep-quellingwave"));

        // The impossible cases said out loud (today they are silent — the character died behind one of them).
        var spentBat = batOut with { PlayerHasCleansableDebuff = true, OneWithNature = false, ReadyTempered = false, ReadyHorn3 = true, Slot3Beast = 34 };
        var cannotCleanse = Decide(spentBat, cfg);
        Check("debuff live, bat out with One with Nature spent, no other cleanser horn: the limit is named",
            cannotCleanse.Declines.Contains("crucible:answer-cleanse-cannot-rearm"),
            $"{cannotCleanse.ActionId}:{cannotCleanse.Reason} [{cannotCleanse.Declines}]");
        Check("... an Ashkin horn is ready instead: the answer swap fires (existing rule)",
            Decide(spentBat with { Slot3Beast = 13 }, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-cleanse-slot3" });

        var spentVulture = vultureOut with { TargetHasDispellableBuff = true, OneWithNature = false, ReadyTempered = false, ReadyHorn3 = true, Slot3Beast = 34 };
        var cannotDispel = Decide(spentVulture, cfg);
        Check("buff live, vulture out with One with Nature spent, no other dispeller horn: the limit is named",
            cannotDispel.Declines.Contains("crucible:answer-dispel-cannot-rearm"),
            $"{cannotDispel.ActionId}:{cannotDispel.Reason} [{cannotDispel.Declines}]");
        Check("... a Wavekin horn is ready instead: the answer swap fires (existing rule)",
            Decide(spentVulture with { Slot3Beast = 4 }, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-dispel-slot3" });
    }

    private static void CrucibleTargetingAndAdvisor()
    {
        Console.WriteLine("-- crucible auto-targeting and beast picks --");
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);
        BST_CrucibleLogic.TargetCandidate C(uint nameId, float hp, bool avoid, bool damageImmune = false) =>
            new(nameId, hp, avoid, damageImmune);

        Check("zu egg never allowed", Allowed(C(14575, 100f, false), C(14572, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("only eggs up: nothing to target", Allowed(C(14575, 100f, false), C(14576, 100f, false)).Count == 0);
        Check("counter stance skipped while another enemy is up", Allowed(C(14571, 100f, true), C(14570, 90f, false)).SequenceEqual(new[] { 1 }));
        Check("only counter stance up: still targetable", Allowed(C(14571, 100f, true)).SequenceEqual(new[] { 0 }));
        Check("status-immune Ymir skipped for an attackable Sahagin",
            Allowed(C(14569, 100f, false, damageImmune: true), C(14571, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("only status-immune enemy up: nothing to target",
            Allowed(C(14569, 100f, false, damageImmune: true)).Count == 0);
        Check("tablitaurs 80% / 50%: only the healthier", Allowed(C(14555, 80f, false), C(14556, 50f, false)).SequenceEqual(new[] { 0 }));
        Check("tablitaurs 60% / 55%: both", Allowed(C(14555, 60f, false), C(14556, 55f, false)).Count == 2);
        Check("Loosefrox 30% / Chewchum 70%: Chewchum", Allowed(C(14561, 30f, false), C(14562, 70f, false)).SequenceEqual(new[] { 1 }));
        Check("Pas de Seul + succubus mage: the add first", Allowed(C(14541, 90f, false), C(14542, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("bone knight + bone bishop: the bishop first", Allowed(C(14531, 100f, false), C(14532, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("siren + shambling + crawling: only the crawling piece while it lives (its touch is Bind + Damage Down)",
            Allowed(C(14583, 40f, false), C(14585, 60f, false), C(14586, 55f, false)).SequenceEqual(new[] { 2 }));
        Check("siren wave after the crawling dies: every shambling, siren still excluded",
            Allowed(C(14583, 40f, false), C(14585, 60f, false), C(14585, 55f, false)).SequenceEqual(new[] { 1, 2 }));
        Check("treant wave: biloko only while it lives (Natural Nurture heals the wave)",
            Allowed(C(14618, 100f, false), C(14619, 100f, false), C(14620, 100f, false), C(14621, 100f, false), C(14622, 100f, false)).SequenceEqual(new[] { 4 }));
        Check("treant wave after the biloko dies: the sapling before the diremite",
            Allowed(C(14618, 100f, false), C(14619, 100f, false), C(14620, 100f, false), C(14621, 100f, false)).SequenceEqual(new[] { 2 }));
        Check("treant wave after the sapling too: the diremite before slugs and treant",
            Allowed(C(14618, 100f, false), C(14619, 100f, false), C(14621, 100f, false)).SequenceEqual(new[] { 2 }));
        Check("treant: slugs and treant only after every documented priority add is dead",
            Allowed(C(14618, 100f, false), C(14619, 100f, false)).Count == 2);
        Check("thanatos + guardia: the thanatos first (a dead thanatos removes a room-wide)",
            Allowed(C(14591, 100f, false), C(14595, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("progenitrix wave: the grenade before the bombs",
            Allowed(C(14624, 100f, false), C(14625, 100f, false)).SequenceEqual(new[] { 0 }));
        Check("boogyman wave: the self-destructing bomb before the deepeye",
            Allowed(C(14639, 100f, false), C(14640, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("moogle finale: the coin-stealing mogmugger before the other officers",
            Allowed(C(14676, 100f, false), C(14681, 100f, false), C(14683, 100f, false)).SequenceEqual(new[] { 2 }));
        Check("cactuar pack: flowertender (heals allies) first, then the guardia once it is the one left (guide kill order)",
            Allowed(C(14588, 50f, false), C(14589, 60f, false), C(14590, 40f, false), C(14591, 30f, false)).SequenceEqual(new[] { 1 })
            && Allowed(C(14588, 50f, false), C(14590, 40f, false), C(14591, 30f, false)).SequenceEqual(new[] { 2 }));
        Check("lakhamu + golem: the golem first once it spawns",
            Allowed(C(14580, 60f, false), C(14581, 40f, false)).SequenceEqual(new[] { 1 }));
        Check("cavalier + bone bishop add: the add first",
            Allowed(C(14564, 80f, false), C(14567, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("ogre in Burning Ward + wisp: the wisp", Allowed(C(14538, 100f, true), C(14539, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("banemite + miteling: mitelings first", Allowed(C(14536, 100f, false), C(14537, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("demon pack: devilet before demon", Allowed(C(14557, 100f, false), C(14559, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("zu + cockerel + pullet: the pullet (Caustic Vomit), then the cockerel, then the zu (guide kill order)",
            Allowed(C(14572, 100f, false), C(14573, 100f, false), C(14574, 100f, false)).SequenceEqual(new[] { 2 })
            && Allowed(C(14572, 100f, false), C(14573, 100f, false)).SequenceEqual(new[] { 1 })
            && Allowed(C(14572, 100f, false)).SequenceEqual(new[] { 0 }));
        Check("corpse flower + queen hawk: queen hawk first", Allowed(C(14603, 100f, false), C(14605, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("ice dragon + ice sprite: ice sprite first", Allowed(C(14606, 100f, false), C(14607, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("treant + biloko: biloko first", Allowed(C(14618, 100f, false), C(14622, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("progenitrix + grenade: grenade first", Allowed(C(14623, 100f, false), C(14624, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("Borgny + toxic mass: toxic mass first", Allowed(C(14628, 100f, false), C(14629, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("flauros + lightning sprite: sprite first", Allowed(C(14631, 100f, false), C(14632, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("boogyman + bomb: bomb first", Allowed(C(14638, 100f, false), C(14640, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("durga + spinner-rook: spinner-rook first", Allowed(C(14657, 100f, false), C(14659, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("medusa + lamia: lamia first", Allowed(C(14660, 100f, false), C(14661, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("gigantis + congealed gel: gel first", Allowed(C(14670, 100f, false), C(14672, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("king ahriman + hapalit: hapalit first", Allowed(C(14688, 100f, false), C(14691, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("drake + spinemole (barbmole): the mole first (near-lethal Seeding Needles when left alone)",
            Allowed(C(14651, 100f, false), C(14653, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("atomos + summon wave: the wave first (every summon killed is boss damage), bavarois, then pudding / flan / dahak, then gremlins and vodoriga",
            Allowed(C(14642, 100f, false), C(14643, 80f, false), C(14646, 70f, false), C(14649, 60f, false)).SequenceEqual(new[] { 2 })
            && Allowed(C(14642, 100f, false), C(14643, 80f, false), C(14645, 70f, false), C(14649, 60f, false), C(14647, 50f, false)).SequenceEqual(new[] { 2, 3, 4 })
            && Allowed(C(14642, 100f, false), C(14643, 80f, false), C(14648, 70f, false), C(14644, 60f, false)).SequenceEqual(new[] { 1, 2, 3 })
            && Allowed(C(14642, 100f, false)).SequenceEqual(new[] { 0 }));
        Check("medusa + lamia + cyclops: the adds first",
            Allowed(C(14660, 100f, false), C(14661, 90f, false), C(14662, 80f, false)).SequenceEqual(new[] { 1, 2 }));
        Check("gigantis + cyclops + gel: the adds first",
            Allowed(C(14670, 100f, false), C(14671, 80f, false), C(14672, 90f, false)).SequenceEqual(new[] { 1, 2 }));
        // Guide kill orders the rotation did not enforce (research-to-behaviour audit, 2026-10-02).
        Check("Pas de Seul + mage + knight: the Succubus Mage, then the Knights, then Pas de Seul (guide kill order)",
            Allowed(C(14541, 90f, false), C(14542, 100f, false), C(14543, 100f, false)).SequenceEqual(new[] { 1 })
            && Allowed(C(14541, 90f, false), C(14543, 100f, false), C(14543, 100f, false)).SequenceEqual(new[] { 1, 2 }));
        Check("ogre + wisp + great wisp: the small wisps before the great wisp",
            Allowed(C(14538, 100f, true), C(14748, 100f, false), C(14539, 100f, false)).SequenceEqual(new[] { 2 })
            && Allowed(C(14538, 100f, true), C(14748, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("boogyman wave: bomb, then deepeye, then light sprite, then the boogyman",
            Allowed(C(14638, 100f, false), C(14641, 100f, false), C(14639, 100f, false), C(14640, 100f, false)).SequenceEqual(new[] { 3 })
            && Allowed(C(14638, 100f, false), C(14641, 100f, false), C(14639, 100f, false)).SequenceEqual(new[] { 2 })
            && Allowed(C(14638, 100f, false), C(14641, 100f, false)).SequenceEqual(new[] { 1 })
            && Allowed(C(14638, 100f, false)).SequenceEqual(new[] { 0 }));
        Check("moogle finale: Mogmugger, then the Melomogs, then the kinged healer and caster together, then the Kinged Swordsmog",
            Allowed(C(14683, 100f, false), C(14681, 100f, false), C(14676, 100f, false), C(14680, 100f, false)).SequenceEqual(new[] { 0 })
            && Allowed(C(14681, 100f, false), C(14676, 100f, false), C(14677, 100f, false), C(14680, 100f, false)).SequenceEqual(new[] { 0 })
            && Allowed(C(14676, 100f, false), C(14677, 100f, false), C(14680, 100f, false)).SequenceEqual(new[] { 0, 1 })
            && Allowed(C(14680, 100f, false)).SequenceEqual(new[] { 0 }));

        // Strix Piece (board 4, battle 1): the boss casts Aero III itself, then a Plume add (14598) casts the 12 s 25y one.
        // The game never marks either cast interruptible (0 of 5 logged casts), so the only counter is killing the add:
        // live 2026-10-01, three runs left it at 100% beside the boss while the cast went off.
        Check("Strix Piece + Plume add (full HP): the Plume while it lives, never the boss",
            Allowed(C(14596, 25f, false), C(14598, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("Strix Piece + Plume add (hurt): still only the Plume",
            Allowed(C(14596, 25f, false), C(14598, 30f, false)).SequenceEqual(new[] { 1 }));
        Check("Strix Piece after the Plume dies: the boss again", Allowed(C(14596, 25f, false)).SequenceEqual(new[] { 0 }));
        Check("morpho stays do-not-attack, never a priority add",
            !BST_CrucibleData.PriorityAdds.Contains(14656) && BST_CrucibleData.DoNotAttack.ContainsKey(14656));

        Check("51 beast profiles, row-indexed", BST_CrucibleData.BeastProfiles.Length == 51 && Enumerable.Range(1, 50).All(r => BST_CrucibleData.BeastProfiles[r].Row == r));
        Check("lamb inflicts sleep (bit 7); coblyn auto is lightning magic",
            (BST_CrucibleData.BeastProfiles[3].Inflicts & (1 << 7)) != 0 && BST_CrucibleData.BeastProfiles[7] is { AutoElement: CrucibleWeakness.Lightning, AutoMagic: true });
        Check("Cu Sith STR grows from rank 5 to 25", BST_CrucibleData.BeastProfiles[1].StatAtBoard(1, CrucibleBeastProfile.Str) < BST_CrucibleData.BeastProfiles[1].StatAtBoard(5, CrucibleBeastProfile.Str));
        Check("Pas de Seul stars 2/5/3/5/5", BST_CrucibleData.Enemy(14541) is { StarStr: 2, StarInt: 5, StarPhysRes: 3, StarMagRes: 5, StarCon: 5 });
        Check("every battle has a role row", BST_CrucibleData.Battles.Length == 45 && BST_CrucibleData.Battles.Count(b => b.Role == CrucibleRole.Boss) == 5);

        bool All(int row) => true;
        var bone = BST_CrucibleAdvisor.Pick(1, 1, All);
        Check("bone knight battle: 3 picks", bone.Count == 3, string.Join(" | ", bone.Select(p => $"{BST_Beasts.All[p.Row].Name} {p.Score} {p.Why}")));
        Check("bone knight battle: a Soulkin for Ossify", bone.Any(p => BST_Beasts.All[p.Row].Kin == BeastmasterKinType.Soulkin), string.Join(",", bone.Select(p => BST_Beasts.All[p.Row].Name)));
        Check("bone knight battle: at least one blunt pick and two of the three needs answered",
            bone.Any(p => BST_CrucibleData.BeastProfiles[p.Row].AutoElement == CrucibleWeakness.Blunt)
            && System.Numerics.BitOperations.PopCount((uint)bone.Aggregate(CrucibleNeeds.None, (n, p) => n | BST_CrucibleAdvisor.Answers(p.Row))) >= 2,
            string.Join(",", bone.Select(p => BST_Beasts.All[p.Row].Name)));
        var allNeeds = CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse;
        Check("blunt opo-opo outscores piercing Cu Sith against bone knights", BST_CrucibleAdvisor.Score(1, 1, 5, allNeeds) > BST_CrucibleAdvisor.Score(1, 1, 1, allNeeds));
        var wespeWhy = new List<string>();
        BST_CrucibleAdvisor.Score(1, 0, 10, allNeeds, wespeWhy);
        Check("Pas de Seul: wespe credited for piercing and Final Sting", wespeWhy.Contains("Final Sting") && wespeWhy.Contains("piercing"), string.Join(",", wespeWhy));
        Check("ogre: a Wavekin among the picks (Quelling Wave for the wisps)",
            BST_CrucibleAdvisor.Pick(1, 5, All).Any(p => BST_Beasts.All[p.Row].Kin == BeastmasterKinType.Wavekin),
            string.Join(",", BST_CrucibleAdvisor.Pick(1, 5, All).Select(p => BST_Beasts.All[p.Row].Name)));

        var owned = new HashSet<int> { 1, 10, 6 };
        var mine = BST_CrucibleAdvisor.Pick(1, 1, r => owned.Contains(r));
        Check("only Cu Sith / wespe / dodo captured: those three", mine.Select(p => p.Row).OrderBy(r => r).SequenceEqual(new[] { 1, 6, 10 }));
        var capture = BST_CrucibleAdvisor.WorthCapturing(1, 1, r => owned.Contains(r), mine);
        Check("worth capturing: something capturable by L30 that answers the fight",
            capture.Count > 0 && capture.All(c => BST_Beasts.All[c.Row].CaptureLevel <= 30 && !owned.Contains(c.Row)),
            string.Join(" | ", capture.Select(c => $"{BST_Beasts.All[c.Row].Name} {c.Score} {c.Why}")));
        var roster = BST_CrucibleAdvisor.BoardRoster(1, All);
        Check("board 1 roster fits the 10-beast limit", roster.Count is > 0 and <= 10, string.Join(",", roster.Select(r => $"{BST_Beasts.All[r.Row].Name}:{r.Battles}")));
        foreach (var board in new[] { 1, 2 })
            foreach (var x in BST_CrucibleData.Battles.Where(x => x.Board == board))
                Console.WriteLine($"   B{board} {BST_CrucibleData.BattleLabel(board, x.Battle),-24} {string.Join(" | ", BST_CrucibleAdvisor.Pick(board, x.Battle, All).Select(p => $"{BST_Beasts.All[p.Row].Name} ({p.Why})"))}");
        Console.WriteLine($"   B1 roster: {string.Join(", ", roster.Select(r => $"{BST_Beasts.All[r.Row].Name}:{r.Battles}"))}");
        foreach (var b in BST_CrucibleData.Boards)
            Check($"board {b.Board}: every battle gets 3 picks", BST_CrucibleData.Battles.Where(x => x.Board == b.Board).All(x => BST_CrucibleAdvisor.Pick(b.Board, x.Battle, All).Count == 3));

        // --- PickSlots: run-state ranking (candidate roster + HP) ---
        var allRows = Enumerable.Range(1, BST_Beasts.Count).ToList();
        var fullHp = new Dictionary<int, int>();
        var emptyHp = new Dictionary<int, int>();
        var today = BST_CrucibleAdvisor.Pick(1, 1, All);
        var slotsFull = BST_CrucibleAdvisor.PickSlots(1, 1, allRows, fullHp);
        Check("PickSlots full-HP empty map: same rows as Pick (empty-horn regression)",
            slotsFull.Select(p => p.Row).SequenceEqual(today.Select(p => p.Row)),
            $"Pick=[{string.Join(",", today.Select(p => p.Row))}] Slots=[{string.Join(",", slotsFull.Select(p => p.Row))}]");
        Check("PickSlots HpFactor at 100% is 1 and at DangerHpPercent is 0.32",
            Math.Abs(BST_CrucibleAdvisor.HpFactor(100) - 1.0) < 1e-9
            && Math.Abs(BST_CrucibleAdvisor.HpFactor(BST_CrucibleAdvisor.DangerHpPercent) - 0.32) < 1e-9);
        Check("PickSlots: 0% HP never picked",
            BST_CrucibleAdvisor.PickSlots(1, 1, allRows, new Dictionary<int, int> { [today[0].Row] = 0 })
                .All(p => p.Row != today[0].Row));
        Check("PickSlots: row absent from candidates never picked",
            BST_CrucibleAdvisor.PickSlots(1, 1, allRows.Where(r => r != today[0].Row).ToList(), emptyHp)
                .All(p => p.Row != today[0].Row));

        // Low-HP top pick loses to a healthy similar second; still beats a healthy zero-fit.
        var top = today[0].Row;
        var second = today[1].Row;
        var zeroFit = Enumerable.Range(1, BST_Beasts.Count)
            .Where(r => r != top && r != second)
            .OrderBy(r => BST_CrucibleAdvisor.Score(1, 1, r, CrucibleNeeds.None))
            .First();
        var lowTop = BST_CrucibleAdvisor.PickSlots(1, 1, new[] { top, second },
            new Dictionary<int, int> { [top] = BST_CrucibleAdvisor.DangerHpPercent, [second] = 100 });
        Check("PickSlots: at DangerHpPercent the top battle fit loses to a healthy similar second",
            lowTop.Count >= 1 && lowTop[0].Row == second,
            string.Join(" | ", lowTop.Select(p => $"{p.Row}@{p.Score} {p.Why}")));
        var lowVsNothing = BST_CrucibleAdvisor.PickSlots(1, 1, new[] { top, zeroFit },
            new Dictionary<int, int> { [top] = BST_CrucibleAdvisor.DangerHpPercent, [zeroFit] = 100 });
        Check("PickSlots: DangerHpPercent top still beats a healthy familiar that answers nothing",
            lowVsNothing.Count >= 1 && lowVsNothing[0].Row == top,
            $"zeroFit={zeroFit} score={BST_CrucibleAdvisor.Score(1, 1, zeroFit, CrucibleNeeds.None)}; "
            + string.Join(" | ", lowVsNothing.Select(p => $"{p.Row}@{p.Score} {p.Why}")));

        var unknownHp = BST_CrucibleAdvisor.PickSlots(1, 1, new[] { top }, emptyHp);
        Check("PickSlots: unknown HP treated as full and said in Why",
            unknownHp.Count == 1 && unknownHp[0].Row == top && unknownHp[0].Why.Contains("HP assumed full"));

        // Coverage: after a Soulkin fills interrupt, a second Soulkin is not credited for Soul Crush.
        var soulkin = Enumerable.Range(1, BST_Beasts.Count).First(r => BST_Beasts.All[r].Kin == BeastmasterKinType.Soulkin);
        var otherSoulkin = Enumerable.Range(1, BST_Beasts.Count).First(r => r != soulkin && BST_Beasts.All[r].Kin == BeastmasterKinType.Soulkin);
        var coverPicks = BST_CrucibleAdvisor.PickSlots(1, 1, new[] { soulkin, otherSoulkin },
            new Dictionary<int, int> { [soulkin] = 100, [otherSoulkin] = 100 }, slots: 2);
        Check("PickSlots: needs coverage — second Soulkin Why has no Soul Crush once interrupt is covered",
            coverPicks.Count == 2
            && coverPicks[0].Why.Contains("Soul Crush")
            && !coverPicks[1].Why.Contains("Soul Crush"),
            string.Join(" | ", coverPicks.Select(p => $"{BST_Beasts.All[p.Row].Name} {p.Why}")));

        var once = BST_CrucibleAdvisor.PickSlots(1, 1, allRows, new Dictionary<int, int> { [top] = 40, [second] = 70 });
        var twice = BST_CrucibleAdvisor.PickSlots(1, 1, allRows, new Dictionary<int, int> { [top] = 40, [second] = 70 });
        Check("PickSlots: deterministic — same inputs same order",
            once.Select(p => p.Row).SequenceEqual(twice.Select(p => p.Row)));

        var hpMap = BST_CrucibleAdvisor.HpPercentByRow(
        [
            (top, 50u, 100u),
            (second, 0u, 100u),  // dead
            (zeroFit, 10u, 0u),  // max 0 → dead
            (9999, 1u, 1u),      // invalid row skipped
        ]);
        Check("HpPercentByRow: live percent, 0=dead, max0=dead, invalid skipped",
            hpMap.TryGetValue(top, out var pct) && pct == 50
            && hpMap.TryGetValue(second, out var dead) && dead == 0
            && hpMap.TryGetValue(zeroFit, out var deadMax) && deadMax == 0
            && !hpMap.ContainsKey(9999));


        // Coverage ranking: board known, no single battle — Why must carry the coverage tag.
        var covCandidates = new[] { 1, 4, 5, 7, 11 }; // Cu Sith, spriggan-ish rows from B1 roster samples
        var covHp = new Dictionary<int, int> { [1] = 100, [4] = 100, [5] = 100, [7] = 100, [11] = 100 };
        var cov = BST_CrucibleAdvisor.PickSlotsCoverage(1, covCandidates, covHp, 3);
        Check("PickSlotsCoverage: returns up to 3 picks for board 1", cov.Count is >= 1 and <= 3);
        Check("PickSlotsCoverage: every Why starts with coverage board tag + unidentified",
            cov.Count > 0 && cov.All(p => p.Why.StartsWith("coverage board 1, battle unidentified", StringComparison.Ordinal)));

        // --- Round-10 Stage-1 roster writer planning and coverage tests ---
        var allCandidates = new List<int>();
        for (var r = 1; r <= BST_Beasts.Count; r++) allCandidates.Add(r);
        var cov10 = BST_CrucibleAdvisor.PickSlotsCoverage(1, allCandidates, new Dictionary<int, int>(), 10);
        Check("PickSlotsCoverage: 10 picks for roster on board 1", cov10.Count == 10);
        Check("PickSlotsCoverage: all 10 picks unique", cov10.Select(p => p.Row).Distinct().Count() == 10);
        Check("PickSlotsCoverage: all 10 Why start with coverage board 1",
            cov10.All(p => p.Why.StartsWith("coverage board 1, battle unidentified", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Survival as a class (task tasks-20261003-crucible-survivability-entry-hp-01, 2026-10-03). Five of the day's ten deaths
    ///     were fights entered below 60% HP (Durga 37%, Lauda 59/57%; below 40% entry died 8.8x the 80%+ rate over 7 days), and
    ///     Durga's Atomic Ray (49272, aimed at the character, 3,998 / 4,782 / 2,953-clamped on a 7,950 bar) killed a 30% character
    ///     with nothing pressed. The only heal actor (AutoDuty's Crucible Items) under-heals on the board (60% line) and goes
    ///     silent when the HUD stock is dry; the Durga fight ran 126 s below its 40% line with zero uses.
    /// </summary>
    private static void CrucibleSurvival()
    {
        Console.WriteLine("-- crucible survival --");
        var cfg = BstSettings.Defaults();
        var off = cfg with { CrucibleSurvival = false };

        // Atomic Ray, the logged lethal shape: 13 s cast, 8 s remaining, character at 2,385 of 7,950 HP, known max hit 4,782.
        var ray = CrucibleState() with
        {
            InCombat = true, TargetCastId = 49272, TargetCastRemaining = 8f,
            PlayerHpPercent = 30f, PlayerHp = 2385f, ReadyHealPotion = 46961,
        };
        Check("Atomic Ray 8 s out, 2,385 HP under the 4,782 known hit: the G3 potion (today: nothing)",
            Decide(ray, cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" }, $"{Decide(ray, cfg).Reason} [{Decide(ray, cfg).Declines}]");
        Check("survival off: nothing pressed", Decide(ray, off).Reason != "crucible:raidwide-guard");
        Check("above the known hit (6,000 HP): no guard", Decide(ray with { PlayerHp = 6000f, PlayerHpPercent = 75f }, cfg).Reason != "crucible:raidwide-guard");
        Check("cast already resolving (0.1 s): too late", Decide(ray with { TargetCastRemaining = 0.1f }, cfg).Reason != "crucible:raidwide-guard");
        Check("no potion usable: decline names the guard", Decide(ray with { ReadyHealPotion = 0 }, cfg).Declines.Contains("crucible:raidwide-guard-none"));

        // Scale Kinship held: the magic-absorbing skin answers first once the hit is close (potions are stock, the skin is free).
        var skinned = ray with { KinshipHeld = true, BeastModeResolved = BST.Scaleskin, ReadyBeastMode = true, TargetCastRemaining = 6f };
        Check("Scaleskin held, hit 6 s out: the skin, not the stock", Decide(skinned, cfg) is { ActionId: BST.Scaleskin, Reason: "crucible:raidwide-guard" });
        Check("Scaleskin held but the hit is 12 s out: the potion heals now, the skin waits",
            Decide(skinned with { TargetCastRemaining = 12f }, cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" });

        // Registered tankbuster with no Snarl -> Parting cover armed (option off, no familiar out): the potion at low HP.
        var vomit = CrucibleState() with
        {
            ActiveSlot = 0, PetObjectPresent = false, EnemyTargetsPet = false,
            TargetCastId = 48809, TargetCastRemaining = 3f, PlayerHpPercent = 30f, PlayerHp = 2385f, ReadyHealPotion = 46961,
        };
        Check("Toxic Vomit, no cover armed, 30% HP: the potion", Decide(vomit, cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" });
        Check("familiar already covering: the dodge owns it, no potion",
            Decide(vomit with { EnemyTargetsPet = true }, cfg).Reason != "crucible:raidwide-guard");

        // Attrition deaths (Lauda: Burns + cone autos at 3% HP): the panic line.
        var panic = CrucibleState() with { PlayerHpPercent = 20f, PlayerHp = 1590f, ReadyHealPotion = 46959 };
        Check("20% HP, no cast to react to: the panic heal", Decide(panic, cfg) is { ActionId: 46959, Reason: "crucible:panic-heal" });
        Check("40% HP: no panic", Decide(panic with { PlayerHpPercent = 40f, PlayerHp = 3180f }, cfg).Reason != "crucible:panic-heal");

        // Entry HP: on the board, top up before the next fight (HP carries between battles).
        var board = CrucibleState() with { InCombat = false, PlayerHpPercent = 59f, PlayerHp = 4690f, ReadyHealPotion = 46960 };
        Check("on the board at 59%: the G2 potion before the next fight", Decide(board, cfg) is { ActionId: 46960, Reason: "crucible:board-heal" },
            $"{Decide(board, cfg).Reason} [{Decide(board, cfg).Declines}]");
        Check("on the board at 85%: no heal", Decide(board with { PlayerHpPercent = 85f, PlayerHp = 6757f }, cfg).Reason != "crucible:board-heal");
        Check("survival off: no board heal", Decide(board, off).Reason != "crucible:board-heal");

        // Outside the Crucible the potions never fire.
        Check("board 0: no survival actions", Decide(ray with { CrucibleBoard = 0 }, cfg).Reason != "crucible:raidwide-guard"
              && Decide(board with { CrucibleBoard = 0 }, cfg).Reason != "crucible:board-heal");

        // The picker itself (the only live-state code this policy had; every case above set ReadyHealPotion by hand,
        // so the shipped selection had zero coverage): grades strongest-first over HELD stock and refused ids.
        // The shipped picker read only the recast, so with no G4 held it pressed the dead 46962 id every time
        // (Durga held G1 only, Lauda G2), and its blanket 2.5 s throttle starved a need appearing just after an idle offer.
        Func<uint, bool> clear = _ => true;
        var grades = BST_CrucibleData.HealPotionActions;
        Check("picker: G1 only held offers the G1 action (Durga's stock; today: the dead G4)",
            BST_CrucibleLogic.PickHealPotion(grades, new HashSet<uint> { 46959u }, new HashSet<uint>(), clear) == 46959u);
        Check("picker: G4 refused falls to G3 (both held)",
            BST_CrucibleLogic.PickHealPotion(grades, new HashSet<uint> { 46962u, 46961u }, new HashSet<uint> { 46962u }, clear) == 46961u);
        Check("picker: G2+G1 held offers the G2 (Lauda's stock)",
            BST_CrucibleLogic.PickHealPotion(grades, new HashSet<uint> { 46960u, 46959u }, new HashSet<uint>(), clear) == 46960u);
        Check("picker: nothing held offers nothing (the old picker pressed G4 anyway)",
            BST_CrucibleLogic.PickHealPotion(grades, new HashSet<uint>(), new HashSet<uint>(), clear) == 0u);
        Check("picker: recast blocks the only held grade",
            BST_CrucibleLogic.PickHealPotion(grades, new HashSet<uint> { 46961u }, new HashSet<uint>(), id => id != 46961u) == 0u);
        Check("picker: refusing the only held grade offers nothing (a refusal never invents stock)",
            BST_CrucibleLogic.PickHealPotion(grades, new HashSet<uint> { 46959u }, new HashSet<uint> { 46959u }, clear) == 0u);
        Check("picker: HUD stock rows map 76-79 to the G1-G4 actions",
            BST_CrucibleData.HealPotionItemRows[76] == 46959u && BST_CrucibleData.HealPotionItemRows[77] == 46960u
            && BST_CrucibleData.HealPotionItemRows[78] == 46961u && BST_CrucibleData.HealPotionItemRows[79] == 46962u);

        PotionUse();
    }

    /// <summary>
    ///     How a held potion is actually drunk (task tasks-20261003-crucible-postplay-268-01, 2026-10-04). The first Crucible runs on the
    ///     survival policy fired 57 potion decisions and the game honoured one: pressing the potion ACTION (46959-46962) is accepted by
    ///     the client and ignored by the server, because the potions are drunk through the item HUD menu. Every potion that did land
    ///     (ten, 19:57-20:03 ET) was AutoDuty's HUD click: callback (6, slot) on XBMContentsMainHUD, then the context menu's first
    ///     entry. And the press tracker saw those dead presses as landed (the use stamp is written when the client accepts), so the
    ///     same grade was re-pressed every two seconds for twelve minutes with no step-down.
    /// </summary>
    private static void PotionUse()
    {
        Console.WriteLine("-- potion use through the item HUD --");
        var rows = BST_CrucibleData.HealPotionItemRows;

        // Which slot holds which grade (the menu opens on a slot index, not on an action id).
        var slots = new List<(bool Held, uint Row)>
        {
            (true, 78u),   // 0: G3
            (true, 130u),  // 1: Fang of Water (not a potion)
            (true, 76u),   // 2: G1
            (false, 0u),   // 3: empty placeholder
            (false, 77u),  // 4: G2 row but not held
            (true, 78u),   // 5: a second G3 (first slot wins)
        };
        var held = BST_CrucibleLogic.HeldHealSlots(slots, rows);
        Check("slots: G3 at 0 and G1 at 2 (the 19:45 stock)", held.Count == 2 && held.GetValueOrDefault(46961u, -1) == 0 && held.GetValueOrDefault(46959u, -1) == 2,
            string.Join(",", held.Select(kv => $"{kv.Key}@{kv.Value}")));
        Check("slots: a G2 row that is not held is not offered", !held.ContainsKey(46960u));
        Check("slots: a non-potion row is ignored", !held.ContainsKey(0u) && held.Count == 2);
        Check("slots: the HUD with nothing held yields nothing",
            BST_CrucibleLogic.HeldHealSlots(new List<(bool Held, uint Row)> { (false, 0u), (false, 76u) }, rows).Count == 0);

        // Did the request land: the potion's recast is running or HP rose; a stamp alone never counts.
        const long grace = 3000;
        Check("press: recast running 400 ms in = landed",
            BST_CrucibleLogic.ResolveHealPress(400, true, 0f, grace) == BST_CrucibleLogic.HealPressOutcome.Landed);
        Check("press: HP up 10% 1.2 s in = landed",
            BST_CrucibleLogic.ResolveHealPress(1200, false, 10f, grace) == BST_CrucibleLogic.HealPressOutcome.Landed);
        Check("press: nothing yet 1 s in = pending",
            BST_CrucibleLogic.ResolveHealPress(1000, false, 0f, grace) == BST_CrucibleLogic.HealPressOutcome.Pending);
        Check("press: nothing at the grace = dead (the id steps down)",
            BST_CrucibleLogic.ResolveHealPress(grace, false, 0f, grace) == BST_CrucibleLogic.HealPressOutcome.Dead);
        Check("press: still nothing a minute on = dead (an older use of the same id counts for nothing)",
            BST_CrucibleLogic.ResolveHealPress(60_000, false, 0f, grace) == BST_CrucibleLogic.HealPressOutcome.Dead);
        Check("press: a 1% wobble is not a heal",
            BST_CrucibleLogic.ResolveHealPress(grace, false, 1f, grace) == BST_CrucibleLogic.HealPressOutcome.Dead);

        // The menu hand-off: open the slot, wait for the context menu, take its first entry, give up in time.
        const long wait = 1500;
        Check("menu: not up yet 400 ms in = wait", BST_CrucibleLogic.NextItemMenuStep(400, false, wait) == BST_CrucibleLogic.ItemMenuStep.Wait);
        Check("menu: up 300 ms in = choose", BST_CrucibleLogic.NextItemMenuStep(300, true, wait) == BST_CrucibleLogic.ItemMenuStep.Choose);
        Check("menu: never up by the wait = give up", BST_CrucibleLogic.NextItemMenuStep(wait, false, wait) == BST_CrucibleLogic.ItemMenuStep.GiveUp);
        Check("menu: one that opens after the wait is not ours = give up", BST_CrucibleLogic.NextItemMenuStep(wait + 200, true, wait) == BST_CrucibleLogic.ItemMenuStep.GiveUp);
    }

    /// <summary> In combat on the First Board, L30, Cu Sith out (One with Nature spent), raptor / buffalo on ready horns 2 and 3. </summary>
    /// <summary>
    ///     Dispel as a class (task tasks-20261003-crucible-regen-dispel-not-fired-01, 2026-10-03). The Board 5 Drake + Barbmole +
    ///     Abaddon + Morpho fight (Abaddon Piece, 14:49 to 14:53 ET) needed a dispel for 428 of 430 ticks with Quelling Wave held
    ///     the whole fight (bm=44900, Wave Kinship up) and was never dispelled: the Abaddon's Regen is status 989
    ///     (Rehabilitation, "Regenerating HP over time", added 4 times and on the hard target for about 160 s) and 989 was
    ///     not in the dispellable-buff list, so the target flag never rose. The same hole stood for the other research rows the sheet
    ///     does not flag (Growing 390, Impassion 3129). Also the dispel only ever aimed at the hard target.
    /// </summary>
    private static void DispelClass()
    {
        Console.WriteLine("-- dispel as a class --");
        BST_CrucibleGuideNeeds.Install();
        try
        {
            // Data: every research dispel row names the status the game puts on the enemy.
            // 2026-10-03 19:34-19:35 (Abaddon Piece, 1.0.4.268): the rotation sent 12 Quelling Waves at the Abaddon's Rehabilitation (989); the
            // casts' effects were damage only (the same cast on the Drake's Blaze Spikes carried the status-removal effect and the
            // spikes came off 0.6 s later) and 989 stood the whole fight. The game does not honour a dispel on it.
            Check("Regen on the Abaddon is status 989 (Rehabilitation): the game does not dispel it (12 Quelling Waves, none took it off), so it is not a dispellable buff",
                !BST_CrucibleData.DispellableBuffs.Contains(989));
            Check("... its research row stays (the guide still counts it) and says why it is not tried",
                BST_CrucibleData.DispelRows.Any(r => r.StatusId == 989 && r.Basis == "disproven"));
            Check("Growing (390, Saplings) and Impassion (3129, Medusa) stay in the list as unproven tries: the guides name them, the logs decide",
                BST_CrucibleData.DispellableBuffs.Contains(390) && BST_CrucibleData.DispellableBuffs.Contains(3129));
            var gaps = new List<string>();
            var guideRows = 0;
            for (var board = 1; board <= 5; board++)
                for (var battle = 0; battle <= 15; battle++)
                {
                    var wanted = BST_CrucibleGuideNeeds.NeedsOf(board, battle).Count(n => n.Kind == CrucibleNeeds.Dispel);
                    var have = BST_CrucibleData.DispelRows.Count(r => r.Board == board && r.Battle == battle);
                    guideRows += wanted;
                    if (wanted != have)
                        gaps.Add($"{board}.{battle}: guide {wanted} vs rows {have}");
                }
            Check("every dispel counter in the research has a status-id row, and no row stands without one (13 of them)",
                gaps.Count == 0 && guideRows == 13 && BST_CrucibleData.DispelRows.Length == 13, $"{guideRows} guide rows; {string.Join("; ", gaps)}");
            Check("every row's status is a dispellable buff the rotation reads, except the ones a run has disproven",
                BST_CrucibleData.DispelRows.Length > 0
                && BST_CrucibleData.DispelRows.All(r => BST_CrucibleData.DispellableBuffs.Contains(r.StatusId) == (r.Basis != "disproven")));
            Check("the stances stay undispellable and the Needles Out / Paralyzing Spikes ids stay out",
                !BST_CrucibleData.DispellableBuffs.Contains(5145) && !BST_CrucibleData.DispellableBuffs.Contains(5434));
            Check("the panel's own flagged ids (known honoured) are never given up on; Regen, Growing, Impassion and Might are not panel-flagged",
                new uint[] { 1225, 2074, 2528, 5020, 5423, 5465 }.All(BST_CrucibleData.PanelFlagsDispellable)
                && new uint[] { 989, 390, 3129, 1572 }.All(id => !BST_CrucibleData.PanelFlagsDispellable(id)));
            Check("the Abaddon fight (5.5) needs the dispel", (BST_CrucibleLogic.FightNeeds(5, 5) & CrucibleNeeds.Dispel) != 0);

            // The Abaddon fight, replayed: Wave Kinship held and ready, Regen on the target.
            var cfg = BstSettings.Defaults();
            var live = CrucibleState(50) with { SinceSummon = 12f, ReadyParting = false, SinceHornPress = 30f };
            var abaddon = live with
            {
                CrucibleBoard = 5, CrucibleBattle = 5, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(5, 5), EnemyCount = 3,
                Slot1Beast = 1, Slot2Beast = 8, Slot3Beast = 4, ActiveSlot = 1, PetObjectBeast = 1, GcdReady = true,
                KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true,
                TargetHasDispellableBuff = true, EnemyHasDispellableBuff = true,
            };
            Check("Abaddon with Regen on the target, Quelling Wave held: dispel it", Decide(abaddon, cfg) is { ActionId: BST.QuellingWave, Reason: "crucible:dispel-quellingwave" },
                $"{Decide(abaddon, cfg).Reason} [{Decide(abaddon, cfg).Declines}]");

            // The carrier is not the target: bring the Wavekin out (there was no way before: the buff was only ever read off the target).
            var elsewhere = abaddon with
            {
                KinshipHeld = false, BeastModeResolved = 0, ReadyBeastMode = false, TargetHasDispellableBuff = false,
                ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = true,
            };
            Check("Regen on an enemy that is not the target, no Wave held, a pugil on a ready horn: blow it",
                Decide(elsewhere, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-dispel-slot3" }, $"{Decide(elsewhere, cfg).Reason} [{Decide(elsewhere, cfg).Declines}]");
            Check("... nobody carries a dispellable buff: no swap",
                Decide(elsewhere with { EnemyHasDispellableBuff = false }, cfg).ActionId is not (BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn));

            // Armed means aim at the carrier.
            Check("armed: Wave held and ready (or the vulture out with Tempered Release up), a fight that needs the dispel, a carrier in reach",
                BST_CrucibleLogic.DispelArmed(abaddon, cfg)
                && !BST_CrucibleLogic.DispelArmed(abaddon with { ReadyBeastMode = false }, cfg)
                && !BST_CrucibleLogic.DispelArmed(abaddon with { KinshipHeld = false }, cfg)
                && !BST_CrucibleLogic.DispelArmed(abaddon with { BeastModeResolved = BST.SoulCrush }, cfg)
                && !BST_CrucibleLogic.DispelArmed(abaddon with { CrucibleNeeds = CrucibleNeeds.None }, cfg)
                && !BST_CrucibleLogic.DispelArmed(abaddon with { EnemyHasDispellableBuff = false }, cfg)
                && !BST_CrucibleLogic.DispelArmed(abaddon with { CrucibleBoard = 0 }, cfg));
            var vultureOut = abaddon with { KinshipHeld = false, ReadyBeastMode = false, Slot1Beast = 11, PetObjectBeast = 11, OneWithNature = true, ReadyTempered = true, SinceSummon = 3f, DispelCawReachable = true };
            Check("... the vulture out, One with Nature and Tempered Release ready, a carrier the Caw can be cast at: armed",
                BST_CrucibleLogic.DispelArmed(vultureOut, cfg));

            // The Caw is an area attack: it is refused while a do-not-attack enemy (the Morphos) stands within reach of the carrier.
            // An armed aim on such a carrier would pin the aim on it and hold every tick (stance carrier + protected add).
            var cawBlocked = vultureOut with { DispelCawReachable = false, ProtectedNearTarget = true, TargetInStance = true };
            Check("... the vulture out but every carrier has a protected enemy beside it (or is out of the Caw's range): not armed, the Caw would be refused",
                !BST_CrucibleLogic.DispelArmed(cawBlocked, cfg));
            Check("... the same with Quelling Wave held instead of the vulture: armed (single target, no area to protect)",
                BST_CrucibleLogic.DispelArmed(abaddon with { ProtectedNearTarget = true, TargetInStance = true, DispelCawReachable = false }, cfg));
            var cawField = new BST_CrucibleLogic.TargetCandidate[] { new(14651, 100f, true, false, false, 0, true), new(14655, 100f, false, false, false, 0, false) };
            Check("... the vulture, a carrier in a counter stance with a protected Morpho beside it: the aim is not pinned on it, the calm enemy is chosen",
                BST_CrucibleLogic.AllowedTargets(cawField, false, BST_CrucibleLogic.DispelArmed(cawBlocked, cfg)).SequenceEqual(new[] { 1 }));

            // Aimed at a stance carrier the rotation holds (no GCD), so the GCD sits idle and a weave window never opens: the
            // Caw must not wait for one. Mid-GCD it holds the tick, the GCD runs out, and the next tick it goes out.
            var cawStance = vultureOut with { TargetInStance = true, TargetDistance = 5f, ProtectedNearTarget = false, CanWeave = false, GcdReady = true };
            Check("the vulture, a stance carrier with a clear area, GCD idle and no weave window: the Caw goes out, the rotation does not hold on it",
                Decide(cawStance, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:dispel-caw" }, $"{Decide(cawStance, cfg).Reason} [{Decide(cawStance, cfg).Declines}]");
            Check("... the GCD still rolling (inside the closing window): held this tick, no flap (the aim stays, the next tick the GCD is idle)",
                Decide(cawStance with { GcdReady = false }, cfg).Reason == "crucible:hold-stance" && BST_CrucibleLogic.DispelArmed(cawStance with { GcdReady = false }, cfg));
            Check("... a calm carrier, GCD idle and no weave window: no Caw, the rotation keeps its GCD (the weave comes with the next window)",
                Decide(cawStance with { TargetInStance = false }, cfg).ActionId != BST.TemperedRelease);

            const uint drake = 14651, barbmole = 14653, abaddonName = 14655, morpho = 14656;
            List<int> Aim(bool armed, params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c, false, armed);
            BST_CrucibleLogic.TargetCandidate N(uint nameId, bool carrier = false, bool avoid = false, bool immune = false) => new(nameId, 100f, avoid, immune, false, 0, carrier);
            var field = new[] { N(drake, carrier: true, avoid: true), N(barbmole, avoid: true), N(abaddonName, carrier: true), N(morpho) };
            Check("unarmed: the dispel does not steer targeting (control: the Barbmole is a priority add and stays the only candidate)", Aim(false, N(barbmole), N(abaddonName, carrier: true)).SequenceEqual(new[] { 0 }));
            Check("armed, Regen on the Abaddon and a Barbmole (priority add) beside it: aim at the Abaddon", Aim(true, N(barbmole), N(abaddonName, carrier: true)).SequenceEqual(new[] { 1 }));
            Check("armed, nobody carries one: nothing changes", Aim(true, N(barbmole), N(abaddonName)).SequenceEqual(new[] { 0 }));
            Check("armed, the Drake in Blaze Spikes (a stance) carries one: it is aimed at, the stance is what the dispel removes",
                Aim(true, N(drake, carrier: true, avoid: true), N(abaddonName)).SequenceEqual(new[] { 0 }));
            Check("armed, Drake and Abaddon both carry one: both stay candidates, the Morpho and the Barbmole do not", Aim(true, field).SequenceEqual(new[] { 0, 2 }));
            Check("armed, a carrier that is damage immune is never aimed at", Aim(true, N(abaddonName, carrier: true, immune: true), N(barbmole)).SequenceEqual(new[] { 1 }));
            Check("armed, an egg or morpho flagged as a carrier is never aimed at", Aim(true, N(morpho, carrier: true), N(barbmole)).SequenceEqual(new[] { 1 }));
            Check("armed with the interrupt too: the interrupt caster still goes first",
                BST_CrucibleLogic.AllowedTargets([N(abaddonName, carrier: true), new(14623, 100f, false, false, true)], true, true).SequenceEqual(new[] { 1 }));

            // A dispel the game does not honour is not retried forever.
            var tries = new Dictionary<(ulong Enemy, uint Status), int>();
            Check("no dispel sent yet: not futile", !BST_CrucibleLogic.DispelFutile(tries, 7, 989));
            BST_CrucibleLogic.NoteDispelSent(tries, 7, [989]);
            Check("one dispel sent and the buff is still up: one more try is allowed", !BST_CrucibleLogic.DispelFutile(tries, 7, 989));
            BST_CrucibleLogic.NoteDispelSent(tries, 7, [989]);
            Check($"{BST_CrucibleLogic.DispelMaxTries} dispels sent and the buff is still up: given up on that enemy and that status",
                BST_CrucibleLogic.DispelFutile(tries, 7, 989) && !BST_CrucibleLogic.DispelFutile(tries, 8, 989) && !BST_CrucibleLogic.DispelFutile(tries, 7, 390));
            BST_CrucibleLogic.ForgetGoneStatuses(tries, 7, [989]);
            Check("the buff still on the enemy: the verdict stands", BST_CrucibleLogic.DispelFutile(tries, 7, 989));
            BST_CrucibleLogic.ForgetGoneStatuses(tries, 7, []);
            Check("the buff came off: the count starts over (it can be put back on)", !BST_CrucibleLogic.DispelFutile(tries, 7, 989));
            BST_CrucibleLogic.NoteDispelSent(tries, 7, [989]);
            BST_CrucibleLogic.ForgetGoneStatuses(tries, 8, []);
            BST_CrucibleLogic.NoteDispelSent(tries, 7, [989]);
            Check("another enemy's buffs coming off does not reset this one", BST_CrucibleLogic.DispelFutile(tries, 7, 989));

            // Every dispel that goes out is one try. The decision repeats every GCD (the rotation re-decides as soon as the GCD is
            // ready again, ~2.5 s), and the old count took a run of decisions less than 2.5 s apart for ONE burst and counted one
            // try per burst: 12 Quelling Waves on the Abaddon's 989 counted 2 (19:34:55 and 19:35:19 DS| lines).
            var uses = new BST_CrucibleLogic.DispelUseTracker();
            var counted = 0;
            for (var cycle = 0; cycle < 8; cycle++)
            {
                var t0 = 10_000L + cycle * 2480L;
                var cast = t0 + 120;
                foreach (var at in new long[] { 0, 250, 500, 750, 1000 })
                {
                    var tick = t0 + at;
                    if (at <= 250)
                        uses.NoteDecision(tick);
                    if (uses.CountUse(tick, tick >= cast ? (tick - cast) / 1000f : 999f))
                        counted++;
                }
            }
            Check("a dispel decided and sent every GCD for 8 GCDs: 8 tries counted, each use once", counted == 8, $"{counted} counted");
            var oneUse = new BST_CrucibleLogic.DispelUseTracker();
            Check("a use with no decision before it is not a dispel (a Caw used for damage)", !oneUse.CountUse(50_000, 0.2f));
            oneUse.NoteDecision(60_000);
            Check("a use from before the decision is not that decision's dispel", !oneUse.CountUse(60_100, 1.4f));
            Check("a use 0.3 s after the decision is", oneUse.CountUse(60_300, 0.2f));
            Check("... and is not counted again on the next tick", !oneUse.CountUse(60_550, 0.45f));
            Check("a decision that went stale (2.5 s without a cast) counts nothing late", !new BST_CrucibleLogic.DispelUseTracker().CountUse(70_000, 0.1f));

            // A proven carrier (the panel flags its buff) comes before an unproven one: the Drake's Blaze Spikes waited 5.5 s
            // (19:34:57.7 to 19:35:03.2) while the rotation spent the GCDs on the Abaddon's Regen.
            BST_CrucibleLogic.TargetCandidate P(uint nameId, bool proven, bool avoid = false) => new(nameId, 100f, avoid, false, false, 0, true, proven);
            Check("armed, the Drake in Blaze Spikes (panel-flagged) and the Abaddon with an unproven buff: the Drake only",
                Aim(true, P(drake, true, avoid: true), P(abaddonName, false), N(barbmole)).SequenceEqual(new[] { 0 }));
            Check("armed, only unproven carriers up: they are still aimed at (the first two tries are how the rotation learns)",
                Aim(true, P(abaddonName, false), N(barbmole)).SequenceEqual(new[] { 0 }));
            Check("armed, two proven carriers: both stay candidates",
                Aim(true, P(drake, true, avoid: true), P(abaddonName, true)).SequenceEqual(new[] { 0, 1 }));
        }
        finally
        {
            CrucibleNeedModel.Extras = null;
        }
    }

    private static BstState CrucibleState(int level = 30)
    {
        var s = BaseState(level);
        s.CrucibleBoard = 1; s.CrucibleBattle = 1; s.EnemyCount = 1;
        s.HighestEnemyHpPercent = 100f; s.PlayerHpPercent = 100f; s.PetHpPercent = 100f; s.PetHp = 20000f;
        s.Slot1Beast = 1; s.Slot2Beast = 34; s.Slot3Beast = 26; s.SlotBeastsKnown = true;
        s.ActiveSlot = 1; s.PetObjectPresent = true; s.PetObjectBeast = 1;
        s.OneWithNature = false; s.ReadyParting = true; s.SinceSummon = 20f; s.SinceHornPress = 21f;
        s.ReadyHorn2 = true; s.ReadyHorn3 = true;
        s.EnemyTargetsPlayer = true;
        s.SinceSnarl = float.MaxValue;
        return s;
    }

    /// <summary>
    ///     The harness settings must be the settings the plugin ships. 2026-10-05: <c>BstSettings.Defaults()</c> had the Snarl rules in log-only
    ///     mode (aggro Shadow, Snarl -> Parting Blow off) while the plugin's own config defaults are On / on (BST_Config.cs), so every case written with
    ///     the plain defaults ran the Snarl rules the way no player's plugin does. The plugin overwrites every Crucible field from its config, so the
    ///     defaults only ever reached the harness. This reads the shipped literals from BST_Config.cs and compares them field by field.
    /// </summary>
    private static void ShippedDefaults(BstSettings d)
    {
        var src = RepoFile("Combos", "PvE", "BST", "BST_Config.cs");
        string? Literal(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(src, name + @"\s*=\s*new\(\s*""" + name + @"""\s*,\s*([^;,]*?)\)\s*[,;]");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        object? Field(string f) => typeof(BstSettings).GetField(f)?.GetValue(d);
        var bad = new List<string>();
        void Bool(string cfgName, string field)
        {
            var lit = Literal(cfgName);
            if (lit is null || Field(field) is not bool v || v != (lit == "true")) bad.Add($"{field}: shipped {lit ?? "missing"}, harness {Field(field)}");
        }
        void Int(string cfgName, string field, Func<string, int> parse)
        {
            var lit = Literal(cfgName);
            if (lit is null || Field(field) is not { } v || Convert.ToInt32(v) != parse(lit)) bad.Add($"{field}: shipped {lit ?? "missing"}, harness {Field(field)}");
        }
        Bool("BST_Crucible", "Crucible");
        Int("BST_CruciblePetSwapHp", "CruciblePetSwapHp", int.Parse);
        Int("BST_CrucibleFinalStingHp", "CrucibleFinalStingHp", int.Parse);
        Int("BST_CrucibleAggro", "CrucibleAggro", l => l.EndsWith("Off") ? 0 : l.EndsWith("Shadow") ? 1 : l.EndsWith("On") ? 2 : -1);
        Bool("BST_CrucibleAllowDisplacing", "CrucibleAllowDisplacing");
        Bool("BST_CrucibleScoreMode", "CrucibleScoreMode");
        Bool("BST_CrucibleCycleForDamage", "CrucibleCycleForDamage");
        Bool("BST_CruciblePrepullHorns", "CruciblePrepullHorns");
        Bool("BST_CrucibleSnarlParting", "CrucibleSnarlParting");
        Bool("BST_CrucibleSurvival", "CrucibleSurvival");
        Bool("BST_CruciblePackWindow", "CruciblePackWindow");
        var lead = Literal("BST_CrucibleSnarlPartingLead");
        if (lead is null || Math.Abs(d.CrucibleSnarlPartingLead - int.Parse(lead) / 10f) > 0.001f) bad.Add($"CrucibleSnarlPartingLead: shipped {lead ?? "missing"} (tenths), harness {d.CrucibleSnarlPartingLead}");
        Check("harness default settings equal the shipped config defaults (Crucible options, read from BST_Config.cs)", bad.Count == 0, string.Join(" | ", bad));
    }

    private static void CrucibleRules()
    {
        Console.WriteLine("-- crucible rules --");
        var cfg = BstSettings.Defaults();
        ShippedDefaults(cfg);
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        // The log-only modes still exist as options; they were the harness default until 2026-10-05, the plugin ships On / on.
        var shadowCfg = cfg with { CrucibleAggro = CrucibleAggroMode.Shadow, CrucibleSnarlParting = false };
        var spOff = on with { CrucibleSnarlParting = false };
        var cycle = cfg with { CrucibleCycleForDamage = true };
        var prepull = cfg with { CruciblePrepullHorns = true };
        bool IsHorn(uint id) => id is BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn;

        // Outside the Crucible (or with the rules off) the Crucible fields change nothing.
        var messy = CrucibleState() with
        {
            PetHpPercent = 5f, TargetInStance = true, TargetDoNotAttack = true, ProtectedNearTarget = true, TargetHasDispellableBuff = true,
            TargetHasParry = true, ReadySnarl = true, TargetInvulnerable = true, TargetVulnerabilityRemaining = 5f, IsMoving = true,
        };
        var plain = CrucibleState() with { CrucibleBoard = 0 };
        var outside = Decide(messy with { CrucibleBoard = 0 }, cfg);
        Check("board 0: Crucible fields ignored", outside == Decide(plain with { IsMoving = true }, cfg), $"{outside.Reason} vs {Decide(plain, cfg).Reason}");
        Check("Crucible rules off: same as outside", Decide(messy, cfg with { Crucible = false }) == Decide(plain with { IsMoving = true }, cfg));

        // Keeping familiars alive: a healthier familiar blown in over a hurt one; Parting Blow only when critical.
        var hurt = CrucibleState() with { PetHpPercent = 50f };
        var swap = Decide(hurt, cfg);
        Check("familiar at 50%, healthy horn ready: blow it in over the familiar", swap is { ActionId: BST.SecondBattlehorn, Reason: "crucible:petsave-swap" }, $"{swap.Reason} [{swap.Declines}]");
        Check("familiar at 60%: no swap", !Decide(hurt with { PetHpPercent = 60f }, cfg).Reason.StartsWith("crucible:petsave"));
        Check("summoned 5 s ago: grace, no swap", !IsHorn(Decide(hurt with { SinceSummon = 5f }, cfg).ActionId));
        Check("next familiars no healthier: no swap", !IsHorn(Decide(hurt with { Slot2PetHp = 45f, Slot3PetHp = 52f }, cfg).ActionId));
        Check("moving: the 1 s horn cast waits", !IsHorn(Decide(hurt with { IsMoving = true }, cfg).ActionId));
        Check("swap line follows the setting", Decide(hurt with { PetHpPercent = 65f }, cfg with { CruciblePetSwapHp = 70 }).Reason == "crucible:petsave-swap");
        var crit = CrucibleState() with { PetHpPercent = 20f, ReadyHorn2 = false, ReadyHorn3 = false, SinceSummon = 2f, OneWithNature = true, ReadyTempered = true };
        // Horn conservation (Third Board Catoblepas death 2026-09-26): the last Parting Blow recall with
        // no horn ready to resummon left the character at 11% with no familiar and no axe partner; the
        // run died weaponless with the boss at 21%. A recall exit with no resummon keeps the familiar out.
        var keepOut = Decide(crit, cfg);
        Check("critical 20%, no horn ready to resummon: keep the familiar out (no Parting Blow)",
            keepOut.ActionId != BST.PartingBlow && keepOut.Reason != "crucible:petsave-finalsting" && keepOut.Declines.Contains("crucible:petsave-no-resummon-horn"), $"{keepOut.Reason} [{keepOut.Declines}]");
        Check("critical, no horn ready, every enemy under 10%: recall anyway (round ending, familiar saved for the next battle)",
            Decide(crit with { TargetHpPercent = 8f, HighestEnemyHpPercent = 8f }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-partingblow" });
        Check("critical, a ready horn whose familiar is also critical: keep the familiar out",
            Decide(crit with { ReadyHorn2 = true, Slot2PetHp = 26f }, cfg).Declines.Contains("crucible:petsave-no-resummon-horn"));
        Check("critical 20%, a horn ready with a 40% familiar: swap even inside the grace",
            Decide(crit with { ReadyHorn2 = true, Slot2PetHp = 40f }, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:petsave-swap-critical" });
        Check("critical, a horn blown 1 s ago: give it time, no Parting Blow", Decide(crit with { SinceHornPress = 1f }, cfg).ActionId != BST.PartingBlow);
        Check("critical wespe, a resummonable horn (28% familiar): Final Sting save still fires",
            Decide(crit with { Slot1Beast = 10, PetObjectBeast = 10, ReadyHorn2 = true, Slot2PetHp = 28f }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:petsave-finalsting" },
            Decide(crit with { Slot1Beast = 10, PetObjectBeast = 10, ReadyHorn2 = true, Slot2PetHp = 28f }, cfg).Reason);
        Check("critical wespe, no resummon: keep it out (no Final Sting save)",
            Decide(crit with { Slot1Beast = 10, PetObjectBeast = 10 }, cfg).ActionId != BST.TemperedRelease);
        Check("last enemy at 2%: no save", !Decide(crit with { TargetHpPercent = 2f, HighestEnemyHpPercent = 2f }, cfg).Reason.StartsWith("crucible:petsave"));
        Check("Parting Blow recasting: declined, logged", Decide(crit with { ReadyParting = false, ReadyHorn2 = true, Slot2PetHp = 28f }, cfg).Declines.Contains("crucible:petsave-partingblow-recast"));
        Check("egg near the target: no save Parting Blow", Decide(crit with { ProtectedNearTarget = true }, cfg).ActionId != BST.PartingBlow);

        var curtains = CrucibleState() with { TargetCastId = 49429, TargetCastRemaining = 1.5f, PetHpPercent = 100f, ReadyParting = true };
        Check("Curtains for Rank 5 cast (pet KO): Parting Blow before resolve even with healthy pet",
            Decide(curtains, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-curtains" });
        // The whole-window rule recalls from the cast start: the sweep's property (a recall out while >= 2.4 s of
        // retreat time remain before the KO) is only met in every GCD state that way, so at 4 s remaining the recall
        // is already the required behaviour. The old "not yet" case pinned the starved last-2.5 s window.
        Check("Curtains for Rank 5 with 4 s remaining: recall out (whole-window rule)",
            Decide(curtains with { TargetCastRemaining = 4.0f }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-curtains" });

        // Timing sweep (the Forward Guard recall starved this exact way until 1.0.4.245): across the whole 6.0 s cast
        // (6.0 s down to 0.25 s in 0.25 s steps) and all three GCD/weave states, on both cast ids, the rule must issue
        // the recall while at least 2.4 s of retreat time remain before the KO (retreat measured 2.15-2.3 s in the raw
        // 2026-10-03 logs; the KO lands about 1.0 s after the cast completes, so a recall issued at cast remaining R
        // leaves R + 1.0 s of retreat time).
        var curtainsSweepMiss = new List<string>();
        foreach (var curtainsCastId in new uint[] { 49428, 49429 })
            foreach (var (gcdReady, canWeave) in new[] { (true, false), (false, true), (false, false) })
            {
                float? firedAt = null;
                for (var rem = 6.00f; rem >= 0.25f; rem -= 0.25f)
                {
                    if (Decide(CrucibleState() with { TargetCastId = curtainsCastId, TargetCastRemaining = rem, GcdReady = gcdReady, CanWeave = canWeave }, cfg)
                        is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-curtains" })
                    {
                        firedAt = rem;
                        break;
                    }
                }
                if (firedAt is not { } f)
                    curtainsSweepMiss.Add($"{curtainsCastId} GCD {(gcdReady ? "idle" : "rolling")}{(canWeave ? ", weavable" : ", not weavable")}: never recalled");
                else if (f + 1.0f < 2.4f)
                    curtainsSweepMiss.Add($"{curtainsCastId} GCD {(gcdReady ? "idle" : "rolling")}{(canWeave ? ", weavable" : ", not weavable")}: first recall at {f:F2} s left ({f + 1.0f:F2} s before the KO)");
            }
        Check("Curtains timing sweep (6.0 -> 0.25 s, 0.25 s steps, 3 GCD/weave states, both casts): recalled while >= 2.4 s of retreat time remain before the KO",
            curtainsSweepMiss.Count == 0, string.Join(" | ", curtainsSweepMiss));

        var forwardGuard = CrucibleState() with { TargetCastId = 46864, TargetCastRemaining = 2.0f, PetHpPercent = 100f, ReadyParting = true };
        Check("Forward Guard cast (directional parry): Parting Blow recalls familiar before guard lands",
            Decide(forwardGuard, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" });
        // Replay of failing run (2026-09-28 run 2): Forward Guard cast began at 4.4 s remaining;
        // recall must fire immediately rather than waiting for <= 2.5 s or being starved by GCD/weave states.
        Check("Forward Guard replay (4.4 s remaining): recall fires immediately",
            Decide(forwardGuard with { TargetCastRemaining = 4.4f }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" });
        Check("Forward Guard replay (4.0 s remaining): recall fires",
            Decide(forwardGuard with { TargetCastRemaining = 4.0f }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" });
        Check("Forward Guard replay (GCD ready, CanWeave false): recall fires, no weaponskill",
            Decide(forwardGuard with { TargetCastRemaining = 2.0f, GcdReady = true, CanWeave = false }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" });
        Check("Forward Guard replay (GCD rolling, CanWeave false): recall fires, no weaponskill",
            Decide(forwardGuard with { TargetCastRemaining = 2.0f, GcdReady = false, CanWeave = false }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" });

        // Timing sweep: across the entire cast window (4.8 s down to 0.5 s) and all 3 weave/GCD states,
        // recall must remain deterministic and never flip back to weaponskills.
        var sweepPassed = true;
        for (var rem = 4.8f; rem >= 0.5f; rem -= 0.5f)
        {
            var canWeave = Decide(forwardGuard with { TargetCastRemaining = rem, GcdReady = false, CanWeave = true }, cfg);
            var gcdReady = Decide(forwardGuard with { TargetCastRemaining = rem, GcdReady = true, CanWeave = false }, cfg);
            var gcdRolling = Decide(forwardGuard with { TargetCastRemaining = rem, GcdReady = false, CanWeave = false }, cfg);
            if (canWeave is not { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" } ||
                gcdReady is not { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" } ||
                gcdRolling is not { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" })
            {
                sweepPassed = false;
                break;
            }
        }
        Check("Forward Guard timing sweep (4.8 s -> 0.5 s, all GCD/weave states): deterministic recall", sweepPassed);

        var parryFacing = CrucibleState() with { TargetHasParry = true, EnemyTargetsPlayer = true, EnemyTargetsPet = false, GcdReady = true, ReadyParting = false };
        Check("Directional Parry facing player: hold attacks",
            Decide(parryFacing, cfg) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-parry" });
        Check("Directional Parry facing pet: attacks allowed from behind",
            Decide(parryFacing with { EnemyTargetsPet = true, EnemyTargetsPlayer = false }, cfg).ActionId != BST_CrucibleLogic.Hold);
        Check("Directional Parry drops (expiry): damage resumes",
            Decide(parryFacing with { TargetHasParry = false }, cfg).ActionId != BST_CrucibleLogic.Hold);

        // ---- 2026-10-05 18:47 ET, First Board of the Unbroken, Second Degree (encounter 2286): replay of the real signals.
        // Horns 3 (Ice Golem), 1 (Salamander), 2 (Ghost) were all pressed inside 70 s and every one was locked when the
        // Knight began Forward Guard (CR c=46864:4.6, pet=100 Ghost summoned 1.3 s earlier, sl=29.100.100). The recall
        // (petsave-guard) sent the healthy Ghost away with no horn to bring another back; the guard then stood for
        // 168 s because only a familiar's Snarl turns the Knight.
        var guardAllHornsLocked = CrucibleState() with
        {
            ActiveSlot = 2, PetObjectPresent = true, PetObjectBeast = 34, PetHpPercent = 100f, SinceSummon = 1.3f, SinceHornPress = 1.3f,
            ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = false,
            TargetCastId = 46864, TargetCastRemaining = 4.6f, ReadyParting = true, GcdReady = true, CanWeave = false,
        };
        var lockedRecall = Decide(guardAllHornsLocked, cfg);
        Check("Forward Guard cast, every horn locked (2026-10-05 replay): the healthy familiar stays out",
            lockedRecall.ActionId != BST.PartingBlow && lockedRecall.Reason != "crucible:petsave-guard", $"{lockedRecall.ActionId} {lockedRecall.Reason}");
        Check("Forward Guard cast, every horn locked: the declined recall is logged",
            lockedRecall.Declines.Contains("crucible:petsave-guard-no-resummon-horn"), lockedRecall.Declines);
        var lockedSweepOk = true;
        for (var rem = 4.8f; rem >= 0.5f; rem -= 0.5f)
            foreach (var (gcdReady, canWeave) in new[] { (true, false), (false, true), (false, false) })
                if (Decide(guardAllHornsLocked with { TargetCastRemaining = rem, GcdReady = gcdReady, CanWeave = canWeave }, cfg).ActionId == BST.PartingBlow)
                    lockedSweepOk = false;
        Check("Forward Guard cast, every horn locked: no recall anywhere in the cast window or GCD state", lockedSweepOk);
        Check("Forward Guard cast, the only ready horn holds a critical familiar: no recall (it could not be resummoned)",
            Decide(guardAllHornsLocked with { ReadyHorn3 = true, Slot3PetHp = 10f }, cfg).ActionId != BST.PartingBlow);
        Check("Forward Guard cast, a healthy horn is ready: the recall still fires (the 1.0.4.245 behavior with a resummon)",
            Decide(guardAllHornsLocked with { ReadyHorn3 = true, Slot3PetHp = 100f }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-guard" });

        // The guard stood 168 s: once a horn was back (about 18:48:37) the hold-parry decision stopped the GCD, the GCD stayed
        // ready, CanWeave() is false in melee, and the summon gate (CanWeave || !GcdReady) declined summon:waiting-weave for the
        // rest of the fight. A hold sends no GCD, so a ready GCD is the weave window.
        var heldNoPet = CrucibleState() with
        {
            ActiveSlot = 0, PetObjectPresent = false, PetObjectBeast = 0, PetHpPercent = 0f, SinceSummon = 0f, SinceHornPress = 90f,
            ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = true, ReadyParting = true,
            TargetHasParry = true, EnemyTargetsPlayer = true, EnemyTargetsPet = false, GcdReady = true, CanWeave = false, TargetDistance = 1.2f,
        };
        var heldSummon = Decide(heldNoPet, cfg);
        Check("guard up, no familiar, a horn is back, GCD idle in melee (2026-10-05 replay): the horn is pressed",
            heldSummon is { ActionId: BST.ThirdBattlehorn, Reason: "summon:slot3" }, $"{heldSummon.ActionId} {heldSummon.Reason} [{heldSummon.Declines}]");
        Check("guard up, no familiar, no horn back yet: holds, logs summon:no-horn-ready",
            Decide(heldNoPet with { ReadyHorn3 = false }, cfg) is { ActionId: BST_CrucibleLogic.Hold } nh && nh.Declines.Contains("summon:no-horn-ready"));
        Check("guard up, no familiar, horn back, character moving: still no cast while moving",
            Decide(heldNoPet with { IsMoving = true }, cfg) is { } hm && !IsHorn(hm.ActionId) && hm.Declines.Contains("crucible:summon-moving"));
        Check("no guard, no familiar, horn back, GCD idle and not weavable: the summon waits for the weave window (unchanged)",
            Decide(heldNoPet with { TargetHasParry = false }, cfg) is { } nw && !IsHorn(nw.ActionId) && nw.Declines.Contains("summon:waiting-weave"));
        Check("counter stance up (hold-stance), no familiar, horn back, GCD idle: the horn is pressed (same deadlock class)",
            Decide(heldNoPet with { TargetHasParry = false, TargetInStance = true }, cfg).ActionId == BST.ThirdBattlehorn);
        Check("do-not-attack target (hold-do-not-attack), no familiar, horn back, GCD idle: the horn is pressed",
            Decide(heldNoPet with { TargetHasParry = false, TargetDoNotAttack = true }, cfg).ActionId == BST.ThirdBattlehorn);
        Check("invulnerable target (hold-invulnerable), no familiar, horn back, GCD idle: the horn is pressed",
            Decide(heldNoPet with { TargetHasParry = false, TargetInvulnerable = true }, cfg).ActionId == BST.ThirdBattlehorn);
        Check("board 0 (outside the Crucible): the idle-GCD summon exception does not apply",
            Decide(heldNoPet with { CrucibleBoard = 0 }, cfg) is { } b0 && !IsHorn(b0.ActionId));

        // The turn: with the familiar out the Snarl is what ends the guard; it was behind the same CanWeave gate in the hold branch.
        var heldWithPet = heldNoPet with
        {
            ActiveSlot = 3, PetObjectPresent = true, PetObjectBeast = 26, PetHpPercent = 100f, SinceSummon = 3f, SinceHornPress = 3f,
            ReadyHorn3 = false, ReadySnarl = true,
        };
        var heldSnarl = Decide(heldWithPet, on);
        Check("guard up facing the character, familiar out, GCD idle in melee (2026-10-05 replay): Snarl turns the Knight",
            heldSnarl is { ActionId: BST.Snarl, Reason: "aggro:snarl-parry" }, $"{heldSnarl.ActionId} {heldSnarl.Reason} [{heldSnarl.Declines}]");
        Check("guard facing the familiar: damage resumes (no hold)",
            Decide(heldWithPet with { EnemyTargetsPet = true, EnemyTargetsPlayer = false }, on).ActionId != BST_CrucibleLogic.Hold);
        Check("guard up, familiar just summoned (inside the settle time): holds, no Snarl yet",
            Decide(heldWithPet with { SinceHornPress = 1f }, on) is { ActionId: BST_CrucibleLogic.Hold });

        // Party-agent HP lag after Parting Blow / horn-swap (first-board run 2026-09-17: live 19% → agent 100 for ~48 s)
        var mem = new Dictionary<int, float> { [20] = 19f };
        Check("party agent 100 after a low leave: keep 19",
            !BST_CrucibleLogic.ApplyPartyHpSample(mem, 20, 100f, recentlyLeftLow: true) && mem[20] == 19f);
        Check("party agent 49 after a low leave: accept the partial heal",
            BST_CrucibleLogic.ApplyPartyHpSample(mem, 20, 49f, recentlyLeftLow: true) && mem[20] == 49f);
        mem[20] = 19f;
        Check("party agent 100 after grace: accept the camp restore",
            BST_CrucibleLogic.ApplyPartyHpSample(mem, 20, 100f, recentlyLeftLow: false) && mem[20] == 100f);
        Check("party agent 76 while remembered 100: accept the drop",
            BST_CrucibleLogic.ApplyPartyHpSample(mem, 20, 76f, recentlyLeftLow: false) && mem[20] == 76f);

        var covering = hurt with { SinceSnarl = 5f, EnemyTargetsPet = true, EnemyTargetsPlayer = false };
        Check("covering familiar at 50%: stays", !IsHorn(Decide(covering, cfg).ActionId));
        Check("covering familiar at 20%: saved", Decide(covering with { PetHpPercent = 20f }, cfg).Reason.StartsWith("crucible:petsave"));

        // No damage cycling by default; the round-ending guard when it is on.
        Check("default: no Parting Blow exit to cycle damage", Decide(CrucibleState(), cfg) is { ActionId: not BST.PartingBlow } hold && hold.Declines.Contains("crucible:exit-hold"));
        Check("cycling on, 60%: Parting Blow", Decide(CrucibleState() with { TargetHpPercent = 60f, HighestEnemyHpPercent = 60f }, cycle).ActionId == BST.PartingBlow);
        var ending = Decide(CrucibleState() with { TargetHpPercent = 8f, HighestEnemyHpPercent = 8f }, cycle);
        Check("cycling on, every enemy under 10%: no exit", ending.ActionId != BST.PartingBlow && ending.Declines.Contains("crucible:exit-round-ending"), $"{ending.Reason} [{ending.Declines}]");
        Check("cycling on, another enemy at 50%: exit allowed", Decide(CrucibleState() with { TargetHpPercent = 8f, HighestEnemyHpPercent = 50f, EnemyCount = 2 }, cycle).ActionId == BST.PartingBlow);
        var critHorns = Decide(CrucibleState() with { ReadyHorn2 = true, ReadyHorn3 = true, Slot2PetHp = 20f, Slot3PetHp = 20f, TargetHpPercent = 60f, HighestEnemyHpPercent = 60f }, cycle);
        Check("cycling on, only critical familiars on ready horns: no exit (no resummon)",
            critHorns.ActionId != BST.PartingBlow && critHorns.Declines.Contains("crucible:exit-no-resummon-horn"), $"{critHorns.Reason} [{critHorns.Declines}]");

        // Final Sting
        var fs = CrucibleState() with { Slot1Beast = 10, PetObjectBeast = 10, OneWithNature = true, ReadyTempered = true, SinceSummon = 1.5f, ReadyHorn2 = false, ReadyHorn3 = false };
        Check("wespe, target 80%: held", Decide(fs with { TargetHpPercent = 80f }, cfg).ActionId != BST.TemperedRelease);
        Check("wespe, target 35%: held (execute line 30%)", Decide(fs with { TargetHpPercent = 35f }, cfg).ActionId != BST.TemperedRelease);
        var exec = Decide(fs with { TargetHpPercent = 25f }, cfg);
        Check("wespe, target 25%: Final Sting (ignores min stay and other horns)", exec is { ActionId: BST.TemperedRelease, Reason: "exit:finalsting" }, $"{exec.Reason} [{exec.Declines}]");
        Check("wespe, 2 enemies, target 25%: held (line halves)", Decide(fs with { TargetHpPercent = 25f, EnemyCount = 2 }, cfg).ActionId != BST.TemperedRelease);
        Check("wespe, 2 enemies, target 12%: Final Sting", Decide(fs with { TargetHpPercent = 12f, EnemyCount = 2 }, cfg).ActionId == BST.TemperedRelease);
        Check("target 80% but Physical Vulnerability Up has 8 s left: Final Sting", Decide(fs with { TargetHpPercent = 80f, TargetVulnerabilityRemaining = 8f }, cfg).ActionId == BST.TemperedRelease);
        Check("vulnerability with 20 s left: not yet", Decide(fs with { TargetHpPercent = 80f, TargetVulnerabilityRemaining = 20f }, cfg).ActionId != BST.TemperedRelease);
        Check("target dying within 2 s anyway: Final Sting saved", Decide(fs with { TargetHpPercent = 25f, TargetTimeToDeath = 2f }, cfg).ActionId != BST.TemperedRelease);
        Check("wespe covering the character: Final Sting waits", Decide(fs with { TargetHpPercent = 25f, SinceSnarl = 5f, EnemyTargetsPet = true, EnemyTargetsPlayer = false }, cfg).ActionId != BST.TemperedRelease);
        Check("outside the Crucible: unchanged min-stay rule", Decide(fs with { TargetHpPercent = 25f, CrucibleBoard = 0 }, cfg).ActionId != BST.TemperedRelease);
        var mantisOut = CrucibleState() with { Slot1Beast = 16, PetObjectBeast = 16, Slot2Beast = 10, Slot3Beast = 18, TargetHpPercent = 25f, ReadyParting = false };
        Check("mantis out (One with Nature spent), wespe ready, target 25%: blow the wespe in", Decide(mantisOut, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:finalsting-swap" }, Decide(mantisOut, cfg).Reason);
        Check("vulnerability closing at 80%: wespe in", Decide(mantisOut with { TargetHpPercent = 80f, TargetVulnerabilityRemaining = 6f }, cfg).Reason == "crucible:finalsting-swap");
        Check("mantis still holds One with Nature: wait", Decide(mantisOut with { OneWithNature = true, ReadyTempered = true }, cfg).Reason != "crucible:finalsting-swap");
        Check("target healthy, no window: no swap", Decide(mantisOut with { TargetHpPercent = 80f }, cfg).Reason != "crucible:finalsting-swap");

        // Stances, invulnerable phases, do-not-attack
        var spikes = CrucibleState() with { TargetInStance = true, PlayerTp = 150, FamiliarTp = 150, ReadyTrick = true, ReadyAxe = true, GcdReady = true };
        Check("spikes up (not dispellable): hold", Decide(spikes, cfg) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-stance" });
        Check("spikes up, no familiar: still summons (set-up raptor first)",
            Decide(spikes with { ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f, ReadyHorn1 = true }, cfg).ActionId == BST.SecondBattlehorn);
        Check("Ice Spikes with Quelling Wave borrowed: dispel it",
            Decide(spikes with { TargetHasDispellableBuff = true, KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true }, cfg) is { ActionId: BST.QuellingWave, Reason: "crucible:dispel-quellingwave" });
        Check("holding still cleanses",
            Decide(spikes with { PlayerHasCleansableDebuff = true, KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true }, cfg).ActionId == BST.ScouringAsh);
        Check("invulnerable target: hold", Decide(CrucibleState() with { TargetInvulnerable = true, GcdReady = true }, cfg) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-invulnerable" });
        Check("egg targeted: hold", Decide(CrucibleState() with { TargetDoNotAttack = true, GcdReady = true }, cfg) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-do-not-attack" });

        // Protected enemy near the target
        Check("egg near target, cycling on: no Parting Blow exit", Decide(CrucibleState() with { ProtectedNearTarget = true }, cycle).ActionId != BST.PartingBlow);
        var aoe = Decide(CrucibleState() with { ProtectedNearTarget = true, Slot1Beast = 34, PetObjectBeast = 34, OneWithNature = true, ReadyTempered = true, ReadyBorrow = true }, cfg);
        Check("egg near target: AoE release (raptor) -> Borrow instead", aoe.ActionId == BST.Borrow, $"{aoe.Reason} [{aoe.Declines}]");
        Check("egg near target: no Trick",
            Decide(CrucibleState() with { ProtectedNearTarget = true, FamiliarTp = 150, PlayerTp = 150, ReadyTrick = true, ReadyAxe = true, ReadyParting = false }, cfg).ActionId != BST.Trick);

        // Dispel / cleanse / Kinship for the fight
        Check("vulture out, buff on target: Bloodcurdling Caw",
            Decide(CrucibleState() with { Slot1Beast = 11, PetObjectBeast = 11, OneWithNature = true, ReadyTempered = true, TargetHasDispellableBuff = true, SinceSummon = 2f }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:dispel-caw" });
        Check("Wavekin held, buff on target: Quelling Wave even mid-combo",
            Decide(CrucibleState() with { TargetHasDispellableBuff = true, KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true, GcdReady = true, ComboTimerActive = true, LastComboAction = BST.SmashAxe }, cfg).ActionId == BST.QuellingWave);
        Check("no dispel tool: rotation continues",
            Decide(CrucibleState() with { TargetHasDispellableBuff = true, ReadyParting = false, GcdReady = true }, cfg).ActionId == BST.SmashAxe);
        Check("cleansable debuff with Ashkin held: Scouring Ash",
            Decide(CrucibleState() with { PlayerHasCleansableDebuff = true, KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true }, cfg).ActionId == BST.ScouringAsh);
        Check("cleansable debuff, bat out with One with Nature: Ultrasonics",
            Decide(CrucibleState() with { PlayerHasCleansableDebuff = true, Slot1Beast = 19, PetObjectBeast = 19, OneWithNature = true, ReadyTempered = true }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:cleanse-ultrasonics" });
        var coblynFight = CrucibleState() with { Slot1Beast = 7, PetObjectBeast = 7, OneWithNature = true, ReadyTempered = true, ReadyBorrow = true, CrucibleNeeds = CrucibleNeeds.Interrupt, ReadyParting = false };
        Check("coblyn out, a cast interruptible NOW: Borrow Soulkin (the re-arm)",
            Decide(coblynFight with { TargetInterruptible = true }, cfg) is { ActionId: BST.Borrow, Reason: "crucible:borrow-soulkin" });
        Check("coblyn out, no cast up: the One with Nature is held for the next cast",
            Decide(coblynFight, cfg).Declines.Contains("own:held-for-interrupt"));
        Check("Soul Kinship already held: Tempered Release", Decide(coblynFight with { KinshipHeld = true, BeastModeResolved = BST.SoulCrush, KinshipSlot = 2 }, cfg).ActionId == BST.TemperedRelease);

        // The familiar that answers a live need is brought out when it is not (2026-10-01 run, board 4 Strix Piece: the
        // vulture stayed on its horn for the whole fight while Ultimate Focus sat on the boss; 19 x crucible:dispel-unavailable).
        var live = CrucibleState(50) with { SinceSummon = 12f, ReadyParting = false, SinceHornPress = 30f };
        var strix = live with
        {
            CrucibleBoard = 4, CrucibleBattle = 1, CrucibleNeeds = BST_CrucibleData.BattleNeeds(4, 1),
            Slot1Beast = 18, Slot2Beast = 8, Slot3Beast = 11, ActiveSlot = 2, PetObjectBeast = 8,
            ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = true, TargetHasDispellableBuff = true, GcdReady = true,
        };
        Check("board 4 Strix Piece: the enemy panel needs a dispel", (strix.CrucibleNeeds & CrucibleNeeds.Dispel) != 0, strix.CrucibleNeeds.ToString());
        var strixSwap = Decide(strix, cfg);
        Check("Strix Piece replay: Ultimate Focus up, Diremite out, vulture on a ready horn 3: blow horn 3",
            strixSwap is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-dispel-slot3" }, $"{strixSwap.Reason} [{strixSwap.Declines}]");
        Check("... vulture already out: Bloodcurdling Caw, no swap",
            Decide(strix with { ActiveSlot = 3, PetObjectBeast = 11, OneWithNature = true, ReadyTempered = true, SinceSummon = 3f }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:dispel-caw" });
        Check("... the buff is gone: no swap", !IsHorn(Decide(strix with { TargetHasDispellableBuff = false }, cfg).ActionId));
        Check("... the fight does not call for a dispel: no swap", !IsHorn(Decide(strix with { CrucibleNeeds = CrucibleNeeds.None }, cfg).ActionId));
        var notReady = Decide(strix with { ReadyHorn3 = false }, cfg);
        Check("... the vulture's horn is locked: no swap, the reason is logged, the rotation goes on",
            !IsHorn(notReady.ActionId) && notReady.Declines.Contains("crucible:answer-dispel-horn-not-ready"), $"{notReady.Reason} [{notReady.Declines}]");
        Check("... the vulture is below the critical line: no swap", !IsHorn(Decide(strix with { Slot3PetHp = 10f }, cfg).ActionId));
        var movingSwap = Decide(strix with { IsMoving = true }, cfg);
        Check("... moving: waits, says so", !IsHorn(movingSwap.ActionId) && movingSwap.Declines.Contains("crucible:answer-dispel-waiting"), $"{movingSwap.Reason} [{movingSwap.Declines}]");
        Check("... the horn was blown a moment ago: waits", !IsHorn(Decide(strix with { SinceHornPress = 1f }, cfg).ActionId));
        Check("... outside the Crucible: unchanged", !IsHorn(Decide(strix with { CrucibleBoard = 0 }, cfg).ActionId));
        var waveOnly = strix with { Slot3Beast = 4, Slot1Beast = 1 };
        Check("no vulture, a pugil (Wavekin) on horn 3: blow it, Borrow follows", Decide(waveOnly, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "crucible:answer-dispel-slot3" });
        Check("... Wavekin out with One with Nature: Borrow",
            Decide(waveOnly with { ActiveSlot = 3, PetObjectBeast = 4, OneWithNature = true, ReadyTempered = true, ReadyBorrow = true, SinceSummon = 3f }, cfg) is { ActionId: BST.Borrow, Reason: "crucible:borrow-wavekin" });
        var batFight = live with
        {
            CrucibleNeeds = CrucibleNeeds.Cleanse, Slot1Beast = 1, Slot2Beast = 19, Slot3Beast = 34, ActiveSlot = 1, PetObjectBeast = 1,
            ReadyHorn2 = true, PlayerHasCleansableDebuff = true, GcdReady = true,
        };
        Check("fight needs a cleanse, debuff on the character, bat on a ready horn: blow its horn", Decide(batFight, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:answer-cleanse-slot2" });
        Check("... Scouring Ash already held: no swap", !IsHorn(Decide(batFight with { KinshipHeld = true, BeastModeResolved = BST.ScouringAsh, ReadyBeastMode = true }, cfg).ActionId));
        var soulFight = live with
        {
            CrucibleNeeds = CrucibleNeeds.Interrupt, Slot1Beast = 1, Slot2Beast = 7, Slot3Beast = 34, ActiveSlot = 1, PetObjectBeast = 1,
            ReadyHorn2 = true, TargetInterruptible = true, GcdReady = true,
        };
        Check("fight needs an interrupt, an interruptible cast is up, Soulkin on a ready horn: blow its horn", Decide(soulFight, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:answer-interrupt-slot2" });
        Check("... no interruptible cast: no swap", !IsHorn(Decide(soulFight with { TargetInterruptible = false }, cfg).ActionId));
        Check("... Soul Kinship already held: no swap", !IsHorn(Decide(soulFight with { KinshipHeld = true, BeastModeResolved = BST.SoulCrush, ReadyBeastMode = true }, cfg).ActionId));
        var both = Decide(soulFight with { Slot3Beast = 11, ReadyHorn3 = true, TargetHasDispellableBuff = true, CrucibleNeeds = CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel }, cfg);
        Check("interrupt and dispel both live: the interrupt (time-critical) goes first", both.Reason == "crucible:answer-interrupt-slot2", both.Reason);
        var noFamiliar = strix with { ActiveSlot = 0, PetObjectPresent = false, PetObjectBeast = 0, SinceHornPress = 30f, ReadyHorn1 = true, ReadyHorn2 = true, ReadyHorn3 = true, Slot1Beast = 1, Slot2Beast = 34 };
        Check("no familiar out, dispel needed: summon the vulture's horn, not the healthiest",
            Decide(noFamiliar, cfg) is { ActionId: BST.ThirdBattlehorn, Reason: "summon:slot3" }, Decide(noFamiliar, cfg).Reason);

        // Out of combat: no horns unless allowed
        var pre = CrucibleState() with
        {
            InCombat = false, ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f,
            ReadyHorn1 = true, ReadyHorn2 = true, ReadyHorn3 = true,
            Slot1Beast = 1, Slot2Beast = 7, Slot3Beast = 4, CrucibleNeeds = CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel,
        };
        var noHorn = Decide(pre, cfg);
        Check("out of combat on a board: no horn by default", !IsHorn(noHorn.ActionId) && noHorn.Declines.Contains("crucible:no-horns-out-of-combat"), $"{noHorn.Reason} [{noHorn.Declines}]");
        Check("allowed: pre-pull, fight needs an interrupt, coblyn on horn 2: summon horn 2", Decide(pre, prepull) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:prepull-soulkin-slot2" });
        var coblyn = pre with { ActiveSlot = 2, PetObjectPresent = true, PetObjectBeast = 7, OneWithNature = true, ReadyBorrow = true, SinceHornPress = 3f };
        Check("allowed: coblyn out pre-pull, the Soulkin's borrow waits for a live cast (no prepull window waste)",
            Decide(coblyn, prepull).Declines.Contains("crucible:prepull-borrow-waiting-cast"));
        Check("allowed: pugil out pre-pull on a dispel fight: the Wavekin borrow still fires (only the Soulkin waits)",
            Decide(coblyn with { CrucibleNeeds = CrucibleNeeds.Dispel, Slot2Beast = 4, PetObjectBeast = 4 }, prepull) is { ActionId: BST.Borrow, Reason: "crucible:prepull-borrow-wavekin" });
        var held = Decide(coblyn with { OneWithNature = false, KinshipHeld = true, KinshipSlot = 2, BeastModeResolved = BST.SoulCrush, SinceHornPress = 6f }, prepull);
        Check("allowed: Soul Kinship held, swap to horn 1", held.ActionId == BST.FirstBattlehorn, $"{held.Reason} [{held.Declines}]");
        Check("allowed: no Soulkin, pugil on horn 3, Wavekin", Decide(pre with { Slot2Beast = 26 }, prepull).Reason == "crucible:prepull-wavekin-slot3");
        Check("in combat, no familiar, moving: no horn", Decide(CrucibleState() with { ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f, ReadyHorn1 = true, IsMoving = true }, cfg) is { } mv
            && !IsHorn(mv.ActionId) && mv.Declines.Contains("crucible:summon-moving"));

        // Snarl / Challenge (default: logged only)
        var parry = CrucibleState() with { TargetHasParry = true, ReadySnarl = true, ReadyParting = false };
        var shadowed = Decide(parry, shadowCfg);
        Check("parry, shadow mode (an option): Snarl logged, not pressed", shadowed.ActionId != BST.Snarl && shadowed.Shadow == "aggro:snarl-parry", $"{shadowed.ActionId} {shadowed.Shadow}");
        Check("parry, On: Snarl", Decide(parry, on).ActionId == BST.Snarl);
        Check("parry, Off: nothing", Decide(parry, cfg with { CrucibleAggro = CrucibleAggroMode.Off }) is { Shadow: "" } off && off.ActionId != BST.Snarl);
        Check("parry just ended with the familiar holding aggro, On: Challenge",
            Decide(CrucibleState() with { ParryJustEnded = true, ReadyChallenge = true, EnemyTargetsPet = true, EnemyTargetsPlayer = false, ReadyParting = false }, on).ActionId == BST.Challenge);
        Check("hard hit on the character: no Snarl (the familiar would lose ~3x what the character saves)",
            Decide(CrucibleState() with { TargetCastId = 46906, TargetCastRemaining = 3f, ReadySnarl = true, ReadyParting = false }, on).ActionId != BST.Snarl);
        var lowChar = CrucibleState() with { PlayerHpPercent = 35f, PetHpPercent = 80f, PetHp = 20000f, PlayerIntakePerSecond = 500f, ReadySnarl = true, ReadyParting = false };
        Check("character 35%, familiar can carry 15 s of intake: Snarl", Decide(lowChar, on).Reason == "aggro:snarl-player-low", Decide(lowChar, on).Reason);
        Check("character 35%, familiar cannot carry it: no Snarl", Decide(lowChar with { PlayerIntakePerSecond = 2000f }, on).ActionId != BST.Snarl);
        // 2026-10-05 18:47 (encounter 2286): the petsave-guard Parting Blow at 26.85 sent the Ghost away; at 28.24 a
        // Snarl (aggro:snarl-player-low) was pressed onto the already-retreating familiar (gone at 29.0) — wasted, and
        // the enemy stayed on the character. While a recall is in flight the familiar is leaving: no Snarl onto it.
        var recalling = CrucibleState() with { PlayerHpPercent = 35f, PetHpPercent = 80f, PetHp = 20000f, PlayerIntakePerSecond = 500f, ReadySnarl = true, ReadyParting = false, SincePartingBlow = 1.39f };
        Check("Parting Blow 1.4 s ago, the familiar already leaving: no Snarl onto it",
            Decide(recalling, on).ActionId != BST.Snarl, Decide(recalling, on).Reason);
        Check("Parting Blow 3.5 s ago (recall no longer in flight): the Snarl rules resume",
            Decide(recalling with { SincePartingBlow = 3.5f }, on).Reason == "aggro:snarl-player-low",
            Decide(recalling with { SincePartingBlow = 3.5f }, on).Reason);
        Check("character 20%: last-resort Snarl", Decide(lowChar with { PlayerHpPercent = 20f, PlayerIntakePerSecond = 2000f }, on).Reason == "aggro:snarl-last-resort");
        Check("character 11%, familiar 30% (under the 50% floor, cannot carry): last-resort Snarl anyway",
            Decide(lowChar with { PlayerHpPercent = 11f, PetHpPercent = 30f, ReadyHorn2 = false, ReadyHorn3 = false }, on).Reason == "aggro:snarl-last-resort",
            Decide(lowChar with { PlayerHpPercent = 11f, PetHpPercent = 30f, ReadyHorn2 = false, ReadyHorn3 = false }, on).Reason);
        var deathWindow = Decide(crit with { PlayerHpPercent = 11f, ReadySnarl = true }, on);
        Check("death window: player 11%, familiar critical, no horns: Snarl the familiar in, no recall",
            deathWindow is { ActionId: BST.Snarl, Reason: "aggro:snarl-last-resort" } && deathWindow.Declines.Contains("crucible:petsave-no-resummon-horn"),
            $"{deathWindow.Reason} [{deathWindow.Declines}]");
        Check("wespe about to Final Sting: no Snarl",
            Decide(lowChar with { Slot1Beast = 10, PetObjectBeast = 10, TargetHpPercent = 25f }, cfg).Shadow != "aggro:snarl-player-low");
        Check("familiar 25% holding aggro, character 80%, On: Challenge",
            Decide(CrucibleState() with { PetHpPercent = 25f, ReadyHorn2 = false, ReadyHorn3 = false, ReadyParting = false, EnemyTargetsPet = true, EnemyTargetsPlayer = false, ReadyChallenge = true }, on).Reason == "aggro:challenge-pet-low");

        // Score mode and Snarl -> Parting Blow (a stand-in tankbuster id, then a real one)
        const uint tb = 999_001;
        BST_CrucibleData.Tankbusters.Add(tb);
        var scoreCfg = on with { CrucibleScoreMode = true };
        Check("score mode: target on the familiar -> Challenge",
            Decide(CrucibleState() with { EnemyTargetsPet = true, EnemyTargetsPlayer = false, ReadyChallenge = true, ReadyParting = false }, scoreCfg).Reason == "aggro:challenge-score");
        Check("score mode: a low character does not Snarl", Decide(lowChar, scoreCfg).ActionId != BST.Snarl);
        var spCfg = on with { CrucibleSnarlParting = true, CrucibleTankbusterParting = true }; // the dormant Parting Blow step, opted in
        var castStart = CrucibleState() with { TargetCastId = tb, TargetCastRemaining = 4f, ReadySnarl = true };
        Check("tankbuster cast starts: Snarl", Decide(castStart, spCfg).Reason == "aggro:snarl-tankbuster", Decide(castStart, spCfg).Reason);
        var landing = CrucibleState() with { TargetCastId = tb, TargetCastRemaining = 1.2f, SinceSnarl = 3f, EnemyTargetsPet = true, EnemyTargetsPlayer = false };
        Check("1.2 s before it lands with Snarl up: Parting Blow", Decide(landing, spCfg) is { ActionId: BST.PartingBlow, Reason: "crucible:snarl-parting" });
        Check("3 s before it lands: not yet", Decide(landing with { TargetCastRemaining = 3f }, spCfg).Reason != "crucible:snarl-parting");
        Check("no Snarl in the last 45 s: no whiff", Decide(landing with { SinceSnarl = 60f }, spCfg).Reason != "crucible:snarl-parting");
        Check("snarl-parting switched off: no Parting Blow", Decide(landing, spOff).Reason != "crucible:snarl-parting");
        // Never evaluated while off, so no run could grade it (0 snarl-parting decisions in 209 logged tankbuster casts, 2026-09-24 .. 10-01):
        // the open window is logged in the shadow field while the option is off, so the next run can be graded without anyone reporting it.
        Check("snarl-parting off: the open window is logged for grading and nothing is pressed",
            Decide(landing, spOff) is { Shadow: "crucible:snarl-parting-off" } offLogged && offLogged.ActionId != BST.PartingBlow, Decide(landing, spOff).Shadow);
        Check("... also when no Snarl was used (the dodge never set up)", Decide(landing with { SinceSnarl = 60f }, spOff).Shadow == "crucible:snarl-parting-off");
        Check("... not logged before the window (3 s before it lands)", Decide(landing with { TargetCastRemaining = 3f }, spOff).Shadow != "crucible:snarl-parting-off");
        Check("... not logged with the aggro option off", Decide(landing, spOff with { CrucibleAggro = CrucibleAggroMode.Off }).Shadow != "crucible:snarl-parting-off");
        Check("... not logged when the option is on (it presses instead)", Decide(landing, spCfg).Shadow != "crucible:snarl-parting-off");
        Check("snarl-parting in log-only mode: logged, not pressed",
            Decide(landing, shadowCfg with { CrucibleSnarlParting = true, CrucibleTankbusterParting = true }) is { Shadow: "crucible:snarl-parting" } logged && logged.Reason != "crucible:snarl-parting");
        Check("score mode with the dormant Parting Blow step opted in: Snarl for the tankbuster", Decide(castStart, scoreCfg with { CrucibleSnarlParting = true, CrucibleTankbusterParting = true }).ActionId == BST.Snarl);
        BST_CrucibleData.Tankbusters.Remove(tb);
        Check("Erratic Blaster castbar 1.4 s left (lands in 1.7 s): not yet", Decide(landing with { TargetCastId = 49188, TargetCastRemaining = 1.4f }, spCfg).Reason != "crucible:snarl-parting");
        Check("Erratic Blaster castbar 1.0 s left (lands in 1.3 s): Parting Blow", Decide(landing with { TargetCastId = 49188, TargetCastRemaining = 1.0f }, spCfg).Reason == "crucible:snarl-parting");

        // Frontal-cleave-auto bosses (Third Board: siren elite 14583, Guttler boss 14592; also Pas de
        // Seul and Lauda). Their autos are a cone that hits the familiar too, so the familiar must NOT
        // hold aggro between hard hits: Challenge it off, Snarl only to cover a known tankbuster cast.
        const uint songOfTorment = 48563, thunderbolt = 48620;
        var cleave = CrucibleState() with { ReadySnarl = true, ReadyChallenge = true, ReadyParting = false, PetHpPercent = 80f };
        Check("siren: Song of Torment cast at the player, pet healthy: Snarl covers",
            Decide(cleave with { TargetNameId = 14583, TargetCastId = songOfTorment, TargetCastRemaining = 3f }, on).Reason == "aggro:snarl-cleave-tankbuster",
            Decide(cleave with { TargetNameId = 14583, TargetCastId = songOfTorment, TargetCastRemaining = 3f }, on).Reason);
        Check("guttler: Thunderbolt cast at the player: Snarl covers",
            Decide(cleave with { TargetNameId = 14592, TargetCastId = thunderbolt, TargetCastRemaining = 3f }, on).Reason == "aggro:snarl-cleave-tankbuster",
            Decide(cleave with { TargetNameId = 14592, TargetCastId = thunderbolt, TargetCastRemaining = 3f }, on).Reason);
        Check("siren: tankbuster cast but the pet is low: no cover Snarl",
            Decide(cleave with { TargetNameId = 14583, TargetCastId = songOfTorment, TargetCastRemaining = 3f, PetHpPercent = 30f }, on).Reason != "aggro:snarl-cleave-tankbuster");
        Check("siren: pet holding aggro, no cast, player healthy: Challenge it off",
            Decide(cleave with { TargetNameId = 14583, EnemyTargetsPet = true, EnemyTargetsPlayer = false }, on).Reason == "aggro:challenge-cleave-auto",
            Decide(cleave with { TargetNameId = 14583, EnemyTargetsPet = true, EnemyTargetsPlayer = false }, on).Reason);
        Check("siren: cast resolved, Snarl cover still on the pet: Challenge back",
            Decide(cleave with { TargetNameId = 14583, EnemyTargetsPet = true, EnemyTargetsPlayer = false, SinceSnarl = 5f }, on).Reason == "aggro:challenge-cleave-auto");
        Check("siren: no Challenge while the tankbuster cast is still up (the pet must keep Cover)",
            Decide(cleave with { TargetNameId = 14583, EnemyTargetsPet = true, EnemyTargetsPlayer = false, TargetCastId = songOfTorment, TargetCastRemaining = 3f }, on).Reason != "aggro:challenge-cleave-auto");
        Check("siren: player below 50%: no cleave Challenge (the low-player rules own that state)",
            Decide(cleave with { TargetNameId = 14583, EnemyTargetsPet = true, EnemyTargetsPlayer = false, PlayerHpPercent = 40f }, on).Reason != "aggro:challenge-cleave-auto");
        Check("ymir (not a cleave boss): pet holding aggro, player healthy: no cleave Challenge",
            Decide(cleave with { TargetNameId = 14569, EnemyTargetsPet = true, EnemyTargetsPlayer = false }, on).Reason != "aggro:challenge-cleave-auto");
        Check("siren cleave Challenge is logged in shadow mode, not pressed",
            Decide(cleave with { TargetNameId = 14583, EnemyTargetsPet = true, EnemyTargetsPlayer = false }, shadowCfg) is { Shadow: "aggro:challenge-cleave-auto" } sh && sh.Reason != "aggro:challenge-cleave-auto");

        // A stance hold with no familiar out and the player dying is a death spiral: fight instead.
        var stanceAlone = CrucibleState() with
        {
            TargetInStance = true, ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f,
            ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = false, PlayerHpPercent = 35f, GcdReady = true,
        };
        Check("stance up, no familiar, player low: fight instead of holding",
            Decide(stanceAlone, on) is { ActionId: not BST_CrucibleLogic.Hold } fight && fight.Declines.Contains("crucible:stance-break-no-familiar"),
            $"{Decide(stanceAlone, on).ActionId} [{Decide(stanceAlone, on).Declines}]");
        Check("stance up, no familiar, player healthy: still holds",
            Decide(stanceAlone with { PlayerHpPercent = 90f }, on).ActionId == BST_CrucibleLogic.Hold);
        Check("stance up, familiar out: still holds (counter damage is the pet's problem)",
            Decide(CrucibleState() with { TargetInStance = true }, on).ActionId == BST_CrucibleLogic.Hold);

        // Summon order: healthy first, set-up beasts before the rest, wespe last unless its Final Sting is due
        var none = CrucibleState() with { ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f, ReadyHorn1 = true, ReadyHorn2 = true, ReadyHorn3 = false, Slot1PetHp = 12f, Slot2PetHp = 90f };
        Check("horn 1 familiar last seen at 12%: summon horn 2", Decide(none, cfg).ActionId == BST.SecondBattlehorn);
        Check("every ready familiar critical, Parting Blow recasting: wait",
            Decide(none with { ReadyHorn3 = true, Slot2PetHp = 10f, Slot3PetHp = 8f, PartingBlowRecast = 7f }, cfg).ActionId is not (BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn));
        Check("every ready familiar low, Parting Blow ready: the healthiest",
            Decide(none with { ReadyHorn3 = true, Slot2PetHp = 10f, Slot3PetHp = 14f, PartingBlowRecast = 0f }, cfg).ActionId == BST.ThirdBattlehorn);
        var opener = CrucibleState() with { ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f, ReadyHorn1 = true, Slot1Beast = 10, Slot2Beast = 18, Slot3Beast = 16 };
        Check("wespe / dullahan / mantis: set-up mantis first", Decide(opener, cfg).ActionId == BST.ThirdBattlehorn);
        Check("wespe on horn 1, target at 25%: wespe now", Decide(opener with { TargetHpPercent = 25f }, cfg).ActionId == BST.FirstBattlehorn);
        Check("wespe the only ready horn: wespe", Decide(opener with { ReadyHorn2 = false, ReadyHorn3 = false }, cfg).ActionId == BST.FirstBattlehorn);
        Check("allowed pre-pull: wespe on horn 1 is not the opener", Decide(opener with { InCombat = false }, prepull).ActionId == BST.ThirdBattlehorn);

        // Knockback / draw-in releases are fine solo
        var toad = CrucibleState() with { Slot1Beast = 31, PetObjectBeast = 31, OneWithNature = true, ReadyTempered = true, ReadyParting = false };
        Check("Crucible: gigantoad's Sticky Tongue used", Decide(toad, cfg).ActionId == BST.TemperedRelease);
        Check("outside: gigantoad's release still blocked", Decide(toad with { CrucibleBoard = 0 }, cfg).ActionId != BST.TemperedRelease);
    }

    // ================================================================== simulator

    private sealed record Loadout(string Name, int[] Slots);

    private static readonly Loadout[] Loadouts =
    [
        new("CuSith/Raptor/Buffalo", [1, 34, 26]),
        new("Wespe/Crab/empty (09-16 test session)", [10, 15, 0]),
        new("Wespe/Mantis/Dullahan", [10, 16, 18]),
        new("Lamb/Puk/Diremite (all releases blocked)", [3, 14, 8]),
        new("CuSith only", [1, 0, 0]),
        new("Chimera/Buffalo/Behemoth", [38, 26, 50]),
    ];

    private static void SimulateAllLevels(bool verbose)
    {
        Console.WriteLine("-- simulator: 300 s fights, every level 1-50, 6 loadouts, 2 exit policies --");
        var totals = new Dictionary<string, int>();
        var worstUptime = 100.0;
        var worstUptimeRun = "";

        foreach (var loadout in Loadouts)
        {
            for (var level = 1; level <= 50; level++)
            {
                foreach (var minStay in new[] { 10, 0 })
                {
                    var cfg = BstSettings.Defaults() with { MinFamiliarStaySeconds = minStay };
                    var sim = new Sim(level, loadout.Slots, cfg);
                    sim.Run(300f);

                    var tag = $"L{level} {loadout.Name} minStay={minStay}";
                    foreach (var v in sim.Violations)
                        totals[v.Kind] = totals.GetValueOrDefault(v.Kind) + 1;

                    if (sim.Violations.Count > 0)
                    {
                        foreach (var v in sim.Violations.GroupBy(v => v.Kind))
                            Check($"{tag}: {v.Key}", false, $"x{v.Count()} first: {v.First().Detail}");
                    }
                    else
                    {
                        _pass++;
                    }

                    if (sim.HasFamiliarPossible && sim.UptimePercent < worstUptime)
                    {
                        worstUptime = sim.UptimePercent;
                        worstUptimeRun = tag;
                    }

                    if (verbose || level is 1 or 8 or 18 or 20 or 22 or 30 or 44 or 50 && minStay == 10 && loadout == Loadouts[0])
                        Console.WriteLine($"   {tag,-52} uptime {sim.UptimePercent,5:0.0}%  PB {sim.Count(BST.PartingBlow),2}  TR {sim.Count(BST.TemperedRelease),2}  Borrow {sim.Count(BST.Borrow),2}  Trick {sim.Count(BST.Trick),3}  axes {sim.AxesUsed,3}  combos {sim.Combos,3} (intentional {sim.IntentionalCombos,3})  Rally {sim.Count(BST.Rally),2}  Cheer {sim.Count(BST.RallyingCheer),2}  universality {sim.Universality}  wavering-blocked {sim.BlockedByWavering}  horns {sim.Summons,2}");
                }
            }
        }

        Console.WriteLine($"   worst familiar uptime across all runs: {worstUptime:0.0}% ({worstUptimeRun})");
        Check("worst familiar uptime >= 92% whenever a familiar is possible", worstUptime >= 92.0, worstUptimeRun);
        if (totals.Count > 0)
            Console.WriteLine("   violation totals: " + string.Join(", ", totals.Select(kv => $"{kv.Key}={kv.Value}")));
    }


    private static void SimulateCrucible(bool verbose)
    {
        Console.WriteLine("-- crucible simulator: boards 1-3 (L30/40/50), familiar HP drain, target 100% -> 0% over 180 s, cycling off/on --");
        Loadout[] loadouts =
        [
            new("CuSith/Coblyn/Pugil", [1, 7, 4]),
            new("Wespe/Mantis/Dullahan", [10, 16, 18]),
            new("Mantis/Diremite/Wespe", [16, 8, 10]),
            new("Gigantoad/Opo-opo/Geshunpest", [31, 5, 13]),
        ];

        foreach (var (level, board) in new[] { (30, 1), (40, 2), (50, 3) })
        {
            foreach (var loadout in loadouts)
            {
                foreach (var (drain, cycling) in new[] { (0f, false), (1.5f, false), (3f, false), (0f, true), (1.5f, true) })
                {
                    var sim = new Sim(level, loadout.Slots, BstSettings.Defaults() with { CrucibleCycleForDamage = cycling }, board, drain);
                    sim.Run(180f);
                    var tag = $"Crucible B{board} L{level} {loadout.Name} drain {drain}%/s{(cycling ? " cycling" : "")}";

                    // 3%/s is a stress run: a knocked-out familiar is reported, not failed. The same for a KO
                    // under the horn-conservation keep-out posture (deliberate: cover the character, don't strand it).
                    var violations = sim.Violations.Where(v => !(drain >= 3f && v.Kind == "familiar-ko") && v.Kind != "familiar-ko-keepout").ToList();
                    if (violations.Count > 0)
                    {
                        foreach (var v in violations.GroupBy(v => v.Kind))
                            Check($"{tag}: {v.Key}", false, $"x{v.Count()} first: {v.First().Detail}");
                    }
                    else
                    {
                        _pass++;
                    }

                    if (verbose || board == 1 && (loadout == loadouts[0] || loadout == loadouts[1]))
                        Console.WriteLine($"   {tag,-66} uptime {sim.UptimePercent,5:0.0}%  PB {sim.Count(BST.PartingBlow),2}  saves {sim.PetSaves} (swaps {sim.Swaps})  TR {sim.Count(BST.TemperedRelease),2}  KO {sim.Kos}  horns {sim.Summons,2}  lowest {sim.LowestPetHp:0}%");
                }
            }
        }
    }

    /// <summary>
    ///     Minimal Beastmaster model. Mechanics are the LIVE-PROVEN ones (research/bst-live-evidence.md):
    ///     One with Nature per summon consumed by Tempered Release or Borrow; horn locked 90 s in combat
    ///     after its familiar retreats; familiar TP shared and persistent; wespe Final Sting retreats;
    ///     pet Heart lands 1.0 s after Trick; axes on their own 5 s recast, GCD 2.5 s; weaponskills are
    ///     sent only when the GCD is ready and abilities only when the animation lock is clear (the
    ///     AutoRotationController send gate).
    /// </summary>
    private sealed class Sim
    {
        public readonly record struct Violation(string Kind, string Detail);

        private const float Dt = 0.05f;
        private readonly int _level;
        private readonly int[] _slots;
        private readonly BstSettings _cfg;

        // Crucible: board (0 = off), familiar HP per horn with a constant drain while out, target HP falling to 0.
        private readonly int _board;
        private readonly float _petDrain;
        private readonly float[] _petHp = [100f, 100f, 100f, 100f];
        private readonly bool[] _petSeen = new bool[4];
        private float _fightLength = 300f;
        public int Kos, PetSaves, Swaps;
        public float LowestPetHp = 100f;
        private bool Crucible => _board != 0;
        private float TargetHp => Crucible ? Math.Max(0f, 100f - 100f * (_t - _combatStart) / _fightLength) : 100f;

        public readonly List<Violation> Violations = [];
        private readonly Dictionary<uint, int> _counts = [];

        // clock
        private float _t;
        private float _combatStart = 2f;

        // player
        private float _gcd;          // remaining GCD
        private float _animLock;
        private int _tp;
        private uint _comboAction;
        private float _comboTimer;
        private float _axeCd;
        private float _trickCd, _temperedCd, _borrowCd, _partingCd, _rallyCd, _cheerCd;
        private int _master, _natural;
        private bool _oneWithNature;
        private float _vantageUntil = -1;
        private float _sunMoonUntil = -1;
        private float _waveringUntil = -1;
        private BeastmasterAffinity _sunMoon;
        private int _kinshipSlot;
        private float _kinshipUntil = -1;
        private int _shieldCharges;
        private float _shieldRegen;

        // familiar
        private int _familiarTp;
        private int _activeSlot;
        private float _arrivalAt = -1;    // pending summon arrival
        private int _arrivalSlot;
        private readonly float[] _hornLockedUntil = new float[4];
        private float _nextAuto;
        private float _summonedAt;
        private float _petHeartAt = -1;     // pending pet heart landing
        private readonly float[] _last = new float[8];

        // combo tracking
        private float _lastInstinctualAt = -100;
        private BeastmasterAffinity _lastInstinctualAffinity;
        private bool _lastWasPet;
        private float _lastAxeAt = -100, _lastTrickAt = -100, _lastTemperedAt = -100, _lastBorrowAt = -100, _lastHornAt = -100, _lastPetHeartAt = -100;
        private BeastmasterAffinity _lastAxeAffinity;

        // stats
        public int Combos, IntentionalCombos, AxesUsed, Summons, Universality, BlockedByWavering;
        private float _familiarOutTime, _combatTime, _firstSummonAt = -1;
        private float _lastGcdAt;

        public Sim(int level, int[] slots, BstSettings cfg, int board = 0, float petDrain = 0f)
        {
            _level = level;
            _slots = (int[])slots.Clone();
            _cfg = cfg;
            _board = board;
            _petDrain = petDrain;
            _shieldCharges = level >= 36 ? 3 : level >= 24 ? 1 : 0;
        }

        public int Count(uint id) => _counts.GetValueOrDefault(id);

        private bool InCombat => _t >= _combatStart;

        private int LearnedSlots => LearnedHornSlots(_level);

        public bool HasFamiliarPossible => Enumerable.Range(1, LearnedSlots).Any(i => _slots[i - 1] != 0);

        public double UptimePercent => _firstSummonAt < 0 || _combatTime <= 0
            ? (HasFamiliarPossible ? 0 : 100)
            : 100.0 * _familiarOutTime / Math.Max(0.001, _t - Math.Max(_firstSummonAt + 1.5f, _combatStart));

        private void Fail(string kind, string detail) => Violations.Add(new(kind, $"t={_t:0.00} {detail}"));

        private BeastmasterBeast? ActiveBeastData => _activeSlot == 0 ? null : BST_Beasts.ByRow(_slots[_activeSlot - 1]);

        private bool HornReadyNow(int slot)
        {
            if (slot > LearnedSlots || _slots[slot - 1] == 0)
                return false;
            if (_t < _hornLockedUntil[slot])
                return false;
            return _t - _lastHornAt >= 2f;
        }

        public void Run(float seconds)
        {
            var end = _combatStart + seconds;
            _fightLength = seconds;
            _lastGcdAt = _combatStart;
            while (_t < end)
            {
                Tick();
                _t += Dt;
            }
        }

        private void Tick()
        {
            // timers
            _gcd = Math.Max(0, _gcd - Dt);
            _animLock = Math.Max(0, _animLock - Dt);
            _axeCd = Math.Max(0, _axeCd - Dt);
            _trickCd = Math.Max(0, _trickCd - Dt);
            _temperedCd = Math.Max(0, _temperedCd - Dt);
            _borrowCd = Math.Max(0, _borrowCd - Dt);
            _partingCd = Math.Max(0, _partingCd - Dt);
            _rallyCd = Math.Max(0, _rallyCd - Dt);
            _cheerCd = Math.Max(0, _cheerCd - Dt);
            _comboTimer = Math.Max(0, _comboTimer - Dt);
            if (_level >= 24)
            {
                var max = _level >= 36 ? 3 : 1;
                if (_shieldCharges < max)
                {
                    _shieldRegen += Dt;
                    if (_shieldRegen >= 60f) { _shieldCharges++; _shieldRegen = 0; }
                }
            }

            // summon arrival (1 s cast)
            if (_arrivalAt >= 0 && _t >= _arrivalAt)
            {
                if (_activeSlot != 0 && InCombat)
                    Fail("summoned-over-active-familiar", $"slot{_arrivalSlot} over slot{_activeSlot}");
                _activeSlot = _arrivalSlot;
                _arrivalAt = -1;
                _summonedAt = _t;
                _nextAuto = _t + 1.5f;
                if (_level >= LvTemperedRelease) _oneWithNature = true;
                if (_level >= LvTemperedResetOnSummon) _temperedCd = 0;
                if (_level >= LvBorrowResetOnSummon) _borrowCd = 0;
                if (_kinshipSlot == _activeSlot) _kinshipUntil = -1; // resummoning the Borrow source ends Kinship
                Summons++;
                if (_firstSummonAt < 0) _firstSummonAt = _t;
            }

            // pet heart landing
            if (_petHeartAt >= 0 && _t >= _petHeartAt)
            {
                _petHeartAt = -1;
                if (_activeSlot != 0 && ActiveBeastData is { } b)
                {
                    _lastPetHeartAt = _t;
                    Instinctual(b.TrickAffinity, pet: true);
                }
            }

            // familiar autos
            if (_activeSlot != 0 && InCombat && _t >= _nextAuto)
            {
                if (_level >= LvTrick) _familiarTp = Math.Min(250, _familiarTp + 12);
                _nextAuto = _t + 3f;
            }

            if (Crucible && _activeSlot != 0 && InCombat)
            {
                _petSeen[_activeSlot] = true;
                _petHp[_activeSlot] -= _petDrain * Dt;
                LowestPetHp = Math.Min(LowestPetHp, Math.Max(0f, _petHp[_activeSlot]));
                if (_petHp[_activeSlot] <= 0f)
                {
                    // A KO under the horn-conservation keep-out posture (no other horn could have been resummoned)
                    // is the accepted trade — the familiar stayed out to cover the character instead of being
                    // recalled into a weaponless spiral. Reported, not failed.
                    var keepOut = !Enumerable.Range(1, LearnedSlots).Any(i => i != _activeSlot
                        && HornReadyNow(i) && (_petSeen[i] ? _petHp[i] : 100f) > BST_CrucibleLogic.CriticalHp(_cfg));
                    Kos++;
                    Fail(keepOut ? "familiar-ko-keepout" : "familiar-ko", $"slot{_activeSlot} ({BST_Beasts.ByRow(_slots[_activeSlot - 1])?.Name})");
                    _slots[_activeSlot - 1] = 0;
                    _activeSlot = 0;
                    _oneWithNature = false;
                    _petHeartAt = -1;
                }
            }

            if (InCombat)
            {
                _combatTime += Dt;
                if (_activeSlot != 0 && _firstSummonAt >= 0 && _t >= _firstSummonAt + 1.5f)
                    _familiarOutTime += Dt;
            }

            if (InCombat && _t - _lastGcdAt > 3.2f)
            {
                Fail("stall-no-gcd", $"{_t - _lastGcdAt:0.0} s without a GCD");
                _lastGcdAt = _t;
            }

            var state = BuildState();
            var d = Decide(state, _cfg);

            // A critical familiar must be saved whenever a healthier horn could do it, or Parting Blow could
            // recall it with a resummon actually available (a ready horn with a familiar above the critical
            // line — horn conservation keeps the last familiar out when there is nothing to resummon).
            if (Crucible && _activeSlot != 0 && InCombat && _petHp[_activeSlot] <= BST_CrucibleLogic.CriticalHp(_cfg) && state.CanWeave
                && TargetHp > BST_CrucibleLogic.EnemyDyingHp && _t - _lastHornAt >= 2.5f && _arrivalAt < 0
                && (Enumerable.Range(1, LearnedSlots).Any(i => i != _activeSlot && HornReadyNow(i) && (_petSeen[i] ? _petHp[i] : 100f) >= _petHp[_activeSlot] + BST_CrucibleLogic.SwapMinGain)
                    || (state.ReadyParting && Enumerable.Range(1, LearnedSlots).Any(i => i != _activeSlot && HornReadyNow(i) && (_petSeen[i] ? _petHp[i] : 100f) > BST_CrucibleLogic.CriticalHp(_cfg))))
                && !d.Reason.StartsWith("crucible:petsave"))
                Fail("petsave-missed", $"{d.Reason} familiar {_petHp[_activeSlot]:0}%");

            Execute(d, state);
        }

        private float Since(float at) => at < -50 ? float.MaxValue : _t - at;

        private BstState BuildState()
        {
            // The live half reports the pet object only while out or while a summon is in flight.
            var petObject = _activeSlot != 0 || (_arrivalAt >= 0 && _t >= _arrivalAt - 0.5f);
            return new BstState
            {
                Level = _level,
                InCombat = InCombat,
                HasHostileTarget = true,
                TargetDistance = 3f,
                TargetHpPercent = TargetHp,
                EnemiesWithin6y = 1,
                PlayerIsCasting = _arrivalAt >= 0,
                PlayerTp = _tp,
                FamiliarTp = _familiarTp,
                ActiveSlot = _activeSlot,
                LastAffinity = _t - _lastInstinctualAt < 7f ? _lastInstinctualAffinity : BeastmasterAffinity.None,
                KinshipSlot = _t < _kinshipUntil || (_kinshipUntil == float.MaxValue) ? _kinshipSlot : 0,
                MasterStacks = _master,
                NaturalStacks = _natural,
                Slot1Beast = _slots[0], Slot2Beast = _slots[1], Slot3Beast = _slots[2],
                SlotBeastsKnown = _slots.Any(x => x != 0),
                PetObjectPresent = petObject,
                PetObjectBeast = _activeSlot != 0 ? _slots[_activeSlot - 1] : 0,
                OneWithNature = _oneWithNature,
                LingeringVantage = _t < _vantageUntil,
                WaveringHeart = _t < _waveringUntil,
                ComboState = _t < _waveringUntil ? BeastmasterAffinity.WaveringHeart : BeastmasterAffinity.None,
                SunMoon = _t < _sunMoonUntil ? _sunMoon : BeastmasterAffinity.None,
                SunOrMoonActive = _t < _sunMoonUntil,
                KinshipHeld = _t < _kinshipUntil,
                GcdReady = _gcd <= 0.5f,
                CanWeave = _gcd > 0.6f && _animLock <= 0,
                SinceSummon = _activeSlot != 0 ? _t - _summonedAt : 0,
                SinceHornPress = Since(_lastHornAt),
                SinceTrick = Since(_lastTrickAt),
                SinceTempered = Since(_lastTemperedAt),
                SinceBorrow = Since(_lastBorrowAt),
                SinceAxe = Since(_lastAxeAt),
                SincePetHeart = Since(_lastPetHeartAt),
                LastAxeAffinity = _lastAxeAffinity,
                TemperedRecastRemaining = _temperedCd,
                ReadyHorn1 = HornReadyNow(1),
                ReadyHorn2 = HornReadyNow(2),
                ReadyHorn3 = HornReadyNow(3),
                ReadyTrick = _level >= LvTrick && _activeSlot != 0 && _trickCd <= 0 && _familiarTp >= 100,
                ReadyTempered = _level >= LvTemperedRelease && _activeSlot != 0 && _oneWithNature && _temperedCd <= 0 && InCombat,
                ReadyBorrow = _level >= LvBorrow && _activeSlot != 0 && _oneWithNature && _borrowCd <= 0,
                ReadyParting = _level >= LvPartingBlow && _activeSlot != 0 && _partingCd <= 0 && InCombat,
                ReadyAxe = _level >= LvAvalancheAxe && _axeCd <= 0.5f,
                ReadyRally = _level >= LvRally && _rallyCd <= 0 && InCombat,
                ReadyCheer = _level >= LvRallyingCheer && _cheerCd <= 0 && _activeSlot != 0 && InCombat,
                ReadyShieldCharge = _level >= LvShieldCharge && _shieldCharges > 0,
                ShieldChargeCharges = _shieldCharges,
                ShieldChargeMax = _level >= 36 ? 3 : 1,
                LastComboAction = _comboAction,
                ComboTimerActive = _comboTimer > 0,
                CrucibleBoard = _board,
                CrucibleBattle = Crucible ? 1 : -1,
                EnemyCount = Crucible ? 1 : 0,
                HighestEnemyHpPercent = TargetHp,
                PlayerHpPercent = 100f,
                PetHpPercent = _activeSlot != 0 ? _petHp[_activeSlot] : 100f,
                PetHp = _activeSlot != 0 ? 200f * _petHp[_activeSlot] : 0f,
                SinceSnarl = float.MaxValue,
                Slot1PetHp = _petSeen[1] ? _petHp[1] : 0f,
                Slot2PetHp = _petSeen[2] ? _petHp[2] : 0f,
                Slot3PetHp = _petSeen[3] ? _petHp[3] : 0f,
                PartingBlowRecast = _partingCd,
                EnemyTargetsPlayer = true,
            };
        }

        private static readonly Dictionary<uint, int> ActionLevel = new()
        {
            [BST.SmashAxe] = 1, [BST.AxebladeBite] = 2, [BST.Shieldsplitter] = 12,
            [BST.AvalancheAxe] = 4, [BST.MistralAxe] = 8, [BST.SpinningAxe] = 14, [BST.GaleAxe] = 16,
            [BST.FirstBattlehorn] = 1, [BST.SecondBattlehorn] = 10, [BST.ThirdBattlehorn] = 20,
            [BST.PartingBlow] = 6, [BST.Trick] = 8, [BST.TemperedRelease] = 18, [BST.Borrow] = 22,
            [BST.ShieldCharge] = 24, [BST.Rally] = 28, [BST.RallyingCheer] = 40,
        };

        private static bool IsWeaponskill(uint id) => id is BST.SmashAxe or BST.AxebladeBite or BST.Shieldsplitter
            or BST.AvalancheAxe or BST.MistralAxe or BST.SpinningAxe or BST.GaleAxe;

        private void Execute(BstDecision d, BstState state)
        {
            var id = d.ActionId;
            if (id == 0)
            {
                Fail("zero-action", d.Reason);
                return;
            }

            if (ActionLevel.TryGetValue(id, out var lv) && lv > _level)
                Fail("unlearned-action", $"{id} needs L{lv} ({d.Reason})");

            // Send gate (AutoRotationController): weaponskills when the GCD is ready, abilities when not locked.
            if (IsWeaponskill(id))
            {
                if (_gcd > 0 || _animLock > 0) return;
            }
            else
            {
                if (_animLock > 0 || _arrivalAt >= 0) return;
            }

            switch (id)
            {
                case BST.SmashAxe:
                case BST.AxebladeBite:
                case BST.Shieldsplitter:
                    Gcd(id);
                    return;

                case BST.AvalancheAxe:
                case BST.MistralAxe:
                case BST.SpinningAxe:
                case BST.GaleAxe:
                    Axe(id, d);
                    return;

                case BST.FirstBattlehorn:
                case BST.SecondBattlehorn:
                case BST.ThirdBattlehorn:
                    Horn(id == BST.FirstBattlehorn ? 1 : id == BST.SecondBattlehorn ? 2 : 3, d);
                    return;

                case BST.Trick:
                    if (!state.ReadyTrick) { Fail("invalid-trick", d.Reason); return; }
                    _trickCd = 3f; _familiarTp = 0; _lastTrickAt = _t; _petHeartAt = _t + 1.0f; _animLock = 0.6f;
                    Bump(id);
                    return;

                case BST.TemperedRelease:
                    if (!state.ReadyTempered) { Fail("invalid-tempered", d.Reason); return; }
                    var beast = ActiveBeastData;
                    if (!Crucible && beast is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && _t - _summonedAt < _cfg.MinFamiliarStaySeconds)
                        Fail("wespe-final-sting-before-min-stay", $"{_t - _summonedAt:0.0} s after summon");
                    if (beast is { } bb && (bb.Release & BeastmasterReleaseTraits.Sleep) != 0 && !_cfg.AllowSleepRelease)
                        Fail("sleep-release-used", bb.Name);
                    if (beast is { } kb && (kb.Release & (BeastmasterReleaseTraits.Knockback | BeastmasterReleaseTraits.DrawIn)) != 0
                        && !(_cfg.AllowDisplacingRelease || (Crucible && _cfg.CrucibleAllowDisplacing)))
                        Fail("displacing-release-used", kb.Name);
                    _oneWithNature = false; _temperedCd = 30f; _lastTemperedAt = _t; _animLock = 0.6f;
                    if (_level >= LvVantageFromTempered) _vantageUntil = _t + 60f;
                    Bump(id);
                    if (beast is { } eb && (eb.Release & BeastmasterReleaseTraits.Exit) != 0)
                    {
                        if (d.Reason == "crucible:petsave-finalsting") PetSaves++;
                        Retreat("finalsting", allowPetless: Crucible);
                    }
                    return;

                case BST.Borrow:
                    if (!state.ReadyBorrow) { Fail("invalid-borrow", d.Reason); return; }
                    _oneWithNature = false; _borrowCd = 30f; _lastBorrowAt = _t; _animLock = 0.6f;
                    _kinshipSlot = _activeSlot; _kinshipUntil = float.MaxValue;
                    if (_level >= LvVantageFromBorrow) _vantageUntil = _t + 60f;
                    Bump(id);
                    return;

                case BST.PartingBlow:
                    if (!state.ReadyParting) { Fail("invalid-partingblow", d.Reason); return; }
                    var petSave = d.Reason.StartsWith("crucible:petsave");
                    if (petSave) PetSaves++;
                    if (!petSave && _oneWithNature && _level >= LvTemperedRelease && PlanRelease(ActiveBeastData, _cfg) == ReleasePlan.Use)
                        Fail("retreat-with-unspent-one-with-nature", ActiveBeastData?.Name ?? "?");
                    if (Crucible && !petSave && TargetHp <= BST_CrucibleLogic.RoundEndingHp)
                        Fail("partingblow-round-ending", $"target {TargetHp:0}% ({d.Reason})");
                    if (petSave && TargetHp <= BST_CrucibleLogic.EnemyDyingHp)
                        Fail("petsave-on-dying-enemy", $"target {TargetHp:0.0}%");
                    _partingCd = 10f; _animLock = 0.6f; _vantageUntil = -1;
                    if (_level >= LvTrick) _familiarTp = Math.Min(250, _familiarTp + 24);
                    Bump(id);
                    Retreat("partingblow", allowPetless: petSave);
                    return;

                case BST.Rally:
                    if (!state.ReadyRally) { Fail("invalid-rally", d.Reason); return; }
                    _tp = Math.Min(250, _tp + 40 + 70 * _master); _master = 0; _rallyCd = _level >= 42 ? 90f : 120f; _animLock = 0.6f;
                    Bump(id);
                    return;

                case BST.RallyingCheer:
                    if (!state.ReadyCheer) { Fail("invalid-cheer", d.Reason); return; }
                    _familiarTp = Math.Min(250, _familiarTp + 30 + 70 * _natural); _natural = 0; _cheerCd = _level >= 48 ? 90f : 120f; _animLock = 0.6f;
                    Bump(id);
                    return;

                case BST.ShieldCharge:
                    if (_shieldCharges <= 0) { Fail("invalid-shieldcharge", d.Reason); return; }
                    _shieldCharges--; _animLock = 0.6f;
                    Bump(id);
                    return;

                default:
                    Fail("unexpected-action", $"{id} {d.Reason}");
                    return;
            }
        }

        private void Bump(uint id) => _counts[id] = _counts.GetValueOrDefault(id) + 1;

        private void Gcd(uint id)
        {
            _gcd = 2.5f; _animLock = 0.6f; _lastGcdAt = _t;
            if (id == BST.AxebladeBite && _comboAction == BST.SmashAxe && _comboTimer > 0 && _level >= 4) _tp = Math.Min(250, _tp + 13);
            if (id == BST.Shieldsplitter && _comboAction == BST.AxebladeBite && _comboTimer > 0 && _level >= 4) _tp = Math.Min(250, _tp + 15);
            _comboAction = id; _comboTimer = 30f;
            Bump(id);
        }

        private void Axe(uint id, BstDecision d)
        {
            if (_tp < 100) { Fail("invalid-axe-tp", $"{_tp} {d.Reason}"); return; }
            if (_axeCd > 0) { Fail("invalid-axe-recast", d.Reason); return; }

            var finisher = _level >= LvFinishers && _tp >= 250;
            _tp = 0; _axeCd = 5f; _animLock = 0.6f; AxesUsed++;
            Bump(id);

            if (finisher)
            {
                // Brutal Rage / Risen Fall are Sunstrider, Hawkish Talons / Calamity Moonstalker.
                var type = id is BST.AvalancheAxe or BST.SpinningAxe ? BeastmasterAffinity.Sunstrider : BeastmasterAffinity.Moonstalker;
                if (_t < _sunMoonUntil && _sunMoon != type)
                {
                    Universality++;
                    _sunMoonUntil = -1;
                }
                _lastAxeAt = _t; _lastAxeAffinity = BeastmasterAffinity.None;
                return;
            }

            var affinity = id switch
            {
                BST.AvalancheAxe => BeastmasterAffinity.Rampant,
                BST.MistralAxe => BeastmasterAffinity.Durant,
                BST.SpinningAxe => BeastmasterAffinity.Eldritch,
                _ => BeastmasterAffinity.Volant,
            };
            _lastAxeAt = _t; _lastAxeAffinity = affinity;
            Instinctual(affinity, pet: false);
        }

        private void Instinctual(BeastmasterAffinity affinity, bool pet)
        {
            // Wavering Heart (7 s after every combo, live 20/20): no combo involving the familiar.
            var familiarInvolved = pet || _lastWasPet;
            if (_t - _lastInstinctualAt < 7f && IsCompass(_lastInstinctualAffinity) && !(familiarInvolved && _t < _waveringUntil))
            {
                Combos++;
                if (Clockwise(_lastInstinctualAffinity) == affinity)
                {
                    IntentionalCombos++;
                    _sunMoon = _lastInstinctualAffinity is BeastmasterAffinity.Volant or BeastmasterAffinity.Durant
                        ? BeastmasterAffinity.Sunstrider : BeastmasterAffinity.Moonstalker;
                    _sunMoonUntil = _t + 7f;
                }
                if (!pet && _lastWasPet && _level >= 28) _master = Math.Min(3, _master + 1);
                if (pet && !_lastWasPet && _level >= 40) _natural = Math.Min(3, _natural + 1);
                _waveringUntil = _t + 7f;
            }
            else if (familiarInvolved && _t < _waveringUntil && _t - _lastInstinctualAt < 7f)
            {
                BlockedByWavering++;
            }
            _lastInstinctualAt = _t;
            _lastInstinctualAffinity = affinity;
            _lastWasPet = pet;
        }

        private void Horn(int slot, BstDecision d)
        {
            if (slot > LearnedSlots) { Fail("unlearned-horn", $"slot{slot}"); return; }
            if (_slots[slot - 1] == 0) { Fail("empty-horn", $"slot{slot}"); return; }
            if (_t < _hornLockedUntil[slot]) { Fail("horn-locked", $"slot{slot} {_hornLockedUntil[slot] - _t:0} s left ({d.Reason})"); return; }
            if (_t - _lastHornAt < 2f) { Fail("horn-spam", $"slot{slot}"); return; }

            var petSaveSwap = d.Reason.StartsWith("crucible:petsave-swap") || d.Reason == "crucible:finalsting-swap";
            if (Crucible && InCombat && !petSaveSwap)
            {
                var hp = _petSeen[slot] ? _petHp[slot] : 100f;
                if (hp <= BST_CrucibleLogic.CriticalHp(_cfg) && _partingCd > 2f)
                    Fail("low-familiar-summoned-while-partingblow-recasts", $"slot{slot} {hp:0}%");
                var healthier = Enumerable.Range(1, LearnedSlots).Any(i => i != slot && HornReadyNow(i) && (_petSeen[i] ? _petHp[i] : 100f) > _cfg.CruciblePetSwapHp);
                if (hp <= _cfg.CruciblePetSwapHp && healthier)
                    Fail("low-familiar-over-healthy", $"slot{slot} {hp:0}%");
            }
            if (Crucible && !InCombat && !_cfg.CruciblePrepullHorns)
                Fail("horn-out-of-combat-in-crucible", d.Reason);

            if (_activeSlot != 0)
            {
                if (InCombat && petSaveSwap)
                {
                    // A horn blown over the familiar: it retreats with its HP kept; its horn locks for 90 s.
                    Swaps++;
                    if (d.Reason != "crucible:finalsting-swap")
                        PetSaves++;
                    _hornLockedUntil[_activeSlot] = _t + 90f;
                    _activeSlot = 0;
                    _oneWithNature = false;
                    _petHeartAt = -1;
                }
                else if (InCombat)
                    Fail("in-combat-swap", $"slot{_activeSlot} -> slot{slot} ({d.Reason})");
                else
                    _activeSlot = 0; // out of combat swap: no lockout
            }

            _lastHornAt = _t; _arrivalAt = _t + 1.0f; _arrivalSlot = slot; _animLock = 1.0f;
            Bump(HornAction(slot));
        }

        private void Retreat(string how, bool allowPetless = false)
        {
            if (_activeSlot == 0) return;

            var otherReady = Enumerable.Range(1, LearnedSlots).Any(i => i != _activeSlot && HornReadyNow(i));
            if (!otherReady && !_cfg.AllowPetlessCycling && InCombat && !allowPetless)
                Fail("retreat-left-no-familiar", $"{how} from slot{_activeSlot} with no other horn ready");

            if (InCombat)
                _hornLockedUntil[_activeSlot] = _t + 90f;
            _activeSlot = 0;
            _oneWithNature = false;
            _petHeartAt = -1;
        }
    }


    /// <summary>
    ///     First / Second Master's Board fight knowledge, measured from the recorded runs of 2026-09-26 .. 09-30
    ///     (analysis: holder / hit-timing / horn-ledger scripts over the CR| telemetry and the network logs). Each case
    ///     quotes the logged situation it comes from.
    /// </summary>
    private static void MasterBoardFights()
    {
        Console.WriteLine("-- master board fights (measured) --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        var spCfg = on with { CrucibleSnarlParting = true, CrucibleTankbusterParting = true }; // the dormant Parting Blow step, opted in
        var cycle = cfg with { CrucibleCycleForDamage = true };
        bool IsHorn(uint id) => id is BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn;

        // --- Tankbusters: who follows the enmity holder, and when the hit lands in the plugin's clock ---
        // Sweeping Evisceration (Gargoyle Piece, First Master's battle 5): cast 48717, 7.6 s, hit 50933 lands 1.3 s after the
        // castbar; 18 of 18 logged hits hit the enmity holder (character 14 / familiar 4: 1,225 / 2,021).
        Check("tankbuster: Sweeping Evisceration 48717 (hits the enmity holder 18 of 18)", BST_CrucibleData.Tankbusters.Contains(48717));
        Check("tankbuster: Obliterate 50649 (Golem Piece, 2 of 2 on the holder, 1,349 / 1,112)", BST_CrucibleData.Tankbusters.Contains(50649));
        Check("tankbuster: On the Properties of Darkness 48669 (Strix Piece, 9 of 9 on the holder, up to 1,265)", BST_CrucibleData.Tankbusters.Contains(48669));
        // Grim Fate 48730: the follow-up 48731 is a FIVE-hit string of 121-212 each, 645-1,030 per cast, on the holder (7 of 7).
        Check("tankbuster kept: Grim Fate 48730 is a five-hit string, 645-1,030 per cast", BST_CrucibleData.Tankbusters.Contains(48730));
        Check("hit delay 48717 Sweeping Evisceration: 1.3 s after the castbar", BST_CrucibleData.TankbusterHitDelay(48717) == 1.3f, BST_CrucibleData.TankbusterHitDelay(48717).ToString());
        Check("hit delay 48730 Grim Fate: 1.3 s after the castbar", BST_CrucibleData.TankbusterHitDelay(48730) == 1.3f);
        Check("hit delay 49188 Erratic Blaster: 0.3 s after the castbar (was 1.0, guide)", BST_CrucibleData.TankbusterHitDelay(49188) == 0.3f, BST_CrucibleData.TankbusterHitDelay(49188).ToString());
        Check("hit delay 48822 / 48689 / 50649 / 48669: 0.3 s after the castbar",
            new uint[] { 48822, 48689, 50649, 48669 }.All(id => BST_CrucibleData.TankbusterHitDelay(id) == 0.3f));
        Check("hit delay 49254 Mangling Fang: 0.4 s", BST_CrucibleData.TankbusterHitDelay(49254) == 0.4f);
        // Toxic Vomit 48809 (Borgny): the plugin shows 4.7 s remaining at cast start, the log says a 3.2 s cast and the hit lands 3.5 s
        // after the start, i.e. 1.2 s BEFORE the plugin's remaining reaches 0 (CR|...|c=48809:4.7 at 13.69, hit at 17.11; 15 of 15 casts).
        Check("hit delay 48809 Toxic Vomit: lands 1.2 s before the plugin's remaining reaches 0 (was +1.5, guide)", BST_CrucibleData.TankbusterHitDelay(48809) == -1.2f, BST_CrucibleData.TankbusterHitDelay(48809).ToString());

        // Snarl -> Parting Blow with that timing (lead 1.5 s): Toxic Vomit with 2.5 s remaining lands in 1.3 s.
        var landing = CrucibleState() with { TargetCastId = 48809, TargetCastRemaining = 2.5f, SinceSnarl = 3f, EnemyTargetsPet = true, EnemyTargetsPlayer = false };
        Check("Toxic Vomit, 2.5 s remaining (hit lands in 1.3 s), Snarl up: Parting Blow", Decide(landing, spCfg) is { ActionId: BST.PartingBlow, Reason: "crucible:snarl-parting" },
            Decide(landing, spCfg).Reason);
        Check("Toxic Vomit, 1.0 s remaining: the hit already landed 0.2 s ago, no Parting Blow", Decide(landing with { TargetCastRemaining = 1.0f }, spCfg).Reason != "crucible:snarl-parting");
        Check("Toxic Vomit, 4.0 s remaining: too early", Decide(landing with { TargetCastRemaining = 4.0f }, spCfg).Reason != "crucible:snarl-parting");
        var sweeping = landing with { TargetCastId = 48717, TargetCastRemaining = 0.4f };
        Check("Sweeping Evisceration, 0.4 s remaining (hit lands in 1.7 s): not yet", Decide(sweeping, spCfg).Reason != "crucible:snarl-parting");
        Check("Sweeping Evisceration, 0.1 s remaining (hit lands in 1.4 s): Parting Blow", Decide(sweeping with { TargetCastRemaining = 0.1f }, spCfg).Reason == "crucible:snarl-parting");

        // --- the measured table and what it may claim ---
        var boards123 = new HashSet<uint> { 46935, 46934, 46872, 46906, 46920, 48138, 48204, 48247, 48620, 48471, 48489, 50465, 48563 };
        Check("every Tankbuster is a Board 1-3 id, a measured Tankbuster row, or marked guide-only (nothing unmeasured passes as measured)",
            BST_CrucibleData.Tankbusters.All(id => boards123.Contains(id) || BST_CrucibleData.GuideBoundTankbusters.Contains(id)
                || BST_CrucibleData.HeavyCast(id) is { Kind: CrucibleHitKind.Tankbuster }),
            string.Join(",", BST_CrucibleData.Tankbusters.Where(id => !boards123.Contains(id) && !BST_CrucibleData.GuideBoundTankbusters.Contains(id) && BST_CrucibleData.HeavyCast(id) is not { Kind: CrucibleHitKind.Tankbuster })));
        Check("the two guide-only Second Master's ids are in Tankbusters and have no measured row",
            BST_CrucibleData.GuideBoundTankbusters.SetEquals(new uint[] { 49205, 49470 })
            && BST_CrucibleData.GuideBoundTankbusters.All(id => BST_CrucibleData.Tankbusters.Contains(id) && BST_CrucibleData.HeavyCast(id) is null));
        Check("every measured Tankbuster row is in the Tankbusters set; no area / party-wide / cast-only row is",
            BST_CrucibleData.HeavyCasts.All(h => (h.Kind == CrucibleHitKind.Tankbuster) == BST_CrucibleData.Tankbusters.Contains(h.CastId)));
        Check("every measured row belongs to a battle the recorded runs fought", BST_CrucibleData.HeavyCasts.All(h => BST_CrucibleData.IsMeasuredBattle(h.Board, h.Battle)),
            string.Join(",", BST_CrucibleData.HeavyCasts.Where(h => !BST_CrucibleData.IsMeasuredBattle(h.Board, h.Battle)).Select(h => h.Name)));
        Check("measured battles: 6 of 10 First Master's and 4 of 14 Second Master's; Durga yes, the rest of the second board no",
            BST_CrucibleData.MeasuredBattles.Count(b => b.Board == 4) == 6 && BST_CrucibleData.MeasuredBattles.Count(b => b.Board == 5) == 4
            && BST_CrucibleData.IsMeasuredBattle(5, 6) && !BST_CrucibleData.IsMeasuredBattle(5, 2) && !BST_CrucibleData.IsMeasuredBattle(4, 2));
        Check("every measured row has cast ids unique, positive castbar, and evidence text", BST_CrucibleData.HeavyCasts.Select(h => h.CastId).Distinct().Count() == BST_CrucibleData.HeavyCasts.Length
            && BST_CrucibleData.HeavyCasts.All(h => h.CastSeconds > 0f && h.Evidence.Length > 20 && h.Casts >= h.Hits));
        Check("Atomic Ray 49272 (12.7 s, 4,782 on the character while the familiar held enmity) is the one guide warning, and never a Snarl rule",
            BST_CrucibleData.WarnCasts().Select(h => h.CastId).SequenceEqual(new uint[] { 49272 })
            && BST_CrucibleData.HeavyCast(49272) is { Kind: CrucibleHitKind.CastOnly, CastSeconds: 12.7f, MaxOnCharacter: 4782 }
            && !BST_CrucibleData.Tankbusters.Contains(49272));
        Check("Sea of Pitch, Rippling Evisceration, Touchdown, Grounding Jolt, Rotten Stench are recorded but not Tankbusters",
            new uint[] { 48729, 48721, 48815, 49266, 48690 }.All(id => BST_CrucibleData.HeavyCast(id) is { Kind: not CrucibleHitKind.Tankbuster } && !BST_CrucibleData.Tankbusters.Contains(id)));

        // --- First Master's battle 3: a discretionary cycle exit must not spend the horns a real familiar save needs ---
        // 2026-09-30 14:35:48 ET, Corpse Flower + Queen Hawk, character 31%:
        //   CR|...|b=4|bt=3|...|hp=31|pet=100|sl=96.100.42|dec=44891:exit:partingblow   (horn 2's familiar out, 100%; horn 1 recasting)
        //   the only ready horn was horn 3, its familiar at 42%: it came out and fell 42% -> 0 in 34 s with no horn left to swap.
        // In 8 of 8 recorded familiar deaths in this fight a cycle exit + resummon had used the horns 4-37 s before the swap line.
        var cycling = CrucibleState() with
        {
            ActiveSlot = 2, PetObjectBeast = 34, Slot1Beast = 1, Slot2Beast = 34, Slot3Beast = 26,
            ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = true, Slot1PetHp = 96f, Slot2PetHp = 100f, Slot3PetHp = 42f,
            PetHpPercent = 100f, SinceSummon = 12f, SinceHornPress = 13f, TargetHpPercent = 79f, HighestEnemyHpPercent = 79f,
            PlayerHpPercent = 31f, ReadyParting = true, CrucibleBattle = 3,
        };
        var wounded = Decide(cycling, cycle);
        Check("09-30 14:35:48: cycle exit into a 42% familiar, no other ready horn: no Parting Blow",
            wounded.ActionId != BST.PartingBlow && wounded.Declines.Contains("crucible:exit-keep-reserve-horn"), $"{wounded.Reason} [{wounded.Declines}]");
        // 2026-09-29 21:36:56: sl=71.90.73, familiar 73% out of slot 3, exit; one ready horn (71%) and nothing behind it.
        var oneHealthy = cycling with { ActiveSlot = 3, PetObjectBeast = 26, ReadyHorn1 = true, ReadyHorn2 = false, ReadyHorn3 = false, Slot1PetHp = 71f, Slot2PetHp = 90f, Slot3PetHp = 73f, PetHpPercent = 73f, PlayerHpPercent = 30f };
        var single = Decide(oneHealthy, cycle);
        Check("09-29 21:36:56: one ready healthy horn and no reserve behind it: no Parting Blow",
            single.ActionId != BST.PartingBlow && single.Declines.Contains("crucible:exit-keep-reserve-horn"), $"{single.Reason} [{single.Declines}]");
        Check("two ready horns, one healthy and one at 42%: the reserve would be the wounded one, no exit",
            Decide(cycling with { ReadyHorn1 = true, Slot1PetHp = 96f }, cycle).ActionId != BST.PartingBlow);
        Check("two ready healthy horns (one to summon, one in reserve): the exit still happens",
            Decide(cycling with { ReadyHorn1 = true, Slot1PetHp = 96f, Slot3PetHp = 90f }, cycle).ActionId == BST.PartingBlow);
        Check("the reserve rule never touches an emergency save: familiar 20%, one ready healthy horn, critical swap still fires",
            Decide(cycling with { PetHpPercent = 20f, ReadyHorn1 = true, Slot1PetHp = 96f, SinceSummon = 2f }, cycle) is { ActionId: BST.FirstBattlehorn, Reason: "crucible:petsave-swap-critical" });
        Check("cycling off (default): no exit, unchanged", Decide(cycling with { ReadyHorn1 = true, Slot1PetHp = 96f, Slot3PetHp = 90f }, cfg).Declines.Contains("crucible:exit-hold"));
    }

    /// <summary>
    ///     Add-pack AoE, window holds and the Ymir stun the fight guide names (task tasks-20261002-crucible-add-pack-and-window-tactics-01).
    ///     Self-destruct burst is on by default (2 of 2 recorded runs did not finish the Golem before the 20 s cast); the pack / shell hold and the
    ///     Parting Blow on the pack are the <see cref="BstSettings.CruciblePackWindow"/> option, off until a run grades them (the open window is logged in <c>sh=</c>).
    /// </summary>
    private static void CrucibleTactics()
    {
        Console.WriteLine("-- crucible tactics: pack AoE, windows, Ymir --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CruciblePackWindow = true };
        // L30, board / battle given, the active familiar `beast` with its One with Nature unspent, two healthy ready horns behind it.
        BstState Tactic(int board, int battle, int beast) => CrucibleState() with
        {
            CrucibleBoard = board, CrucibleBattle = battle, ActiveSlot = 1, Slot1Beast = beast, PetObjectBeast = beast, Slot2Beast = 34, Slot3Beast = 26,
            OneWithNature = true, ReadyTempered = true, SinceSummon = 5f, SinceHornPress = 6f, ReadyHorn2 = true, ReadyHorn3 = true,
            TargetHpPercent = 90f, HighestEnemyHpPercent = 90f, EnemyCount = 1,
        };

        Check("option defaults off", !cfg.CruciblePackWindow);
        Check("pack battles are the twelve fights whose guide answer is an AoE on an add pack (1.0 1.4 1.5 2.2 2.4 3.1 3.6 4.0 4.8 5.4 5.5 5.9)",
            BST_CrucibleData.PackBattles.Count == 12 && new[] { (1, 0), (1, 4), (1, 5), (2, 2), (2, 4), (3, 1), (3, 6), (4, 0), (4, 8), (5, 4), (5, 5), (5, 9) }.All(BST_CrucibleData.PackBattles.Contains));
        Check("a release binds when its trait says crowd control (ziz, buffalo, coeurl, chimera) or it is the slime (Bind+ on the Ymir 6 times); Cu Sith and raptor do not",
            new[] { 21, 26, 33, 38, 17 }.All(r => BST_CrucibleData.ReleaseBinds(BST_Beasts.All[r])) && !BST_CrucibleData.ReleaseBinds(BST_Beasts.All[1]) && !BST_CrucibleData.ReleaseBinds(BST_Beasts.All[34]));

        // ---- 4.7 Self-destruct: a 20 s deadline the normal rotation missed in 2 of 2 recorded runs ----
        var sd = Tactic(4, 7, 34) with { TargetNameId = 14617, TargetCastId = 48766, TargetCastRemaining = 19.5f, TargetHpPercent = 87f, HighestEnemyHpPercent = 87f };
        var burst = Decide(sd, cfg);
        Check("Self-destruct begins (48766, 19.5 s left), familiar out, One with Nature unspent: Tempered Release now",
            burst is { ActionId: BST.TemperedRelease, Reason: "crucible:burst-tempered" }, $"{burst.Reason} [{burst.Declines}]");
        var spent = sd with { OneWithNature = false, ReadyTempered = false, TemperedRecastRemaining = 50f, SinceTempered = 3f, TargetCastRemaining = 16f, SinceSummon = 14f };
        var recall = Decide(spent, cfg);
        Check("Self-destruct, release spent, Parting Blow ready, healthy horns ready: Parting Blow to bring a fresh familiar and its release",
            recall is { ActionId: BST.PartingBlow, Reason: "crucible:burst-parting" }, $"{recall.Reason} [{recall.Declines}]");
        Check("Self-destruct: no Parting Blow when the Golem dies before the cast ends anyway (3 s to death, 16 s left)", Decide(spent with { TargetTimeToDeath = 3f }, cfg).ActionId != BST.PartingBlow);
        Check("Self-destruct: no Parting Blow with 5 s of the cast left (the resummon would not land)", Decide(spent with { TargetCastRemaining = 5f }, cfg).ActionId != BST.PartingBlow);
        Check("Self-destruct: no Parting Blow with no ready horn behind it (it would strand the character)", Decide(spent with { ReadyHorn2 = false, ReadyHorn3 = false }, cfg).ActionId != BST.PartingBlow);
        Check("Self-destruct: no Parting Blow while the release is still resolving (1 s ago)", Decide(spent with { SinceTempered = 1f }, cfg).ActionId != BST.PartingBlow);
        Check("another cast of the Golem (Outcrop 48767) is not a burst", Decide(sd with { TargetCastId = 48767 }, cfg).Reason != "crucible:burst-tempered");

        // ---- 3.1 and the other add packs: hold the release for the pack (option), Parting Blow it once it is out ----
        var pk = Tactic(3, 1, 34) with { TargetNameId = 14567 };
        var held = Decide(pk, on);
        Check("pack window on, no pack yet, raptor (AoE release) with One with Nature unspent: Tempered Release is held for the pack",
            held.ActionId != BST.TemperedRelease && held.Declines.Contains("crucible:hold-release-window"), $"{held.Reason} [{held.Declines}]");
        Check("pack window on, 4 enemies up (the Cavalier's Bone Bishops): the held Tempered Release goes on the pack",
            Decide(pk with { EnemyCount = 4 }, on) is { ActionId: BST.TemperedRelease, Reason: "crucible:pack-release" });
        Check("pack window off (default): the release is spent as before and the window that would have held it is logged for grading",
            Decide(pk, cfg) is { ActionId: BST.TemperedRelease, Reason: "own:tempered", Shadow: "crucible:pack-hold-off" });
        Check("pack window: the hold ends 30 s after the summon (the release is the summon's one use of One with Nature)",
            Decide(pk with { SinceSummon = 31f }, on) is { ActionId: BST.TemperedRelease, Reason: "own:tempered" });
        Check("pack window: a familiar under the swap line is not held for a pack", !Decide(pk with { PetHpPercent = 40f }, on).Declines.Contains("crucible:hold-release-window"));
        Check("pack window: a familiar whose release is not AoE (Cu Sith) is not held", !Decide(Tactic(3, 1, 1) with { TargetNameId = 14567 }, on).Declines.Contains("crucible:hold-release-window"));
        Check("pack window: a battle with no pack in the guide (1.1) is not held", !Decide(Tactic(1, 1, 34), on).Declines.Contains("crucible:hold-release-window"));
        Check("pack window: next to a protected Morpho (5.5) nothing AoE goes off", Decide(Tactic(5, 5, 34) with { EnemyCount = 4, ProtectedNearTarget = true }, on).ActionId != BST.TemperedRelease);

        var out4 = pk with { EnemyCount = 4, OneWithNature = false, ReadyTempered = false, TemperedRecastRemaining = 50f, SinceTempered = 3f, SinceSummon = 14f };
        var pb = Decide(out4, on);
        Check("pack window on, the release has gone off on a pack of 4, a reserve of two healthy ready horns: Parting Blow the pack",
            pb is { ActionId: BST.PartingBlow, Reason: "crucible:pack-parting" }, $"{pb.Reason} [{pb.Declines}]");
        Check("pack window off (default): the open Parting Blow window is logged and nothing is pressed",
            Decide(out4, cfg) is { Shadow: "crucible:pack-parting-off" } off && off.ActionId != BST.PartingBlow);
        Check("pack Parting Blow keeps a reserve: one ready horn behind it is not enough", Decide(out4 with { ReadyHorn3 = false }, on).ActionId != BST.PartingBlow);
        Check("pack Parting Blow respects the minimum stay (5 s after the summon)", Decide(out4 with { SinceSummon = 5f }, on).ActionId != BST.PartingBlow);
        Check("pack Parting Blow needs the pack: with 1 enemy up nothing is pressed", Decide(out4 with { EnemyCount = 1 }, on).ActionId != BST.PartingBlow);
        Check("pack Parting Blow never ends a round (highest enemy at 8%)", Decide(out4 with { HighestEnemyHpPercent = 8f, TargetHpPercent = 8f }, on).ActionId != BST.PartingBlow);

        // ---- 3.2 Ymir: a stun or bind once the shell breaks ----
        var ym = Tactic(3, 2, 21) with { TargetNameId = 14569, EnemyCount = 2 };
        var shelled = Decide(ym, on);
        Check("Ymir window on, shell still up, ziz (stunning AoE release): Tempered Release is held for the shell break",
            shelled.ActionId != BST.TemperedRelease && shelled.Declines.Contains("crucible:hold-release-window"), $"{shelled.Reason} [{shelled.Declines}]");
        Check("Ymir window on, the shell just broke, Ymir targeted: the stun goes on it", Decide(ym with { ShellJustBroke = true }, on) is { ActionId: BST.TemperedRelease, Reason: "crucible:ymir-bind" });
        Check("Ymir window on, the shell just broke but another enemy is targeted: no stun on the wrong target", Decide(ym with { ShellJustBroke = true, TargetNameId = 14570 }, on).Reason != "crucible:ymir-bind");
        Check("Ymir window: a familiar whose release does not bind (Cu Sith) is not held", !Decide(Tactic(3, 2, 1) with { TargetNameId = 14569 }, on).Declines.Contains("crucible:hold-release-window"));
        Check("Ymir window off (default): the release is spent as before and the held shell window is logged", Decide(ym, cfg) is { ActionId: BST.TemperedRelease, Reason: "own:tempered", Shadow: "crucible:pack-hold-off" });
    }

    private static void ShieldChargeOvercap()
    {
        Console.WriteLine("-- Shield Charge overcap protection (option, only with the dash off) --");
        var on = BstSettings.Defaults();
        var dashOff = on with { UseShieldCharge = false };
        var protect = dashOff with { ShieldChargeOvercap = true };
        var s = BaseState(40);
        s.ReadyShieldCharge = true; s.ShieldChargeCharges = 3; s.ShieldChargeMax = 3;
        bool Dashes(BstDecision d) => d.ActionId == BST.ShieldCharge;

        Check("option defaults off", !on.ShieldChargeOvercap);
        Check("dash on, option ignored: 12 y gap-close unchanged", Decide(s with { TargetDistance = 12f }, on with { ShieldChargeOvercap = true }) is { Reason: "shieldcharge:gapclose" } d0 && Dashes(d0));
        Check("dash on, max charges, melee, moving -> held (option has no effect while the dash is on)", !Dashes(Decide(s with { IsMoving = true }, on with { ShieldChargeOvercap = true })));
        Check("dash on, max charges, melee, standing -> spends (unchanged)", Dashes(Decide(s, on)));

        Check("dash off, option off, max charges -> never dashes", !Dashes(Decide(s, dashOff)) && !Dashes(Decide(s with { IsMoving = true }, dashOff)) && !Dashes(Decide(s with { TargetDistance = 12f }, dashOff)));
        var m = Decide(s with { IsMoving = true }, protect);
        Check("dash off, option on, max charges, melee, moving -> spends one", Dashes(m) && m.Reason == "shieldcharge:overcap", $"{m.ActionId}:{m.Reason} [{m.Declines}]");
        Check("dash off, option on, max charges, standing -> spends one", Dashes(Decide(s, protect)));
        Check("dash off, option on, max charges, 12 y -> spends one (overcap, not gap-close)", Decide(s with { TargetDistance = 12f }, protect) is { Reason: "shieldcharge:overcap" } d1 && Dashes(d1));
        Check("dash off, option on, max-1 charges -> held (never closes gaps, never the last charges)", !Dashes(Decide(s with { ShieldChargeCharges = 2, IsMoving = true }, protect)) && !Dashes(Decide(s with { ShieldChargeCharges = 2, TargetDistance = 12f }, protect)));
        Check("dash off, option on, max charges, no weave window -> no dash", !Dashes(Decide(s with { CanWeave = false }, protect)));
        var oor = Decide(s with { TargetDistance = 25f }, protect);
        Check("dash off, option on, 25 y out of range -> no dash, reason recorded", !Dashes(oor) && oor.Declines.Contains("shieldcharge:overcap-out-of-range"), $"{oor.ActionId}:{oor.Reason} [{oor.Declines}]");
        Check("dash off, option on, no target -> no dash", !Dashes(Decide(s with { HasHostileTarget = false }, protect)));

        var cr = s with { CrucibleBoard = 1, IsMoving = true, ProtectedNearTarget = true };
        var prot = Decide(cr, protect);
        Check("Crucible, protected enemy near target -> declines with reason", !Dashes(prot) && prot.Declines.Contains("crucible:shieldcharge-protected-near"), $"{prot.ActionId}:{prot.Reason} [{prot.Declines}]");
        Check("Crucible, nothing protected, moving -> spends one", Dashes(Decide(cr with { ProtectedNearTarget = false }, protect)));

        var l30 = BaseState(30);
        l30.ReadyShieldCharge = true; l30.ShieldChargeCharges = 1; l30.ShieldChargeMax = 1;
        Check("L30 (max 1), option on, moving -> spends the single charge", Dashes(Decide(l30 with { IsMoving = true }, protect)));
        Check("L23 (before the skill) -> no dash", !Dashes(Decide(BaseState(23) with { ReadyShieldCharge = true, ShieldChargeCharges = 1, ShieldChargeMax = 1 }, protect)));
    }

    // Joey, 2026-10-03T16:26:26Z, verbatim: "Use a dash movement ability to dash into an enemy that was out of
    // bounds and in a danger puddle." The gap-close and overcap dashes had three gates (20 y range, Crucible
    // protected-near, weave) and nothing that asked where the character would land.
    private static void DashLandingSafety()
    {
        Console.WriteLine("-- Shield Charge landing safety (every auto-fired dash) --");
        var on = BstSettings.Defaults();
        var protect = on with { UseShieldCharge = false, ShieldChargeOvercap = true };
        var s = BaseState(40);
        s.ReadyShieldCharge = true; s.ShieldChargeCharges = 3; s.ShieldChargeMax = 3;
        bool Dashes(BstDecision d) => d.ActionId == BST.ShieldCharge;

        // control: a clean landing dashes exactly as before
        Check("clean landing, 12 y gap-close: dashes", Decide(s with { TargetDistance = 12f, DashLanding = DashLanding.Safe }, on) is { Reason: "shieldcharge:gapclose" } c0 && Dashes(c0));

        // the puddle
        var puddle = Decide(s with { TargetDistance = 12f, DashLanding = DashLanding.Danger }, on);
        Check("enemy standing in a danger puddle, 12 y gap-close: no dash, reason recorded", !Dashes(puddle) && puddle.Declines.Contains("shieldcharge:landing-danger"), $"{puddle.ActionId}:{puddle.Reason} [{puddle.Declines}]");
        var puddleMax = Decide(s with { DashLanding = DashLanding.Danger }, on);
        Check("enemy in a puddle, melee range, max charges: the spend-at-cap dash is held too", !Dashes(puddleMax) && puddleMax.Declines.Contains("shieldcharge:landing-danger"), $"{puddleMax.ActionId}:{puddleMax.Reason} [{puddleMax.Declines}]");

        // out of bounds
        var oob = Decide(s with { TargetDistance = 15f, DashLanding = DashLanding.Unreachable }, on);
        Check("enemy outside the arena, 15 y gap-close: no dash, reason recorded", !Dashes(oob) && oob.Declines.Contains("shieldcharge:landing-unreachable"), $"{oob.ActionId}:{oob.Reason} [{oob.Declines}]");

        // the overcap rule (1.0.4.247+) fires while moving: same check
        var overcap = Decide(s with { IsMoving = true, DashLanding = DashLanding.Danger }, protect);
        Check("overcap dash while moving, landing in a puddle: no dash", !Dashes(overcap) && overcap.Declines.Contains("shieldcharge:landing-danger"), $"{overcap.ActionId}:{overcap.Reason} [{overcap.Declines}]");
        Check("overcap dash while moving, landing outside the arena: no dash", !Dashes(Decide(s with { IsMoving = true, TargetDistance = 12f, DashLanding = DashLanding.Unreachable }, protect)));
        Check("overcap dash while moving, clean landing: spends one (unchanged)", Dashes(Decide(s with { IsMoving = true, DashLanding = DashLanding.Safe }, protect)));

        // nothing could answer (boss-mod IPC missing): blocks only where the void is real
        var cr = s with { CrucibleBoard = 1, TargetDistance = 12f, DashLanding = DashLanding.Unknown };
        var unk = Decide(cr, on);
        Check("Crucible board, landing unknown: no dash, reason recorded", !Dashes(unk) && unk.Declines.Contains("shieldcharge:landing-unknown"), $"{unk.ActionId}:{unk.Reason} [{unk.Declines}]");
        Check("outside the Crucible, landing unknown: dashes as before", Dashes(Decide(s with { TargetDistance = 12f, DashLanding = DashLanding.Unknown }, on)));

        // a held dash never costs the damage floor: the GCD chain still goes
        Check("dash held for a puddle: the GCD chain still goes", Decide(s with { TargetDistance = 12f, DashLanding = DashLanding.Danger }, on).ActionId != 0);
    }

    // BST-2 baseline (characterization): today the Parting Blow exit has NO enemy-count test - it fires
    // whenever its gates pass, on both presets, however many enemies stand in its area. These pin that
    // behaviour so the opt-in AoE rule can be shown to change only the option-on AoE-preset path.
    private static void PartingBlowExitBaseline()
    {
        Console.WriteLine("-- Parting Blow exit baseline (BST-2 characterization) --");
        var s = ExitReadyState();
        bool Exits(BstDecision d) => d.ActionId == BST.PartingBlow;

        var one = Decide(s with { EnemiesWithin6y = 1 }, BstSettings.Defaults(aoe: true));
        Check("BST-2 baseline: option off, one enemy in its area - the exit still fires (today's behaviour)",
            Exits(one), $"{one.ActionId}:{one.Reason} [{one.Declines}]");
        Check("BST-2 baseline: option off, five enemies - the exit fires exactly the same",
            Exits(Decide(s with { EnemiesWithin6y = 5 }, BstSettings.Defaults(aoe: true))));
        Check("BST-2 baseline: the ST preset exit is unchanged",
            Exits(Decide(s with { EnemiesWithin6y = 1 }, BstSettings.Defaults())));
        CheckCanary("BST-2 baseline canary: no familiar out must mean no Parting Blow",
            Exits(Decide(s with { ActiveSlot = 0 }, BstSettings.Defaults())));
    }

    // BST-2 proper: opt-in AoE gate for Parting Blow. Default off preserves today's count-blind
    // behaviour exactly; on, the AoE preset declines the exit until enough enemies stand in its area
    // (the ST preset is exempt - Aetheric Burst's bonus is the pack, not the single target).
    private static void PartingBlowAoeGate()
    {
        Console.WriteLine("-- Parting Blow AoE gate (BST-2) --");
        var s = ExitReadyState();
        bool Exits(BstDecision d) => d.ActionId == BST.PartingBlow;

        var off = Decide(s, BstSettings.Defaults(aoe: true));
        CheckLoud("BST-2: option off, one enemy - the exit still fires (default off changes nothing)",
            Exits(off), $"{off.ActionId}:{off.Reason} [{off.Declines}]");

        var on = Opt(BstSettings.Defaults(aoe: true), "AoePartingBlow", true);
        var blocked = Decide(s, on);
        CheckLoud("BST-2: option on, one enemy in the area - declined with the named reason",
            !Exits(blocked) && blocked.Declines.Contains("exit:partingblow-few-enemies", StringComparison.Ordinal),
            $"{blocked.ActionId}:{blocked.Reason} [{blocked.Declines}]");

        var fired = Decide(s with { EnemiesWithin6y = 3 }, on);
        CheckLoud("BST-2: option on, three enemies - the exit fires",
            Exits(fired), $"{fired.ActionId}:{fired.Reason} [{fired.Declines}]");

        var on5 = Opt(on, "AoePartingBlowEnemies", 5);
        CheckLoud("BST-2: the threshold is configurable - four enemies decline at five, five fire",
            !Exits(Decide(s with { EnemiesWithin6y = 4 }, on5)) && Exits(Decide(s with { EnemiesWithin6y = 5 }, on5)),
            "");

        CheckLoud("BST-2: the ST preset is exempt even with the option on",
            Exits(Decide(s, Opt(BstSettings.Defaults(), "AoePartingBlow", true))));

        CheckLoud("BST-2: the live config is wired (statics, UI, AdvancedSettings)",
            RepoFile("Combos", "PvE", "BST", "BST_Config.cs").Contains("BST_AoePartingBlow", StringComparison.Ordinal)
            && RepoFile("Combos", "PvE", "BST", "BST.cs").Contains("AoePartingBlow = BST_AoePartingBlow", StringComparison.Ordinal),
            "BST_Config.cs statics + BST.cs AdvancedSettings wiring");
    }

    /// <summary> A state where every TryExit gate passes: familiar out past the minimum stay, One with
    /// Nature spent, Parting Blow ready, another horn ready, in weave range - only the enemy count
    /// varies between cases. </summary>
    private static BstState ExitReadyState()
    {
        var s = BaseState(40);
        s.ActiveSlot = 1;
        s.Slot1Beast = 1; s.Slot2Beast = 34; s.Slot3Beast = 26; s.SlotBeastsKnown = true;
        s.ReadyHorn2 = s.ReadyHorn3 = true;
        s.ReadyParting = true;
        s.SinceSummon = 30f;
        s.EnemiesWithin6y = 1;
        return s;
    }

    // The fallback target list: Gluttony's Crucible targeting narrows to the head of the kill order, which may be
    // out of reach; the fallback must keep every exclusion (eggs, immune enemies, stances) and drop only the
    // kill-order narrowing, so the nearby plain enemy can take the hit.
    private static void FallbackCandidates()
    {
        Console.WriteLine("-- fallback targets (safe set without the kill-order narrowing) --");
        const uint egg = 14656;      // do-not-attack
        const uint priority = 14586; // priority add
        const uint plain = 14569;    // ordinary enemy
        Check("test data: egg is do-not-attack, priority add is priority, plain is neither",
            BST_CrucibleData.DoNotAttack.ContainsKey(egg) && BST_CrucibleData.PriorityAdds.Contains(priority)
            && !BST_CrucibleData.PriorityAdds.Contains(plain) && !BST_CrucibleData.DoNotAttack.ContainsKey(plain));

        BST_CrucibleLogic.TargetCandidate T(uint id, bool avoid = false, bool immune = false) => new(id, 100f, avoid, immune);
        var field = new[] { T(priority), T(plain), T(egg), T(plain, immune: true) };

        var narrowed = BST_CrucibleLogic.AllowedTargets(field);
        Check("the kill-order list is only the priority add (that is the targeting working as designed)", narrowed.SequenceEqual(new[] { 0 }), string.Join(",", narrowed));

        var safe = BST_CrucibleLogic.SafeTargets(field);
        Check("safe set keeps the priority add and the plain enemy", safe.Contains(0) && safe.Contains(1), string.Join(",", safe));
        Check("safe set never holds an egg / morpho", !safe.Contains(2), string.Join(",", safe));
        Check("safe set never holds a damage-immune enemy", !safe.Contains(3), string.Join(",", safe));
        Check("safe set prefers enemies out of a counter stance, and falls back to the stance enemy only when nothing else is up",
            BST_CrucibleLogic.SafeTargets(new[] { T(plain, avoid: true), T(plain) }).SequenceEqual(new[] { 1 })
            && BST_CrucibleLogic.SafeTargets(new[] { T(plain, avoid: true) }).SequenceEqual(new[] { 0 }));
    }

    // Live 2026-10-03, 62 Master's Board fights (GluttonyCombo 1.0.4.256-260). Two defects found in the logs that a unit case can pin.
    private static void LearnedFromTheRuns()
    {
        Console.WriteLine("-- learned from the 2026-10-03 runs --");
        var cfg = BstSettings.Defaults();

        // King Ahriman Piece starts "Curtains for Rank 5" as TWO simultaneous casts, 49428 and 49429, at 47.6 s and 164.3 s of the
        // fight. The target's castbar reads 49428 (CR| c=49428:4.7 down to 0.1) while the rule only knew 49429, so
        // crucible:petsave-curtains had zero decisions in all of the recorded history and both familiars died to the effect
        // (924 on Ziz, 890 on Bat).
        var curtains = CrucibleState() with { TargetCastId = 49428, TargetCastRemaining = 1.5f, PetHpPercent = 100f, ReadyParting = true };
        Check("Curtains for Rank 5 as the castbar shows it (49428): Parting Blow before it resolves, even with a healthy familiar",
            Decide(curtains, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-curtains" },
            $"{Decide(curtains, cfg).ActionId}/{Decide(curtains, cfg).Reason}");
        Check("... the same cast id with 4 s remaining: recall out (whole-window rule)", Decide(curtains with { TargetCastRemaining = 4.0f }, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-curtains" });

        // Roulette #3 of the same fight spawned a Final Hourglass (name id 14689). The target list narrows to the priority adds
        // whenever one is up, and the hourglass was not one: Hapalit and Dirty Eye (up) were, so it was never attacked and the
        // Death it carries killed the character through Doom. In roulettes 1 and 2, with no adds up, it was broken within ~12 s.
        BST_CrucibleLogic.TargetCandidate T(uint id) => new(id, 100f, false);
        const uint hourglass = 14689, hapalit = 14691, dirtyEye = 14692;
        Check("Final Hourglass is a priority add", BST_CrucibleData.PriorityAdds.Contains(hourglass));
        var field = new[] { T(hapalit), T(dirtyEye), T(hourglass) };
        var allowed = BST_CrucibleLogic.AllowedTargets(field);
        Check("hourglass up with Hapalit and Dirty Eye: the hourglass is the only target until it is broken",
            allowed.SequenceEqual(new[] { 2 }), string.Join(",", allowed));
        var noGlass = BST_CrucibleLogic.AllowedTargets(new[] { T(hapalit), T(dirtyEye) });
        Check("control: no hourglass, Hapalit and Dirty Eye stay equally targetable", noGlass.SequenceEqual(new[] { 0, 1 }), string.Join(",", noGlass));
        Check("control: the hourglass alone is targetable", BST_CrucibleLogic.AllowedTargets(new[] { T(hourglass) }).SequenceEqual(new[] { 0 }));
    }

    // Live 2026-10-06, the AutoDuty First Master's Board loop (First Degree, GluttonyCombo 1.0.4.285 / .286), and every registered
    // tankbuster cast in ffxivdb action_events since 2026-09-26 (394,732 rows). Snarl -> Parting Blow was switched ON by default on
    // 2026-10-03 (commit 26057ca6, ConfigMigration v10) on the guides' word that Parting Blow sends the hit to nobody. The logs say the opposite:
    //   - no Snarl, no Parting Blow:           the character took the hit 280 of 313 casts (89%)
    //   - Snarl alone:                          the familiar took it 76 of 94 (81%; 86% when the Snarl came 4 s or more before the hit)
    //   - a Parting Blow within 6 s before it:  the character took it in about 92% of ~150 casts, at EVERY margin from 0.5 s to 6 s
    // Sweeping Evisceration (Gargoyle, raw network log): the Parting Blow came 1.38-1.43 s before the hit in 64 of 71 tethered casts; the
    // enemy tethers to the character exactly 1.25 s after the press (the familiar is gone) and the hit follows 0.13 s later: 11:11:17.147
    // Parting Blow, 11:11:18.396 tether to the character, 11:11:18.529 "You take 1225 damage". The same Evisceration took 3,100 HP a fight
    // (55% of the character's 5,661) off the character in 29 Gargoyle fights.
    // The Snarl alone is not free on the harder board: First Degree Evisceration put 3,300-3,900 on a 3,492 HP familiar and knocked it
    // out in 11 of 15 casts, so a cover Snarl needs the familiar to hold twice the largest hit the logs recorded on a familiar.
    private static void TankbusterSnarlOnly()
    {
        Console.WriteLine("-- tankbuster: no Parting Blow after the Snarl; cover only what a familiar survives (2026-10-06 loop) --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        const uint evisceration = 48717, darkness = 48669, toxicVomit = 48809, erraticBlaster = 49188, obliterate = 50649;

        // 11:11:09.6 Sweeping Evisceration starts on the Gargoyle (7.6 s castbar), the character holds the enemy (CR| f=ysc), the
        // Diremite (3,492 HP) is out at 100%, Snarl and Parting Blow are both ready.
        var start = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 5, TargetCastId = evisceration, TargetCastRemaining = 7.6f,
            ReadySnarl = true, ReadyParting = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
            EnemyTargetsPlayer = true, EnemyTargetsPet = false,
        };
        Check("Evisceration (recorded hit on a familiar 2,021, 3,300-3,900 at First Degree), a 3,492 HP familiar: no cover Snarl",
            Decide(start, on).Reason != "aggro:snarl-tankbuster", Decide(start, on).Reason);

        // 11:11:17.078 the familiar holds (f=pc) after the Snarl the old rule pressed, 6.1 s later, 0.1 s of castbar left (the hit lands 1.4 s later).
        var landing = start with { TargetCastRemaining = 0.1f, SinceSnarl = 6.1f, EnemyTargetsPet = true, EnemyTargetsPlayer = false };
        var landed = Decide(landing, on);
        Check("1.4 s before it lands, the familiar holding: NO Parting Blow (it sends the hit back to the character)",
            landed.ActionId != BST.PartingBlow && landed.Reason != "crucible:snarl-parting", $"{landed.ActionId}/{landed.Reason}");
        Check("... and the window is still logged for grading", landed.Shadow == "crucible:snarl-parting-off", landed.Shadow);
        foreach (var (name, id, remaining) in new[] { ("Toxic Vomit", toxicVomit, 2.5f), ("Erratic Blaster", erraticBlaster, 1.0f), ("On the Properties of Darkness", darkness, 0.4f), ("Obliterate", obliterate, 1.0f) })
        {
            var d = Decide(landing with { TargetCastId = id, TargetCastRemaining = remaining }, on);
            Check($"{name}, {remaining} s left, familiar holding: no Parting Blow", d.ActionId != BST.PartingBlow && d.Reason != "crucible:snarl-parting", $"{d.ActionId}/{d.Reason}");
        }

        // A cast the logs show a familiar surviving twice over is still covered by the Snarl alone: Obliterate (Golem, battle 7: 1,112 on a
        // familiar, 1,349 on the character) and Salivous Snap (Borgny: 1,077 / 875).
        var golem = start with { CrucibleBattle = 7, TargetCastId = obliterate, TargetCastRemaining = 4.7f };
        Check("Obliterate cast starts, a 3,492 HP familiar: Snarl covers it", Decide(golem, on).Reason == "aggro:snarl-tankbuster", Decide(golem, on).Reason);
        Check("... Parting Blow on cooldown (a horn lock from an earlier recall): the Snarl still covers it",
            Decide(golem with { ReadyParting = false }, on).Reason == "aggro:snarl-tankbuster", Decide(golem with { ReadyParting = false }, on).Reason);
        Check("... a familiar at 2,000 HP (under twice the 1,112): no cover", Decide(golem with { PetHp = 2000f, PetHpPercent = 60f }, on).Reason != "aggro:snarl-tankbuster");
        Check("... the same cast with the dormant Parting Blow step opted in: the old window still presses",
            Decide(landing with { TargetCastId = obliterate, TargetCastRemaining = 1.0f }, on with { CrucibleTankbusterParting = true }) is { ActionId: BST.PartingBlow, Reason: "crucible:snarl-parting" });
        Check("Salivous Snap cast starts (Borgny): Snarl covers it", Decide(start with { CrucibleBattle = 0, TargetCastId = 48822, TargetCastRemaining = 6.7f }, on).Reason == "aggro:snarl-tankbuster");

        // Grim Fate (Gargoyle, 48730) is a FIVE-hit string: the recorded 212 is one hit, so the familiar has to hold five of them twice over.
        var grim = start with { TargetCastId = 48730, TargetCastRemaining = 4.7f };
        Check("Grim Fate, a 3,492 HP familiar (5 x 212 x 2 = 2,120): Snarl covers it", Decide(grim, on).Reason == "aggro:snarl-tankbuster", Decide(grim, on).Reason);
        Check("Grim Fate, a familiar at 900 HP (a five-hit string would take it down): no cover",
            Decide(grim with { PetHp = 900f, PetHpPercent = 60f }, on).Reason != "aggro:snarl-tankbuster");

        // A cast with no recorded hit on a familiar (Darkness, Final Sting, Mangling Fang) is never covered: the character takes it as before.
        Check("Darkness (no recorded hit on a familiar): no cover Snarl", Decide(start with { CrucibleBattle = 1, TargetCastId = darkness, TargetCastRemaining = 7.7f }, on).Reason != "aggro:snarl-tankbuster");

        // A character already under the cast's recorded hit is covered whatever the familiar's HP.
        var dying = start with { PetHp = 1500f, PetHpPercent = 60f, PlayerHp = 1100f, PlayerHpPercent = 19f };
        Check("character at 1,100 HP, Evisceration (1,225 recorded) coming, a 1,500 HP familiar: the Snarl goes in anyway",
            Decide(dying, on).ActionId == BST.Snarl, $"{Decide(dying, on).ActionId}/{Decide(dying, on).Reason}");

        // With the cover available the heal potion waits: Snarl first (the hit goes to the familiar); the potion fires only when no cover exists.
        var low = golem with { PlayerHp = 1700f, PlayerHpPercent = 30f, ReadyHealPotion = 46961 };
        Check("character 30%, Obliterate (1,349 recorded) coming, Snarl ready, a healthy familiar: Snarl, not the potion",
            Decide(low, on).Reason == "aggro:snarl-tankbuster", $"{Decide(low, on).ActionId}/{Decide(low, on).Reason}");
        var noCover = low with { ReadySnarl = false };
        Check("... no Snarl ready: the potion guard still fires", Decide(noCover, on).Reason == "crucible:raidwide-guard", Decide(noCover, on).Reason);
    }

    // The plugin did not know which degree the board was on (task tasks-20261006-crucible-degree-aware-tankbuster-cover-01). First Degree hits
    // 1.5-2.0x harder than the Standard runs every recorded number comes from (ffxivdb action_events, First Master's Board, 10-02/03 Standard
    // against 10-05/06 First Degree): Evisceration on a familiar max 2,616 -> 3,892, on the character max 1,254 -> 2,152, Grim Fate 194 -> 325 a
    // hit, Rotten Stench 943 -> 1,579. 1.0.4.287 therefore covered a cast only where a familiar held TWICE the recorded hit, which at Standard
    // gives up covers that worked, and the heavy-cast guard lines (recorded at Standard) fired too late at First. LazyCrucible reads the degree
    // from the board layout's set-degree event and publishes it; the state carries CrucibleDegreeKnown / CrucibleDegreeLevel (a default state
    // is "unknown", never Standard).
    private static void CrucibleDegreeAware()
    {
        Console.WriteLine("-- crucible degree: the cover Snarl and the heavy-cast guards scale with the board's degree --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
        const uint evisceration = 48717, grimFate = 48730, obliterate = 50649;
        BstState At(BstState s, int degree) => s with { CrucibleDegreeKnown = true, CrucibleDegreeLevel = degree };

        // 11:11:09.6 on 2026-10-06: Sweeping Evisceration starts, the character holds the enemy, a 3,492 HP Diremite at 100%, Snarl ready.
        var start = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 5, TargetCastId = evisceration, TargetCastRemaining = 7.6f,
            ReadySnarl = true, ReadyParting = true, PetHp = 3492f, PetHpPercent = 100f, PlayerHp = 5240f, PlayerHpPercent = 93f,
            EnemyTargetsPlayer = true, EnemyTargetsPet = false,
        };
        Check("a default state does not know the degree", !start.CrucibleDegreeKnown);
        Check("First Degree, Evisceration, a 3,492 HP familiar: no cover Snarl (it took 3,300-3,900 and fell in 11 of 15 casts)",
            Decide(At(start, CrucibleDegree.First), on).Reason != "aggro:snarl-tankbuster", Decide(At(start, CrucibleDegree.First), on).Reason);
        Check("Standard, the same cast and familiar: the cover Snarl goes in (Standard put 1,224-2,616 on a familiar)",
            Decide(At(start, CrucibleDegree.Standard), on) is { ActionId: BST.Snarl, Reason: "aggro:snarl-tankbuster" }, $"{Decide(At(start, CrucibleDegree.Standard), on).ActionId}/{Decide(At(start, CrucibleDegree.Standard), on).Reason}");
        Check("degree unknown: 1.0.4.287's margin stays (twice the recorded 2,021 is more than 3,492: no cover)",
            Decide(start, on).Reason != "aggro:snarl-tankbuster", Decide(start, on).Reason);
        Check("Second Degree (unmeasured): no cover", Decide(At(start, CrucibleDegree.Second), on).Reason != "aggro:snarl-tankbuster");
        Check("Third Degree (unmeasured): no cover", Decide(At(start, CrucibleDegree.Third), on).Reason != "aggro:snarl-tankbuster");

        // The margin at Standard is 1.25 x the recorded hit on a familiar (2,021 -> 2,526): a 2,400 HP familiar is not covered, a 2,600 one is.
        Check("Standard, a 2,400 HP familiar (under 1.25 x 2,021): no cover",
            Decide(At(start with { PetHp = 2400f, PetHpPercent = 70f }, CrucibleDegree.Standard), on).Reason != "aggro:snarl-tankbuster");
        Check("Standard, a 2,600 HP familiar (over 1.25 x 2,021): the cover Snarl goes in",
            Decide(At(start with { PetHp = 2600f, PetHpPercent = 75f }, CrucibleDegree.Standard), on).Reason == "aggro:snarl-tankbuster");

        // First Degree prices a familiar's hit at twice the recorded one, with the 1.25 margin: Obliterate (1,112 recorded) needs 2,780 HP,
        // Grim Fate (five hits of 212) needs 2,650.
        var golem = start with { CrucibleBattle = 7, TargetCastId = obliterate, TargetCastRemaining = 4.7f };
        Check("First Degree, Obliterate, a 3,492 HP familiar: the cover Snarl goes in", Decide(At(golem, CrucibleDegree.First), on).Reason == "aggro:snarl-tankbuster");
        Check("First Degree, Obliterate, a 2,700 HP familiar (under 2 x 1.25 x 1,112): no cover",
            Decide(At(golem with { PetHp = 2700f, PetHpPercent = 77f }, CrucibleDegree.First), on).Reason != "aggro:snarl-tankbuster");
        var grim = start with { TargetCastId = grimFate, TargetCastRemaining = 4.7f };
        Check("First Degree, Grim Fate, a 3,492 HP familiar: the cover Snarl goes in", Decide(At(grim, CrucibleDegree.First), on).Reason == "aggro:snarl-tankbuster");
        Check("First Degree, Grim Fate, a 2,500 HP familiar (1,060 recorded x 2 = 2,120, the margin wants 2,650): no cover",
            Decide(At(grim with { PetHp = 2500f, PetHpPercent = 72f }, CrucibleDegree.First), on).Reason != "aggro:snarl-tankbuster");
        Check("Standard, Grim Fate, a 1,400 HP familiar (5 x 212 x 1.25 = 1,325): the cover Snarl goes in",
            Decide(At(grim with { PetHp = 1400f, PetHpPercent = 60f }, CrucibleDegree.Standard), on).Reason == "aggro:snarl-tankbuster");

        // A character already under the scaled hit is covered whatever the familiar's HP: at First Degree the 1,225 recorded is 2,144.
        var dying = start with { PetHp = 1500f, PetHpPercent = 60f, PlayerHp = 2000f, PlayerHpPercent = 50f };
        Check("First Degree, the character at 2,000 HP (under 1.75 x 1,225), a 1,500 HP familiar: the Snarl goes in anyway",
            Decide(At(dying, CrucibleDegree.First), on) is { ActionId: BST.Snarl, Reason: "aggro:snarl-tankbuster" }, $"{Decide(At(dying, CrucibleDegree.First), on).ActionId}/{Decide(At(dying, CrucibleDegree.First), on).Reason}");
        Check("Standard, the character at 2,000 HP (over the 1,225 recorded) and a 1,500 HP familiar: no Snarl",
            Decide(At(dying, CrucibleDegree.Standard), on).Reason != "aggro:snarl-tankbuster");

        // The heavy-cast guard lines were recorded at Standard. Rotten Stench (Corpse Flower, 48690, hits the character and the familiar together,
        // 773 recorded, 1,579 at First Degree): a 1,100 HP character is over the Standard line and under the First Degree one.
        var stench = CrucibleState() with
        {
            InCombat = true, CrucibleBoard = 4, CrucibleBattle = 3, TargetCastId = 48690, TargetCastRemaining = 3f,
            PlayerHp = 1100f, PlayerHpPercent = 20f, ReadyHealPotion = 46961, EnemyTargetsPet = true, EnemyTargetsPlayer = false,
        };
        Check("Rotten Stench, 1,100 HP, degree unknown: over the recorded 773, no guard", Decide(stench, cfg).Reason != "crucible:raidwide-guard");
        Check("Rotten Stench, 1,100 HP, Standard: no guard", Decide(At(stench, CrucibleDegree.Standard), cfg).Reason != "crucible:raidwide-guard");
        Check("Rotten Stench, 1,100 HP, First Degree (the hit is up to 1,579): the potion",
            Decide(At(stench, CrucibleDegree.First), cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" }, Decide(At(stench, CrucibleDegree.First), cfg).Reason);
        Check("Rotten Stench, 1,500 HP, First Degree (over 1.75 x 773 = 1,353): no guard",
            Decide(At(stench with { PlayerHp = 1500f, PlayerHpPercent = 28f }, CrucibleDegree.First), cfg).Reason != "crucible:raidwide-guard");
        Check("Rotten Stench, 1,100 HP, Second Degree (at least First's scale): the potion",
            Decide(At(stench, CrucibleDegree.Second), cfg) is { ActionId: 46961, Reason: "crucible:raidwide-guard" });
        Check("Atomic Ray at 6,000 HP: no guard unknown / Standard, the guard at First Degree (4,782 x 1.75)",
            Decide(CrucibleState() with { InCombat = true, TargetCastId = 49272, TargetCastRemaining = 8f, PlayerHp = 6000f, PlayerHpPercent = 75f, ReadyHealPotion = 46961 }, cfg).Reason != "crucible:raidwide-guard"
            && Decide(At(CrucibleState() with { InCombat = true, TargetCastId = 49272, TargetCastRemaining = 8f, PlayerHp = 6000f, PlayerHpPercent = 75f, ReadyHealPotion = 46961 }, CrucibleDegree.Standard), cfg).Reason != "crucible:raidwide-guard"
            && Decide(At(CrucibleState() with { InCombat = true, TargetCastId = 49272, TargetCastRemaining = 8f, PlayerHp = 6000f, PlayerHpPercent = 75f, ReadyHealPotion = 46961 }, CrucibleDegree.First), cfg).Reason == "crucible:raidwide-guard");

        // The shared parser and scales.
        Check("layout event 2, 1 reads First", CrucibleDegree.FromStageDetailEvent(0, 2, 2, 1) == CrucibleDegree.First);
        Check("layout event 2, 0 reads Standard", CrucibleDegree.FromStageDetailEvent(0, 2, 2, 0) == CrucibleDegree.Standard);
        Check("layout event 2, 3 reads Third", CrucibleDegree.FromStageDetailEvent(0, 2, 2, 3) == CrucibleDegree.Third);
        Check("layout events that are not the set-degree event read nothing",
            CrucibleDegree.FromStageDetailEvent(0, 1, 8, null) == CrucibleDegree.Unknown && CrucibleDegree.FromStageDetailEvent(1, 1, 0, null) == CrucibleDegree.Unknown
            && CrucibleDegree.FromStageDetailEvent(0, 2, 5, 1) == CrucibleDegree.Unknown && CrucibleDegree.FromStageDetailEvent(0, 2, 2, 4) == CrucibleDegree.Unknown
            && CrucibleDegree.FromStageDetailEvent(0, 2, 2, -1) == CrucibleDegree.Unknown && CrucibleDegree.FromStageDetailEvent(0, 2, 2, null) == CrucibleDegree.Unknown
            && CrucibleDegree.FromStageDetailEvent(1, 2, 2, 1) == CrucibleDegree.Unknown);
    }

    // A familiar that holds the enemy is not recalled into a tankbuster it can take (2026-10-06; ffxivdb action_events + the plugin's CR| lines,
    // 2026-09-26 .. 10-06): with Snarl alone the familiar took the hit in 76 of 94 casts (81%); in 47 recalls by a Parting Blow with no Snarl while
    // the familiar held the enemy and a registered tankbuster landed within 6 s, 36 landed on the character (a stricter count of the same sweep: 17 and 11), because the recalled familiar's
    // enmity goes with it. The discretionary recalls (the cycle exit, the pack Parting Blow; the Self-destruct burst can never overlap, the
    // target casts one thing at a time) wait for the hit to land; a recall to SAVE a low familiar is never held, and neither is one where the
    // logs show the familiar would not survive the hit (the cover rule's own question, scaled to the degree).
    private static void OptionalRecallHeldForTankbuster()
    {
        Console.WriteLine("-- optional recalls wait for a tankbuster the familiar holds --");
        var cfg = BstSettings.Defaults();
        var cycle = cfg with { CrucibleCycleForDamage = true };
        const uint obliterate = 50649, evisceration = 48717, rippling = 48721;
        BstState At(BstState s, int degree) => s with { CrucibleDegreeKnown = true, CrucibleDegreeLevel = degree };

        // The 09-30 14:35 shape that Exits with a cycle recall (two healthy ready horns behind the active one), the familiar holding the enemy.
        var cycling = CrucibleState() with
        {
            CrucibleBoard = 4, CrucibleBattle = 3, ActiveSlot = 2, PetObjectBeast = 34, Slot1Beast = 1, Slot2Beast = 34, Slot3Beast = 26,
            ReadyHorn1 = true, ReadyHorn2 = false, ReadyHorn3 = true, Slot1PetHp = 96f, Slot2PetHp = 100f, Slot3PetHp = 90f,
            PetHpPercent = 100f, PetHp = 3492f, SinceSummon = 12f, SinceHornPress = 13f, TargetHpPercent = 79f, HighestEnemyHpPercent = 79f,
            PlayerHpPercent = 80f, PlayerHp = 4600f, ReadyParting = true, EnemyTargetsPet = true, EnemyTargetsPlayer = false,
        };
        Check("control: the cycle exit recalls with the familiar holding and no cast up",
            Decide(cycling, cycle).ActionId == BST.PartingBlow, $"{Decide(cycling, cycle).ActionId}/{Decide(cycling, cycle).Reason}");

        var golem = cycling with { CrucibleBattle = 7, TargetCastId = obliterate, TargetCastRemaining = 4.7f };
        var held = Decide(golem, cycle);
        Check("Obliterate cast up, the familiar holding and able to take it: the cycle exit waits",
            held.ActionId != BST.PartingBlow && held.Declines.Contains("crucible:exit-tankbuster-up"), $"{held.ActionId}/{held.Reason} [{held.Declines}]");
        Check("... also at First Degree (1,112 x 2 x 1.25 = 2,780 under 3,492)",
            Decide(At(golem, CrucibleDegree.First), cycle).ActionId != BST.PartingBlow);
        Check("... the character holding the enemy instead: nothing to hand back, the exit is unchanged",
            Decide(golem with { EnemyTargetsPet = false, EnemyTargetsPlayer = true }, cycle).ActionId == BST.PartingBlow);
        Check("... the hit has already landed (castbar over by 1.5 s): the exit goes",
            Decide(golem with { TargetCastRemaining = -1.5f }, cycle).ActionId == BST.PartingBlow);

        var evis = cycling with { CrucibleBattle = 5, TargetCastId = evisceration, TargetCastRemaining = 7.6f };
        Check("Evisceration at Standard, a 3,492 HP familiar (it takes it, 1.25 x 2,021): the cycle exit waits",
            Decide(At(evis, CrucibleDegree.Standard), cycle).ActionId != BST.PartingBlow);
        Check("Evisceration at First Degree, a 3,492 HP familiar (it would not survive 3,300-3,900): the recall stays allowed",
            Decide(At(evis, CrucibleDegree.First), cycle).ActionId == BST.PartingBlow);
        Check("Rippling Evisceration (not a tankbuster): the exit is unchanged", Decide(evis with { TargetCastId = rippling }, cycle).ActionId == BST.PartingBlow);

        // A recall that SAVES the familiar is not a discretionary one.
        var dying = cycling with { PetHpPercent = 20f, SinceSummon = 2f };
        var saved = Decide(dying with { CrucibleBattle = 7, TargetCastId = obliterate, TargetCastRemaining = 4.7f }, cycle);
        Check("a 20% familiar still swaps out with Obliterate up", saved is { ActionId: BST.FirstBattlehorn, Reason: "crucible:petsave-swap-critical" }, $"{saved.ActionId}/{saved.Reason}");

        // The pack Parting Blow (the window option on), once the pack's release has gone off.
        var on = cfg with { CruciblePackWindow = true };
        var pack = CrucibleState() with
        {
            CrucibleBoard = 3, CrucibleBattle = 1, ActiveSlot = 1, Slot1Beast = 34, PetObjectBeast = 34, Slot2Beast = 34, Slot3Beast = 26,
            OneWithNature = false, ReadyTempered = false, TemperedRecastRemaining = 50f, SinceTempered = 3f, SinceSummon = 14f, SinceHornPress = 15f,
            ReadyHorn2 = true, ReadyHorn3 = true, TargetHpPercent = 90f, HighestEnemyHpPercent = 90f, EnemyCount = 4, TargetNameId = 14567,
            PetHp = 3492f, PetHpPercent = 100f, EnemyTargetsPet = true, EnemyTargetsPlayer = false,
        };
        Check("control: the pack Parting Blow goes with the familiar holding and no cast up",
            Decide(pack, on) is { ActionId: BST.PartingBlow, Reason: "crucible:pack-parting" }, $"{Decide(pack, on).ActionId}/{Decide(pack, on).Reason}");
        var crushing = Decide(pack with { TargetCastId = 48471, TargetCastRemaining = 3f }, on);
        Check("Crushing Blade (a Third Board tankbuster, nothing recorded on a familiar) up: the pack Parting Blow waits",
            crushing.ActionId != BST.PartingBlow && crushing.Declines.Contains("crucible:pack-tankbuster-up"), $"{crushing.ActionId}/{crushing.Reason} [{crushing.Declines}]");
        Check("... and the hold is not logged as the option being off", crushing.Shadow != "crucible:pack-parting-off", crushing.Shadow);
    }

    // ================================================================== helpers

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok)
        {
            _pass++;
            return;
        }

        _fail++;
        Console.WriteLine($"FAIL {what}{(detail is null ? "" : $"  [{detail}]")}");
    }

    /// <summary> Sets a <see cref="BstSettings"/> field by name through reflection, boxed so the struct
    /// copy is what gets mutated. The BST-2 red proof must FAIL AT RUNTIME against pre-fix code
    /// (field absent -> assignment silently skipped), not fail to compile; a typo'd name fails the
    /// check post-fix, so the reflection cannot mask a defect. </summary>
    private static BstSettings Opt(BstSettings s, string field, object value)
    {
        object boxed = s;
        typeof(BstSettings).GetField(field)?.SetValue(boxed, value);
        return (BstSettings)boxed;
    }

    /// <summary> Reads a file from the live-plugin tree, walking up from the harness output directory
    /// to the repo root (same pattern as the telemetry harness). Empty string when not found. </summary>
    private static string RepoFile(params string[] rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "check-preset-ids.py")))
            dir = dir.Parent;
        if (dir is null)
            return "";
        var parts = new string[rel.Length + 4];
        parts[0] = dir.FullName;
        parts[1] = "src";
        parts[2] = "GluttonyCombo";
        parts[3] = "GluttonyCombo";
        rel.CopyTo(parts, 4);
        return File.ReadAllText(Path.Combine(parts));
    }

    /// <summary> A <see cref="Check"/> that also prints its PASS line: the BST-1/BST-2 cases use it so the
    /// run output names each shipped case when it passes (the shared Check is silent on pass by design). </summary>
    private static void CheckLoud(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else Check(what, false, detail);
    }

    /// <summary> A deliberately-wrong assertion that must FAIL: it proves the <see cref="Check"/> plumbing
    /// can fail, so the PASS lines are not vacuous (canary pattern from the rot/harness-spike harness).
    /// A canary that PASSES counts as a real failure. </summary>
    private static void CheckCanary(string what, bool ok, string? detail = null)
    {
        if (ok)
        {
            _fail++;
            Console.WriteLine($"FAIL CANARY (expected to FAIL, and did not) {what}{(detail is null ? "" : $"  [{detail}]")}");
        }
        else
        {
            _canary++;
            Console.WriteLine($"FAIL CANARY (expected to FAIL): {what}");
        }
    }
}
