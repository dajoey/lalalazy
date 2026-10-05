// Fork (1.0.4.282): pure decision core for "does an enemy's reflect status stop the
// auto-rotation" (task tasks-20261003-crucible-dispel-270-verdict-01). Deliberately free of
// Dalamud and ECommons types so the offline harness (tests/GluttonyCombo.EnemyReflectStopHarness)
// asserts the exact semantics that ship; AutoRotationController's enemy-reflect stop consults it
// before the Eureka-style freeze (target the player, skip every press).

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for the enemy-reflect stop. A reflect this applies to freezes the whole
///     auto-rotation and targets the player until it clears on every mob: Eureka's Gelid Charge ->
///     Ice Spikes and Static Charge -> Shock Spikes kill an attacker outright, so pressing nothing
///     is the right answer there. The Crucible of the Unbroken's own counter-stance spikes that the
///     dispel lane removes in one cast (Blaze Spikes on the drake, Ice Spikes on the snoll) are NOT
///     stops while Crucible targeting is active: the rotation's stance hold already keeps the
///     attacks off the carrier and the dispel takes the stance off, so the stop would only silence
///     the dispel itself - target the player, skip every press, no dispel, no attack - for the
///     stance's full duration (six 12 s windows with zero presses, 2026-10-04 20:17-20:22, where
///     the unstopped evenings removed the same stance in under a second). Everywhere else every
///     reflect status keeps the full stop.
/// </summary>
internal static class EnemyReflectStop
{
    /// <summary>
    ///     Whether the enemy-reflect stop applies to an enemy carrying <paramref name="statusIds"/>:
    ///     any status in <paramref name="reflects"/> that is not one of
    ///     <paramref name="crucibleDispellableStances"/> while <paramref name="crucibleTargeting"/> is
    ///     active. A null status sequence is not a stop.
    /// </summary>
    /// <param name="statusIds">The enemy's status ids, or null when none are readable.</param>
    /// <param name="reflects">Reflect / counter status ids the stop applies to (name-resolved by the caller).</param>
    /// <param name="crucibleDispellableStances">The Crucible's counter-stance spikes the dispel lane removes in one cast.</param>
    /// <param name="crucibleTargeting">Whether Crucible targeting is active (on a Crucible board with it enabled).</param>
    internal static bool StopsRotation(
        IEnumerable<uint>? statusIds,
        FrozenSet<uint> reflects,
        HashSet<uint> crucibleDispellableStances,
        bool crucibleTargeting)
    {
        if (statusIds is null)
            return false;

        return statusIds.Any(id => reflects.Contains(id)
            && !(crucibleTargeting && crucibleDispellableStances.Contains(id)));
    }
}
