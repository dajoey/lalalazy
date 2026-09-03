using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Harness;

internal static class RecipeGraphTests
{
    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        ("Expand builds the sub-recipe tree (Sword -> Ingot -> Ore/Coal, Leather -> Hide)", () =>
        {
            var g = new RecipeGraph(World.Build());
            var sword = g.Expand(World.SwordRecipe)!;
            var ingot = sword.Ingredients.Single(i => i.ItemId == World.Ingot);
            var leather = sword.Ingredients.Single(i => i.ItemId == World.Leather);
            return sword.ResultItemId == World.Sword
                && ingot.Amount == 2 && ingot.SubRecipe is not null
                && ingot.SubRecipe.Ingredients.Select(i => i.ItemId).OrderBy(x => x).SequenceEqual([World.Ore, World.Coal])
                && ingot.SubRecipe.Ingredients.All(i => i.SubRecipe is null)
                && leather.SubRecipe is not null
                && leather.SubRecipe.Ingredients.Single().ItemId == World.Hide;
        }),
        ("Expand prefers a same-job sub-recipe, else any job", () =>
        {
            var g = new RecipeGraph(World.Build());
            var sword = g.Expand(World.SwordRecipe)!;
            var ingot = sword.Ingredients.Single(i => i.ItemId == World.Ingot).SubRecipe!;
            var leather = sword.Ingredients.Single(i => i.ItemId == World.Leather).SubRecipe!;
            return ingot.RecipeId == World.IngotBsm && ingot.JobId == World.Bsm     // BSM parent -> BSM ingot, not ARM
                && leather.RecipeId == World.LeatherLtw && leather.JobId == World.Ltw; // no BSM leather recipe -> LTW
        }),
        ("Expand terminates on a recipe cycle and yields a finite tree", () =>
        {
            var g = new RecipeGraph(World.Build());
            var a = g.Expand(World.CycleARecipe)!;
            var b = a.Ingredients.Single().SubRecipe;   // CycleB expanded once…
            return b is not null && b.RecipeId == World.CycleBRecipe
                && b.Ingredients.Single().ItemId == World.CycleA
                && b.Ingredients.Single().SubRecipe is null;  // …but the back edge to A is cut.
        }),
        ("Expand is memoized per graph instance and unknown ids return null", () =>
        {
            var g = new RecipeGraph(World.Build());
            return ReferenceEquals(g.Expand(World.SwordRecipe), g.Expand(World.SwordRecipe))
                && g.Expand(99999) is null;
        }),
        ("HowMany: everything on hand -> N (Sword: 6 Ingot, 3 Leather -> 3)", () =>
        {
            var g = new RecipeGraph(World.Build());
            var inv = new FakeInventory().Set(World.Ingot, 6).Set(World.Leather, 3);
            return g.HowMany(World.SwordRecipe, inv) == 3 && g.CanCraft(World.SwordRecipe, inv);
        }),
        ("HowMany counts craftable sub-recipes (Artisan NumberCraftable semantics)", () =>
        {
            // Ingot: Ore 10, Coal 4 -> min(10/2, 4/1) = 4 craftable. Sword: (1 on hand + 4) / 2 = 2; Leather 3/1 -> 2.
            var g = new RecipeGraph(World.Build());
            var inv = new FakeInventory().Set(World.Ore, 10).Set(World.Coal, 4).Set(World.Ingot, 1).Set(World.Leather, 3);
            return g.HowMany(World.IngotBsm, inv) == 4 && g.HowMany(World.SwordRecipe, inv) == 2;
        }),
        ("HowMany multiplies by the recipe's result amount (Arrows x3: 2 Feather -> 6)", () =>
        {
            var g = new RecipeGraph(World.Build());
            return g.HowMany(World.ArrowsRecipe, new FakeInventory().Set(World.Feather, 2)) == 6;
        }),
        ("HowMany is 0 / CanCraft false with an empty inventory, and 0 on a cycle", () =>
        {
            var g = new RecipeGraph(World.Build());
            var empty = new FakeInventory();
            return g.HowMany(World.SwordRecipe, empty) == 0 && !g.CanCraft(World.SwordRecipe, empty)
                && g.HowMany(World.CycleARecipe, empty) == 0;
        }),
    };
}
