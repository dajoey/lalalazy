using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

/// <summary>Phase 4: cart aggregation (Tiering.AssessCart) and the TeamCraft export link.</summary>
internal static class CartTests
{
    private static Tiering T()
    {
        var data = World.Build();
        var graph = new RecipeGraph(data);
        var ventures = new VentureResolver(data);
        return new Tiering(graph, new SourceClassifier(data, graph, ventures, []));
    }

    private static IngredientLeaf Total(CartAssessment c, uint itemId) => c.Totals.Single(l => l.ItemId == itemId);

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("cart shares one inventory ledger: 1 Coal on hand serves Trophy OR Ornament, not both", () =>
        {
            var c = T().AssessCart([(World.TrophyRecipe, 1), (World.OrnamentRecipe, 1)], new FakeInventory().Set(World.Coal, 1).Set(World.Hide, 1).Set(World.RareOre, 1));
            var coal = Total(c, World.Coal);
            return c.Lines.Count == 2 && coal.Need == 2 && coal.Have == 1 && coal.Missing == 1
                && c.Lines[0].Tier == EffortTier.Now                  // Trophy got the coal + hide
                && c.Lines[1].Tier == EffortTier.Easy                 // Ornament: RareOre on hand, coal from the gil vendor
                && c.Tier == EffortTier.Easy
                && c.Missing.Select(l => l.ItemId).SequenceEqual([World.Coal]);
        }),
        ("cart totals sum need/have per item across lines and across sub-craft levels; crafts multiply", () =>
        {
            // Sword x2 (Ingot 4, Leather 2) with 0 Ingot but 8 Ore + 4 Coal (-> 4 Ingot craftable) and 2 Leather; plus Arrows x1 (Feather 1).
            var c = T().AssessCart([(World.SwordRecipe, 2), (World.ArrowsRecipe, 1)], new FakeInventory().Set(World.Ore, 8).Set(World.Coal, 4).Set(World.Leather, 2));
            return Total(c, World.Ingot) is { Need: 4, Have: 0 }
                && Total(c, World.Ore) is { Need: 8, Have: 8, Missing: 0 }
                && Total(c, World.Coal) is { Need: 4, Have: 4 }
                && Total(c, World.Leather) is { Need: 2, Have: 2 }
                && Total(c, World.Feather) is { Need: 1, Have: 0 }
                && c.Missing.Select(l => l.ItemId).OrderBy(x => x).SequenceEqual(new uint[] { World.Ingot, World.Feather }.OrderBy(x => x))
                && c.Lines[0].Tier == EffortTier.Easy && c.Lines[1].Tier == EffortTier.Blocked && c.Tier == EffortTier.Blocked;
        }),
        ("cart skips unknown recipes and non-positive quantities; empty cart -> Now with no totals", () =>
        {
            var t = T();
            var c = t.AssessCart([(99999, 1), (World.SwordRecipe, 0), (World.SwordRecipe, -3)], new FakeInventory());
            var empty = t.AssessCart([], new FakeInventory());
            return c.Lines.Count == 0 && c.Totals.Count == 0 && c.Tier == EffortTier.Now
                && empty.Lines.Count == 0 && empty.Totals.Count == 0 && !empty.Missing.Any();
        }),
        ("cart line tier worsens the per-item total tier when a later line cannot cover it", () =>
        {
            // Charm needs Mystery (Unknown -> Blocked). Two Charm lines: totals for Mystery need 2, tier Blocked.
            var c = T().AssessCart([(World.CharmRecipe, 1), (World.CharmRecipe, 1)], new FakeInventory().Set(World.Mystery, 1));
            var m = Total(c, World.Mystery);
            return m.Need == 2 && m.Have == 1 && m.Tier == EffortTier.Blocked && c.Tier == EffortTier.Blocked
                && c.Lines[0].Tier == EffortTier.Now && c.Lines[1].Tier == EffortTier.Blocked;
        }),
        ("teamcraft payload matches TeamCraft's own test vector (itemId,recipeId|null,qty joined by ';')", () =>
        {
            var lines = new[] { new TeamcraftExport.Line(20545, null, 3), new TeamcraftExport.Line(17962, 32308, 1), new TeamcraftExport.Line(20247, null, 1) };
            return TeamcraftExport.Payload(lines) == "20545,null,3;17962,32308,1;20247,null,1"
                && TeamcraftExport.Encode(lines) == "MjA1NDUsbnVsbCwzOzE3OTYyLDMyMzA4LDE7MjAyNDcsbnVsbCwx"
                && TeamcraftExport.Link(lines) == "https://ffxivteamcraft.com/import/MjA1NDUsbnVsbCwzOzE3OTYyLDMyMzA4LDE7MjAyNDcsbnVsbCwx";
        }),
        ("teamcraft export merges duplicate items, drops non-positive quantities, and returns null for an empty list", () =>
        {
            var lines = new[] { new TeamcraftExport.Line(1, null, 2), new TeamcraftExport.Line(1, 10, 3), new TeamcraftExport.Line(2, null, 0), new TeamcraftExport.Line(3, null, -1) };
            return TeamcraftExport.Payload(lines) == "1,10,5"
                && TeamcraftExport.Link([]) is null
                && TeamcraftExport.Link([new TeamcraftExport.Line(2, null, 0)]) is null;
        }),
        ("leaves carry Depth: sub-craft leaves are depth+1 and precede the ingredient they serve (Pendant <- Ornament <- RareOre)", () =>
        {
            var a = T().Assess(World.PendantRecipe, new FakeInventory().Set(World.Coal, 5));
            var ids = a.Leaves.Select(l => (l.ItemId, l.Depth)).ToList();
            // Walk order: Ornament's leaves (RareOre d1, Coal d1) then Ornament d0, then Pendant's Coal d0.
            return ids.SequenceEqual([(World.RareOre, 1), (World.Coal, 1), (World.Ornament, 0), (World.Coal, 0)]);
        }),
        ("IngredientTree.Build re-attaches sub-craft leaves under their parent; Flatten yields parent-first", () =>
        {
            var a = T().Assess(World.PendantRecipe, new FakeInventory().Set(World.Coal, 5));
            var roots = IngredientTree.Build(a.Leaves);
            var flat = IngredientTree.Flatten(roots).Select(x => (x.Node.Leaf.ItemId, x.Depth)).ToList();
            return roots.Count == 2
                && roots[0].Leaf.ItemId == World.Ornament && roots[0].Children.Count == 2
                && roots[0].Children[0].Leaf.ItemId == World.RareOre && roots[0].Children[1].Leaf.ItemId == World.Coal
                && roots[1].Leaf.ItemId == World.Coal && roots[1].Children.Count == 0
                && flat.SequenceEqual([(World.Ornament, 0), (World.RareOre, 1), (World.Coal, 1), (World.Coal, 0)]);
        }),
        ("IngredientTree.Build: on-hand ingredient with a recipe has no children; two-level nesting (Sword <- Ingot <- Ore/Coal)", () =>
        {
            var t = T();
            var onHand = IngredientTree.Build(t.Assess(World.SwordRecipe, new FakeInventory().Set(World.Ingot, 2).Set(World.Leather, 1)).Leaves);
            var nested = IngredientTree.Build(t.Assess(World.SwordRecipe, new FakeInventory().Set(World.Ore, 4).Set(World.Coal, 2).Set(World.Leather, 1)).Leaves);
            return onHand.Count == 2 && onHand.All(n => n.Children.Count == 0)
                && nested.Count == 2 && nested[0].Leaf.ItemId == World.Ingot && nested[0].Children.Select(c => c.Leaf.ItemId).SequenceEqual([World.Ore, World.Coal])
                && nested[1].Leaf.ItemId == World.Leather && nested[1].Children.Count == 0
                && IngredientTree.Build([]).Count == 0;
        }),
    };
}
