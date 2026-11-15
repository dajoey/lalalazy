using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyGearCollector;

/// <summary>Drawn controls and content-measured cards for the collector window.</summary>
internal static class CollectorChrome
{
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

    internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 15, Theme.Pack(Theme.Accent));
        const string label = "Lazy Gear Collector";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        if (selected || hovered)
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), 2 * Theme.Unit);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(27, (size.Y - textSize.Y) / 2f), Theme.Pack(color), label);
    }

    internal static bool Toggle(string literalLabel, ref bool value, string? help)
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
        var clicked = ImGui.InvisibleButton("##gear_setting_" + literalLabel, ToggleSize);
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

        var totalHeight = rowHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        ImGui.SetCursorScreenPos(pos + new Vector2(0, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }
}

/// <summary>A rounded surface whose height follows its content; cards are never nested.</summary>
internal static class CollectorCard
{
    private const float Padding = 12;
    private const float Gap = 12;

    internal static void Draw(Action content, float minHeight = 0, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = width <= 0 ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);

        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(origin + new Vector2(Padding, Padding));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)));
        ImGui.BeginGroup();
        content();
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var contentBottom = ImGui.GetCursorScreenPos().Y;
        var height = MathF.Max(minHeight, contentBottom - origin.Y + Padding);

        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        draw.ChannelsMerge();
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height + Gap));
    }
}
