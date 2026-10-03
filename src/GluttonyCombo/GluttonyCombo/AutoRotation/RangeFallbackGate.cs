// Fork (1.0.4.258): pure decision core for "the chosen enemy is out of range of the chosen action
// while another valid enemy is in range". Deliberately free of Dalamud types so the offline
// harness (tests/GluttonyCombo.RangeFallbackHarness) asserts the exact semantics that ship.

using System.Collections.Generic;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     ExecuteST / ExecuteAoE choose the enemy first (the hard target in Manual mode, the mode's pick,
///     the Crucible kill-order head, or the boss-mod target) and then range-check the resolved action
///     against that one enemy. Unreachable (out of range, no line of sight, not usable on it) used to mean
///     "no action": the GCD idled with another valid enemy standing in reach (observed live 2026-10-03,
///     Second Master's Board: 362 of 451 s idle while a priority add was the selected target). This gate keeps
///     the chosen enemy preferred and redirects only that tick's action when it cannot reach it; the hard
///     target is never changed.
/// </summary>
internal static class RangeFallbackGate
{
    /// <summary> One enemy the auto-rotation may hit instead of the unreachable choice. </summary>
    /// <param name="Reachable"> The action is in range of the enemy (line of sight included) and can be used on it. </param>
    /// <param name="KillOrder"> The enemy is in the narrowed list the targeting mode / Crucible kill order allows. </param>
    internal readonly record struct Candidate<T>(T Enemy, bool Reachable, float Distance, bool KillOrder)
        where T : class;

    /// <summary>
    ///     Whether this tick's action should be redirected. Only hostile single-target style actions are:
    ///     self-usable, ground-targeted and friendly-only actions never are, and neither is a missing target.
    /// </summary>
    internal static bool NeedsFallback(
        bool enabled,
        bool hasTarget,
        bool actionTargetsHostile,
        bool canUseSelf,
        bool areaTargeted,
        bool targetReachable) =>
        enabled
        && hasTarget
        && actionTargetsHostile
        && !canUseSelf
        && !areaTargeted
        && !targetReachable;

    /// <summary>
    ///     The enemy that takes the hit instead: of those the action can reach and use (never the selection itself),
    ///     kill-order members first, then the nearest; the first listed on a tie, so the choice never flaps.
    ///     Null when nothing is in reach (the rotation keeps waiting for the chosen enemy).
    /// </summary>
    internal static T? PickInRange<T>(T? selected, IEnumerable<Candidate<T>> candidates)
        where T : class
    {
        Candidate<T>? best = null;
        foreach (var c in candidates)
        {
            if (!c.Reachable || ReferenceEquals(c.Enemy, selected))
                continue;

            if (best is not { } b
                || (c.KillOrder && !b.KillOrder)
                || (c.KillOrder == b.KillOrder && c.Distance < b.Distance))
                best = c;
        }

        return best?.Enemy;
    }
}
