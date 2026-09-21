using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercises by tests/LazyMarketCompanion.Harness (case 127).
//
// After "Have Retainer Sell Items" vendoring, closing the retainer bell menu surfaces a
// SelectYesno: "Your retainer will be unable to process item buyback requests once recalled.
// Are you sure you wish to proceed?" Yes leaves and abandons the buyback list; No / ESC
// returns to the menu. AutoRetainer names the same gate WillBeUnableToProcessBuyback /
// ConfirmCantBuyback. Without handling it, CloseRetainer completes (or stalls) while the
// confirm stays up and the automarket chain cannot leave the retainer cleanly.
//
// Safeguards pinned here:
//   * Match ONLY this buyback-abandon confirm (distinctive prompt text), never any other
//     SelectYesno and never a Shop / Buy Back purchase UI.
//   * ConfirmLeave means click Yes (proceed / abandon buyback). There is no path that
//     opens buyback or re-purchases vendored stock.

/// <summary>What CloseRetainer should do when a SelectYesno may be on screen.</summary>
public enum BuybackConfirmDecision
{
  /// <summary>No SelectYesno text to judge - continue closing the bell menu.</summary>
  None,
  /// <summary>The buyback-abandon confirm - click Yes so leaving can finish.</summary>
  ConfirmLeave,
  /// <summary>Some other SelectYesno - do not click; leave it alone.</summary>
  IgnoreOther,
}

/// <summary>
/// Matches the post-vendoring retainer leave confirm. Kept Dalamud-free so the harness can
/// pin the text match and the "never generic dismiss / never buyback-accept" rules.
/// </summary>
public static class BuybackConfirmGate
{
  /// <summary>
  /// Distinctive English substring of the live SelectYesno prompt (Addon sheet text; OCR'd
  /// from the in-game dialog and corroborated by AutoRetainer's WillBeUnableToProcessBuyback
  /// naming). Matching requires this phrase so a generic YesAlready-style dismiss is impossible
  /// through this gate.
  /// </summary>
  public const string PromptMarker = "unable to process item buyback";

  /// <summary>
  /// Decides whether a SelectYesno prompt is the retainer buyback-abandon confirm.
  /// Null/empty text is <see cref="BuybackConfirmDecision.None"/> (addon not readable yet).
  /// </summary>
  public static BuybackConfirmDecision Decide(string? promptText)
  {
    if (string.IsNullOrWhiteSpace(promptText))
      return BuybackConfirmDecision.None;

    // Normalize newlines the way AddonMaster.SelectYesno.TextLegacy does, so a multi-line
    // prompt still matches the marker.
    var normalized = promptText.Replace('\n', ' ').Trim();
    if (normalized.IndexOf(PromptMarker, StringComparison.OrdinalIgnoreCase) >= 0)
      return BuybackConfirmDecision.ConfirmLeave;
    return BuybackConfirmDecision.IgnoreOther;
  }

  /// <summary>
  /// True only for the buyback-abandon confirm. Negative control for the harness: shop
  /// "Buy Back" labels and unrelated yes/no prompts must never pass.
  /// </summary>
  public static bool IsBuybackAbandonConfirm(string? promptText) =>
    Decide(promptText) == BuybackConfirmDecision.ConfirmLeave;
}
