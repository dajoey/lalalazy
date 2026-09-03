using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Decides where a missing ingredient can come from (Plan §Phase 1 task 3, Scope §3.2).
/// <para>
/// Returns every applicable <see cref="SourceKind"/> for a leaf, in enum order. <see cref="SourceKind.OnHand"/>
/// is exclusive: when <c>have >= need</c> nothing else is consulted. Otherwise each lookup contributes
/// independently: a recipe exists → <see cref="SourceKind.SubCraft"/>; <c>GilShopItem</c> →
/// <see cref="SourceKind.GilVendor"/>; a currency shop → <see cref="SourceKind.SpecialShop"/>; a gather
/// node → <see cref="SourceKind.RegularNode"/> or <see cref="SourceKind.TimedNode"/> (timed, or any
/// non-Regular node type); fishing → <see cref="SourceKind.Fish"/>; a venture one of the supplied
/// retainers qualifies for (<see cref="VentureResolver"/>) → <see cref="SourceKind.Venture"/>; marketable →
/// <see cref="SourceKind.Market"/>; a known drop/voyage source → <see cref="SourceKind.Drop"/>. When nothing
/// matches the single result is <see cref="SourceKind.Unknown"/>.
/// </para>
/// </summary>
public sealed class SourceClassifier
{
    private readonly IGameData _data;
    private readonly RecipeGraph _graph;
    private readonly VentureResolver _ventures;
    private readonly IReadOnlyList<RetainerStats> _retainers;
    private readonly IReadOnlySet<uint>? _gatheredItems;

    public SourceClassifier(
        IGameData data,
        RecipeGraph graph,
        VentureResolver ventures,
        IReadOnlyList<RetainerStats> retainers,
        IReadOnlySet<uint>? gatheredItems = null)
    {
        _data = data;
        _graph = graph;
        _ventures = ventures;
        _retainers = retainers;
        _gatheredItems = gatheredItems;
    }

    public IReadOnlyList<SourceKind> Classify(uint itemId, int need, int have)
    {
        if (have >= need) return [SourceKind.OnHand];

        var kinds = new List<SourceKind>(4);
        if (_graph.IsCraftable(itemId)) kinds.Add(SourceKind.SubCraft);
        if (_data.IsGilVendor(itemId, out _)) kinds.Add(SourceKind.GilVendor);
        if (_data.IsSpecialShop(itemId)) kinds.Add(SourceKind.SpecialShop);

        var gather = _data.Gather(itemId);
        if (gather is not null)
            kinds.Add(gather.Timed || gather.NodeType != NodeType.Regular ? SourceKind.TimedNode : SourceKind.RegularNode);

        if (_data.IsFish(itemId)) kinds.Add(SourceKind.Fish);
        if (_retainers.Count > 0 && _ventures.ResolveBest(itemId, _retainers, _gatheredItems) is not null)
            kinds.Add(SourceKind.Venture);
        if (_data.IsMarketable(itemId)) kinds.Add(SourceKind.Market);
        if (_data.IsDrop(itemId)) kinds.Add(SourceKind.Drop);

        if (kinds.Count == 0) kinds.Add(SourceKind.Unknown);
        return kinds;
    }

    /// <summary>Convenience: classify with the inventory count looked up.</summary>
    public IReadOnlyList<SourceKind> Classify(uint itemId, int need, IInventory inv) =>
        Classify(itemId, need, inv.Count(itemId));
}
