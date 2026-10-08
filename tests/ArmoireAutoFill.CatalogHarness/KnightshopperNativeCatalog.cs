using System.IO.Compression;
using System.Text.Json;

namespace ArmoireAutoFill.CatalogHarness;

// GROUND TRUTH for the Knightshopper import check, independent of Armoire's own model.
//
// Source of the data: Knightshopper 1.0.1.6's own native library (Knightshopper.Native.dll)
// run offline against a real game install (tools/KnightshopperGroundTruth/NativeDump calls
// the same exports the plugin calls: discover(sqpackPath, language), then the six result
// arrays). tests/.../fixtures/ks-native-catalog.json.gz is that output, trimmed to the fields
// the managed side uses.
//
// This class is a line-for-line port of what the plugin's MANAGED code does with those
// arrays (decompiled 1.0.1.6, class D19LanCAMcLAygBMAH.LoadVendorData and
// HmcZxNdvtzR1P.Aalhs91G9ayZRNV / w6DpuKjHno1oM9ySa / vDdU2e0dSLK3ObL0Uq4O):
//   * a vendor exists only if the native side gave it at least one location, and lives in
//     the dictionary of its own currency ("kind");
//   * a shop row (vendor, shopKey, shopId, currency) is registered only on a vendor that
//     exists in that currency's dictionary;
//   * a listing row (vendor, shopKey, item, currency, sub) becomes an item entry only when
//     the vendor's registered shops contain shopKey;
//   * import accepts (item, vendor, shop, sub) iff the item has an entry whose registered
//     shop has that vendor id and that shop id; for the sub-currency families the entry's
//     sub-currency must also equal the imported one unless the imported one is < 0.
// Nothing here reads Armoire's catalog, Level rows, ENpcBase links or any rule Armoire infers.
public sealed class KnightshopperNativeCatalog
{
    private const int Bicolor = 0;

    // Families whose import check also compares the sub-currency (everything but
    // Bicolor, Gil, MGP and Cosmocredits - Aalhs91G9ayZRNV).
    private static readonly HashSet<int> SubFamilies = [3, 5, 6, 7, 8, 10];

    private readonly record struct Shop(uint VendorId, uint ShopId);

    // currency -> vendorId -> shopKey -> shop
    private readonly Dictionary<int, Dictionary<uint, Dictionary<uint, Shop>>> _shopsByVendor = [];

    // currency -> itemId -> entries (shop, sub)
    private readonly Dictionary<int, Dictionary<uint, List<(Shop Shop, int Sub)>>> _items = [];

    public int VendorCount { get; private set; }
    public int ShopCount { get; private set; }
    public int ListingCount { get; private set; }
    public string Source { get; private set; } = "";

    public static KnightshopperNativeCatalog Load(string gzPath)
    {
        using var file = File.OpenRead(gzPath);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        var root = doc.RootElement;
        var cat = new KnightshopperNativeCatalog
        {
            Source = root.GetProperty("meta").GetProperty("source").GetString() ?? "",
        };

        foreach (var v in root.GetProperty("vendors").EnumerateArray())
        {
            var currency = v[1].GetInt32();
            cat.VendorsFor(currency)[v[0].GetUInt32()] = [];
            cat.VendorCount++;
        }

        foreach (var s in root.GetProperty("shops").EnumerateArray())
        {
            var vendor = s[0].GetUInt32();
            var currency = s[3].GetInt32();
            if (cat.VendorsFor(currency).TryGetValue(vendor, out var shops))
            {
                shops[s[1].GetUInt32()] = new Shop(vendor, s[2].GetUInt32());
                cat.ShopCount++;
            }
        }

        foreach (var l in root.GetProperty("listings").EnumerateArray())
        {
            var vendor = l[0].GetUInt32();
            var shopKey = l[1].GetUInt32();
            var item = l[2].GetUInt32();
            var currency = l[3].GetInt32();
            if (!cat.VendorsFor(currency).TryGetValue(vendor, out var shops) || !shops.TryGetValue(shopKey, out var shop))
                continue;
            if (!cat._items.TryGetValue(currency, out var byItem))
                cat._items[currency] = byItem = [];
            if (!byItem.TryGetValue(item, out var entries))
                byItem[item] = entries = [];
            // The managed side de-duplicates (shop key, sub) records; the check below is a
            // plain existence test so duplicates cannot change its answer.
            entries.Add((shop, l[4].GetInt32()));
            cat.ListingCount++;
        }

        return cat;
    }

    private Dictionary<uint, Dictionary<uint, Shop>> VendorsFor(int currency)
    {
        if (!_shopsByVendor.TryGetValue(currency, out var d))
            _shopsByVendor[currency] = d = [];
        return d;
    }

    // Knightshopper's import check for one item of an import code. Null = accepted;
    // otherwise the reason (its log line is "Item {id} is not available from its shared
    // {currency} vendor.").
    public string? Validate(int currency, uint item, uint vendor, uint shop, int sub)
    {
        if (!_items.TryGetValue(currency, out var byItem) || !byItem.TryGetValue(item, out var entries))
            return $"item {item}: not sold by any vendor in Knightshopper's {currency} catalog";
        var simple = !SubFamilies.Contains(currency);
        foreach (var (s, entrySub) in entries)
        {
            if (s.VendorId != vendor || s.ShopId != shop)
                continue;
            if (simple || sub < 0 || entrySub == sub)
                return null;
        }
        return $"item {item}: ({vendor}, {shop}, sub {sub}) is not one of its {entries.Select(e => e.Shop).Distinct().Count()} catalog vendor/shop pair(s)";
    }

    // Every (vendor, shop) pair Knightshopper accepts for an item in a currency.
    public IEnumerable<(uint Vendor, uint Shop, int Sub)> Pairs(int currency, uint item)
    {
        if (_items.TryGetValue(currency, out var byItem) && byItem.TryGetValue(item, out var entries))
            foreach (var (s, sub) in entries)
                yield return (s.VendorId, s.ShopId, sub);
    }

    public bool HasItem(int currency, uint item)
        => _items.TryGetValue(currency, out var byItem) && byItem.ContainsKey(item);
}
