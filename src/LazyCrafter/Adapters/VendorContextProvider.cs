using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LazyCrafter.Core;
// NOT `using Lumina.Excel.Sheets` - FFXIVClientStructs.FFXIV.Client.Game.UI also declares a `Map`, and the two
// collide (CS0104). The one sheet type this file needs is aliased instead.
using LuminaMap = Lumina.Excel.Sheets.Map;

namespace LazyCrafter.Adapters;

/// <summary>
/// Builds the <see cref="VendorContext"/> the vendor ranking needs: which zone the player is standing in, where in
/// it, and what a teleport to each attuned aetheryte costs (card t_731ea0e7, 0.1.6.2).
/// <para>
/// Everything here is best-effort. Not logged in, a sheet miss, an FCS read that throws - any of them degrade to
/// <see cref="VendorContext.Unknown"/>, which ranks exactly the way 0.1.6.1 <c>Find()</c> did (walk distance from
/// the nearest aetheryte). The ranking is never allowed to fail the hand-off.
/// </para>
/// <para>
/// Teleport fares come from the client's own <c>Telepo</c> list, the same source the Teleport window reads:
/// <c>Telepo.Instance()-&gt;UpdateAetheryteList()</c> then <c>TeleportList</c> (<c>TeleportInfo.AetheryteId</c> /
/// <c>.GilCost</c>, verified against the installed FFXIVClientStructs). An aetheryte the player is not attuned to is
/// simply absent from that list, which is exactly the ranking we want: unreachable sorts below every reachable one.
/// Cached for <see cref="CacheSeconds"/> because <c>UpdateAetheryteList</c> is a real game call and the vendor
/// buttons can be clicked repeatedly.
/// </para>
/// </summary>
public sealed class VendorContextProvider
{
    /// <summary>How long a fetched fare table is reused. Fares only change when you attune somewhere new.</summary>
    public const int CacheSeconds = 30;

    private readonly IClientState _clientState;
    private readonly IObjectTable _objects;
    private readonly IDataManager _data;
    private readonly IPluginLog _log;

    private Dictionary<uint, uint>? _costs;
    private DateTime _costsAt = DateTime.MinValue;
    private bool _costsWarned;

    public VendorContextProvider(IClientState clientState, IObjectTable objects, IDataManager data, IPluginLog log)
    {
        _clientState = clientState;
        _objects = objects;
        _data = data;
        _log = log;
    }

    /// <summary>Where the player is and what travel costs, right now. Never throws; falls back to <see cref="VendorContext.Unknown"/>.</summary>
    public VendorContext Current()
    {
        try
        {
            if (!_clientState.IsLoggedIn) return VendorContext.Unknown;
            var territory = _clientState.TerritoryType;
            if (territory == 0) return VendorContext.Unknown;

            var hasPos = false;
            float mx = 0, my = 0;
            if (_objects.LocalPlayer is { } me
                && _data.GetExcelSheet<LuminaMap>() is { } maps
                && maps.TryGetRow(_clientState.MapId, out var map)
                && map.SizeFactor > 0)
            {
                // Player position is world space; vendor placements are map space. Same conversion the locator uses.
                var p = VendorLocator.WorldToMap(new System.Numerics.Vector2(me.Position.X, me.Position.Z), map);
                mx = p.X;
                my = p.Y;
                hasPos = true;
            }

            return new VendorContext(territory, mx, my, hasPos, TeleportCosts());
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "VendorContextProvider fell back to Unknown");
            return VendorContext.Unknown;
        }
    }

    /// <summary>aetheryteId -&gt; gil for every attuned destination, or <c>null</c> when the list cannot be read.</summary>
    private unsafe IReadOnlyDictionary<uint, uint>? TeleportCosts()
    {
        if (_costs is not null && DateTime.UtcNow - _costsAt < TimeSpan.FromSeconds(CacheSeconds)) return _costs;
        try
        {
            var telepo = Telepo.Instance();
            if (telepo is null) return _costs;
            telepo->UpdateAetheryteList();
            var list = telepo->TeleportList;
            if (list.Count == 0) return _costs;   // keep the last good table rather than pretending nothing is attuned
            var map = new Dictionary<uint, uint>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                ref var info = ref list[i];
                if (info.AetheryteId == 0) continue;
                // Several sub-indices (housing wards) share an aetheryte id; keep the cheapest.
                if (!map.TryGetValue(info.AetheryteId, out var gil) || info.GilCost < gil) map[info.AetheryteId] = info.GilCost;
            }
            _costs = map;
            _costsAt = DateTime.UtcNow;
            return _costs;
        }
        catch (Exception ex)
        {
            if (!_costsWarned)
            {
                _costsWarned = true;
                _log.Warning(ex, "Telepo teleport list unreadable - vendor ranking falls back to walk distance only");
            }
            return _costs;
        }
    }
}
