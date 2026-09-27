// Fork (1.0.4.241): pure decision core for the "Use boss-mod targeting when
// active" checkbox. Deliberately free of Dalamud types so the offline harness
// (tests/GluttonyCombo.BossModTargetingHarness) asserts the exact semantics that
// ship; AutoRotationController also reuses the status-immunity seam for dropdown,
// AoE, and action-retarget fallback routing.

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
///         <c>DPSTargeting.Query</c> enemy filter. <c>targetDamageImmune</c> adds
///         encounter-specific immunity statuses that Dalamud's native actor flags do
///         not expose. Either failure falls back to the dropdown instead of stalling.
///     </para>
/// </remarks>
internal static class BossModTargetingGate
{
    /// <summary>
    ///     Applies the status-based damage-immunity guard after the caller's normal
    ///     target-presence/usability checks.
    /// </summary>
    internal static bool IsTargetUsable(bool baseUsable, bool statusDamageImmune) =>
        baseUsable && !statusDamageImmune;

    /// <summary>
    ///     Replaces a selected status-immune target with a caller-provided, already
    ///     filtered fallback. A missing selection remains missing so Manual mode's
    ///     no-target behavior is unchanged.
    /// </summary>
    internal static T? ResolveStatusImmuneTarget<T>(
        T? selectedTarget,
        bool selectedDamageImmune,
        T? fallbackTarget)
        where T : class =>
        selectedTarget is not null && selectedDamageImmune
            ? fallbackTarget
            : selectedTarget;

    /// <summary>
    ///     Whether this tick's DPS single-target choice should be the boss-mod target.
    ///     Checkbox off never overrides (byte-identical to pre-checkbox behavior). Checkbox
    ///     on overrides only on a non-zero id whose actor is usable and can take damage;
    ///     encounter-specific status immunity is separate from Dalamud's actor flags.
    /// </summary>
    internal static bool ShouldUseBossModTarget(
        bool checkboxOn,
        ulong bossModTargetId,
        bool targetUsable,
        bool targetDamageImmune = false)
        => checkboxOn
        && bossModTargetId != 0
        && IsTargetUsable(targetUsable, targetDamageImmune);
}
