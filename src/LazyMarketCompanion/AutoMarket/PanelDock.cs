using System;
using System.Numerics;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness (case 161).

/// <summary>
/// The Auto-Market panel's dock arithmetic (0.2.8.11). The panel docks beside the game's inventory
/// window: to its RIGHT when the screen has room, else to its LEFT, else - and whenever the
/// inventory's rectangle cannot be read at all - it floats at a position it remembers. It must
/// NEVER silently not appear: a missing or degenerate inventory rectangle is just the floating
/// case, and a floating position that would land off-screen is clamped back inside the viewport.
///
/// Pure screen-space math: the window reads the live rectangles from the game and ImGui, this file
/// decides where the panel goes. All inputs and outputs are raw ImGui screen coordinates (the same
/// space the bag markers position their windows in, where AtkResNode screen coordinates and ImGui
/// window positions are directly comparable).
/// </summary>
public static class PanelDock
{
  /// <summary>How the panel ended up placed - the value the open log line carries.</summary>
  public enum DockMode
  {
    Right = 0,
    Left = 1,
    Floating = 2,
  }

  /// <summary>A screen-space rectangle (top-left position + size).</summary>
  public readonly record struct Rect(Vector2 Pos, Vector2 Size)
  {
    public float Left => Pos.X;
    public float Top => Pos.Y;
    public float Right => Pos.X + Size.X;
    public float Bottom => Pos.Y + Size.Y;
    public bool Valid => Size.X > 0 && Size.Y > 0;
  }

  /// <summary>Where to put the panel and what mode it ended up in. <see cref="Clamped"/> = the remembered floating position had to be pulled back on screen.</summary>
  public readonly record struct Plan(Vector2 Position, DockMode Mode, bool Clamped);

  /// <summary>Horizontal gap between the inventory window and a docked panel, in screen pixels.</summary>
  public const float Gap = 8f;

  /// <summary>
  /// Place the panel. <paramref name="dockRequested"/> = the dock-to-inventory toggle;
  /// <paramref name="inventory"/> = null or a degenerate rectangle when the inventory window's
  /// geometry is unreadable (closed counts as unreadable here - the caller only passes a rectangle
  /// for a live, visible window). Docking tries the RIGHT of the inventory, then the LEFT, then
  /// floats at <paramref name="remembered"/>, clamped into <paramref name="viewport"/> so the
  /// panel always stays reachable. The docked vertical position follows the inventory's top edge,
  /// clamped so the panel's bottom never leaves the viewport.
  /// </summary>
  public static Plan Place(
    bool dockRequested,
    Rect? inventory,
    Rect viewport,
    Vector2 panelSize,
    Vector2 remembered,
    float gap = Gap)
  {
    if (dockRequested && inventory is { } inv && inv.Valid && viewport.Valid)
    {
      // RIGHT: clear of the inventory's right edge, fully inside the viewport.
      var rightX = inv.Right + gap;
      if (rightX + panelSize.X <= viewport.Right + 0.5f)
        return new Plan(new Vector2(rightX, DockY(inv.Top, panelSize.Y, viewport)), DockMode.Right, false);

      // LEFT: panel fully inside the viewport, clear of the inventory's left edge.
      var leftX = inv.Left - gap - panelSize.X;
      if (leftX >= viewport.Left - 0.5f)
        return new Plan(new Vector2(leftX, DockY(inv.Top, panelSize.Y, viewport)), DockMode.Left, false);
    }

    // Floating (no dock, no room either side, or no readable inventory): the remembered
    // position, clamped so the whole window stays on screen.
    var maxX = Math.Max(viewport.Left, viewport.Right - panelSize.X);
    var maxY = Math.Max(viewport.Top, viewport.Bottom - panelSize.Y);
    var x = Math.Clamp(remembered.X, viewport.Left, maxX);
    var y = Math.Clamp(remembered.Y, viewport.Top, maxY);
    return new Plan(new Vector2(x, y), DockMode.Floating, x != remembered.X || y != remembered.Y);
  }

  private static float DockY(float inventoryTop, float panelHeight, Rect viewport)
  {
    var maxY = Math.Max(viewport.Top, viewport.Bottom - panelHeight);
    return Math.Clamp(inventoryTop, viewport.Top, maxY);
  }
}
