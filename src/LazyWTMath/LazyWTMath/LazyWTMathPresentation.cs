using System;
using System.Globalization;

namespace WondrousTailsSolver;

/// <summary>Immutable display data captured from the existing solver and live sticker state.</summary>
internal sealed record LazyWTMathSnapshot(
    int StickersPlaced,
    bool[] Cells,
    string[] LineChances,
    string[]? ShuffleAverage,
    bool PlayerAvailable,
    bool HasValidProbabilities)
{
    internal static LazyWTMathSnapshot Unavailable()
        => new(0, new bool[16], [], null, false, false);
}

/// <summary>Converts the solver's user-facing text into validated values for the refreshed UI.</summary>
internal static class LazyWTMathPresentation
{
    private const string LineMarker = "Line Chances:";
    private const string ShuffleMarker = "Shuffle Average:";
    private static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    internal static LazyWTMathSnapshot Parse(int stickersPlaced, bool[] cells, string solverText)
    {
        if (cells.Length != 16)
            return Invalid(stickersPlaced, cells);

        var cellCopy = (bool[])cells.Clone();
        var lineStart = solverText.IndexOf(LineMarker, StringComparison.Ordinal);
        if (lineStart < 0)
            return Invalid(stickersPlaced, cellCopy);

        var valuesStart = lineStart + LineMarker.Length;
        var shuffleStart = solverText.IndexOf(ShuffleMarker, StringComparison.Ordinal);
        if (shuffleStart >= 0 && shuffleStart < valuesStart)
            return Invalid(stickersPlaced, cellCopy);

        var lineEnd = shuffleStart >= 0 ? shuffleStart : solverText.Length;
        if (!TryThreePercentages(solverText[valuesStart..lineEnd], out var lineChances))
            return Invalid(stickersPlaced, cellCopy);

        string[]? shuffleAverage = null;
        if (shuffleStart >= 0)
        {
            var shuffleText = solverText[(shuffleStart + ShuffleMarker.Length)..];
            if (!TryThreePercentages(shuffleText, out shuffleAverage))
                return Invalid(stickersPlaced, cellCopy);
        }

        return new LazyWTMathSnapshot(stickersPlaced, cellCopy, lineChances, shuffleAverage, true, true);
    }

    internal static bool TryPercent(string value, out float fraction)
    {
        fraction = 0;
        if (!value.EndsWith('%') ||
            !double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ||
            !double.IsFinite(percent) || percent < 0 || percent > 100)
            return false;

        fraction = (float)(percent / 100);
        return true;
    }

    private static bool TryThreePercentages(string text, out string[] values)
    {
        values = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 3)
            return false;

        foreach (var value in values)
            if (!TryPercent(value, out _))
                return false;

        return true;
    }

    private static LazyWTMathSnapshot Invalid(int stickersPlaced, bool[] cells)
        => new(stickersPlaced, cells.Length == 16 ? (bool[])cells.Clone() : new bool[16], [], null, true, false);
}
