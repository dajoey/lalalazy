using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace LazyGearCollector;

/// <summary>A single "you can receive this for those costs" offer from a SpecialShop row.</summary>
public sealed record ShopOffer(
    uint ShopId,
    string ShopName,
    uint ReceiveItemId,
    uint ReceiveCount,
    IReadOnlyList<CostLine> Costs);

/// <summary>
/// A reverse index over the whole SpecialShop sheet: "what trades produce item X, and at what price".
/// Built once at load. Everything the plugin knows about prices comes from here, so nothing is
/// hardcoded and the numbers stay right when Square Enix retunes a patch.
/// </summary>
public sealed class ShopGraph
{
    private readonly Dictionary<uint, List<ShopOffer>> _producedBy = new();
    private readonly IDataManager _data;

    public int OfferCount { get; private set; }

    public ShopGraph(IDataManager data)
    {
        _data = data;
        Build();
    }

    public IReadOnlyList<ShopOffer> OffersFor(uint itemId) =>
        _producedBy.TryGetValue(itemId, out var list) ? list : Array.Empty<ShopOffer>();

    private void Build()
    {
        var shops = _data.GetExcelSheet<SpecialShop>();
        if (shops == null)
        {
            Plugin.PluginLog.Error("ShopGraph: SpecialShop sheet unavailable");
            return;
        }

        foreach (var shop in shops)
        {
            string shopName;
            try { shopName = shop.Name.ExtractText(); }
            catch { shopName = string.Empty; }

            foreach (var entry in shop.Item)
            {
                var costs = new List<CostLine>();
                foreach (var c in entry.ItemCosts)
                {
                    // CostType 0 means ItemCost is a real Item row. Other cost types (notably 2)
                    // encode a special-currency *index*, not an item id - decoding those as items
                    // yields nonsense like "Ice Shard x495" on tomestone shops. Skip them.
                    if (c.CostType != 0) continue;
                    var costId = c.ItemCost.RowId;
                    if (costId == 0 || c.CurrencyCost == 0) continue;
                    costs.Add(new CostLine(costId, ItemName(costId), c.CurrencyCost));
                }

                if (costs.Count == 0) continue;

                foreach (var r in entry.ReceiveItems)
                {
                    var recvId = r.Item.RowId;
                    if (recvId == 0) continue;

                    var offer = new ShopOffer(shop.RowId, shopName, recvId,
                        r.ReceiveCount == 0 ? 1 : r.ReceiveCount, costs);

                    if (!_producedBy.TryGetValue(recvId, out var list))
                        _producedBy[recvId] = list = new List<ShopOffer>();
                    list.Add(offer);
                    OfferCount++;
                }
            }
        }

        Plugin.PluginLog.Info($"ShopGraph: indexed {OfferCount} offers across {_producedBy.Count} obtainable items");
    }

    public string ItemName(uint itemId)
    {
        if (itemId == 0) return "";
        var sheet = _data.GetExcelSheet<Item>();
        if (sheet == null || !sheet.TryGetRow(itemId, out var item)) return $"Item#{itemId}";
        var n = item.Name.ExtractText();
        return string.IsNullOrWhiteSpace(n) ? $"Item#{itemId}" : n;
    }
}
