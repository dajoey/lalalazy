using LazyCrafter.Adapters;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Catalog;

/// <summary>
/// Turns Core results into <see cref="CatalogRow"/>s. Deliberately free of Dalamud types (only Core + the
/// sheet-backed <see cref="LuminaGameData"/>) so <c>tests/LazyCrafter.Probe</c> can run the exact pass the
/// <see cref="CatalogService"/> worker runs, against the installed game files, without the client.
/// </summary>
public sealed class CatalogBuilder
{
    public LuminaGameData Data { get; }
    public RecipeGraph Graph { get; }
    public Tiering Tiering { get; }
    public ProfitModel Profit { get; }
    public ScripValue Scrip { get; }
    public DesynthValue Desynth { get; }

    public CatalogBuilder(LuminaGameData data, RecipeGraph graph, IReadOnlyList<RetainerStats> retainers, IReadOnlySet<uint>? gatheredItems, RevenueBasis basis)
    {
        Data = data;
        Graph = graph;
        var ventures = new VentureResolver(data);
        Tiering = new Tiering(graph, new SourceClassifier(data, graph, ventures, retainers, gatheredItems));
        Profit = new ProfitModel(data, graph) { Basis = basis };
        Scrip = new ScripValue(data);
        Desynth = new DesynthValue(data);
    }

    /// <summary>
    /// Every item a full pass will ask the inventory about: ingredients and sub-craft ingredients. Results are not
    /// counted (nothing in Core reads a result's stock), which roughly halves the AllaganTools IPC calls per pass.
    /// </summary>
    public HashSet<uint> AllItemIds()
    {
        var ids = new HashSet<uint>();
        foreach (var id in Graph.RecipeIds) CollectIngredients(Graph.Expand(id), ids);
        return ids;
    }

    public static void CollectIngredients(RecipeNode? node, HashSet<uint> into)
    {
        if (node is null) return;
        foreach (var ing in node.Ingredients)
        {
            into.Add(ing.ItemId);
            if (ing.SubRecipe is not null) CollectIngredients(ing.SubRecipe, into);
        }
    }

    public CatalogRow BuildRow(RecipeRow def, RecipeAssessment a, IInventory inv, IPriceSource prices, double tax,
        IReadOnlyDictionary<uint, int> jobs, IReadOnlySet<uint> logComplete)
    {
        var gd = Data;
        var marketable = gd.IsMarketable(def.ResultItemId);
        var canHq = gd.CanBeHq(def.ResultItemId);
        var nq = marketable ? Profit.Evaluate(def.RecipeId, inv, prices, tax, hq: false) : null;
        var hq = marketable && canHq ? Profit.Evaluate(def.RecipeId, inv, prices, tax, hq: true) : null;
        var jobLevel = jobs.TryGetValue(def.JobId, out var jl) ? jl : 0;
        var scrip = Scrip.Evaluate(def.ResultItemId, jobLevel > 0 ? jobLevel : int.MaxValue)?.ScripPerCraft ?? 0;
        var desynth = gd.IsDesynthable(def.ResultItemId) ? Desynth.Evaluate(def.ResultItemId, prices)?.ExpectedValue : null;
        var exp = jobLevel > 0 ? LevelingScore.ExpPerCraft(jobLevel, def.Level) : 0;
        return new CatalogRow(
            def.RecipeId, def.ResultItemId, Math.Max(1, def.ResultAmount), gd.ItemName(def.ResultItemId), def.JobId, gd.JobAbbr(def.JobId), def.Level, jobLevel,
            a.Tier, a.HowMany, a.Leaves, MissingSummary(a.Leaves), nq, hq, marketable, canHq, scrip, desynth,
            logComplete.Contains(def.RecipeId), exp);
    }

    /// <summary>Top-level shortfalls only; sub-craft leaves are folded into the ingredient they serve.</summary>
    public string MissingSummary(IReadOnlyList<IngredientLeaf> leaves)
    {
        var parts = leaves.Where(l => l.Depth == 0 && l.Missing > 0).Select(l => $"{Data.ItemName(l.ItemId)} x{l.Missing}").ToList();
        if (parts.Count == 0) return "";
        return parts.Count <= 3 ? string.Join(", ", parts) : string.Join(", ", parts.Take(3)) + $" +{parts.Count - 3}";
    }

    /// <summary>Frozen inventory counts for one pass.</summary>
    public sealed class DictInventory : IInventory
    {
        private readonly Dictionary<uint, int> _counts;
        public DictInventory(Dictionary<uint, int> counts) => _counts = counts;
        public int Count(uint itemId) => _counts.TryGetValue(itemId, out var c) ? c : 0;
    }
}
