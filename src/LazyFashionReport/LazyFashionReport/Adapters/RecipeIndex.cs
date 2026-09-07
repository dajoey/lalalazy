using Dalamud.Plugin.Services;
using LazyFashionReport.Core;
using Lumina.Excel.Sheets;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Recipe-sheet index: item id -> the recipe Artisan should run to produce it. Built once off the
/// framework thread (a few hundred ms over the whole sheet), answered from a dictionary afterwards.
/// Pick rule when several recipes produce the same item: highest recipe level first (master-book
/// recipes are the ones current crafters know), tie-break on the lowest RowId (deterministic).
/// </summary>
internal sealed class RecipeIndex
{
    private readonly Dictionary<uint, RecipeOption> _byItem = new();

    public int Count => _byItem.Count;

    public static RecipeIndex Load(IDataManager data, IPluginLog log)
    {
        var index = new RecipeIndex();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var recipes = data.GetExcelSheet<Recipe>();
            foreach (var r in recipes)
            {
                if (r.RowId == 0 || r.ItemResult.RowId == 0) continue;
                var level = r.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0;
                var option = new RecipeOption(r.RowId, (int)r.CraftType.RowId, (short)level);
                if (index._byItem.TryGetValue(r.ItemResult.RowId, out var best))
                {
                    if (option.Level > best.Level || (option.Level == best.Level && option.RecipeId < best.RecipeId))
                        index._byItem[r.ItemResult.RowId] = option;
                }
                else
                {
                    index._byItem[r.ItemResult.RowId] = option;
                }
            }
            log.Information($"[LFR] recipe index: {index._byItem.Count} craftable items in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[LFR] recipe index failed to build; fetch-missing craft actions stay hidden");
        }
        return index;
    }

    public RecipeOption? ForItem(uint itemId) =>
        _byItem.TryGetValue(itemId, out var r) ? r : null;
}
