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

    private RemoteDataSource.XivStatsRoot? _xiv;
    private RemoteDataSource.ReportState? _state;
    private CrowdDataAdapter? _crowd;
    private FashionWeek? _week;
    private OutfitReport? _outfit;
    private HashSet<uint>? _owned;
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
    }

    private static string CacheDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "pluginConfigs", "LazyFashionReportCache");

    public FashionWeek? Week => _week;
    public OutfitReport? Outfit => _outfit;
    public HashSet<uint>? OwnedItems => _owned;

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

    private unsafe (List<EquippedItem>? eq, HashSet<uint>? owned) ReadGame(CrowdDataAdapter? crowd)
    {
        var eq = ClientReader.ReadEquipped();
        HashSet<uint>? owned = null;
        if (_plugin.Config.FilterOwned)
        {
            var candidates = new HashSet<uint>();
            if (crowd != null && _week != null)
                foreach (var slot in Enum.GetValues<FashionSlot>())
                {
                    if (!_week.IsHinted(slot)) continue;
                    foreach (var c in crowd.CandidatesFor(_week, slot, null))
                        candidates.Add(c.ItemId);
                }
            owned = ClientReader.ReadOwnedItems(candidates.Count > 0 ? candidates : null);
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
        var (eq, owned) = Plugin.Framework.RunOnFrameworkThread(() => ReadGame(crowd)).Result;

        var eqArray = new EquippedItem?[11];
        foreach (var e in eq ?? Enumerable.Empty<EquippedItem>())
            if ((int)e.Slot < 11) eqArray[(int)e.Slot] = e;

        _owned = owned;
        if (crowd != null && owned is { Count: > 0 })
            Plugin.Framework.RunOnFrameworkThread(() => _sheets.WarmItemNames(owned, Plugin.Data)).Wait();

        _outfit = Predictor.Build(_week, eqArray, _sheets.StainFamilies, crowd,
            _plugin.Config.FilterOwned ? _owned : null);
    }

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
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "FashionCheck", OnAddonPostSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreClose, "FashionCheck", OnAddonPreClose);
        _http.Dispose();
    }
}
