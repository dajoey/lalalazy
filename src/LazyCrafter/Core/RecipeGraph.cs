using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Recipe expansion and craftability over an <see cref="IGameData"/> snapshot (Plan §Phase 1, tasks 1-2).
/// <para>
/// <see cref="Expand"/> turns a recipe id into a <see cref="RecipeNode"/> tree. An ingredient that is
/// itself the result of some recipe gets a <see cref="RecipeNode.Ingredient.SubRecipe"/>; when several
/// recipes yield the same item, one on the parent's job wins, otherwise the first (lowest id). A recipe
/// already on the current expansion path is not re-entered, so cycles produce a finite tree.
/// Results are memoized for the lifetime of the graph (the plan's "per session" cache - build a new
/// graph when the recipe universe changes).
/// </para>
/// <para>
/// <see cref="HowMany"/> is Artisan's <c>NumberCraftable</c> rule re-stated: for each ingredient,
/// <c>(have + craftable-from-sub-recipe) / amount</c>; take the minimum; multiply by the result amount.
/// Inventory is a plain count already filtered by the enabled sources (<see cref="IInventory"/>).
/// </para>
/// </summary>
public sealed class RecipeGraph
{
    private readonly Dictionary<uint, RecipeRow> _byRecipe;
    private readonly Dictionary<uint, List<RecipeRow>> _byResult;
    private readonly Dictionary<uint, RecipeNode?> _expanded = new();

    public RecipeGraph(IGameData data)
    {
        _byRecipe = new Dictionary<uint, RecipeRow>();
        _byResult = new Dictionary<uint, List<RecipeRow>>();
        foreach (var row in data.Recipes())
        {
            _byRecipe[row.RecipeId] = row;
            if (!_byResult.TryGetValue(row.ResultItemId, out var list))
                _byResult[row.ResultItemId] = list = new List<RecipeRow>();
            list.Add(row);
        }
        foreach (var list in _byResult.Values)
            list.Sort((a, b) => a.RecipeId.CompareTo(b.RecipeId));
    }

    /// <summary>All known recipe ids.</summary>
    public IEnumerable<uint> RecipeIds => _byRecipe.Keys;

    public RecipeRow? Row(uint recipeId) => _byRecipe.TryGetValue(recipeId, out var r) ? r : null;

    /// <summary>Recipes whose result is <paramref name="itemId"/>, preferring <paramref name="preferJob"/>.</summary>
    public RecipeRow? RecipeFor(uint itemId, uint? preferJob = null)
    {
        if (!_byResult.TryGetValue(itemId, out var list) || list.Count == 0) return null;
        if (preferJob is { } job)
            foreach (var r in list)
                if (r.JobId == job) return r;
        return list[0];
    }

    public bool IsCraftable(uint itemId) => _byResult.ContainsKey(itemId);

    /// <summary>Expand a recipe into its ingredient tree; <c>null</c> when the id is unknown.</summary>
    public RecipeNode? Expand(uint recipeId)
    {
        if (_expanded.TryGetValue(recipeId, out var cached)) return cached;
        var node = ExpandInner(recipeId, new HashSet<uint>());
        _expanded[recipeId] = node;
        return node;
    }

    private RecipeNode? ExpandInner(uint recipeId, HashSet<uint> path)
    {
        if (!_byRecipe.TryGetValue(recipeId, out var row)) return null;
        if (!path.Add(recipeId)) return null;   // cycle: caller treats this ingredient as a leaf
        try
        {
            var ingredients = new List<RecipeNode.Ingredient>(row.Ingredients.Count);
            foreach (var (itemId, amount) in row.Ingredients)
            {
                if (amount <= 0) continue;
                RecipeNode? sub = null;
                var subRow = RecipeFor(itemId, row.JobId);
                if (subRow is not null && !path.Contains(subRow.RecipeId))
                    sub = ExpandInner(subRow.RecipeId, path);
                ingredients.Add(new RecipeNode.Ingredient(itemId, amount, sub));
            }
            return new RecipeNode(row.RecipeId, row.ResultItemId, row.ResultAmount, row.JobId, row.Level, ingredients);
        }
        finally
        {
            path.Remove(recipeId);
        }
    }

    /// <summary>Number of result items craftable from <paramref name="inv"/>, counting sub-crafts.</summary>
    public int HowMany(uint recipeId, IInventory inv)
    {
        var node = Expand(recipeId);
        return node is null ? 0 : HowMany(node, inv);
    }

    /// <summary>Same as <see cref="HowMany(uint, IInventory)"/> over an already expanded node.</summary>
    public static int HowMany(RecipeNode node, IInventory inv)
    {
        if (node.Ingredients.Count == 0) return 0;
        var crafts = int.MaxValue;
        foreach (var ing in node.Ingredients)
        {
            var available = (long)inv.Count(ing.ItemId);
            if (ing.SubRecipe is not null) available += HowMany(ing.SubRecipe, inv);
            var possible = (int)Math.Min(int.MaxValue, available / ing.Amount);
            if (possible < crafts) crafts = possible;
            if (crafts == 0) return 0;
        }
        return (int)Math.Min(int.MaxValue, (long)crafts * Math.Max(1, node.ResultAmount));
    }

    public bool CanCraft(uint recipeId, IInventory inv) => HowMany(recipeId, inv) > 0;
}
