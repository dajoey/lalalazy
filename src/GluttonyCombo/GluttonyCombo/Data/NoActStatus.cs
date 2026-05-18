using ECommons.GameHelpers;
using System.Collections.Generic;

namespace GluttonyCombo.Data;

/// <summary>
/// Detects "any action triggers damage / wipe" debuffs (Pyretic, Acceleration
/// Bomb, etc.). Used by autorotation and the UseAction hook to suppress every
/// action while one of these statuses is active, so the plugin can't kill the
/// player by firing the next combo step during the mechanic.
/// </summary>
/// <remarks>
/// Background: AutoDuty's own Pyretic handling pauses its queue but a queued
/// action can still be in flight when the status lands, and Gluttony's combo
/// replacement intercepts every UseAction call from any source. Without this
/// gate, a single in-flight action during Pyretic kills the player. Gating at
/// both <see cref="GluttonyCombo.AutoRotation.AutoRotationController.ShouldSkipAutorotation"/>
/// and <see cref="GluttonyCombo.Data.ActionWatching.UseActionDetour"/> blocks
/// every code path that could fire an action.
/// </remarks>
internal static class NoActStatus
{
    /// <summary>
    /// Status IDs whose presence on the player makes "any action triggers
    /// damage or wipe." Curated, conservative list — add IDs as encounters
    /// are observed in the wild rather than guessing.
    /// </summary>
    private static readonly HashSet<uint> Ids = new()
    {
        960,    // Pyretic — every action triggers fire damage
        2127,   // Acceleration Bomb (Endwalker / Dawntrail variant)
        1387,   // Acceleration Bomb (older variant)
    };

    /// <summary>True if the local player currently has a "no action" status.</summary>
    public static bool Active()
    {
        var p = Player.Object;
        if (p is null) return false;
        foreach (var s in p.StatusList)
        {
            if (Ids.Contains(s.StatusId)) return true;
        }
        return false;
    }
}
