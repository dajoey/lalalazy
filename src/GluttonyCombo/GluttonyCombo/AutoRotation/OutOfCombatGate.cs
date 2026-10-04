// Fork (1.0.4.277): pure decision core for "out of combat the auto-rotation is
// a passenger" (task tasks-20261004-gluttony-out-of-combat-self-target-overcap-01).
// Deliberately free of Dalamud types so the offline harness
// (tests/GluttonyCombo.OutOfCombatGateHarness) asserts the exact semantics that
// ship; AutoRotationController consults it at the out-of-combat press sites and
// the hard-target writes.

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for the shared out-of-combat gate: while the party is not
///     in combat the auto-rotation must not change the player's target and must
///     not fire a combat-only action. A pull may only start from the player's
///     own input (their target, their button) or from party combat - never from
///     the plugin alone.
/// </summary>
internal static class OutOfCombatGate
{
    // STUB (failing-first): keeps the shipped behavior - everything may fire out
    // of combat, every target write allowed - so the harness runs RED against
    // the target semantics. The fix commit replaces these bodies.
    internal static bool MayFire(bool canTargetHostile, bool canTargetSelf, bool targetArea) => true;

    internal static bool MayWriteTarget(bool inCombat) => true;
}
