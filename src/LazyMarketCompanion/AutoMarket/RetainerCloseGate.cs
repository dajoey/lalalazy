using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercised by tests/LazyMarketCompanion.Harness (case 128).
//
// Governs the retainer-close sequence across SweepAllRetainers, ReviewSweep, and
// FinalDepositLap. When closing a retainer, the automation must:
//   1. Wait for the retainer menu (SelectString) to open after RetainerSellList closes.
//   2. Request close on the retainer menu once (and retry if it remains open for >= 60 ticks).
//   3. Acknowledge and dismiss the buyback-abandon confirm (SelectYesno) if vendoring occurred.
//   4. Declare completion only when the Summoning Bell's retainer list (RetainerList) is ready.

/// <summary>Actions decided by <see cref="RetainerCloseGate"/> during retainer exit.</summary>
public enum RetainerCloseAction
{
  /// <summary>In transition: waiting for an expected window or confirmation to process.</summary>
  Wait,
  /// <summary>The retainer menu (SelectString) is open and ready to be closed.</summary>
  CloseMenu,
  /// <summary>The post-vendoring buyback abandon dialog (SelectYesno) is open and ready for Yes.</summary>
  ConfirmBuyback,
  /// <summary>Retainer exit complete: RetainerList is ready at the bell.</summary>
  Done,
}

/// <summary>
/// State machine for leaving a retainer cleanly. Pure and Dalamud-free for harness testing.
/// </summary>
public static class RetainerCloseGate
{
  /// <summary>Ticks (~1 second at 60 fps) before re-issuing close if SelectString is still open.</summary>
  public const int RetryCloseIntervalTicks = 60;

  /// <summary>
  /// Decides what action to take this tick when closing a retainer.
  /// </summary>
  public static RetainerCloseAction Decide(
    bool retainerListReady,
    bool selectYesnoReady,
    string? yesnoPrompt,
    bool selectStringReady,
    bool closeSent,
    int closeTicks,
    bool buybackConfirmed)
  {
    // 1. If RetainerList is ready, the retainer has been closed and we are back at the bell list.
    if (retainerListReady)
      return RetainerCloseAction.Done;

    // 2. If the buyback-abandon confirm is up and not yet clicked, confirm it.
    if (!buybackConfirmed && selectYesnoReady)
    {
      if (BuybackConfirmGate.Decide(yesnoPrompt) == BuybackConfirmDecision.ConfirmLeave)
        return RetainerCloseAction.ConfirmBuyback;
      return RetainerCloseAction.Wait;
    }

    // 3. If SelectString is up and we haven't requested close yet (or retry interval elapsed):
    if (selectStringReady && (!closeSent || closeTicks >= RetryCloseIntervalTicks))
      return RetainerCloseAction.CloseMenu;

    // 4. In transition: waiting for SelectString to open, waiting for SelectYesno to appear/confirm, or waiting for RetainerList to appear.
    return RetainerCloseAction.Wait;
  }
}
