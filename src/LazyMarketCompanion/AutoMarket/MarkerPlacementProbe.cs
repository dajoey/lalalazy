using System.Numerics;

namespace LazyMarketCompanion.AutoMarket;

/// <summary>
/// 0.2.8.11 THE ADDON-RELATIVE PLACEMENT PROBE. Eight builds graded the marker dots against the
/// numbers the plugin's own position source produced, and every line looked right while the dots
/// still sat in the wrong place or nowhere. The one question never asked is the other side of the
/// comparison: where the game's own inventory window says it is. This probe holds a drawn dot's
/// center against the grid addon's real root-node rectangle (the game's position, scale and size
/// read the same frame) and grades it on-addon / off-addon, so a coordinate-space disagreement
/// between the game's layout and the drawn dots names itself in the log line instead of hiding
/// behind a perfect walk.
/// </summary>
public static class MarkerPlacementProbe
{
  /// <summary>Half a pixel of slack so an exact-edge landing is not graded off-addon by float noise.</summary>
  public const float RectTolerance = 0.5f;

  /// <summary>
  /// Whether <paramref name="point"/> sits within <paramref name="rectPos"/>+<paramref name="rectSize"/>
  /// (inclusive, with RectTolerance slack). A zero-size or negative-size rectangle contains nothing
  /// beyond its own corner: a missing root must not read as agreement.
  /// </summary>
  public static bool IsInsideRect(Vector2 point, Vector2 rectPos, Vector2 rectSize)
    => point.X >= rectPos.X - RectTolerance && point.Y >= rectPos.Y - RectTolerance
    && point.X <= rectPos.X + rectSize.X + RectTolerance && point.Y <= rectPos.Y + rectSize.Y + RectTolerance;

  /// <summary>The dot-vs-window verdict: true when the center lands inside the addon's own rectangle.</summary>
  public static bool OnAddon(Vector2 center, Vector2 rootPos, Vector2 rootScaledSize)
    => IsInsideRect(center, rootPos, rootScaledSize);

  /// <summary>The dot's offset from the window's own top-left corner, for the render log line.</summary>
  public static string RelText(Vector2 center, Vector2 rootPos)
    => $"rel=({center.X - rootPos.X:0.#},{center.Y - rootPos.Y:0.#})";
}
