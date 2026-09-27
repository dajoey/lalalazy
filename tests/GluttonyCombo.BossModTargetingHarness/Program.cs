// Failing-first cases for the "Use boss-mod targeting when active" checkbox
// (GluttonyCombo 1.0.4.241, task tasks-20260927-gluttony-bmr-targeting-checkbox-01).
//
// Semantics under test (Joey, 2026-09-27T03:55:25Z): "Can we set a checkbox to
// supercede the normal autotargeting in gluttony. it would have it use the BMR
// autotargeting instead and then fall back to the dropdown if there is no active
// boss mod."
//
// The four states, expressed at the decision seam GetSingleTarget consults:
//   1. ON  + active module + opinion          -> boss-mod target wins
//   2. ON  + active module + NO opinion       -> dropdown fallback
//   3. ON  + NO active module                 -> dropdown fallback
//   4. OFF (module + opinion live or not)     -> byte-identical to pre-checkbox
// plus the stall guard: ON + opinion that resolves to an unusable target (dead,
// untargetable, out of range) -> dropdown fallback, never a dead-air rotation.
//
// States 2 and 3 arrive at this seam as the same input (endpoint id 0) BY DESIGN:
// BossModReborn's Hints.PriorityTarget endpoint reports 0 both when no module is
// active and when the active module expresses no attackable opinion. Both cases
// are asserted separately to pin that collapse: any future change that makes one
// of them override would fail exactly one of these two cases.

using GluttonyCombo.AutoRotation;

var failures = 0;

void Check(string name, bool expected, bool actual)
{
    var pass = expected == actual;
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: expected {expected}, got {actual}");
}

void CheckEqual<T>(string name, T expected, T actual)
{
    var pass = EqualityComparer<T>.Default.Equals(expected, actual);
    if (!pass) failures++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}: expected {expected}, got {actual}");
}

// 1. ON + active module + opinion -> the boss-mod target must win.
Check("on+module+opinion overrides",
    expected: true,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0x1001234, targetUsable: true));

// 2. ON + active module + NO opinion (endpoint id 0) -> dropdown fallback.
Check("on+module+no-opinion falls back",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0, targetUsable: false));

// 3. ON + NO active module (endpoint id 0) -> dropdown fallback. Distinct scenario
//    from case 2 upstream; must stay a fallback here too.
Check("on+no-module falls back",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0, targetUsable: false));

// 4. OFF -> never overrides, even with a live module opinion available. This is
//    the "checkbox OFF = byte-identical behavior" guarantee.
Check("off never overrides",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: false, bossModTargetId: 0x1001234, targetUsable: true));

// Stall guard: ON + opinion whose actor fails the standard enemy filter
// (dead / untargetable / out of range / LOS) -> dropdown fallback, not a
// rotation locked onto an unusable target.
Check("on+unusable-target falls back (no stall)",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0x1001234, targetUsable: false));

// Regression (.241 in-game verdict): a status-based damage immunity can coexist
// with IsTargetable and the standard enemy checks. It must still reject BMR's
// opinion so normal targeting can choose an attackable enemy.
Check("on+status-immune target falls back",
    expected: false,
    actual: BossModTargetingGate.ShouldUseBossModTarget(
        checkboxOn: true, bossModTargetId: 0x1001234, targetUsable: true,
        targetDamageImmune: true));

// The dropdown result itself is guarded too: direct modes such as Manual and
// Tank Target bypass the candidate list, so an immune result must be rejected.
Check("direct dropdown target with status immunity is rejected",
    expected: false,
    actual: BossModTargetingGate.IsTargetUsable(
        baseUsable: true, statusDamageImmune: true));
Check("direct dropdown attackable target is retained",
    expected: true,
    actual: BossModTargetingGate.IsTargetUsable(
        baseUsable: true, statusDamageImmune: false));

// All eight DPS dropdown values converge on this resolution seam after their
// tank/non-tank switch has selected an actor.
var dropdownModes = new[]
{
    "Manual", "Highest_Max", "Lowest_Max", "Highest_Current",
    "Lowest_Current", "Tank_Target", "Nearest", "Furthest",
};
foreach (var mode in dropdownModes)
    CheckEqual($"direct {mode} immune target selects attackable fallback",
        expected: "sahagin",
        actual: BossModTargetingGate.ResolveStatusImmuneTarget(
            selectedTarget: "ymir",
            selectedDamageImmune: true,
            fallbackTarget: "sahagin"));

CheckEqual("direct immune target with no attackable fallback returns none",
    expected: null,
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "ymir",
        selectedDamageImmune: true,
        fallbackTarget: null as string));
CheckEqual("outside-Crucible/direct attackable selection is unchanged",
    expected: "ymir",
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "ymir",
        selectedDamageImmune: false,
        fallbackTarget: "sahagin"));
CheckEqual("manual AoE (AoEIgnoreManual off) immune center selects filtered auto-target",
    expected: "sahagin",
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "ymir",
        selectedDamageImmune: true,
        fallbackTarget: "sahagin"));
CheckEqual("manual AoE (AoEIgnoreManual on) keeps filtered auto-target",
    expected: "sahagin",
    actual: BossModTargetingGate.ResolveStatusImmuneTarget(
        selectedTarget: "sahagin",
        selectedDamageImmune: false,
        fallbackTarget: "sahagin"));

if (failures > 0)
{
    Console.WriteLine($"{failures} case(s) FAILED");
    return 1;
}

Console.WriteLine("OK");
return 0;
