using LazyCrafter.Core;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace LazyCrafter.Adapters;

/// <summary>
/// <see cref="IGameData"/> over the game's Excel sheets (+ LuminaSupplemental for drops / desynth),
/// Plan §Phase 3 task 1. Everything is indexed once in <see cref="Load"/> (a few hundred ms - call it
/// off the framework thread) and answered from dictionaries afterwards; the Core never sees a sheet type.
/// Takes a bare <see cref="GameData"/> (not <c>IDataManager</c>) so <c>tests/LazyCrafter.Probe</c> can run it
/// against the installed game files without the client; GBR node types arrive through an optional lookup.
/// <para>Sheet field names verified against xivdev/EXDSchema and real API-15 usage in GBR / Artisan / ARC (2026-09-03).</para>
/// </summary>
public sealed class LuminaGameData : IGameData
{
    private readonly List<RecipeRow> _recipes = new();
    private readonly Dictionary<uint, uint> _gilVendor = new();        // itemId -> gil (Item.PriceMid)
    private readonly HashSet<uint> _specialShop = new();
    private readonly Dictionary<uint, GatherInfo> _gather = new();
    private readonly HashSet<uint> _fish = new();
    private readonly List<VentureRow> _ventures = new();
    private readonly HashSet<uint> _marketable = new();
    private readonly HashSet<uint> _drops = new();
    private readonly Dictionary<uint, CollectableInfo> _collectables = new();
    private readonly Dictionary<uint, List<DesynthResult>> _desynth = new();
    private readonly Dictionary<uint, string> _names = new();
    private readonly HashSet<uint> _desynthable = new();
    private readonly HashSet<uint> _canBeHq = new();
    private readonly Dictionary<uint, string> _jobAbbr = new();
    private readonly Func<uint, GatherInfo?>? _gbr;
    private Func<uint, bool?>? _marketableOverride;

    public int RecipeCount => _recipes.Count;
    public int GilVendorCount => _gilVendor.Count;
    public int SpecialShopCount => _specialShop.Count;
    public int GatherableCount => _gather.Count;
    public int FishCount => _fish.Count;
    public int VentureCount => _ventures.Count;
    public int MarketableCount => _marketable.Count;
    public int DropCount => _drops.Count;
    public int CollectableCount => _collectables.Count;
    public int DesynthSourceCount => _desynth.Count;
    public bool GbrUsed { get; private set; }
    public TimeSpan LoadTime { get; private set; }

    private LuminaGameData(Func<uint, GatherInfo?>? gbr) => _gbr = gbr;

    /// <summary>When Universalis' marketable list is known, prefer it over the sheet heuristic.</summary>
    public void UseMarketableOverride(Func<uint, bool?> isMarketable) => _marketableOverride = isMarketable;

    public string ItemName(uint itemId) => _names.TryGetValue(itemId, out var n) ? n : $"#{itemId}";
    public bool IsDesynthable(uint itemId) => _desynthable.Contains(itemId);
    /// <summary>Whether the item can exist in high quality (<c>Item.CanBeHq</c>) - the UI only evaluates an HQ price row for these.</summary>
    public bool CanBeHq(uint itemId) => _canBeHq.Contains(itemId);
    /// <summary>ClassJob abbreviation (CRP, BSM, ...) from the sheet; the row id as text when unknown.</summary>
    public string JobAbbr(uint classJobId) => _jobAbbr.TryGetValue(classJobId, out var a) ? a : classJobId.ToString();

    // ---------------------------------------------------------------- IGameData

    public IEnumerable<RecipeRow> Recipes() => _recipes;
    public bool IsGilVendor(uint itemId, out uint gil) => _gilVendor.TryGetValue(itemId, out gil);
    public bool IsSpecialShop(uint itemId) => _specialShop.Contains(itemId);
    public GatherInfo? Gather(uint itemId) => _gather.TryGetValue(itemId, out var g) ? g : null;
    public bool IsFish(uint itemId) => _fish.Contains(itemId);
    public IEnumerable<VentureRow> Ventures() => _ventures;
    public bool IsMarketable(uint itemId) => _marketableOverride?.Invoke(itemId) ?? _marketable.Contains(itemId);
    public bool IsDrop(uint itemId) => _drops.Contains(itemId);
    public CollectableInfo? Collectable(uint itemId) => _collectables.TryGetValue(itemId, out var c) ? c : null;
    public IReadOnlyList<DesynthResult> Desynth(uint itemId) => _desynth.TryGetValue(itemId, out var l) ? l : Array.Empty<DesynthResult>();

    // ---------------------------------------------------------------- loading

    /// <param name="data">Lumina game data (<c>IDataManager.GameData</c> in the plugin).</param>
    /// <param name="log">Where to write the one-line load summary and any warnings.</param>
    /// <param name="gbr">Optional GatherBuddyReborn lookup (<see cref="GbrData.Get"/>) that overrides sheet node types.</param>
    public static LuminaGameData Load(GameData data, Action<string> log, Func<uint, GatherInfo?>? gbr = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var g = new LuminaGameData(gbr);
        g.LoadItems(data);
        g.LoadRecipes(data, log);
        g.LoadShops(data, log);
        g.LoadGathering(data, log);
        g.LoadVentures(data, log);
        g.LoadCollectables(data, log);
        g.LoadSupplemental(data, log);
        g.LoadTime = sw.Elapsed;
        log(g.Summary());
        return g;
    }

    public string Summary() =>
        $"LuminaGameData: {RecipeCount} recipes, {GilVendorCount} gil-vendor, {SpecialShopCount} special-shop, {GatherableCount} gatherable ({(GbrUsed ? "via GBR" : "via sheets")}), " +
        $"{FishCount} fish, {VentureCount} ventures, {MarketableCount} marketable, {DropCount} drop, {CollectableCount} collectable, {DesynthSourceCount} desynth sources in {(int)LoadTime.TotalMilliseconds} ms";

    private static ExcelSheet<T> Sheet<T>(GameData data) where T : struct, IExcelRow<T> =>
        data.GetExcelSheet<T>() ?? throw new InvalidOperationException($"sheet {typeof(T).Name} missing");

    private static SubrowExcelSheet<T> Subrows<T>(GameData data) where T : struct, IExcelSubrow<T> =>
        data.GetSubrowExcelSheet<T>() ?? throw new InvalidOperationException($"subrow sheet {typeof(T).Name} missing");

    private void LoadItems(GameData data)
    {
        foreach (var item in Sheet<Item>(data))
        {
            if (item.RowId == 0) continue;
            var name = item.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            _names[item.RowId] = name;
            // Same heuristic GBR/Artisan use; Universalis' /marketable list overrides it when loaded.
            if (item.ItemSearchCategory.RowId > 0 && !item.IsUntradable) _marketable.Add(item.RowId);
            if (item.Desynth > 0) _desynthable.Add(item.RowId);
            if (item.CanBeHq) _canBeHq.Add(item.RowId);
        }
        foreach (var job in Sheet<ClassJob>(data))
        {
            var abbr = job.Abbreviation.ExtractText();
            if (!string.IsNullOrEmpty(abbr)) _jobAbbr[job.RowId] = abbr;
        }
    }

    private void LoadRecipes(GameData data, Action<string> log)
    {
        foreach (var r in Sheet<Recipe>(data))
        {
            if (r.RowId == 0 || r.ItemResult.RowId == 0) continue;
            var level = r.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0;
            var ingredients = new List<(uint ItemId, int Amount)>(8);
            for (var i = 0; i < r.Ingredient.Count && i < r.AmountIngredient.Count; i++)
            {
                var id = r.Ingredient[i].RowId;
                int amount = r.AmountIngredient[i];
                if (id == 0 || amount == 0) continue;
                ingredients.Add((id, amount));
            }
            if (ingredients.Count == 0) continue;
            _recipes.Add(new RecipeRow(
                RecipeId: r.RowId,
                ResultItemId: r.ItemResult.RowId,
                ResultAmount: Math.Max(1, (int)r.AmountResult),
                JobId: r.CraftType.RowId + 8,                        // CraftType 0..7 = CRP..CUL = ClassJob 8..15
                Level: level,
                Ingredients: ingredients));
        }
    }

    private void LoadShops(GameData data, Action<string> log)
    {
        var items = Sheet<Item>(data);
        foreach (var page in Subrows<GilShopItem>(data))
        {
            foreach (var row in page)
            {
                var id = row.Item.RowId;
                if (id == 0 || _gilVendor.ContainsKey(id)) continue;
                var price = items.TryGetRow(id, out var item) ? item.PriceMid : 0u;
                _gilVendor[id] = price;
            }
        }

        foreach (var shop in Sheet<SpecialShop>(data))
        {
            foreach (var entry in shop.Item)
            {
                foreach (var received in entry.ReceiveItems)
                {
                    var id = received.Item.RowId;
                    if (id != 0) _specialShop.Add(id);
                }
            }
        }
    }

    private void LoadGathering(GameData data, Action<string> log)
    {
        var items = Sheet<Item>(data);

        // Fish first (spearfishing rows are also "gatherable" in the sheets; FSH is a separate SourceKind).
        foreach (var f in Sheet<FishParameter>(data))
            if (f.Item.RowId is > 0 and < 1_000_000) _fish.Add(f.Item.RowId);
        foreach (var s in Sheet<SpearfishingItem>(data))
            if (s.Item.RowId is > 0 and < 1_000_000) _fish.Add(s.Item.RowId);

        // Sheet fallback: GatheringItem -> GatheringPointBase(s) -> GatheringPoint(s) -> GatheringPointTransient.
        var gatheringItemSheet = Sheet<GatheringItem>(data);
        var baseSheet = Sheet<GatheringPointBase>(data);
        var pointSheet = Sheet<GatheringPoint>(data);
        var transientSheet = Sheet<GatheringPointTransient>(data);

        // gatheringItemRowId -> itemId
        var itemByGathering = new Dictionary<uint, (uint ItemId, int Level)>();
        foreach (var gi in gatheringItemSheet)
        {
            var itemId = gi.Item.RowId;
            if (itemId is 0 or >= 1_000_000) continue;
            var level = gi.GatheringItemLevel.ValueNullable?.GatheringItemLevel ?? 0;
            itemByGathering[gi.RowId] = (itemId, level);
        }

        // baseRowId -> (type, timed flags) from any of its points' transient rows
        var pointsByBase = new Dictionary<uint, List<uint>>();
        foreach (var p in pointSheet)
        {
            if (p.GatheringPointBase.RowId == 0 || p.TerritoryType.RowId is 0 or 1) continue;
            if (!pointsByBase.TryGetValue(p.GatheringPointBase.RowId, out var list)) pointsByBase[p.GatheringPointBase.RowId] = list = new List<uint>();
            list.Add(p.RowId);
        }

        foreach (var b in baseSheet)
        {
            if (!pointsByBase.TryGetValue(b.RowId, out var pointIds)) continue;
            var gatheringType = b.GatheringType.RowId;
            if (gatheringType > 4) continue;                       // 0..3 = MIN/BTN nodes, 4 = spearfishing
            var job = gatheringType switch { 0 or 1 => 16u, 2 or 3 => 17u, _ => 18u };

            // Node classification per GBR's GetTimes: GatheringPoint.Type 8 = clouded; else transient row.
            var nodeType = NodeType.Regular;
            foreach (var pid in pointIds)
            {
                if (pointSheet.TryGetRow(pid, out var point) && point.Type == 8) { nodeType = NodeType.Clouded; break; }
                if (!transientSheet.TryGetRow(pid, out var tr)) continue;
                var nt = Classify(tr);
                if (nt != NodeType.Regular) { nodeType = nt; break; }
            }

            foreach (var slot in b.Item)
            {
                if (slot.RowId == 0 || !itemByGathering.TryGetValue(slot.RowId, out var entry)) continue;
                var (itemId, level) = entry;
                if (_fish.Contains(itemId)) continue;
                var collectable = items.TryGetRow(itemId, out var item) && item.IsCollectable;
                var info = new GatherInfo(job, level == 0 ? b.GatheringLevel : level, nodeType, Timed: nodeType is NodeType.Unspoiled or NodeType.Ephemeral or NodeType.Legendary, collectable);
                // An item on both a regular and a timed node counts as regular (GBR's AddNodeToItem rule).
                if (_gather.TryGetValue(itemId, out var existing))
                {
                    if (existing.NodeType != NodeType.Regular && nodeType == NodeType.Regular) _gather[itemId] = info;
                }
                else _gather[itemId] = info;
            }
        }

        // GBR overlay: authoritative node types (incl. legendary/folklore) when it is loaded.
        if (_gbr is not null)
        {
            var replaced = 0;
            foreach (var itemId in _gather.Keys.ToList())
            {
                var g = _gbr(itemId);
                if (g is null) continue;
                var cur = _gather[itemId];
                _gather[itemId] = g with { Level = g.Level > 0 ? g.Level : cur.Level, Collectable = cur.Collectable };
                replaced++;
            }
            GbrUsed = replaced > 0;
        }
    }

    /// <summary>GBR's GetTimes rule, minus the bitfield: ephemeral window when no rare-pop table, else the table's slots.</summary>
    private static NodeType Classify(GatheringPointTransient tr)
    {
        if (tr.GatheringRarePopTimeTable.RowId == 0)
        {
            var start = tr.EphemeralStartTime;
            var end = tr.EphemeralEndTime;
            return start == end || start > 2400 || end > 2400 ? NodeType.Regular : NodeType.Ephemeral;
        }
        var table = tr.GatheringRarePopTimeTable.ValueNullable;
        if (table is null) return NodeType.Regular;
        var hours = 0u;
        for (var i = 0; i < table.Value.Duration.Count && i < table.Value.StartTime.Count; i++)
        {
            int duration = table.Value.Duration[i];
            if (duration == 0) continue;
            if (duration == 160) duration = 200;
            int start = table.Value.StartTime[i];
            var end = (start + duration) % 2400;
            if (start == end || start > 2400 || end > 2400) return NodeType.Regular;
            var s = start / 100; var e = end / 100;
            if (e < s) e += 24;
            for (var h = s; h < e; h++) hours |= 1u << (h % 24);
        }
        return hours == 0xFFFFFF ? NodeType.Regular : NodeType.Unspoiled;
    }

    private void LoadVentures(GameData data, Action<string> log)
    {
        var normal = Sheet<RetainerTaskNormal>(data);
        foreach (var t in Sheet<RetainerTask>(data))
        {
            if (t.RowId == 0 || t.IsRandom || t.Task.RowId == 0) continue;
            if (!normal.TryGetRow(t.Task.RowId, out var n) || n.Item.RowId == 0) continue;
            var category = t.ClassJobCategory.RowId;
            var p = t.RetainerTaskParameter.ValueNullable;

            var thresholds = new List<int>(4);
            if (p is { } param)
            {
                var src = category switch
                {
                    VentureResolver.CategoryMiner or VentureResolver.CategoryBotanist => param.PerceptionDoL,
                    VentureResolver.CategoryFisher => param.PerceptionFSH,
                    _ => param.ItemLevelDoW,
                };
                foreach (var v in src) thresholds.Add(v);
            }

            var quantities = new List<int>(5);
            foreach (var q in n.Quantity) quantities.Add(q);

            _ventures.Add(new VentureRow(
                TaskId: t.RowId,
                ItemId: n.Item.RowId,
                Level: t.RetainerLevel,
                JobCategory: category,
                RequiredGathering: t.RequiredGathering,
                RequiredItemLevel: t.RequiredItemLevel,
                QuantityTiers: quantities,
                RewardThresholds: thresholds));

            // Combat ventures fetch monster drops - the same "this is a drop" fallback GBR uses when no drop table is loaded.
            if (n.GatheringLog.RowId == 0 && n.FishingLog.RowId == 0) _drops.Add(n.Item.RowId);
        }
    }

    private void LoadCollectables(GameData data, Action<string> log)
    {
        foreach (var page in Subrows<CollectablesShopItem>(data))
        {
            foreach (var row in page)
            {
                var itemId = row.Item.RowId;
                if (itemId == 0 || _collectables.ContainsKey(itemId)) continue;
                // Legacy pre-5.0 rows point at refine/scrip row 0 (all zeros); they are not turn-ins any more.
                if (row.CollectablesShopRefine.RowId == 0 || row.CollectablesShopRewardScrip.RowId == 0) continue;
                var refine = row.CollectablesShopRefine.ValueNullable;
                var scrip = row.CollectablesShopRewardScrip.ValueNullable;
                if (refine is null || scrip is null) continue;
                if (scrip.Value.LowReward == 0 && scrip.Value.MidReward == 0 && scrip.Value.HighReward == 0) continue;
                _collectables[itemId] = new CollectableInfo(
                    ItemId: itemId,
                    Currency: (uint)scrip.Value.Currency,
                    LevelMin: (int)row.LevelMin,
                    LevelMax: (int)row.LevelMax,
                    Collectability: [(int)refine.Value.LowCollectability, (int)refine.Value.MidCollectability, (int)refine.Value.HighCollectability],
                    Reward: [(int)scrip.Value.LowReward, (int)scrip.Value.MidReward, (int)scrip.Value.HighReward],
                    ExpRatio: [(int)scrip.Value.ExpRatioLow, (int)scrip.Value.ExpRatioMid, (int)scrip.Value.ExpRatioHigh]);
            }
        }
    }

    /// <summary>
    /// LuminaSupplemental 4.3.0 CSVs (embedded in the package). Drops: MobDrop, DungeonDrop, DungeonChestItem,
    /// DungeonBossDrop, SubmarineDrop, AirshipDrop. Desynth: ItemSupplement rows with source Desynth
    /// (<c>SourceItemId</c> = what you desynth, <c>ItemId</c> = what you get, <c>Probability</c> in percent,
    /// <c>Min/Max</c> quantity; ~4% of rows carry no probability and are taken as certain, 1 unit).
    /// Loaded without <c>PopulateData</c> (no RowRefs needed), which keeps it well under 100 ms.
    /// </summary>
    private void LoadSupplemental(GameData data, Action<string> log)
    {
        void AddDrops<T>(string resource, Func<T, uint> itemId) where T : ICsv, new()
        {
            try
            {
                var rows = CsvLoader.LoadResource<T>(resource, true, out var failed, out var exceptions);
                foreach (var r in rows) { var id = itemId(r); if (id != 0) _drops.Add(id); }
                if (failed.Count > 0) log($"LuminaSupplemental {resource}: {failed.Count} unparsable lines");
            }
            catch (Exception ex)
            {
                log($"LuminaSupplemental {resource} failed to load: {ex.Message}");
            }
        }

        AddDrops<MobDrop>(CsvLoader.MobDropResourceName, r => r.ItemId);
        AddDrops<DungeonDrop>(CsvLoader.DungeonDropItemResourceName, r => r.ItemId);
        AddDrops<DungeonChestItem>(CsvLoader.DungeonChestItemResourceName, r => r.ItemId);
        AddDrops<DungeonBossDrop>(CsvLoader.DungeonBossDropResourceName, r => r.ItemId);
        AddDrops<SubmarineDrop>(CsvLoader.SubmarineDropResourceName, r => r.ItemId);
        AddDrops<AirshipDrop>(CsvLoader.AirshipDropResourceName, r => r.ItemId);

        try
        {
            var rows = CsvLoader.LoadResource<ItemSupplement>(CsvLoader.ItemSupplementResourceName, true, out _, out _);
            foreach (var r in rows)
            {
                if (r.ItemSupplementSource != ItemSupplementSource.Desynth || r.SourceItemId == 0 || r.ItemId == 0) continue;
                if (!_desynth.TryGetValue(r.SourceItemId, out var list)) _desynth[r.SourceItemId] = list = new List<DesynthResult>();
                var chance = r.Probability is { } p ? Math.Clamp((double)p / 100.0, 0, 1) : 1.0;
                var qty = r.Min is { } mn && r.Max is { } mx && mx >= mn ? (mn + mx) / 2.0 : 1.0;
                list.Add(new DesynthResult(r.ItemId, chance, qty));
            }
        }
        catch (Exception ex)
        {
            log($"LuminaSupplemental ItemSupplement failed to load; desynth values unavailable: {ex.Message}");
        }
    }
}
