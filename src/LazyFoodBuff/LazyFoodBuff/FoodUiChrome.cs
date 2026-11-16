using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyFoodBuff;

/// <summary>Plugin-local drawn controls for the food automation window.</summary>
internal static class FoodUiChrome
{
    private static readonly Vector2 ToggleSize = new(44, 24);

    internal static void PushInputStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.40f });
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Theme.Accent with { W = 0.30f });
    }

    internal static void PopInputStyle()
    {
        ImGui.PopStyleColor(10);
        ImGui.PopStyleVar();
    }

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

    internal static Vector2 StatusPillSize(string label, string? detail)
    {
        var text = string.IsNullOrEmpty(detail) ? label : $"{label} · {detail}";
        return new Vector2(MathF.Max(78, ImGui.CalcTextSize(text).X + 43), 34);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, string label, string? detail, UiState state)
    {
        var size = StatusPillSize(label, detail);
        var color = Copy.MarkColor(state);
        var text = string.IsNullOrEmpty(detail) ? label : $"{label} · {detail}";
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        var textSize = ImGui.CalcTextSize(text);
        draw.AddText(pos + new Vector2(27, (size.Y - textSize.Y) / 2f), Theme.Pack(color), text);
    }

    internal static bool ChoiceButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##lfb_choice_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##lfb_button_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool StopButton(Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.StateRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.StateRed with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.StateRed with { W = 0.70f });
        try
        {
            return ImGui.Button("Stop##lfb_stop", size);
        }
        finally
        {
            ImGui.PopStyleColor(3);
        }
    }

    internal static bool Toggle(string literalLabel, ref bool value, string? help = null)
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
        var clicked = ImGui.InvisibleButton("##lfb_setting_" + literalLabel, ToggleSize);
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
        ImGui.TextWrapped(literalLabel);
        ImGui.PopTextWrapPos();

        if (helpHeight > 0)
        {
            ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight + Theme.Unit));
            ImGui.SetWindowFontScale(Theme.CaptionScale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextWrapped(help);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
        }

        var totalHeight = rowHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        ImGui.SetCursorScreenPos(pos + new Vector2(0, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    internal static void SectionTitle(string title, string? description = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var titleSize = ImGui.CalcTextSize(title) * Theme.TitleScale;
        Diamond(draw, origin + new Vector2(6, titleSize.Y / 2f), 5, Theme.Pack(Theme.Accent));
        DrawText(draw, origin + new Vector2(20, 0), title, Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, MathF.Max(25, titleSize.Y + 5)));
        if (!string.IsNullOrWhiteSpace(description))
            Widgets.SecondaryWrapped(description);
    }

    internal static void Requirement(string label, string value, UiState state)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        var color = Copy.MarkColor(state);
        draw.AddCircleFilled(origin + new Vector2(5, lineHeight / 2f), Theme.DotRadius, Theme.Pack(color));
        DrawText(draw, origin + new Vector2(17, 0), label, Theme.TextSecondary);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 45 <= width)
        {
            DrawTextRight(draw, origin.X + width, origin.Y, value, color);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, lineHeight + Theme.ItemSpacing.Y));
        }
        else
        {
            DrawText(draw, origin + new Vector2(17, lineHeight + Theme.Unit), value, color);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, (2 * lineHeight) + Theme.Unit + Theme.ItemSpacing.Y));
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
        if (text.Length != 0)
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
}

/// <summary>Content-measured rounded cards; their backgrounds never cover their contents.</summary>
internal static class FoodCard
{
    private const float Padding = 12;
    private const float Gap = 12;
    private static int _depth;

    internal static void Draw(string id, Action content, float width = 0)
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

        ImGui.PushID(id);
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
            var height = MathF.Max(48, contentBottom - origin.Y + Padding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + Gap));
        }
        finally
        {
            ImGui.PopID();
            if (topLevel)
                _depth--;
        }
    }
}
