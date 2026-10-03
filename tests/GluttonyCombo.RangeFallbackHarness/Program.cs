// Failing-first cases for the out-of-range hard target defect (GluttonyCombo 1.0.4.258,
// task tasks-20261003-coeurl-crucible-targeting-review-01).
//
// Joey, 2026-10-03T16:26:26Z, verbatim: "I just watched Gluttony stand next to one target it could
// do damage to - not doing damage to it b/c the hard target was out of range. wasting DPS uptime."
//
// Live mechanism (AutoRotationController.ExecuteST / ExecuteAoE): the enemy is chosen first (the hard
// target in Manual mode, the mode's pick otherwise, the Crucible kill-order head, or the boss-mod
// target), then the resolved action is range-checked against THAT enemy alone. Out of range means
// "return false": no action, the GCD rolls idle, even with another valid enemy 3 y away.
//
// Semantics under test: the chosen enemy stays the preferred target; only when the resolved action
// cannot reach it, the best enemy that CAN be reached takes the hit (kill-order members first,
// then nearest), and nothing else changes (self / ground / friendly actions are never redirected,
// the option off is byte-identical to before, no in-range enemy still means no action).

using GluttonyCombo.AutoRotation;

var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail.Length == 0 ? "" : "  [" + detail + "]")}");
}

// A named enemy stand-in: the gate is generic over any reference type.
var hard = "hard target (boss, 30 y)";
var near = "enemy at 3 y";
var nearKill = "kill-order add at 5 y";
var nearPlain = "plain enemy at 2 y";
var far = "second far enemy at 28 y";

RangeFallbackGate.Candidate<string> C(string e, bool inRange, float d, bool kill = false) => new(e, inRange, d, kill);

// ---- 1. the observed case: hard target 30 y away, another enemy standing at 3 y
Check("hard target 30 y, enemy at 3 y: the near enemy takes the hit",
    RangeFallbackGate.PickInRange(hard, new[] { C(hard, false, 30f), C(near, true, 3f) }) == near);

Check("the fallback is wanted when the resolved melee action cannot reach the hard target",
    RangeFallbackGate.NeedsFallback(enabled: true, hasTarget: true, actionTargetsHostile: true,
        canUseSelf: false, areaTargeted: false, targetInActionRange: false));

// ---- 2. in range: nothing changes
Check("target in range: no fallback",
    !RangeFallbackGate.NeedsFallback(true, true, true, false, false, targetInActionRange: true));

// ---- 3. the option off is byte-identical to the old behaviour
Check("option off: no fallback",
    !RangeFallbackGate.NeedsFallback(enabled: false, true, true, false, false, false));

// ---- 4. only hostile single-target style actions are ever redirected
Check("no target at all: nothing to fall back from",
    !RangeFallbackGate.NeedsFallback(true, hasTarget: false, true, false, false, false));
Check("friendly-only action (phantom cure): never redirected to an enemy",
    !RangeFallbackGate.NeedsFallback(true, true, actionTargetsHostile: false, false, false, false));
Check("self-usable action: never redirected",
    !RangeFallbackGate.NeedsFallback(true, true, true, canUseSelf: true, false, false));
Check("ground-targeted action: never redirected",
    !RangeFallbackGate.NeedsFallback(true, true, true, false, areaTargeted: true, false));

// ---- 5. which enemy takes the hit
Check("kill-order member in range beats a nearer plain enemy",
    RangeFallbackGate.PickInRange(hard, new[] { C(hard, false, 30f), C(nearPlain, true, 2f), C(nearKill, true, 5f, kill: true) }) == nearKill);
Check("no kill-order member in range: nearest in-range enemy",
    RangeFallbackGate.PickInRange(hard, new[] { C(hard, false, 30f), C(far, false, 28f), C(nearPlain, true, 2f), C(near, true, 3f) }) == nearPlain);
Check("a kill-order member that is itself out of range is not chosen over an in-range plain enemy",
    RangeFallbackGate.PickInRange(hard, new[] { C(hard, false, 30f, kill: true), C(near, true, 3f) }) == near);
Check("two in-range enemies at equal distance: the first listed (stable, no flapping)",
    RangeFallbackGate.PickInRange(hard, new[] { C(near, true, 3f), C(nearPlain, true, 3f) }) == near);

// ---- 6. no in-range enemy: no invented target
Check("nothing in range: null (the rotation keeps waiting for the hard target)",
    RangeFallbackGate.PickInRange(hard, new[] { C(hard, false, 30f), C(far, false, 28f) }) == null);
Check("empty candidate list: null",
    RangeFallbackGate.PickInRange(hard, Array.Empty<RangeFallbackGate.Candidate<string>>()) == null);

// ---- 7. the selected enemy is never its own fallback
Check("the out-of-range selection itself is never returned",
    RangeFallbackGate.PickInRange(hard, new[] { C(hard, true, 1f) }) == null);

Console.WriteLine(failures == 0 ? "OK" : $"FAILED ({failures})");
return failures == 0 ? 0 : 1;
