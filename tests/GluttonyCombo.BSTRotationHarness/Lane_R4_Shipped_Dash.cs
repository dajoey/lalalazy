using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

internal static partial class Program
{
    /// <summary>
    ///     The dash placement fix (4679c683e + fa2e884c6, 1.0.4.302; the build before it is 21295dd09 = 1.0.4.301). Shield Charge is an auto-fired dash that lands up to 20 y away and locks the
    ///     character 1.1 s. On 301 the only gates were the landing verdicts, so a dash went out the tick after a boss cast ended (stock BossMod Reborn drops the cast's zone and its predicted hit
    ///     as the cast completes), and while a cast the zone did not cover was running. The fix holds every auto-fired dash for a predicted hit on the character within 3.5 s, a target cast or
    ///     known knockback with 3 s or less left, and 1.5 s after any of them (<c>DashSettleRemaining</c>, fed by the dash clock). The state field the clock fills is set by name
    ///     (<see cref="R4ShippedSet"/>), so this file runs on 301.
    /// </summary>
    private static void R4ShippedDash()
    {
        Console.WriteLine("-- r4-shipped: dash placement before a boss ability --");
        var on = BstSettings.Defaults();
        var protect = on with { UseShieldCharge = false, ShieldChargeOvercap = true };
        // Board 5 battle 6 as in the 2026-10-13 log; Shield Charge ready with three charges, 14 y out, the landing verdict 'safe' (as BMR answered all along).
        var live = BaseState(50) with { CrucibleBoard = 5, CrucibleBattle = 6, TargetNameId = 14657, PlayerHpPercent = 52f, TargetDistance = 14f, DashLanding = DashLanding.Safe };
        live.ReadyShieldCharge = true; live.ShieldChargeCharges = 3; live.ShieldChargeMax = 3;
        bool Dashes(BstDecision d) => d.ActionId == BST.ShieldCharge;
        BstState Settle(BstState s, float seconds) => R4ShippedSet(s, "DashSettleRemaining", seconds);
        string Show(BstDecision d) => $"{d.ActionId}:{d.Reason} [{d.Declines}]";

        // ---- a mechanic about to go off holds the dash (each of these dashed on 301)
        var cast2 = Decide(live with { TargetCastId = 49271, TargetCastRemaining = 2.0f }, on);
        Check("r4-shipped: dash a target cast with 2.0 s left, landing 'safe', 14 y out: no dash, named cast-ending", !Dashes(cast2) && cast2.Declines.Contains("shieldcharge:cast-ending"), Show(cast2));
        Check("r4-shipped: dash ... the GCD chain still goes while the dash waits (the damage floor is never held)", cast2.ActionId != 0 && !Dashes(cast2), Show(cast2));
        var cast3 = Decide(live with { TargetCastId = 49271, TargetCastRemaining = 3.0f }, on);
        Check("r4-shipped: dash a 3 s cast whole (3.0 s left, the lead): no dash", !Dashes(cast3), Show(cast3));
        var hit = Decide(live with { BmrSeen = true, BmrHitIn = 2.0f, BmrHitType = 2, BmrHitOnPlayer = true }, on);
        Check("r4-shipped: dash BMR predicts a hit on the character in 2.0 s, no cast visible: no dash, named hit-imminent", !Dashes(hit) && hit.Declines.Contains("shieldcharge:hit-imminent"), Show(hit));
        var knock = Decide(live with { KnockbackCastRemaining = 2.0f }, on);
        Check("r4-shipped: dash a known knockback cast from any caster, 2.0 s left: no dash, named knockback", !Dashes(knock) && knock.Declines.Contains("shieldcharge:knockback"), Show(knock));

        // ---- just gone off: the clock's settle seconds hold the dash (301: the tick after the cast ended the zone was gone and the dash went out)
        var settling = Decide(Settle(live, 1.1f), on);
        Check("r4-shipped: dash the cast ended 0.4 s ago (1.1 s of settle left), landing 'safe': no dash, named settling", !Dashes(settling) && settling.Declines.Contains("shieldcharge:settling"), Show(settling));
        var settled = Decide(Settle(live, 0f), on);
        Check("r4-shipped: dash ... the settle run out: the dash goes again (held for a moment, not for good)", Dashes(settled) && settled.Reason == "shieldcharge:gapclose", Show(settled));

        // ---- every dash path is held, not only the gap-close
        var melee = live with { TargetDistance = 2f, TargetCastId = 49271, TargetCastRemaining = 0.5f };
        Check("r4-shipped: dash the max-charges spend at melee range is held by a cast about to go off", !Dashes(Decide(melee, on)));
        Check("r4-shipped: dash the overcap-only dash is held by a cast about to go off", !Dashes(Decide(melee with { TargetDistance = 12f }, protect)));
        var fin = BaseState(50) with { CrucibleBoard = 5, CrucibleBattle = 6, TargetDistance = 2f, SunOrMoonActive = true, SunMoon = BeastmasterAffinity.Sunstrider, PlayerTp = 300, ReadyAxe = true, DashLanding = DashLanding.Safe };
        Check("r4-shipped: dash control: the finisher dash with nothing casting goes (Mistral Axe)", Decide(fin, on).ActionId == BST.MistralAxe);
        var finHeld = Decide(fin with { TargetCastId = 49271, TargetCastRemaining = 0.5f }, on);
        Check("r4-shipped: dash the Hawkish Talons finisher (a dash) is held by a cast about to go off", finHeld.ActionId != BST.MistralAxe, Show(finHeld));

        // ---- controls: nothing changes where nothing is going off (these pass on 301 and on the head)
        Check("r4-shipped: dash control: nothing casting, no prediction, clean landing: the dash goes", Dashes(Decide(live, on)));
        Check("r4-shipped: dash control: a cast with 4.5 s left is far enough away: the dash goes", Dashes(Decide(live with { TargetCastId = 49271, TargetCastRemaining = 4.5f }, on)));
        Check("r4-shipped: dash control: a predicted hit on the familiar only does not hold the dash",
            Dashes(Decide(live with { BmrSeen = true, BmrHitIn = 1.0f, BmrHitType = 1, BmrHitOnPlayer = false, BmrHitOnFamiliar = true }, on)));
        var outside = BaseState(50) with { TargetDistance = 12f, TargetCastId = 49271, TargetCastRemaining = 0.2f, DashLanding = DashLanding.Safe };
        outside.ReadyShieldCharge = true; outside.ShieldChargeCharges = 3; outside.ShieldChargeMax = 3;
        Check("r4-shipped: dash control: outside the Crucible a cast does not hold the dash", Dashes(Decide(outside, on)));
        Check("r4-shipped: dash control: the Crucible tactics option off, on a board, a cast about to go off: the dash is not held (option respected)",
            Dashes(Decide(live with { TargetCastId = 49271, TargetCastRemaining = 0.5f }, on with { Crucible = false })));
        Check("r4-shipped: dash control: a landing in danger is still held by its own verdict", Decide(live with { DashLanding = DashLanding.Danger }, on).Declines.Contains("shieldcharge:landing-danger"));
    }
}
