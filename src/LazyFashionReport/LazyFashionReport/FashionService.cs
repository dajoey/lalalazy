using System.Net.Http;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using LazyFashionReport.Adapters;
using LazyFashionReport.Core;

namespace LazyFashionReport;

/// <summary>
/// Orchestrates everything: listens for the FashionCheck addon, resolves the current week,
/// keeps the remote datasets fresh (background task, never the game thread), snapshots owned
/// items, and rebuilds the prediction when inputs change.
///
/// Threading contract (the LazyCrafter lesson): every game-memory read is batched into ONE
/// framework-thread prologue per pass; remote fetches are Task-based; the draw thread only
/// reads immutable snapshots. The only unsafe surface is CurrentAddon (an addon pointer,
/// written/read on the framework thread via the AddonLifecycle callbacks).
/// </summary>
internal sealed class FashionService : IDisposable
{
    private readonly Plugin _plugin;
    private readonly SheetAdapter _sheets = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly RemoteDataSource _remote;
    private readonly ArtisanCraft _artisan;
    private readonly RecipeIndex _recipes;
    private readonly VendorIndex _vendors;
    private readonly MarketQuotes _market;
    private IReadOnlyList<MissingPiece> _missing = Array.Empty<MissingPiece>();
    private IReadOnlyList<SlotPlan> _slotPlans = Array.Empty<SlotPlan>();

    private RemoteDataSource.XivStatsRoot? _xiv;
    private RemoteDataSource.ReportState? _state;
    private CrowdDataAdapter? _crowd;
    private FashionWeek? _week;
    private OutfitReport? _outfit;
    private HashSet<uint>? _owned;
    private OwnedCatalog? _ownedCatalog;
    private OutfitAssembly? _assembly;
    private string?[]? _liveHints;
    private bool _fetchInFlight;
    private long _nextFetchTick;
    private long _lastPredictTick;
    private volatile bool _refreshRequested;

    private IntPtr _currentAddon; // AddonFashionCheck* as IntPtr; never dereferenced off the framework thread

    public FashionService(Plugin plugin)
    {
        _plugin = plugin;
        _remote = new RemoteDataSource(_http, CacheDir(), m => Plugin.Log.Information($"[LFR] {m}"));
        _artisan = new ArtisanCraft(Plugin.Pi, Plugin.Log);
        _recipes = RecipeIndex.Load(Plugin.Data, Plugin.Log);
        _vendors = new VendorIndex(Plugin.Data, m => Plugin.Log.Information($"[LFR] {m}"),
            m => Plugin.Log.Warning($"[LFR] {m}"));
        _market = new MarketQuotes(CacheDir(), typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "dev",
            m => Plugin.Log.Information($"[LFR] {m}"));
    }

    private static string CacheDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "pluginConfigs", "LazyFashionReportCache");

    public FashionWeek? Week => _week;
    public OutfitReport? Outfit => _outfit;
    public HashSet<uint>? OwnedItems => _owned;
    /// <summary>P3 get-to catalog: where each owned piece actually sits (bags/dresser/armoire).</summary>
    public OwnedCatalog? OwnedCatalog => _ownedCatalog ?? (_owned is null ? null : OwnedCatalog.Empty);
    public IReadOnlyList<MissingPiece> MissingPieces => _missing;
    public IReadOnlyList<SlotPlan> SlotPlans => _slotPlans;

    /// <summary>Location note for a Wear row: "(in glamour dresser)" etc.; empty when the
    /// piece is in bags/equipped (nothing to say).</summary>
    public string LocationNoteFor(uint itemId) => _ownedCatalog?.LocationNote(itemId) ?? "";

    /// <summary>P4 planner: the best 80+ outfit from owned pieces + owned dyes (read-only).</summary>
    public OutfitAssembly? Assembly => _assembly;

    /// <summary>P4 executor half (v0.5.0.0): the latest dry-run readout, if one was run.</summary>
    public ApplyDryRun? LastDryRun => _dryRun;
    private ApplyDryRun? _dryRun;

    /// <summary>
    /// P4 executor half, dry-run release: build the executable apply plan from the CURRENT
    /// assembly + live snapshot and simulate it - one pass, read-only, no mutations. Every
    /// step line is logged to the LazyFashionReport context so an in-game verify from ffxivdb
    /// can grade exactly what the plugin said it would do. The live mover ships only after
    /// this readout is verified against the character sheet.
    /// </summary>
    public ApplyDryRun? DryRunApply()
    {
        if (_assembly is null || _week is null) return null;
        try
        {
            // ONE framework-thread snapshot: equipped appearance + stains, per-item location,
            // dye stock (ApplyExecutor is all reads - the dry run must not move anything).
            var itemIds = _assembly.Pieces.Where(p => p.ItemId != 0).Select(p => p.ItemId).ToList();
            var plusTwoStains = new Dictionary<FashionSlot, uint>();
            foreach (var slot in Enum.GetValues<FashionSlot>())
            {
                var s = _crowd?.PreferredStainFor(_week, slot) ?? 0;
                if (s != 0) plusTwoStains[slot] = s;
            }
            var snap = Plugin.Framework.RunOnFrameworkThread(
                () => ApplyExecutor.Snapshot(itemIds, plusTwoStains.Values)).Result;

            var steps = ApplyPlanBuilder.Build(
                _assembly,
                plusTwoStains,
                snap.Equipped,
                snap.EquippedStain,
                id => snap.Locations.TryGetValue(id, out var loc) ? (loc.Storage, loc.Coord, loc.Stain) : null,
                stain => ClientReader.StainToDyeItem.GetValueOrDefault(stain),
                snap.DyeItemLocations,
                id => _sheets.ItemName(id),
                _sheets.StainToName);

            var run = ApplySimulator.Run(steps, _assembly.Total);

            // The audit log: one line per step, prefixed for the ffxivdb grading query.
            Plugin.Log.Information($"[LFR] dry-run apply: {run.Applies} would apply, {run.Dyes} dye(s), {run.Skips} skip(s), predicted {run.PredictedTotal}");
            foreach (var r in run.Steps)
                Plugin.Log.Information($"[LFR] dry-run | {r.Line}");

            // Offline proof for the live executor's destination mapping: dump the equip
            // container layout as seen right now. The live card reads this from ffxivdb to
            // verify the EquippedItems slot indices before writing any MoveItemSlot call.
            LogEquippedLayout(snap);

            _dryRun = run;
            return run;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[LFR] dry-run apply failed");
            return null;
        }
    }

    /// <summary>Log the equipped container layout (slot index -> container/slot/item) so the
    /// live executor card can verify the equip-slot mapping from ffxivdb without a debugger.</summary>
    private void LogEquippedLayout(ApplySnapshot snap)
    {
        try
        {
            foreach (var (slot, id) in snap.Equipped.OrderBy(kv => (int)kv.Key))
            {
                var stain = snap.EquippedStain.GetValueOrDefault(slot, 0u);
                Plugin.Log.Information(
                    $"[LFR] equipped-layout | {(int)slot} {slot.DisplayName()} = item {id} stain {stain}");
            }

            // v0.5.1.0: the RAW container view the live mover will address via MoveItemSlot.
            // The agent view above is the Fashion Report's judging source; this is the
            // executor's destination coordinate space. Both, one press, one ffxivdb query.
            foreach (var row in snap.RawEquipped.OrderBy(r => r.Slot))
            {
                Plugin.Log.Information(
                    $"[LFR] raw-equipped | cont {row.Container} slot {row.Slot} = item {row.ItemId} glam {row.GlamourId} stain {row.Stain0}");
            }
        }
        catch { /* layout log is best-effort */ }
    }


    /// <summary>Last judged-week summary line ("" with no history yet) + accuracy over the
    /// recent weeks. P5: the predictor is only as good as its measured diff.</summary>
    public string JudgedSummary =>
        JudgedFeedback.SummaryLine(_plugin.Config.JudgedHistory) is { Length: > 0 } line
            ? line + $" | within 1pt: {JudgedFeedback.Accuracy(_plugin.Config.JudgedHistory).Within}/{JudgedFeedback.Accuracy(_plugin.Config.JudgedHistory).Total} of recent weeks"
            : "";

    /// <summary>
    /// P5 feedback loop: while the FashionCheck addon is open, read the judged result from
    /// AgentFashion (the result screen sets OpenType == Result) and record ONE entry per
    /// judged week - predicted total at submit time vs Masked Rose's awarded score. Runs on
    /// the framework thread (Tick). Never throws; a missing read is simply no record.
    /// </summary>
    private void HarvestJudged()
    {
        try
        {
            var judged = Adapters.ClientReader.ReadJudgedResult();
            if (judged is not { } r) return;
            var predicted = _outfit?.Total;
            if (predicted is null or 0) return;   // no live prediction: nothing to diff against
            var record = JudgedFeedback.Next(_plugin.Config.JudgedHistory, r.Week, predicted.Value, r.Score, DateTime.UtcNow);
            if (record is null) return;
            _plugin.Config.JudgedHistory = JudgedFeedback.Append(_plugin.Config.JudgedHistory, record).ToList();
            _plugin.SaveConfig();
            var note = record.WithinOne
                ? "within 1 point - the predictor held"
                : $"OFF BY {record.Diff:+0;-#;0} - predictor needs attention";
            Plugin.Log.Information($"[LFR] judged week {record.Week}: awarded {record.Awarded}, predicted {record.Predicted} ({note})");
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[LFR] judged harvest skipped: {ex.Message}");
        }
    }

    /// <summary>xivstats crowd dataset loaded (candidates + crowd dyes). Honest per-source
    /// status: week 449's "no hint" bug hid behind a combined flag that was true while the
    /// actual hint source (fashionreportxiv) had failed to bind.</summary>
    public bool XivLoaded => _xiv != null;

    /// <summary>fashionreportxiv report-state loaded (theme, hints, exact dyes).</summary>
    public bool StateLoaded => _state != null;

    public SheetAdapter Sheets => _sheets;

    public void Start()
    {
        Plugin.Framework.RunOnFrameworkThread(() => _sheets.Load(Plugin.Data));
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FashionCheck", OnAddonPostSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreClose, "FashionCheck", OnAddonPreClose);
        _ = RefreshRemoteAsync();
        // Buy leg: warm the vendor index off-thread and set the market scope from the live
        // world once known. Both degrade silently when the player is not logged in yet.
        _ = Task.Run(() =>
        {
            try { _vendors.EnsureBuiltPublic(); } catch { /* reported inside */ }
        });
        Plugin.ClientState.Login += OnLogin;
        SetMarketScopeFromPlayer();
    }

    private void OnLogin() => SetMarketScopeFromPlayer();

    /// <summary>Market scope = the current world name. API 15: LocalPlayer hangs off
    /// IObjectTable (the sibling plugins' proven pattern), not IClientState.</summary>
    private void SetMarketScopeFromPlayer()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player is null) return;
            // API 15: CurrentWorld is a non-nullable RowRef<World>; IsValid is the null check.
            var world = player.CurrentWorld.IsValid
                ? player.CurrentWorld.ValueNullable?.Name.ToString()
                : null;
            if (!string.IsNullOrWhiteSpace(world)) SetMarketScope(world);
        }
        catch { /* scope stays empty; market labels simply show no number */ }
    }

    private void OnAddonPostSetup(AddonEvent type, AddonArgs args)
    {
        _currentAddon = args.Addon.Address;
        RefreshFromAddon();
        if (_plugin.Config.AutoOpen)
            _plugin.OpenReport();
    }

    private void OnAddonPreClose(AddonEvent type, AddonArgs args)
    {
        _currentAddon = IntPtr.Zero;
        _liveHints = null;
    }

    public void RequestRefresh()
    {
        _refreshRequested = true;
        _nextFetchTick = 0;
    }

    /// <summary>Framework tick (game thread): light, throttled, never throws.</summary>
    public void Tick()
    {
        try
        {
            var now = Environment.TickCount64;

            if (!_fetchInFlight && (now >= _nextFetchTick || _refreshRequested))
            {
                var reason = _refreshRequested ? "manual" : "scheduled";
                _refreshRequested = false;
                _ = RefreshRemoteAsync(reason);
                _nextFetchTick = now + 3600_000;
            }

            // While the FashionCheck addon is open, keep the live hints + prediction fresh.
            // With no addon and no live hints, still keep ONE remote-seeded rebuild alive so
            // /lfr shows the week's theme + hints before the player opens the game window
            // (equipped/candidates still need the addon or refresh).
            if (_currentAddon != IntPtr.Zero && now - _lastPredictTick > 2000)
            {
                var hints = ReadHints();
                if (hints != null && !HintsEqual(hints, _liveHints))
                {
                    _liveHints = hints;
                    RebuildAll();
                }
                else if (_outfit is null)
                {
                    RebuildAll();
                }
                HarvestJudged();
                _lastPredictTick = now;
            }
            else if (_currentAddon == IntPtr.Zero && _liveHints is null && _outfit is null && StateLoaded)
            {
                RebuildAll();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "LFR tick failed");
        }
    }

    // ---- unsafe surface, framework thread only ----

    private unsafe string?[]? ReadHints()
    {
        if (_currentAddon == IntPtr.Zero) return null;
        return Adapters.ClientReader.ReadAddonHints((FFXIVClientStructs.FFXIV.Client.UI.AddonFashionCheck*)_currentAddon);
    }

    private unsafe (List<EquippedItem>? eq, HashSet<uint>? owned) ReadGame(CrowdDataAdapter? crowd, bool alwaysOwned = false)
    {
        var eq = ClientReader.ReadEquipped();
        HashSet<uint>? owned = null;
        if (alwaysOwned || _plugin.Config.FilterOwned)
        {
            var candidates = new HashSet<uint>();
            if (crowd != null && _week != null)
                foreach (var slot in Enum.GetValues<FashionSlot>())
                {
                    if (!_week.IsHinted(slot)) continue;
                    foreach (var c in crowd.CandidatesFor(_week, slot, null))
                        candidates.Add(c.ItemId);
                }
            var catalog = ClientReader.ReadOwnedCatalog(candidates.Count > 0 ? candidates : null);
            _ownedCatalog = catalog;   // P3: keep the per-location view for the Wear notes
            owned = catalog.Ids();
        }
        return (eq, owned);
    }

    public void RefreshFromAddon()
    {
        _liveHints = ReadHints();
        RebuildAll();
    }

    private static bool HintsEqual(string?[] a, string?[]? b)
    {
        if (b is null || a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private void RebuildAll()
    {
        RebuildWeek();
        RebuildPrediction();
    }

    private void RebuildWeek()
    {
        var week = ComputeWeek();
        var theme = _state?.LastOptions?.ReportTitle ?? "";
        var live = _liveHints ?? new string?[11];

        var frxivHints = new Dictionary<FashionSlot, string>();
        if (_state?.LastOptions?.Hints is { } sh)
            foreach (var h in sh)
            {
                if (ParseSlot(h.Slot) is { } s && !string.IsNullOrWhiteSpace(h.Hint))
                    frxivHints[s] = h.Hint;
            }

        var hintList = new string?[11];
        for (var i = 0; i < 11; i++)
        {
            var l = live.Length > i ? live[i] : null;
            hintList[i] = !string.IsNullOrWhiteSpace(l)
                ? l
                : frxivHints.TryGetValue((FashionSlot)i, out var fr) ? fr : null;
        }

        _week = new FashionWeek
        {
            Week = week,
            Theme = theme,
            Hints = hintList,
            PlusTwoDyes = BuildDyeMap(plus2: true),
            PlusOneShades = BuildDyeMap(plus2: false),
        };
    }

    private IReadOnlyDictionary<FashionSlot, string> BuildDyeMap(bool plus2)
    {
        var result = new Dictionary<FashionSlot, string>();
        if (_state?.DyeData is { } dd)
            foreach (var (key, entry) in dd)
            {
                if (ParseSlot(key) is { } slot)
                {
                    var v = plus2 ? entry.Plus2 : entry.Plus1;
                    if (!string.IsNullOrWhiteSpace(v))
                        result[slot] = v;
                }
            }
        return result;
    }

    private static FashionSlot? ParseSlot(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "weapon" => FashionSlot.Weapon,
        "head" => FashionSlot.Head,
        "body" => FashionSlot.Body,
        "hands" => FashionSlot.Hands,
        "legs" => FashionSlot.Legs,
        "feet" => FashionSlot.Feet,
        "ears" => FashionSlot.Ears,
        "neck" => FashionSlot.Neck,
        "wrist" or "wrists" => FashionSlot.Wrist,
        "ringl" or "ring left" or "ring (left)" => FashionSlot.RingL,
        "ringr" or "ring right" or "ring (right)" => FashionSlot.RingR,
        _ => null,
    };

    public static int ComputeWeek()
    {
        var epoch = new DateTime(2018, 1, 30, 8, 0, 0, DateTimeKind.Utc);
        return (int)Math.Floor((DateTime.UtcNow - epoch).TotalDays / 7.0) + 1;
    }

    private void RebuildPrediction()
    {
        if (_week is null) return;

        var crowd = EnsureCrowdAdapter();

        // ONE framework-thread prologue for all game reads (LazyCrafter threading pattern).
        // The owned snapshot is ALWAYS taken (not only under FilterOwned): the fetch/missing
        // view needs to know what is missing even when the wear list is unfiltered (UI unhide).
        var (eq, owned) = Plugin.Framework.RunOnFrameworkThread(() => ReadGame(crowd, alwaysOwned: true)).Result;

        var eqArray = new EquippedItem?[11];
        foreach (var e in eq ?? Enumerable.Empty<EquippedItem>())
            if ((int)e.Slot < 11) eqArray[(int)e.Slot] = e;

        _owned = owned;
        if (crowd != null && owned is { Count: > 0 })
            Plugin.Framework.RunOnFrameworkThread(() => _sheets.WarmItemNames(owned, Plugin.Data)).Wait();

        _outfit = Predictor.Build(_week, eqArray, _sheets.StainFamilies, crowd,
            _plugin.Config.FilterOwned ? _owned : null);

        // UI unhide (v0.2.0.0): the flat per-slot plan — wear + fetch with sources, every
        // hinted slot rendered, nothing dropped for being uncraftable.
        _slotPlans = SlotPlanner.Compose(_week, crowd, _owned, id => _recipes.ForItem(id),
            _plugin.Config.FilterOwned);

        // Buy leg (v0.3.0.0): resolve each missing piece's real source. Built on the same
        // snapshot so the label and the plan always agree; market medians prime in the
        // background (they are display-only).
        if (_owned is { Count: > 0 })
        {
            foreach (var plan in _slotPlans)
                foreach (var piece in plan.Fetch)
                    piece.Buy = ResolveBuy(piece.Item.ItemId);
            var marketIds = _slotPlans.SelectMany(p => p.Fetch)
                .Where(f => f.Buy is { Source: BuySource.Market })
                .Select(f => f.Item.ItemId)
                .Distinct()
                .ToList();
            if (marketIds.Count > 0)
                _ = _market.PrimeAsync(marketIds);
        }

        // P4 planner half (v0.4.0.0): the best 80+ outfit from owned pieces + owned dyes.
        // Read-only; the equip/dye executor is a separate, later step.
        var plusTwoStains = new Dictionary<FashionSlot, uint>();
        foreach (var slot in Enum.GetValues<FashionSlot>())
        {
            var s = crowd?.PreferredStainFor(_week, slot) ?? 0;
            if (s != 0) plusTwoStains[slot] = s;
        }
        _assembly = OutfitAssembler.Build(_week, crowd, _ownedCatalog,
            id => _sheets.ItemName(id), plusTwoStains,
            ClientReader.ReadOwnedStains(), _sheets.StainToName, _sheets.StainFamilies);

        // The Artisan craft list (unchanged behavior, FetchMissingCraft toggle): built from
        // the same crowd + owned snapshot so the two views always agree.
        _missing = _plugin.Config.FetchMissingCraft
            ? FetchPlan.Build(_week, crowd, _owned, id => _recipes.ForItem(id))
            : Array.Empty<MissingPiece>();
    }

    /// <summary>Game-side source resolution for one item (buy leg). Runs wherever the crowd
    /// data is already materialized; sheet reads are dictionary answers after the one-time
    /// index build. Craftable check first (the craft leg already ships), then gil vendor,
    /// then placed special shop, then market.</summary>
    private BuyOption ResolveBuy(uint itemId)
    {
        var marketable = Plugin.Data.GameData?.GetExcelSheet<Lumina.Excel.Sheets.Item>() is { } items
            && items.TryGetRow(itemId, out var it) && it.ItemSearchCategory.RowId != 0;
        return BuyResolver.Resolve(
            itemId,
            id => _recipes.ForItem(id),
            id => _vendors.GilVendorFor(id) is { } v
                ? ((uint, string?, uint, uint, float, float)?)(v.Price,
                    v.Vendor is { } pv ? $"{pv.NpcName} ({pv.ZoneName} {pv.X:0.0}, {pv.Y:0.0})" : null,
                    v.Vendor?.TerritoryId ?? 0, v.Vendor?.MapId ?? 0, v.Vendor?.X ?? 0, v.Vendor?.Y ?? 0)
                : null,
            id => _vendors.SpecialShopFor(id) is { } ss
                ? new BuyOption
                {
                    Source = BuySource.SpecialShop,
                    ShopId = ss.Offer.ShopId,
                    Label = $"{ss.Vendor.NpcName} ({ss.Vendor.ZoneName} {ss.Vendor.X:0.0}, {ss.Vendor.Y:0.0}) - {string.Join(" + ", ss.Offer.Costs.Select(c => c.Phrase))}",
                    Costs = ss.Offer.Costs,
                    TerritoryId = ss.Vendor.TerritoryId,
                    MapId = ss.Vendor.MapId,
                    MapX = ss.Vendor.X,
                    MapY = ss.Vendor.Y,
                }
                : null,
            marketable);
    }

    /// <summary>Median market price for an item, when a quote was fetched (buy leg display).</summary>
    public uint? MarketMedianFor(uint itemId) => _market.MedianFor(itemId);

    /// <summary>Market scope for Universalis quotes: the player's current world, read live.</summary>
    public void SetMarketScope(string? world) => _market.SetScope(world);

    /// <summary>Start one craft via Artisan's public IPC (auto-dress v1 step 4). Framework thread
    /// only (CraftItem opens the crafting log). Returns null when the request was handed over, or
    /// the error text to surface in the window.</summary>
    public string? CraftViaArtisan(uint recipeId) => _artisan.Craft(recipeId);

    /// <summary>True while Artisan is mid-craft (null when Artisan is not installed).</summary>
    public bool? ArtisanBusy => _artisan.IsBusy();

    /// <summary>Whether Artisan is present - drives whether the craft buttons render at all.</summary>
    public bool ArtisanInstalled => _artisan.Installed;

    private CrowdDataAdapter? EnsureCrowdAdapter()
    {
        if (_crowd != null) return _crowd;
        if (_xiv == null && _state == null) return null;
        _crowd = new CrowdDataAdapter(_xiv, _state, _sheets.DyeNameToStain, _sheets.CategoryNameToRow, _sheets.ItemNameById);
        return _crowd;
    }

    private async Task RefreshRemoteAsync(string reason = "scheduled")
    {
        if (_fetchInFlight) return;
        _fetchInFlight = true;
        try
        {
            var xiv = await _remote.FetchXivStatsAsync(default);
            var state = await _remote.FetchReportStateAsync(default);
            // Say WHICH source failed: a null here is a parse/binding problem, not just a
            // network one, and the old combined log line hid the v0.1.0.0 binding bug.
            if (xiv == null) Plugin.Log.Warning("[LFR] xivstats dataset unavailable (fetch AND cache read failed)");
            if (state == null) Plugin.Log.Warning("[LFR] fashionreportxiv report-state unavailable (fetch AND cache read failed)");
            _xiv = xiv;
            _state = state;
            _crowd = null;
            RebuildAll();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[LFR] remote refresh ({reason}) failed: {ex.Message}");
        }
        finally
        {
            _fetchInFlight = false;
        }
    }

    public void Dispose()
    {
        Plugin.ClientState.Login -= OnLogin;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "FashionCheck", OnAddonPostSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreClose, "FashionCheck", OnAddonPreClose);
        _market.Dispose();
        _http.Dispose();
    }
}
