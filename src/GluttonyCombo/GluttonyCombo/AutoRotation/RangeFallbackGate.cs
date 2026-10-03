// Fork (1.0.4.258): pure decision core for "the chosen enemy is out of range of the chosen action
// while another valid enemy is in range". Deliberately free of Dalamud types so the offline
// harness (tests/GluttonyCombo.RangeFallbackHarness) asserts the exact semantics that ship.

namespace GluttonyCombo.AutoRotation;

internal static class RangeFallbackGate
{
    /// <summary> One enemy the auto-rotation may hit instead of the out-of-range choice. </summary>
    internal readonly record struct Candidate<T>(T Enemy, bool InActionRange, float Distance, bool KillOrder)
        where T : class;

    internal static bool NeedsFallback(
        bool enabled,
        bool hasTarget,
        bool actionTargetsHostile,
        bool canUseSelf,
        bool areaTargeted,
        bool targetInActionRange) => false;

    internal static T? PickInRange<T>(T? selected, IEnumerable<Candidate<T>> candidates)
        where T : class => null;
}
