using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazySkywardTracker;

/// <summary>Drawn controls for the Skyward tracker shell and its progress rows.</summary>
internal static class SkywardChrome
{
    internal static void Diamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
    {
        var points = new[]
        {
            center + new Vector2(0, -radius),
            center + new Vector2(radius, 0),
            center + new Vector2(0, radius),
            center + new Vector2(-radius, 0),
        };
        draw.AddConvexPolyFilled(ref points[0], points.Length, color);
        draw.AddCircleFilled(center, radius * 0.30f, Theme.Pack(Theme.Surface));
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##skyward_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var labelSize = ImGui.CalcTextSize(label);
        DrawText(draw, pos + ((size - labelSize) / 2f) - new Vector2(0, 1),
            FitText(label, size.X - 16), hovered ? Theme.Accent : Theme.TextPrimary);
        return clicked;
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state, string detail)
    {
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        var label = FitText($"{Copy.Word(state)} · {detail}", size.X - 38);
        DrawText(draw, pos + new Vector2(28, (size.Y - ImGui.CalcTextSize(label).Y) / 2f), label, color);
    }

    internal static void Progress(ImDrawListPtr draw, Vector2 pos, float width, float fraction, Vector4 color)
    {
        var size = new Vector2(width, Theme.BarHeight);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2f);
        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0)
            draw.AddRectFilled(pos, pos + new Vector2(width * clamped, Theme.BarHeight), Theme.Pack(color), 2f);
    }

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        if (scale == 1f)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        var width = ImGui.CalcTextSize(text).X * scale;
        DrawText(draw, new Vector2(right - width, y), text, color, scale);
    }

    internal static float TextWidth(string text, float scale = 1f) => ImGui.CalcTextSize(text).X * scale;

    internal static string FitText(string text, float width, float scale = 1f)
    {
        if (width <= 0)
            return "";
        if (TextWidth(text, scale) <= width)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length] + ellipsis, scale) > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }
}

internal enum SkywardCardTone
{
    Surface,
    Row,
}

/// <summary>Content-measured rounded cards whose backgrounds stay behind their text.</summary>
internal static class SkywardCard
{
    private const float Padding = 12;
    private const float Gap = 12;
    private static int _depth;

    internal static void Draw(float minHeight, Action content, SkywardCardTone tone = SkywardCardTone.Surface, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = width <= 0 ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);
        var topLevel = _depth == 0;
        if (topLevel)
        {
            _depth++;
            draw.ChannelsSplit(3);
        }

        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(Padding, Padding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)));
            ImGui.BeginGroup();
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + Padding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var fill = tone == SkywardCardTone.Row ? Theme.ShellBar : Theme.Surface;
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(fill), 8f);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + Gap));
        }
        finally
        {
            if (topLevel)
                _depth--;
        }
    }

    internal static void Row(Action content, float minHeight = 48)
        => Draw(minHeight, content, SkywardCardTone.Row);
}
