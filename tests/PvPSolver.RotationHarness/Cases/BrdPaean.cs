namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 12, BRD-PAEAN (BRD-2). T2: <c>ModifyTheWardensPaeanPvP</c> sets <c>StatusFromSelf = false</c> so the
///     optional "Use Warden's Paean on other players" (<c>BRDEsuna2</c>, off by default) can find an afflicted ally.
///     The self path (<c>targetOverride: Self</c>) skips the status check and is unchanged.
/// </summary>
internal static class BrdPaean
{
    public static void Run()
    {
        Console.WriteLine("-- change 12 BRD-PAEAN (T2) --");
        AllyCleanse.Check("Warden's Paean", "BardRotation.cs", "ModifyTheWardensPaeanPvP", "BRD_Default.PVP.cs", "BRDEsuna2");
    }
}
