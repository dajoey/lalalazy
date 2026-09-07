using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercises by tests/LazyMarketCompanion.Harness (case 42).
//
// 0.1.16.3: a session-end trigger with NO vendoring plan completes as a no-op (true), never a
// retry (false) - a false answer re-runs it every tick for its full time limit at the FRONT of the
// queue, which stalled the sweep two minutes per listing retainer on 2026-09-07 (harness case 44).
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

  /// <summary>
  /// 0.1.16.3: the completion polarity of the session-end vendor trigger when no vendoring plan
  /// was placed this retainer. A trigger with no plan is NOTHING TO DO - it completes (true) and
  /// the task manager continues the queue. It is never a retry (false): a false answer re-runs the
  /// trigger every tick until its time limit expires, ahead of the session's own listing steps -
  /// the 0.1.16.2 two-minute-per-retainer stall. Only a genuinely failed leg stops the sweep
  /// (stop-on-failure); this path emits no stop.
  /// </summary>
  public static bool NoPlanTriggerCompletes() => true;

  /// <summary>
  /// The wait window before a menu WITHOUT the wanted entry is declared a failure (0.1.23.0).
  /// 0.1.19.0 declared it on the first tick, and on 2026-09-07 15:06 the first tick was 181 ms
  /// after the sell-list close was queued - mid-transition, the menu not yet back. A real
  /// wrong-menu failure is static; a transition is not. The step retries through this window
  /// before the verdict, and the step's own 10 s limit still caps the wait-for-menu side.
  /// </summary>
  public const int MenuGraceWindowMs = 2000;

  /// <summary>
  /// Whether one SelectString entry matches the wanted Addon-sheet text (0.1.23.0). Row 2378's
  /// template is "Entrust or withdraw items. (Slots filled: 0)" - the number is a live runtime
  /// payload, so the rendered entry reads "(Slots filled: 20)" on a full retainer. Comparing the
  /// TEMPLATE against the RENDERED text fails even on a perfect menu; AutoRetainer drives this
  /// exact entry with the same sheet row and a StartsWith, which is why it never mis-fires here.
  /// Matching therefore trims the template at the first '(' and compares that stable prefix
  /// case-insensitively, and the entry side additionally accepts a prefix match before any '('
  /// (which catches a template drift where the rendered entry carries an older, suffix-free
  /// text). Empty wanted text never matches - an unresolved sheet row must WAIT (WaitForMenu),
  /// never fail.
  /// </summary>
  public static bool MatchMenuEntry(string? entryText, string? wanted)
  {
    if (string.IsNullOrEmpty(wanted) || entryText == null)
      return false;

    var wantedPrefix = wanted;
    var wp = wantedPrefix.IndexOf('(');
    if (wp > 0)
      wantedPrefix = wantedPrefix[..wp].TrimEnd();

    var entry = entryText.Trim();
    var ep = entry.IndexOf('(');
    var entryPrefix = ep > 0 ? entry[..ep].TrimEnd() : entry;

    return entry.StartsWith(wantedPrefix, StringComparison.OrdinalIgnoreCase)
        || entryPrefix.StartsWith(wantedPrefix, StringComparison.OrdinalIgnoreCase);
  }
}
