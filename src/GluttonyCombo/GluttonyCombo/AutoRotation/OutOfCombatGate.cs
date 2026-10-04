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
///     own input (their target, their button), from party combat, or from the
///     user's own out-of-combat attack settings - never from the plugin alone. Deliberately out-of-combat-legal actions keep their
///     behavior: prepull self-buffs (BypassBuffs), BST pre-pull horns toward a
///     target the player chose, heals, raises, cleanses and ground-targeted
///     actions. The 1.0.4.278 amendment: out of combat, a damage preset does
///     not react - heal dumps, shields and regens wait for combat.
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
    ///         1.0.4.278: a friendly-only or self-only action resolved from a
    ///         damage preset also waits for combat (the Kerachole/Druochole
    ///         Addersgall overcap dumps, Eukrasia chains, Physis and shields of
    ///         the 2026-10-04 Sage report) - sheet recast 5s+ separates every
    ///         dump from the sanctioned prepull self-buffs. Heal presets keep
    ///         their need-gated out-of-combat behavior (raises, cleanses,
    ///         actual heals), and the plugin's own BypassBuffs prepull class
    ///         (can use on self, BypassBuffs enabled, recast under 5s: tank
    ///         stances, Eukrasia) keeps firing out of combat.
    ///     </para>
    /// </summary>
    internal static bool MayFire(bool canTargetHostile, bool canTargetSelf, bool targetArea, bool fromHealPreset = false, bool sanctionedSelfBuff = false, bool userAllowsOutOfCombatAttacks = false)
    {
        if (targetArea)
            return true;

        if (sanctionedSelfBuff)
            return true;

        // A hostile-only attack is combat-only unless the user has asked for
        // out-of-combat attacking (UserAllowsOutOfCombatAttacks). Friendly-only and
        // self-only dumps stay combat-only either way.
        if (canTargetHostile && !canTargetSelf)
            return userAllowsOutOfCombatAttacks;

        if (fromHealPreset)
            return true;

        return canTargetSelf && canTargetHostile;
    }

    /// <summary>
    ///     Whether incoming-damage detection (raidwide cast bars, shared-damage
    ///     effects) may report true at all. Incoming damage is a combat
    ///     concept: out of combat no hostile cast bar can threaten the party, so
    ///     GroupDamageIncoming reads false and the whole raidwide-shield chain
    ///     stands down. The 2026-10-04 amendment's per-cast re-derivation found
    ///     every one of the reporting player's own raidwide-shield casts inside
    ///     a combat window - the ungated detection path itself was the fire this
    ///     gate closes.
    /// </summary>
    internal static bool MayDetectIncomingDamage(bool inCombat) => inCombat;

    /// <summary>
    ///     Whether the auto-rotation may write the player's hard target this
    ///     tick (DPS/healer hard-target modes, the reflect-penalty self-select,
    ///     the action-penalty clear). Out of combat a target change is pure
    ///     visible noise - the 2026-10-04 report's "I keep getting selected as
    ///     the target for no reason". Presses carry their own explicit target
    ///     id, so this only stops the UI-level retarget, not any action.
    /// </summary>
    internal static bool MayWriteTarget(bool inCombat, bool userAllowsOutOfCombatAttacks = false) => inCombat || userAllowsOutOfCombatAttacks;

    /// <summary>
    ///     Whether the user's own auto-rotation settings ask for out-of-combat
    ///     attacking: "Restrict to Combat Only" is off and "Prioritise Targets Not
    ///     in Combat" is on (that option only means anything if attacking an enemy
    ///     that is not yet in combat is allowed). Defaults, and anyone who kept the
    ///     combat restriction, keep the passenger behavior. This opens only the
    ///     hostile-only attack press and the DPS hard-target write; friendly dumps,
    ///     shields and raidwide detection stay combat-only regardless.
    /// </summary>
    internal static bool UserAllowsOutOfCombatAttacks(bool inCombatOnly, bool preferNonCombat) => !inCombatOnly && preferNonCombat;
}
