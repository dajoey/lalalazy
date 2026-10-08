using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3, Part A, item 1. The pure HP trigger of the generic PvP defensives, ported from the unreleased
///     round-2 branch (<c>DefensiveTrigger</c>, commit cc83d8cf). T1: the <see cref="DefensiveTrigger.ShouldUse"/>
///     truth table (off never fires, on fires strictly below the threshold only; boundary and NaN rows) with a
///     reference model, the old behaviour shown wrong, and canaries that must fail.
/// </summary>
internal static class DefensiveTriggerTruth
{
    // Reference for the table: strictly below, never when off, never for NaN.
    private static bool Reference(bool enabled, float ratio, float threshold) =>
        enabled && !float.IsNaN(ratio) && !float.IsNaN(threshold) && ratio < threshold;

    private static readonly float[] Ratios =
        [0f, 0.01f, 0.25f, 0.4999f, 0.5f, 0.5001f, 0.75f, 0.9999f, 1f, 1.5f, -0.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity];

    private static readonly float[] Thresholds = [0f, 0.25f, 0.5f, 0.75f, 1f, float.NaN];

    private static IEnumerable<(bool On, float Ratio, float Threshold)> Rows() =>
        from on in new[] { false, true } from r in Ratios from t in Thresholds select (on, r, t);

    private static string[] Mismatches(Func<bool, float, float, bool> impl) =>
        Rows().Where(r => impl(r.On, r.Ratio, r.Threshold) != Reference(r.On, r.Ratio, r.Threshold))
            .Select(r => $"on={r.On},ratio={r.Ratio},threshold={r.Threshold}").ToArray();

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part A, item 1 DefensiveTrigger (T1) --");

        var rows = Rows().ToArray();
        var bad = Mismatches(DefensiveTrigger.ShouldUse);
        Harness.Case("ShouldUse equals the truth table over on/off x 14 ratios x 6 thresholds", bad.Length == 0,
            bad.Length == 0 ? $"{rows.Length} rows" : $"{bad.Length} differ, first: {bad[0]}");

        var offFires = rows.Where(r => !r.On && DefensiveTrigger.ShouldUse(r.On, r.Ratio, r.Threshold)).ToArray();
        Harness.Case("setting off: never fires, whatever the HP and threshold (default behaviour unchanged)", offFires.Length == 0,
            $"{rows.Count(r => !r.On)} off rows, {offFires.Length} fire");

        Harness.Case("on: strictly below the threshold fires", DefensiveTrigger.ShouldUse(true, 0.4999f, 0.5f) && DefensiveTrigger.ShouldUse(true, 0f, 0.5f));
        Harness.Case("on: exactly at the threshold does not fire (boundary)", !DefensiveTrigger.ShouldUse(true, 0.5f, 0.5f));
        Harness.Case("on: above the threshold does not fire", !DefensiveTrigger.ShouldUse(true, 0.5001f, 0.5f) && !DefensiveTrigger.ShouldUse(true, 1f, 0.5f));
        Harness.Case("on: threshold 0 never fires and threshold 100 percent fires below full HP only",
            !DefensiveTrigger.ShouldUse(true, 0f, 0f) && DefensiveTrigger.ShouldUse(true, 0.9999f, 1f) && !DefensiveTrigger.ShouldUse(true, 1f, 1f));
        Harness.Case("on: NaN health ratio (no player) and NaN threshold never fire",
            !DefensiveTrigger.ShouldUse(true, float.NaN, 0.5f) && !DefensiveTrigger.ShouldUse(true, 0.1f, float.NaN) && !DefensiveTrigger.ShouldUse(true, float.NaN, float.NaN));

        // RED against the old behaviour: before the generic table nothing in the base Emergency stage used these
        // defensives by HP, i.e. the old decision is "never", which the table contradicts in every on-row below its
        // threshold.
        var oldNever = Mismatches((_, _, _) => false);
        var expectedFires = rows.Count(r => Reference(r.On, r.Ratio, r.Threshold));
        Harness.Case("RED on old behaviour: the old Emergency stage (never uses the defensive by HP) disagrees with the table exactly where it must fire",
            oldNever.Length == expectedFires && expectedFires > 0 && Reference(true, 0.4999f, 0.5f),
            $"{oldNever.Length} rows where the setting is on and the HP is below the threshold, e.g. on=True,ratio=0.4999,threshold=0.5");

        // Canaries (false by construction): an always-on mutant, a <= mutant and a mutant that is always off.
        Harness.Canary("a trigger that ignores the setting satisfies the table", Mismatches((_, r, t) => r < t).Length == 0);
        Harness.Canary("a <= trigger satisfies the table", Mismatches((on, r, t) => on && r <= t).Length == 0);
        Harness.Canary("a trigger that is always off satisfies the table", Mismatches((_, _, _) => false).Length == 0);
    }
}
