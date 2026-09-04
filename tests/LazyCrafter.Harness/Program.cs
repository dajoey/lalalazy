using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>
/// Minimal test runner: each check is a name + predicate, prints PASS/FAIL per line and
/// "OK" at the end when everything passed. Suites live one-per-Core-class (TDD, Phase 1+).
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, Func<bool> Check)> Smoke = new()
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

    private static IEnumerable<(string Suite, string Name, Func<bool> Check)> AllTests()
    {
        foreach (var t in Smoke) yield return ("smoke", t.Name, t.Check);
        foreach (var t in RecipeGraphTests.Tests) yield return ("graph", t.Name, t.Check);
        foreach (var t in SourceClassifierTests.Tests) yield return ("classify", t.Name, t.Check);
        foreach (var t in TieringTests.Tests) yield return ("tier", t.Name, t.Check);
        foreach (var t in VentureResolverTests.Tests) yield return ("venture", t.Name, t.Check);
        foreach (var t in ProfitModelTests.Tests) yield return ("profit", t.Name, t.Check);
        foreach (var t in ScripValueTests.Tests) yield return ("scrip", t.Name, t.Check);
        foreach (var t in DesynthValueTests.Tests) yield return ("desynth", t.Name, t.Check);
        foreach (var t in LevelingScoreTests.Tests) yield return ("leveling", t.Name, t.Check);
        foreach (var t in UndersuppliedFinderTests.Tests) yield return ("undersupplied", t.Name, t.Check);
        foreach (var t in CraftingLogFilterTests.Tests) yield return ("log", t.Name, t.Check);
        foreach (var t in AdversarialTests.Tests) yield return ("adversarial", t.Name, t.Check);
        foreach (var t in CartTests.Tests) yield return ("cart", t.Name, t.Check);
        foreach (var t in DispatchPlanTests.Tests) yield return ("dispatch", t.Name, t.Check);
        foreach (var t in RetrieveTests.Tests) yield return ("retrieve", t.Name, t.Check);
    }

    private static int Main()
    {
        var total = 0;
        var failed = 0;
        foreach (var (suite, name, check) in AllTests())
        {
            total++;
            bool ok;
            string? err = null;
            try { ok = check(); }
            catch (Exception ex) { ok = false; err = ex.GetType().Name + ": " + ex.Message; }
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  [{suite}] {name}{(err is null ? "" : "  (" + err + ")")}");
            if (!ok) failed++;
        }

        Console.WriteLine($"{total - failed}/{total} passed");
        Console.WriteLine(failed == 0 ? "OK" : "FAILED");
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>Shared fixture: a tiny fake game world every suite can reuse.</summary>
internal static class World
{
    // Jobs (ClassJob row ids): BSM=10, ARM=11, LTW=14 (crafters); MIN=16, BTN=17, FSH=18; PLD=19 (combat).
    public const uint Bsm = 10, Arm = 11, Ltw = 14, Min = 16, Btn = 17, Fsh = 18, Pld = 19;

    // Items
    public const uint Ingot = 100, Ore = 200, Coal = 201;
    public const uint Sword = 300, Leather = 400, Hide = 401;
    public const uint RareOre = 500, Trout = 501, MarketOnly = 502, ScripMat = 503, Mystery = 504;
    public const uint Arrows = 600, Feather = 601;
    public const uint CycleA = 700, CycleB = 701;
    public const uint Ornament = 800, Trophy = 801, Charm = 802, Pendant = 900;

    // Recipes
    public const uint IngotBsm = 10, IngotArm = 11, SwordRecipe = 30, LeatherLtw = 40, ArrowsRecipe = 60;
    public const uint CycleARecipe = 70, CycleBRecipe = 71, OrnamentRecipe = 80, TrophyRecipe = 81, CharmRecipe = 82, PendantRecipe = 90;

    public static FakeGameData Build() => new FakeGameData()
        // Ingot: two recipes for the same result item; same-job preference must pick BSM from a BSM parent.
        .Recipe(IngotBsm, Ingot, 1, Bsm, 10, (Ore, 2), (Coal, 1))
        .Recipe(IngotArm, Ingot, 1, Arm, 10, (Ore, 3), (Coal, 1))
        .Recipe(SwordRecipe, Sword, 1, Bsm, 20, (Ingot, 2), (Leather, 1))
        .Recipe(LeatherLtw, Leather, 1, Ltw, 10, (Hide, 1))
        .Recipe(ArrowsRecipe, Arrows, 3, Bsm, 5, (Feather, 1))
        .Recipe(CycleARecipe, CycleA, 1, Bsm, 1, (CycleB, 1))
        .Recipe(CycleBRecipe, CycleB, 1, Bsm, 1, (CycleA, 1))
        .Recipe(OrnamentRecipe, Ornament, 1, Bsm, 50, (RareOre, 1), (Coal, 1))
        .Recipe(TrophyRecipe, Trophy, 1, Bsm, 50, (Coal, 1), (Hide, 1))
        .Recipe(CharmRecipe, Charm, 1, Bsm, 50, (Mystery, 1))
        .Recipe(PendantRecipe, Pendant, 1, Bsm, 60, (Ornament, 1), (Coal, 1))
        .GilVendor(Coal, 3)
        .Gatherable(Ore, new GatherInfo(Min, 10, NodeType.Regular, Timed: false, Collectable: false))
        .Gatherable(RareOre, new GatherInfo(Min, 50, NodeType.Unspoiled, Timed: true, Collectable: false))
        .Fish(Trout)
        .Marketable(Ore).Marketable(Coal).Marketable(Ingot).Marketable(Sword).Marketable(MarketOnly).Marketable(RareOre)
        .SpecialShop(ScripMat)
        .Drop(Hide)
        .Venture(new VentureRow(TaskId: 1, ItemId: Ore, Level: 10, JobCategory: 17, RequiredGathering: 50, RequiredItemLevel: 0,
            QuantityTiers: [10, 15, 20, 25, 30], RewardThresholds: [100, 200, 300, 400]))
        .Venture(new VentureRow(TaskId: 2, ItemId: Hide, Level: 30, JobCategory: 34, RequiredGathering: 0, RequiredItemLevel: 60,
            QuantityTiers: [5, 8, 11, 14, 17], RewardThresholds: [70, 80, 90, 100]));
}
