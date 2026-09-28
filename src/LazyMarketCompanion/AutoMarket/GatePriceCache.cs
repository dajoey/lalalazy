using System;
using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Short-TTL price cache for the Universalis value gate (0.2.1.0).
/// When transient Universalis outages or 504 timeouts occur during a multi-chunk gate lookup,
/// previously-priced items within their TTL degrade to cached quotes rather than declaring all
/// items unpriced and starving the listing pass. Still ACTS ONLY on confirmed data - never a guess.
/// </summary>
public sealed class GatePriceCache
{
  public const long DefaultTtlMs = 30 * 60 * 1000L; // 30 minutes bounded TTL

  private readonly object _lock = new();
  private readonly Dictionary<uint, (ItemQuote Quote, long CachedAtUnixMs)> _entries = new();

  public int Count
  {
    get
    {
      lock (_lock)
        return _entries.Count;
    }
  }

  public void Record(ItemQuote quote, long nowUnixMs)
  {
    if (quote == null || quote.ItemId == 0)
      return;

    lock (_lock)
    {
      _entries[quote.ItemId] = (quote, nowUnixMs);
    }
  }

  public void Record(IEnumerable<KeyValuePair<uint, ItemQuote>> quotes, long nowUnixMs)
  {
    if (quotes == null)
      return;

    lock (_lock)
    {
      foreach (var kv in quotes)
      {
        if (kv.Value != null && kv.Key != 0)
          _entries[kv.Key] = (kv.Value, nowUnixMs);
      }
    }
  }

  public bool TryGet(uint itemId, long nowUnixMs, long ttlMs, out ItemQuote quote)
  {
    lock (_lock)
    {
      if (_entries.TryGetValue(itemId, out var entry))
      {
        if (nowUnixMs - entry.CachedAtUnixMs <= ttlMs)
        {
          quote = entry.Quote;
          return true;
        }
        _entries.Remove(itemId);
      }
    }
    quote = null!;
    return false;
  }

  /// <summary>
  /// Fills any missing item ids in <paramref name="destination"/> from cache if a valid quote within <paramref name="ttlMs"/> exists.
  /// Returns the number of items recovered from the cache.
  /// </summary>
  public int PopulateMissing(Dictionary<uint, ItemQuote> destination, IEnumerable<uint> requestedIds, long nowUnixMs, long ttlMs)
  {
    var recovered = 0;
    lock (_lock)
    {
      foreach (var id in requestedIds)
      {
        if (destination.ContainsKey(id))
          continue;

        if (_entries.TryGetValue(id, out var entry))
        {
          if (nowUnixMs - entry.CachedAtUnixMs <= ttlMs)
          {
            destination[id] = entry.Quote;
            recovered++;
          }
          else
          {
            _entries.Remove(id);
          }
        }
      }
    }
    return recovered;
  }

  public void Clear()
  {
    lock (_lock)
    {
      _entries.Clear();
    }
  }

  private string? _scopeKey;

  /// <summary>
  /// 0.2.8.0: the cache is keyed by item id alone, so it must only ever hold quotes from ONE market
  /// scope. The plugin's lifetime spans several characters on different worlds (AutoRetainer
  /// multi-mode), and a world quote recovered from the cache after a failed chunk would otherwise
  /// stand in for ANOTHER world's price and could drive the vendor or pull leg. The caller names
  /// the scope it is about to query (for example "world:Hyperion"); when it differs from the
  /// scope the cache was last used for, every entry is dropped. Returns true when entries were
  /// discarded because the scope changed.
  /// </summary>
  public bool EnsureScope(string scopeKey)
  {
    lock (_lock)
    {
      if (string.Equals(_scopeKey, scopeKey, StringComparison.Ordinal))
        return false;

      var discarded = _entries.Count > 0;
      _entries.Clear();
      _scopeKey = scopeKey;
      return discarded;
    }
  }
}
