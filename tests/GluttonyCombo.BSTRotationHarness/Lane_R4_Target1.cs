using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 targeting lane (task tasks-20261015-crucible-r4-fix-targeting-board2-m1-01). Titles start with "r4-target1:" and name the gap id.
///     Rows: 2.5-06 (the Tablitaur brothers die together: the first kill waits for the other), 2.6-05 (Void Fire III 48222 is a kill-the-caster
///     cast), 2.6-02 (the enemy gorging on Recharge dies first), 4.2-08 (the Seedling Piece is killed on sight). Closed without code, each with a
///     case that pins today's behaviour: 4.3-05 (Queen Hawk eat trick), 2.0-05 (Gobbieboom Barrage), 4.9 kill order (grenade, pyrobolus, bomb).
/// </summary>
internal static partial class Program
{
    private static void R4Target1Lane20261015()
    {
        Console.WriteLine("-- r4-target1: tablitaur pair, Void Fire III, Recharge, seedlings; hawk / Gobbieboom / progenitrix wave closed without code --");
        var cfg = BstSettings.Defaults();
        const uint elder = 14555, younger = 14556, loosefrox = 14561, chewchum = 14562;
        const uint demon = 14557, evilWeapon = 14558, devilet = 14559;
        BST_CrucibleLogic.TargetCandidate T(uint id, float hp = 100f, uint cast = 0, int recharge = 0, bool immune = false) =>
            new(id, hp, false, DamageImmune: immune, CastId: cast, RechargeStacks: recharge);
        List<int> Allowed(params BST_CrucibleLogic.TargetCandidate[] c) => BST_CrucibleLogic.AllowedTargets(c);
        string Show(IEnumerable<int> l) => string.Join(",", l);

        // ---- 2.5-06 Disorienting Groan 48213 + Endless Slashes 48214-48216 (P1). After one brother dies the survivor gains a permanent Damage Up
        // and chains 8 unavoidable hits. The recorded fights (2026-09-25, six of them) lost the first brother with the other 7 to 13 points up
        // (01:54:56 Younger 4 / Elder 13; 02:16:39 Younger 1 / Elder 13) because the 10-point balancing lets the lower one be hit down to zero.
        var close = Allowed(T(elder, 13f), T(younger, 4f));
        Check("r4-target1: 2.5-06 kill zone: the Younger at 4% with the Elder at 13% is left alone, only the Elder is hit",
            close.SequenceEqual(new[] { 0 }), Show(close));
        var mirrored = Allowed(T(younger, 4f), T(elder, 13f));
        Check("r4-target1: 2.5-06 ... the same pair in the other order (the Younger listed first)", mirrored.SequenceEqual(new[] { 1 }), Show(mirrored));
        var elderLow = Allowed(T(elder, 3f), T(younger, 9f));
        Check("r4-target1: 2.5-06 ... and the Elder at 3% with the Younger at 9% the same way: only the Younger is hit",
            elderLow.SequenceEqual(new[] { 1 }), Show(elderLow));
        var near = Allowed(T(elder, 7f), T(younger, 4f));
        Check("r4-target1: 2.5-06 ... within the margin (4% / 7%, 3 points) both stay targetable", near.SequenceEqual(new[] { 0, 1 }), Show(near));
        var edge = Allowed(T(elder, 8f), T(younger, 4f));
        Check("r4-target1: 2.5-06 ... exactly at the margin (4 points) both stay targetable; one point more does not",
            edge.SequenceEqual(new[] { 0, 1 }) && Allowed(T(elder, 8.5f), T(younger, 4f)).SequenceEqual(new[] { 0 }), Show(edge));
        var early = Allowed(T(elder, 68f), T(younger, 60f));
        Check("r4-target1: 2.5-06 outside the kill zone the plain balancing stays: 68% / 60% (8 points) both targetable",
            early.SequenceEqual(new[] { 0, 1 }), Show(early));
        var wide = Allowed(T(elder, 55f), T(younger, 40f));
        Check("r4-target1: 2.5-06 ... and 55% / 40% (15 points) still leaves the lower one alone", wide.SequenceEqual(new[] { 0 }), Show(wide));
        var zoneEdge = Allowed(T(elder, 30f), T(younger, 21f));
        Check("r4-target1: 2.5-06 the zone starts at 20%: 30% / 21% (9 points, lower above the zone) keeps the plain 10-point margin",
            zoneEdge.SequenceEqual(new[] { 0, 1 }), Show(zoneEdge));
        var other = Allowed(T(loosefrox, 13f), T(chewchum, 4f));
        Check("r4-target1: 2.5-06 the Loosefrox / Chewchum pair keeps the plain margin (its kill order is unsettled in the dossier): 13% / 4% both targetable",
            other.SequenceEqual(new[] { 0, 1 }), Show(other));

        // The partner alive but not attackable (the brothers go untargetable in turn: ne=1 for 24 to 28 s in the recorded fights): Decide holds
        // the lower brother instead of landing the first kill, and lets go after PairHoldMaxSeconds with the reason in the decision.
        var pairFight = CrucibleState() with
        {
            CrucibleBoard = 2, CrucibleBattle = 5, HasHostileTarget = true, TargetNameId = younger, TargetHpPercent = 4f, TargetDistance = 3f,
            HighestEnemyHpPercent = 4f, EnemyCount = 1, PairPartnerSeen = true, PairPartnerHpPercent = 13f, PairGuardSeconds = 0f,
        };
        var held = Decide(pairFight, cfg);
        Check("r4-target1: 2.5-06 the Younger at 4%, the Elder alive at 13% and out of reach: the press is held (crucible:hold-pair-final-blow)",
            held is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-pair-final-blow" }, $"{held.ActionId}:{held.Reason} [{held.Declines}]");
        var late = Decide(pairFight with { PairGuardSeconds = BST_CrucibleLogic.PairHoldMaxSeconds - 0.1f }, cfg);
        Check("r4-target1: 2.5-06 ... still held 0.1 s before the limit", late is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-pair-final-blow" },
            $"{late.ActionId}:{late.Reason}");
        var released = Decide(pairFight with { PairGuardSeconds = BST_CrucibleLogic.PairHoldMaxSeconds }, cfg);
        Check("r4-target1: 2.5-06 ... released at the limit: the rotation attacks and the decision says why (crucible:pair-final-blow-timeout)",
            released.ActionId != BST_CrucibleLogic.Hold && released.Declines.Contains("crucible:pair-final-blow-timeout"), $"{released.ActionId}:{released.Reason} [{released.Declines}]");
        Check("r4-target1: 2.5-06 the limit is under the Standard bar's 15 s idle hold", BST_CrucibleLogic.PairHoldMaxSeconds is > 0f and < 15f, $"{BST_CrucibleLogic.PairHoldMaxSeconds} s");
        Check("r4-target1: 2.5-06 no hold when the target is the healthier brother (Elder 13%, Younger 4%)",
            Decide(pairFight with { TargetNameId = elder, TargetHpPercent = 13f, PairPartnerHpPercent = 4f }, cfg).ActionId != BST_CrucibleLogic.Hold);
        Check("r4-target1: 2.5-06 no hold when the partner is within the margin (4% / 7%)",
            Decide(pairFight with { PairPartnerHpPercent = 7f }, cfg).ActionId != BST_CrucibleLogic.Hold);
        Check("r4-target1: 2.5-06 no hold when the partner is gone (the first kill already happened)",
            Decide(pairFight with { PairPartnerSeen = false }, cfg).ActionId != BST_CrucibleLogic.Hold);
        Check("r4-target1: 2.5-06 no hold above the kill zone (target 30%, partner 45%)",
            Decide(pairFight with { TargetHpPercent = 30f, PairPartnerHpPercent = 45f }, cfg).ActionId != BST_CrucibleLogic.Hold);
        Check("r4-target1: 2.5-06 no hold on the Loosefrox / Chewchum pair (kill order unsettled): Loosefrox at 4%, Chewchum at 13%",
            Decide(pairFight with { CrucibleBattle = 0, TargetNameId = loosefrox, PairPartnerSeen = true }, cfg).ActionId != BST_CrucibleLogic.Hold);

        // The clock: runs while the risk holds without a break, restarts after one.
        long since = 0;
        var t0 = BST_CrucibleLogic.TrackPairGuard(true, ref since, 10_000);
        var t1 = BST_CrucibleLogic.TrackPairGuard(true, ref since, 12_500);
        var t2 = BST_CrucibleLogic.TrackPairGuard(false, ref since, 13_000);
        var t3 = BST_CrucibleLogic.TrackPairGuard(true, ref since, 14_000);
        Check("r4-target1: 2.5-06 the guard clock counts 0 s, 2.5 s, stops on a break and restarts from 0",
            t0 == 0f && Math.Abs(t1 - 2.5f) < 0.01f && t2 == 0f && since == 14_000 && t3 == 0f, $"{t0} {t1} {t2} {t3} since={since}");

        // Never a stall: one brother untargetable for 28 s (the longest recorded span), the visible one at 18% with the partner at 25%. Ticks of
        // 0.5 s; the rotation takes 0.4 points a tick when it attacks. The longest run of held ticks is the hold the rule can produce.
        {
            var s = pairFight with { TargetHpPercent = 18f, PairPartnerHpPercent = 25f, HighestEnemyHpPercent = 18f };
            long sinceMs = 0, now = 1_000_000;
            float hp = 18f, worst = 0f, run = 0f, heldTotal = 0f;
            var timedOutSeen = false;
            for (var tick = 0; tick < 56; tick++, now += 500)
            {
                var risk = BST_CrucibleLogic.PairFinalBlowRisk(s with { TargetHpPercent = hp });
                var clock = BST_CrucibleLogic.TrackPairGuard(risk, ref sinceMs, now);
                var d = Decide(s with { TargetHpPercent = hp, PairGuardSeconds = clock }, cfg);
                if (d.ActionId == BST_CrucibleLogic.Hold && d.Reason == "crucible:hold-pair-final-blow")
                {
                    run += 0.5f;
                    heldTotal += 0.5f;
                    worst = Math.Max(worst, run);
                }
                else
                {
                    run = 0f;
                    hp = Math.Max(0f, hp - 0.4f);
                    timedOutSeen |= d.Declines.Contains("crucible:pair-final-blow-timeout");
                }
            }

            Check("r4-target1: 2.5-06 28 s with the partner out of reach: the longest hold is at most the limit, under 15 s, and the fight goes on after it",
                worst <= BST_CrucibleLogic.PairHoldMaxSeconds + 0.01f && worst > 0f && hp < 18f && timedOutSeen, $"longest {worst} s, total {heldTotal} s, hp {hp}");
            Check("r4-target1: 2.5-06 ... one hold for the whole span: it does not re-arm while the risk lasts (total = longest)",
                Math.Abs(heldTotal - worst) < 0.01f, $"total {heldTotal} s, longest {worst} s");
        }

        // ---- 2.6-05 Void Fire III 48222: a 10 s room-wide cast. The Devilet is a priority add, but the priority adds are unordered, so a Devilet mid-cast
        // was treated like the Evil Weapon beside it (13 casts in the one recorded run, the Devilet never killed in time).
        Check("r4-target1: 2.6-05 Void Fire III (48222) is a kill-the-caster cast", BST_CrucibleData.KillTheCaster.Contains(48222));
        var vf = Allowed(T(evilWeapon), T(devilet, 100f, 48222));
        Check("r4-target1: 2.6-05 the Devilet mid-cast is the only target, ahead of the Evil Weapon", vf.SequenceEqual(new[] { 1 }), Show(vf));
        var vfMirror = Allowed(T(devilet, 100f, 48222), T(evilWeapon), T(demon));
        Check("r4-target1: 2.6-05 ... in any list order, the Demon beside them too", vfMirror.SequenceEqual(new[] { 0 }), Show(vfMirror));
        var vfIdle = Allowed(T(evilWeapon), T(devilet));
        Check("r4-target1: 2.6-05 control: the Devilet between casts is one of the priority adds again", vfIdle.SequenceEqual(new[] { 0, 1 }), Show(vfIdle));
        var vfOther = Allowed(T(evilWeapon, 100f, 48220), T(devilet, 100f, 48222));
        Check("r4-target1: 2.6-05 ... another enemy's ordinary cast does not take the aim from the Void Fire III caster", vfOther.SequenceEqual(new[] { 1 }), Show(vfOther));

        // ---- 2.6-02 Recharge 5406 + Dark Eruption 48217: one enemy at a time gorges (a stack every 2 s, Damage Up on the fifth). The recorded run: Demon
        // 18:32:21-29, Evil Weapon 18:32:44-52, Devilet 18:33:22-30, four stacks each, then the permanent Damage Up.
        Check("r4-target1: 2.6-02 the Recharge status is 5406", BST_CrucibleData.RechargeStatus == 5406u);
        var gorge = Allowed(T(evilWeapon), T(demon, 100f, recharge: 3), T(devilet));
        Check("r4-target1: 2.6-02 the Demon with 3 Recharge stacks is the only target (it is not even a priority add)", gorge.SequenceEqual(new[] { 1 }), Show(gorge));
        var most = Allowed(T(demon, 100f, recharge: 2), T(evilWeapon, 100f, recharge: 4));
        Check("r4-target1: 2.6-02 two gorging: the one with more stacks first", most.SequenceEqual(new[] { 1 }), Show(most));
        var none = Allowed(T(demon), T(evilWeapon), T(devilet));
        Check("r4-target1: 2.6-02 control: no stacks, the two priority adds are the targets", none.SequenceEqual(new[] { 1, 2 }), Show(none));
        var immune = Allowed(T(demon, 100f, recharge: 3, immune: true), T(evilWeapon));
        Check("r4-target1: 2.6-02 a damage-immune gorger is still never aimed at", immune.SequenceEqual(new[] { 1 }), Show(immune));
        var cast = Allowed(T(demon, 100f, recharge: 3), T(devilet, 100f, 48222));
        Check("r4-target1: 2.6-02 a Void Fire III caster still comes before the gorger (the 10 s cast would land)", cast.SequenceEqual(new[] { 1 }), Show(cast));

        // ---- 4.2-08 Seedling Pieces (14600, Morbol fight): "kill them, the poison is not worth leaving up". No add rule existed.
        const uint morbol = 14599, seedling = 14600;
        var seeds = Allowed(T(morbol), T(seedling));
        Check("r4-target1: 4.2-08 the Seedling is killed ahead of the Morbol", seeds.SequenceEqual(new[] { 1 }), Show(seeds));
        var seedsFirst = Allowed(T(seedling), T(morbol), T(seedling, 40f));
        Check("r4-target1: 4.2-08 ... several seedlings are all targetable, the boss not", seedsFirst.SequenceEqual(new[] { 0, 2 }), Show(seedsFirst));
        var bossAlone = Allowed(T(morbol));
        Check("r4-target1: 4.2-08 control: with no seedling up the Morbol is hit", bossAlone.SequenceEqual(new[] { 0 }), Show(bossAlone));
        Check("r4-target1: 4.2-08 the 4.2 kill order is not touched: the seedling is in no documented order",
            !BST_CrucibleData.PriorityAddOrder.SelectMany(w => w).SelectMany(t => t).Contains(seedling));

        // ---- 4.3-05 Queen Hawk eat trick: closed without code. The trick is the player's positioning (the hawk on the line to the flower); the plugin
        // needs 17 to 24 s to take a hawk from 100% to 0 (22 recorded windows), so killing it on sight never beat Final Sting's 5 s cast, and the flower
        // already ate it in 13 of those 22. A gate that keeps it alive lengthens every hawk's life with no hand on the cone (16 character deaths on record).
        const uint hawk = 14605, flower = 14603;
        var hawks = Allowed(T(flower), T(hawk));
        Check("r4-target1: 4.3-05 closed without code: the Queen Hawk stays a priority add, killed on sight ahead of the Corpse Flower",
            hawks.SequenceEqual(new[] { 1 }) && BST_CrucibleData.PriorityAdds.Contains(hawk), Show(hawks));
        Check("r4-target1: 4.3-05 ... and Final Sting (48689) stays a registered tankbuster the cover rules answer", BST_CrucibleData.Tankbusters.Contains(48689));

        // ---- 2.0-05 Gobbieboom Barrage 48234/50446: closed without code. The game gives no stun on Chewchum in any log (22 + 24 casts, 11 runs), the
        // bomb's push needs its position against the pool, which the plugin does not read, and a 12 y blast beside the party is the cost of a wrong hit.
        const uint bomb = 14563;
        var bombField = Allowed(T(loosefrox), T(chewchum), T(bomb));
        Check("r4-target1: 2.0-05 closed without code: the Gobbie Bomb (14563) is no priority add; the brothers' pair is the only thing the order narrows",
            !BST_CrucibleData.PriorityAdds.Contains(bomb) && !BST_CrucibleData.FastKillAdds.Contains(bomb) && bombField.SequenceEqual(new[] { 0, 1, 2 }), Show(bombField));

        // ---- 4.9 kill order (grenade, pyrobolus, bomb): closed without code. The plugin already ranks the progenitrix wave from BossMod Reborn's
        // ProgenitrixPiece priorities (5 / 4 / 3); the dossier's only source for "Pyrobolus last" (S7) names no pyrobolus rank (cross-check: partial).
        const uint grenade = 14624, pyrobolus = 14627, bomb49 = 14625;
        var wave = Allowed(T(bomb49), T(pyrobolus), T(grenade));
        Check("r4-target1: 4.9 kill order closed without code: the wave already dies grenade first", wave.SequenceEqual(new[] { 2 }), Show(wave));
        var rest = Allowed(T(bomb49), T(pyrobolus));
        Check("r4-target1: 4.9 ... then the pyrobolus, then the bombs (BossMod Reborn 5 / 4 / 3)", rest.SequenceEqual(new[] { 1 }), Show(rest));
    }
}
