using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 160h).

/// <summary>
/// The marker state-transition log (0.2.8.9). Five testing builds proved the anchors, the draw and
/// the renderer, and the dots still vanished - "as soon as I click my bag, they disappear and don't
/// come back" - with NO trace in the log: every marker logger was first-draw-only or
/// early-pass-capped, so a click that killed the dots after the first frames was invisible. This
/// policy inverts that: the caller reports each grid's outcome every frame, and the log fires on
/// CHANGE - the first observation logs whatever it shows (a session that starts broken is on
/// record), every outcome change logs with what it changed from (drawn -> held -> drawn, drawn ->
/// hidden, drawn -> closed -> drawn), a persisting outcome logs nothing, and the whole ring is
/// capped at <see cref="LineCap"/> lines per (addon, container) per session. A click that kills
/// the dots leaves a trace; the next session's log names the gate that ate them and when.
/// </summary>
public static class MarkerTransitions
{
  /// <summary>Hard cap of transition lines per (addon, container) per session - bounded logging, no spam.</summary>
  public const int LineCap = 48;

  /// <summary>Per-(addon, container) transition bookkeeping, held by the caller (AutoMarketMarkers).</summary>
  public sealed class State
  {
    /// <summary>Whether the first observation has been recorded at all.</summary>
    public bool Started;

    /// <summary>The last outcome that logged - the on-change trigger.</summary>
    public string? LastOutcome;

    /// <summary>How many transition lines this (addon, container) has logged this session.</summary>
    public int Lines;
  }

  /// <summary>
  /// Whether this frame's outcome logs a transition line: the first observation always does, then
  /// every change of outcome, never past <see cref="LineCap"/> lines. Pure read plus no mutation -
  /// the caller records only when it actually logs.
  /// </summary>
  public static bool ShouldLog(State state, string outcome)
  {
    if (state.Lines >= LineCap)
      return false;
    if (!state.Started)
      return true;
    return !string.Equals(state.LastOutcome, outcome, StringComparison.Ordinal);
  }

  /// <summary>Caller-side bookkeeping after a transition line actually logged.</summary>
  public static void RecordLogged(State state, string outcome)
  {
    state.Started = true;
    state.Lines++;
    state.LastOutcome = outcome;
  }

  /// <summary>The previous outcome for the log line ("start" before the first observation).</summary>
  public static string PreviousOutcome(State state) => state.Started ? state.LastOutcome ?? "?" : "start";
}
