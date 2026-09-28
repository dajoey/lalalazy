using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// 0.2.6.0: the pull pass's result contract, stated once so the executor cannot invent its
/// own polarity. THE DEFECT (2026-09-28 session, item 44072 x12): the game's
/// MoveFromRetainerMarket* call returned rc=0 - the server ACCEPTED the withdrawal - but the
/// market container's read-back still showed the item after the bounded 1.5 s retry window
/// (container lag under load), and ExecutePull reported FAILED with "leaving the listing on
/// the board" for a pull that had landed: the same pass later vendored that stack out of
/// retainer inventory. rc=0 is acceptance; a lagging read-back is a CONFIRMATION problem, not
/// a failure, and is reported as accepted-with-lag so the next pass's fresh board snapshot
/// is the corrective. A nonzero rc is a real failure and keeps the old semantics.
/// </summary>
public enum PullResult
{
  /// <summary>The server refused the move (rc != 0) or the pre-fire slot check failed: the listing stays up.</summary>
  Failed,

  /// <summary>rc == 0 and the slot read back cleared: the ordinary success.</summary>
  Accepted,

  /// <summary>rc == 0 but the slot still reads occupied after the bounded retry window: accepted, container lagging.</summary>
  AcceptedWithLag,
}

/// <summary>The classification and the line formats, pinned by the harness (case 154).</summary>
public static class PullOutcome
{
  public const PullResult Failed = PullResult.Failed;
  public const PullResult Accepted = PullResult.Accepted;
  public const PullResult AcceptedWithLag = PullResult.AcceptedWithLag;

  /// <summary>The one classification, from the raw rc and the read-back outcome.</summary>
  public static PullResult Classify(int rc, bool slotClearedAfterRetry)
    => rc != 0 ? PullResult.Failed
      : slotClearedAfterRetry ? PullResult.Accepted
      : PullResult.AcceptedWithLag;

  /// <summary>
  /// The line for an accepted-with-lag pull: names the acceptance (rc=0), the lag window,
  /// and that the next pass re-reads the board - never "FAILED", never "leaving the listing
  /// on the board".
  /// </summary>
  public static string LagLine(uint itemId, bool hq, int quantity, int retryWindowMs)
    => $"pull: item {itemId}{(hq ? " HQ" : "")} x{quantity} was accepted (rc=0) but the market slot still reads occupied after the {retryWindowMs}ms retry window (container lag); treating it as pulled - the next pass re-reads the board and corrects any stale slot state";
}
