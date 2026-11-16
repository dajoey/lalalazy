using System;
using System.Collections.Generic;
using System.Numerics;

namespace LazyMarketCompanion.AutoMarket;

/// <summary>
/// Where the retainer-window overlay (Auto Pinch / Auto Market / Sweep to Bags, or Running + Stop
/// while a run is going) sits so it never covers the game window's own header buttons or the game's
/// own content in its header slot (issue #12: the venture count and the close button).
///
/// The overlay is anchored to a header node whose right edge ends at, or near, the window's close
/// button (RetainerSellList node 2: x 408, width 158 in a 598-wide window whose close button starts
/// at x 565; RetainerList node 3, the venture count: x 730, width 120 in a 930-wide window, close
/// button at x 897). It used to be laid out left-aligned at that node, so content wider than the
/// node spilled right over the close button. It is now laid out from its right edge: the anchor
/// node's right edge, pulled left of every box it must keep clear of, so wider content grows
/// leftward over empty header space.
///
/// Dalamud-free so the harness can pin it; the game reads happen in MarketAutomation.KeepClear.
/// </summary>
internal static class OverlayPlacement
{
  /// <summary>
  /// Node ids of the header buttons inside the window component (WindowA component data:
  /// close 7, config 6, help 5).
  /// </summary>
  internal static readonly uint[] HeaderButtonNodeIds = [7, 6, 5];

  /// <summary>Pixels kept between the overlay and anything it keeps clear of, before UI scale.</summary>
  internal const float Gap = 4f;

  /// <summary>
  /// Fallback when the close button node cannot be read: the window component draws it 33 px in
  /// from the window's right edge, 6 px down, 28 px square (before UI scale).
  /// </summary>
  internal const float CloseInset = 33f;
  internal const float CloseTop = 6f;
  internal const float CloseSize = 28f;

  /// <summary>A screen-space rectangle.</summary>
  internal readonly record struct Box(Vector2 Pos, Vector2 Size)
  {
    public float Left => Pos.X;
    public float Right => Pos.X + Size.X;
  }

  /// <summary>
  /// Screen x of the overlay's right edge. Starts at the anchor node's right edge; every box to keep
  /// clear of (header button or game content) that reaches right of the anchor's left edge pulls it
  /// to that box's left edge minus the gap. A box wholly left of the anchor is ignored: the overlay
  /// would only reach it with content wider than the whole header, which the retainer windows never draw.
  /// </summary>
  internal static float RightEdge(Vector2 anchor, Vector2 anchorSize, IReadOnlyList<Box> keepClear, float scale)
  {
    var right = anchor.X + anchorSize.X;
    foreach (var b in keepClear)
    {
      if (b.Right <= anchor.X)
        continue;
      right = MathF.Min(right, b.Left - (Gap * scale));
    }
    return right;
  }

  /// <summary>True when <paramref name="inner"/> lies wholly inside <paramref name="outer"/> (a header button read before the game laid it out sits at the screen origin, outside its window).</summary>
  internal static bool Inside(Box inner, Box outer)
    => inner.Size.X > 0 && inner.Size.Y > 0
       && inner.Left >= outer.Left - 0.5f && inner.Right <= outer.Right + 0.5f
       && inner.Pos.Y >= outer.Pos.Y - 0.5f && inner.Pos.Y + inner.Size.Y <= outer.Pos.Y + outer.Size.Y + 0.5f;

  /// <summary>The close button's box from the window's own box, for when the node cannot be read.</summary>
  internal static Box FallbackClose(Box window, float scale)
    => new(new Vector2(window.Right - (CloseInset * scale), window.Pos.Y + (CloseTop * scale)), new Vector2(CloseSize * scale, CloseSize * scale));
}
