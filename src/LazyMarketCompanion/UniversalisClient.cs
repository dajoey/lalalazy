using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LazyMarketCompanion;

internal sealed class UniversalisClient : IDisposable
{
  public const int ListingCount = 10;
  /// <summary>Listings per item on the multi-item pre-flight request - deep enough that the cheapest of the wanted quality is in there.</summary>
  private const int PreflightListingCount = 20;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  private readonly HttpClient _client;

  public UniversalisClient()
  {
    _client = new HttpClient
    {
      BaseAddress = new Uri("https://universalis.app/api/v2/"),
      Timeout = TimeSpan.FromSeconds(8),
    };

    _client.DefaultRequestHeaders.UserAgent.ParseAdd($"LazyMarketCompanion/{Plugin.PluginInterface.Manifest.AssemblyVersion} (github.com/dajoey/lalalazy)");
  }

  /// <summary>
  /// One Universalis call. <paramref name="listings"/> is how many LIVE listings to ask for and
  /// <paramref name="entries"/> how many recent SALES; the plugin asked for entries=0 from 0.1.0.0
  /// through 0.1.7.0, which is why averagePrice/saleVelocity always came back 0 (measured 2026-09-06:
  /// item 41878 reports averagePrice 0 with entries=0 and 89,940 with entries=5). Sales are requested
  /// only when the empty-board fallback is switched on, so the default request is byte-for-byte what
  /// it has always been.
  /// </summary>
  public async Task<UniversalisMarketDataResponse> GetMarketData(uint itemId, string worldDcRegion, bool hqOnly, int listings, int entries, CancellationToken cancellationToken)
  {
    var query = $"?listings={Math.Max(listings, 0)}&entries={Math.Max(entries, 0)}" + (hqOnly ? "&hq=true" : string.Empty);
    var requestUri = new Uri($"{Uri.EscapeDataString(worldDcRegion)}/{itemId}{query}", UriKind.Relative);

    using var stream = await _client.GetStreamAsync(requestUri, cancellationToken).ConfigureAwait(false);
    return await JsonSerializer.DeserializeAsync<UniversalisMarketDataResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
      ?? throw new InvalidOperationException($"Failed to parse Universalis market data for item {itemId} on {worldDcRegion}.");
  }

  /// <summary>
  /// Board data for MANY items in one request - what the Auto Pinch pre-flight runs on.
  ///
  /// Returns the body verbatim rather than a typed object on purpose: with several ids Universalis answers
  /// {"itemIDs":[..],"items":{"&lt;id&gt;":{..}}}, but with exactly ONE id it answers the flat single-item
  /// object with no "items" key at all. <see cref="AutoMarket.UniversalisQuotes.Parse"/> handles both;
  /// a typed model for one of the two shapes would silently return nothing for the other, which on a
  /// one-listing retainer means the pre-flight quietly does nothing.
  ///
  /// No hq=true here either: one request covers a mix of HQ and NQ listings, and the quality is chosen
  /// per row afterwards.
  /// </summary>
  public async Task<string> GetMarketDataJson(IReadOnlyList<uint> itemIds, string worldDcRegion, CancellationToken cancellationToken, int listings = -1, int entries = -1)
  {
    var ids = string.Join(",", itemIds);
    // listings/entries -1 = the pre-flight defaults, so its request stays byte-for-byte what it has
    // always been. The Auto-Market gate (0.1.11.0) passes entries > 0 because Universalis computes the
    // per-item sale-velocity fields from the recent-sales array and returns zeroes for them at entries=0.
    var listingCount = listings >= 0 ? listings : PreflightListingCount;
    var requestUri = new Uri($"{Uri.EscapeDataString(worldDcRegion)}/{ids}?listings={listingCount}&entries={Math.Max(entries, 0)}", UriKind.Relative);
    return await _client.GetStringAsync(requestUri, cancellationToken).ConfigureAwait(false);
  }

  public void Dispose()
  {
    _client.Dispose();
  }
}

internal sealed class UniversalisMarketDataResponse
{
  [JsonPropertyName("listings")]
  public List<UniversalisMarketDataListing> Listings { get; set; } = [];

  /// <summary>
  /// Recent SALES, newest first. Empty unless the request asked for entries &gt; 0.
  /// </summary>
  [JsonPropertyName("recentHistory")]
  public List<UniversalisSaleEntry> RecentHistory { get; set; } = [];

  [JsonPropertyName("hasData")]
  public bool HasData { get; set; }
}

/// <summary>One completed sale from <c>recentHistory</c>.</summary>
internal sealed class UniversalisSaleEntry
{
  [JsonPropertyName("hq")]
  public bool Hq { get; set; }

  [JsonPropertyName("pricePerUnit")]
  public long PricePerUnit { get; set; }

  /// <summary>Seconds since the epoch.</summary>
  [JsonPropertyName("timestamp")]
  public long Timestamp { get; set; }

  [JsonPropertyName("worldName")]
  public string? WorldName { get; set; }
}

internal sealed class UniversalisMarketDataListing
{
  [JsonPropertyName("hq")]
  public bool Hq { get; set; }

  [JsonPropertyName("pricePerUnit")]
  public long PricePerUnit { get; set; }

  [JsonPropertyName("retainerID")]
  public string? RetainerId { get; set; }

  [JsonPropertyName("worldName")]
  public string? WorldName { get; set; }
}
