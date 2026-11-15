using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyFishSitter;

internal readonly record struct FishSitterStat(string Label, string Value, string Note);

/// <summary>Drawn controls for the plugin's single-area settings shell.</summary>
internal static class FishSitterChrome
{
    private const float CardPadding = 12;
    private const float CardGap = 12;
    private static readonly Vector2 ToggleSize = new(44, 24);

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
        draw.AddCircleFilled(center, radius * 0.32f, Theme.Pack(Theme.Surface));
    }

    internal static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 14, Theme.Pack(Theme.Accent));
        var label = "LazyFishSitter";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        DrawText(draw, pos + new Vector2(48, ((size.Y - textSize.Y) / 2f) - 1), label, Theme.TextPrimary, Theme.TitleScale);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var color = Copy.MarkColor(state);
        var label = Copy.Word(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(27, (size.Y - textSize.Y) / 2f), Theme.Pack(color), label);
    }

    internal static void FeatureTab(ImDrawListPtr draw, string label, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), 2 * Theme.Unit);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(Theme.Accent), 12, 1);
        draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.Accent), label);
    }

    internal static void SectionTitle(string title)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var textHeight = ImGui.CalcTextSize(title).Y * Theme.TitleScale;
        Diamond(draw, origin + new Vector2(6, textHeight / 2f), 5, Theme.Pack(Theme.Accent));
        DrawText(draw, origin + new Vector2(18, 0), title, Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, MathF.Max(20, textHeight) + Theme.Unit));
    }

    /// <summary>A content-measured rounded card. The background is painted behind all content.</summary>
    internal static void Card(Action<float> content, float width = 0, float minHeight = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = width <= 0 ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);
        var contentWidth = MathF.Max(1, cardWidth - (2 * CardPadding));
        draw.ChannelsSplit(2);
        var height = minHeight;
        try
        {
            draw.ChannelsSetCurrent(1);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentWidth);
            ImGui.BeginGroup();
            content(contentWidth);
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);

            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        }
        finally
        {
            draw.ChannelsMerge();
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height + CardGap));
    }

    internal static void StatCards(params FishSitterStat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 620;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
        const float height = 104;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            var size = new Vector2(width, height);
            draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            Diamond(draw, pos + new Vector2(18, 20), 5, Theme.Pack(Theme.Accent));
            DrawText(draw, pos + new Vector2(32, 12), FitText(stat.Label, width - 44, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            DrawText(draw, pos + new Vector2(12, 36), FitText(stat.Value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, pos + new Vector2(12, 78), FitText(stat.Note, width - 24, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        }
        var totalHeight = stacked ? (stats.Length * height) + ((stats.Length - 1) * gap) : height;
        ImGui.SetCursorScreenPos(origin + new Vector2(0, totalHeight + gap));
    }

    internal static bool Toggle(string literalLabel, ref bool value, string? help, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        var labelSize = ImGui.CalcTextSize(literalLabel, false, labelWidth);
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var helpHeight = string.IsNullOrWhiteSpace(help)
            ? 0
            : ImGui.CalcTextSize(help, false, width / Theme.CaptionScale).Y * Theme.CaptionScale;
        var togglePos = pos + new Vector2(width - ToggleSize.X, 0);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##fish_sitter_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1 ? (ToggleSize.Y - labelSize.Y) / 2f : 0;
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
            ImGui.TextUnformatted(help);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
        }

        var totalHeight = rowHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        ImGui.SetCursorScreenPos(pos + new Vector2(0, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    internal static bool ActionButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool danger)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fish_sitter_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var accent = danger ? Theme.StateRed : Theme.Accent;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(accent with { W = danger ? 0.12f : 0.14f }), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.TextPrimary : accent), 2 * Theme.Unit);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1), Theme.Pack(accent), label);
        return clicked;
    }

    internal static void KeyValue(string label, string value, float width)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var labelSize = ImGui.CalcTextSize(label);
        var valueSize = ImGui.CalcTextSize(value);
        if (labelSize.X + valueSize.X + 36 <= width)
        {
            DrawText(draw, origin, label, Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y, value, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, MathF.Max(labelSize.Y, valueSize.Y) + Theme.ItemSpacing.Y));
            return;
        }

        ImGui.SetCursorScreenPos(origin);
        Widgets.Secondary(label);
        Widgets.Caption(value);
    }

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        var width = ImGui.CalcTextSize(text).X * scale;
        DrawText(draw, new Vector2(right - width, y), text, color, scale);
    }

    internal static string FitText(string text, float width, float scale = 1)
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
