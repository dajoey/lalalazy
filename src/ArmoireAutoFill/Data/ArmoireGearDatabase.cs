using System.Diagnostics;
using ArmoireAutoFill.Models;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;
using LuminaContentFinder = Lumina.Excel.Sheets.ContentFinderCondition;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace ArmoireAutoFill.Data;

// Builds the armoire-eligible gear database at plugin startup.
//
// Sources:
//   * Lumina Cabinet sheet  — canonical list of every item the armoire can hold
//   * Lumina Item sheet     — names + equip-slot category for each item
//   * LuminaSupplemental    — community-curated drop tables that map items to the
//                             ContentFinderConditions (dungeons) that drop them
//
// Items with no LuminaSupplemental mapping are collected into a synthetic
// "Source unknown" DungeonInfo so the user can still see they're missing.
public static class ArmoireGearDatabase
{
    private const uint UnknownSourceCfcId = 0;
    private const string UnknownSourceName = "Source unknown";

    public static List<ArmoireItem> AllItems { get; private set; } = [];
    public static List<DungeonInfo> DungeonSets { get; private set; } = [];

    public static bool IsLoaded { get; private set; }

    // The summary totals count *unique items*, not ArmoireItem instances — an
    // item that drops in N dungeons appears N times in AllItems but should
    // only contribute 1 to the totals.
    public static int TotalItems => AllItems.DistinctBy(i => i.ItemId).Count();
    public static int OwnedCount => AllItems
        .Where(i => i.Owned != OwnershipStatus.NotOwned)
        .Select(i => i.ItemId)
        .Distinct()
        .Count();
    public static int MissingCount => TotalItems - OwnedCount;

    public static void Build()
    {
        var stopwatch = Stopwatch.StartNew();

        var cabinetSheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        var itemSheet = Svc.Data.GetExcelSheet<LuminaItem>();
        var cfcSheet = Svc.Data.GetExcelSheet<LuminaContentFinder>();
        if (cabinetSheet == null || itemSheet == null || cfcSheet == null)
        {
            Svc.Log.Error("[ArmoireAutoFill] Required excel sheets unavailable; database not built.");
            return;
        }

        var itemIdToCfcIds = BuildItemToCfcIndex();

        var dungeons = new Dictionary<uint, DungeonInfo>();
        var allItems = new List<ArmoireItem>();

        foreach (var cabinetRow in cabinetSheet)
        {
            var itemId = cabinetRow.Item.RowId;
            if (itemId == 0)
                continue;

            var itemRow = itemSheet.GetRowOrDefault(itemId);
            if (itemRow == null)
                continue;

            var name = itemRow.Value.Name.ExtractText();
            var slot = MapEquipSlot(itemRow.Value.EquipSlotCategory.RowId);

            if (itemIdToCfcIds.TryGetValue(itemId, out var cfcIds) && cfcIds.Count > 0)
            {
                foreach (var cfcId in cfcIds)
                {
                    var dungeon = GetOrCreateDungeon(dungeons, cfcId, cfcSheet);
                    var armoireItem = new ArmoireItem
                    {
                        ItemId = itemId,
                        Name = name,
                        Slot = slot,
                        DungeonName = dungeon.Name,
                        ContentFinderConditionId = dungeon.ContentFinderConditionId == UnknownSourceCfcId
                            ? null
                            : dungeon.ContentFinderConditionId,
                    };
                    dungeon.Items.Add(armoireItem);
                    allItems.Add(armoireItem);
                }
            }
            else
            {
                var dungeon = GetOrCreateDungeon(dungeons, UnknownSourceCfcId, cfcSheet);
                var armoireItem = new ArmoireItem
                {
                    ItemId = itemId,
                    Name = name,
                    Slot = slot,
                    DungeonName = dungeon.Name,
                    ContentFinderConditionId = null,
                };
                dungeon.Items.Add(armoireItem);
                allItems.Add(armoireItem);
            }
        }

        AllItems = allItems;
        DungeonSets = [.. dungeons.Values];
        IsLoaded = true;

        stopwatch.Stop();
        var unknownCount = dungeons.TryGetValue(UnknownSourceCfcId, out var unknown) ? unknown.Items.Count : 0;
        Svc.Log.Information(
            $"[ArmoireAutoFill] database built in {stopwatch.ElapsedMilliseconds}ms: "
            + $"{allItems.Count} entries across {DungeonSets.Count} sources "
            + $"({unknownCount} with no known source).");
    }

    private static DungeonInfo GetOrCreateDungeon(Dictionary<uint, DungeonInfo> bucket, uint cfcId, Lumina.Excel.ExcelSheet<LuminaContentFinder> cfcSheet)
    {
        if (bucket.TryGetValue(cfcId, out var existing))
            return existing;

        string name;
        if (cfcId == UnknownSourceCfcId)
        {
            name = UnknownSourceName;
        }
        else
        {
            var cfcRow = cfcSheet.GetRowOrDefault(cfcId);
            name = cfcRow.HasValue ? cfcRow.Value.Name.ExtractText() : $"Duty #{cfcId}";
            if (string.IsNullOrWhiteSpace(name))
                name = $"Duty #{cfcId}";
        }

        var info = new DungeonInfo
        {
            ContentFinderConditionId = cfcId,
            Name = name,
        };
        bucket[cfcId] = info;
        return info;
    }

    private static Dictionary<uint, HashSet<uint>> BuildItemToCfcIndex()
    {
        var index = new Dictionary<uint, HashSet<uint>>();

        void Add(uint itemId, uint cfcId)
        {
            if (itemId == 0 || cfcId == 0)
                return;
            if (!index.TryGetValue(itemId, out var bucket))
            {
                bucket = [];
                index[itemId] = bucket;
            }
            bucket.Add(cfcId);
        }

        // DungeonChest + DungeonChestItem need joining: each chest-item references
        // a chest by DungeonChest.RowId (NOT DungeonChest.ChestId — that's the
        // in-map chest position, not the primary key).
        var dungeonChests = LoadCsv<DungeonChest>(CsvLoader.DungeonChestResourceName);
        var chestRowIdToCfc = dungeonChests
            .ToDictionary(c => c.RowId, c => c.ContentFinderConditionId);
        foreach (var chestItem in LoadCsv<DungeonChestItem>(CsvLoader.DungeonChestItemResourceName))
        {
            if (chestRowIdToCfc.TryGetValue(chestItem.ChestId, out var cfcId))
                Add(chestItem.ItemId, cfcId);
        }

        foreach (var bossChest in LoadCsv<DungeonBossChest>(CsvLoader.DungeonBossChestResourceName))
            Add(bossChest.ItemId, bossChest.ContentFinderConditionId);

        foreach (var bossDrop in LoadCsv<DungeonBossDrop>(CsvLoader.DungeonBossDropResourceName))
            Add(bossDrop.ItemId, bossDrop.ContentFinderConditionId);

        foreach (var drop in LoadCsv<DungeonDrop>(CsvLoader.DungeonDropItemResourceName))
            Add(drop.ItemId, drop.ContentFinderConditionId);

        return index;
    }

    private static List<T> LoadCsv<T>(string resourceName) where T : ICsv, new()
    {
        try
        {
            var rows = CsvLoader.LoadResource<T>(resourceName, true, out var failedLines, out var exceptions);
            if (failedLines.Count > 0)
            {
                Svc.Log.Warning(
                    $"[ArmoireAutoFill] {failedLines.Count} CSV rows failed to parse from {resourceName} "
                    + $"(first exception: {exceptions.FirstOrDefault()?.Message ?? "n/a"})");
            }
            return rows;
        }
        catch (Exception e)
        {
            Svc.Log.Error(e, $"[ArmoireAutoFill] Failed to load {resourceName}");
            return [];
        }
    }

    // EquipSlotCategory RowId mapping — values come from ECommons.ExcelServices.EquipSlotCategoryEnum.
    // The previous mapping was wrong: it used 1..6 = Head..Feet, but the real game data is
    // 1=MainHand, 2=OffHand, 3=Head, 4=Body, 5=Gloves, 6=Waist, 7=Legs, 8=Feet, plus a slew of
    // multi-slot "set" variants. For armoire display we collapse set items to whichever single
    // drawer they conceptually live in (body takes precedence on combos).
    private static GearSlot MapEquipSlot(uint equipSlotCategoryRowId) => (EquipSlotCategoryEnum)equipSlotCategoryRowId switch
    {
        EquipSlotCategoryEnum.Head => GearSlot.Head,
        EquipSlotCategoryEnum.Body => GearSlot.Body,
        EquipSlotCategoryEnum.Gloves => GearSlot.Hands,
        EquipSlotCategoryEnum.Waist => GearSlot.Waist,
        EquipSlotCategoryEnum.Legs => GearSlot.Legs,
        EquipSlotCategoryEnum.Feet => GearSlot.Feet,
        EquipSlotCategoryEnum.BodyHead
            or EquipSlotCategoryEnum.BodyGloves
            or EquipSlotCategoryEnum.BodyGlovesLegs
            or EquipSlotCategoryEnum.BodyGlovesLegsFeet
            or EquipSlotCategoryEnum.BodyHeadGlovesLegsFeet
            or EquipSlotCategoryEnum.BodyLegsFeet => GearSlot.Body,
        EquipSlotCategoryEnum.LegsFeet => GearSlot.Legs,
        _ => GearSlot.Unknown,
    };
}
