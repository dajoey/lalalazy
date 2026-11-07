// Fork (1.0.4.334): pure decision core for the one-button healer AoE heal HP-average
// trigger. Deliberately free of Dalamud and ECommons types so the offline harness
// (tests/GluttonyCombo.RotationHarness.AST / .WHM) asserts the exact semantics that
// ship; CustomComboFunctions.GetPartyAvgHPPercent(uint) feeds it from the live party
// list. The previous whole-party average counted members the heal could not reach, so
// an out-of-range (or untargetable) member's low HP kept the AoE heal trigger true and
// the one-button kept choosing AoE heals that never healed them.

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
    /// <summary>One party member as the gate sees them: plain data, no game types.</summary>
    internal readonly record struct PartyMember(float HpPercent, bool IsAlive, bool IsTargetable, float DistanceYalms);

    /// <summary>Whether the given member counts toward an AoE heal with this effect range.</summary>
    internal static bool CountsTowardHeal(PartyMember member, float effectRangeYalms) =>
        member.IsAlive && member.IsTargetable && member.DistanceYalms <= effectRangeYalms;

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

            total += member.HpPercent;
            counted++;
        }

        return counted == 0 ? 0f : total / counted;
    }
}
