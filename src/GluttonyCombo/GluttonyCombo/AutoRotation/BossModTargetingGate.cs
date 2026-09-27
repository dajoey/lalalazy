// Fork (1.0.4.241): pure decision core for the "Use boss-mod targeting when
// active" checkbox. Deliberately free of Dalamud types so the offline harness
// (tests/GluttonyCombo.BossModTargetingHarness) asserts the exact semantics that
// ship; GetSingleTarget in AutoRotationController is the only runtime caller.

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Decision core for routing the DPS single-target choice to BossMod Reborn's
///     fight-aware targeting (task tasks-20260927-gluttony-bmr-targeting-checkbox-01).
/// </summary>
/// <remarks>
///     <para>
///         Inputs at the seam in <c>AutoRotationHelper.GetSingleTarget</c>:
///         <c>bossModTargetId</c> is BossModReborn's <c>BossMod.Hints.PriorityTarget</c>
///         IPC answer — a non-zero actor id only when BMR has an active module that
///         expresses an attackable opinion (forced target or the head of the priority
///         ordering BMR's own AI selects from). BMR absent, no active module, module
///         without opinion, and any IPC failure all surface as 0.
///     </para>
///     <para>
///         <c>targetUsable</c> is the resolved actor passed through the standard
///         <c>DPSTargeting.Query</c> enemy filter, so a boss-mod opinion pointing at a
///         dead, untargetable or out-of-range actor falls back to the dropdown instead
///         of stalling the rotation.
///     </para>
/// </remarks>
internal static class BossModTargetingGate
{
    /// <summary>
    ///     Whether this tick's DPS single-target choice should be the boss-mod target.
    ///     Checkbox off never overrides (byte-identical to pre-checkbox behavior);
    ///     checkbox on overrides only on a non-zero id whose actor is usable.
    /// </summary>
    internal static bool ShouldUseBossModTarget(bool checkboxOn, ulong bossModTargetId, bool targetUsable)
        => checkboxOn && bossModTargetId != 0 && targetUsable;
}
