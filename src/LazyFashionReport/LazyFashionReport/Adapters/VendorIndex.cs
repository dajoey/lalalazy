using System.Numerics;
using Dalamud.Plugin.Services;
using LazyFashionReport.Core;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Services;
using ENpcPlace = LuminaSupplemental.Excel.Model.ENpcPlace;
using ENpcShop = LuminaSupplemental.Excel.Model.ENpcShop;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Vendor index for the buy leg (v0.3.0.0), ported from LazyCrafter's VendorLocator (the
/// verified implementation, cards t_731ea0e7 / t_b431de3a):
/// GilShopItem subrows give item -&gt; gil shops; ENpcBase.ENpcData handlers give shop -&gt; NPCs
/// (GilShop ids 262144.. and SpecialShop ids 1769472.. are disjoint, so membership classifies);
/// the supplemental ENpcShop CSV widens the NPC set; ENpcPlace + a Level-sheet fallback place
/// the NPCs in map coordinates; MapMarker (DataType 3) places each territory's aetherytes.
/// Best-vendor ranking is nearest-aetheryte (walk distance), the simpler of LazyCrafter's two
/// legacy metrics — for a single fashion piece the zone/teleport-fare refinement does not
/// change which NPC to name.
/// Built lazily off the framework thread (~1 s); every lookup is a dictionary answer afterwards.
/// A supplemental load failure degrades to "no placed vendor" and is reported, never thrown.
/// </summary>
internal sealed class VendorIndex
{
    public sealed record PlacedVendor(uint NpcId, string NpcName, string ZoneName, uint TerritoryId, uint MapId, float X, float Y, float WalkFromAetheryte);

    private readonly IDataManager _data;
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly object _lock = new();
    private bool _built;

    private readonly Dictionary<uint, List<uint>> _shopsByItem = new();       // itemId -> GilShop ids
    private readonly Dictionary<uint, uint> _gilPriceByItem = new();          // itemId -> Item.PriceMid
    private readonly Dictionary<uint, List<uint>> _npcsByShop = new();        // shop id -> NPC ids
    private readonly Dictionary<uint, List<ShopOffer>> _offersByItem = new(); // itemId -> costed special-shop offers
    private readonly Dictionary<uint, List<ENpcPlace>> _placesByNpc = new();
    private readonly Dictionary<uint, string> _npcNames = new();
    private readonly Dictionary<uint, string> _territoryNames = new();
    private readonly Dictionary<uint, List<(uint Id, string Name, Vector2 Map)>> _aetherytesByTerritory = new();

    public sealed record ShopOffer(uint ShopId, string ShopName, uint ItemId, int ReceiveQuantity, IReadOnlyList<Core.ShopCost> Costs);

    public VendorIndex(IDataManager data, Action<string> log, Action<string>? warn = null)
    {
        _data = data;
        _log = log;
        _warn = warn ?? log;
    }

    public bool Built => _built;

    /// <summary>Force the one-time index build (call off the framework thread, e.g. from the
    /// service's startup task — the build is ~1 s of sheet walking).</summary>
    public void EnsureBuiltPublic() => EnsureBuilt();

    // ------------------------------------------------------------------ public lookups

    /// <summary>Best placed gil vendor for an item (nearest aetheryte), or null when no placed
    /// vendor sells it. The gil price comes back even when nobody is placed (price is real).</summary>
    public (uint Price, PlacedVendor? Vendor)? GilVendorFor(uint itemId)
    {
        EnsureBuilt();
        if (!_shopsByItem.TryGetValue(itemId, out var shops))
            return _gilPriceByItem.TryGetValue(itemId, out var p) ? (p, null) : null;
        var price = _gilPriceByItem.TryGetValue(itemId, out var priceMid) ? priceMid : 0;
        return (price, BestVendor(shops));
    }

    /// <summary>Best placed, costed special-shop offer for an item, or null. Unplaced offers
    /// are dropped entirely (LazyCrafter D1: an unplaced currency vendor is a dead end).</summary>
    public (ShopOffer Offer, PlacedVendor Vendor)? SpecialShopFor(uint itemId)
    {
        EnsureBuilt();
        if (!_offersByItem.TryGetValue(itemId, out var offers) || offers.Count == 0) return null;
        (ShopOffer Offer, PlacedVendor Vendor, float Walk)? best = null;
        foreach (var offer in offers)
        {
            if (!_npcsByShop.TryGetValue(offer.ShopId, out var npcs)) continue;
            foreach (var npc in npcs)
            {
                if (!_placesByNpc.TryGetValue(npc, out var places)) continue;
                foreach (var p in places)
                {
                    if (!_aetherytesByTerritory.TryGetValue(p.TerritoryTypeId, out var aeths) || aeths.Count == 0) continue;
                    var minWalk = float.MaxValue;
                    foreach (var a in aeths)
                        minWalk = Math.Min(minWalk, Vector2.Distance(p.Position, a.Map));
                    var vendor = new PlacedVendor(npc,
                        _npcNames.GetValueOrDefault(npc, $"NPC {npc}"),
                        _territoryNames.GetValueOrDefault(p.TerritoryTypeId, $"zone {p.TerritoryTypeId}"),
                        p.TerritoryTypeId, p.MapId, p.Position.X, p.Position.Y, minWalk);
                    if (best is null || minWalk < best.Value.Walk)
                        best = (offer, vendor, minWalk);
                }
            }
        }
        return best is { } b ? (b.Offer, b.Vendor) : null;
    }

    private PlacedVendor? BestVendor(List<uint> shops)
    {
        PlacedVendor? best = null;
        var bestWalk = float.MaxValue;
        var seenNpc = new HashSet<uint>();
        foreach (var shop in shops)
        {
            if (!_npcsByShop.TryGetValue(shop, out var npcs)) continue;
            foreach (var npc in npcs)
            {
                if (!seenNpc.Add(npc)) continue;
                if (!_placesByNpc.TryGetValue(npc, out var places)) continue;
                foreach (var p in places)
                {
                    if (!_aetherytesByTerritory.TryGetValue(p.TerritoryTypeId, out var aeths) || aeths.Count == 0) continue;
                    var minWalk = float.MaxValue;
                    foreach (var a in aeths)
                        minWalk = Math.Min(minWalk, Vector2.Distance(p.Position, a.Map));
                    if (best is null || minWalk < bestWalk)
                    {
                        best = new PlacedVendor(npc,
                            _npcNames.GetValueOrDefault(npc, $"NPC {npc}"),
                            _territoryNames.GetValueOrDefault(p.TerritoryTypeId, $"zone {p.TerritoryTypeId}"),
                            p.TerritoryTypeId, p.MapId, p.Position.X, p.Position.Y, minWalk);
                        bestWalk = minWalk;
                    }
                }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ index build (LazyCrafter's verified pass)

    private void EnsureBuilt()
    {
        if (_built) return;
        lock (_lock)
        {
            if (_built) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Build(); }
            catch (Exception ex) { _warn($"[LFR] vendor index build failed: {ex.Message} - buy sources degrade to craft/market"); }
            _built = true;
            _log($"[LFR] vendor index: {_shopsByItem.Count} gil-shop items, {_offersByItem.Count} special-shop items, {_placesByNpc.Count} placed NPCs in {sw.ElapsedMilliseconds} ms");
        }
    }

    private void Fail(string line) =>
        _warn($"[LFR] LuminaSupplemental {line} - vendor placements incomplete; buy labels fall back to craft/market. This is a PACKAGING fault if it persists.");

    private void Build()
    {
        var lumina = _data.GameData;
        if (lumina is null) throw new InvalidOperationException("no Lumina GameData");

        // item -> gil shops (+ the gil price from Item.PriceMid).
        var gilShopItems = lumina.GetSubrowExcelSheet<GilShopItem>() ?? throw new InvalidOperationException("GilShopItem sheet missing");
        foreach (var page in gilShopItems)
            foreach (var row in page)
            {
                var id = row.Item.RowId;
                if (id == 0) continue;
                (_shopsByItem.TryGetValue(id, out var l) ? l : _shopsByItem[id] = new List<uint>()).Add(row.RowId);
            }
        var items = lumina.GetExcelSheet<Item>();
        foreach (var id in _shopsByItem.Keys)
            if (items.TryGetRow(id, out var it))
                _gilPriceByItem[id] = it.PriceMid;

        // SpecialShop -> costed offers per item (LazyCrafter's exact parsing: CostType 3 is a
        // "special bucket" not an item; CurrencyCost 0 rows are padding; free rows are knowable
        // but not offers).
        var shopNames = new Dictionary<uint, string>();
        var plurals = new Dictionary<uint, string>();
        foreach (var shop in lumina.GetExcelSheet<SpecialShop>())
        {
            shopNames[shop.RowId] = shop.Name.ExtractText();
            foreach (var entry in shop.Item)
            {
                var costs = new List<Core.ShopCost>(2);
                foreach (var cost in entry.ItemCosts)
                {
                    var costId = cost.ItemCost.RowId;
                    var qty = (long)cost.CurrencyCost;
                    if (costId == 0 || qty <= 0 || cost.CostType == 3) continue;
                    var name = items.TryGetRow(costId, out var ci) ? ci.Name.ExtractText() : $"#{costId}";
                    var plural = items.TryGetRow(costId, out var cip) ? cip.Plural.ExtractText() : "";
                    costs.Add(new Core.ShopCost(costId, name, (int)Math.Min(int.MaxValue, qty), plural));
                }
                foreach (var received in entry.ReceiveItems)
                {
                    var id = received.Item.RowId;
                    if (id == 0 || costs.Count == 0) continue;
                    var recv = (int)Math.Max(1, Math.Min(int.MaxValue, (long)received.ReceiveCount));
                    (_offersByItem.TryGetValue(id, out var ol) ? ol : _offersByItem[id] = new List<ShopOffer>())
                        .Add(new ShopOffer(shop.RowId, shopNames[shop.RowId], id, recv, costs));
                }
            }
        }

        // shop -> NPCs (ENpcBase handlers; id ranges classify gil vs special).
        var npcBase = lumina.GetExcelSheet<ENpcBase>() ?? throw new InvalidOperationException("ENpcBase sheet missing");
        var shopIds = new HashSet<uint>(_shopsByItem.Values.SelectMany(x => x));
        shopIds.UnionWith(_offersByItem.Values.SelectMany(o => o.Select(x => x.ShopId)));
        foreach (var b in npcBase)
            foreach (var h in b.ENpcData)
            {
                if (h.RowId == 0 || !shopIds.Contains(h.RowId)) continue;
                (_npcsByShop.TryGetValue(h.RowId, out var nl) ? nl : _npcsByShop[h.RowId] = new List<uint>()).Add(b.RowId);
            }

        // Supplemental ENpcShop widens the gil-shop NPC set.
        try
        {
            var shops = CsvLoader.LoadResource<ENpcShop>(CsvLoader.ENpcShopResourceName, true, out _, out _);
            if (shops.Count == 0) Fail($"{CsvLoader.ENpcShopResourceName}: no rows");
            foreach (var s in shops)
            {
                if (!shopIds.Contains(s.ShopId)) continue;
                var l = _npcsByShop.TryGetValue(s.ShopId, out var x) ? x : _npcsByShop[s.ShopId] = new List<uint>();
                if (!l.Contains(s.ENpcResidentId)) l.Add(s.ENpcResidentId);
            }
        }
        catch (Exception ex) { Fail($"{CsvLoader.ENpcShopResourceName}: {ex.Message}"); }

        // NPC placements: supplemental ENpcPlace, Level-sheet fallback (Type 8) for the rest.
        var shopNpcs = new HashSet<uint>(_npcsByShop.Values.SelectMany(x => x));
        try
        {
            var places = CsvLoader.LoadResource<ENpcPlace>(CsvLoader.ENpcPlaceResourceName, true, out _, out _);
            if (places.Count == 0) Fail($"{CsvLoader.ENpcPlaceResourceName}: no rows");
            foreach (var p in places)
            {
                if (!shopNpcs.Contains(p.ENpcResidentId)) continue;
                (_placesByNpc.TryGetValue(p.ENpcResidentId, out var l) ? l : _placesByNpc[p.ENpcResidentId] = new List<ENpcPlace>()).Add(p);
            }
        }
        catch (Exception ex) { Fail($"{CsvLoader.ENpcPlaceResourceName}: {ex.Message}"); }

        var maps = lumina.GetExcelSheet<Map>();
        var fromLevel = 0;
        foreach (var lv in lumina.GetExcelSheet<Level>())
        {
            if (lv.Type != 8 || lv.Object.RowId == 0 || !shopNpcs.Contains(lv.Object.RowId) || _placesByNpc.ContainsKey(lv.Object.RowId)) continue;
            if (lv.Map.ValueNullable is not { } map || lv.Territory.RowId == 0) continue;
            var pos = WorldToMap(new Vector2(lv.X, lv.Z), map);
            _placesByNpc[lv.Object.RowId] = new List<ENpcPlace> { new(lv.Object.RowId, lv.Territory.RowId, map.RowId, map.PlaceName.RowId, pos) };
            fromLevel++;
        }
        if (fromLevel > 0) _log($"[LFR] vendor index: {fromLevel} shop NPCs placed from the Level sheet");

        // Names.
        var residents = lumina.GetExcelSheet<ENpcResident>();
        foreach (var npc in _placesByNpc.Keys)
            if (residents.TryGetRow(npc, out var r)) _npcNames[npc] = r.Singular.ExtractText();
        var territories = lumina.GetExcelSheet<TerritoryType>() ?? throw new InvalidOperationException("TerritoryType sheet missing");

        // Aetherytes by territory in map coords (MapMarker DataType 3, GBR marker formula).
        var markers = lumina.GetSubrowExcelSheet<MapMarker>() ?? throw new InvalidOperationException("MapMarker sheet missing");
        var markerByAetheryte = new Dictionary<(uint Range, uint Aetheryte), (float X, float Y)>();
        foreach (var page in markers)
            foreach (var m in page)
                if (m.DataType == 3 && m.DataKey.RowId != 0)
                    markerByAetheryte.TryAdd((m.RowId, m.DataKey.RowId), (m.X, m.Y));
        var mapsByTerritory = new Dictionary<uint, List<Map>>();
        foreach (var mp in maps)
            if (mp.TerritoryType.RowId != 0)
                (mapsByTerritory.TryGetValue(mp.TerritoryType.RowId, out var ml) ? ml : mapsByTerritory[mp.TerritoryType.RowId] = new List<Map>()).Add(mp);
        foreach (var a in lumina.GetExcelSheet<Aetheryte>())
        {
            if (!a.IsAetheryte || a.Territory.RowId == 0) continue;
            Map? map = a.Map.ValueNullable is { RowId: > 0 } am ? am : null;
            (float X, float Y)? mk = null;
            if (map is { } m0 && markerByAetheryte.TryGetValue((m0.MapMarkerRange, a.RowId), out var found)) mk = found;
            if (mk is null && mapsByTerritory.TryGetValue(a.Territory.RowId, out var candidates))
                foreach (var c in candidates)
                    if (markerByAetheryte.TryGetValue((c.MapMarkerRange, a.RowId), out found)) { map = c; mk = found; break; }
            if (mk is null || map is null) continue;
            var scale = (map.Value.SizeFactor > 0 ? map.Value.SizeFactor : 100) / 100.0;
            var mapPos = new Vector2((float)((2.0 * mk.Value.X / scale + 100.9) / 100.0), (float)((2.0 * mk.Value.Y / scale + 100.9) / 100.0));
            var name = a.PlaceName.ValueNullable?.Name.ExtractText() ?? $"aetheryte {a.RowId}";
            (_aetherytesByTerritory.TryGetValue(a.Territory.RowId, out var al) ? al : _aetherytesByTerritory[a.Territory.RowId] = new()).Add((a.RowId, name, mapPos));
            if (!_territoryNames.ContainsKey(a.Territory.RowId) && territories.TryGetRow(a.Territory.RowId, out var t))
                _territoryNames[a.Territory.RowId] = t.PlaceName.ValueNullable?.Name.ExtractText() ?? $"zone {a.Territory.RowId}";
        }
        foreach (var p in _placesByNpc.Values.SelectMany(x => x))
            if (!_territoryNames.ContainsKey(p.TerritoryTypeId) && territories.TryGetRow(p.TerritoryTypeId, out var t))
                _territoryNames[p.TerritoryTypeId] = t.PlaceName.ValueNullable?.Name.ExtractText() ?? $"zone {p.TerritoryTypeId}";
    }

    public static Vector2 WorldToMap(Vector2 world, Map map) => new(
        0.02f * map.OffsetX + 2048f / map.SizeFactor + 0.02f * world.X + 1f,
        0.02f * map.OffsetY + 2048f / map.SizeFactor + 0.02f * world.Y + 1f);
}
