using WondrousTailsSolver;

namespace LazyWTMath.UiHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Console.WriteLine("-- LazyWTMath UI presentation --");

        var cells = new bool[16];
        cells[0] = true;
        cells[5] = true;
        var withShuffle = LazyWTMathPresentation.Parse(7, cells,
            "Line Chances: 12.34%  56.78%  90.00%\rShuffle Average: 10.00% 20.00% 30.00%");
        Check("line and shuffle text is accepted", withShuffle.HasValidProbabilities);
        Check("line values remain exact", withShuffle.LineChances.SequenceEqual(["12.34%", "56.78%", "90.00%"]));
        Check("shuffle values remain exact", withShuffle.ShuffleAverage?.SequenceEqual(["10.00%", "20.00%", "30.00%"]) == true);
        Check("sticker count remains live", withShuffle.StickersPlaced == 7);
        Check("sticker map is copied", withShuffle.Cells[0] && withShuffle.Cells[5] && !withShuffle.Cells[1]);
        Check("percentage becomes a bounded fraction", LazyWTMathPresentation.TryPercent("56.78%", out var fraction)
            && Math.Abs(fraction - 0.5678f) < 0.0001f);

        cells[0] = false;
        Check("later game-state updates do not mutate the snapshot", withShuffle.Cells[0]);

        var withoutShuffle = LazyWTMathPresentation.Parse(9, new bool[16],
            "Line Chances: 100.00% 25.00% 0.00%");
        Check("line-only solver text is accepted", withoutShuffle.HasValidProbabilities);
        Check("missing shuffle sample stays empty", withoutShuffle.ShuffleAverage is null);

        var solverError = LazyWTMathPresentation.Parse(10, new bool[16], "Line Chances: error error error");
        Check("solver errors never become fake percentages", !solverError.HasValidProbabilities);

        var malformed = LazyWTMathPresentation.Parse(3, new bool[16], "Line Chances: 10.00% 20.00%");
        Check("incomplete values are rejected", !malformed.HasValidProbabilities);

        var malformedShuffle = LazyWTMathPresentation.Parse(3, new bool[16],
            "Line Chances: 10.00% 20.00% 30.00%\nShuffle Average: unavailable");
        Check("invalid shuffle values reject the snapshot", !malformedShuffle.HasValidProbabilities);

        var unavailable = LazyWTMathSnapshot.Unavailable();
        Check("unavailable player state is distinct from valid zeroes", !unavailable.PlayerAvailable && !unavailable.HasValidProbabilities);

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
        if (condition) _pass++; else _fail++;
    }
}
