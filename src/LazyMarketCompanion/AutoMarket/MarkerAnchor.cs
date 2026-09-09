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
/// sits in the cell above - "seemingly random locations" (Helm t-joey-1788992037468).
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
}
