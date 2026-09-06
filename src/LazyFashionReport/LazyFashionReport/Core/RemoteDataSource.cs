using System.Net.Http;
using System.Text.Json;

namespace LazyFashionReport.Core;

/// <summary>
/// Fetches + caches the two live datasets (both verified live 2026-09-06):
/// - xivstats.com/data/FashionReport.json : crowdsourced Categories{categoryRowId:{itemId:count}}
///   and WeeklyDyes{week:[{Id: slotCode, Dyes:{stainId:{Count,Pct}}}]}. Slot codes
///   1/34/35/37/36/38 = weapon/head/body/hands/legs/feet.
/// - fashionreportxiv.com/api/report-state : this week's theme + hints + EXACT plus2 dyes
///   + plus1 shade + easy100/easy80 sets. No auth. Fetched OFF the game thread; failures
///   degrade to xivstats-only.
///
/// Pure .NET (HttpClient + System.Text.Json) so the offline harness can drive it with a
/// fake transport; the plugin injects the real one.
/// </summary>
public sealed class RemoteDataSource
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly Action<string>? _log;

    public RemoteDataSource(HttpClient http, string cacheDir, Action<string>? log = null)
    {
        _http = http;
        _cacheDir = cacheDir;
        _log = log;
        Directory.CreateDirectory(cacheDir);
    }

    public sealed record XivStatsRoot
    {
        public Dictionary<string, Dictionary<string, int>> Categories { get; init; } = new();
        public Dictionary<string, List<XivStatsDyeSlot>> WeeklyDyes { get; init; } = new();
    }

    public sealed record XivStatsDyeSlot
    {
        public int Id { get; init; }
        public string? Name { get; init; }
        public Dictionary<string, XivStatsDyeCount>? Dyes { get; init; }
    }

    public sealed record XivStatsDyeCount
    {
        public int Count { get; init; }
        public double Pct { get; init; }
    }

    public sealed record ReportState
    {
        // fashionreportxiv.com/api/report-state shape (verified live 2026-09-06):
        // { lastOptions: { week, reportTitle, hints:[{hint, slot, ringNote}] },
        //   dyeData: { weapon/head/...: { plus1, plus2 }, _updatedAt },
        //   easy100: { itemPairs: [{slot, name}], dyes }, easy80: {...}, links }
        public LastOptions? LastOptions { get; init; }
        public Dictionary<string, DyeEntry>? DyeData { get; init; }
        public EasySet? Easy100 { get; init; }
        public EasySet? Easy80 { get; init; }
    }

    public sealed record LastOptions
    {
        public int Week { get; init; }
        public string? ReportTitle { get; init; }
        public List<HintEntry>? Hints { get; init; }
    }

    public sealed record EasySet
    {
        public List<ItemPair>? ItemPairs { get; init; }
    }

    public sealed record HintEntry
    {
        public string? Hint { get; init; }
        public string? Slot { get; init; }
    }

    public sealed record DyeEntry
    {
        public string? Plus1 { get; init; }
        public string? Plus2 { get; init; }
    }

    public sealed record ItemPair
    {
        public string? Slot { get; init; }
        public string? Name { get; init; }
    }

    // --- fetchers ---

    public async Task<XivStatsRoot?> FetchXivStatsAsync(CancellationToken ct)
    {
        return await GetJsonCachedAsync<XivStatsRoot>("xivstats.json", "https://xivstats.com/data/FashionReport.json", ct);
    }

    public async Task<ReportState?> FetchReportStateAsync(CancellationToken ct)
    {
        return await GetJsonCachedAsync<ReportState>("report-state.json", "https://fashionreportxiv.com/api/report-state", ct);
    }

    private async Task<T?> GetJsonCachedAsync<T>(string cacheFile, string url, CancellationToken ct)
    {
        var path = Path.Combine(_cacheDir, cacheFile);
        try
        {
            var bytes = await _http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
            _log?.Invoke($"fetched {url} ({bytes.Length} bytes)");
            return JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"fetch {url} failed: {ex.Message}; trying cache");
        }
        if (File.Exists(path))
        {
            try
            {
                await using var fs = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<T>(fs, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"cache read {path} failed: {ex.Message}");
            }
        }
        return default;
    }
}
