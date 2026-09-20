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
        OutOfCombat();
        CrucibleDataChecks();
        CrucibleRules();
        CrucibleTargetingAndAdvisor();

        SimulateAllLevels(verbose);
        SimulateCrucible(verbose);

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
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
        Check("tankbusters: Deadly Thrust 46906; Erratic Blaster castbar 49188 lands 1 s after it",
            BST_CrucibleData.Tankbusters.Contains(46906) && BST_CrucibleData.Tankbusters.Contains(49188) && BST_CrucibleData.TankbusterHitDelay(49188) == 1f);
    }

    private static void CrucibleTargetingAndAdvisor()
    {
        Console.WriteLine("-- crucible auto-targeting and beast picks --");
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);
        BST_CrucibleLogic.TargetCandidate C(uint nameId, float hp, bool avoid) => new(nameId, hp, avoid);

        Check("zu egg never allowed", Allowed(C(14575, 100f, false), C(14572, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("only eggs up: nothing to target", Allowed(C(14575, 100f, false), C(14576, 100f, false)).Count == 0);
        Check("stance / invulnerable skipped while another enemy is up", Allowed(C(14571, 100f, true), C(14570, 90f, false)).SequenceEqual(new[] { 1 }));
        Check("everything to avoid: still targetable", Allowed(C(14571, 100f, true)).SequenceEqual(new[] { 0 }));
        Check("tablitaurs 80% / 50%: only the healthier", Allowed(C(14555, 80f, false), C(14556, 50f, false)).SequenceEqual(new[] { 0 }));
        Check("tablitaurs 60% / 55%: both", Allowed(C(14555, 60f, false), C(14556, 55f, false)).Count == 2);
        Check("Loosefrox 30% / Chewchum 70%: Chewchum", Allowed(C(14561, 30f, false), C(14562, 70f, false)).SequenceEqual(new[] { 1 }));
        Check("Pas de Seul + succubus mage: the add first", Allowed(C(14541, 90f, false), C(14542, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("bone knight + bone bishop: the bishop first", Allowed(C(14531, 100f, false), C(14532, 100f, false)).SequenceEqual(new[] { 1 }));
        Check("ogre in Burning Ward + wisp: the wisp", Allowed(C(14538, 100f, true), C(14539, 100f, false)).SequenceEqual(new[] { 1 }));

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

        // --- PlanHornChanges + HpPercentByRow (autograb assignment planning) ---
        var planWant = slotsFull.Select(p => p.Row).ToList();
        Check("PlanHornChanges: all slots already correct → no calls",
            BST_CrucibleAdvisor.PlanHornChanges(planWant, slotsFull).Count == 0);
        if (planWant.Count >= 2)
        {
            var oneWrong = new List<int>(planWant) { [0] = planWant[1] };
            var changes = BST_CrucibleAdvisor.PlanHornChanges(oneWrong, slotsFull);
            Check("PlanHornChanges: one slot wrong → exactly that slot changes",
                changes.Count == 1 && changes[0].Slot == 0 && changes[0].FromRow == planWant[1]
                && changes[0].ToRow == planWant[0],
                string.Join(",", changes.Select(c => $"{c.Slot}:{c.FromRow}->{c.ToRow}")));
        }
        if (planWant.Count >= 1)
        {
            var knocked = new List<int>(planWant) { [0] = 0 };
            var replace = BST_CrucibleAdvisor.PlanHornChanges(knocked, slotsFull);
            Check("PlanHornChanges: knocked-out slot occupant is replaced",
                replace.Count == 1 && replace[0].Slot == 0 && replace[0].FromRow == 0
                && replace[0].ToRow == planWant[0],
                string.Join(",", replace.Select(c => $"{c.Slot}:{c.FromRow}->{c.ToRow}")));
        }

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

        // --- FormationArm re-arm (Dalamud-free phase decision) ---
        var closed = default(BST_CrucibleAdvisor.FormationArmState);
        var opened = BST_CrucibleAdvisor.NextFormationArm(closed, screenOpen: true, battleKey: 10);
        Check("FormationArm: screen opens → armed",
            BST_CrucibleAdvisor.IsFormationArmed(opened) && opened.BattleKey == 10);

        var afterPass = BST_CrucibleAdvisor.MarkFormationPassDone(opened);
        var sameBattle = BST_CrucibleAdvisor.NextFormationArm(afterPass, screenOpen: true, battleKey: 10);
        Check("FormationArm: pass done, same battle → not re-armed",
            !BST_CrucibleAdvisor.IsFormationArmed(sameBattle) && sameBattle.PassDone);

        var battleChanged = BST_CrucibleAdvisor.NextFormationArm(afterPass, screenOpen: true, battleKey: 20);
        Check("FormationArm: battle changes while screen open → re-armed",
            BST_CrucibleAdvisor.IsFormationArmed(battleChanged) && battleChanged.BattleKey == 20 && !battleChanged.PassDone);

        var afterClose = BST_CrucibleAdvisor.NextFormationArm(afterPass, screenOpen: false, battleKey: 10);
        var reopen = BST_CrucibleAdvisor.NextFormationArm(afterClose, screenOpen: true, battleKey: 10);
        Check("FormationArm: screen closes and reopens → re-armed",
            !afterClose.ScreenOpen && BST_CrucibleAdvisor.IsFormationArmed(reopen));

        var aborted = BST_CrucibleAdvisor.MarkFormationAborted(opened);
        var abortSame = BST_CrucibleAdvisor.NextFormationArm(aborted, screenOpen: true, battleKey: 99);
        Check("FormationArm: aborted phase stays closed until screen drops (battle change ignored)",
            !BST_CrucibleAdvisor.IsFormationArmed(abortSame) && abortSame.Aborted);
        var abortCleared = BST_CrucibleAdvisor.NextFormationArm(
            BST_CrucibleAdvisor.NextFormationArm(aborted, screenOpen: false, battleKey: 0),
            screenOpen: true, battleKey: 11);
        Check("FormationArm: aborted phase re-arms after screen drop",
            BST_CrucibleAdvisor.IsFormationArmed(abortCleared));

        // --- Round-7 surface / pre-entry arming ---
        var preentryOpen = BST_CrucibleAdvisor.NextFormationArm(closed, screenOpen: true, battleKey: -1, surfaceKey: 0);
        Check("FormationArm: screen opens outside any board (preentry surface) → armed",
            BST_CrucibleAdvisor.IsFormationArmed(preentryOpen) && preentryOpen.SurfaceKey == 0);

        // "No roster" is a live-layer gate (armed requires partyCount>0); the latch itself still arms on screen open.
        // Territory change alone must not re-arm: same surface + same battle key, pass already done.
        var preentryDone = BST_CrucibleAdvisor.MarkFormationPassDone(preentryOpen);
        var territoryOnly = BST_CrucibleAdvisor.NextFormationArm(preentryDone, screenOpen: true, battleKey: -1, surfaceKey: 0);
        Check("FormationArm: territory change alone (same surface+battle key) → not re-armed",
            !BST_CrucibleAdvisor.IsFormationArmed(territoryOnly) && territoryOnly.PassDone);

        var surfaceFlip = BST_CrucibleAdvisor.NextFormationArm(preentryDone, screenOpen: true, battleKey: -1, surfaceKey: 1);
        Check("FormationArm: surface preentry→board → re-armed",
            BST_CrucibleAdvisor.IsFormationArmed(surfaceFlip) && surfaceFlip.SurfaceKey == 1 && !surfaceFlip.PassDone);

        // Coverage ranking: board known, no single battle — Why must carry the coverage tag.
        var covCandidates = new[] { 1, 4, 5, 7, 11 }; // Cu Sith, spriggan-ish rows from B1 roster samples
        var covHp = new Dictionary<int, int> { [1] = 100, [4] = 100, [5] = 100, [7] = 100, [11] = 100 };
        var cov = BST_CrucibleAdvisor.PickSlotsCoverage(1, covCandidates, covHp, 3);
        Check("PickSlotsCoverage: returns up to 3 picks for board 1", cov.Count is >= 1 and <= 3);
        Check("PickSlotsCoverage: every Why starts with coverage board tag + unidentified",
            cov.Count > 0 && cov.All(p => p.Why.StartsWith("coverage board 1, battle unidentified", StringComparison.Ordinal)));

        // --- Round-8 horn index basis + party index resolve (autograb write route) ---
        var partySample = new List<int> { 17, 28, 35, 22, 18, 6, 1, 12, 29, 27 };
        Check("IsHornIndexBasis: empty with party → horn-capable",
            BST_CrucibleAdvisor.IsHornIndexBasis(Array.Empty<int>(), partySample.Count));
        Check("IsHornIndexBasis: 0.1.4 indices → horn",
            BST_CrucibleAdvisor.IsHornIndexBasis(new[] { 0, 1, 4 }, partySample.Count));
        Check("IsHornIndexBasis: ten familiar ids → not horn",
            !BST_CrucibleAdvisor.IsHornIndexBasis(partySample, partySample.Count));
        Check("IsHornIndexBasis: pet id 27 as lone value with party 10 → not horn",
            !BST_CrucibleAdvisor.IsHornIndexBasis(new[] { 27 }, partySample.Count));
        // .222 live defect: transient horn-shaped sl=3.5.6 at Bentbranch roster-menu-open must not
        // take the horn write path (screen/shape before size). Same vector on a board/ActivePet screen stays horn.
        Check("IsHornWriteBasis: .222 transient 3.5.6 on roster surface → not horn",
            !BST_CrucibleAdvisor.IsHornWriteBasis(new[] { 3, 5, 6 }, partySample.Count, rosterSurface: true));
        Check("IsHornWriteBasis: .222 transient 3.5.6 on horn/board surface → horn",
            BST_CrucibleAdvisor.IsHornWriteBasis(new[] { 3, 5, 6 }, partySample.Count, rosterSurface: false));
        Check("IsHornWriteBasis: empty on roster surface → not horn (wait settle)",
            !BST_CrucibleAdvisor.IsHornWriteBasis(Array.Empty<int>(), partySample.Count, rosterSurface: true));
        Check("IsHornWriteBasis: empty on horn surface → horn-capable",
            BST_CrucibleAdvisor.IsHornWriteBasis(Array.Empty<int>(), partySample.Count, rosterSurface: false));
        Check("FindPartyIndex: present row → index",
            BST_CrucibleAdvisor.FindPartyIndex(partySample, 18) == 4
            && BST_CrucibleAdvisor.FindPartyIndex(partySample, 17) == 0);
        Check("FindPartyIndex: missing row → -1",
            BST_CrucibleAdvisor.FindPartyIndex(partySample, 99) == -1);
        var resolved = BST_CrucibleAdvisor.ResolveHornPetRows(new[] { 0, 1, 4 }, partySample);
        Check("ResolveHornPetRows: indices 0.1.4 → pets 17.28.18",
            resolved.Count == 3 && resolved[0] == 17 && resolved[1] == 28 && resolved[2] == 18);
        Check("SelectionEquals: match / mismatch",
            BST_CrucibleAdvisor.SelectionEquals(new[] { 0, 1, 4 }, new[] { 0, 1, 4 })
            && !BST_CrucibleAdvisor.SelectionEquals(new[] { 0, 1, 4 }, new[] { 0 })
            && !BST_CrucibleAdvisor.SelectionEquals(new[] { 0, 1 }, new[] { 0, 1, 4 }));

        // --- Round-10 Stage-1 roster writer planning and coverage tests ---
        var allCandidates = new List<int>();
        for (var r = 1; r <= BST_Beasts.Count; r++) allCandidates.Add(r);
        var cov10 = BST_CrucibleAdvisor.PickSlotsCoverage(1, allCandidates, new Dictionary<int, int>(), 10);
        Check("PickSlotsCoverage: 10 picks for roster on board 1", cov10.Count == 10);
        Check("PickSlotsCoverage: all 10 picks unique", cov10.Select(p => p.Row).Distinct().Count() == 10);
        Check("PickSlotsCoverage: all 10 Why start with coverage board 1",
            cov10.All(p => p.Why.StartsWith("coverage board 1, battle unidentified", StringComparison.Ordinal)));

        var covPicks = cov10;
        var desired10 = cov10.ConvertAll(p => p.Row);
        // Case 1: already matching -> 0 changes
        var changesMatch = BST_CrucibleAdvisor.PlanRosterChanges(desired10, covPicks);
        Check("PlanRosterChanges: identical roster → 0 changes", changesMatch.Count == 0);

        // Case 2: completely disjoint roster
        var actualDisjoint = allCandidates.Where(c => !desired10.Contains(c)).Take(10).ToList();
        var changesDisjoint = BST_CrucibleAdvisor.PlanRosterChanges(actualDisjoint, covPicks);
        Check("PlanRosterChanges: disjoint roster → 10 removes, 10 adds",
            changesDisjoint.Count == 20
            && changesDisjoint.Count(c => c.FromRow != 0 && c.ToRow == 0) == 10
            && changesDisjoint.Count(c => c.FromRow == 0 && c.ToRow != 0) == 10);

        // Case 3: 7 matching, 3 different
        var partialSample = new List<int>(desired10.Take(7));
        partialSample.AddRange(actualDisjoint.Take(3));
        var changesPartial = BST_CrucibleAdvisor.PlanRosterChanges(partialSample, covPicks);
        Check("PlanRosterChanges: 7 matching + 3 different → 3 removes, 3 adds",
            changesPartial.Count == 6
            && changesPartial.Count(c => c.FromRow != 0 && c.ToRow == 0) == 3
            && changesPartial.Count(c => c.FromRow == 0 && c.ToRow != 0) == 3);
    }

    /// <summary> In combat on the First Board, L30, Cu Sith out (One with Nature spent), raptor / buffalo on ready horns 2 and 3. </summary>
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

    private static void CrucibleRules()
    {
        Console.WriteLine("-- crucible rules --");
        var cfg = BstSettings.Defaults();
        var on = cfg with { CrucibleAggro = CrucibleAggroMode.On };
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
        Check("critical 20%, no horn ready: Parting Blow", Decide(crit, cfg) is { ActionId: BST.PartingBlow, Reason: "crucible:petsave-partingblow" }, Decide(crit, cfg).Reason);
        Check("critical 20%, a horn ready with a 40% familiar: swap even inside the grace",
            Decide(crit with { ReadyHorn2 = true, Slot2PetHp = 40f }, cfg) is { ActionId: BST.SecondBattlehorn, Reason: "crucible:petsave-swap-critical" });
        Check("critical, a horn blown 1 s ago: give it time, no Parting Blow", Decide(crit with { SinceHornPress = 1f }, cfg).ActionId != BST.PartingBlow);
        Check("critical wespe with One with Nature: Final Sting", Decide(crit with { Slot1Beast = 10, PetObjectBeast = 10 }, cfg) is { ActionId: BST.TemperedRelease, Reason: "crucible:petsave-finalsting" });
        Check("last enemy at 2%: no save", !Decide(crit with { TargetHpPercent = 2f, HighestEnemyHpPercent = 2f }, cfg).Reason.StartsWith("crucible:petsave"));
        Check("Parting Blow recasting: declined, logged", Decide(crit with { ReadyParting = false }, cfg).Declines.Contains("crucible:petsave-partingblow-recast"));
        Check("egg near the target: no save Parting Blow", Decide(crit with { ProtectedNearTarget = true }, cfg).ActionId != BST.PartingBlow);

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
        Check("coblyn out, fight needs an interrupt: Borrow Soulkin", Decide(coblynFight, cfg) is { ActionId: BST.Borrow, Reason: "crucible:borrow-soulkin" });
        Check("Soul Kinship already held: Tempered Release", Decide(coblynFight with { KinshipHeld = true, BeastModeResolved = BST.SoulCrush, KinshipSlot = 2 }, cfg).ActionId == BST.TemperedRelease);

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
        Check("allowed: coblyn out, Borrow", Decide(coblyn, prepull) is { ActionId: BST.Borrow, Reason: "crucible:prepull-borrow-soulkin" });
        var held = Decide(coblyn with { OneWithNature = false, KinshipHeld = true, KinshipSlot = 2, BeastModeResolved = BST.SoulCrush, SinceHornPress = 6f }, prepull);
        Check("allowed: Soul Kinship held, swap to horn 1", held.ActionId == BST.FirstBattlehorn, $"{held.Reason} [{held.Declines}]");
        Check("allowed: no Soulkin, pugil on horn 3, Wavekin", Decide(pre with { Slot2Beast = 26 }, prepull).Reason == "crucible:prepull-wavekin-slot3");
        Check("in combat, no familiar, moving: no horn", Decide(CrucibleState() with { ActiveSlot = 0, PetObjectPresent = false, SinceHornPress = 30f, ReadyHorn1 = true, IsMoving = true }, cfg) is { } mv
            && !IsHorn(mv.ActionId) && mv.Declines.Contains("crucible:summon-moving"));

        // Snarl / Challenge (default: logged only)
        var parry = CrucibleState() with { TargetHasParry = true, ReadySnarl = true, ReadyParting = false };
        var shadowed = Decide(parry, cfg);
        Check("parry, shadow mode (default): Snarl logged, not pressed", shadowed.ActionId != BST.Snarl && shadowed.Shadow == "aggro:snarl-parry", $"{shadowed.ActionId} {shadowed.Shadow}");
        Check("parry, On: Snarl", Decide(parry, on).ActionId == BST.Snarl);
        Check("parry, Off: nothing", Decide(parry, cfg with { CrucibleAggro = CrucibleAggroMode.Off }) is { Shadow: "" } off && off.ActionId != BST.Snarl);
        Check("parry just ended with the familiar holding aggro, On: Challenge",
            Decide(CrucibleState() with { ParryJustEnded = true, ReadyChallenge = true, EnemyTargetsPet = true, EnemyTargetsPlayer = false, ReadyParting = false }, on).ActionId == BST.Challenge);
        Check("hard hit on the character: no Snarl (the familiar would lose ~3x what the character saves)",
            Decide(CrucibleState() with { TargetCastId = 46906, TargetCastRemaining = 3f, ReadySnarl = true, ReadyParting = false }, on).ActionId != BST.Snarl);
        var lowChar = CrucibleState() with { PlayerHpPercent = 35f, PetHpPercent = 80f, PetHp = 20000f, PlayerIntakePerSecond = 500f, ReadySnarl = true, ReadyParting = false };
        Check("character 35%, familiar can carry 15 s of intake: Snarl", Decide(lowChar, on).Reason == "aggro:snarl-player-low", Decide(lowChar, on).Reason);
        Check("character 35%, familiar cannot carry it: no Snarl", Decide(lowChar with { PlayerIntakePerSecond = 2000f }, on).ActionId != BST.Snarl);
        Check("character 20%: last-resort Snarl", Decide(lowChar with { PlayerHpPercent = 20f, PlayerIntakePerSecond = 2000f }, on).Reason == "aggro:snarl-last-resort");
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
        var spCfg = on with { CrucibleSnarlParting = true };
        var castStart = CrucibleState() with { TargetCastId = tb, TargetCastRemaining = 4f, ReadySnarl = true };
        Check("tankbuster cast starts: Snarl", Decide(castStart, spCfg).Reason == "aggro:snarl-tankbuster", Decide(castStart, spCfg).Reason);
        var landing = CrucibleState() with { TargetCastId = tb, TargetCastRemaining = 1.2f, SinceSnarl = 3f, EnemyTargetsPet = true, EnemyTargetsPlayer = false };
        Check("1.2 s before it lands with Snarl up: Parting Blow", Decide(landing, spCfg) is { ActionId: BST.PartingBlow, Reason: "crucible:snarl-parting" });
        Check("3 s before it lands: not yet", Decide(landing with { TargetCastRemaining = 3f }, spCfg).Reason != "crucible:snarl-parting");
        Check("no Snarl in the last 45 s: no whiff", Decide(landing with { SinceSnarl = 60f }, spCfg).Reason != "crucible:snarl-parting");
        Check("snarl-parting is off by default", Decide(landing, on).Reason != "crucible:snarl-parting");
        Check("snarl-parting in log-only mode: logged, not pressed",
            Decide(landing, cfg with { CrucibleSnarlParting = true }) is { Shadow: "crucible:snarl-parting" } logged && logged.Reason != "crucible:snarl-parting");
        Check("score mode with snarl-parting: Snarl for the tankbuster", Decide(castStart, scoreCfg with { CrucibleSnarlParting = true }).ActionId == BST.Snarl);
        BST_CrucibleData.Tankbusters.Remove(tb);
        Check("Erratic Blaster castbar 0.8 s left (lands in 1.8 s): not yet", Decide(landing with { TargetCastId = 49188, TargetCastRemaining = 0.8f }, spCfg).Reason != "crucible:snarl-parting");
        Check("Erratic Blaster castbar 0.4 s left (lands in 1.4 s): Parting Blow", Decide(landing with { TargetCastId = 49188, TargetCastRemaining = 0.4f }, spCfg).Reason == "crucible:snarl-parting");

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

                    // 3%/s is a stress run: a knocked-out familiar is reported, not failed.
                    var violations = sim.Violations.Where(v => !(drain >= 3f && v.Kind == "familiar-ko")).ToList();
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
                    Kos++;
                    Fail("familiar-ko", $"slot{_activeSlot} ({BST_Beasts.ByRow(_slots[_activeSlot - 1])?.Name})");
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

            // A critical familiar must be saved whenever Parting Blow or a healthier horn could do it.
            if (Crucible && _activeSlot != 0 && InCombat && _petHp[_activeSlot] <= BST_CrucibleLogic.CriticalHp(_cfg) && state.CanWeave
                && TargetHp > BST_CrucibleLogic.EnemyDyingHp && _t - _lastHornAt >= 2.5f && _arrivalAt < 0
                && (state.ReadyParting || Enumerable.Range(1, LearnedSlots).Any(i => i != _activeSlot && HornReadyNow(i) && (_petSeen[i] ? _petHp[i] : 100f) >= _petHp[_activeSlot] + BST_CrucibleLogic.SwapMinGain))
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
}
