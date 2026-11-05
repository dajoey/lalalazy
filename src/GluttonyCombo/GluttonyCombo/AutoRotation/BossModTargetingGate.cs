// Fork (1.0.4.241): pure decision core for the status-immunity seam the targeting
// paths share. Deliberately free of Dalamud types so the offline harness
// (tests/GluttonyCombo.BossModTargetingHarness) asserts the exact semantics that
// ship; AutoRotationController reuses the status-immunity seam for dropdown,
// AoE, and action-retarget fallback routing.
//
// Fork (Fight Brain 1a): the checkbox half of this file — ShouldUseBossModTarget,
// the "Use boss-mod targeting when active" supersede — is REMOVED. It needed BMR's
// `Hints.PriorityTarget`, which stock BossMod Reborn does not provide, so it could
// never act and only logged on every load.

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     The status-immunity seam the DPS targeting paths share (dropdown, manual AoE,
///     action retargeting): a Crucible damage-immunity status replaces the selected
///     target with the caller's filtered fallback.
/// </summary>
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
}
