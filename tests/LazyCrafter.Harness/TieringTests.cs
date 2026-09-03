using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class TieringTests
{
    private static Tiering T()
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        return new Tiering(graph, new SourceClassifier(data, graph, ventures, []));
    }

    private static IngredientLeaf Leaf(RecipeAssessment a, uint itemId) => a.Leaves.Single(l => l.ItemId == itemId);

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("stock recipe with everything on hand -> tier 0 (Now), HowMany = N, every leaf OnHand", () =>
        {
            var inv = new FakeInventory().Set(World.Ingot, 6).Set(World.Leather, 3);
            var a = T().Assess(World.SwordRecipe, inv);
            return a.Tier == EffortTier.Now && a.HowMany == 3 && a.CanCraft
                && a.Leaves.Count == 2 && a.Leaves.All(l => l.Tier == EffortTier.Now && l.Missing == 0 && l.Sources.SequenceEqual([SourceKind.OnHand]))
                && Leaf(a, World.Ingot).Need == 2 && Leaf(a, World.Leather).Need == 1;
        }),
        ("missing timed-node mat -> tier 2 (Ornament: RareOre unspoiled, Coal on hand)", () =>
        {
            var a = T().Assess(World.OrnamentRecipe, new FakeInventory().Set(World.Coal, 1));
            return a.Tier == EffortTier.SomeEffort && a.HowMany == 0 && !a.CanCraft
                && Leaf(a, World.RareOre).Tier == EffortTier.SomeEffort && Leaf(a, World.RareOre).Missing == 1
                && Leaf(a, World.Coal).Tier == EffortTier.Now;
        }),
        ("missing gil-vendor mat -> tier 1; the leaf takes the cheapest of its sources (GilVendor 1 beats Market 2)", () =>
        {
            var a = T().Assess(World.TrophyRecipe, new FakeInventory().Set(World.Hide, 1));
            var coal = Leaf(a, World.Coal);
            return a.Tier == EffortTier.Easy && coal.Tier == EffortTier.Easy
                && coal.Sources.Contains(SourceKind.GilVendor) && coal.Sources.Contains(SourceKind.Market);
        }),
        ("sub-craft whose sub-leaves are all on hand -> tier 1, sub-leaves reported, quantities scaled by the sub amount", () =>
        {
            // Sword: 0 Ingot on hand but 4 Ore + 2 Coal -> 2 Ingot craftable; Leather on hand.
            var a = T().Assess(World.SwordRecipe, new FakeInventory().Set(World.Ore, 4).Set(World.Coal, 2).Set(World.Leather, 1));
            var ingot = Leaf(a, World.Ingot);
            return a.Tier == EffortTier.Easy && a.HowMany == 1
                && ingot.Tier == EffortTier.Easy && ingot.Sources.Contains(SourceKind.SubCraft) && ingot.Missing == 2
                && Leaf(a, World.Ore) is { Need: 4, Have: 4, Tier: EffortTier.Now }
                && Leaf(a, World.Coal) is { Need: 2, Have: 2, Tier: EffortTier.Now };
        }),
        ("a sub-craft inherits its own missing leaves' tier (Pendant <- Ornament <- timed RareOre -> tier 2)", () =>
        {
            var a = T().Assess(World.PendantRecipe, new FakeInventory().Set(World.Coal, 5));
            return a.Tier == EffortTier.SomeEffort
                && Leaf(a, World.Ornament).Tier == EffortTier.SomeEffort
                && Leaf(a, World.RareOre).Tier == EffortTier.SomeEffort;
        }),
        ("drop-only mat -> tier 3; unsourced mat -> Blocked; unknown recipe -> Blocked with no leaves", () =>
        {
            var trophy = T().Assess(World.TrophyRecipe, new FakeInventory().Set(World.Coal, 1));
            var charm = T().Assess(World.CharmRecipe, new FakeInventory());
            var nothing = T().Assess(99999, new FakeInventory());
            return trophy.Tier == EffortTier.RealEffort && Leaf(trophy, World.Hide).Sources.SequenceEqual([SourceKind.Drop])
                && charm.Tier == EffortTier.Blocked && Leaf(charm, World.Mystery).Sources.SequenceEqual([SourceKind.Unknown])
                && nothing.Tier == EffortTier.Blocked && nothing.Leaves.Count == 0 && nothing.HowMany == 0;
        }),
        ("shared material is not double-counted across levels (one Coal serves Pendant OR Ornament, not both)", () =>
        {
            var a = T().Assess(World.PendantRecipe, new FakeInventory().Set(World.Coal, 1).Set(World.RareOre, 1));
            var coals = a.Leaves.Where(l => l.ItemId == World.Coal).ToList();
            return coals.Count == 2 && coals.Sum(l => l.Have) == 1 && coals.Sum(l => l.Missing) == 1
                && a.Tier == EffortTier.Easy;
        }),
        ("Assess for N crafts scales every need by N (Sword x2 with 3 Ingot -> Ingot missing 1, tier from Ingot's sources)", () =>
        {
            var a = T().Assess(World.SwordRecipe, new FakeInventory().Set(World.Ingot, 3).Set(World.Leather, 2), crafts: 2);
            var ingot = Leaf(a, World.Ingot);
            return ingot.Need == 4 && ingot.Have == 3 && ingot.Missing == 1 && Leaf(a, World.Leather).Need == 2
                && a.Tier == EffortTier.Easy;   // Ore is a regular node (1) -> sub-craft path 1; Market would be 2
        }),
    };
}
