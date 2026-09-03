using System.Net;
using System.Net.Http;
using System.Text.Json;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.Adapters;

/// <summary>
/// <see cref="IPriceSource"/> over Universalis v2 (Plan §Phase 3 task 3). Grew out of Dagobert's
/// <c>UniversalisClient.cs</c> (shared <c>HttpClient</c>, versioned User-Agent) and adds everything the
/// scope asked for:
/// <list type="bullet">
/// <item>batched <c>GET /api/v2/aggregated/{dc}/{≤100 ids}</c> for min/median listing, average sale price
///   and daily sale velocity (NQ and HQ, at DC or world scope);</item>
/// <item>a second, field-projected <c>GET /api/v2/{dc}/{ids}?fields=…</c> per batch for <c>listingsCount</c>
///   (the aggregated endpoint has no listing count and the saturation column needs it);</item>
/// <item><c>GET /api/v2/marketable</c> cached for the session, <c>GET /api/v2/tax-rates?world=</c> cached
///   for the session;</item>
/// <item>in-memory + on-disk cache (<c>{configDir}/prices.json</c>) with a 10-minute TTL;</item>
/// <item>at most 4 requests in flight, exponential backoff on 429 / 5xx, gzip.</item>
/// </list>
/// <see cref="Get"/> is synchronous and only ever reads the cache; <see cref="PrimeAsync"/> fetches what is
/// missing or stale for the set the caller is about to show (visible/filtered rows + cart), never the
/// whole marketable list. A missing <c>dailySaleVelocity</c> maps to <c>0</c>, never NaN (V1 contract).
/// </summary>
public sealed class UniversalisClient : IPriceSource, IDisposable
{
    public const int BatchSize = 100;
    private const int MaxConcurrent = 4;
    private const int MaxAttempts = 4;

    private static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = false };

    private readonly HttpClient _http;
    private readonly Action<string> _log;      // warnings/debug lines; the plugin routes them to IPluginLog, the probe to the console
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly object _lock = new();
    private readonly Dictionary<uint, CachedQuote> _cache = new();
    private readonly HashSet<uint> _inFlight = new();

    private HashSet<uint>? _marketable;
    private Dictionary<string, int>? _taxRates;
    private string? _taxWorld;
    private DateTime _lastDiskWrite = DateTime.MinValue;
    private bool _dirty;

    /// <summary>How long a quote is served from cache before <see cref="PrimeAsync"/> refetches it.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Universalis scope for the quotes: a data centre name (default) or a world name.</summary>
    public string Scope { get; set; } = "";

    /// <summary>True when <see cref="Scope"/> is a world rather than a DC (selects the <c>world</c> block of the aggregate).</summary>
    public bool ScopeIsWorld { get; set; }

    public int CacheSize { get { lock (_lock) return _cache.Count; } }
    public int MarketableCount => _marketable?.Count ?? 0;
    public IReadOnlyDictionary<string, int>? TaxRates => _taxRates;
    public DateTime? LastFetch { get; private set; }
    public int RequestsMade { get; private set; }
    public int Failures { get; private set; }

    public UniversalisClient(string configDirectory, string version, Action<string> log)
    {
        _log = log;
        _cachePath = Path.Combine(configDirectory, "prices.json");
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        })
        {
            BaseAddress = new Uri("https://universalis.app/api/v2/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"LazyCrafter/{version} (lalalazy; github.com/dajoey/lalalazy)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        LoadDisk();
    }

    // ---------------------------------------------------------------- IPriceSource

    public PriceQuote? Get(uint itemId)
    {
        lock (_lock) return _cache.TryGetValue(itemId, out var c) ? c.Quote : null;
    }

    /// <summary>True when the quote is absent or older than <see cref="Ttl"/>.</summary>
    public bool IsStale(uint itemId)
    {
        lock (_lock)
            return !_cache.TryGetValue(itemId, out var c) || DateTime.UtcNow - c.FetchedAt > Ttl;
    }

    // ---------------------------------------------------------------- fetching

    /// <summary>
    /// Fetch quotes for every marketable item in <paramref name="itemIds"/> that is missing or stale.
    /// Returns the number of items refreshed. Safe to call concurrently; overlapping ids are only fetched once.
    /// </summary>
    public async Task<int> PrimeAsync(IEnumerable<uint> itemIds, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Scope)) return 0;
        var marketable = await MarketableAsync(ct).ConfigureAwait(false);

        List<uint> wanted;
        lock (_lock)
        {
            wanted = itemIds.Distinct()
                .Where(id => marketable.Contains(id) && !_inFlight.Contains(id) && IsStaleUnlocked(id))
                .ToList();
            foreach (var id in wanted) _inFlight.Add(id);
        }
        if (wanted.Count == 0) return 0;

        try
        {
            var batches = wanted.Chunk(BatchSize).Select(b => FetchBatchAsync(b, ct));
            var counts = await Task.WhenAll(batches).ConfigureAwait(false);
            var total = counts.Sum();
            if (total > 0) FlushDisk(force: false);
            return total;
        }
        finally
        {
            lock (_lock) foreach (var id in wanted) _inFlight.Remove(id);
        }
    }

    private bool IsStaleUnlocked(uint id) =>
        !_cache.TryGetValue(id, out var c) || DateTime.UtcNow - c.FetchedAt > Ttl;

    private async Task<int> FetchBatchAsync(uint[] ids, CancellationToken ct)
    {
        var idList = string.Join(',', ids);
        var scope = Uri.EscapeDataString(Scope);

        using var agg = await GetJsonAsync($"aggregated/{scope}/{idList}", ct).ConfigureAwait(false);
        if (agg is null) return 0;

        // listingsCount lives only on the current-data endpoint; project just what we need.
        using var cur = await GetJsonAsync(
            $"{scope}/{idList}?entries=0&fields=items.itemID,items.listingsCount,items.unitsForSale,items.lastUploadTime,unresolvedItems",
            ct).ConfigureAwait(false);

        var listings = new Dictionary<uint, (int Count, long Upload)>();
        if (cur is not null && cur.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in items.EnumerateObject())
            {
                if (!uint.TryParse(p.Name, out var id)) continue;
                var v = p.Value;
                listings[id] = (
                    v.TryGetProperty("listingsCount", out var lc) && lc.TryGetInt32(out var n) ? n : 0,
                    v.TryGetProperty("lastUploadTime", out var lu) && lu.TryGetInt64(out var t) ? t : 0);
            }
        }

        var now = DateTime.UtcNow;
        var parsed = new List<PriceQuote>();
        if (agg.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in results.EnumerateArray())
            {
                if (!r.TryGetProperty("itemId", out var idEl) || !idEl.TryGetUInt32(out var itemId)) continue;
                var (lc, upload) = listings.TryGetValue(itemId, out var l) ? l : (0, 0L);
                parsed.Add(Parse(itemId, r, lc, upload));
            }
        }

        // Items Universalis knows nothing about still get a (empty) quote so we stop asking for them until the TTL lapses.
        var seen = parsed.Select(q => q.ItemId).ToHashSet();
        foreach (var id in ids.Where(id => !seen.Contains(id)))
            parsed.Add(new PriceQuote(id, null, null, null, null, null, null, 0, 0, 0, null));

        lock (_lock)
        {
            foreach (var q in parsed) _cache[q.ItemId] = new CachedQuote(q, now);
            _dirty = true;
        }
        LastFetch = now;
        return parsed.Count;
    }

    private PriceQuote Parse(uint itemId, JsonElement r, int listingsCount, long lastUploadMs)
    {
        var block = ScopeIsWorld ? "world" : "dc";
        var nq = r.TryGetProperty("nq", out var n) ? n : default;
        var hq = r.TryGetProperty("hq", out var h) ? h : default;

        static long? Price(JsonElement side, string field, string block)
        {
            if (side.ValueKind != JsonValueKind.Object) return null;
            if (!side.TryGetProperty(field, out var f) || f.ValueKind != JsonValueKind.Object) return null;
            if (!f.TryGetProperty(block, out var b) || b.ValueKind != JsonValueKind.Object) return null;
            if (!b.TryGetProperty("price", out var p)) return null;
            return p.ValueKind == JsonValueKind.Number ? (long?)Math.Round(p.GetDouble()) : null;
        }

        static double Velocity(JsonElement side, string block)
        {
            if (side.ValueKind != JsonValueKind.Object) return 0;
            if (!side.TryGetProperty("dailySaleVelocity", out var f) || f.ValueKind != JsonValueKind.Object) return 0;
            if (!f.TryGetProperty(block, out var b) || b.ValueKind != JsonValueKind.Object) return 0;
            if (!b.TryGetProperty("quantity", out var q) || q.ValueKind != JsonValueKind.Number) return 0;
            var v = q.GetDouble();
            return double.IsFinite(v) && v > 0 ? v : 0;   // V1 contract: missing/garbage velocity is 0, never NaN
        }

        return new PriceQuote(
            itemId,
            MinListingNq: Price(nq, "minListing", block),
            MinListingHq: Price(hq, "minListing", block),
            MedianNq: Price(nq, "medianListing", block),
            MedianHq: Price(hq, "medianListing", block),
            AvgSaleNq: Price(nq, "averageSalePrice", block),
            AvgSaleHq: Price(hq, "averageSalePrice", block),
            VelocityNq: Velocity(nq, block),
            VelocityHq: Velocity(hq, block),
            ListingsCount: listingsCount,
            LastUpload: lastUploadMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastUploadMs) : null);
    }

    /// <summary>The marketable item-id set, fetched once per session.</summary>
    public async Task<HashSet<uint>> MarketableAsync(CancellationToken ct = default)
    {
        if (_marketable is { Count: > 0 } m) return m;
        using var doc = await GetJsonAsync("marketable", ct).ConfigureAwait(false);
        var set = new HashSet<uint>();
        if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetUInt32(out var id)) set.Add(id);
        if (set.Count > 0) _marketable = set;
        return _marketable ?? set;
    }

    /// <summary>Synchronous view of the marketable set; <c>null</c> until <see cref="MarketableAsync"/> has run.</summary>
    public bool? IsMarketable(uint itemId) => _marketable?.Contains(itemId);

    /// <summary>Per-city retainer tax percentages for <paramref name="world"/>, fetched once per session per world.</summary>
    public async Task<IReadOnlyDictionary<string, int>> TaxRatesAsync(string world, CancellationToken ct = default)
    {
        if (_taxRates is not null && _taxWorld == world) return _taxRates;
        using var doc = await GetJsonAsync($"tax-rates?world={Uri.EscapeDataString(world)}", ct).ConfigureAwait(false);
        var d = new Dictionary<string, int>();
        if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.TryGetInt32(out var pct)) d[p.Name] = pct;
        if (d.Count > 0) { _taxRates = d; _taxWorld = world; }
        return _taxRates ?? d;
    }

    /// <summary>Lowest city tax known for the tax world (what a sensible seller pays); 5 when unknown.</summary>
    public double BestTaxPct => _taxRates is { Count: > 0 } t ? t.Values.Min() : 5;

    private async Task<JsonDocument?> GetJsonAsync(string relative, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var delay = TimeSpan.FromSeconds(1);
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    RequestsMade++;
                    using var resp = await _http.GetAsync(relative, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                    }

                    var code = (int)resp.StatusCode;
                    if (code == 404) return null;                       // nothing to retry
                    if (code != 429 && code < 500) { Failures++; _log($"Universalis {relative} -> {code}"); return null; }
                    if (resp.Headers.RetryAfter?.Delta is { } ra && ra > delay) delay = ra;
                    _log($"Universalis {relative} -> {code}, retry {attempt}/{MaxAttempts} in {delay.TotalSeconds}s");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _log($"Universalis {relative} attempt {attempt} failed: {ex.Message}");
                }

                if (attempt == MaxAttempts) break;
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
            }
            Failures++;
            _log($"Universalis {relative} gave up after {MaxAttempts} attempts");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- disk cache

    private sealed record CachedQuote(PriceQuote Quote, DateTime FetchedAt);
    private sealed record DiskEntry(PriceQuote Quote, DateTime FetchedAt);
    private sealed record DiskFile(int Version, string Scope, List<DiskEntry> Entries);

    private void LoadDisk()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var file = JsonSerializer.Deserialize<DiskFile>(File.ReadAllText(_cachePath), CacheJson);
            if (file is null || file.Version != 1) return;
            var cutoff = DateTime.UtcNow - Ttl;
            lock (_lock)
            {
                foreach (var e in file.Entries)
                    if (e.FetchedAt > cutoff && e.Quote is not null)
                        _cache[e.Quote.ItemId] = new CachedQuote(e.Quote, e.FetchedAt);
            }
            if (!string.IsNullOrEmpty(file.Scope) && string.IsNullOrEmpty(Scope)) Scope = file.Scope;
            _log($"Universalis disk cache: {CacheSize} fresh quotes loaded from {_cachePath}");
        }
        catch (Exception ex)
        {
            _log($"Universalis disk cache unreadable; starting empty: {ex.Message}");
        }
    }

    /// <summary>Write the in-memory cache to disk (at most once a minute unless <paramref name="force"/>).</summary>
    public void FlushDisk(bool force)
    {
        List<DiskEntry> entries;
        lock (_lock)
        {
            if (!_dirty) return;
            if (!force && DateTime.UtcNow - _lastDiskWrite < TimeSpan.FromMinutes(1)) return;
            var cutoff = DateTime.UtcNow - Ttl;
            entries = _cache.Values.Where(c => c.FetchedAt > cutoff).Select(c => new DiskEntry(c.Quote, c.FetchedAt)).ToList();
            _dirty = false;
            _lastDiskWrite = DateTime.UtcNow;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var tmp = _cachePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new DiskFile(1, Scope, entries), CacheJson));
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log($"Universalis disk cache write failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        FlushDisk(force: true);
        _http.Dispose();
        _gate.Dispose();
    }
}
