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
// The user's stated goal: "show me which pieces I'm missing from each dungeon."
// We therefore include only items that have at least one known dungeon source
// (via LuminaSupplemental). Items with no known source are dropped — they're
// usually class-quest, event, or vendor rewards, none of which fit the goal.
//
// Sources:
//   * Lumina Cabinet sheet  — canonical list of every item the armoire can hold
//   * Lumina Item sheet     — names + equip-slot category for each item
//   * LuminaSupplemental    — community-curated drop tables that map items to
//                             the ContentFinderConditions (dungeons) that drop them
public static class ArmoireGearDatabase
{
    public static List<ArmoireItem> AllItems { get; private set; } = [];
    public static List<DungeonInfo> DungeonSets { get; private set; } = [];

    public static bool IsLoaded { get; private set; }

    // Headline totals dedupe — an item that drops in N dungeons appears N times
    // in AllItems but should only contribute 1 to the totals.
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
        var slotDistribution = new Dictionary<GearSlot, int>();
        var skippedNoSource = 0;
        var skippedEmptyItem = 0;
        uint sampleEquipSlotCat = 0;
        var sampleEquipSlotCatLogged = false;

        foreach (var cabinetRow in cabinetSheet)
        {
            var itemId = cabinetRow.Item.RowId;
            if (itemId == 0)
            {
                skippedEmptyItem++;
                continue;
            }

            if (!itemIdToCfcIds.TryGetValue(itemId, out var cfcIds) || cfcIds.Count == 0)
            {
                skippedNoSource++;
                continue;
            }

            var itemRow = itemSheet.GetRowOrDefault(itemId);
            if (itemRow == null)
                continue;

            var name = itemRow.Value.Name.ExtractText();
            var equipSlotCatRowId = itemRow.Value.EquipSlotCategory.RowId;
            var slot = DeriveSlot(equipSlotCatRowId, cabinetRow.Category.RowId);
            slotDistribution[slot] = slotDistribution.GetValueOrDefault(slot) + 1;

            if (!sampleEquipSlotCatLogged && equipSlotCatRowId != 0)
            {
                sampleEquipSlotCat = equipSlotCatRowId;
                sampleEquipSlotCatLogged = true;
            }

            foreach (var cfcId in cfcIds)
            {
                var dungeon = GetOrCreateDungeon(dungeons, cfcId, cfcSheet);
                var armoireItem = new ArmoireItem
                {
                    ItemId = itemId,
                    Name = name,
                    Slot = slot,
                    DungeonName = dungeon.Name,
                    ContentFinderConditionId = dungeon.ContentFinderConditionId,
                };
                dungeon.Items.Add(armoireItem);
                allItems.Add(armoireItem);
            }
        }

        AllItems = allItems;
        DungeonSets = [.. dungeons.Values];
        IsLoaded = true;

        stopwatch.Stop();
        var slotSummary = string.Join(", ", slotDistribution.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        Svc.Log.Information(
            $"[ArmoireAutoFill] database built in {stopwatch.ElapsedMilliseconds}ms: "
            + $"{allItems.Count} entries across {DungeonSets.Count} dungeons. "
            + $"Skipped {skippedNoSource} cabinet items with no dungeon source, "
            + $"{skippedEmptyItem} empty cabinet rows. "
            + $"Slot distribution: {slotSummary}. "
            + $"Sample EquipSlotCategory.RowId: {sampleEquipSlotCat}.");

        var roster = DungeonSets
            .OrderBy(d => d.Level == 0 ? 999 : d.Level)
            .ThenBy(d => d.Name)
            .Select(d => $"{d.Name} L{d.Level}({d.Items.DistinctBy(i => i.ItemId).Count()})");
        Svc.Log.Information($"[ArmoireAutoFill] dungeon roster ({DungeonSets.Count}): {string.Join(" | ", roster)}");
    }

    private static DungeonInfo GetOrCreateDungeon(Dictionary<uint, DungeonInfo> bucket, uint cfcId, Lumina.Excel.ExcelSheet<LuminaContentFinder> cfcSheet)
    {
        if (bucket.TryGetValue(cfcId, out var existing))
            return existing;

        var cfcRow = cfcSheet.GetRowOrDefault(cfcId);
        var name = cfcRow.HasValue ? cfcRow.Value.Name.ExtractText() : $"Duty #{cfcId}";
        if (string.IsNullOrWhiteSpace(name))
            name = $"Duty #{cfcId}";
        var level = cfcRow.HasValue ? cfcRow.Value.ClassJobLevelRequired : (byte)0;

        var info = new DungeonInfo
        {
            ContentFinderConditionId = cfcId,
            Name = name,
            Level = level,
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

    // Slot derivation is from Item.EquipSlotCategory only. An earlier
    // CabinetCategory fallback inflated the Legs count to ~300 because the
    // row-id table for CabinetCategory is patch-volatile and I didn't have
    // the live mapping. Items with no EquipSlotCategory (mostly fashion
    // accessories) now show as Unknown rather than mislabeled.
    private static GearSlot DeriveSlot(uint equipSlotCatRowId, uint cabinetCategoryRowId)
    {
        _ = cabinetCategoryRowId; // reserved for a future, correct fallback
        return MapFromEquipSlotCategory(equipSlotCatRowId);
    }

    private static GearSlot MapFromEquipSlotCategory(uint rowId) => (EquipSlotCategoryEnum)rowId switch
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
