// Fork (1.0.4.277): pure decision core for "out of combat the auto-rotation is
// a passenger" (task tasks-20261004-gluttony-out-of-combat-self-target-overcap-01).
// Deliberately free of Dalamud types so the offline harness
// (tests/GluttonyCombo.OutOfCombatGateHarness) asserts the exact semantics that
// ship; AutoRotationController consults it at the out-of-combat press sites and
// the hard-target writes.

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for the shared out-of-combat gate. While the party is not
///     in combat the auto-rotation must not change the player's target and must
///     not fire a combat-only action: a pull may only start from the player's
///     own input (their target, their button) or from party combat - never from
///     the plugin alone. Deliberately out-of-combat-legal actions keep their
///     behavior: prepull self-buffs (BypassBuffs), BST pre-pull horns toward a
///     target the player chose, heals, raises, cleanses and ground-targeted
///     actions.
/// </summary>
internal static class OutOfCombatGate
{
    /// <summary>
    ///     Whether the auto-rotation may press an action while not in combat.
    ///     An action that can only be aimed at a hostile (attack, gap-close,
    ///     gauge/charge dump at an enemy - the Shield Charge overcap dash of
    ///     the 2026-10-04 report) is combat-only and waits for combat even with
    ///     quest/FATE bypass. Self-usable actions, friendly-only actions and
    ///     ground-targeted actions pass; an action with no target bits in its
    ///     sheet row (unknown to the sheet) is not blocked, preserving the
    ///     pre-gate behavior for unmapped ids.
    ///     <para>
    ///         The 1.0.4.278 amendment adds two inputs; this stub keeps the
    ///         1.0.4.277 semantics (the new flags are not yet consulted) so the
    ///         harness can run red against the shipped behavior first.
    ///     </para>
    /// </summary>
    internal static bool MayFire(bool canTargetHostile, bool canTargetSelf, bool targetArea, bool fromHealPreset = false, bool sanctionedSelfBuff = false) =>
        !(canTargetHostile && !canTargetSelf && !targetArea);

    /// <summary>
    ///     Whether incoming-damage detection (raidwide cast bars, shared-damage
    ///     effects) may report true at all. Shipped 1.0.4.277 behavior ran the
    ///     detection regardless of combat state; this stub pins that behavior
    ///     while the harness runs red for the combat-only reading.
    /// </summary>
    internal static bool MayDetectIncomingDamage(bool inCombat) => true;

    /// <summary>
    ///     Whether the auto-rotation may write the player's hard target this
    ///     tick (DPS/healer hard-target modes, the reflect-penalty self-select,
    ///     the action-penalty clear). Out of combat a target change is pure
    ///     visible noise - the 2026-10-04 report's "I keep getting selected as
    ///     the target for no reason". Presses carry their own explicit target
    ///     id, so this only stops the UI-level retarget, not any action.
    /// </summary>
    internal static bool MayWriteTarget(bool inCombat) => inCombat;
}
