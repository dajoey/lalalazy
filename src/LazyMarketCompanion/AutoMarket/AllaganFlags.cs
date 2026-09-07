using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// One row of AllaganMarket's MarketPriceCache.csv, the on-disk form of the price data its red/yellow
/// overlay is computed from. Field order in the CSV: itemId, isHq (Y/N), worldId, type, lastUpdated
/// ("MM/dd/yyyy HH:mm:ss"), unitCost, ownPrice (Y/N).
/// </summary>
public sealed record AllaganCacheEntry(uint ItemId, bool Hq, uint WorldId, DateTime LastUpdated, long UnitCost, bool OwnPrice);

/// <summary>
/// AllaganMarket's per-item settings, read from AllaganMarket.json (IntegerSettings / EnumSettings).
/// Defaults are AllaganMarket's own shipped defaults, used when a key is absent.
/// </summary>
public sealed record AllaganSettings(int ItemUpdatePeriodMinutes, int UndercutBy, string UndercutComparison)
{
  public static AllaganSettings Defaults { get; } = new(300, 0, "MatchingQuality");
}

/// <summary>
/// The parsed verdict set for one world: which (item, quality) listings AllaganMarket currently flags
/// as undercut (its RED) and which it flags as stale (its YELLOW). Everything else is UNFLAGGED, and
/// under the flags-only rule an unflagged row is never walked.
///
/// AllaganMarket exposes no IPC, so its verdicts are read from these files. That is file coupling: a
/// format change in AllaganMarket breaks this silently, and the changelog says so.
/// </summary>
public sealed class AllaganFlagSet
{
  private readonly uint _worldId;
  private readonly AllaganSettings _settings;
  private readonly Dictionary<(uint ItemId, bool Hq), AllaganCacheEntry> _entries = new();

  public AllaganFlagSet(uint worldId, AllaganSettings settings)
  {
    _worldId = worldId;
    _settings = settings;
  }

  public int EntryCount => _entries.Count;

  /// <summary>
  /// Quality AllaganMarket's cache LOOKUP uses for a listing of quality <paramref name="listingHq"/>:
  /// MatchingQuality keeps the listing's quality, NqOnly always reads NQ, HqOnly always reads HQ.
  /// Storage (Add) always uses the row's own quality - the comparison setting is a lookup decision,
  /// exactly as it is in AllaganMarket's GetMarketPriceCache.
  /// </summary>
  private bool LookupQuality(bool listingHq)
    => _settings.UndercutComparison switch
    {
      "NqOnly" => false,
      "HqOnly" => true,
      _ => listingHq,
    };

  /// <summary>
  /// Records one cache row. Rows for OTHER worlds are ignored: every lookup AllaganMarket makes is
  /// world-scoped (its overlay, websocket and REST reads all key on the player's home world), so a
  /// price cached on another world must never answer for this one. The newest row per (item, quality)
  /// wins - AllaganMarket keeps one entry per key and replaces it when a newer row arrives.
  /// </summary>
  public void Add(AllaganCacheEntry entry)
  {
    if (entry.WorldId != _worldId || entry.ItemId == 0)
      return;

    var key = (entry.ItemId, entry.Hq);
    if (!_entries.TryGetValue(key, out var existing) || entry.LastUpdated > existing.LastUpdated)
      _entries[key] = entry;
  }

  /// <summary>
  /// AllaganMarket's RED (UndercutService.IsItemUndercut): the recommended unit price for this listing
  /// - the cached cheapest (item, lookup-quality) row's unit cost, minus UndercutBy, floored at 1, with
  /// an own-price entry recommending the listing's own price - is strictly BELOW the price the listing
  /// carries. The current price passed in is the same container reading AllaganMarket compares against,
  /// so an own-lowest listing is never flagged and a stranger's cheaper listing always is.
  /// </summary>
  public bool IsUndercut(uint itemId, bool listingHq, long currentPrice)
  {
    if (itemId == 0 || currentPrice <= 0)
      return false;
    if (!_entries.TryGetValue((itemId, LookupQuality(listingHq)), out var entry))
      return false;

    // AllaganMarket returns the cached price UNCHANGED when it is one of the player's own (OwnPrice),
    // so such a recommendation is never below the listing and never red. The subtraction below only
    // happens on the not-own branch; an own-price entry compares its own price against itself.
    var recommended = entry.OwnPrice
      ? entry.UnitCost
      : Math.Max(1, entry.UnitCost - Math.Max(_settings.UndercutBy, 0));
    return recommended < currentPrice;
  }

  /// <summary>
  /// AllaganMarket's YELLOW (UndercutService.NeedsUpdate): the newest cache entry for the item across
  /// BOTH qualities is older than the ItemUpdatePeriod.
  ///
  /// An item with NO cache rows at all is NOT flagged. AllaganMarket's NeedsUpdate returns true for
  /// such an item, but on the live client that state does not survive first sight: the moment a
  /// listing is added, AllaganMarket writes a fresh own-price cache row for it
  /// (LocalRetainerMarketItemAdded), so a no-row item is one AllaganMarket has never had any opinion
  /// about - it renders unmarked in the overlay, and Joey's spec is that unmarked rows are never
  /// walked. The nine no-op rows of the 2026-09-06 21:22 pass are exactly this shape once their
  /// own-price rows exist; items with genuinely no rows are the never-checked ones the card pins as
  /// never-walked.
  /// </summary>
  public bool IsStale(uint itemId, bool listingHq, DateTime now)
  {
    if (itemId == 0)
      return false;

    var hasAny = false;
    var newest = DateTime.MinValue;
    foreach (var (key, entry) in _entries)
    {
      if (key.ItemId != itemId)
        continue;
      hasAny = true;
      if (entry.LastUpdated > newest)
        newest = entry.LastUpdated;
    }

    if (!hasAny)
      return false;
    return now - newest > TimeSpan.FromMinutes(Math.Max(_settings.ItemUpdatePeriodMinutes, 1));
  }

  /// <summary>Whether the item has a cache row for the lookup quality on this world. Diagnostic.</summary>
  public bool HasEntry(uint itemId, bool listingHq)
    => itemId != 0 && _entries.ContainsKey((itemId, LookupQuality(listingHq)));
}

/// <summary>
/// Parses AllaganMarket's on-disk state into an <see cref="AllaganFlagSet"/>.
///
/// SOURCES (no IPC exists - file coupling is the only way to read AllaganMarket's verdicts):
///   - MarketPriceCache.csv: one line per (item, quality, world): itemId,isHq,worldId,type,lastUpdated,unitCost,ownPrice.
///     Dates are AllaganMarket's own "MM/dd/yyyy HH:mm:ss" (invariant culture). A line that cannot be
///     parsed is SKIPPED, never thrown - a truncated or future-format file reads as fewer entries, and
///     an empty set simply unflags everything.
///   - AllaganMarket.json: IntegerSettings.ItemUpdatePeriod (minutes, default 300) and .UndercutBy
///     (default 0); EnumSettings.UndercutComparison.Value (default MatchingQuality). Missing keys fall
///     back to the default rather than failing; broken JSON reads as the defaults.
/// </summary>
public static class AllaganMarketFlags
{
  public const string CacheFileName = "MarketPriceCache.csv";
  public const string SettingsFileName = "AllaganMarket.json";
  private static readonly string[] DateFormats =
  [
    "MM/dd/yyyy HH:mm:ss", "M/dd/yyyy HH:mm:ss", "MM/dd/yyyy H:mm:ss", "M/dd/yyyy H:mm:ss",
  ];

  public static AllaganFlagSet Parse(string cacheCsv, string? settingsJson, uint worldId, DateTime now)
  {
    var settings = ParseSettings(settingsJson);
    var set = new AllaganFlagSet(worldId, settings);

    if (!string.IsNullOrWhiteSpace(cacheCsv))
    {
      foreach (var line in cacheCsv.Split('\n'))
      {
        var trimmed = line.TrimEnd('\r');
        if (trimmed.Length == 0)
          continue;
        if (TryParseLine(trimmed, out var entry))
          set.Add(entry);
      }
    }

    return set;
  }

  public static bool TryParseLine(string line, out AllaganCacheEntry entry)
  {
    entry = null!;
    var fields = line.Split(',');
    if (fields.Length < 7)
      return false;
    if (!uint.TryParse(fields[0], out var itemId) || itemId == 0)
      return false;
    if (!uint.TryParse(fields[2], out var worldId))
      return false;
    if (!long.TryParse(fields[5], out var unitCost))
      return false;
    if (!DateTime.TryParseExact(fields[4].Trim(), DateFormats, System.Globalization.CultureInfo.InvariantCulture,
          System.Globalization.DateTimeStyles.None, out var lastUpdated))
      return false;

    entry = new AllaganCacheEntry(itemId, fields[1].Trim() == "Y", worldId, lastUpdated, unitCost, fields[6].Trim() == "Y");
    return true;
  }

  public static AllaganSettings ParseSettings(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
      return AllaganSettings.Defaults;

    try
    {
      using var doc = System.Text.Json.JsonDocument.Parse(json);
      var root = doc.RootElement;

      var period = AllaganSettings.Defaults.ItemUpdatePeriodMinutes;
      var undercutBy = AllaganSettings.Defaults.UndercutBy;
      var comparison = AllaganSettings.Defaults.UndercutComparison;

      if (root.TryGetProperty("IntegerSettings", out var ints) && ints.ValueKind == System.Text.Json.JsonValueKind.Object)
      {
        if (ints.TryGetProperty("ItemUpdatePeriod", out var periodEl) && periodEl.TryGetInt32(out var p) && p > 0)
          period = p;
        if (ints.TryGetProperty("UndercutBy", out var byEl) && byEl.TryGetInt32(out var b) && b >= 0)
          undercutBy = b;
      }

      if (root.TryGetProperty("EnumSettings", out var enums) && enums.ValueKind == System.Text.Json.JsonValueKind.Object
          && enums.TryGetProperty("UndercutComparison", out var cmp) && cmp.ValueKind == System.Text.Json.JsonValueKind.Object
          && cmp.TryGetProperty("Value", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
      {
        var name = value.GetString();
        if (!string.IsNullOrEmpty(name))
          comparison = name;
      }

      return new AllaganSettings(period, undercutBy, comparison);
    }
    catch (System.Text.Json.JsonException)
    {
      return AllaganSettings.Defaults;
    }
  }
}
