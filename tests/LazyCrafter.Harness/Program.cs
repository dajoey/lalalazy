using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Minimal test runner: each check is a name + predicate, prints PASS/FAIL per line and
/// "OK" at the end when everything passed. Phase 1+ adds cases here (TDD).
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("core self-check", () => CoreInfo.SelfCheck() == "OK"),
        ("core assembly has no Dalamud/Lumina reference", () =>
            !typeof(CoreInfo).Assembly.GetReferencedAssemblies()
                .Any(a => a.Name!.StartsWith("Dalamud", StringComparison.Ordinal)
                       || a.Name!.StartsWith("Lumina", StringComparison.Ordinal)
                       || a.Name!.StartsWith("FFXIVClientStructs", StringComparison.Ordinal))),
        ("leaf missing = need - have, floored at 0", () =>
            new IngredientLeaf(1, 5, 3, [SourceKind.OnHand], EffortTier.Now).Missing == 2
            && new IngredientLeaf(1, 2, 9, [SourceKind.OnHand], EffortTier.Now).Missing == 0),
        ("effort tiers order Now < Easy < SomeEffort < RealEffort < Blocked", () =>
            EffortTier.Now < EffortTier.Easy && EffortTier.Easy < EffortTier.SomeEffort
            && EffortTier.SomeEffort < EffortTier.RealEffort && EffortTier.RealEffort < EffortTier.Blocked),
    };

    private static int Main()
    {
        var failed = 0;
        foreach (var (name, check) in Tests)
        {
            bool ok;
            string? err = null;
            try { ok = check(); }
            catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(err is null ? "" : "  (" + err + ")")}");
            if (!ok) failed++;
        }

        Console.WriteLine($"{Tests.Count - failed}/{Tests.Count} passed");
        Console.WriteLine(failed == 0 ? "OK" : "FAILED");
        return failed == 0 ? 0 : 1;
    }
}
