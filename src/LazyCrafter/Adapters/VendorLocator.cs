using System.Numerics;
using LazyCrafter.Core;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace LazyCrafter.Adapters;

/// <summary>
/// Where to buy a gil-vendor item (Plan §Phase 5 task 4). Lumina + LuminaSupplemental only - no Dalamud - so
/// <c>tests/LazyCrafter.Probe</c> can exercise it offline.
/// <para>
/// Chain: item → <c>GilShopItem</c> parent rows (= <c>GilShop</c> ids) → <c>ENpcBase</c> rows whose <c>ENpcData</c> handlers
/// name that shop (+ LuminaSupplemental <c>ENpcShop</c> for the handful the sheets miss) → NPC placements from
/// LuminaSupplemental <c>ENpcPlace</c> (territory, map, <b>map</b> coordinates; the <c>Level</c> sheet only places
/// quest/event NPCs, see the P6 spike) → every placement in a territory with a teleportable aetheryte
/// (<c>Aetheryte.IsAetheryte</c>, position from the <c>MapMarker</c> sheet - DataType 3 - converted with GBR's marker
/// formula). Index built lazily on first use (~50 ms).
/// </para>
/// <para>
/// <b>This class does not rank anything (0.1.6.2, card t_731ea0e7).</b> It turns the sheets into
/// <see cref="VendorCandidate"/>s and hands every choice to <see cref="VendorChoice"/>, which is the single
/// comparer both <see cref="Find"/> and <see cref="Plan"/> go through. Before 0.1.6.2 those two methods had their
/// own metrics - lowest NPC id vs nearest aetheryte - and returned different vendors for the same item, so whichever
/// printed last won the map flag.
/// </para>
/// </summary>
public sealed class VendorLocator
{
    public sealed record Location(uint ItemId, uint NpcId, string NpcName, uint TerritoryId, string TerritoryName, uint MapId, Vector2 MapCoords,
        uint AetheryteId, string AetheryteName, Vector2 AetheryteMapCoords, float MapDistance);

    private readonly GameData _data;
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly object _lock = new();
    private bool _built;
    private readonly List<string> _supplementalFailures = new();

    private readonly Dictionary<uint, List<uint>> _shopsByItem = new();          // itemId -> GilShop ids
    private readonly Dictionary<uint, List<uint>> _npcsByShop = new();           // GilShop id -> ENpcBase ids
    private readonly Dictionary<uint, List<ENpcPlace>> _placesByNpc = new();     // ENpc id -> placements
    private readonly Dictionary<uint, List<(uint Id, string Name, Vector2 Map)>> _aetherytesByTerritory = new();
    private readonly Dictionary<uint, string> _npcNames = new();
    private readonly Dictionary<uint, string> _territoryNames = new();

    /// <param name="warn">
    /// Where load FAILURES go; defaults to <paramref name="log"/>. Both LuminaSupplemental tables this class
    /// reads (<c>ENpcShop</c>, <c>ENpcPlace</c>) place the NPCs behind the Lifestream vendor hand-off, so a
    /// packaging fault silently degrades it to "no placed gil vendor" for every item (t_1a91db8f).
    /// </param>
    public VendorLocator(GameData data, Action<string> log, Action<string>? warn = null)
    {
        _data = data;
        _log = log;
        _warn = warn ?? log;
    }

    /// <summary>LuminaSupplemental resources that failed to load; empty on a healthy build. Reports only what the
    /// index build has already seen - it deliberately does NOT force the build, so the Settings tab can read it
    /// from the draw thread.</summary>
    public IReadOnlyList<string> SupplementalFailures => _built ? _supplementalFailures : Array.Empty<string>();

    private void Fail(string line)
    {
        _supplementalFailures.Add(line);
        _warn($"LuminaSupplemental {line} - vendor placements are incomplete; the Lifestream vendor hand-off will not find NPCs. This is a PACKAGING fault.");
    }

    public int ShopItemCount { get { EnsureBuilt(); return _shopsByItem.Count; } }
    public int PlacedNpcCount { get { EnsureBuilt(); return _placesByNpc.Count; } }

    // ------------------------------------------------------------------ the two public selectors

    /// <summary>
    /// Best place to buy the item, or <c>null</c> when no placed vendor sells it in a teleportable zone.
    /// <paramref name="context"/> is where the player is standing; <c>null</c> ranks on walk-from-aetheryte alone.
    /// </summary>
    public Location? Find(uint itemId, VendorContext? context = null)
    {
        EnsureBuilt();
        var winner = VendorChoice.Find(itemId, CandidatesFor, context);
        return winner is { } c ? ToLocation(c, itemId) : null;
    }

    /// <summary>
    /// Group a shopping list by vendor so one teleport covers several items: the vendor that sells the most of the
    /// remaining items wins each round (greedy), ties broken by the same ranking <see cref="Find"/> uses.
    /// </summary>
    public IReadOnlyList<(Location Where, IReadOnlyList<(uint ItemId, int Quantity)> Items)> Plan(
        IReadOnlyList<(uint ItemId, int Quantity)> wanted,
        out IReadOnlyList<(uint ItemId, int Quantity)> unlocated,
        VendorContext? context = null)
    {
        EnsureBuilt();
        var stops = VendorChoice.Plan(wanted, CandidatesFor, context, out unlocated);
        return stops
            .Select(s => (ToLocation(s.Where, s.Items.Count > 0 ? s.Items[0].ItemId : 0), s.Items))
            .ToList();
    }

    /// <summary>Every placed, teleportable placement of every NPC selling the item. The only thing the sheets are asked for.</summary>
    private IReadOnlyList<VendorCandidate> CandidatesFor(uint itemId)
    {
        var list = new List<VendorCandidate>();
        if (!_shopsByItem.TryGetValue(itemId, out var shops)) return list;
        var seenNpc = new HashSet<uint>();
        foreach (var shop in shops)
        {
            if (!_npcsByShop.TryGetValue(shop, out var npcs)) continue;
            foreach (var npc in npcs)
            {
                if (!seenNpc.Add(npc)) continue;   // an NPC can front several shops that both sell the item
                if (!_placesByNpc.TryGetValue(npc, out var places)) continue;
                foreach (var p in places)
                {
                    if (!_aetherytesByTerritory.TryGetValue(p.TerritoryTypeId, out var aeths)) continue;
                    foreach (var a in aeths)
                        list.Add(new VendorCandidate(npc, p.TerritoryTypeId, p.MapId, p.Position.X, p.Position.Y,
                            a.Id, a.Map.X, a.Map.Y, Vector2.Distance(p.Position, a.Map)));
                }
            }
        }
        return list;
    }

    private Location ToLocation(VendorCandidate c, uint itemId) => new(
        itemId, c.NpcId, _npcNames.GetValueOrDefault(c.NpcId, $"NPC {c.NpcId}"), c.TerritoryId,
        _territoryNames.GetValueOrDefault(c.TerritoryId, $"zone {c.TerritoryId}"), c.MapId, new Vector2(c.MapX, c.MapY),
        c.AetheryteId, AetheryteName(c.TerritoryId, c.AetheryteId), new Vector2(c.AetheryteMapX, c.AetheryteMapY), c.AetheryteDistance);

    private string AetheryteName(uint territoryId, uint aetheryteId)
    {
        if (_aetherytesByTerritory.TryGetValue(territoryId, out var aeths))
            foreach (var a in aeths)
                if (a.Id == aetheryteId) return a.Name;
        return $"aetheryte {aetheryteId}";
    }

    // ------------------------------------------------------------------ index

    private void EnsureBuilt()
    {
        if (_built) return;
        lock (_lock)
        {
            if (_built) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Build(); }
            catch (Exception ex) { _log($"VendorLocator build failed: {ex.Message}"); }
            _built = true;
            _log($"VendorLocator: {_shopsByItem.Count} gil-shop items, {_npcsByShop.Count} shops with NPCs, {_placesByNpc.Count} placed NPCs, {_aetherytesByTerritory.Count} territories with aetherytes in {sw.ElapsedMilliseconds} ms");
        }
    }

    private void Build()
    {
        // item -> shops
        var gilShopItems = _data.GetSubrowExcelSheet<GilShopItem>() ?? throw new InvalidOperationException("GilShopItem sheet missing");
        var gilShopIds = new HashSet<uint>();
        foreach (var page in gilShopItems)
        {
            foreach (var row in page)
            {
                var id = row.Item.RowId;
                if (id == 0) continue;
                gilShopIds.Add(row.RowId);
                (_shopsByItem.TryGetValue(id, out var l) ? l : _shopsByItem[id] = new List<uint>()).Add(row.RowId);
            }
        }

        // shop -> npcs (ENpcBase handlers), plus the supplemental ENpcShop table
        var npcBase = _data.GetExcelSheet<ENpcBase>() ?? throw new InvalidOperationException("ENpcBase sheet missing");
        foreach (var b in npcBase)
        {
            foreach (var h in b.ENpcData)
            {
                if (h.RowId == 0 || !gilShopIds.Contains(h.RowId)) continue;
                (_npcsByShop.TryGetValue(h.RowId, out var l) ? l : _npcsByShop[h.RowId] = new List<uint>()).Add(b.RowId);
            }
        }
        try
        {
            var shops = CsvLoader.LoadResource<ENpcShop>(CsvLoader.ENpcShopResourceName, true, out _, out _);
            if (shops.Count == 0) Fail($"{CsvLoader.ENpcShopResourceName}: no rows (resource missing from the package?)");
            foreach (var s in shops)
            {
                if (!gilShopIds.Contains(s.ShopId)) continue;
                var l = _npcsByShop.TryGetValue(s.ShopId, out var x) ? x : _npcsByShop[s.ShopId] = new List<uint>();
                if (!l.Contains(s.ENpcResidentId)) l.Add(s.ENpcResidentId);
            }
        }
        catch (Exception ex) { Fail($"{CsvLoader.ENpcShopResourceName}: {ex.Message}"); }

        // npc -> placements (map coordinates)
        var shopNpcs = new HashSet<uint>(_npcsByShop.Values.SelectMany(x => x));
        try
        {
            var places = CsvLoader.LoadResource<ENpcPlace>(CsvLoader.ENpcPlaceResourceName, true, out _, out _);
            if (places.Count == 0) Fail($"{CsvLoader.ENpcPlaceResourceName}: no rows (resource missing from the package?)");
            foreach (var p in places)
            {
                if (!shopNpcs.Contains(p.ENpcResidentId)) continue;
                (_placesByNpc.TryGetValue(p.ENpcResidentId, out var l) ? l : _placesByNpc[p.ENpcResidentId] = new List<ENpcPlace>()).Add(p);
            }
        }
        catch (Exception ex) { Fail($"{CsvLoader.ENpcPlaceResourceName}: {ex.Message}"); }

        // Level-sheet fallback (Type 8 = ENpc) for shop NPCs the supplemental table does not place; world -> map coords.
        var levels = _data.GetExcelSheet<Level>();
        var mapsSheet = _data.GetExcelSheet<Map>();
        var fromLevel = 0;
        if (levels is not null && mapsSheet is not null)
            foreach (var lv in levels)
            {
                if (lv.Type != 8 || lv.Object.RowId == 0 || !shopNpcs.Contains(lv.Object.RowId) || _placesByNpc.ContainsKey(lv.Object.RowId)) continue;
                if (lv.Map.ValueNullable is not { } map || lv.Territory.RowId == 0) continue;
                var pos = WorldToMap(new Vector2(lv.X, lv.Z), map);
                _placesByNpc[lv.Object.RowId] = [new ENpcPlace(lv.Object.RowId, lv.Territory.RowId, map.RowId, map.PlaceName.RowId, pos)];
                fromLevel++;
            }
        if (fromLevel > 0) _log($"VendorLocator: {fromLevel} shop NPCs placed from the Level sheet");

        // names
        var residents = _data.GetExcelSheet<ENpcResident>();
        if (residents is not null)
            foreach (var npc in _placesByNpc.Keys)
                if (residents.TryGetRow(npc, out var r)) _npcNames[npc] = r.Singular.ExtractText();
        var territories = _data.GetExcelSheet<TerritoryType>() ?? throw new InvalidOperationException("TerritoryType sheet missing");

        // aetherytes by territory, in map coordinates: the MapMarker sheet places every aetheryte (DataType 3, DataKey =
        // aetheryte row) as marker pixels on its map; GBR's Maps.MarkerToMap: 2 * px / (SizeFactor/100) + 100.9, /100 -> map units.
        // Only aetherytes with IsAetheryte (teleportable) count; aethernet shards are skipped.
        var aetherytes = _data.GetExcelSheet<Aetheryte>() ?? throw new InvalidOperationException("Aetheryte sheet missing");
        var markers = _data.GetSubrowExcelSheet<MapMarker>() ?? throw new InvalidOperationException("MapMarker sheet missing");
        // (mapMarkerRange, aetheryteId) -> marker px. An aetheryte appears on several maps (its zone, the region map);
        // the zone map is the one whose Map.MapMarkerRange the aetheryte's own Map row names.
        var markerByAetheryte = new Dictionary<(uint Range, uint Aetheryte), (float X, float Y)>();
        foreach (var page in markers)
            foreach (var m in page)
                if (m.DataType == 3 && m.DataKey.RowId != 0)
                    markerByAetheryte.TryAdd((m.RowId, m.DataKey.RowId), (m.X, m.Y));
        var maps = _data.GetExcelSheet<Map>() ?? throw new InvalidOperationException("Map sheet missing");
        var mapsByTerritory = new Dictionary<uint, List<Map>>();
        foreach (var mp in maps)
            if (mp.TerritoryType.RowId != 0)
                (mapsByTerritory.TryGetValue(mp.TerritoryType.RowId, out var ml) ? ml : mapsByTerritory[mp.TerritoryType.RowId] = new()).Add(mp);
        foreach (var a in aetherytes)
        {
            if (!a.IsAetheryte || a.Territory.RowId == 0) continue;
            Map? map = a.Map.ValueNullable is { RowId: > 0 } am ? am : null;
            (float X, float Y)? mk = null;
            if (map is { } m0 && markerByAetheryte.TryGetValue((m0.MapMarkerRange, a.RowId), out var found)) mk = found;
            if (mk is null && mapsByTerritory.TryGetValue(a.Territory.RowId, out var candidates))
                foreach (var c in candidates)
                    if (markerByAetheryte.TryGetValue((c.MapMarkerRange, a.RowId), out found)) { map = c; mk = found; break; }
            if (mk is null || map is null) continue;
            // GBR: Maps.MarkerToMap(px, Territory.SizeFactor) with Territory.SizeFactor = Map.SizeFactor / 100, result in centi-units.
            var scale = (map.Value.SizeFactor > 0 ? map.Value.SizeFactor : 100) / 100.0;
            var mapPos = new Vector2((float)((2.0 * mk.Value.X / scale + 100.9) / 100.0), (float)((2.0 * mk.Value.Y / scale + 100.9) / 100.0));
            var name = a.PlaceName.ValueNullable?.Name.ExtractText() ?? $"aetheryte {a.RowId}";
            (_aetherytesByTerritory.TryGetValue(a.Territory.RowId, out var l) ? l : _aetherytesByTerritory[a.Territory.RowId] = new()).Add((a.RowId, name, mapPos));
            if (!_territoryNames.ContainsKey(a.Territory.RowId) && territories.TryGetRow(a.Territory.RowId, out var t))
                _territoryNames[a.Territory.RowId] = t.PlaceName.ValueNullable?.Name.ExtractText() ?? $"zone {a.Territory.RowId}";
        }
        foreach (var p in _placesByNpc.Values.SelectMany(x => x))
            if (!_territoryNames.ContainsKey(p.TerritoryTypeId) && territories.TryGetRow(p.TerritoryTypeId, out var t))
                _territoryNames[p.TerritoryTypeId] = t.PlaceName.ValueNullable?.Name.ExtractText() ?? $"zone {p.TerritoryTypeId}";
    }

    /// <summary>Dalamud's <c>MapUtil.WorldToMap</c> re-stated so this class stays Dalamud-free: <c>0.02·offset + 2048/scale + 0.02·value + 1</c>.</summary>
    public static Vector2 WorldToMap(Vector2 world, Map map) => new(
        0.02f * map.OffsetX + 2048f / map.SizeFactor + 0.02f * world.X + 1f,
        0.02f * map.OffsetY + 2048f / map.SizeFactor + 0.02f * world.Y + 1f);
}
