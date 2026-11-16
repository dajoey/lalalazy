using WondrousTailsSolver;

namespace LazyWTMath.Harness;

/// <summary>
/// Offline checks for the overlay refresh gate. The addon update callback fires every frame; the
/// text prune and the probability solve only need to run when the board or the sticker count change.
/// </summary>
internal static class Program
{
    private static int _pass, _fail;

    private static int Main()
    {
        Case01_UnchangedInputsRecomputeOnce();
        Case02_ChangedInputsRecompute();
        Case03_ResetRecomputes();
        Case04_UncommittedPassKeepsAsking();

        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        if (_fail == 0) Console.WriteLine($"OK ({_pass} checks)");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine($"PASS  {name}"); }
        else { _fail++; Console.WriteLine($"FAIL  {name}  -- {detail}"); }
    }

    /// <summary>One frame as AddonRefresh runs it: recompute (and commit) only when the gate asks.</summary>
    private static int Frames(RefreshGate gate, int frames, int mask, int stickers)
    {
        var recomputes = 0;
        for (var i = 0; i < frames; i++)
        {
            if (!gate.ShouldRefresh(mask, stickers)) continue;
            recomputes++;
            gate.Commit(mask, stickers);
        }

        return recomputes;
    }

    private static void Case01_UnchangedInputsRecomputeOnce()
    {
        var gate = new RefreshGate();
        var n = Frames(gate, 100, 0b1010, 2);
        Check("01a 100 frames with unchanged inputs recompute exactly once", n == 1, $"recomputed {n} times");
        Check("01b the very first frame always recomputes", new RefreshGate().ShouldRefresh(0, 0));
    }

    private static void Case02_ChangedInputsRecompute()
    {
        var gate = new RefreshGate();
        Frames(gate, 10, 0b1010, 2);
        Check("02a a changed board mask recomputes", gate.ShouldRefresh(0b1011, 2));
        Frames(gate, 1, 0b1011, 2);
        Check("02b a changed sticker count recomputes", gate.ShouldRefresh(0b1011, 3));
        Frames(gate, 1, 0b1011, 3);
        Check("02c unchanged again after the new commit", !gate.ShouldRefresh(0b1011, 3));
    }

    private static void Case03_ResetRecomputes()
    {
        var gate = new RefreshGate();
        Frames(gate, 10, 0b1010, 2);
        gate.Reset();
        var n = Frames(gate, 10, 0b1010, 2);
        Check("03 Reset makes the next frame recompute even with the same inputs", n == 1, $"recomputed {n} times");
    }

    private static void Case04_UncommittedPassKeepsAsking()
    {
        // A pass skipped for a missing text node never commits, so it must not suppress the next real refresh.
        var gate = new RefreshGate();
        var asks = 0;
        for (var i = 0; i < 5; i++)
            if (gate.ShouldRefresh(0b1, 1)) asks++;
        Check("04 without a Commit the gate keeps asking for a refresh", asks == 5, $"asked {asks} times");
    }
}
