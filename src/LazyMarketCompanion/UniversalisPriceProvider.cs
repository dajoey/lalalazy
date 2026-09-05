using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System;
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

  /// <summary>Id-based lookup used before a fresh auto-market listing.</summary>
  public async Task<int> GetNewPriceById(uint itemId, bool hq, CancellationToken cancellationToken)
  {
    var hqOnly = Plugin.Configuration.HQ && hq;
    var dataCenterName = await Svc.Framework.RunOnFrameworkThread(() =>
      Svc.Objects.LocalPlayer?.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ToString()).ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(dataCenterName))
    {
      Svc.Log.Warning("[LMC] could not resolve current data center for Universalis price check");
      return -1;
    }

    var marketData = await _client.GetMarketData(itemId, dataCenterName, hqOnly, cancellationToken).ConfigureAwait(false);
    if (!marketData.HasData || marketData.Listings.Count == 0)
      return -1;

    var listing = marketData.Listings
      .Where(listing => listing.PricePerUnit > 0 && (!hqOnly || listing.Hq))
      .OrderBy(listing => listing.PricePerUnit)
      .FirstOrDefault();

    if (listing == null)
      return -1;

    var ownRetainer = ulong.TryParse(listing.RetainerId, out var retainerId)
                      && Plugin.Configuration.SeenRetainers.Contains(retainerId);
    Svc.Log.Debug($"[LMC] Universalis lowest data center price for {itemId}: {listing.PricePerUnit} on {listing.WorldName ?? dataCenterName}");
    return CalculateNewPrice(listing.PricePerUnit, ownRetainer);
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

  private static int CalculateNewPrice(long pricePerUnit, bool ownRetainer)
  {
    var price = (int)Math.Min(pricePerUnit, int.MaxValue);

    if (!Plugin.Configuration.UndercutSelf && ownRetainer)
      return price;

    if (Plugin.Configuration.UndercutMode == UndercutMode.FixedAmount)
      return Math.Max(price - Plugin.Configuration.UndercutAmount, 1);

    return (int)Math.Max((100L - Plugin.Configuration.UndercutAmount) * price / 100L, 1);
  }
}
