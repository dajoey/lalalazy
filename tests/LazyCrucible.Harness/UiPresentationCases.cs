using System.Numerics;
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

        var guidePagesSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "LazyCrucible", "CrucibleGuidePages.cs"));
        Check("Missing capture notice wraps inside the horn-picks card",
            guidePagesSource.Contains("Widgets.SecondaryWrapped(\"Captured list unavailable; ranking assumes every familiar is captured.\")", StringComparison.Ordinal));
        Check("Fight-facts threats wrap inside the right rail",
            guidePagesSource.Contains("Widgets.SecondaryWrapped(\"Threats  \"", StringComparison.Ordinal));
        Check("Fight-facts sources wrap inside the right rail",
            guidePagesSource.Contains("Widgets.SecondaryWrapped(\"Sources  \"", StringComparison.Ordinal));
        var cardsSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "LazyCrucible", "CruciblePages.cs"));
        Check("Manually sized cards bound wrapping to their inner right edge",
            cardsSource.Contains("ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)))", StringComparison.Ordinal));

        SuggestionPlacementCases();
    }

    /// <summary>The suggestion note sits above the game screen it describes and must stay on screen at the edges.</summary>
    private static void SuggestionPlacementCases()
    {
        var work = new Vector2(1920, 1080);
        var box = new Vector2(400, 120);
        const float gap = 8f;

        var centred = UiPresentationLogic.SuggestionBoxMin(new Vector2(700, 500), 300, box, Vector2.Zero, work, gap);
        Check("Suggestion note: a centred screen keeps the note above it", centred == new Vector2(698, 372), centred.ToString());

        var top = UiPresentationLogic.SuggestionBoxMin(new Vector2(700, 10), 300, box, Vector2.Zero, work, gap);
        Check("Suggestion note: a screen at the top edge flips the note below it", top == new Vector2(698, 318), top.ToString());

        var right = UiPresentationLogic.SuggestionBoxMin(new Vector2(1800, 500), 300, box, Vector2.Zero, work, gap);
        Check("Suggestion note: a screen at the right edge clamps the note's X", right == new Vector2(1520, 372), right.ToString());

        var left = UiPresentationLogic.SuggestionBoxMin(new Vector2(0, 500), 300, box, Vector2.Zero, work, gap);
        Check("Suggestion note: a screen at the left edge clamps the note's X", left == new Vector2(0, 372), left.ToString());

        var offset = UiPresentationLogic.SuggestionBoxMin(new Vector2(2600, 40), 300, box, new Vector2(1920, 0), work, gap);
        Check("Suggestion note: placement respects the work area origin of a second monitor",
            offset == new Vector2(2600 - 2, 348), offset.ToString());

        var tall = UiPresentationLogic.SuggestionBoxMin(new Vector2(700, 20), 1070, box, Vector2.Zero, work, gap);
        Check("Suggestion note: with no room above or below it stays inside the work area",
            tall.Y >= 0 && tall.Y + box.Y <= work.Y, tall.ToString());
    }
}
