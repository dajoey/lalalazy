using System.Numerics;
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

    /// <summary>
    /// Top-left corner of the suggestion note. It sits above the game screen it describes when it fits
    /// inside the work area, otherwise below it, and is clamped horizontally into the work area so it
    /// never leaves the screen at the edges.
    /// </summary>
    public static Vector2 SuggestionBoxMin(Vector2 addonPos, float addonHeight, Vector2 boxSize, Vector2 workPos, Vector2 workSize, float gap)
    {
        var x = addonPos.X - 2;
        var maxX = workPos.X + workSize.X - boxSize.X;
        x = MathF.Max(workPos.X, MathF.Min(x, maxX));

        var y = addonPos.Y - gap - boxSize.Y;
        if (y < workPos.Y)
            y = addonPos.Y + addonHeight + gap;
        var maxY = workPos.Y + workSize.Y - boxSize.Y;
        y = MathF.Max(workPos.Y, MathF.Min(y, maxY));
        return new Vector2(x, y);
    }
}
