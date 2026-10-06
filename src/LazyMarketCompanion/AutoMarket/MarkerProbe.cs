using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 160c).

/// <summary>
/// The numeric marker probe (0.2.8.5). Two testing builds (0.2.8.3, 0.2.8.4) both looked perfect in
/// the suppression counters - zero cells suppressed at the logged draw - and both drew dots the
/// player could not find on their items, so counts cannot distinguish right from wrong placement.
/// The probe records the actual numbers instead: for the first resolved cells of a draw pass, the
/// walked anchor, the node's own ScreenX/ScreenY, the raw node X/Y, the scaled size, the scale, the
/// item id and the dot kind - the triplet that separates a wrong-node anchor (positions valid but
/// not the visible cell) from a draw-space offset (positions right, coordinates misinterpreted)
/// from a stale first frame (the numbers were only logged once per session before 0.2.8.5).
///
/// The logging policy lives here so the harness can hold it: the first <see cref="DrawLimit"/> draw
/// passes per (addon, container) always log, any later pass whose anchors moved logs again (a stale
/// first frame must not hide the settled one), and the whole probe is capped at
/// <see cref="LineLimit"/> lines per (addon, container) per session - a probe is not a firehose.
/// The policy is deliberately independent of the suppression counters: a pass in which nothing was
/// suppressed is exactly when the numbers matter most.
/// </summary>
public static class MarkerProbe
{
  /// <summary>The first N draw passes per (addon, container) always log a probe line, so the very first frame is on record whatever it shows.</summary>
  public const int DrawLimit = 3;

  /// <summary>How many resolved cells a probe line lists: the first K cells of the pass, suppressed ones included.</summary>
  public const int CellLimit = 8;

  /// <summary>Hard cap of probe lines per (addon, container) per session - bounded logging, no spam.</summary>
  public const int LineLimit = 8;

  /// <summary>Per-(addon, container) probe bookkeeping, held by the caller (AutoMarketMarkers).</summary>
  public sealed class State
  {
    /// <summary>How many draw passes reached the policy so far (counted even past the cap, so the draw number in the line stays true).</summary>
    public int Draws;

    /// <summary>How many probe lines this (addon, container) has logged this session.</summary>
    public int Lines;

    /// <summary>The anchor signature of the last logged pass - the on-change trigger.</summary>
    public string? LastSignature;
  }

  /// <summary>
  /// Whether this draw pass logs a probe line: true for the first <see cref="DrawLimit"/> passes,
  /// then again whenever <paramref name="signature"/> differs from the last logged one, never past
  /// <see cref="LineLimit"/> lines. Independent of the suppression counters by design.
  /// </summary>
  public static bool ShouldLog(State state, string signature)
  {
    if (state.Lines >= LineLimit)
      return false;
    state.Draws++;
    if (state.Draws <= DrawLimit)
      return true;
    return !string.Equals(state.LastSignature, signature, StringComparison.Ordinal);
  }

  /// <summary>Caller-side bookkeeping after a probe line actually logged.</summary>
  public static void RecordLogged(State state, string signature)
  {
    state.Lines++;
    state.LastSignature = signature;
  }

  /// <summary>
  /// What changed between passes: the listed cells' display slot and screen anchor. Two passes over
  /// the same anchors give the same text (a stable layout must not re-log); one cell moving changes
  /// it (the on-change trigger).
  /// </summary>
  public static string Signature(IEnumerable<(int Slot, Vector2 Screen)> cells)
  {
    var sb = new StringBuilder();
    var n = 0;
    foreach (var (slot, screen) in cells)
    {
      if (n >= CellLimit)
        break;
      if (n > 0)
        sb.Append(';');
      sb.Append(slot).Append(':').Append(Fmt(screen.X)).Append(',').Append(Fmt(screen.Y));
      n++;
    }
    return sb.ToString();
  }

  /// <summary>
  /// One cell's numbers, the whole point of the probe: walked anchor next to the node's own
  /// ScreenX/ScreenY (their disagreement is the evidence), plus raw node X/Y, scaled size, scale,
  /// item id and dot kind so the line is gradeable against what the bag actually shows.
  /// </summary>
  public static string CellLine(int slot, uint itemId, MarkerMatch.MarkKind kind, Vector2 walk, Vector2 screen, Vector2 rawXY, Vector2 scale, Vector2 size)
    => $"s{slot} id={itemId} {KindLabel(kind)} walk=({Fmt(walk.X)},{Fmt(walk.Y)}) screen=({Fmt(screen.X)},{Fmt(screen.Y)}) node=({Fmt(rawXY.X)},{Fmt(rawXY.Y)}) size=({Fmt(size.X)},{Fmt(size.Y)}) scale={Fmt(scale.X)}";

  private static string KindLabel(MarkerMatch.MarkKind kind)
    => kind switch
    {
      MarkerMatch.MarkKind.OnList => "green",
      MarkerMatch.MarkKind.MarketableNotListed => "grey",
      _ => "none",
    };

  private static string Fmt(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
