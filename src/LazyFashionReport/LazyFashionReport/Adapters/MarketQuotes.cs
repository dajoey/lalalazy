using System.Net.Http;
using System.Text.Json;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Market-board medians for the buy leg (v0.3.0.0). Single-item Universalis v2 calls
/// (`listings=5&amp;entries=5` — the multi-item endpoint 504s, and `entries=0` silently zeroes
/// the averages), median computed from the entries array because `averagePrice` is
/// outlier-poisoned, staleness-guarded to 30 days because history freshness varies by four
/// orders of magnitude (all measured live, see the universalis reference). A quote with no
/// usable entries is simply absent — the UI then says "market board" without a number rather
/// than inventing one.
/// </summary>
internal sealed class MarketQuotes : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly Action<string> _log;
    private readonly object _lock = new();
    private readonly Dictionary<uint, uint> _medians = new();
    private readonly HashSet<uint> _fetched = new();

    public MarketQuotes(string cacheDir, string version, Action<string> log)
    {
        _log = log;
        _cachePath = Path.Combine(cacheDir, "market-medians.json");
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"LazyFashionReport/{version} (lalalazy; github.com/dajoey/lalalazy)");
        try
        {
            if (File.Exists(_cachePath))
            {
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, uint>>(File.ReadAllText(_cachePath)) ?? new Dictionary<string, uint>())
                    if (uint.TryParse(k, out var id)) _medians[id] = v;
            }
        }
        catch { /* cache is optional */ }
    }

    /// <summary>Median for an item when one was ever fetched (memory or disk); null otherwise.</summary>
    public uint? MedianFor(uint itemId)
    {
        lock (_lock) return _medians.TryGetValue(itemId, out var m) ? m : null;
    }

    /// <summary>Fetch medians for the given items (off any game thread). Errors degrade to
    /// "no quote", never throw.</summary>
    public async Task PrimeAsync(IReadOnlyCollection<uint> itemIds)
    {
        if (string.IsNullOrWhiteSpace(_world)) return;   // no live world read yet: no quote beats a wrong-DC quote
        foreach (var id in itemIds)
        {
            uint? median = null;
            try
            {
                lock (_lock) { if (_fetched.Contains(id)) continue; _fetched.Add(id); }
                using var resp = await _http.GetAsync($"https://universalis.app/api/v2/{Scope}/{id}?listings=5&entries=5");
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
                var root = doc.RootElement;
                // Empty board: listings empty + minPrice 0 — hasData true means "known item",
                // NOT "has a price". Only entries with a timestamp within 30 days count.
                if (!root.TryGetProperty("entries", out var entries) || entries.GetArrayLength() == 0) continue;
                var prices = new List<uint>();
                foreach (var e in entries.EnumerateArray())
                {
                    if (!e.TryGetProperty("pricePerUnit", out var ppu)) continue;
                    if (!e.TryGetProperty("timestamp", out var ts)) continue;
                    var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts.GetInt64();
                    if (age < 0 || age > 30L * 24 * 3600) continue;
                    prices.Add((uint)ppu.GetUInt32());
                }
                if (prices.Count == 0) continue;
                prices.Sort();
                median = prices[prices.Count / 2];
                lock (_lock) _medians[id] = median.Value;
            }
            catch (Exception ex)
            {
                _log($"[LFR] market quote for {id} failed: {ex.Message}");
            }
        }
        try
        {
            lock (_lock)
                File.WriteAllText(_cachePath, JsonSerializer.Serialize(_medians.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)));
        }
        catch { /* cache write is optional */ }
    }

    // World for the per-world scope; set from the service once the player is logged in.
    // Empty until then - PrimeAsync refuses to fetch rather than guess a datacenter.
    private string? _world;
    public void SetScope(string? world) => _world = string.IsNullOrWhiteSpace(world) ? null : world;
    private string Scope => _world!;

    public void Dispose() => _http.Dispose();
}
