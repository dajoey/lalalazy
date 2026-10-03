using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary> One "Calls for" line of the fight guide: a need of the fight, its tier, and who covers it or why nobody does. </summary>
internal readonly record struct GuideNeedRow(CrucibleNeedTier Tier, CrucibleNeeds Kind, string Tool, string What, IReadOnlyList<string> Src, int CoveredBy, string? WhyNot)
{
    public bool Covered => CoveredBy != 0;

    /// <summary> Only a Required need that nothing covers is a warning. </summary>
    public bool Warn => !Covered && Tier == CrucibleNeedTier.Required;
}

/// <summary>
///     What the fight guide's "Calls for" block shows, built from the SAME selection the formation screen's horn pick
///     computes (<see cref="BST_CrucibleNeedFirst.Select"/> over <see cref="CrucibleNeedModel"/>). PURE: the window only draws it.
/// </summary>
internal static class GuideNeeds
{
    public static string Tool(CrucibleNeeds kind) => kind switch
    {
        CrucibleNeeds.Interrupt => "interrupt (Soul Crush)",
        CrucibleNeeds.Dispel => "dispel (Quelling Wave)",
        CrucibleNeeds.Cleanse => "cleanse (Scouring Ash)",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    ///     The need coverage of one pick as a telemetry field value: <c>kind:tier:row</c> per need, or
    ///     <c>kind:tier:miss=reason</c>, joined with ';' (I/D/C = interrupt/dispel/cleanse, R/U = required/useful).
    ///     Empty when the fight has no ability needs.
    /// </summary>
    public static string Log(CrucibleSelection selection) =>
        string.Join(";", selection.Needs.Select(s =>
            $"{(s.Need.Kind == CrucibleNeeds.Interrupt ? "I" : s.Need.Kind == CrucibleNeeds.Dispel ? "D" : "C")}:"
            + $"{(s.Need.Tier == CrucibleNeedTier.Required ? "R" : "U")}:"
            + (s.Covered ? s.Row.ToString() : "miss=" + s.WhyNot)));

    /// <summary>
    ///     The fight's ability needs graded against the rows actually standing on the horn (the <c>HC|</c> line,
    ///     0.1.9.7): one <c>kind:tier:row</c> per need the fight calls for (I/D/C = interrupt/dispel/cleanse,
    ///     R/U = required/useful), naming the first horn row that answers it, or <c>miss</c> when none does.
    ///     Unlike <see cref="Log"/> this reads the SETTLED horn, so it grades what the fight actually runs with,
    ///     including a driver's picks. Empty when the fight has no ability needs. PURE.
    /// </summary>
    public static string HornLog(int board, int battle, IReadOnlyList<int> hornRows)
    {
        var model = CrucibleNeedModel.For(board, battle);
        var rows = hornRows.Where(r => r is >= 1 and <= BST_Beasts.Count).Distinct().ToList();
        var parts = new List<string>(3);
        foreach (var kind in BST_CrucibleNeedFirst.Kinds)
        {
            var required = (model.Required & kind) != 0;
            var useful = (model.Useful & kind) != 0;
            if (!required && !useful)
                continue;
            var cover = rows.FirstOrDefault(r => (BST_CrucibleAdvisor.Answers(r) & kind) != 0);
            parts.Add(Letter(kind) + ":" + (required ? "R" : "U") + ":" + (cover == 0 ? "miss" : cover.ToString()));
        }
        return string.Join(";", parts);
    }

    private static char Letter(CrucibleNeeds kind) => kind == CrucibleNeeds.Interrupt ? 'I' : kind == CrucibleNeeds.Dispel ? 'D' : 'C';


    /// <summary> Required needs first, each group in interrupt, dispel, cleanse order. </summary>
    public static List<GuideNeedRow> Rows(CrucibleSelection selection) =>
        selection.Needs
            .Select(s => new GuideNeedRow(s.Need.Tier, s.Need.Kind, Tool(s.Need.Kind), s.Need.What, s.Need.Src, s.Row, s.WhyNot))
            .OrderByDescending(r => r.Tier)
            .ThenBy(r => Array.IndexOf(BST_CrucibleNeedFirst.Kinds, r.Kind))
            .ToList();

    /// <summary>
    ///     The picks for a fight: the run's roster and HP when the run is on that board, else every captured familiar.
    ///     The formation screen computes the same selection from the live party.
    /// </summary>
    public static CrucibleSelection SelectionFor(int board, int battle, int runBoard, IReadOnlyList<int> roster,
        IReadOnlyDictionary<int, int> rosterHp, Func<int, bool> captured) =>
        runBoard == board && roster.Count > 0
            ? BST_CrucibleNeedFirst.Select(board, battle, roster, rosterHp, 3, "in the run roster")
            : BST_CrucibleNeedFirst.SelectCaptured(board, battle, captured);
}
