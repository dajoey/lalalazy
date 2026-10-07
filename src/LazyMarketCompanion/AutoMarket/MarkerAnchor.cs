using System.Numerics;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 53).

/// <summary>
/// The screen-space anchor for one bag-slot marker dot: where the dot's CENTER sits inside the
/// slot's cell, and where the ImGui window that draws it is placed.
///
/// Until 0.1.30.0 the draw code placed the marker window at the cell's top-right corner minus the
/// corner inset in BOTH axes (top - inset), so with the zeroed window padding the dot's center
/// landed at (right - inset + radius, top - inset + radius) = 2.5 px ABOVE the cell's top edge,
/// with most of the circle outside the cell. In the expanded inventory the four E-grids are
/// stacked, so a top-row dot visually landed on the bottom row of the grid above it (a different
/// bag) or on the window header, and in sparse bags the dot read as attached to whatever item
/// sits in the cell above - "seemingly random locations" (the related support thread).
///
/// Since 0.1.31.0 the dot is anchored INSIDE the cell: the center sits Inset px in from the
/// cell's right edge and Inset px below its top edge, and the window is placed one radius
/// up-left of the center so the circle is exactly inscribed in it.
/// </summary>
public static class MarkerAnchor
{
  /// <summary>Dot radius, in game-scaled pixels (mirrored by AutoMarketMarkers.DotRadius).</summary>
  public const float Radius = 4.5f;

  /// <summary>Distance from the cell's right/top edge to the dot's center, in game-scaled pixels (mirrored by AutoMarketMarkers.CornerInset).</summary>
  public const float Inset = 7f;

  /// <summary>
  /// The dot's CENTER for a cell whose top-left screen position is <paramref name="cellPosition"/>
  /// and whose scaled size is <paramref name="cellSize"/>: inset from the right edge and inset
  /// below the top edge - inside the cell, never hanging off its top-right corner.
  /// </summary>
  public static Vector2 Center(Vector2 cellPosition, Vector2 cellSize)
    => cellPosition + new Vector2(cellSize.X - Inset, Inset);

  /// <summary>
  /// The top-left screen position for the marker ImGui window (the draw code zeroes
  /// WindowPadding/WindowBorderSize): one dot radius up-left of <see cref="Center"/>, so the
  /// drawn circle is exactly inscribed in the window.
  /// </summary>
  public static Vector2 WindowPosition(Vector2 cellPosition, Vector2 cellSize)
    => Center(cellPosition, cellSize) - new Vector2(Radius, Radius);

  /// <summary>
  /// The corner-diagnostic window (0.2.8.6). A dot whose CENTER lands within this many pixels of
  /// the viewport's top-left corner is the shape the 2026-10-06 report calls the stray corner dot.
  /// The origin gate only suppresses cells resolving inside <see cref="Inset"/> of the origin in
  /// BOTH axes, so a genuinely laid-out cell near the corner draws - and must say so: the only
  /// green circle this plugin draws anywhere is the marker dot, so a corner dot WITH no such log
  /// line is another plugin's overlay, and one WITH the line names its own source.
  /// </summary>
  public const float CornerReportInset = 64f;

  /// <summary>Whether a drawn dot's center sits inside the corner-report window (top-left corner).</summary>
  public static bool IsNearScreenOrigin(Vector2 center)
    => center.X < CornerReportInset && center.Y < CornerReportInset;

  /// <summary>
  /// Whether a dot may be drawn for a cell that resolved to this screen position and size
  /// (0.2.8.3). The unpositioned-node signature - the state behind the 2026-10-06 report where
  /// every dot window stacked at the viewport origin and read as one stray dot in the screen's
  /// top-left corner - is a top-left within the corner inset of the origin in BOTH axes. No
  /// visible bag or retainer cell sits that close to the screen corner on both axes (the
  /// windows have headers and borders), and a cell whose top-left hangs fully off the top-left
  /// corner has no visible icon region for the dot to sit on anyway. Fail closed: a missing
  /// dot is better than a wrong one (the GridMap contract). This decides WHETHER a dot draws,
  /// never where - the anchor of a drawable cell is unchanged (case 53).
  /// </summary>
  public static bool IsResolvableCell(Vector2 cellPosition, Vector2 cellSize)
  {
    if (cellSize.X <= 0 || cellSize.Y <= 0)
      return false;
    if (cellPosition.X < Inset && cellPosition.Y < Inset)
      return false;
    return true;
  }

  /// <summary>
  /// How far a walked anchor may sit from the node's own ScreenX/ScreenY and still count as the
  /// game-confirmed position, in screen pixels.
  /// </summary>
  public const float ScreenTolerance = 2f;

  /// <summary>Two anchors this close in BOTH axes are the same spot on screen (stacked dots), not two cells.</summary>
  public const float DuplicateTolerance = 1f;

  /// <summary>
  /// Whether the game's own layout agrees that a cell walked to <paramref name="walkedPosition"/>:
  /// AtkResNode.ScreenX/ScreenY is the top-left the game itself computed for the node this frame
  /// (the same numbers the PvPSolver hotbar overlay draws from), and on a live positioned cell
  /// the parent-walk reproduces them to a rounding error. A walked anchor further than
  /// <see cref="ScreenTolerance"/> from them means the walk started from a node the game did not
  /// lay out at that position - 0.2.8.3's shape, where every classified cell passed the origin
  /// gate, drew, and no dot sat on its icon. A node not laid out this frame carries the zero
  /// signature, which never agrees with a real walked anchor and suppresses the dot. Fail closed
  /// with the origin gate: a missing dot is better than a wrong one. Decides WHETHER a dot draws,
  /// never where (case 53).
  /// </summary>
  public static bool IsScreenConfirmed(Vector2 walkedPosition, Vector2 screenPosition)
    => Math.Abs(walkedPosition.X - screenPosition.X) <= ScreenTolerance
    && Math.Abs(walkedPosition.Y - screenPosition.Y) <= ScreenTolerance;

  /// <summary>
  /// Whether two anchors are the same screen spot: no visible grid ever shows two cells at one
  /// position, so a position source that hands several cells the same anchor (one shared or
  /// template node the game still positions) is not resolving cells at all - it stacks every dot
  /// window on one spot, which reads as one stray dot exactly like the origin signature did.
  /// The first cell keeps the spot; the rest draw nothing.
  /// </summary>
  public static bool IsDuplicateAnchor(Vector2 position, Vector2 drawn)
    => Math.Abs(position.X - drawn.X) <= DuplicateTolerance
    && Math.Abs(position.Y - drawn.Y) <= DuplicateTolerance;
}
