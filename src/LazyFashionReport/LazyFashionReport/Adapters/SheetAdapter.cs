using System.Text.Json;
using Dalamud.Plugin.Services;
using LazyFashionReport.Core;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Sheet-side lookups from the LIVE client data (Lumina through Dalamud's IDataManager):
/// - Stain sheet -> dye name (lowercase) -> stain id, and stain -> dye item -> icon -> family
///   via ShadeMap.BuildStainFamilies.
/// - FashionCheckThemeCategory sheet -> category name (lowercase) -> row id (the xivstats key).
/// - Item sheet -> id -> name (candidates render real names, never "item 12345").
/// </summary>
internal sealed class SheetAdapter
{
    private readonly Dictionary<string, uint> _dyeNameToStain = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _stainToName = new();
    private readonly Dictionary<string, int> _categoryNameToRow = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _itemNameById = new();
    private IReadOnlyDictionary<uint, string>? _stainFamilies;

    public bool Loaded { get; private set; }

    public void Load(IDataManager data)
    {
        _dyeNameToStain.Clear();
        _stainToName.Clear();
        _categoryNameToRow.Clear();
        _itemNameById.Clear();

        // Stains: name <-> id, and id -> dye item icon (via the Item links on each stain row).
        var stains = data.GetExcelSheet<Stain>();
        var items = data.GetExcelSheet<Item>();
        Dictionary<uint, uint> stainToIcon = new();
        foreach (var s in stains)
        {
            var name = s.Name.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            _dyeNameToStain[name] = s.RowId;
            _stainToName[s.RowId] = name;
            foreach (var link in s.Item)
            {
                var it = items.GetRow(link.RowId);
                if (it.RowId != 0)
                    stainToIcon[s.RowId] = it.Icon;
            }
        }
        _stainFamilies = ShadeMap.BuildStainFamilies(stainToIcon);

        // Fashion report hint categories.
        var cats = data.GetExcelSheet<FashionCheckThemeCategory>();
        foreach (var c in cats)
        {
            var name = c.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                _categoryNameToRow[name] = (int)c.RowId;
        }

        Loaded = true;
    }

    /// <summary>Warm the item-name cache for the ids we are about to render.</summary>
    public void WarmItemNames(IEnumerable<uint> itemIds, IDataManager data)
    {
        var items = data.GetExcelSheet<Item>();
        foreach (var id in itemIds)
        {
            if (_itemNameById.ContainsKey(id) || id == 0) continue;
            // A junk id from the crowd data (e.g. 1010533) must not abort the whole weekly
            // rebuild: GetRow throws on an out-of-range rowId. Fall back to a raw-id label.
            try
            {
                var it = items.GetRow(id);
                _itemNameById[id] = it.RowId != 0 ? it.Name.ToString() : $"item {id}";
            }
            catch
            {
                _itemNameById[id] = $"item {id}";
            }
        }
    }

    public IReadOnlyDictionary<uint, string> StainFamilies => _stainFamilies ?? new Dictionary<uint, string>();
    public IReadOnlyDictionary<string, uint> DyeNameToStain => _dyeNameToStain;
    public IReadOnlyDictionary<uint, string> StainToName => _stainToName;
    public IReadOnlyDictionary<string, int> CategoryNameToRow => _categoryNameToRow;
    public IReadOnlyDictionary<uint, string> ItemNameById => _itemNameById;

    public string? DyeNameFor(uint stainId) => _stainToName.GetValueOrDefault(stainId);

    /// <summary>Resolve an item id to a display name, falling back to the raw id.</summary>
    public string ItemName(uint id) => _itemNameById.GetValueOrDefault(id, $"item {id}");
}
