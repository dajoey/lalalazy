using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace LazySkywardTracker;

public sealed class InventoryScanner
{
    private readonly IDataManager _dataManager;
    private readonly Dictionary<uint, SkywardItemInfo> _itemLookup = new();

    private Dictionary<uint, InventoryProjection>? _cache;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    /// <summary>Aggregated point projection for one job/achievement.</summary>
    public sealed class InventoryProjection
    {
        public uint TotalPoints { get; set; }
        public List<ItemBreakdown> Items { get; } = new();
    }

    /// <summary>One line-item in a projection tooltip.</summary>
    public readonly record struct ItemBreakdown(string ItemName, uint Quantity, uint PerItemPoints, uint TotalPoints);

    /// <summary>Static metadata for a single Skybuilders item that can be turned in for points.</summary>
    private readonly record struct SkywardItemInfo(
        uint AchievementId,
        string ItemName,
        bool IsCollectable,
        // Collectability thresholds (crafted items only)
        ushort BaseRating,
        ushort MidRating,
        ushort HighRating,
        // Points per turn-in at each tier (crafted) or flat points (gathered)
        uint BasePoints,
        uint MidPoints,
        uint HighPoints,
        // Quantity required per inspection (gathered items only, 0 for crafted)
        uint AmountRequired);

    public InventoryScanner(IDataManager dataManager)
    {
        _dataManager = dataManager;
        BuildLookupTables();
    }

    /// <summary>Force the next ScanInventory call to re-read inventory.</summary>
    public void InvalidateCache() => _cache = null;

    // ─── Lookup Table Construction ───────────────────────────────────────

    private void BuildLookupTables()
    {
        try { BuildCrafterLookup(); }
        catch (Exception ex) { Plugin.PluginLog.Error(ex, "Failed to build crafter item lookup"); }

        try { BuildGathererLookup(); }
        catch (Exception ex) { Plugin.PluginLog.Error(ex, "Failed to build gatherer item lookup"); }

        Plugin.PluginLog.Info($"InventoryScanner: indexed {_itemLookup.Count} skyward turn-in item(s)");
    }

    /// <summary>
    /// Reads the HWDCrafterSupply Lumina sheet to build a lookup from crafted-collectable item ID
    /// to achievement ID and point values (using PostPhase rewards since the restoration is complete).
    /// Sheet rows 0–7 map to CRP, BSM, ARM, GSM, LTW, WVR, ALC, CUL in standard DoH order.
    /// </summary>
    private void BuildCrafterLookup()
    {
        var sheet = _dataManager.GetExcelSheet<HWDCrafterSupply>();
        if (sheet == null) return;

        // Row index → achievement ID (standard DoH ordering)
        uint[] crafterAchievements = [2491, 2494, 2497, 2500, 2503, 2506, 2509, 2512];

        foreach (var row in sheet)
        {
            if (row.RowId >= (uint)crafterAchievements.Length) continue;
            var achievementId = crafterAchievements[row.RowId];

            foreach (var param in row.HWDCrafterSupplyParams)
            {
                var itemId = param.ItemTradeIn.RowId;
                if (itemId == 0) continue;

                // PostPhase rewards (restoration is complete)
                uint basePoints = GetRewardPoints(param.BaseCollectableRewardPostPhase);
                uint midPoints = GetRewardPoints(param.MidCollectableRewardPostPhase);
                uint highPoints = GetRewardPoints(param.HighCollectableRewardPostPhase);

                if (basePoints == 0 && midPoints == 0 && highPoints == 0) continue;

                var itemName = GetItemName(param.ItemTradeIn.RowId) ?? $"Item#{itemId}";

                _itemLookup[itemId] = new SkywardItemInfo(
                    achievementId, itemName, IsCollectable: true,
                    param.BaseCollectableRating, param.MidCollectableRating, param.HighCollectableRating,
                    basePoints, midPoints, highPoints,
                    AmountRequired: 0);

                Plugin.PluginLog.Debug(
                    $"Crafter: {itemName} (ID={itemId}) → ach {achievementId}, pts {basePoints}/{midPoints}/{highPoints}, " +
                    $"ratings {param.BaseCollectableRating}/{param.MidCollectableRating}/{param.HighCollectableRating}");
            }
        }
    }

    /// <summary>
    /// Reads the HWDGathererInspection Lumina sheet to build a lookup from gathered-material item ID
    /// to achievement ID and point values. Gathered items are regular stacks; points = qty ÷ amountRequired × pts.
    /// </summary>
    private void BuildGathererLookup()
    {
        var sheet = _dataManager.GetExcelSheet<HWDGathererInspection>();
        if (sheet == null) return;

        foreach (var row in sheet)
        {
            // The HWDGathererInspection row index encodes the gathering class:
            //   row 1 = Miner, row 2 = Botanist, row 3 = Fisher (row 0 is empty).
            // This is the game's own classification; never infer the class from item names.
            uint rowAchievementId = row.RowId switch
            {
                1 => 2515u, // MIN - Skyward Sledgehammer III
                2 => 2518u, // BTN - Skyward Scythe III
                3 => 2521u, // FSH - Skyward Rod III
                _ => 0u,
            };
            if (rowAchievementId == 0) continue;

            foreach (var entry in row.HWDGathererInspectionData)
            {
                // Resolve the actual Item ID. Two paths:
                //   MIN/BTN: RequiredItem → GatheringItem → Item
                //   FSH:     FishParameter → Item  (RequiredItem is 0 for fish)
                uint itemId;
                uint achievementId;
                var gatheringItemRef = entry.RequiredItem;
                var fishParamRef = entry.FishParameter;

                if (gatheringItemRef.RowId != 0)
                {
                    // MIN or BTN path
                    var gatheringItem = gatheringItemRef.ValueNullable;
                    if (gatheringItem is not { } gi) continue;
                    itemId = gi.Item.RowId;
                    if (itemId == 0) continue;

                    achievementId = rowAchievementId;
                }
                else if (fishParamRef.RowId != 0)
                {
                    // FSH path — item ID comes from FishParameter → Item
                    var fishParam = fishParamRef.ValueNullable;
                    if (fishParam is not { } fp) continue;
                    itemId = fp.Item.RowId;
                    if (itemId == 0) continue;
                    achievementId = rowAchievementId; // FSH – Skyward Rod III
                }
                else
                {
                    continue; // Empty entry
                }

                var amountRequired = entry.AmountRequired;
                if (amountRequired == 0) continue;

                // Reward array has 2 entries: [0] = active-phase, [1] = post-phase.
                // Take the best non-zero value (prefer index 1 for PostPhase).
                uint points = 0;
                int idx = 0;
                foreach (var rewardRef in entry.Reward)
                {
                    if (rewardRef.RowId > 0)
                    {
                        var reward = rewardRef.ValueNullable;
                        if (reward is { } r && r.Points > 0)
                        {
                            if (idx == 1 || points == 0)
                                points = (uint)r.Points;
                        }
                    }
                    idx++;
                }

                if (points == 0) continue;

                var name = GetItemName(itemId) ?? $"Item#{itemId}";

                // Keep the entry with the highest point value if we see the same item in multiple phases
                if (_itemLookup.TryGetValue(itemId, out var existing) && existing.BasePoints >= points)
                    continue;

                _itemLookup[itemId] = new SkywardItemInfo(
                    achievementId, name, IsCollectable: false,
                    0, 0, 0,
                    BasePoints: points, MidPoints: 0, HighPoints: 0,
                    amountRequired);

                Plugin.PluginLog.Debug(
                    $"Gatherer: {name} (ID={itemId}) → ach {achievementId}, pts {points}, amt {amountRequired}");
            }
        }
    }

    // ─── Inventory Scanning ──────────────────────────────────────────────

    /// <summary>
    /// Scans the player's four main inventory bags for Skybuilders items and calculates
    /// the projected Skyward points per achievement/job. Results are cached for 2 seconds.
    /// </summary>
    public unsafe Dictionary<uint, InventoryProjection> ScanInventory()
    {
        if (_cache != null && DateTime.UtcNow < _cacheExpiry)
            return _cache;

        var results = new Dictionary<uint, InventoryProjection>();

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return CacheAndReturn(results);

        var bags = new[]
        {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        foreach (var bagType in bags)
        {
            var container = inventoryManager->GetInventoryContainer(bagType);
            if (container == null) continue;

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;

                uint itemId = slot->ItemId;
                // Strip HQ flag (shouldn't happen for Skybuilders items, but safety)
                if (itemId >= 1000000) itemId -= 1000000;

                if (!_itemLookup.TryGetValue(itemId, out var info)) continue;

                uint quantity = (uint)slot->Quantity;
                uint perItem;
                uint totalPts;

                if (info.IsCollectable)
                {
                    // Crafted collectables: match collectability against tier thresholds
                    ushort collectability = slot->SpiritbondOrCollectability;

                    if (info.HighRating > 0 && collectability >= info.HighRating && info.HighPoints > 0)
                        perItem = info.HighPoints;
                    else if (info.MidRating > 0 && collectability >= info.MidRating && info.MidPoints > 0)
                        perItem = info.MidPoints;
                    else if (info.BaseRating > 0 && collectability >= info.BaseRating && info.BasePoints > 0)
                        perItem = info.BasePoints;
                    else
                        continue; // Below minimum collectability

                    totalPts = perItem * quantity;
                }
                else
                {
                    // Gathered materials: flat points per inspection batch
                    if (info.AmountRequired == 0) continue;
                    uint inspections = quantity / info.AmountRequired;
                    if (inspections == 0) continue;
                    perItem = info.BasePoints;
                    totalPts = inspections * perItem;
                }

                if (!results.TryGetValue(info.AchievementId, out var proj))
                {
                    proj = new InventoryProjection();
                    results[info.AchievementId] = proj;
                }

                proj.TotalPoints += totalPts;
                proj.Items.Add(new ItemBreakdown(info.ItemName, quantity, perItem, totalPts));
            }
        }

        return CacheAndReturn(results);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private Dictionary<uint, InventoryProjection> CacheAndReturn(Dictionary<uint, InventoryProjection> results)
    {
        _cache = results;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
        return results;
    }

    private static uint GetRewardPoints(RowRef<HWDCrafterSupplyReward> rewardRef)
    {
        if (rewardRef.RowId == 0) return 0;
        var reward = rewardRef.ValueNullable;
        return reward is { } r ? (uint)r.Points : 0;
    }

    private string? GetItemName(uint itemId)
    {
        if (itemId == 0) return null;
        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null) return null;
        if (!itemSheet.TryGetRow(itemId, out var item)) return null;
        var name = item.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
