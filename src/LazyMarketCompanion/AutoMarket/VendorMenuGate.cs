using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercises by tests/LazyMarketCompanion.Harness (case 42).
//
// 0.1.15.2: the vendoring leg's menu-open decision table, lifted out of
// MarketAutomation.ClickRetainerEntrust so the harness can pin it. Two shipped releases failed on
// exactly this decision:
//   - 0.1.15.1 glanced ONCE at the bell menu 135 ms after the sell-list close was queued, saw no
//     SelectString, and treated "not yet" as a verdict - the panel never opened and the session's
//     close steps timed out (the "Clearing 53 remaining tasks" abort, omasky 01:45:28).
//   - Treating "menu present but no entrust entry" as "skip quietly" would vendor nothing while
//     the run reported done.
//
// The rules:
//   * menu not on screen YET  -> WaitForMenu (the step RETRIES until its own time limit)
//   * sheet text not loaded   -> WaitForMenu (a menu this fresh may not be either; a wrong verdict
//                                here would stop a sweep that was about to succeed)
//   * entrust entry found     -> OpenPanel (one SelectString click)
//   * menu ready, no entry    -> MenuMissingEntry - a REAL failure: the sweep stops on purpose
//                                (stop-on-failure, Joey's pick on Helm t-joey-1788757755566)

/// <summary>What the vendoring leg's menu-open step should do on this tick.</summary>
public enum VendorMenuDecision
{
  /// <summary>The bell menu has not come back yet (or the Addon text is not loaded) - retry the step.</summary>
  WaitForMenu,
  /// <summary>The entrust entry was found - click it and proceed to the panel wait.</summary>
  OpenPanel,
  /// <summary>The menu is up but carries no entrust entry - the leg cannot run; stop the sweep on purpose.</summary>
  MenuMissingEntry,
}

public static class VendorMenuGate
{
  /// <summary>
  /// Decides what the menu-open step does this tick. <paramref name="menuReady"/> is the SelectString
  /// addon's ready state, <paramref name="sheetTextLoaded"/> whether the Addon-sheet text for the
  /// entrust entry resolved, and <paramref name="entryFound"/> whether a menu entry matching that
  /// text is on screen. Unknown states wait; only a menu that is demonstrably up WITHOUT the entry
  /// is a failure.
  /// </summary>
  public static VendorMenuDecision Decide(bool menuReady, bool sheetTextLoaded, bool entryFound)
  {
    if (!menuReady)
      return VendorMenuDecision.WaitForMenu;
    if (!sheetTextLoaded)
      return VendorMenuDecision.WaitForMenu;
    if (entryFound)
      return VendorMenuDecision.OpenPanel;
    return VendorMenuDecision.MenuMissingEntry;
  }
}
