using System.Text.RegularExpressions;
using RotationSolver.Basic.Data;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 2, ALLOWLIST (SHARED-2 / TANK-S2 / XC-1). T1: the pure
///     <see cref="PvpUtilityActionIds.IsAllowedWithoutHostiles"/> allows exactly Guard 29054, Standard-issue
///     Elixir 29055, Purify 29056, Sprint 29057 and Recuperate 29711; PvE Sprint (3) and Thunderclap (29484)
///     no longer pass. The call site in BaseAction.cs is only shape-checked (not claimed as T1).
/// </summary>
internal static class UtilityAllowlist
{
    private static readonly uint[] Expected = [29054, 29055, 29056, 29057, 29711];

    // The behaviour before the change, as the literals stood in BaseAction.cs.
    private static bool OldList(uint id) => id == 3 || id == 29711 || id == 29054 || id == 29055 || id == 29484;

    private static string[] Mismatches(Func<uint, bool> impl) =>
        Enumerable.Range(0, 70000).Select(i => (uint)i).Where(id => impl(id) != Expected.Contains(id)).Select(id => id.ToString()).ToArray();

    public static void Run()
    {
        Console.WriteLine("-- change 2 ALLOWLIST (T1) --");

        var wrong = Mismatches(PvpUtilityActionIds.IsAllowedWithoutHostiles);
        Harness.Case("allowed ids are exactly {29054, 29055, 29056, 29057, 29711} for every id 0..69999", wrong.Length == 0,
            wrong.Length == 0 ? "70000 ids" : "differs at " + string.Join(",", wrong));

        Harness.Case("PvP Sprint 29057 and Purify 29056 pass", PvpUtilityActionIds.IsAllowedWithoutHostiles(29057) && PvpUtilityActionIds.IsAllowedWithoutHostiles(29056));
        Harness.Case("PvE Sprint 3 and Thunderclap 29484 no longer pass",
            !PvpUtilityActionIds.IsAllowedWithoutHostiles(3) && !PvpUtilityActionIds.IsAllowedWithoutHostiles(29484));

        // Each constant is the real action id.
        (string Name, uint Const, uint Real)[] ids =
        [
            ("GuardPvP", PvpUtilityActionIds.GuardPvP, (uint)ActionID.GuardPvP),
            ("StandardissueElixirPvP", PvpUtilityActionIds.StandardissueElixirPvP, (uint)ActionID.StandardissueElixirPvP),
            ("PurifyPvP", PvpUtilityActionIds.PurifyPvP, (uint)ActionID.PurifyPvP),
            ("SprintPvP", PvpUtilityActionIds.SprintPvP, (uint)ActionID.SprintPvP),
            ("RecuperatePvP", PvpUtilityActionIds.RecuperatePvP, (uint)ActionID.RecuperatePvP),
        ];
        foreach (var (name, c, real) in ids)
            Harness.Case($"{name} constant equals (uint)ActionID.{name}", c == real, $"{c} vs {real}");

        // Red against the old behaviour: the old literal list disagrees with the table at exactly these ids.
        var oldWrong = Mismatches(OldList);
        Harness.Case("RED on old behaviour: the old literal list disagrees at 3, 29056, 29057, 29484",
            oldWrong.OrderBy(x => x).SequenceEqual(["29056", "29057", "29484", "3"]), string.Join(",", oldWrong));

        // Call site: BaseAction.CanUse uses the pure core and no longer carries the raw literals.
        var path = Path.Combine(Program.SrcRoot, "PvPSolver.Basic", "Actions", "BaseAction.cs");
        var san = CsSource.Sanitize(File.ReadAllText(path));
        var uses = Regex.IsMatch(san, @"RotationSolver\.Decisions\.PvpUtilityActionIds\.IsAllowedWithoutHostiles\(\s*ID\s*\)");
        var literals = Regex.Matches(san, @"\bID\s*==\s*(3|29711|29054|29055|29484)\b").Select(m => m.Value).ToArray();
        Harness.Case("BaseAction.CanUse calls PvpUtilityActionIds.IsAllowedWithoutHostiles(ID)", uses);
        Harness.Case("BaseAction.cs no longer compares ID against the five raw literals", literals.Length == 0,
            literals.Length == 0 ? "no ID == literal" : string.Join(", ", literals));

        // Canary: a mutant that keeps PvE Sprint must not satisfy the table.
        static bool Mutant(uint id) => PvpUtilityActionIds.IsAllowedWithoutHostiles(id) || id == 3;
        Harness.Canary("mutant allowlist that also lets PvE Sprint (3) through satisfies the table", Mismatches(Mutant).Length == 0);
    }
}
