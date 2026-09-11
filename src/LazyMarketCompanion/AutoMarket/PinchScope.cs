using System.Collections.Generic;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>What the Auto-Market chain does with the retainer's sell list once it has finished listing.</summary>
public enum PinchAfterMarket
{
  /// <summary>Walk all 20 rows and re-price every listing, exactly as the Auto Pinch button does.</summary>
  FullRePass,
  /// <summary>Price only the slots this run listed into.</summary>
  NewListingsOnly,
  /// <summary>Touch nothing. This run put nothing on this retainer's board, so it has nothing to price.</summary>
  Nothing,
}

/// <summary>
/// The one decision that says how much of a retainer an Auto-Market pass is allowed to re-price.
///
/// It lives here, out of <c>MarketAutomation</c>, because from the 0.1.0.0 release commit (58e882000) through
/// 0.1.6.0 it was a single un-testable clause in the middle of a task lambda:
///
/// <code>if (Plugin.Configuration.AutoMarketPinchAllAfter || _listedThisRetainer.Count == 0)</code>
///
/// The second half of that <c>||</c> is what Joey reported on 2026-09-05: "It did the first retainer
/// correctly. none of the other retainers needed auto-market b/c they were full. and so it re-pinched all of
/// their items." A retainer whose market board is full plans zero listings, so <c>_listedThisRetainer</c> is
/// empty, so the clause fired and the run did a full 20-row re-pass on a retainer it had not touched at all.
///
/// In 0.1.0.0 that was deliberate - "Auto Market" then meant "list, and then Auto Pinch everything" - and it
/// survived untouched through 0.1.3.0 / 0.1.5.0 / 0.1.6.0 because every one of those fixes was inside
/// <c>InsertPinchForNewListings</c>, which this branch never reaches.
///
/// The rule now: <b>listing nothing means pricing nothing.</b> Re-pricing a whole retainer is still available
/// two ways, both of which the user asks for explicitly - the Auto Pinch button, and the "Pinch everything
/// after listing" setting.
/// </summary>
public static class PinchScope
{
  /// <summary>
  /// Whether the vendoring leg may be queued at the moment the pinch decision runs: the pinch pass
  /// must go first, while the sell list is still open (it reads the list to find the rows this run
  /// just listed). Pure bookkeeping order - the gate's vendoring decision itself is unchanged.
  ///
  /// 0.1.38.0 (t_fe0e06f0): the call in MarketAutomation.cs that makes this true is the vendor block
  /// calling Insert() FIRST, then the pinch switch calling Insert() SECOND - ECommons'
  /// TaskManager.Insert() always pushes to the FRONT of the queue (LIFO), so the LAST Insert() call in
  /// a step's body is the FIRST one the task manager runs. 0.1.28.0 through 0.1.37.0 had the vendor
  /// call textually last, believing that placed it last at runtime too - it placed it first, and every
  /// vending-then-pinching session in that window ran the vendor leg before the pinch, closing the
  /// sell list the pinch needed open ("Clearing 110 remaining tasks because of timeout" /
  /// "OpenItemContextMenu" stuck retrying against a closed addon). If this call order in
  /// MarketAutomation.cs is ever "tidied" back to vendor-block-last, this constant goes false again
  /// with no compiler error to catch it - only harness case 50's ordering assertion does.
  /// </summary>
  public static bool PinchRunsBeforeVendorLeg => true;
  /// <param name="pinchAllAfter">The "Pinch everything after listing" setting (<c>AutoMarketPinchAllAfter</c>).</param>
  /// <param name="listedThisRetainer">
  /// How many listings this run actually got onto this retainer's board. Zero covers both "the plan was empty
  /// because the board is full" and "listings were planned but none of them landed" - neither leaves anything
  /// sitting at the placeholder price, so in both cases there is nothing for a pinch pass to rescue.
  /// </param>
  public static PinchAfterMarket Decide(bool pinchAllAfter, int listedThisRetainer)
  {
    if (pinchAllAfter)
      return PinchAfterMarket.FullRePass;
    return listedThisRetainer > 0 ? PinchAfterMarket.NewListingsOnly : PinchAfterMarket.Nothing;
  }
}
