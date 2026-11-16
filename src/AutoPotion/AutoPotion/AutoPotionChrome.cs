using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace AutoPotion;

/// <summary>Drawn controls for AutoPotion's settings shell and rounded cards.</summary>
internal static class AutoPotionChrome
{
    private const float CardPadding = 12f;
    private const float CardGap = 12f;
    private static readonly Vector2 ToggleSize = new(44f, 24f);

    internal static void Diamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
    {
        var points = new[]
        {
            center + new Vector2(0f, -radius),
            center + new Vector2(radius, 0f),
            center + new Vector2(0f, radius),
            center + new Vector2(-radius, 0f),
        };
        draw.AddConvexPolyFilled(ref points[0], points.Length, color);
        draw.AddCircleFilled(center, radius * 0.32f, Theme.Pack(Theme.Surface));
    }

    internal static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2f * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2f * Theme.Unit);
        Diamond(draw, pos + new Vector2(23f, size.Y / 2f), 15f, Theme.Pack(Theme.Accent));
        const string label = "AutoPotion";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50f, ((size.Y - textSize.Y) / 2f) - 1f), Theme.Pack(Theme.TextPrimary), label);
    }

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##autopotion_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2f * Theme.Unit);
        if (selected || hovered)
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), 2f * Theme.Unit);
        var marker = pos + new Vector2(16f, size.Y / 2f);
        draw.AddCircle(marker, 6f, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1f);
        if (selected)
            draw.AddCircleFilled(marker, 3f, Theme.Pack(Theme.Accent));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(30f, ((size.Y - textSize.Y) / 2f) - 1f),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##autopotion_close", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), 2f * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2f * Theme.Unit);
        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextPrimary);
        draw.AddLine(center + new Vector2(-6f, -6f), center + new Vector2(6f, 6f), color, 1.5f);
        draw.AddLine(center + new Vector2(-6f, 6f), center + new Vector2(6f, -6f), color, 1.5f);
        if (hovered)
            ImGui.SetTooltip("Close");
        return clicked;
    }

    internal static Vector2 StatusSize(UiState state)
    {
        var label = Copy.Word(state);
        return new Vector2(MathF.Max(76f, ImGui.CalcTextSize(label).X + 43f), 34f);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var label = Copy.Word(state);
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15f, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(27f, (size.Y - textSize.Y) / 2f), Theme.Pack(color), label);
    }

    internal static bool Toggle(string id, string literalLabel, ref bool value, string? help = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var labelWidth = MathF.Max(20f, width - ToggleSize.X - (3f * Theme.Unit));
        var labelSize = ImGui.CalcTextSize(literalLabel, false, labelWidth);
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var helpHeight = string.IsNullOrWhiteSpace(help)
            ? 0f
            : ImGui.CalcTextSize(help, false, width / Theme.CaptionScale).Y * Theme.CaptionScale;
        var togglePos = pos + new Vector2(width - ToggleSize.X, 0f);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##autopotion_setting_" + id, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32f : 12f, 12f), 8f,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1f
            ? (ToggleSize.Y - labelSize.Y) / 2f
            : 0f;
        ImGui.SetCursorScreenPos(pos + new Vector2(0f, labelY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
        ImGui.TextUnformatted(literalLabel);
        ImGui.PopTextWrapPos();

        if (helpHeight > 0f)
        {
            ImGui.SetCursorScreenPos(pos + new Vector2(0f, rowHeight + Theme.Unit));
            ImGui.SetWindowFontScale(Theme.CaptionScale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(help);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
        }

        var totalHeight = rowHeight + (helpHeight > 0f ? Theme.Unit + helpHeight : 0f);
        ImGui.SetCursorScreenPos(pos + new Vector2(0f, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    internal static bool ThresholdSlider(string id, string label, ref float value)
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        var valueText = $"{value:F0}%";
        var valueWidth = ImGui.CalcTextSize(valueText).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - valueWidth));
        Widgets.Secondary(valueText);

        ImGui.SetNextItemWidth(MathF.Max(1f, width));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
        var changed = ImGui.SliderFloat("##autopotion_threshold_" + id, ref value, 1f, 99f, "%.0f%%");
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar();
        return changed;
    }

    /// <summary>A content-measured rounded card; its background is drawn behind the completed content.</summary>
    internal static void Card(string id, Action content, float width = 0f, float minHeight = 0f)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = width <= 0f ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);
        draw.ChannelsSplit(2);
        try
        {
            draw.ChannelsSetCurrent(1);
            ImGui.PushID(id);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1f, cardWidth - (2f * CardPadding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            ImGui.PopID();

            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);
            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 2f * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 2f * Theme.Unit);
            ImGui.SetCursorScreenPos(origin + new Vector2(0f, height + CardGap));
        }
        finally
        {
            draw.ChannelsMerge();
        }
    }

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##autopotion_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.StateRed with { W = 0.85f } : Theme.StateRed), 2f * Theme.Unit);
        const string label = "Stop";
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0f, 1f), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }
}
