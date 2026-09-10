using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// t_deb0e274 (2026-09-10): which planned listings this retainer never got a Listed{slot} confirmation
/// for. Before this fix, a timed-out confirmation just dropped the op - it never joined
/// <c>_listedThisRetainer</c>, so PinchScope saw a lower (sometimes zero) count and the pinch pass never
/// knew the retainer had anything new to price, and the value gate's own accounting silently undercounted.
///
/// Evidence this is real, not a race in the read: on 2026-09-10 11:45, three concurrent Universalis
/// timeouts for the value-gate lookup starved three Listed{slot} confirmations on the FIRST retainer of a
/// sweep; item 44011 turned up unvendored, unlisted, sitting in its retainer's own inventory nearly two
/// hours later - the listing itself never took, not just the read of it. So "planned but not confirmed"
/// needs a second look at the game's own state, not a second guess: see
/// <see cref="MarketAutomation.ReconcileUnconfirmedListings"/>, which re-checks
/// <c>AutoMarketService.IsListed</c> before deciding whether to retry.
/// </summary>
public static class ListingConfirmation
{
  /// <param name="planned">Every listing this retainer's plan attempted, in attempt order.</param>
  /// <param name="confirmed">Listings whose Listed{slot} step actually saw the server reflect them.</param>
  public static List<ListingOp> Unconfirmed(IReadOnlyList<ListingOp> planned, IReadOnlyList<ListingOp> confirmed)
  {
    if (planned.Count == 0)
      return [];
    if (confirmed.Count == 0)
      return planned.ToList();

    var confirmedSet = new HashSet<ListingOp>(confirmed);
    return planned.Where(op => !confirmedSet.Contains(op)).ToList();
  }

  /// <summary>
  /// Whether a Listed{slot} confirmation step should retry the <c>MoveToRetainerMarket</c> call right
  /// now. True exactly once per op: the first tick after <paramref name="retryDeadlineMs"/> has passed
  /// while the item still is not confirmed listed and no retry has fired for this op yet. Kept pure so
  /// the retry-exactly-once contract (never twice, never never) is harness-pinned rather than trusted
  /// to a hand-checked closure inside MarketAutomation.
  /// </summary>
  public static bool ShouldRetryNow(bool alreadyRetried, long nowMs, long retryDeadlineMs)
    => !alreadyRetried && nowMs >= retryDeadlineMs;
}
