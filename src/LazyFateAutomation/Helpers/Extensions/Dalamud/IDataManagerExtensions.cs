using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using Action = Lumina.Excel.Sheets.Action;

namespace clib.Extensions;

// https://github.com/Haselnussbomber/HaselCommon/blob/main/HaselCommon/Services/ExcelService.cs
public static class IDataManagerExtensions {
    public record class NPCInfo(ulong Id, Vector3 Location, uint ShopId);

    public static NPCInfo? GetNPCInfo(this IDataManager data, uint enpcId, uint territoryId, uint itemId = 0) {
        var scene = GetRow<TerritoryType>(data, territoryId)!.Value.Bg.ToString();
        var filenameStart = scene.LastIndexOf('/') + 1;
        var planeventLayerGroup = "bg/" + scene[0..filenameStart] + "planevent.lgb";
        Svc.Log.Debug($"Territory {territoryId} -> {planeventLayerGroup}");
        var lvb = Svc.Data.GetFile<LgbFile>(planeventLayerGroup);
        if (lvb != null) {
            foreach (var layer in lvb.Layers) {
                foreach (var instance in layer.InstanceObjects) {
                    if (instance.AssetType != LayerEntryType.EventNPC)
                        continue;
                    var baseId = ((LayerCommon.ENPCInstanceObject)instance.Object).ParentData.ParentData.BaseId;
                    if (baseId == enpcId) {
                        var npcId = (1ul << 32) | instance.InstanceId;
                        Vector3 npcLocation = new(instance.Transform.Translation.X, instance.Transform.Translation.Y, instance.Transform.Translation.Z);
                        Svc.Log.Debug($"Found npc {baseId} {instance.InstanceId} '{GetRow<ENpcResident>(data, baseId)?.Singular}' at {npcLocation}");
                        if (itemId != 0) {
                            var vendor = FindVendorItem(data, baseId, itemId);
                            if (vendor.itemIndex >= 0) {
                                Svc.Log.Debug($"Found shop #{vendor.shopId} and item index #{vendor.itemIndex}");
                                return new NPCInfo(npcId, npcLocation, vendor.shopId);
                            }
                        }
                        return new NPCInfo(npcId, npcLocation, 0);
                    }
                }
            }
        }
        return null;
    }

    public static List<Recipe> GetCraftableRecipes(this IDataManager data)
        => [.. GetSheet<Recipe>(data).Where(r => r.ItemResult.RowId != 0).Where(r => r.IngredientsWithAmounts().All(x => x.item.GetCount() >= x.amount))];

    public static List<Recipe> GetUncompletedRecipes(this IDataManager data)
        => [.. GetSheet<Recipe>(data).Where(r => r.ItemResult.RowId != 0 && r.SecretRecipeBook.RowId == 0 && r.RecipeNotebookList.RowId == 0 && !QuestManager.IsRecipeComplete(r.RowId))];

    public static List<Item> GetMoochableFish(this IDataManager data)
        // 33 for tackle, 47 for fish
        => FindRows<FishingBaitParameter>(data, x => x is { Item.RowId: not 0, Item.Value.ItemUICategory.RowId: 47 }).Select(f => f.Item.Value).ToList() ?? [];

    public static IReadOnlyList<Action> GetActionsForJob(this IDataManager data, ClassJob cj)
        => FindRows<Action>(data, a => a.ClassJobCategory.ValueNullable?.ContainsJob(cj) ?? false);

    public static List<T> GetSupplemental<T>(this IDataManager data, string resourceName) where T : ICsv, new() => CsvLoader.LoadResource<T>(
        resourceName: resourceName, includesHeaders: false, out _, out _, data.GameData, data.GameData.Options.DefaultExcelLanguage);

    // Regular sheets

    public static RowRef<T> GetRef<T>(this IDataManager data, uint rowId, ClientLanguage? language = null) where T : struct, IExcelRow<T>
        => new(data.Excel, rowId, (language ?? Svc.ClientState.ClientLanguage).ToLumina());

    public static ExcelSheet<T> GetSheet<T>(this IDataManager data, ClientLanguage? language = null) where T : struct, IExcelRow<T>
        => data.GetExcelSheet<T>(language ?? Svc.ClientState.ClientLanguage)!;

    public static ExcelSheet<T> GetSheet<T>(this IDataManager data, string sheetName, ClientLanguage? language = null) where T : struct, IExcelRow<T>
        => data.GetExcelSheet<T>(language ?? Svc.ClientState.ClientLanguage, sheetName)!;

    public static T? GetRow<T>(this IDataManager data, uint rowId, ClientLanguage? language = null) where T : struct, IExcelRow<T>
        => GetSheet<T>(data, language).GetRowOrDefault(rowId);

    public static bool TryGetRow<T>(this IDataManager data, uint rowId, out T row) where T : struct, IExcelRow<T>
        => TryGetRow(data, rowId, null, out row);

    public static bool TryGetRow<T>(this IDataManager data, uint rowId, ClientLanguage? language, out T row) where T : struct, IExcelRow<T>
        => GetSheet<T>(data, language ?? Svc.ClientState.ClientLanguage).TryGetRow(rowId, out row);

    public static bool TryGetRow<T>(this IDataManager data, string sheetName, uint rowId, out T row) where T : struct, IExcelRow<T>
        => TryGetRow(data, sheetName, rowId, null, out row);

    public static bool TryGetRow<T>(this IDataManager data, string sheetName, uint rowId, ClientLanguage? language, out T row) where T : struct, IExcelRow<T>
        => GetSheet<T>(data, sheetName, language ?? Svc.ClientState.ClientLanguage).TryGetRow(rowId, out row);

    public static bool TryFindRow<T>(this IDataManager data, string sheetName, Predicate<T> predicate, out T row) where T : struct, IExcelRow<T>
        => TryFindRow(data, sheetName, predicate, null, out row);

    public static bool TryFindRow<T>(this IDataManager data, string sheetName, Predicate<T> predicate, ClientLanguage? language, out T row) where T : struct, IExcelRow<T>
        => GetSheet<T>(data, sheetName, language ?? Svc.ClientState.ClientLanguage).TryGetFirst(predicate, out row);

    public static bool TryFindRow<T>(this IDataManager data, Predicate<T> predicate, out T row) where T : struct, IExcelRow<T>
        => TryFindRow(data, predicate, null, out row);

    public static bool TryFindRow<T>(this IDataManager data, Predicate<T> predicate, ClientLanguage? language, out T row) where T : struct, IExcelRow<T>
        => GetSheet<T>(data, language ?? Svc.ClientState.ClientLanguage).TryGetFirst(predicate, out row);

    public static T? FindRow<T>(this IDataManager data, Func<T, bool> predicate) where T : struct, IExcelRow<T>
         => Lumina.Extensions.LinqExtensions.FirstOrNull(GetSheet<T>(data), row => predicate(row));

    public static IReadOnlyList<T> FindRows<T>(this IDataManager data, Predicate<T> predicate, ClientLanguage? language = null) where T : struct, IExcelRow<T>
        => [.. GetSheet<T>(data, language ?? Svc.ClientState.ClientLanguage).Where(row => predicate(row))];

    public static bool TryFindRows<T>(this IDataManager data, Predicate<T> predicate, out IReadOnlyList<T> rows) where T : struct, IExcelRow<T>
        => TryFindRows(data, predicate, null, out rows);

    public static bool TryFindRows<T>(this IDataManager data, Predicate<T> predicate, ClientLanguage? language, out IReadOnlyList<T> rows) where T : struct, IExcelRow<T> {
        rows = [.. GetSheet<T>(data, language).Where(row => predicate(row))];
        return rows.Count != 0;
    }

    // Subrow Sheets

    public static SubrowRef<T> GetSubRef<T>(this IDataManager data, uint rowId, ClientLanguage? language = null) where T : struct, IExcelSubrow<T>
        => new(data.Excel, rowId, (language ?? Svc.ClientState.ClientLanguage).ToLumina());

    public static SubrowExcelSheet<T> GetSubrowSheet<T>(this IDataManager data, ClientLanguage? language = null) where T : struct, IExcelSubrow<T>
        => data.GetSubrowExcelSheet<T>(language ?? Svc.ClientState.ClientLanguage)!;

    public static SubrowExcelSheet<T> GetSubrowSheet<T>(this IDataManager data, string sheetName, ClientLanguage? language = null) where T : struct, IExcelSubrow<T>
        => data.GetSubrowExcelSheet<T>(language ?? Svc.ClientState.ClientLanguage, sheetName)!;

    public static T? GetRow<T>(this IDataManager data, uint rowId, ushort subRowId, ClientLanguage? language = null) where T : struct, IExcelSubrow<T>
        => GetSubrowSheet<T>(data, language).GetSubrowOrDefault(rowId, subRowId);

    public static bool TryGetSubrows<T>(this IDataManager data, uint rowId, out SubrowCollection<T> rows) where T : struct, IExcelSubrow<T>
        => TryGetSubrows(data, rowId, null, out rows);

    public static bool TryGetSubrows<T>(this IDataManager data, uint rowId, ClientLanguage? language, out SubrowCollection<T> rows) where T : struct, IExcelSubrow<T>
        => GetSubrowSheet<T>(data, language ?? Svc.ClientState.ClientLanguage).TryGetRow(rowId, out rows);

    public static bool TryGetSubrow<T>(this IDataManager data, uint rowId, int subRowIndex, out T row) where T : struct, IExcelSubrow<T>
        => TryGetSubrow(data, rowId, subRowIndex, null, out row);

    public static bool TryGetSubrow<T>(this IDataManager data, uint rowId, int subRowIndex, ClientLanguage? language, out T row) where T : struct, IExcelSubrow<T> {
        if (!GetSubrowSheet<T>(data, language ?? Svc.ClientState.ClientLanguage).TryGetRow(rowId, out var rows) || subRowIndex < rows.Count) {
            row = default;
            return false;
        }

        row = rows[subRowIndex];
        return true;
    }

    public static bool TryFindSubrow<T>(this IDataManager data, Predicate<T> predicate, out T subrow) where T : struct, IExcelSubrow<T>
        => TryFindSubrow(data, predicate, null, out subrow);

    public static bool TryFindSubrow<T>(this IDataManager data, Predicate<T> predicate, ClientLanguage? language, out T subrow) where T : struct, IExcelSubrow<T> {
        foreach (var irow in GetSubrowSheet<T>(data, language ?? Svc.ClientState.ClientLanguage)) {
            foreach (var isubrow in irow) {
                if (predicate(isubrow)) {
                    subrow = isubrow;
                    return true;
                }
            }
        }

        subrow = default;
        return false;
    }

    // RawRow

    public static bool TryGetRawRow(this IDataManager data, string sheetName, uint rowId, out RawRow rawRow)
        => TryGetRow(data, sheetName, rowId, out rawRow);

    public static bool TryGetRawRow(this IDataManager data, string sheetName, uint rowId, ClientLanguage? language, out RawRow rawRow)
        => TryGetRow(data, sheetName, rowId, language, out rawRow);

    private static (uint shopId, int itemIndex) FindVendorItem(IDataManager data, uint enpcId, uint itemId) {
        var enpcBase = data.GetRow<ENpcBase>(enpcId);
        if (enpcBase == null)
            return (0, -1);

        foreach (var handler in enpcBase.Value.ENpcData) {
            if ((handler.RowId >> 16) != (uint)EventHandlerContent.Shop)
                continue;

            if (data.TryGetSubrows<GilShopItem>(handler.RowId, out var items)) {
                for (var i = 0; i < items.Count; ++i) {
                    var shopItem = items[i];
                    if (shopItem.Item.RowId == itemId)
                        return (handler.RowId, i);
                }
            }
        }
        return (0, -1);
    }
}
