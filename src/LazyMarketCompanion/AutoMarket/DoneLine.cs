using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. The Auto-Market run's closing chat line. Communicator.PrintSweepDone delegates to
// Format; the offline harness (case 40) pins the format character-for-character.
//
// 0.1.15.0 adds the vendoring-failure clause. The 0.1.12.0 build announced "vendoring N stack(s)"
// before executing and then only warned in the log when the ops failed, so a run that vendored 0 of 7
// read as success in chat. The done line now carries "M vendoring op(s) failed (see log)" and the
// AnnounceRunDone guard names a 0-of-N leg explicitly.

/// <summary>The counters one Auto-Market run closes with.</summary>
public static class DoneLine
{
  /// <summary>
  /// Renders the closing line. Order is fixed and user-visible: listings, listing skips, vendored,
  /// vendoring failures, held-back, unconfirmed. All-zero renders as the plain "done." the button has
  /// always printed for a run that did nothing.
  /// </summary>
  /// <param name="unconfirmed">
  /// t_deb0e274 (2026-09-10): listings whose Listed{slot} confirmation never saw the server reflect
  /// them, even after a retry - a DIFFERENT failure from <paramref name="failures"/> (which means the
  /// source stock moved before the listing call even fired), so it gets its own honest clause rather
  /// than being folded into "skipped (stock moved)", which would misname the reason.
  /// </param>
  public static string Format(int listed, int failures, int vendored, int heldBack, int vendorFailures, int pulled = 0, int unconfirmed = 0, int routed = 0)
  {
    return listed == 0 && failures == 0 && vendored == 0 && heldBack == 0 && vendorFailures == 0 && pulled == 0 && unconfirmed == 0 && routed == 0
      ? "done."
      : $"done: {listed} new listing(s){(failures > 0 ? $", {failures} skipped (stock moved)" : string.Empty)}{(unconfirmed > 0 ? $", {unconfirmed} unconfirmed (see log)" : string.Empty)}{(pulled > 0 ? $", {pulled} pulled" : string.Empty)}{(routed > 0 ? $", {routed} routed into place" : string.Empty)}{(vendored > 0 ? $", {vendored} vendored" : string.Empty)}{(vendorFailures > 0 ? $", {vendorFailures} vendoring op(s) failed (see log)" : string.Empty)}{(heldBack > 0 ? $", {heldBack} held back by the value gate" : string.Empty)}.";
  }

  /// <summary>
  /// 0.2.6.0: run-scoped counters. The done line's counters cover ONE run - the ClearState-to-done
  /// window, whatever its scope (a whole sweep, one manual current-retainer run, or one AutoRetainer
  /// postprocess session) - and a game session can contain several runs. The 2026-09-28 session ran
  /// two sweeps; its single surviving done line reconciled with the SECOND run's ops only, and
  /// grading it against the whole session's op lines read as "5 counted vs 8 executed". The run tag
  /// makes the boundary mechanical: every run logs a tagged start line and a tagged done line, and a
  /// run whose chain dies (task-manager cascade) is visible as a start with no done. The untagged
  /// Format above is unchanged - case 40 still pins it character-for-character.
  /// </summary>
  public static string RunTag(int runId) => $"run #{runId}";

  public static string RunStartLogLine(int runId) => $"Auto-Market {RunTag(runId)} start";

  public static string RunDoneLogLine(int runId, string formattedDoneLine) => $"Auto-Market {RunTag(runId)} {formattedDoneLine}";
}

