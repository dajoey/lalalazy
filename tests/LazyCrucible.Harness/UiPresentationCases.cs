using Lalalazy.Crucible;
using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>Regression coverage for presentation rules that must not change fight-need meaning or location.</summary>
internal static class UiPresentationCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    public static void Run()
    {
        Console.WriteLine("-- structural UI presentation --");

        var usefulSelection = new CrucibleSelection([], [
            new CrucibleNeedStatus(new CrucibleAbilityNeed(CrucibleNeeds.Dispel, CrucibleNeedTier.Useful, "Might on the Golem", ["AS"]), 41, null),
        ]);
        var usefulRows = GuideNeeds.Rows(usefulSelection);
        var usefulStats = UiPresentationLogic.RequiredCallStats(usefulRows);
        Check("Required-call stats exclude useful needs", usefulStats.Total == 0 && usefulStats.Covered == 0,
            $"covered={usefulStats.Covered} total={usefulStats.Total}");
        Check("Useful needs keep their tier suffix", usefulRows.Any(row => UiPresentationLogic.TierSuffix(row.Tier) == " (useful)"),
            string.Join("; ", usefulRows.Select(row => $"{row.Tool}={row.Tier}")));

        var requiredSelection = new CrucibleSelection([], [
            new CrucibleNeedStatus(new CrucibleAbilityNeed(CrucibleNeeds.Dispel, CrucibleNeedTier.Required, "Ultimate Focus (Magic Damage Up)", ["panel"]), 11, null),
        ]);
        var requiredRows = GuideNeeds.Rows(requiredSelection);
        var requiredStats = UiPresentationLogic.RequiredCallStats(requiredRows);
        Check("Required-call stats count only required coverage", requiredStats.Total == 1 && requiredStats.Covered == 1,
            $"covered={requiredStats.Covered} total={requiredStats.Total}");
        Check("Required needs have no useful suffix", requiredRows.Any(row => row.Tier == CrucibleNeedTier.Required)
            && requiredRows.Where(row => row.Tier == CrucibleNeedTier.Required).All(row => UiPresentationLogic.TierSuffix(row.Tier).Length == 0));

        var location = UiPresentationLogic.LocationSuffix(3, 5);
        Check("Guide subtext keeps row/space location", location.Contains("row", StringComparison.OrdinalIgnoreCase), location);

        var pluginSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "LazyCrucible", "Plugin.cs"));
        Check("Guide slash command routes through ToggleGuide",
            System.Text.RegularExpressions.Regex.IsMatch(pluginSource,
                "if \\(a.Equals\\(\"guide\", StringComparison.OrdinalIgnoreCase\\)\\)\\s*\\{\\s*ToggleGuide\\(\\);"));
        var guideWindowSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "LazyCrucible", "GuideWindow.cs"));
        Check("Guide window passes its plugin to the shell for footer actions",
            guideWindowSource.Contains("public override void Draw() => _shell.Draw(_plugin);", StringComparison.Ordinal));
    }
}
