using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 rotation lane (task tasks-20261015-crucible-r4-fix-rotation-shield-charge-01). Titles start with "r4-rotation:".
///     Rows: rot-09 (Natural Instinct never reaches the 2 stacks Rallying Cheer needs), rot-21 (finishers reach 25 y / 10 y on a board),
///     rot-32 (the TP axes are off the GCD: weave them, never on a GCD-ready tick), 2.1-03 (Hammerleap) and 3.6-06b (Feral Lunge):
///     keep the last Shield Charge while the cast runs, 3.0-09b (Deadly Demesne): no dash into the cage, 3.6-05c (Unmooring Melody).
///     New <see cref="BstState"/> fields are set by name through <see cref="R4Set"/>, so against the pre-fix code a case fails at runtime
///     (field absent, assignment skipped) instead of failing to compile.
/// </summary>
internal static partial class Program
{
    /// <summary> Sets a <see cref="BstState"/> field by name through reflection, boxed so the struct copy is what gets mutated. </summary>
    private static BstState R4Set(BstState s, string field, object value)
    {
        object boxed = s;
        typeof(BstState).GetField(field)?.SetValue(boxed, value);
        return (BstState)boxed;
    }

    /// <summary> The state of a character that sends the TP axes the way the live half now does (as abilities, in a weave window). </summary>
    private static BstState R4AxesOffGcd(BstState s) => R4Set(s, "AxesOffGcd", true);

    private static void R4RotationLane20261015()
    {
        Console.WriteLine("-- r4-rotation: Natural stacks for Rallying Cheer, finisher reach, axe weave, Shield Charge reserve and cage --");
        var cfg = BstSettings.Defaults();
        string D(BstDecision d) => $"{d.ActionId}/{d.Reason} [{d.Declines}]";

        // A free familiar, both gauges full, L40 (Rallying Cheer is learned), the Cu Sith (row 1) out.
        var t40 = BaseState(40) with
        {
            Slot1Beast = 1, Slot2Beast = 34, Slot3Beast = 26, SlotBeastsKnown = true, ActiveSlot = 1, PetObjectPresent = true, PetObjectBeast = 1,
            PlayerTp = 150, FamiliarTp = 150, ReadyTrick = true, ReadyAxe = true, SinceSummon = 20f, SinceHornPress = 21f, ReadyParting = false,
            PetOrder = PetOrder.None,
        };

        // ---------------------------------------------------------------- rot-09: Natural Instinct reaches 2 stacks
        // Mastered Instinct caps at 3 and Rally is on a 120 s recast, so every axe-finished combo after the cap adds nothing; a Trick-finished
        // combo (axe first) adds a Natural stack instead. The old rule banked one Natural stack (at 0) and never a second.
        const BeastmasterAffinity pet = BeastmasterAffinity.Rampant;
        Check("r4-rotation: rot-09 L40, Mastered at its cap (3), one Natural banked: the next pair starts axe-first for the second",
            ChooseComboOrder(40, pet, 3, 1) == ComboOrder.AxeFirst, ChooseComboOrder(40, pet, 3, 1).ToString());
        Check("r4-rotation: rot-09 ... and a second one for the third (Natural caps at 3)",
            ChooseComboOrder(40, pet, 3, 2) == ComboOrder.AxeFirst);
        Check("r4-rotation: rot-09 ... Natural at 3: Trick first again (nothing left to bank)",
            ChooseComboOrder(40, pet, 3, 3) == ComboOrder.TrickFirst);
        Check("r4-rotation: rot-09 control: Mastered below the cap still builds first (2 Mastered, 1 Natural -> Trick first)",
            ChooseComboOrder(40, pet, 2, 1) == ComboOrder.TrickFirst && ChooseComboOrder(40, pet, 1, 1) == ComboOrder.TrickFirst);
        Check("r4-rotation: rot-09 control: below L40 Natural is never banked, at any Mastered count",
            ChooseComboOrder(39, pet, 3, 1) == ComboOrder.TrickFirst);
        var capped = Decide(t40 with { MasterStacks = 3, NaturalStacks = 1, GcdReady = true, CanWeave = false }, cfg);
        Check("r4-rotation: rot-09 Decide, L40, 3 Mastered + 1 Natural, both gauges ready: the pair starts axe-first",
            capped.Reason == "combo:axe-first", D(capped));
        var done = Decide(t40 with { MasterStacks = 3, NaturalStacks = 2, FamiliarTp = 40, ReadyCheer = true, CanWeave = true }, cfg);
        Check("r4-rotation: rot-09 Decide, 2 Natural and the familiar below 100 TP: Rallying Cheer is pressed",
            done is { ActionId: BST.RallyingCheer, Reason: "rallyingcheer:stacks" }, D(done));
        // The stack climb with the real ChooseComboOrder: a combo ends with +1 Mastered (Trick first) or +1 Natural (axe first); Rally (every 6th
        // combo, once Mastered is 3) clears Mastered. How many combos until 2 Natural stand together with the Cheer ready?
        int CombosToCheer(int maxCombos)
        {
            int master = 0, natural = 0, sinceRally = 99;
            for (var combo = 1; combo <= maxCombos; combo++)
            {
                sinceRally++;
                if (ChooseComboOrder(40, pet, master, natural) == ComboOrder.AxeFirst)
                    natural = Math.Min(3, natural + 1);
                else
                    master = Math.Min(3, master + 1);
                if (natural >= 2)
                    return combo;
                if (master == 3 && sinceRally >= 6) { master = 0; sinceRally = 0; }
            }
            return -1;
        }
        var climb = CombosToCheer(40);
        Check("r4-rotation: rot-09 the stacks climb: 2 Natural stand within 12 combos of a fresh fight (the old rule: never in 40)",
            climb is > 0 and <= 12, $"combos {climb}");

        // ---------------------------------------------------------------- rot-21: finishers reach 25 y (Risen Fall) and 10 y (Calamity) on a board
        var fin = BaseState(50) with
        {
            CrucibleBoard = 5, CrucibleBattle = 6, TargetDistance = 2f, SunOrMoonActive = true, SunMoon = BeastmasterAffinity.Moonstalker,
            PlayerTp = 300, ReadyAxe = true, DashLanding = DashLanding.Safe,
        };
        var moon12 = Decide(fin with { TargetDistance = 12f }, cfg);
        Check("r4-rotation: rot-21 board, Moonstalker, target 12 y out: Risen Fall (it does not move the character, reach 25 y)",
            moon12 is { ActionId: BST.SpinningAxe, Reason: "finisher:risenfall-universality" }, D(moon12));
        Check("r4-rotation: rot-21 ... at 25 y still Risen Fall, at 26 y none",
            Decide(fin with { TargetDistance = 25f }, cfg).Reason == "finisher:risenfall-universality" && !Decide(fin with { TargetDistance = 26f }, cfg).Reason.StartsWith("finisher:"));
        var sun8 = Decide(fin with { SunMoon = BeastmasterAffinity.Sunstrider, TargetDistance = 8f }, cfg);
        Check("r4-rotation: rot-21 board, Sunstrider, target 8 y out: Calamity (a 10 y line, no movement) completes Universality",
            sun8 is { ActionId: BST.GaleAxe, Reason: "finisher:calamity-universality" }, D(sun8));
        Check("r4-rotation: rot-21 ... at 10 y still Calamity, at 11 y none",
            Decide(fin with { SunMoon = BeastmasterAffinity.Sunstrider, TargetDistance = 10f }, cfg).Reason == "finisher:calamity-universality"
            && !Decide(fin with { SunMoon = BeastmasterAffinity.Sunstrider, TargetDistance = 11f }, cfg).Reason.StartsWith("finisher:"));
        Check("r4-rotation: rot-21 control: Hawkish Talons (a dash) stays melee-only on a board: 3 y -> Mistral Axe, 8 y -> not Mistral Axe",
            Decide(fin with { SunMoon = BeastmasterAffinity.Sunstrider, TargetDistance = 3f }, cfg).ActionId == BST.MistralAxe
            && Decide(fin with { SunMoon = BeastmasterAffinity.Sunstrider, TargetDistance = 8f }, cfg).ActionId != BST.MistralAxe);
        var guardedMelee = Decide(fin with { TargetDistance = 3f, ProtectedNearTarget = true }, cfg);
        Check("r4-rotation: rot-21 control: an egg / morpho in reach of the target stops the finisher in melee (unchanged)",
            !guardedMelee.Reason.StartsWith("finisher:") && guardedMelee.Declines.Contains("crucible:finisher-protected-near"), D(guardedMelee));
        var guarded = Decide(fin with { TargetDistance = 12f, ProtectedNearTarget = true }, cfg);
        Check("r4-rotation: rot-21 ... and it stops the ranged finisher the same way (the reach does not skip the protected-enemy hold)",
            !guarded.Reason.StartsWith("finisher:") && guarded.Declines.Contains("crucible:finisher-protected-near"), D(guarded));

        // ---------------------------------------------------------------- rot-32: the TP axes are off the GCD
        // xivapi 7.56: Avalanche / Mistral / Spinning / Gale Axe are category Weaponskill with recast group 16 (the GCD is group 58), so the game
        // takes them in a weave window. Recorded play (action_events, Joey, since 09-01): 33 axes sent 0.7-0.85 s after a GCD kept the next GCD
        // 2.497 s after it, 2,707 axes sent when the GCD was ready made it 2.933 s (0.43 s lost each).
        var bare = BaseState(5) with { PlayerTp = 150, ReadyAxe = true };
        var weaveOnly = R4AxesOffGcd(bare with { GcdReady = false, CanWeave = true });
        var gcdTick = R4AxesOffGcd(bare with { GcdReady = true, CanWeave = false });
        Check("r4-rotation: rot-32 off-GCD axes: TP 150, a weave window -> the axe goes (it rides the weave, the GCD is not touched)",
            Decide(weaveOnly, cfg).ActionId == BST.AvalancheAxe, D(Decide(weaveOnly, cfg)));
        Check("r4-rotation: rot-32 off-GCD axes: TP 150, GCD ready, no window -> no axe (it would push the GCD back); the GCD chain goes",
            Decide(gcdTick, cfg).ActionId == BST.SmashAxe, D(Decide(gcdTick, cfg)));
        Check("r4-rotation: rot-32 off-GCD axes: the weave window must be a real one (a GCD-ready tick that CanWeave reports for an idle GCD is not)",
            Decide(R4AxesOffGcd(bare with { GcdReady = true, CanWeave = true }), cfg).ActionId != BST.AvalancheAxe);
        Check("r4-rotation: rot-32 off-GCD axes: the axe waits out its own 5 s recast (Rally refilled TP 2 s after the last axe) -> not offered",
            Decide(R4AxesOffGcd(bare with { GcdReady = false, CanWeave = true, SinceAxe = 2f, LastAxeAffinity = BeastmasterAffinity.Rampant }), cfg).ActionId != BST.AvalancheAxe
            && Decide(R4AxesOffGcd(bare with { GcdReady = false, CanWeave = true, SinceAxe = 5.1f }), cfg).ActionId == BST.AvalancheAxe);
        Check("r4-rotation: rot-32 legacy sender (axes still typed as Weaponskills): the GCD-ready tick keeps the old behaviour",
            Decide(bare with { GcdReady = true, CanWeave = false }, cfg).ActionId == BST.AvalancheAxe
            && Decide(bare with { GcdReady = false, CanWeave = true }, cfg).ActionId != BST.AvalancheAxe);
        // every site: after the pet's Heart, after an axe (a Rally refill), the axe-first pair start, the overcap axe
        var afterTrick = t40 with { SinceTrick = 1.5f, SincePetHeart = 0.5f };
        var weaveOff = (BstState s) => R4AxesOffGcd(s with { GcdReady = false, CanWeave = true });
        var gcdOff = (BstState s) => R4AxesOffGcd(s with { GcdReady = true, CanWeave = false });
        Check("r4-rotation: rot-32 axe after the pet's Heart (combo:axe-clockwise-after-trick): in the weave window, never on the GCD-ready tick",
            Decide(weaveOff(afterTrick), cfg).Reason.StartsWith("combo:axe-", StringComparison.Ordinal)
            && !Decide(gcdOff(afterTrick), cfg).Reason.StartsWith("combo:axe-", StringComparison.Ordinal), D(Decide(weaveOff(afterTrick), cfg)));
        var afterAxe = t40 with { SinceAxe = 6f, SinceTrick = 9f, SincePetHeart = 8f, LastAxeAffinity = BeastmasterAffinity.Rampant, PlayerTp = 130, FamiliarTp = 30 };
        afterAxe = afterAxe with { SinceAxe = 5.2f };
        Check("r4-rotation: rot-32 axe after an axe (combo:axe-after-axe): in the weave window, never on the GCD-ready tick",
            Decide(weaveOff(afterAxe), cfg).Reason == "combo:axe-after-axe" && !Decide(gcdOff(afterAxe), cfg).Reason.StartsWith("combo:axe-", StringComparison.Ordinal),
            $"{D(Decide(weaveOff(afterAxe), cfg))} | {D(Decide(gcdOff(afterAxe), cfg))}");
        var pairStart = t40 with { MasterStacks = 2, NaturalStacks = 0 };
        Check("r4-rotation: rot-32 axe-first pair start (combo:axe-first): in the weave window, never on the GCD-ready tick",
            Decide(weaveOff(pairStart), cfg).Reason == "combo:axe-first" && !Decide(gcdOff(pairStart), cfg).Reason.StartsWith("combo:axe-", StringComparison.Ordinal),
            $"{D(Decide(weaveOff(pairStart), cfg))} | {D(Decide(gcdOff(pairStart), cfg))}");
        var overcapAxe = t40 with { PlayerTp = 240, FamiliarTp = 20, ReadyTrick = false };
        Check("r4-rotation: rot-32 overcap axe (axe:overcap): in the weave window, never on the GCD-ready tick",
            Decide(weaveOff(overcapAxe), cfg).Reason.StartsWith("axe:", StringComparison.Ordinal) && !Decide(gcdOff(overcapAxe), cfg).Reason.StartsWith("axe:", StringComparison.Ordinal),
            $"{D(Decide(weaveOff(overcapAxe), cfg))} | {D(Decide(gcdOff(overcapAxe), cfg))}");
        Check("r4-rotation: rot-32 the combo wait is still named (combo:axe-waiting-gcd) on the GCD-ready tick",
            Decide(gcdOff(afterTrick), cfg).Declines.Contains("combo:axe-waiting-gcd"), D(Decide(gcdOff(afterTrick), cfg)));

        // The GCD chain is never delayed: 60 s of a melee fight with the real Decide, 100 ms ticks, GCD 2.5 s, every action a 0.6 s animation
        // lock, an axe every time TP reaches 100 (+15 per GCD), 5 s axe recast. The sender: an ability goes when the lock is under the queue
        // window (0.5 s), a Weaponskill when the GCD is. CanWeave is Wrath's: GCD left > 0.6 s + lock, lock <= 0.6 s.
        (float MaxGcdGap, float MedianGcdGap, int Axes, int WeavedAxes, int GcdPresses) Timeline(bool axesOffGcd)
        {
            // deciseconds, so the 2.5 s GCD is exactly 25 ticks
            int gcd = 0, lockT = 0, axeCd = 0, lastGcdAt = -1, tp = 100, axes = 0, weavedAxes = 0;
            var gaps = new List<float>();
            for (var tick = 0; tick < 600; tick++)
            {
                gcd = Math.Max(0, gcd - 1); lockT = Math.Max(0, lockT - 1); axeCd = Math.Max(0, axeCd - 1);
                var st = BaseState(5) with
                {
                    PlayerTp = tp, ReadyAxe = axeCd == 0, SinceAxe = axeCd == 0 ? float.MaxValue : (50 - axeCd) / 10f,
                    GcdReady = gcd <= 5, CanWeave = lockT <= 6 && gcd > 6 + lockT,
                };
                if (axesOffGcd)
                    st = R4AxesOffGcd(st);
                var d = Decide(st, cfg);
                var isAxe = d.ActionId == BST.AvalancheAxe;
                var sends = isAxe ? (axesOffGcd ? lockT <= 5 : gcd == 0 && lockT == 0) : gcd == 0 && lockT == 0 && d.ActionId != 0;
                if (!sends)
                    continue;
                lockT = 6;
                if (isAxe)
                {
                    tp = 0; axeCd = 50; axes++;
                    if (gcd >= 7)
                        weavedAxes++;
                }
                else
                {
                    if (lastGcdAt >= 0)
                        gaps.Add((tick - lastGcdAt) / 10f);
                    lastGcdAt = tick; gcd = 25; tp = Math.Min(250, tp + 15);
                }
            }
            gaps.Sort();
            return (gaps.Count == 0 ? 0f : gaps[^1], gaps.Count == 0 ? 0f : gaps[gaps.Count / 2], axes, weavedAxes, gaps.Count + 1);
        }
        var legacy = Timeline(false);
        var weaved = Timeline(true);
        Check("r4-rotation: rot-32 timeline: with the axes on the GCD-ready tick the longest GCD gap is pushed past 3 s (the recorded 2.9 s, the old behaviour)",
            legacy.MaxGcdGap >= 3.0f, $"max gap {legacy.MaxGcdGap:0.0} median {legacy.MedianGcdGap:0.0} axes {legacy.Axes} gcds {legacy.GcdPresses}");
        Check("r4-rotation: rot-32 timeline: with the axes weaved every axe is sent with 0.7 s or more of GCD left and no GCD gap exceeds 2.5 s (the GCD chain is never delayed)",
            weaved.Axes > 0 && weaved.WeavedAxes == weaved.Axes && weaved.MaxGcdGap <= 2.5f, $"max gap {weaved.MaxGcdGap:0.0} median {weaved.MedianGcdGap:0.0} axes {weaved.Axes} mid-GCD {weaved.WeavedAxes} gcds {weaved.GcdPresses}");
        Check("r4-rotation: rot-32 timeline: and no axe is lost to the weave (same count, or one more, as on the GCD-ready tick) while the GCD count goes up",
            weaved.Axes >= legacy.Axes - 1 && weaved.GcdPresses > legacy.GcdPresses, $"axes {weaved.Axes} vs {legacy.Axes}, gcds {weaved.GcdPresses} vs {legacy.GcdPresses}");
        var senderSrc = RepoFile("Extensions", "UIntExtensions.cs");
        var stateSrc = RepoFile("Combos", "PvE", "BST", "BST.cs");
        Check("r4-rotation: rot-32 the sender types the four TP axes as abilities (UIntExtensions.ActionAttackType) and the state reads that typing",
            senderSrc.Contains("OffGcdAxe", StringComparison.Ordinal) && stateSrc.Contains("AxesOffGcd =", StringComparison.Ordinal));

        // ---------------------------------------------------------------- Shield Charge: reserve (2.1-03, 3.6-06b), cage (3.0-09b), the dart (3.6-05c)
        // Cast data (action_events, 28-43 casts each): Hammerleap 48135 7.0 s and 48137 8.1 s start 0.05 s apart; Feral Lunge 48569 3.8 s and 48570 4.0 s;
        // Deadly Demesne 48608 3.0 s, Fetters 48609 land 8.1 s after the cast starts, the last blade lines 13.6 / 16.6 s after it.
        var live = BaseState(50) with { CrucibleBoard = 3, CrucibleBattle = 6, TargetNameId = 14657, PlayerHpPercent = 100f, TargetDistance = 12f };
        live.ReadyShieldCharge = true; live.ShieldChargeCharges = 1; live.ShieldChargeMax = 3; live.DashLanding = DashLanding.Safe;
        bool Dashes(BstDecision d) => d.ActionId == BST.ShieldCharge;
        var overcapOnly = cfg with { UseShieldCharge = false, ShieldChargeOvercap = true };
        var leap = live with { TargetCastId = 48135, TargetCastRemaining = 5.0f };
        var leapDecision = Decide(leap, cfg);
        Check("r4-rotation: 2.1-03 Hammerleap cast (48135) running, one charge left: the dash is held, the last charge is kept to return",
            !Dashes(leapDecision) && leapDecision.Declines.Contains("shieldcharge:reserve-hammerleap"), D(leapDecision));
        Check("r4-rotation: 2.1-03 ... the landing cast (48137) holds it too, at its full 8 s",
            !Dashes(Decide(live with { TargetCastId = 48137, TargetCastRemaining = 7.9f }, cfg)));
        Check("r4-rotation: 2.1-03 ... with two charges the dash still goes (one stays in reserve)",
            Dashes(Decide(leap with { ShieldChargeCharges = 2 }, cfg)));
        Check("r4-rotation: 2.1-03 ... the held dash never costs the damage floor: the GCD chain still goes",
            leapDecision.ActionId != 0);
        var afterLeap = Decide(live, cfg);
        Check("r4-rotation: 2.1-03 ... once the cast is over the last charge is spent to return (gap-close)",
            Dashes(afterLeap) && afterLeap.Reason == "shieldcharge:gapclose", D(afterLeap));
        Check("r4-rotation: 2.1-03 ... outside a board nothing is reserved (the reserve is a Crucible tactic)",
            Dashes(Decide(leap with { CrucibleBoard = 0, CrucibleBattle = 0 }, cfg)));
        Check("r4-rotation: 2.1-03 ... another cast (49271, 5 s left) does not reserve the charge",
            Dashes(Decide(live with { TargetCastId = 49271, TargetCastRemaining = 5.0f }, cfg)));
        // the longest hold the reserve can produce: sweep a cast of 8.1 s in 0.1 s ticks and count the ticks the one charge is held
        var heldTicks = 0;
        for (var rem = 8.1f; rem > 0.05f; rem -= 0.1f)
            if (!Dashes(Decide(live with { TargetCastId = 48137, TargetCastRemaining = rem }, cfg)))
                heldTicks++;
        Check("r4-rotation: 2.1-03 the reserve holds the last charge for the cast and no longer: at most 8.2 s over an 8.1 s cast (under 15 s)",
            heldTicks is > 0 and <= 82, $"{heldTicks} ticks = {heldTicks / 10f:0.0} s");
        var lunge = live with { TargetCastId = 48569, TargetCastRemaining = 3.7f };
        var lungeDecision = Decide(lunge, cfg);
        Check("r4-rotation: 3.6-06b Feral Lunge cast (48569) running, one charge left: the dash is held, one charge kept",
            !Dashes(lungeDecision) && lungeDecision.Declines.Contains("shieldcharge:reserve-feral-lunge"), D(lungeDecision));
        Check("r4-rotation: 3.6-06b ... 48570 (4.0 s) too, and with two charges the dash goes",
            !Dashes(Decide(live with { TargetCastId = 48570, TargetCastRemaining = 3.9f }, cfg)) && Dashes(Decide(lunge with { ShieldChargeCharges = 2 }, cfg)));
        Check("r4-rotation: 3.6-06b ... after the cast the charge is spent normally",
            Dashes(Decide(live, cfg)));
        Check("r4-rotation: 3.6-06b ... the overcap dash (option on, dash off) keeps the last charge during the cast too",
            !Dashes(Decide(lunge with { TargetDistance = 3f, ShieldChargeMax = 1 }, overcapOnly)));

        // 3.0-09b Deadly Demesne: no dash while the cage stands (cast start to the last blade line, 16.6 s); only the dash is held
        var cage = live with { ShieldChargeCharges = 3 };
        Check("r4-rotation: 3.0-09b control: three charges, nothing casting, the cage not up -> the dash goes",
            Dashes(Decide(cage, cfg)));
        var caged = R4Set(cage, "DashCageRemaining", 10f);
        var cagedDecision = Decide(caged, cfg);
        Check("r4-rotation: 3.0-09b the cage is up (10 s of it left): no gap-close dash, even with three charges, decline named",
            !Dashes(cagedDecision) && cagedDecision.Declines.Contains("shieldcharge:cage"), D(cagedDecision));
        Check("r4-rotation: 3.0-09b ... no max-charges spend in melee, no overcap spend either",
            !Dashes(Decide(R4Set(cage with { TargetDistance = 2f, ShieldChargeMax = 3 }, "DashCageRemaining", 10f), cfg))
            && !Dashes(Decide(R4Set(cage with { TargetDistance = 12f }, "DashCageRemaining", 10f), overcapOnly)));
        Check("r4-rotation: 3.0-09b ... the finisher dash (Hawkish Talons) is held by the cage too",
            Decide(R4Set(fin with { SunMoon = BeastmasterAffinity.Sunstrider, DashLanding = DashLanding.Safe }, "DashCageRemaining", 10f), cfg).ActionId != BST.MistralAxe);
        Check("r4-rotation: 3.0-09b ... the hold is the dash only: the GCD chain goes while the cage stands",
            cagedDecision.ActionId != 0);
        Check("r4-rotation: 3.0-09b ... the hold is on a board only (outside one the cage field is not read)",
            Dashes(Decide(R4Set(cage with { CrucibleBoard = 0, CrucibleBattle = 0 }, "DashCageRemaining", 10f), cfg)));
        var cageSrc = RepoFile("Combos", "PvE", "BST", "BST_Crucible.cs");
        Check("r4-rotation: 3.0-09b the live half stamps the cast (48608) and feeds DashCageRemaining",
            cageSrc.Contains("DashCageRemainingSeconds", StringComparison.Ordinal) && stateSrc.Contains("DashCageRemaining =", StringComparison.Ordinal));
        // the longest dash hold: the cage window from the cast's first tick; the settle after the cast-ending hold is the existing 1.5 s
        var cageHeld = 0;
        for (var tick = 0; tick < 300; tick++)
        {
            var since = tick / 10f;
            var casting = since < 3.0f;
            var cs = R4Set(cage with { TargetCastId = casting ? 48608u : 0u, TargetCastRemaining = casting ? 3.0f - since : 0f }, "DashCageRemaining", Math.Max(0f, DashCageSecondsFor(since)));
            // the 1.5 s settle that already follows the cast-ending hold (DashHazardClock) is part of the hold
            if (!Dashes(Decide(cs with { DashSettleRemaining = since is >= 3.0f and < 4.5f ? 4.5f - since : 0f }, cfg)))
                cageHeld++;
        }
        Check("r4-rotation: 3.0-09b the dash is held from the cast's first tick until the last blade line has gone (16.6 s) and not a second longer than the cage window (17.0 s)",
            cageHeld is >= 166 and <= 170, $"{cageHeld / 10f:0.0} s");

        // 3.6-05c Unmooring Melody (48565 jump / 48566 cone): no cast row exists for either (76 jump and 904 cone effects, 0 cast starts in action_events),
        // so TargetCastId is 0 when she lands: nothing to key a reserve on. The dart is the plain gap-close, which fires at the last charge.
        var melody = live with { TargetDistance = 14f, BmrSeen = true, BmrHitIn = float.MaxValue };
        var dart = Decide(melody, cfg);
        Check("r4-rotation: 3.6-05c Unmooring Melody: she has landed at the edge (no cast visible, no hit predicted, clean landing): the gap-close dart goes with the last charge",
            Dashes(dart) && dart.Reason == "shieldcharge:gapclose", D(dart));
        var cones = Decide(melody with { BmrHitIn = 2.0f, BmrHitType = 2, BmrHitOnPlayer = true }, cfg);
        Check("r4-rotation: 3.6-05c ... and while BossMod predicts the cone on the character the dash stays held (existing hit-imminent hold)",
            !Dashes(cones) && cones.Declines.Contains("shieldcharge:hit-imminent"), D(cones));
        Check("r4-rotation: 3.6-05c ... neither id keys a rule: no rule table names 48565 or 48566",
            !RepoFile("Combos", "PvE", "BST", "BST_RotationLogic.cs").Contains("48565", StringComparison.Ordinal)
            && !RepoFile("Combos", "PvE", "BST", "BST_RotationLogic.cs").Contains("48566", StringComparison.Ordinal));
    }

    /// <summary> Seconds of the Deadly Demesne cage window left at <paramref name="sinceCastStart"/>, as the live half feeds it (0 once over). </summary>
    private static float DashCageSecondsFor(float sinceCastStart) =>
        (float)(typeof(BST_RotationLogic).GetField("DashCageSeconds")?.GetValue(null) is float w ? w - sinceCastStart : 0f);
}
