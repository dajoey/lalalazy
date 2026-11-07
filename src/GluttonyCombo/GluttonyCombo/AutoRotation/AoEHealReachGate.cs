// Fork (1.0.4.334): pure decision core for the one-button healer AoE heal HP-average
// trigger. Deliberately free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.RotationHarness.AST / .WHM) asserts the exact semantics that
// ship; CustomComboFunctions.GetPartyAvgHPPercent(uint) feeds it from the live party
// list. The previous whole-party average counted members the heal could not reach, so
// an out-of-range (or untargetable) member's low HP kept the AoE heal trigger true and
// the one-button kept choosing AoE heals that never healed them.
//
// Fork (1.0.4.335): the same trigger family also had no notion of "near-full is full".
// Reported live (2026-11-07, PotD floors 51+): with the auto-rotation leasing heals out
// of combat, members reading 97.9-99% (regen-tick oscillation, display rounding — bars
// that look full) kept the average and the auto-rotation's member count under the
// threshold, so Helios-family heals went out back to back against a party that was
// effectively full. The near-full rule below makes only a REAL deficit count.

using System.Collections.Generic;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for the one-button healer AoE heal trigger: which party members
///     count toward the heal's HP-average gate. A member counts only when the AoE heal
///     can actually reach them — alive, targetable, and within the action's effect
///     range of the caster (edge distance: center-to-center minus the member's hitbox
///     radius, matching TargetInSelfCircle's radius + hitbox reach).
/// </summary>
internal static class AoEHealReachGate
{
    /// <summary>
    ///     Fork (1.0.4.335): a health bar at or above this percentage is FULL for AoE-heal
    ///     purposes. A 2% sliver is regen/rounding territory — invisible on the party frame
    ///     and cheaper to leave to a HoT tick than a whole AoE GCD. Members below it carry a
    ///     real deficit and count normally.
    /// </summary>
    internal const float NearFullPercent = 98f;

    /// <summary>One party member as the gate sees them: plain data, no game types.</summary>
    internal readonly record struct PartyMember(float HpPercent, bool IsAlive, bool IsTargetable, float DistanceYalms);

    /// <summary>Whether the given member counts toward an AoE heal with this effect range.</summary>
    internal static bool CountsTowardHeal(PartyMember member, float effectRangeYalms) =>
        member.IsAlive && member.IsTargetable && member.DistanceYalms <= effectRangeYalms;

    /// <summary>
    ///     Fork (1.0.4.335): whether an HP reading is a real deficit for an AoE heal — at or
    ///     below the configured threshold AND not a near-full bar. This is the count-gate
    ///     twin of the average's near-full clamp: a threshold configured at or above
    ///     <see cref="NearFullPercent"/> can no longer make a full party count, so the
    ///     auto-rotation's AoE trigger ("enough reachable members actually below the
    ///     threshold") can never pass on a party that is at or near full HP.
    /// </summary>
    internal static bool IsActuallyBelowThreshold(float hpPercent, float thresholdPercent) =>
        hpPercent <= thresholdPercent && hpPercent < NearFullPercent;

    /// <summary>
    ///     Fork (1.0.4.335): a member's contribution to the AoE-heal average. A near-full
    ///     member contributes a full bar, so rounding-level slivers cannot drag the average
    ///     under the threshold and keep it there while regen tops them off.
    /// </summary>
    internal static float EffectiveContribution(float hpPercent) =>
        hpPercent >= NearFullPercent ? 100f : hpPercent;

    /// <summary>
    ///     The average HP across the members the AoE heal can reach. An empty reachable
    ///     set returns 0, matching the legacy whole-party average's empty-party answer.
    /// </summary>
    internal static float AverageHPPercent(IEnumerable<PartyMember> members, float effectRangeYalms)
    {
        float total = 0f;
        int counted = 0;
        foreach (var member in members)
        {
            if (!CountsTowardHeal(member, effectRangeYalms))
                continue;

            total += EffectiveContribution(member.HpPercent);
            counted++;
        }

        return counted == 0 ? 0f : total / counted;
    }
}
