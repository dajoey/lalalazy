using Lumina.Excel.Sheets;
using ECommons.DalamudServices;

namespace LazyFateAutomation;

public sealed record FateZone(uint TerritoryId, string Name, uint ExVersionId, string ExVersionName, FateZoneLogic.ZoneClass ZoneClass);

/// <summary>
///     Runtime FATE zone table, built from the game sheets on first use (FateZoneLogic holds the
///     pure rules and evidence). Because the pools derive from TerritoryType classes instead of a
///     hard-coded list, future zones are picked up with no plugin change.
/// </summary>
public static class FateZones {
    private static IReadOnlyList<FateZone>? _all;

    /// <summary>
    ///     Every zone the plugin can run FATEs in, in sheet (expansion) order. Includes
    ///     NoTeleportFateZone entries (e.g. The Dravanian Hinterlands): shown in the UI but never
    ///     picked by automatic zone swaps, since TeleportTo cannot route into them.
    /// </summary>
    public static IReadOnlyList<FateZone> All => _all ??= Load();

    public static IEnumerable<IGrouping<(uint Id, string Name), FateZone>> ByExpansion
        => from zone in All
            group zone by (Id: zone.ExVersionId, Name: zone.ExVersionName) into g
            orderby g.Key.Id
            select g;

    /// <summary>True when a territory has an in-zone primary aetheryte (zone swaps can teleport into it).</summary>
    public static bool HasPrimaryAetheryte(uint territoryId)
        => All.Any(z => z.TerritoryId == territoryId && z.ZoneClass == FateZoneLogic.ZoneClass.FateZone);

    /// <summary>Zones whose FATEs reward the currency; empty for None.</summary>
    public static IReadOnlySet<uint> CurrencyZones(FateCurrency currency) => currency switch {
        FateCurrency.CompanySeals => ZonesOf(FateCurrency.CompanySeals),
        FateCurrency.BicolorGemstones => ZonesOf(FateCurrency.BicolorGemstones),
        FateCurrency.YokaiMedals => YokaiZoneIds,
        _ => new HashSet<uint>(),
    };

    private static IReadOnlySet<uint> ZonesOf(FateCurrency currency)
        => All
            .Where(z => FateZoneLogic.ZoneCurrency(z.ExVersionId, z.ZoneClass) == currency)
            .Select(z => z.TerritoryId)
            .ToHashSet();

    private static IReadOnlySet<uint>? _yokaiZoneIds;

    private static IReadOnlySet<uint> YokaiZoneIds => _yokaiZoneIds ??= YokaiGrindMode.Yokai.Values
        .SelectMany(e => e.Zones)
        .Select(z => z.RowId)
        .Where(HasPrimaryAetheryte)
        .ToHashSet();

    private static IReadOnlyList<FateZone> Load() {
        var list = new List<FateZone>();
        var sheet = Svc.Data.GetExcelSheet<TerritoryType>();
        if (sheet == null) return list;

        var exVersions = Svc.Data.GetExcelSheet<ExVersion>();
        var primaryAetherytes = Svc.Data.GetExcelSheet<Aetheryte>()?
            .Where(a => a.IsAetheryte && !a.Invisible)
            .Select(a => a.Territory.RowId)
            .Where(id => id != 0)
            .ToHashSet() ?? [];

        foreach (var t in sheet) {
            var zoneClass = FateZoneLogic.ClassifyZone(
                t.IsInUse, t.Mount, t.IsPvpZone,
                t.TerritoryIntendedUse.RowId,
                primaryAetherytes.Contains(t.RowId));
            if (zoneClass is not (FateZoneLogic.ZoneClass.FateZone or FateZoneLogic.ZoneClass.NoTeleportFateZone))
                continue;

            var name = t.PlaceName.Value.Name.ToString();
            if (string.IsNullOrEmpty(name)) name = t.Name.ToString();
            var exVersion = exVersions?.GetRow(t.ExVersion.RowId);
            list.Add(new FateZone(
                t.RowId,
                string.IsNullOrEmpty(name) ? $"Zone {t.RowId}" : name,
                t.ExVersion.RowId,
                string.IsNullOrEmpty(exVersion?.Name.ToString()) ? $"Expansion {t.ExVersion.RowId}" : exVersion.Value.Name.ToString(),
                zoneClass));
        }
        return list;
    }
}
