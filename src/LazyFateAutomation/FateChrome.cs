using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyFateAutomation;

/// <summary>Drawn controls and content-measured cards for the FATE window.</summary>
internal static class FateChrome
{
    internal const float ControlHeight = 34f;
    private const float CardPadding = 12f;
    private const float CardGap = 12f;
    private static readonly Vector2 ToggleSize = new(44, 24);
    private static int _cardDepth;

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

    internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 15, Theme.Pack(Theme.Accent));
        const string label = "LazyFateAutomation";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
        var fitted = FitText(label, size.X - 16);
        var textSize = ImGui.CalcTextSize(fitted);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), fitted);
        return clicked;
    }

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
        if (selected || hovered)
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), 8f);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var fitted = FitText(label, size.X - 42);
        var textSize = ImGui.CalcTextSize(fitted);
        draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), fitted);
        return clicked;
    }

    internal static bool DangerButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = Theme.StateRed with { W = hovered ? 0.85f : 1f };
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
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

    internal static bool Toggle(string id, string literalLabel, ref bool value, string? help = null, string? tooltip = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        var labelSize = ImGui.CalcTextSize(literalLabel, false, labelWidth);
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var helpHeight = string.IsNullOrWhiteSpace(help)
            ? 0
            : ImGui.CalcTextSize(help, false, width / Theme.CaptionScale).Y * Theme.CaptionScale;
        var togglePos = pos + new Vector2(width - ToggleSize.X, 0);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##fate_setting_" + id, ToggleSize);
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
            ImGui.TextUnformatted(help);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
        }

        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);

        var totalHeight = rowHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        ImGui.SetCursorScreenPos(pos + new Vector2(0, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    internal static void Card(string id, Action content, float minHeight = 0, float width = 0, bool row = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = width <= 0 ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
            ImGui.PushID(id);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            ImGui.PopID();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(row ? Theme.ShellBar : Theme.Surface), 8f);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + CardGap));
        }
        finally
        {
            if (topLevel)
                _cardDepth--;
        }
    }

    internal static void TwoColumns(Action work, Action rail, float leftFraction = 0.66f)
    {
        if (ImGui.GetContentRegionAvail().X >= 720)
        {
            using var columns = Layout.TwoColumns.Begin(leftFraction);
            work();
            columns.Split();
            rail();
        }
        else
        {
            work();
            rail();
        }
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

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length == 0)
            return;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
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

    internal readonly record struct Stat(string Label, string Value, string Note, float? Fraction = null);

    internal static void StatCards(params Stat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 620;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
        const float height = 96;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            DrawText(draw, pos + new Vector2(36, 12), FitText(stat.Label, width - 48, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            DrawText(draw, pos + new Vector2(12, 34), FitText(stat.Value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 64);
                var max = pos + new Vector2(width - 12, 68);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0), Theme.Pack(Theme.StateGreen), 2f);
            }
            DrawText(draw, pos + new Vector2(12, 76), FitText(stat.Note, width - 24, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
    }
}
