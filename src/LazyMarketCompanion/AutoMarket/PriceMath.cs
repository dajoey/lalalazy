using System;

namespace LazyMarketCompanion;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.
//
// NOTE on the namespace: this file sits under AutoMarket/ with the rest of the Dalamud-free logic, but it
// declares the PARENT namespace LazyMarketCompanion on purpose. UndercutMode below is referenced from
// Configuration.cs, MarketBoardHandler.cs, UniversalisPriceProvider.cs and ConfigWindow.cs; keeping it in
// the parent namespace means moving the declaration needed no new using directive anywhere, and the
// AutoMarket namespace still sees both types because C# searches enclosing namespaces.

/// <summary>
/// How a matched price is derived from the lowest listing on the board.
///
/// This enum lived in Configuration.cs until 0.1.9.0. It moved here so the Dalamud-free price formula
/// (<see cref="PriceMath"/>) and the harness can both see it without dragging Dalamud in. The member
/// ORDER and NAMES are unchanged - an existing config serializes this as the integer 0/1, so moving the
/// declaration must not reorder it.
/// </summary>
public enum UndercutMode
{
  FixedAmount,
  Percentage
}

/// <summary>
/// The one price formula: given the lowest listing on the board, what would this plugin ask?
///
/// It exists as its own Dalamud-free class because 0.1.9.0 added a SECOND caller. The Auto Pinch
/// pre-flight (<see cref="AutoMarket.PinchPreflight"/>) predicts what the pricing pass would write so it
/// can skip rows where the answer is the price the listing already has. If the prediction and the pass
/// ever computed the number differently, the pre-flight would skip rows that the pass would in fact have
/// changed - a silent wrong skip, which costs a sale. So both go through here, and the harness pins this
/// against the pre-0.1.9.0 inline formula over a table of inputs.
/// </summary>
public static class PriceMath
{
  /// <param name="lowestPricePerUnit">Unit price of the cheapest listing of the wanted quality on the board.</param>
  /// <param name="lowestIsOwnRetainer">True when that cheapest listing is one of the user's own retainers.</param>
  /// <param name="mode">Fixed gil below, or a percentage of, the lowest listing.</param>
  /// <param name="undercutAmount">Gil (FixedAmount) or percent (Percentage). 0 = match the lowest exactly.</param>
  /// <param name="undercutSelf">
  /// When false (the default) the user's own listing is MATCHED rather than undercut - which is exactly why
  /// the pre-flight has anything to do: if the user is already the cheapest on the data centre, the answer
  /// is the price already on the listing.
  /// </param>
  public static int Candidate(long lowestPricePerUnit, bool lowestIsOwnRetainer, UndercutMode mode, int undercutAmount, bool undercutSelf)
  {
    var price = (int)Math.Min(lowestPricePerUnit, int.MaxValue);

    if (!undercutSelf && lowestIsOwnRetainer)
      return price;

    if (mode == UndercutMode.FixedAmount)
      return Math.Max(price - undercutAmount, 1);

    return (int)Math.Max((100L - undercutAmount) * price / 100L, 1);
  }
}
