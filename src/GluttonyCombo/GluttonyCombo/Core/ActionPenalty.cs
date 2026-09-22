using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.Core;

/// <summary>
///     The Dalamud-free decision table for "does the player have a status that must block actions".
/// </summary>
/// <remarks>
///     <para>
///         <c>Player.Status</c> (ECommons <c>Player</c>) is <c>Object?.StatusList ?? default!</c> -
///         null whenever there is no local player (login screen, character select, territory
///         transition). Calling <c>Enumerable.Any</c> on that null source throws
///         <c>ArgumentNullException</c> out of <c>UseActionDetour</c>, which still fires while the
///         player object is gone (fingerprint 3072e17f8875, 4 hits in 26 h on 1.0.4.230).
///     </para>
///     <para>
///         Kept free of every Dalamud and ECommons type on purpose: the caller in
///         <c>CustomCombo/Functions/Status.cs</c> does the game-state gathering (including the
///         null guard), this answers the question, and
///         <c>tests/GluttonyCombo.ActionPenaltyHarness</c> compiles THIS file and asserts the
///         whole truth table offline. Fork-owned, so upstream WrathCombo merges do not touch it.
///     </para>
/// </remarks>
internal static class ActionPenalty
{
    /// <summary>
    ///     Returns true when any of the player's statuses is an action penalty: an Acceleration
    ///     Bomb inside the threshold, any Pyretic, or a Misc pause inside the threshold.
    ///     A null status sequence (no local player) is NOT a penalty - it returns false instead
    ///     of throwing.
    /// </summary>
    /// <param name="statuses">The player's (StatusId, RemainingTime) pairs, or null.</param>
    /// <param name="threshold">Seconds of remaining time that still count for timed penalties.</param>
    /// <param name="accelerationBombs">Status IDs treated as Acceleration Bombs.</param>
    /// <param name="pyretics">Status IDs treated as Pyretic (no time gate).</param>
    /// <param name="misc">Other pause status IDs, gated on remaining time like bombs.</param>
    internal static bool HasPenalty(
        IEnumerable<(uint StatusId, float RemainingTime)>? statuses,
        float threshold,
        FrozenSet<uint> accelerationBombs,
        FrozenSet<uint> pyretics,
        FrozenSet<uint> misc)
    {
        if (statuses is null)
            return false;

        return statuses.Any(s =>
            (accelerationBombs.Contains(s.StatusId) && s.RemainingTime <= threshold) ||
            pyretics.Contains(s.StatusId) ||
            (misc.Contains(s.StatusId) && s.RemainingTime <= threshold));
    }
}
