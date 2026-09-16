using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Offline proof for the rebuilt Beastmaster engine (BST_RotationLogic, 2026-09-16).
///     Part 1: unit cases on the pure rules, including a replay of Joey's 2026-09-16 17:48 failure.
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

        SimulateAllLevels(verbose);

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
    ///     Joey 2026-09-16 17:48: L18-21, wespe in horn 1, crab in horn 2. Old build: summon -> Tempered
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
        Check("pet object arriving/leaving -> no summon on top of it", arriving.ActionId is not (BST.FirstBattlehorn or BST.SecondBattlehorn or BST.ThirdBattlehorn), arriving.Reason);
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

    // ================================================================== simulator

    private sealed record Loadout(string Name, int[] Slots);

    private static readonly Loadout[] Loadouts =
    [
        new("CuSith/Raptor/Buffalo", [1, 34, 26]),
        new("Wespe/Crab/empty (Joey 09-16)", [10, 15, 0]),
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
                        Console.WriteLine($"   {tag,-52} uptime {sim.UptimePercent,5:0.0}%  PB {sim.Count(BST.PartingBlow),2}  TR {sim.Count(BST.TemperedRelease),2}  Borrow {sim.Count(BST.Borrow),2}  Trick {sim.Count(BST.Trick),3}  axes {sim.AxesUsed,3}  combos {sim.Combos,3} (intentional {sim.IntentionalCombos,3})  Rally {sim.Count(BST.Rally),2}  Cheer {sim.Count(BST.RallyingCheer),2}  universality {sim.Universality}  horns {sim.Summons,2}");
                }
            }
        }

        Console.WriteLine($"   worst familiar uptime across all runs: {worstUptime:0.0}% ({worstUptimeRun})");
        Check("worst familiar uptime >= 85% whenever a familiar is possible", worstUptime >= 85.0, worstUptimeRun);
        if (totals.Count > 0)
            Console.WriteLine("   violation totals: " + string.Join(", ", totals.Select(kv => $"{kv.Key}={kv.Value}")));
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
        private float _leavingUntil = -1; // retreat animation (pet object lingers)
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
        public int Combos, IntentionalCombos, AxesUsed, Summons, Universality;
        private float _familiarOutTime, _combatTime, _firstSummonAt = -1;
        private float _lastGcdAt;

        public Sim(int level, int[] slots, BstSettings cfg)
        {
            _level = level;
            _slots = slots;
            _cfg = cfg;
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
            Execute(d, state);
        }

        private float Since(float at) => at < -50 ? float.MaxValue : _t - at;

        private BstState BuildState()
        {
            var petObject = _activeSlot != 0 || _t < _leavingUntil || (_arrivalAt >= 0 && _t >= _arrivalAt - 0.5f);
            return new BstState
            {
                Level = _level,
                InCombat = InCombat,
                HasHostileTarget = true,
                TargetDistance = 3f,
                TargetHpPercent = 100f,
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
                    if (beast is { } b && (b.Release & BeastmasterReleaseTraits.Exit) != 0 && _t - _summonedAt < _cfg.MinFamiliarStaySeconds)
                        Fail("wespe-final-sting-before-min-stay", $"{_t - _summonedAt:0.0} s after summon");
                    if (beast is { } bb && (bb.Release & BeastmasterReleaseTraits.Sleep) != 0 && !_cfg.AllowSleepRelease)
                        Fail("sleep-release-used", bb.Name);
                    if (beast is { } kb && (kb.Release & (BeastmasterReleaseTraits.Knockback | BeastmasterReleaseTraits.DrawIn)) != 0 && !_cfg.AllowDisplacingRelease)
                        Fail("displacing-release-used", kb.Name);
                    _oneWithNature = false; _temperedCd = 30f; _lastTemperedAt = _t; _animLock = 0.6f;
                    if (_level >= LvVantageFromTempered) _vantageUntil = _t + 60f;
                    Bump(id);
                    if (beast is { } eb && (eb.Release & BeastmasterReleaseTraits.Exit) != 0)
                        Retreat("finalsting");
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
                    if (_oneWithNature && _level >= LvTemperedRelease && PlanRelease(ActiveBeastData, _cfg) == ReleasePlan.Use)
                        Fail("retreat-with-unspent-one-with-nature", ActiveBeastData?.Name ?? "?");
                    _partingCd = 10f; _animLock = 0.6f; _vantageUntil = -1;
                    if (_level >= LvTrick) _familiarTp = Math.Min(250, _familiarTp + 24);
                    Bump(id);
                    Retreat("partingblow");
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
            if (_t - _lastInstinctualAt < 7f && IsCompass(_lastInstinctualAffinity))
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

            if (_activeSlot != 0)
            {
                if (InCombat)
                    Fail("in-combat-swap", $"slot{_activeSlot} -> slot{slot} ({d.Reason})");
                else
                    _activeSlot = 0; // out of combat swap: no lockout
            }

            _lastHornAt = _t; _arrivalAt = _t + 1.0f; _arrivalSlot = slot; _animLock = 1.0f;
            Bump(HornAction(slot));
        }

        private void Retreat(string how)
        {
            if (_activeSlot == 0) return;

            var otherReady = Enumerable.Range(1, LearnedSlots).Any(i => i != _activeSlot && HornReadyNow(i));
            if (!otherReady && !_cfg.AllowPetlessCycling && InCombat)
                Fail("retreat-left-no-familiar", $"{how} from slot{_activeSlot} with no other horn ready");

            if (InCombat)
                _hornLockedUntil[_activeSlot] = _t + 90f;
            _activeSlot = 0;
            _leavingUntil = _t + 3f;
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
