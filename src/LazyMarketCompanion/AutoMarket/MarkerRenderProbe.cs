using System;
using System.Globalization;
using System.Numerics;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 160g).

/// <summary>
/// The render-side probe (0.2.8.8). Five testing builds measured the markers' numbers BEFORE the
/// draw - walked anchors, the node's own screen position, suppression counters, display-order
/// stability - and every line looked perfect, while the player kept reporting missing or stray
/// dots. Nothing measured what the renderer actually DID with a dot window: the size ImGui really
/// gave it (the 0.2.8.7 review: an empty AlwaysAutoResize window floors at 4x4 in imgui.cpp
/// CalcWindowMinSize, and without that flag the style's own WindowMinSize floors it instead - the
/// requested size is a floor, never a promise), the clip rect the circle is finally cut by, and
/// whether the circle's vertices were added at all. This probe grades exactly that, from the
/// numbers read out of the live window after the circle was drawn: is the circle fully inside the
/// window rect and its clip rect, and did the draw list gain vertices.
///
/// The logging policy lives here so the harness can hold it: the first <see cref="PassLimit"/>
/// passes per (addon, container) measure and log one line each, capped - a probe is not a
/// firehose. Like the numeric probe (MarkerProbe), the policy is deliberately independent of the
/// suppression counters: a pass in which nothing was suppressed is exactly when the render side
/// matters most.
/// </summary>
public static class MarkerRenderProbe
{
  /// <summary>The first N draw passes per (addon, container) measure and log the render side.</summary>
  public const int PassLimit = 3;

  /// <summary>
  /// How many eras a (addon, container) may measure per session: each era is <see cref="PassLimit"/>
  /// passes, re-opened by <see cref="ReArm"/> when the grid's window is re-created. 0.2.8.9 left
  /// the re-open era unmeasured exactly because the pass count never reset - the vanish moment was
  /// blind. The session-wide bound is PassLimit * EraLimit lines, never lifted by a re-arm.
  /// </summary>
  public const int EraLimit = 3;

  /// <summary>How many drawn dots a render line grades: the first K dots of the pass that actually drew.</summary>
  public const int DotLimit = 5;

  /// <summary>Per-(addon, container) render-probe bookkeeping, held by the caller (AutoMarketMarkers).</summary>
  public sealed class State
  {
    /// <summary>How many passes reached the policy so far.</summary>
    public int Passes;

    /// <summary>How many render lines this (addon, container) has logged this session.</summary>
    public int Lines;
  }

  /// <summary>Whether this pass measures and logs the render side: true for the first <see cref="PassLimit"/> passes of the current era, never past the session-wide bound of <see cref="PassLimit"/> * <see cref="EraLimit"/> lines (the caps keep the gates honest even if a pass never logged).</summary>
  public static bool ShouldMeasure(State state)
  {
    if (state.Passes >= PassLimit || state.Lines >= PassLimit * EraLimit)
      return false;
    state.Passes++;
    return true;
  }

  /// <summary>
  /// Open a new measurement era: called when the grid's window was re-created (the transition log
  /// saw a closed/not-ready boundary), so the re-opened addon's first passes are measured again.
  /// Lines keep their session-wide bound - a re-arm never lifts it.
  /// </summary>
  public static void ReArm(State state) => state.Passes = 0;

  /// <summary>Caller-side bookkeeping after a render line actually logged.</summary>
  public static void RecordLogged(State state) => state.Lines++;

  /// <summary>Whether a drawn dot's render numbers say it could appear, and if not, what cut it.</summary>
  public enum Verdict
  {
    /// <summary>The circle is fully inside the window rect and its clip rect, and the draw list gained vertices.</summary>
    Ok,
    /// <summary>The circle extends past the clip rect - the rect that actually culls the pixels.</summary>
    OutsideClip,
    /// <summary>The circle extends past the window rect itself (the clip check catches this first in practice; kept as the honest container gate).</summary>
    OutsideWindow,
    /// <summary>The draw list gained no vertices - the circle was never added at all.</summary>
    NoVertices,
  }

  /// <summary>
  /// What the renderer's own numbers say about one drawn dot. <paramref name="vertexCount"/> is the
  /// window draw list's vertex count after the circle was added - ImGui applies clip rects at
  /// render time, so a fully clipped circle still carries vertices: the geometry checks, not the
  /// count, catch clipping; the count catches a circle that was never added at all.
  /// </summary>
  public static Verdict Check(Vector2 windowPos, Vector2 windowSize, Vector2 clipMin, Vector2 clipMax, int vertexCount, Vector2 circleCenter, float circleRadius)
  {
    if (vertexCount <= 0)
      return Verdict.NoVertices;
    var min = circleCenter - new Vector2(circleRadius, circleRadius);
    var max = circleCenter + new Vector2(circleRadius, circleRadius);
    if (min.X < clipMin.X || min.Y < clipMin.Y || max.X > clipMax.X || max.Y > clipMax.Y)
      return Verdict.OutsideClip;
    if (min.X < windowPos.X || min.Y < windowPos.Y || max.X > windowPos.X + windowSize.X || max.Y > windowPos.Y + windowSize.Y)
      return Verdict.OutsideWindow;
    return Verdict.Ok;
  }

  public static string VerdictLabel(Verdict verdict) => verdict switch
  {
    Verdict.Ok => "ok",
    Verdict.OutsideClip => "clipped",
    Verdict.OutsideWindow => "outside window",
    _ => "no vertices",
  };

  /// <summary>One dot's measured render numbers for the log line.</summary>
  public static string DotLine(int slot, Verdict verdict, Vector2 windowPos, Vector2 windowSize, Vector2 clipMin, Vector2 clipMax, int vertexCount, Vector2 circleCenter, float circleRadius)
    => $"s{slot} {VerdictLabel(verdict)} win=({Fmt(windowPos.X)},{Fmt(windowPos.Y)})+({Fmt(windowSize.X)},{Fmt(windowSize.Y)}) clip=({Fmt(clipMin.X)},{Fmt(clipMin.Y)})-({Fmt(clipMax.X)},{Fmt(clipMax.Y)}) vtx={vertexCount} circle=({Fmt(circleCenter.X)},{Fmt(circleCenter.Y)}) r={Fmt(circleRadius)}";

  private static string Fmt(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
