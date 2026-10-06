using Lalalazy.Crucible;
using LazyCrucible.Policy;

namespace LazyCrucible.Harness;

/// <summary>
///     The board's degree latch (0.1.9.13). Live 2026-10-06 the plugin could not tell Standard from First Degree, so the BST rotation had to price
///     every recorded hit as if it could be 1.5-2x bigger. The only signal is the board layout's set-degree event: 42 of them since 10-05, all
///     <c>n=2|0:i2|1:i1</c> (First Degree, the AutoDuty loop); every other event the layout receives has one value (<c>0:i8</c>, <c>0:i-2</c>,
///     <c>1:i0</c>). Each case is a line of that capture or a variation the same grammar allows.
/// </summary>
internal static class DegreeLatchCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    public static void Run()
    {
        Console.WriteLine("-- degree latch --");

        var latch = new DegreeLatch();
        Check("nothing seen yet: unknown", latch.Value == CrucibleDegree.Unknown, latch.Value.ToString());

        // 2026-10-06 12:17:07.367 XE|ag=XBMStageDetailList|via=re|kind=0|n=2|0:i2|1:i1, then the one-value events that follow it.
        Check("the captured set-degree event latches First", latch.Note(0, 2, 2, 1) && latch.Value == CrucibleDegree.First, latch.Value.ToString());
        Check("a repeat of the same value is not a change", !latch.Note(0, 2, 2, 1) && latch.Value == CrucibleDegree.First);
        latch.Note(0, 1, 8, null);
        latch.Note(0, 1, -2, null);
        latch.Note(1, 1, 0, null);
        latch.Note(1, 1, -2, null);
        Check("the one-value events the layout also receives leave it alone", latch.Value == CrucibleDegree.First, latch.Value.ToString());

        Check("the player picks Standard: the latch follows", latch.Note(0, 2, 2, 0) && latch.Value == CrucibleDegree.Standard, latch.Value.ToString());
        Check("Second and Third latch too", latch.Note(0, 2, 2, 2) && latch.Value == CrucibleDegree.Second && latch.Note(0, 2, 2, 3) && latch.Value == CrucibleDegree.Third);

        latch.Note(0, 2, 5, 1);
        latch.Note(0, 2, 2, 4);
        latch.Note(0, 2, 2, -1);
        latch.Note(0, 2, 2, null);
        latch.Note(0, 3, 2, 1);
        latch.Note(1, 2, 2, 1);
        Check("a two-value event that is not the set-degree event, or a value outside 0-3, changes nothing", latch.Value == CrucibleDegree.Third, latch.Value.ToString());

        // 2026-10-06 15:39:49 .. 15:40:11 ET, every loop run: the result screen zones out (BetweenAreas, the player is gone, the hooks come down),
        // AutoDuty sets First Degree in the lobby at 15:40:03, then the queue zones into the duty (the hooks come down again) and the first CR| line
        // read dg=x in all 5,896 lines of 0.1.9.13. A loading screen is not a job change: the degree has to survive it.
        latch.Reset();
        latch.Note(0, 2, 2, 1);
        latch.HooksDown(playerAvailable: false);
        Check("the hooks come down for a loading screen (no player): First Degree is kept", latch.Value == CrucibleDegree.First, latch.Value.ToString());
        latch.HooksDown(playerAvailable: false);
        Check("a second loading screen keeps it too", latch.Value == CrucibleDegree.First, latch.Value.ToString());
        latch.HooksDown(playerAvailable: true);
        Check("the hooks come down with the player present (a job change): forgotten", latch.Value == CrucibleDegree.Unknown, latch.Value.ToString());

        latch.Reset();
        Check("a reset (job change, unload) forgets it", latch.Value == CrucibleDegree.Unknown, latch.Value.ToString());
    }
}
