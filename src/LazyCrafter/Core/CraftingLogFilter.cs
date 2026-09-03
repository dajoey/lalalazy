namespace LazyCrafter.Core;

/// <summary>What the game knows about the character's crafting log (Phase 3 adapter: <c>PlayerState.IsRecipeComplete</c>).</summary>
public interface ICraftingLog
{
    bool IsRecipeComplete(uint recipeId);
}

/// <summary>
/// Crafting-log completion predicate (Plan §Phase 2 task 6, Scope §5.4): <see cref="NotYetCrafted"/> is the
/// filter for the "Log completion" tab; <see cref="Remaining"/> lists what is left for a job, cheapest-first
/// when a cost function is supplied. Master-recipe-book recipes are still "in the log" for our purposes -
/// the adapter decides what <c>IsRecipeComplete</c> means.
/// </summary>
public sealed class CraftingLogFilter
{
    private readonly RecipeGraph _graph;
    private readonly ICraftingLog _log;

    public CraftingLogFilter(RecipeGraph graph, ICraftingLog log)
    {
        _graph = graph;
        _log = log;
    }

    /// <summary>True when the recipe exists and has never been completed.</summary>
    public bool NotYetCrafted(uint recipeId) => _graph.Row(recipeId) is not null && !_log.IsRecipeComplete(recipeId);

    /// <summary>Predicate form for LINQ / table filters.</summary>
    public Func<uint, bool> Predicate => NotYetCrafted;

    /// <summary>
    /// Uncompleted recipes for <paramref name="jobId"/> (all jobs when <c>null</c>) at or below <paramref name="maxLevel"/>,
    /// ordered by <paramref name="cost"/> ascending when given (unknown cost = last), else by level then id.
    /// </summary>
    public IEnumerable<uint> Remaining(uint? jobId = null, int maxLevel = int.MaxValue, Func<uint, long?>? cost = null)
    {
        var ids = _graph.RecipeIds.Where(id =>
        {
            var row = _graph.Row(id)!;
            return (jobId is null || row.JobId == jobId) && row.Level <= maxLevel && NotYetCrafted(id);
        });
        if (cost is null)
            return ids.OrderBy(id => _graph.Row(id)!.Level).ThenBy(id => id).ToList();
        return ids.Select(id => (Id: id, Cost: cost(id)))
                  .OrderBy(x => x.Cost is null ? 1 : 0)
                  .ThenBy(x => x.Cost ?? long.MaxValue)
                  .ThenBy(x => _graph.Row(x.Id)!.Level)
                  .ThenBy(x => x.Id)
                  .Select(x => x.Id)
                  .ToList();
    }

    /// <summary>Completed / total for a job (all jobs when <c>null</c>).</summary>
    public (int Done, int Total) Progress(uint? jobId = null)
    {
        int done = 0, total = 0;
        foreach (var id in _graph.RecipeIds)
        {
            if (jobId is not null && _graph.Row(id)!.JobId != jobId) continue;
            total++;
            if (_log.IsRecipeComplete(id)) done++;
        }
        return (done, total);
    }
}
