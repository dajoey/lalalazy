using ECommons.DalamudServices;
using LazyMarketCompanion.AutoMarket;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LazyMarketCompanion;

internal sealed class UniversalisPriceProvider : IDisposable
{
  private readonly Lumina.Excel.ExcelSheet<Item> _items;
  private readonly UniversalisClient _client;

  public UniversalisPriceProvider()
  {
    _items = Svc.Data.GetExcelSheet<Item>();
    _client = new UniversalisClient();
  }

  public bool CanResolveItem(string itemName, string rawItemName) => TryGetItem(itemName, rawItemName, out _, out _);

  /// <summary>Name-based lookup used by the pinch chain (the RetainerSell addon only gives us text).</summary>
  public Task<int> GetNewPrice(string itemName, string rawItemName, CancellationToken cancellationToken)
  {
    if (!TryGetItem(itemName, rawItemName, out var itemId, out var hqOnly))
    {
      Svc.Log.Warning($"[LMC] could not resolve item id for Universalis price check: {itemName}");
      return Task.FromResult(-1);
    }

    return GetNewPriceById(itemId, hqOnly, cancellationToken);
  }

  /// <summary>
  /// History-only lookup used by the in-game "Compare Prices" path when the board came back empty.
  /// Asks for no live listings at all - the caller has already established there are none.
  /// </summary>
  public async Task<int> GetSaleHistoryPrice(string itemName, string rawItemName, CancellationToken cancellationToken)
  {
    if (!TryGetItem(itemName, rawItemName, out var itemId, out var hqOnly))
    {
      Svc.Log.Warning($"[LMC] could not resolve item id for sale-history price check: {itemName}");
      return -1;
    }

    var dataCenterName = await ResolveDataCenter().ConfigureAwait(false);
    if (dataCenterName == null)
      return -1;

    UniversalisMarketDataResponse marketData;
    try
    {
      marketData = await _client.GetMarketData(itemId, dataCenterName, hqOnly, 0, EntryCount, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
      Svc.Log.Warning(ex, $"[LMC] sale-history lookup failed for item {itemId}");
      return -1;
    }

    return PriceFromSaleHistory(itemId, hqOnly, marketData, dataCenterName);
  }

  /// <summary>Id-based lookup used before a fresh auto-market listing.</summary>
  public async Task<int> GetNewPriceById(uint itemId, bool hq, CancellationToken cancellationToken)
  {
    var hqOnly = Plugin.Configuration.HQ && hq;
    var dataCenterName = await ResolveDataCenter().ConfigureAwait(false);
    if (dataCenterName == null)
      return -1;

    // Recent sales ride along in the SAME request when the fallback is on, so an empty board costs
    // one call, not two. With the fallback off the query is unchanged (entries=0).
    var wantHistory = Plugin.Configuration.UseUniversalisSaleHistoryFallback;
    var marketData = await _client.GetMarketData(
      itemId, dataCenterName, hqOnly, UniversalisClient.ListingCount, wantHistory ? EntryCount : 0, cancellationToken).ConfigureAwait(false);

    var listing = marketData.HasData
      ? marketData.Listings
          .Where(listing => listing.PricePerUnit > 0 && (!hqOnly || listing.Hq))
          .OrderBy(listing => listing.PricePerUnit)
          .FirstOrDefault()
      : null;

    if (listing != null)
    {
      var ownRetainer = ulong.TryParse(listing.RetainerId, out var retainerId)
                        && Plugin.Configuration.SeenRetainers.Contains(retainerId);
      Svc.Log.Debug($"[LMC] Universalis lowest data center price for {itemId}: {listing.PricePerUnit} on {listing.WorldName ?? dataCenterName}");
      return CalculateNewPrice(listing.PricePerUnit, ownRetainer);
    }

    // Nothing live to match. Either refuse exactly as before, or fall back to recent sales.
    return wantHistory ? PriceFromSaleHistory(itemId, hqOnly, marketData, dataCenterName) : -1;
  }

  /// <summary>How many recent sales to request, bounded so a stray config value cannot ask for thousands.</summary>
  private static int EntryCount => Math.Clamp(Plugin.Configuration.SaleHistoryEntryCount, 5, 100);

  private static async Task<string?> ResolveDataCenter()
  {
    var dataCenterName = await Svc.Framework.RunOnFrameworkThread(() =>
      Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ToString()).ConfigureAwait(false);

    if (!string.IsNullOrWhiteSpace(dataCenterName))
      return dataCenterName;

    Svc.Log.Warning("[LMC] could not resolve current data center for Universalis price check");
    return null;
  }

  /// <summary>
  /// The empty-board fallback: median of the recent data-centre sales inside the freshness window,
  /// or -1 (refuse) when there is no history or the newest sale is too old. The undercut/match rule is
  /// deliberately NOT applied - there is no competing listing to undercut, and the median already IS
  /// what the item has been clearing at. Per-item min/max limits still apply at the call site.
  /// </summary>
  private static int PriceFromSaleHistory(uint itemId, bool hqOnly, UniversalisMarketDataResponse marketData, string dataCenterName)
  {
    var entries = marketData.RecentHistory
      .Select(e => new SaleHistoryEntry(e.PricePerUnit, e.Timestamp, e.Hq))
      .ToList();

    var maxAgeDays = Plugin.Configuration.SaleHistoryMaxAgeDays;
    var result = SaleHistoryPricing.Evaluate(entries, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), maxAgeDays, hqOnly);

    switch (result.Outcome)
    {
      case SaleHistoryOutcome.Priced:
        Svc.Log.Information($"[LMC] {itemId}: board is empty on {dataCenterName}; pricing from the median of {result.SampleCount} sale(s) in the last {maxAgeDays} day(s): {result.UnitPrice} gil");
        return (int)Math.Clamp(result.UnitPrice, 1, int.MaxValue);

      case SaleHistoryOutcome.Stale:
        var ageDays = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - result.NewestUnixSeconds) / 86400;
        Svc.Log.Information($"[LMC] {itemId}: board is empty on {dataCenterName} and the newest sale is {ageDays} day(s) old (limit {maxAgeDays}); refusing to price from it");
        return -1;

      default:
        Svc.Log.Information($"[LMC] {itemId}: board is empty on {dataCenterName} and there is no sale history to price from");
        return -1;
    }
  }

  /// <summary>
  /// Quotes for the Auto-Market value gate and listing order (0.1.11.0): one request for every enabled
  /// Auto-Market item, with recent sales requested so the per-item sale-velocity fields are populated
  /// (an entries=0 query zeroes them - measured 2026-09-06). Same scope rule as
  /// <see cref="GetQuotes"/>: ask the board the pricing pass reads, i.e. the home world unless the user
  /// prices from the data centre. Returns null on ANY failure - the gate and the sort then leave every
  /// item alone, which is exactly the pre-0.1.11.0 behaviour.
  /// </summary>
  public async Task<Dictionary<uint, ItemQuote>?> GetRuleQuotes(IReadOnlyList<uint> itemIds, CancellationToken cancellationToken)
  {
    if (itemIds.Count == 0)
      return [];

    var useDataCenter = Plugin.Configuration.UseUniversalisDataCenterPrices;
    var scopeName = await Svc.Framework.RunOnFrameworkThread(() => useDataCenter
      ? Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ToString()
      : Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString()).ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(scopeName))
    {
      Svc.Log.Warning($"[LMC] could not resolve the current {(useDataCenter ? "data center" : "world")} for the Auto-Market gate");
      return null;
    }

    Svc.Log.Debug($"[LMC] Auto-Market gate asking Universalis about {itemIds.Count} item(s) on {scopeName}");
    try
    {
      var json = await _client.GetMarketDataJson(itemIds, scopeName, cancellationToken, listings: UniversalisClient.ListingCount, entries: 20).ConfigureAwait(false);
      return UniversalisQuotes.Parse(json, Plugin.Configuration.SeenRetainers);
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
      Svc.Log.Warning(ex, "[LMC] Auto-Market gate lookup failed; every item will list in list order");
      return null;
    }
  }

  /// <summary>
  /// One board snapshot for many items, for the Auto Pinch pre-flight (0.1.9.0). Unlike
  /// <see cref="GetNewPriceById"/> this does NOT decide a price - it hands the raw listings to
  /// <see cref="PinchPreflight"/>, which predicts what the pricing pass would do with them.
  /// Returns an empty map on any failure, and an empty map means every row gets walked.
  /// </summary>
  public async Task<Dictionary<uint, ItemQuote>> GetQuotes(IReadOnlyList<uint> itemIds, CancellationToken cancellationToken)
  {
    if (itemIds.Count == 0)
      return [];

    // SCOPE (0.1.10.0). Ask the SAME board the pricing pass reads, or the prediction is meaningless.
    // With UseUniversalisDataCenterPrices off (the default) the pass reads the in-game Compare Prices
    // window, which is the player's HOME WORLD. 0.1.9.0 asked the whole data centre regardless, so it
    // predicted a DC-wide lowest price that almost never equalled the world price on the listing and
    // walked nearly every row. Replayed over 80 live listings: asking the data centre skipped 17 rows,
    // asking the world skipped 66.
    var useDataCenter = Plugin.Configuration.UseUniversalisDataCenterPrices;
    var scopeName = await Svc.Framework.RunOnFrameworkThread(() => useDataCenter
      ? Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ToString()
      : Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString()).ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(scopeName))
    {
      Svc.Log.Warning($"[LMC] could not resolve the current {(useDataCenter ? "data center" : "world")} for the Auto Pinch pre-flight");
      return [];
    }

    Svc.Log.Debug($"[LMC] Auto Pinch pre-flight asking Universalis about {itemIds.Count} item(s) on {scopeName}");
    var json = await _client.GetMarketDataJson(itemIds, scopeName, cancellationToken).ConfigureAwait(false);
    return UniversalisQuotes.Parse(json, Plugin.Configuration.SeenRetainers);
  }

  public void Dispose()
  {
    _client.Dispose();
  }

  private bool TryGetItem(string itemName, string rawItemName, out uint itemId, out bool hqOnly)
  {
    var itemHq = itemName.Contains('\uE03C') || rawItemName.Contains('\uE03C');
    hqOnly = Plugin.Configuration.HQ && itemHq;
    return ItemNameResolver.TryGetItemId(itemName, rawItemName, out itemId);
  }

  /// <summary>
  /// The formula itself lives in <see cref="PriceMath"/> since 0.1.9.0, because the Auto Pinch pre-flight
  /// has to predict this exact number to decide a row is not worth walking. Two copies of it would drift,
  /// and a drifted prediction is a SKIPPED row that should have been re-priced - so there is only one.
  /// </summary>
  private static int CalculateNewPrice(long pricePerUnit, bool ownRetainer)
    => PriceMath.Candidate(
      pricePerUnit,
      ownRetainer,
      Plugin.Configuration.UndercutMode,
      Plugin.Configuration.UndercutAmount,
      Plugin.Configuration.UndercutSelf);
}
