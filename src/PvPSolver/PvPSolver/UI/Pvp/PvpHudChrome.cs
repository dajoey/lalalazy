using System.Numerics;
using LalaUiKit;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Fork-only frame of the compact HUD windows (control and cooldown): the suite's 1 px border and 8 px radius, and the
///     slim strip that moves an unlocked window now that the native title bar is gone. No band, footer or close glyph:
///     these are combat overlays whose visibility follows the configuration toggle.
/// </summary>
internal static class PvpHudChrome
{
    internal const float Radius = 8f;

    /// <summary>
    ///     Draws the drag strip along the top edge of the current window and moves the window while it is dragged. Nothing
    ///     is drawn for a locked window. The strip lies inside the window's top padding, so it never covers content, and the
    ///     cursor is put back where the window's own content starts.
    /// </summary>
    internal static void DragStrip(bool locked)
    {
        if (locked)
        {
            return;
        }

        Vector2 start = ImGui.GetCursorPos();
        Vector2 winPos = ImGui.GetWindowPos();
        float width = ImGui.GetWindowWidth();
        float height = Math.Clamp(start.Y, 6f, 12f);
        ImGui.SetCursorPos(new Vector2(Radius, 1f));
        _ = ImGui.InvisibleButton("##hudDrag", new Vector2(MathF.Max(1f, width - (2 * Radius)), height - 1f));
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
        {
            ImGui.SetWindowPos(winPos + ImGui.GetIO().MouseDelta);
        }

        if (hovered || active)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }

        Vector2 center = winPos + new Vector2(width / 2f, 1f + ((height - 1f) / 2f));
        Vector4 color = active || hovered ? Theme.Accent : Theme.TextSecondary with { W = 0.55f };
        ImGui.GetWindowDrawList().AddRectFilled(center - new Vector2(14f, 1.5f), center + new Vector2(14f, 1.5f), Theme.Pack(color), 1.5f);
        ImGui.SetCursorPos(start);
    }
}
