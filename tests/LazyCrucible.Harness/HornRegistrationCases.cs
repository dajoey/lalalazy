namespace LazyCrucible.Harness;

/// <summary>
///     Patch 7.57: a team of three or fewer familiars has every member registered on a battlehorn by the game itself (the team review
///     screen). The horn pass must stand down on a screen the game already filled: no writes, no fighting the game's registration (a
///     knocked-out member the game registered is left where it is: with no other familiar to put there, removing it gains nothing),
///     while a bigger team, or a small team the game did not fill, is handled as before.
/// </summary>
internal static class HornRegistrationCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private static List<CrucibleBeastPick> Picks(params int[] rows) => rows.Select(r => new CrucibleBeastPick(r, 0, true, "")).ToList();

    private static string Show((List<int> Remove, List<int> Add) d) => $"remove={string.Join(".", d.Remove)} add={string.Join(".", d.Add)}";

    public static void Run()
    {
        Console.WriteLine("-- battlehorns the game registered itself (7.57, teams of three or fewer) --");

        int[] trio = [19, 8, 22];
        var koOne = FormationLogic.PlanHornWrites(trio, [19, 8, 22], Picks(19, 22));
        Check("three-familiar team on its three horns, one knocked out: no writes (the knocked-out member the game registered stays)",
            koOne.RemoveRows.Count == 0 && koOne.AddRows.Count == 0, Show(koOne));
        var reordered = FormationLogic.PlanHornWrites(trio, [22, 19, 8], Picks(19, 8, 22));
        Check("three-familiar team registered in another order: no writes", reordered.RemoveRows.Count == 0 && reordered.AddRows.Count == 0, Show(reordered));
        var pair = FormationLogic.PlanHornWrites([19, 8], [8, 19, 0], Picks(19));
        Check("two-familiar team on two horns: no writes even when the picker would field only one", pair.RemoveRows.Count == 0 && pair.AddRows.Count == 0, Show(pair));
        Check("the game-filled verdict: three of three, two of two",
            FormationLogic.HornFilledByGame(trio, [8, 22, 19]) && FormationLogic.HornFilledByGame([19, 8], [19, 8]));

        int[] ten = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var bigTeam = FormationLogic.PlanHornWrites(ten, [1, 2, 3], Picks(4, 5, 6));
        Check("a ten-familiar team is never game-filled: the usual membership delta (remove 1.2.3, add 4.5.6)",
            !FormationLogic.HornFilledByGame(ten, [1, 2, 3]) && bigTeam.RemoveRows.Order().SequenceEqual([1, 2, 3]) && bigTeam.AddRows.SequenceEqual([4, 5, 6]),
            Show(bigTeam));
        var gap = FormationLogic.PlanHornWrites(trio, [19, 8], Picks(19, 8, 22));
        Check("a small team the game did not fill (a horn left empty): the missing member is added as before",
            !FormationLogic.HornFilledByGame(trio, [19, 8]) && gap.RemoveRows.Count == 0 && gap.AddRows.SequenceEqual([22]), Show(gap));
        Check("an empty party or an empty horn is not game-filled",
            !FormationLogic.HornFilledByGame([], []) && !FormationLogic.HornFilledByGame(trio, []));
    }
}
