namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 13, WHM-AQUAVEIL (sibling of change 12, same pattern). T2: <c>ModifyAquaveilPvP</c> sets
///     <c>StatusFromSelf = false</c> so the optional "Use Aquaveil on other players" (<c>AquaveilEsuna</c>, off by
///     default) can find an afflicted ally. Identical to Warden's Paean: Purify-able <c>TargetStatusNeed</c>,
///     friendly, Dispel target type, no StatusNeed or StatusProvide.
/// </summary>
internal static class WhmAquaveil
{
    public static void Run()
    {
        Console.WriteLine("-- change 13 WHM-AQUAVEIL (T2) --");
        AllyCleanse.Check("Aquaveil", "WhiteMageRotation.cs", "ModifyAquaveilPvP", "WHM_Default.PVP.cs", "AquaveilEsuna");
    }
}
