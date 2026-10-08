using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 9, GNB-HEART (GNB-5). T2: the Emergency self-save tests the player's own HP, so Heart of Corundum
///     must be aimed at the player: <c>CanUse(out action, targetOverride: TargetType.Self)</c>. The
///     DefenseSingleAbility call (an attacked ally) is a different decision and must stay as it was.
/// </summary>
internal static class GnbHeart
{
    private static readonly Regex Call = new(@"HeartOfCorundumPvP\s*\.\s*CanUse\s*\(\s*(?<args>[^)]*)\)");

    public static void Run()
    {
        Console.WriteLine("-- change 9 GNB-HEART (T2) --");

        var emergency = SourceFiles.OverrideMethod("GNB_Default.PVP.cs", "EmergencyAbility");
        var calls = Call.Matches(emergency.Body).Select(m => CsSource.Squash(m.Groups["args"].Value)).ToList();
        Harness.Case("GNB EmergencyAbility calls HeartOfCorundumPvP.CanUse exactly once", calls.Count == 1, $"{calls.Count} found");
        if (calls.Count != 1) return;
        Harness.Case("EmergencyAbility Heart of Corundum is aimed at Self",
            calls[0] == "out action, targetOverride: TargetType.Self", calls[0]);

        var cond = CsSource.IfConditions(emergency.Body).Single(c => c.Contains("HeartOfCorundumPvP.CanUse("));
        Harness.Case("EmergencyAbility still gates on the player's own HP <= 30 percent",
            Regex.IsMatch(cond, @"Player\?\.GetHealthRatio\(\)\s*\*\s*100\s*<=\s*30\b"), cond);

        var defense = SourceFiles.OverrideMethod("GNB_Default.PVP.cs", "DefenseSingleAbility");
        var dcalls = Call.Matches(defense.Body).Select(m => CsSource.Squash(m.Groups["args"].Value)).ToList();
        Harness.Case("DefenseSingleAbility Heart of Corundum is untouched (no target override)",
            dcalls.Count == 1 && dcalls[0] == "out action", string.Join("|", dcalls));

        // Canary: run the same extraction on the old Emergency text; it must not come back as the Self form.
        var oldArgs = CsSource.Squash(Call.Match("if (HeartOfCorundumPvP.CanUse(out action) && Player?.GetHealthRatio() * 100 <= 30)").Groups["args"].Value);
        Harness.Canary("old Emergency Heart of Corundum call is accepted as aimed at Self", oldArgs == "out action, targetOverride: TargetType.Self");
    }
}
