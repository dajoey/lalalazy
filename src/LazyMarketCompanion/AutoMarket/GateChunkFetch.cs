using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>What one gate fetch run produced.</summary>
/// <param name="Quotes">Merged per-item quotes, or null when no chunk answered (the declared blind gate).</param>
/// <param name="AnsweredItemCount">Items in chunks that answered or were recovered from cache.</param>
/// <param name="RetriedChunks">Chunks that failed at least once and answered on retry.</param>
/// <param name="FailedChunks">Chunks that answered never, even after all retries.</param>
/// <param name="CachedItemCount">Items recovered from the short-TTL price cache on failed chunks.</param>
internal sealed record GateFetchResult(
  Dictionary<uint, ItemQuote>? Quotes,
  int AnsweredItemCount,
  int RetriedChunks,
  int FailedChunks,
  int CachedItemCount = 0);

/// <summary>
/// Chunked Universalis gate fetch (0.1.19.0 chunking, 0.1.68.0 retry, 0.2.1.0 bounded backoff + cache).
/// One request per <paramref name="chunkSize"/> ids; a dead chunk costs only its own
/// items instead of the whole gate's sight. A real cancel (sweep aborted) still throws; a chunk
/// that dies any other way - including the HttpClient's own 8 s timeout, which surfaces as a
/// TaskCanceledException - is retried with backoff. If all attempts die, valid recent quotes from
/// <paramref name="cache"/> within <paramref name="cacheTtlMs"/> are used as fallback before items
/// are declared unpriceable.
/// </summary>
internal static class GateChunkFetch
{
  /// <summary>First try plus two retries per chunk with backoff (0.2.1.0).</summary>
  private const int MaxAttempts = 3;

  public static async Task<GateFetchResult> FetchAllAsync(
    IReadOnlyList<uint> itemIds,
    int chunkSize,
    Func<IReadOnlyList<uint>, CancellationToken, Task<Dictionary<uint, ItemQuote>>> fetchChunk,
    Action<int, Exception> onChunkFailed,
    CancellationToken cancellationToken,
    GatePriceCache? cache = null,
    long nowUnixMs = 0,
    long cacheTtlMs = GatePriceCache.DefaultTtlMs,
    Func<int, Task>? backoffDelay = null)
  {
    var merged = new Dictionary<uint, ItemQuote>();
    var answered = 0;
    var retried = 0;
    var failed = 0;
    var cached = 0;
    for (var i = 0; i < itemIds.Count; i += chunkSize)
    {
      var chunk = itemIds.Skip(i).Take(chunkSize).ToList();
      Dictionary<uint, ItemQuote>? quotes = null;
      for (var attempt = 0; attempt < MaxAttempts && quotes == null; attempt++)
      {
        var lastAttempt = attempt + 1 == MaxAttempts;
        if (attempt > 0 && backoffDelay != null)
        {
          await backoffDelay(attempt).ConfigureAwait(false);
        }

        try
        {
          quotes = await fetchChunk(chunk, cancellationToken).ConfigureAwait(false);
          if (attempt > 0)
            retried++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          if (lastAttempt)
          {
            failed++;
            onChunkFailed(chunk.Count, ex);
          }
        }
      }

      if (quotes != null)
      {
        if (cache != null && nowUnixMs > 0)
          cache.Record(quotes, nowUnixMs);

        foreach (var kv in quotes)
          merged[kv.Key] = kv.Value;
        answered += chunk.Count;
      }
      else if (cache != null && nowUnixMs > 0)
      {
        var recovered = cache.PopulateMissing(merged, chunk, nowUnixMs, cacheTtlMs);
        if (recovered > 0)
        {
          cached += recovered;
          answered += recovered;
        }
      }
    }

    return new GateFetchResult(answered > 0 ? merged : null, answered, retried, failed, cached);
  }
}
