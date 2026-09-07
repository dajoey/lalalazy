using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LazyFashionReport.Core;

/// <summary>
/// Fetches + caches the two live datasets (both verified live 2026-09-06):
/// - xivstats.com/data/FashionReport.json : crowdsourced Categories{categoryRowId:{itemId:count}}
///   and WeeklyDyes{week:[{Id: slotCode, Dyes:{stainId:{Count,Pct}}}]}.
/// - fashionreportxiv.com/api/report-state : this week's theme + hints + EXACT plus2 dyes
///   + plus1 shade + easy100/easy80 sets. No auth. Fetched OFF the game thread; failures
///   degrade to xivstats-only.
///
/// JSON binding (fixed 2026-09-06, v0.1.1.0 — the "no hint on every slot" bug): the frxiv
/// payload is camelCase, carries "week" as a STRING, and parks numeric "_updatedAt" entries
/// inside the dyeData map. System.Text.Json's defaults (case-sensitive property names,
/// strict numeric typing) turned all of that into an all-null ReportState while the fetch
/// itself logged success. Binding is now case-insensitive, accepts quoted numbers, and a
/// custom converter skips non-object entries in Dictionary&lt;string, DyeEntry&gt;. The parse
/// entry points are public static so the offline harness can replay the REAL cached payload
/// bytes through this exact path (tests/LazyFashionReport.Harness/report-state-week449.json).
///
/// Pure .NET (HttpClient + System.Text.Json) so the offline harness can drive it with a
/// fake transport; the plugin injects the real one.
/// </summary>
public sealed class RemoteDataSource
{
    /// <summary>
    /// Shared binding options. CamelCase payload, case-insensitive property match (the frxiv
    /// JSON is lowercase while these records are PascalCase), and quoted numbers allowed
    /// ("week": "449"). Dictionary keys are never case-mapped by STJ, so the slot-code keys
    /// ("weapon", "35", ...) pass through verbatim.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new SkipNonDyeEntryDictionaryConverter() },
    };

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
        // { lastOptions: { week (STRING), reportTitle, hints:[{hint, slot, ringNote}] },
        //   dyeData: { weapon/head/...: {plus1, plus2}, _updatedAt: <number> },
        //   easy100: { itemPairs: [{slot, name}], dyes }, easy80: {...}, links }
        public LastOptions? LastOptions { get; init; }
        public Dictionary<string, DyeEntry>? DyeData { get; init; }
        public EasySet? Easy100 { get; init; }
        public EasySet? Easy80 { get; init; }
    }

    public sealed record LastOptions
    {
        public int Week { get; init; }        // arrives as a JSON string ("449") — AllowReadingFromString
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

    // --- parse entry points (public for the offline harness) ---

    public static XivStatsRoot? ParseXivStats(byte[] bytes) =>
        JsonSerializer.Deserialize<XivStatsRoot>(bytes, JsonOptions);

    public static ReportState? ParseReportState(byte[] bytes) =>
        JsonSerializer.Deserialize<ReportState>(bytes, JsonOptions);

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
            return await ParseAsync<T>(bytes);
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
                return await ParseStreamAsync<T>(fs);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"cache read {path} failed: {ex.Message}");
            }
        }
        return default;
    }

    private static Task<T?> ParseAsync<T>(byte[] bytes) =>
        typeof(T) == typeof(ReportState) ? Task.FromResult((T?)(object?)ParseReportState(bytes))
        : typeof(T) == typeof(XivStatsRoot) ? Task.FromResult((T?)(object?)ParseXivStats(bytes))
        : ParseStreamAsync<T>(new MemoryStream(bytes));

    private static async Task<T?> ParseStreamAsync<T>(Stream stream) =>
        await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
}

/// <summary>
/// Dictionary&lt;string, DyeEntry&gt; binding that skips keys whose value is not a JSON object:
/// frxiv parks "_updatedAt": 1788513962430 (a bare number) inside the dyeData map, and the
/// default converter throws on it, killing the whole payload. Skipped, not rejected.
/// </summary>
public sealed class SkipNonDyeEntryDictionaryConverter : JsonConverter<Dictionary<string, RemoteDataSource.DyeEntry>>
{
    public override Dictionary<string, RemoteDataSource.DyeEntry>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        var result = new Dictionary<string, RemoteDataSource.DyeEntry>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString() ?? string.Empty;
            if (!reader.Read())
                break;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                result[key] = JsonSerializer.Deserialize<RemoteDataSource.DyeEntry>(ref reader, options)
                              ?? new RemoteDataSource.DyeEntry();
            }
            else
            {
                reader.Skip(); // "_updatedAt" and any future non-dye metadata key
            }
        }
        return result;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, RemoteDataSource.DyeEntry> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
