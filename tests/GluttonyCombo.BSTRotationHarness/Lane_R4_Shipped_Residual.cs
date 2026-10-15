using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

internal static partial class Program
{
    /// <summary>
    ///     The open residual rows of the four shipped fixes (entry check "Entry check shipped fixes", rows R1 R4 R5 R6 R7 R8 R10 R11 and the gap rows kd-05b, sv-03). These cases need the
    ///     extractions this task makes (<see cref="BST_CrucibleLogic.CountsAsOtherEnemy"/>, <see cref="BST_RotationLogic.FeedDashClock"/>), so they run on the integration head only; the four fix files
    ///     are the ones that run on the build before each fix. Nothing here changes what a shipped rule decides.
    /// </summary>
    private static void R4ShippedResidual()
    {
        Console.WriteLine("-- r4-shipped: residual rows R1 R4 R5 R7 R8 R10 R11 kd-05b sv-03 --");
        var cfg = BstSettings.Defaults();
        R4ShippedR1(cfg);
        R4ShippedR4R5();
        R4ShippedDispelRows(cfg);
        R4ShippedR10R11(cfg);
    }


    /// <summary> A file of this harness project, found by walking up from the output directory to the repo root; empty when not found. </summary>
    private static string R4ShippedHarnessFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "check-preset-ids.py")))
            dir = dir.Parent;
        var path = dir is null ? "" : Path.Combine(dir.FullName, "tests", "GluttonyCombo.BSTRotationHarness", name);
        return File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
    }

    // ================================================================== R1 and kd-05b: the Abaddon alone at the HP floor
    private static void R4ShippedR1(BstSettings cfg)
    {
        const uint abaddon = 14655;
        // A model of the ticks (0.25 s) from the moment the Abaddon is the only enemy up with a Morpho alive: its HP falls from 100 to 0 over killSeconds of the character's
        // damage (assumed linear: an assumption, the recorded HP is not monotonic because the Abaddon heals as it eats Morphos), and the Abaddon is led to a Morpho
        // and eats the last one at morphoEatenAt seconds (movement is BossMod Reborn's / AutoDuty's, never the plugin's: dossier plugin row 5.5/m6). Every tick asks the shipped rule.
        (double Longest, double Held, double FirstHeldHp, double MaxHeldHp) Model(double killSeconds, double morphoEatenAt, bool shipped = true)
        {
            double longest = 0, run = 0, held = 0, firstHeldHp = -1, maxHeldHp = 0;
            for (var t = 0.0; t < 200.0; t += 0.25)
            {
                var hp = (float)Math.Max(0.0, 100.0 * (1.0 - t / killSeconds));
                var morphoAlive = t < morphoEatenAt;
                var forbidden = shipped
                    ? BST_CrucibleLogic.ConditionallyForbidden(abaddon, 0, morphoAlive, 0, false, hp, false)
                    : morphoAlive; // the 1.0.4.304 rule, for the comparison column
                if (forbidden)
                {
                    held += 0.25; run += 0.25; longest = Math.Max(longest, run);
                    if (firstHeldHp < 0) firstHeldHp = hp;
                    maxHeldHp = Math.Max(maxHeldHp, hp);
                }
                else
                {
                    run = 0;
                }
            }
            return (longest, held, firstHeldHp, maxHeldHp);
        }

        // The recorded lead: 83.5 s from the Abaddon alone (20:54:57) to the last Morpho gone (20:56:20), 10-14.
        const double recorded = 83.5;
        var rows = new List<string>();
        var allAtFloor = true;
        foreach (var kill in new[] { 20.0, 30.0, 45.0, 60.0 })
        {
            var floorAt = kill * (1.0 - BST_CrucibleLogic.AbaddonHeldFloorPercent / 100.0); // seconds until HP reaches the 35 percent floor
            var m = Model(kill, recorded);
            var old = Model(kill, recorded, shipped: false);
            allAtFloor &= m.MaxHeldHp <= BST_CrucibleLogic.AbaddonHeldFloorPercent + 0.01 && Math.Abs(m.Longest - Math.Max(0, recorded - floorAt)) <= 0.5;
            rows.Add($"damage kills it in {kill:0} s: floor reached at {floorAt:0.0} s, longest hold {m.Longest:0.0} s (304 rule: {old.Longest:0.0} s)");
        }
        Check("r4-shipped: R1 kd-05b the Abaddon alone with a Morpho alive: held only at or below the 35 percent floor, and the hold lasts from the floor to the last Morpho (recorded lead 83.5 s)", allAtFloor, string.Join("; ", rows));
        Console.WriteLine("   r4-shipped: R1 longest floor hold for the recorded 83.5 s lead: " + string.Join(" | ", rows));
        Check("r4-shipped: R1 kd-05b a Morpho eaten before the floor is reached: no hold at all", Model(30.0, 5.0).Held == 0 && Model(30.0, 19.0).Held == 0);
        Check("r4-shipped: R1 kd-05b the 304 rule held the whole lead (83.5 s) whatever the damage; the shipped rule never holds above the floor",
            Math.Abs(Model(30.0, recorded, shipped: false).Longest - recorded) <= 0.5 && Model(30.0, recorded).FirstHeldHp <= BST_CrucibleLogic.AbaddonHeldFloorPercent + 0.01);
        var withDrake = Enumerable.Range(0, 11).Select(i => (float)(i * 10)).All(hp => BST_CrucibleLogic.ConditionallyForbidden(abaddon, 0, true, 0, false, hp, true));
        Check("r4-shipped: R1 kd-05b the Drake or a Barbmole up with a Morpho alive: held at every HP (Abaddon last), so the floor hold is only the 'alone' case", withDrake);
    }

    // ================================================================== R4 and R5: the collector's OtherEnemiesUp
    /// <summary> The inline test the collector used before the extraction, copied verbatim from BST_Crucible.cs at a60f1ec77 (lines 466 and 489-503): the filter, the do-not-attack table, then the Abaddon-name test. </summary>
    private static bool R4ShippedOldInline(uint nameId, bool dead, bool targetable, uint currentHp, bool hostile)
    {
        if (dead || !targetable || currentHp == 0 || !hostile)
            return false;                                                   // `continue` at BST_Crucible.cs:466
        if (BST_CrucibleData.DoNotAttack.TryGetValue(nameId, out _))
            return false;                                                   // `continue` at BST_Crucible.cs:489-499
        var otherEnemiesUp = false;
        if (nameId != BST_CrucibleData.AbaddonNameId)                       // BST_Crucible.cs:502-503
            otherEnemiesUp = true;
        return otherEnemiesUp;
    }

    private static void R4ShippedR4R5()
    {
        const uint abaddon = 14655, drake = 14651, barbmole = 14653, morpho = 14656, zuEgg = 14575, hourglass = 14689, unknown = 99999;
        var names = new[] { abaddon, drake, barbmole, morpho, zuEgg, hourglass, unknown };
        var mismatches = new List<string>();
        var total = 0;
        foreach (var name in names)
            foreach (var dead in new[] { false, true })
                foreach (var targetable in new[] { false, true })
                    foreach (var hp in new[] { 0u, 1u, 40_000u })
                        foreach (var hostile in new[] { false, true })
                        {
                            total++;
                            var now = BST_CrucibleLogic.CountsAsOtherEnemy(name, dead, targetable, hp, hostile);
                            var was = R4ShippedOldInline(name, dead, targetable, hp, hostile);
                            if (now != was)
                                mismatches.Add($"{name} dead={dead} targetable={targetable} hp={hp} hostile={hostile}: now {now}, was {was}");
                        }
        Check($"r4-shipped: R4 the extracted collector test returns what the old inline loop returned on all {total} combinations of name, dead, targetable, HP and hostile", mismatches.Count == 0, string.Join("; ", mismatches.Take(3)));

        Check("r4-shipped: R4 the Abaddon itself is never 'another enemy'", !BST_CrucibleLogic.CountsAsOtherEnemy(abaddon, false, true, 40_000, true));
        Check("r4-shipped: R4 a living, targetable, hostile Drake or Barbmole is", BST_CrucibleLogic.CountsAsOtherEnemy(drake, false, true, 40_000, true) && BST_CrucibleLogic.CountsAsOtherEnemy(barbmole, false, true, 40_000, true));
        Check("r4-shipped: R4 a dead one is not", !BST_CrucibleLogic.CountsAsOtherEnemy(drake, true, true, 0, true));
        Check("r4-shipped: R4 an untargetable one is not (the Drake mid-phase)", !BST_CrucibleLogic.CountsAsOtherEnemy(drake, false, false, 40_000, true));
        Check("r4-shipped: R4 a non-hostile one is not (a party member's pet)", !BST_CrucibleLogic.CountsAsOtherEnemy(drake, false, true, 40_000, false));
        Check("r4-shipped: R4 one with 0 HP is not", !BST_CrucibleLogic.CountsAsOtherEnemy(drake, false, true, 0, true));
        Check("r4-shipped: R4 a Morpho and an egg (the do-not-attack table) are not", !BST_CrucibleLogic.CountsAsOtherEnemy(morpho, false, true, 40_000, true) && !BST_CrucibleLogic.CountsAsOtherEnemy(zuEgg, false, true, 40_000, true));

        // The collector's output feeds the rule: the whole chain from the objects on the field to the Abaddon's attackability, Morpho alive.
        bool AbaddonAttackable((uint Name, bool Dead, bool Targetable, uint Hp, bool Hostile)[] field, float abaddonHp = 100f)
        {
            var others = field.Any(o => BST_CrucibleLogic.CountsAsOtherEnemy(o.Name, o.Dead, o.Targetable, o.Hp, o.Hostile));
            return !BST_CrucibleLogic.ConditionallyForbidden(abaddon, 0, true, 0, false, abaddonHp, others);
        }
        var a = (abaddon, false, true, 90_000u, true);
        var m = (morpho, false, true, 5_000u, true);
        Check("r4-shipped: R4 chain: the Abaddon and a Morpho only: attackable at 100% (the 304 idle hold)", AbaddonAttackable([a, m]));
        Check("r4-shipped: R4 chain: ... with the Drake alive beside them: held (Abaddon last)", !AbaddonAttackable([a, m, (drake, false, true, 40_000u, true)]));
        Check("r4-shipped: R4 chain: ... with the Drake dead, or untargetable, or not hostile: attackable again",
            AbaddonAttackable([a, m, (drake, true, true, 0u, true)]) && AbaddonAttackable([a, m, (drake, false, false, 40_000u, true)]) && AbaddonAttackable([a, m, (drake, false, true, 40_000u, false)]));
        Check("r4-shipped: R4 chain: ... with a Barbmole alive: held; the floor still holds the Abaddon alone", !AbaddonAttackable([a, m, (barbmole, false, true, 30_000u, true)]) && !AbaddonAttackable([a, m], 30f));

        // R4 wiring (source text, the reader cannot run offline): the collector calls the extracted predicate and the inline test is gone.
        var reader = RepoFile("Combos", "PvE", "BST", "BST_Crucible.cs");
        Check("r4-shipped: R4 the live collector computes OtherEnemiesUp through BST_CrucibleLogic.CountsAsOtherEnemy with the object's own fields",
            reader.Length > 0 && reader.Contains("BST_CrucibleLogic.CountsAsOtherEnemy(nameId, npc.IsDead, npc.IsTargetable, npc.CurrentHp, npc.IsHostile())", StringComparison.Ordinal)
            && !System.Text.RegularExpressions.Regex.IsMatch(reader, @"if \(nameId != BST_CrucibleData\.AbaddonNameId\)\s*otherEnemiesUp = true;"));

        // R5: leaving a board resets both flags together.
        Check("r4-shipped: R5 leaving a board resets OtherEnemiesUp where it resets MorphoAlive",
            System.Text.RegularExpressions.Regex.IsMatch(reader, @"MorphoAlive = false;\s*OtherEnemiesUp = false;"));
    }

    // ================================================================== R7, R8, sv-03: the dispel rows
    private static void R4ShippedDispelRows(BstSettings cfg)
    {
        const uint drake = 14651, barbmole = 14653, abaddon = 14655;
        const uint blazeSpikes = 5465, regen = 989;
        var drakeBuff = BST_CrucibleData.DispellableBuffs.Contains(blazeSpikes);

        // R7: the old case set TargetHasDispellableBuff by hand on the Abaddon's Regen. The flag a carrier raises is derived from the table; Regen raises none.
        Check("r4-shipped: R7 the flag the rotation reads off a Regen (989) carrier is false, off a Blaze Spikes (5465) carrier true: derived from DispellableBuffs, not set by hand",
            !BST_CrucibleData.DispellableBuffs.Contains(regen) && drakeBuff);
        var src = R4ShippedHarnessFile("Program.cs");
        Check("r4-shipped: R7 the dispel-class case no longer sets the flag by hand on a case named for Regen",
            src.Length > 0 && !src.Contains("Abaddon with Regen on the target, Quelling Wave held: dispel it", StringComparison.Ordinal)
            && !src.Contains("TargetHasDispellableBuff = true, EnemyHasDispellableBuff = true,\n            };\n            Check(\"Abaddon", StringComparison.Ordinal)
            && src.Contains("Quelling Wave held, a Blaze Spikes (5465) carrier targeted: dispel it", StringComparison.Ordinal));

        // The dispel states: the Abaddon piece (5.5), the Drake targeted and in Blaze Spikes (a stance), Wave Kinship held and Quelling Wave ready.
        var live = CrucibleState(50) with { SinceSummon = 12f, ReadyParting = false, SinceHornPress = 30f };
        var spikes = live with
        {
            CrucibleBoard = 5, CrucibleBattle = 5, CrucibleNeeds = CrucibleNeeds.Dispel, EnemyCount = 3, HasHostileTarget = true, TargetNameId = drake, TargetInStance = true,
            Slot1Beast = 1, Slot2Beast = 8, Slot3Beast = 4, ActiveSlot = 1, PetObjectBeast = 1, GcdReady = true, TargetDistance = 5f,
            KinshipHeld = true, BeastModeResolved = BST.QuellingWave, ReadyBeastMode = true,
            TargetHasDispellableBuff = drakeBuff, EnemyHasDispellableBuff = drakeBuff,
        };
        string Show(BstDecision d) => $"{d.ActionId} {d.Reason} [{d.Declines}]";

        // sv-03: Blaze Spikes up, a Wavekin on hand: the dispel goes before the stance hold.
        var wave = Decide(spikes, cfg);
        Check("r4-shipped: sv-03 Blaze Spikes up on the Drake, Wave Kinship held and Quelling Wave ready: the dispel goes at the Drake before the stance hold", wave is { ActionId: BST.QuellingWave, Reason: "crucible:dispel-quellingwave" }, Show(wave));
        // ... with a Wavekin horn ready instead of a held Kinship: the horn comes first (the answer swap), the dispel the tick after.
        var horn = Decide(spikes with { KinshipHeld = false, BeastModeResolved = 0, ReadyBeastMode = false, ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = true, EnemyHasDispellableBuff = true, TargetHasDispellableBuff = false }, cfg);
        Check("r4-shipped: sv-03 ... no Kinship held but a Wavekin horn ready: the horn is blown to bring the Wavekin out", horn.Reason.StartsWith("crucible:answer-dispel-slot", StringComparison.Ordinal), Show(horn));

        // R8: why a Blaze Spikes add (12.0 s) ran out in 11 of 17 recorded cases, by cause, with what the plugin does on the head. See the page for the 17 adds.
        const double spikesSeconds = 12.0;
        var idle = Decide(spikes with { KinshipHeld = false, BeastModeResolved = 0, ReadyBeastMode = false, ReadyHorn1 = false, ReadyHorn2 = false, ReadyHorn3 = false }, cfg);
        Check("r4-shipped: R8 no Wave Kinship and no horn to answer with: the dispel is unavailable and the stance hold stands for as long as the spikes (12.0 s), under the Standard bar's 15 s",
            idle.Reason == "crucible:hold-stance" && idle.Declines.Contains("crucible:dispel-unavailable") && spikesSeconds < 15.0, Show(idle));
        var recast = Decide(spikes with { ReadyBeastMode = false }, cfg);
        Check("r4-shipped: R8 Kinship held but Quelling Wave on recast: not armed, the stance hold stands until it is ready (the dispel then goes)",
            recast.Reason == "crucible:hold-stance" && !BST_CrucibleLogic.DispelArmed(spikes with { ReadyBeastMode = false }, cfg) && Decide(spikes with { ReadyBeastMode = true }, cfg).ActionId == BST.QuellingWave, Show(recast));
        var far = Decide(spikes with { TargetDistance = 31f }, cfg);
        Check("r4-shipped: R8 the Drake out of Quelling Wave's 30 y reach: no dispel goes (declined as unavailable), the stance hold stands",
            far.ActionId != BST.QuellingWave && far.Declines.Contains("crucible:dispel-unavailable"), Show(far));
        var gcd = Decide(spikes with { GcdReady = false }, cfg);
        Check("r4-shipped: R8 the GCD still rolling: the Wave waits for the next GCD tick, never longer than a GCD", gcd.ActionId != BST.QuellingWave && Decide(spikes, cfg).ActionId == BST.QuellingWave, Show(gcd));
        // The character on another target (adds 3 and 4: it fought the Barbmole through both spikes): armed, the aim goes to the carrier.
        List<int> Aim(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c, false, BST_CrucibleLogic.DispelArmed(spikes, cfg));
        Check("r4-shipped: R8 the character on the Barbmole (a priority add) while the Drake carries Blaze Spikes: armed, the aim moves to the Drake",
            Aim(R4ShippedCandidate(barbmole, avoid: false), R4ShippedCandidate(drake, avoid: true, dispellable: true, proven: true)).SequenceEqual(new[] { 1 }));
        Check("r4-shipped: R8 ... and not armed (no Kinship), the Barbmole stays the aim and the Drake in its stance is left alone",
            BST_CrucibleLogic.AllowedTargets([R4ShippedCandidate(barbmole), R4ShippedCandidate(drake, avoid: true, dispellable: true, proven: true)], false, BST_CrucibleLogic.DispelArmed(spikes with { KinshipHeld = false }, cfg)).SequenceEqual(new[] { 0 }));
        Check("r4-shipped: R8 a Regen (989) carrier, the Abaddon, never takes the aim from the Barbmole priority add (it is not a dispel carrier)",
            BST_CrucibleLogic.AllowedTargets([R4ShippedCandidate(barbmole), R4ShippedCandidate(abaddon, dispellable: BST_CrucibleData.DispellableBuffs.Contains(regen))], false, true).SequenceEqual(new[] { 0 }));
    }

    // ================================================================== R10 and R11: the dash clock feed and the long cast
    private static void R4ShippedR10R11(BstSettings cfg)
    {
        // A tick the way ReadState builds it: the state read so far, then the clock fed from it with the same call BST.cs makes.
        var live = BaseState(50) with { CrucibleBoard = 5, CrucibleBattle = 6, TargetNameId = 14657, PlayerHpPercent = 52f, TargetDistance = 14.3f };
        live.ReadyShieldCharge = true; live.ShieldChargeCharges = 3; live.ShieldChargeMax = 3;
        bool Dashes(BstDecision d) => d.ActionId == BST.ShieldCharge;
        var clock = new DashHazardClock();
        BstState Tick(double t, BstState s)
        {
            FeedDashClock(ref clock, ref s, t);
            return s;
        }
        string Show(BstDecision d) => $"{d.ActionId} {d.Reason} [{d.Declines}]";

        // 2026-10-13 19:29:24-26 (the log the clock was built from): a cast ends, BMR's zone and hit prediction vanish with it, the verdict flips to safe.
        var t1 = Tick(24.470, live with { TargetCastId = 49273, TargetCastRemaining = 1.5f, BmrSeen = true, BmrHitIn = 3.4f, BmrHitOnPlayer = true, DashLanding = DashLanding.Safe });
        Check("r4-shipped: R10 the feed fills DashSettleRemaining from the state read: a cast with 1.5 s left raises the full 1.5 s", Math.Abs(t1.DashSettleRemaining - DashSettleSeconds) < 1e-4, t1.DashSettleRemaining.ToString("0.000"));
        var t2 = Tick(25.396, live with { TargetCastId = 0, TargetCastRemaining = 0f, BmrSeen = true, BmrHitIn = float.MaxValue, BmrHitOnPlayer = false, DashLanding = DashLanding.Safe });
        var d2 = Decide(t2, cfg);
        Check("r4-shipped: R10 the tick the cast ended (0.93 s later, landing 'safe'): the settle left is 0.57 s and the dash waits, named settling", Math.Abs(t2.DashSettleRemaining - 0.574f) < 0.01f && !Dashes(d2) && d2.Declines.Contains("shieldcharge:settling"), $"{t2.DashSettleRemaining:0.000} {Show(d2)}");
        Check("r4-shipped: R10 ... the label the SG| line carries for that tick is the same string (DashMechanicHold)", DashMechanicHold(t2, true) == "settling" && DashMechanicHold(t2, false) is null);
        var t3 = Tick(26.60, live with { DashLanding = DashLanding.Safe });
        Check("r4-shipped: R10 1.6 s after the last hazard the settle is spent and the dash goes (held for a moment, not for good)", t3.DashSettleRemaining == 0f && Dashes(Decide(t3, cfg)), Show(Decide(t3, cfg)));
        clock.Reset();
        _ = Tick(56.861, live with { DashLanding = DashLanding.Danger });
        var t4 = Tick(57.606, live with { DashLanding = DashLanding.Safe });
        Check("r4-shipped: R10 a Danger landing feeds the clock too: safe 0.75 s after danger still holds 0.75 s", Math.Abs(t4.DashSettleRemaining - 0.755f) < 0.01f && !Dashes(Decide(t4, cfg)), $"{t4.DashSettleRemaining:0.000}");
        clock.Reset();
        var off = Tick(1.0, live with { CrucibleBoard = 0, TargetCastId = 49271, TargetCastRemaining = 0.5f, DashLanding = DashLanding.Danger });
        Check("r4-shipped: R10 off a Crucible board nothing feeds the clock", off.DashSettleRemaining == 0f);

        // Source text: the order ReadState runs in. The feed comes after the state it reads (landing, Crucible read, knockback cast) and before the SG| label.
        var bst = RepoFile("Combos", "PvE", "BST", "BST.cs");
        int At(string needle) => bst.IndexOf(needle, StringComparison.Ordinal);
        var landing = At("s.DashLanding = ReadDashLanding(");
        var crucible = At("ReadCrucible(ref s);");
        var knock = At("s.KnockbackCastRemaining = ");
        var feed = At("BST_RotationLogic.FeedDashClock(ref _dashHazardClock, ref s, Environment.TickCount64 / 1000.0);");
        var label = At("s.DashHold = BST_DashSafety.HoldLabel(");
        Check("r4-shipped: R10 ReadState feeds the clock through FeedDashClock after the landing, the Crucible read and the knockback read, and before the SG| label",
            landing > 0 && crucible > landing && knock > crucible && feed > knock && label > feed, $"landing {landing} crucible {crucible} knock {knock} feed {feed} label {label}");
        Check("r4-shipped: R10 the SG| label takes the same mechanic hold as the rotation (DashMechanicHold with the Crucible option)",
            bst.Contains("mechanicHold: BST_RotationLogic.DashMechanicHold(s, BST_Crucible && s.CrucibleBoard != 0)", StringComparison.Ordinal));

        // R11: a cast with more than DashCastLeadSeconds left does not hold the dash. 2026-10-14 21:12:42.746 ET (Lauda the Spellcleaver, Overpowering Point 50846, a 6.6 s cast):
        // CR| c=50846:4.0, dec=44893:shieldcharge:gapclose, d=3.6 (a short gap-close), the character's HP 87 before and after. Verdict: correct as designed. The hold covers the last
        // 3 s of a cast (the 1.1 s dash lock ends 1.9 s before a 3 s cast would); with 4.0 s left the dash resolves 2.9 s before the cast ends and BMR's zone for the running cast is
        // already part of the landing verdict.
        var cast = live with { TargetNameId = 14693, TargetDistance = 3.6f, TargetCastId = 50846, DashLanding = DashLanding.Safe };
        clock.Reset();
        var four = Decide(Tick(42.746, cast with { TargetCastRemaining = 4.0f }), cfg);
        Check("r4-shipped: R11 a cast with 4.0 s left (the 21:12:42 dash): the dash goes, as recorded (no hold for a cast over the 3 s lead)", Dashes(four) && four.Reason == "shieldcharge:gapclose", Show(four));
        clock.Reset();
        var edge3 = Decide(Tick(1.0, cast with { TargetCastRemaining = 3.0f }), cfg);
        clock.Reset();
        var edge31 = Decide(Tick(1.0, cast with { TargetCastRemaining = 3.1f }), cfg);
        Check("r4-shipped: R11 the lead is exactly 3.0 s: held at 3.0 s left, goes at 3.1 s left", !Dashes(edge3) && Dashes(edge31) && DashCastLeadSeconds == 3f, $"3.0: {Show(edge3)} | 3.1: {Show(edge31)}");
        clock.Reset();
        var landingDanger = Decide(Tick(1.0, cast with { TargetCastRemaining = 4.0f, DashLanding = DashLanding.Danger }), cfg);
        Check("r4-shipped: R11 a 4.0 s cast whose zone BMR already shows (landing in danger): the dash is held by that verdict", !Dashes(landingDanger) && landingDanger.Declines.Contains("shieldcharge:landing-danger"), Show(landingDanger));
    }
}
