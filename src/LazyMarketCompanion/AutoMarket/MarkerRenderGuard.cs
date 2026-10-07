using System;
using System.Globalization;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 160i).

/// <summary>
/// The render-side guard (0.2.8.10). The 0.2.8.8/0.2.8.9 probes measured everything the plugin
/// could see - anchors, window rects, clip rects, vertex counts - and every line looked perfect
/// while the player still reported vanished dots. The ImGui v1.90.4 source names the gap the
/// probes cannot see: a window whose style Alpha is &lt;= 0 at Begin is marked Hidden, any window
/// Begun inside a hidden window's scope inherits that flag, and at ImGui::Render a hidden
/// window's draw list is silently dropped (imgui.cpp IsWindowActiveAndVisible) - the plugin's own
/// bookkeeping stays green the whole time, because the vertices are still added and the rects
/// still measure. The only producer of a persistent Alpha = 0 is an unbalanced
/// PushStyleVar(ImGuiStyleVar.Alpha, 0) by code drawn earlier in the frame: v1.90.4 has no
/// per-frame leak recovery (ErrorCheckEndFrameRecover is commented out of EndFrame) and the
/// style-var stack is cleared only at context destruction, so one leak lasts the session and
/// hides every window Begun after it - "drawn, then gone for good, with no line". Dalamud's own
/// window wrapper never reaches zero (its fade-in multiplies a 0.072 s ramp over the current
/// alpha and its opacity slider clamps to 0.2). This guard holds the two decisions the caller
/// executes on the live ImGui: <see cref="Plan"/> - at the marker window's PreDraw (which runs
/// before Dalamud Begins it), whether the observed style state is a leak that must be repaired
/// by pushing Alpha = 1 over the marker scope for this frame; and <see cref="Classify"/> - what a
/// window's real post-Begin flags say happened to it. Pure functions; the caller reads ImGui,
/// logs, pushes and pops.
/// </summary>
public static class MarkerRenderGuard
{
  /// <summary>Hard cap of style-epoch lines per session - bounded logging, no spam.</summary>
  public const int StyleEpochLineCap = 16;

  /// <summary>Style-epoch bookkeeping, held by the caller (AutoMarketMarkers).</summary>
  public sealed class StyleEpochState
  {
    /// <summary>The last observed style state that logged (leaked vs healthy + stack depth).</summary>
    public string? LastKey;

    /// <summary>How many style-epoch lines this session has logged.</summary>
    public int Lines;
  }

  /// <summary>The decision for one frame, from the style state observed at the marker window's PreDraw.</summary>
  /// <param name="ForceAlpha">Whether the caller must push Alpha = 1 over the marker scope this frame.</param>
  /// <param name="ObservedAlpha">The style Alpha the caller observed (what this window's Begin will see).</param>
  /// <param name="StyleVarStackDepth">The style-var stack depth the caller observed - the leak's fingerprint.</param>
  public sealed record RepairPlan(bool ForceAlpha, float ObservedAlpha, int StyleVarStackDepth);

  /// <summary>
  /// The frame's repair plan: a style Alpha &lt;= 0 is always a leak - no visible window ever
  /// wants it (ImGui itself hides every window Begun under it and drops them at render) - and is
  /// repaired by pushing Alpha = 1 for the marker scope. Healthy frames plan no repair: the
  /// caller pushes nothing and nothing about the frame changes.
  /// </summary>
  public static RepairPlan Plan(float styleAlpha, int styleVarStackDepth) =>
    new(styleAlpha <= 0f, styleAlpha, styleVarStackDepth);

  /// <summary>What a window's own flags say happened to it, read right after its Begin.</summary>
  public enum Verdict
  {
    /// <summary>Active, not hidden, not skipped, first submission this frame.</summary>
    Rendered,
    /// <summary>SkipItems or Hidden set - the window still processes and its vertices are still added, but ImGui::Render drops its draw list.</summary>
    DroppedAtRender,
    /// <summary>Not Active - the window never made it into this frame.</summary>
    NotActive,
    /// <summary>Collapsed - impossible for the marker windows (NoTitleBar/NoCollapse make Begin force it false), so a surprise names itself.</summary>
    Collapsed,
    /// <summary>Begun more than once this frame - the second submission's position was consumed and ignored.</summary>
    DuplicateSubmit,
  }

  /// <summary>
  /// Classify one window from its post-Begin flags. Dropped outranks everything but !Active and
  /// Collapsed (a hidden window is the stronger truth than a duplicate submission); a healthy
  /// window that was Begun twice still reports the duplicate, because its position is stale.
  /// </summary>
  public static Verdict Classify(bool active, bool hidden, bool skipItems, bool collapsed, int beginCount)
  {
    if (!active)
      return Verdict.NotActive;
    if (collapsed)
      return Verdict.Collapsed;
    if (hidden || skipItems)
      return Verdict.DroppedAtRender;
    if (beginCount > 1)
      return Verdict.DuplicateSubmit;
    return Verdict.Rendered;
  }

  public static string VerdictLabel(Verdict verdict) => verdict switch
  {
    Verdict.Rendered => "rendered",
    Verdict.DroppedAtRender => "dropped at render",
    Verdict.NotActive => "not active",
    Verdict.Collapsed => "collapsed",
    _ => "duplicate submit",
  };

  /// <summary>The compact outcome token for the transition log (no spaces, like the other outcomes).</summary>
  public static string VerdictToken(Verdict verdict) => verdict switch
  {
    Verdict.Rendered => "rendered",
    Verdict.DroppedAtRender => "dropped-at-render",
    Verdict.NotActive => "not-active",
    Verdict.Collapsed => "collapsed",
    _ => "duplicate-submit",
  };

  /// <summary>
  /// Whether this frame's style observation logs the style-epoch line: the first observation logs
  /// whatever it shows (a session that starts leaked is on record), every CHANGE of the observed
  /// state (leaked vs healthy, stack depth) logs the moment it changes - so a leak starting
  /// mid-session names its own onset - a persisting state is silent, and the whole log is bounded
  /// at <see cref="StyleEpochLineCap"/> lines per session. Pure read plus no mutation; the caller
  /// records only when it actually logs.
  /// </summary>
  public static bool ShouldLogStyleEpoch(StyleEpochState state, RepairPlan plan)
  {
    if (state.Lines >= StyleEpochLineCap)
      return false;
    var key = StyleEpochKey(plan);
    if (state.LastKey == key)
      return false;
    state.LastKey = key;
    return true;
  }

  /// <summary>Caller-side bookkeeping after a style-epoch line actually logged.</summary>
  public static void RecordStyleEpochLogged(StyleEpochState state) => state.Lines++;

  /// <summary>
  /// The one style-epoch line: what the style state was, and what the caller did about it.
  /// <paramref name="repaired"/> is the plan the caller executed (it pushed Alpha = 1 over the
  /// marker scope this frame).
  /// </summary>
  public static string StyleEpochLine(RepairPlan plan, bool repaired) =>
    $"alpha={Fmt(plan.ObservedAlpha, "0.###")} stylevar-stack={plan.StyleVarStackDepth} - " +
    (repaired
      ? "a zero alpha hides every window begun under it and drops them at render; pushed alpha=1 over the marker scope for this frame"
      : "healthy, no repair");

  /// <summary>The epoch key: leaked vs healthy plus the stack depth, not the exact alpha - alpha jitter among healthy frames must not log.</summary>
  private static string StyleEpochKey(RepairPlan plan) =>
    $"{(plan.ForceAlpha ? "leak" : "healthy")} depth={plan.StyleVarStackDepth}";

  private static string Fmt(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
}
