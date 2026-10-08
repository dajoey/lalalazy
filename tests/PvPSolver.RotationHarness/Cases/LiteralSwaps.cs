using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Round 3, Part A, item 7: the six fixed HP numbers that became settings by literal swap (Heart of Corundum 30,
///     Riddle of Earth 0.8, Lady of Crowns 0.6, Microcosmos 0.6, Meisui 0.5, Impalement 60). T2 per branch: the
///     method keeps exactly the conditions it had at origin/main 4d6d0e9f in the same order (same position), every
///     one unchanged except the swapped ones, each swapped condition has an Enabled test in front and, with the
///     Enabled test removed and the accessor read put back to the old literal, is character for character the old
///     condition (so the comparison operator and every other condition are unchanged), and the number is read from
///     the row accessor. T1: the new expression at the row default agrees with the old literal comparison for every
///     float within 200 steps of the threshold and over a grid of the whole range. Canaries: the old text, a changed
///     operator, a missing Enabled test and a changed default.
/// </summary>
internal static class LiteralSwaps
{
    private const string Table = "RotationSolver.Decisions.DefensiveTable.";

    private sealed record Swap(int Index, string Row, string Accessor, string Literal);

    private sealed record Fixture(string File, string Method, string[] OldIfs, Swap[] Swaps);

    // The `if` conditions of each method at origin/main 4d6d0e9f (the tree before this change), in source order.
    private static readonly Fixture[] Fixtures =
    [
        new("GNB_Default.PVP.cs", "EmergencyAbility",
            ["HeartOfCorundumPvP.CanUse(out action, targetOverride: TargetType.Self) && Player?.GetHealthRatio() * 100 <= 30"],
            [new(0, "HeartOfCorundum", "DefensivePercentPoints", "30")]),
        new("MNK_Default.PVP.cs", "EmergencyAbility",
            [
                "EarthsReplyStatusEnd && StatusHelper.PlayerHasStatus(true, StatusID.EarthResonance) && StatusHelper.PlayerWillStatusEndGCD(1, 0, true, StatusID.EarthResonance)",
                "EarthsReplyPvP.CanUse(out action, usedUp: true, skipAoeCheck: true, skipStatusNeed: true)",
                "Player?.GetHealthRatio() <= EarthsReplyPercent && StatusHelper.PlayerHasStatus(true, StatusID.EarthResonance)",
                "EarthsReplyPvP.CanUse(out action, usedUp: true, skipAoeCheck: true, skipStatusNeed: true)",
                "RiddleOfEarthPvP.CanUse(out action) && InCombat && Player?.GetHealthRatio() < 0.8",
                "BloodbathPvP.CanUse(out action) && Player?.GetHealthRatio() < BloodBathPvPPercent",
                "SwiftPvP.CanUse(out action)",
                "SmitePvP.CanUse(out action, usedUp: true) && SmitePvP.Target.Target.GetHealthRatio() <= SmitePvPPercent",
            ],
            [new(4, "RiddleOfEarth", "DefensivePercent", "0.8")]),
        new("AST_Default.PVP.cs", "EmergencyAbility",
            [
                "StatusHelper.PlayerWillStatusEndGCD(1, 0, true, StatusID.Macrocosmos_3104) && MicrocosmosPvP.CanUse(out action)",
                "StatusHelper.PlayerWillStatusEndGCD(1, 0, true, StatusID.LadyOfCrowns_4328) && LadyOfCrownsPvP.CanUse(out action)",
                "AspectedBeneficPvP_29247.CanUse(out action, usedUp: true)",
                "Player?.GetHealthRatio() < 0.6 && LadyOfCrownsPvP.CanUse(out action)",
                "Player?.GetHealthRatio() < 0.6 && MicrocosmosPvP.CanUse(out action)",
                "OraclePvP.CanUse(out action)",
            ],
            [new(3, "LadyOfCrowns", "DefensivePercent", "0.6"), new(4, "Microcosmos", "DefensivePercent", "0.6")]),
        new("NIN_Default.PVP.cs", "GeneralGCD",
            [
                "HasHidden", "ZeshoMeppoPvP.CanUse(out action)", "Player?.GetHealthRatio() < .5", "MeisuiPvP.CanUse(out action)",
                "ForkedRaijuPvP.CanUse(out action)", "FleetingRaijuPvP.CanUse(out action)", "GokaMekkyakuPvP.CanUse(out action)",
                "HyoshoRanryuPvP.CanUse(out action)", "StatusHelper.PlayerWillStatusEnd(1, true, StatusID.ThreeMudra) && HutonPvP.CanUse(out action)",
                "FumaShurikenPvP.CanUse(out action, usedUp: true)", "AeolianEdgePvP.CanUse(out action)", "GustSlashPvP.CanUse(out action)",
                "SpinningEdgePvP.CanUse(out action)",
            ],
            [new(2, "Meisui", "DefensivePercent", ".5")]),
        new("DRK_Default.PVP.cs", "GeneralGCD",
            [
                "DisesteemPvP.CanUse(out action)", "(Player?.GetHealthRatio() * 100) < 60 && ImpalementPvP.CanUse(out action)", "TorcleaverPvP.CanUse(out action)",
                "ComeuppancePvP.CanUse(out action)", "ScarletDeliriumPvP.CanUse(out action)", "SouleaterPvP.CanUse(out action)",
                "SyphonStrikePvP.CanUse(out action)", "HardSlashPvP.CanUse(out action)",
            ],
            [new(1, "Impalement", "DefensivePercentPoints", "60")]),
    ];

    private static string EnabledPrefix(Swap s) => $"Service.Config.DefensiveActive({Table}{s.Row}) && ";

    private static string Read(Swap s) => $"Service.Config.{s.Accessor}({Table}{s.Row})";

    /// <summary>What is wrong with a method's `if` conditions as a literal swap of the fixture's old ones (empty when right).</summary>
    private static List<string> SwapProblems(Fixture fx, IReadOnlyList<string> now)
    {
        var p = new List<string>();
        if (now.Count != fx.OldIfs.Length)
        {
            p.Add($"{now.Count} conditions, was {fx.OldIfs.Length}");
            return p;
        }

        for (var i = 0; i < now.Count; i++)
        {
            var swap = fx.Swaps.SingleOrDefault(s => s.Index == i);
            if (swap == null)
            {
                if (now[i] != fx.OldIfs[i]) p.Add($"#{i} changed: {now[i]}");
                continue;
            }

            if (!now[i].StartsWith(EnabledPrefix(swap), StringComparison.Ordinal)) { p.Add($"#{i} {swap.Row}: no Enabled test in front: {now[i]}"); continue; }
            var undone = now[i][EnabledPrefix(swap).Length..];
            if (!undone.Contains(Read(swap), StringComparison.Ordinal)) { p.Add($"#{i} {swap.Row}: number not read from {Read(swap)}: {now[i]}"); continue; }
            undone = undone.Replace(Read(swap), swap.Literal, StringComparison.Ordinal);
            if (undone != fx.OldIfs[i]) p.Add($"#{i} {swap.Row}: not the old condition once the number is put back: {undone}");
        }

        return p;
    }

    private static List<string> NowIfs(Fixture fx) => CsSource.IfConditions(SourceFiles.OverrideMethod(fx.File, fx.Method).Body);

    // ---- T1: the new expression at the row default equals the old literal comparison -----------------------------
    private static float Ratio(DefensiveRow row) => DefensiveTable.ResolvePercent(row, null);

    private static float Points(DefensiveRow row) => DefensiveTable.ToPercentPoints(Ratio(row));

    private static readonly (string Row, float Center, Func<float, bool> Old, Func<float, bool> New)[] Branches =
    [
        ("Heart of Corundum 30 (<=, 0-100 scale)", 0.30f, r => r * 100 <= 30, r => r * 100 <= Points(DefensiveTable.HeartOfCorundum)),
        ("Riddle of Earth 0.8 (<)", 0.8f, r => r < 0.8, r => r < Ratio(DefensiveTable.RiddleOfEarth)),
        ("Lady of Crowns 0.6 (<)", 0.6f, r => r < 0.6, r => r < Ratio(DefensiveTable.LadyOfCrowns)),
        ("Microcosmos 0.6 (<)", 0.6f, r => r < 0.6, r => r < Ratio(DefensiveTable.Microcosmos)),
        ("Meisui 0.5 (<)", 0.5f, r => r < .5, r => r < Ratio(DefensiveTable.Meisui)),
        ("Impalement 60 (<, 0-100 scale)", 0.60f, r => (r * 100) < 60, r => (r * 100) < Points(DefensiveTable.Impalement)),
    ];

    private static IEnumerable<float> Sweep(float center)
    {
        var up = center;
        var down = center;
        yield return center;
        for (var i = 0; i < 200; i++) { up = MathF.BitIncrement(up); down = MathF.BitDecrement(down); yield return up; yield return down; }
        for (var i = 0; i <= 10000; i++) yield return i / 10000f;
        foreach (var special in new[] { float.NaN, -1f, 2f, float.PositiveInfinity, float.NegativeInfinity, 0f, 1f }) yield return special;
    }

    private static string[] Disagreements(Func<float, bool> old, Func<float, bool> now, float center) =>
        Sweep(center).Where(r => old(r) != now(r)).Select(r => r.ToString("R")).Take(3).ToArray();

    public static void Run()
    {
        Console.WriteLine("-- round 3 Part A, item 7: the six literal swaps (T2 + T1) --");

        foreach (var fx in Fixtures)
        {
            var tag = fx.File.Split('_')[0];
            var now = NowIfs(fx);
            var problems = SwapProblems(fx, now);
            var rows = string.Join(" and ", fx.Swaps.Select(s => s.Row));
            Harness.Case($"{tag} {fx.Method}: {rows} swapped in place: same {fx.OldIfs.Length} conditions in the same order, operator and other conditions unchanged, Enabled test in front, number read from the row accessor",
                problems.Count == 0, string.Join("; ", problems));
        }

        // Which accessor reads which scale: the two branches that used a 0-100 number read the exact 0-100 value.
        Harness.Case("the 0-100 branches (Heart of Corundum, Impalement) read DefensivePercentPoints, the 0-1 branches read DefensivePercent",
            Fixtures.SelectMany(f => f.Swaps).All(s => (s.Row is "HeartOfCorundum" or "Impalement") == (s.Accessor == "DefensivePercentPoints")));

        // The generic stage must not also fire these rows: the swapped rows are Param rows.
        Harness.Case("the six swapped rows are Param rows with a default on (the generic stage skips them)",
            Fixtures.SelectMany(f => f.Swaps).All(s => DefensiveTable.Rows.Single(r => r.ActionName == s.Row + "PvP") is { Mode: DefensiveMode.Param, DefaultEnabled: true })
            && Fixtures.SelectMany(f => f.Swaps).Count() == 6);

        // ---- T1 ------------------------------------------------------------------------------------------------
        foreach (var (name, center, old, now) in Branches)
        {
            var bad = Disagreements(old, now, center);
            Harness.Case($"{name}: at the row default the new comparison equals the old literal comparison (200 floats each side of the threshold, a 10001-point grid, NaN, infinities)",
                bad.Length == 0, bad.Length == 0 ? $"{Sweep(center).Count()} values" : "differs at " + string.Join(",", bad));
        }

        // ---- canaries (false by construction) ---------------------------------------------------------------------
        var gnb = Fixtures[0];
        Harness.Canary("the old GNB text (no swap) is accepted as a literal swap", SwapProblems(gnb, gnb.OldIfs).Count == 0);
        var swapNow = NowIfs(gnb);
        Harness.Canary("a swapped condition whose operator was changed from <= to < is accepted",
            SwapProblems(gnb, [swapNow[0].Replace(" <= Service.Config", " < Service.Config")]).Count == 0);
        Harness.Canary("a swapped condition without the Enabled test is accepted",
            SwapProblems(gnb, [swapNow[0].Replace(EnabledPrefix(gnb.Swaps[0]), "")]).Count == 0);
        Harness.Canary("a swapped condition that dropped another condition (the Self target) is accepted",
            SwapProblems(gnb, [swapNow[0].Replace(", targetOverride: TargetType.Self", "")]).Count == 0);
        var mnk = Fixtures[1];
        var mnkNow = NowIfs(mnk);
        Harness.Canary("an MNK method that lost its InCombat condition is accepted",
            SwapProblems(mnk, [.. mnkNow.Select(c => c.Replace(" && InCombat", ""))]).Count == 0);
        Harness.Canary("a changed default (Meisui 0.55 instead of 0.5) agrees with the old comparison",
            Disagreements(r => r < .5, r => r < 0.55f, 0.5f).Length == 0);
        Harness.Canary("an unscaled read (Heart of Corundum compared on the 0-1 scale against 30) agrees with the old comparison",
            Disagreements(r => r * 100 <= 30, r => r * 100 <= Ratio(DefensiveTable.HeartOfCorundum), 0.30f).Length == 0);
    }
}
