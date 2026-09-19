namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// t_deb0e274 (2026-09-10): a Listed{slot} confirmation that times out must not silently drop the op.
/// Before that fix it never joined <c>_listedThisRetainer</c>, so PinchScope saw a lower (sometimes
/// zero) count and the pinch pass never knew the retainer had anything new to price, and the value
/// gate's own accounting silently undercounted.
///
/// Evidence this is real, not a race in the read: on 2026-09-10 11:45, three concurrent Universalis
/// timeouts for the value-gate lookup starved three Listed{slot} confirmations on the FIRST retainer of a
/// sweep; item 44011 turned up unvendored, unlisted, sitting in its retainer's own inventory nearly two
/// hours later - the listing itself never took, not just the read of it. So a listing that has not
/// confirmed gets one retry of the call itself, on the schedule <see cref="ShouldRetryNow"/> decides,
/// and a timeout that survives that retry is reported loudly rather than dropped.
///
/// 0.1.60.0: the <c>Unconfirmed(planned, confirmed)</c> set-difference helper that used to sit here was
/// removed. It had no callers, and its summary described a second-look pass
/// (<c>MarketAutomation.ReconcileUnconfirmedListings</c>) that was never written - a doc comment
/// promising a safety net that did not exist is worse than no comment. The retry-once-then-report-loudly
/// path in MarketAutomation.AddListingSteps is the whole of what actually runs.
/// </summary>
public static class ListingConfirmation
{
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
