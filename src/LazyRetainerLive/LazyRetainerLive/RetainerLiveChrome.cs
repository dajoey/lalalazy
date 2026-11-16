using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyRetainerLive;

/// <summary>Plugin-local drawn controls for the approved LazyRetainerLive shell.</summary>
internal static class RetainerLiveChrome
{
    private static readonly Vector2 ToggleSize = new(44, 24);

    public static void Diamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
    {
        var points = new[]
        {
            center + new Vector2(0, -radius),
            center + new Vector2(radius, 0),
            center + new Vector2(0, radius),
            center + new Vector2(-radius, 0),
        };
        draw.AddConvexPolyFilled(ref points[0], points.Length, color);
        draw.AddCircleFilled(center, radius * 0.32f, Theme.Pack(Theme.Surface));
    }

    public static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 15, Theme.Pack(Theme.Accent));
        const string label = "LazyRetainerLive";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1),
            Theme.Pack(Theme.TextPrimary),
            label);
    }

    public static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(27, (size.Y - textSize.Y) / 2f), Theme.Pack(color), label);
    }

    public static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##retainerlive_close", size);
        var hovered = ImGui.IsItemHovered();
        var fill = hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);

        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        const float extent = 6;
        draw.AddLine(center + new Vector2(-extent, -extent), center + new Vector2(extent, extent), color, 1.5f);
        draw.AddLine(center + new Vector2(-extent, extent), center + new Vector2(extent, -extent), color, 1.5f);
        if (hovered)
            ImGui.SetTooltip("Close");
        return clicked;
    }

    public static bool Toggle(string literalLabel, ref bool value, string? help)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        var labelSize = ImGui.CalcTextSize(literalLabel, false, labelWidth);
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var helpHeight = string.IsNullOrWhiteSpace(help)
            ? 0
            : ImGui.CalcTextSize(help, false, MathF.Max(20, width / Theme.CaptionScale)).Y * Theme.CaptionScale;
        var togglePos = pos + new Vector2(width - ToggleSize.X, 0);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##retainerlive_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;

        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(
            togglePos,
            togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder),
            ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(
            togglePos + new Vector2(value ? 32 : 12, 12),
            8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1
            ? (ToggleSize.Y - labelSize.Y) / 2f
            : 0;
        ImGui.SetCursorScreenPos(pos + new Vector2(0, labelY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
        ImGui.TextUnformatted(literalLabel);
        ImGui.PopTextWrapPos();

        if (helpHeight > 0)
        {
            ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight + Theme.Unit));
            ImGui.SetWindowFontScale(Theme.CaptionScale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(help!);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
        }

        var totalHeight = rowHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        ImGui.SetCursorScreenPos(pos + new Vector2(0, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    /// <summary>A content-measured rounded card. Background and content use separate channels.</summary>
    public static void Card(Action content, float minHeight = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float padding = 12;
        const float gap = 12;

        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, width - (2 * padding)));
        ImGui.BeginGroup();
        content();
        ImGui.EndGroup();
        ImGui.PopTextWrapPos();
        var contentBottom = ImGui.GetCursorScreenPos().Y;
        var height = MathF.Max(minHeight, contentBottom - origin.Y + padding);

        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
        draw.ChannelsMerge();
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height + gap));
    }

    public static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, bool enabled)
    {
        ImGui.SetCursorScreenPos(pos);
        if (!enabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton("##retainerlive_stop", size);
        if (!enabled)
            ImGui.EndDisabled();

        var hovered = enabled && ImGui.IsItemHovered();
        var fill = enabled
            ? hovered ? Theme.StateRed with { W = 0.82f } : Theme.StateRed with { W = 0.68f }
            : Theme.Surface;
        var border = enabled ? Theme.StateRed : Theme.SurfaceBorder;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(border), 2 * Theme.Unit);
        var label = "Stop listener";
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(
            pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(enabled ? Theme.TextPrimary : Theme.TextSecondary),
            label);
        return enabled && clicked;
    }

    public static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    public static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length == 0)
            return;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
    }

    public static string FitText(string text, float width, float scale = 1)
    {
        if (width <= 0)
            return "";
        if (ImGui.CalcTextSize(text).X * scale <= width)
            return text;

        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }
}
