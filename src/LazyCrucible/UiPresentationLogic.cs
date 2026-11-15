using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary>Small presentation rules shared by the structural UI and its regression harness.</summary>
internal static class UiPresentationLogic
{
    /// <summary>Required-only call coverage; useful or disputed optional needs never inflate the total.</summary>
    public static (int Covered, int Total) RequiredCallStats(IEnumerable<GuideNeedRow> rows)
    {
        var required = rows.Where(row => row.Tier == CrucibleNeedTier.Required).ToList();
        return (required.Count(row => row.Covered), required.Count);
    }

    /// <summary>Preserve the old guide's explicit distinction between required and useful needs.</summary>
    public static string TierSuffix(CrucibleNeedTier tier) => tier == CrucibleNeedTier.Required ? "" : " (useful)";

    /// <summary>Keep the fight's row/space location in the guide subtext, including random-space types.</summary>
    public static string LocationSuffix(int board, int battle)
    {
        var location = CrucibleBoards.Where(board, battle);
        return location.Length == 0 ? "" : $" · {location}";
    }
}
