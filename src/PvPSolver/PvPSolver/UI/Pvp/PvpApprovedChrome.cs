using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;

namespace RotationSolver.UI.Pvp;

/// <summary>Fork-local drawn controls for the approved PvP Solver settings shell.</summary>
internal static class PvpApprovedChrome
{
    internal const float ControlHeight = 34f;
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
        draw.AddCircleFilled(center, radius * 0.30f, Theme.Pack(Theme.Surface));
    }

    internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_approved_brand", size);
        bool hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 14, Theme.Pack(Theme.Accent));
        const string label = "PvP Solver";
        Vector2 textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(48, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_approved_" + id, size);
        bool hovered = ImGui.IsItemHovered();
        Vector4 fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_approved_feature_" + id, size);
        bool hovered = ImGui.IsItemHovered();
        Vector4 fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        if (selected || hovered)
        {
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), 2 * Theme.Unit);
        }

        Vector2 marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
        {
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        }

        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        Vector4 color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(27, (size.Y - textSize.Y) / 2f), Theme.Pack(color), label);
    }

    internal static bool ChoiceButton(string id, string label, bool selected)
    {
        Vector2 size = new(MathF.Max(64, ImGui.CalcTextSize(label).X + 28), 30);
        return OutlineButton(ImGui.GetWindowDrawList(), "choice_" + id, label, ImGui.GetCursorScreenPos(), size, selected);
    }

    internal static bool Button(string label, bool disabled = false, bool danger = false, bool small = false, Vector2? size = null)
    {
        using var off = ImRaii.Disabled(disabled);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        using var normal = ImRaii.PushColor(ImGuiCol.Button, danger ? Theme.StateRed : Theme.Surface);
        using var hovered = ImRaii.PushColor(ImGuiCol.ButtonHovered, danger ? Theme.StateRed with { W = 0.85f } : Theme.SurfaceBorder);
        using var active = ImRaii.PushColor(ImGuiCol.ButtonActive, danger ? Theme.StateRed with { W = 0.70f } : Theme.AccentActive);
        bool clicked = small ? ImGui.SmallButton(label) : ImGui.Button(label, size ?? Vector2.Zero);
        return clicked && !disabled;
    }

    /// <summary>A labeled setting row with a right-aligned pink track. The caller retains the binding and save semantics.</summary>
    internal static bool Toggle(string literalLabel, ref bool value)
    {
        Vector2 pos = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        float labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        Vector2 labelSize = ImGui.CalcTextSize(literalLabel, false, labelWidth);
        float rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        Vector2 togglePos = pos + new Vector2(width - ToggleSize.X, 0);

        ImGui.SetCursorScreenPos(togglePos);
        bool clicked = ImGui.InvisibleButton("##pvp_approved_setting_" + literalLabel, ToggleSize);
        if (clicked)
        {
            value = !value;
        }

        bool hovered = ImGui.IsItemHovered();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
        {
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        }

        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        float labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1
            ? (ToggleSize.Y - labelSize.Y) / 2f
            : 0;
        ImGui.SetCursorScreenPos(pos + new Vector2(0, labelY));
        using (var wrap = ImRaii.TextWrapPos(ImGui.GetCursorPosX() + labelWidth))
        {
            ImGui.TextUnformatted(literalLabel);
        }

        ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    /// <summary>A content-measured rounded card; nested rows share the outer card's draw channels.</summary>
    internal static void Card(string id, Action content, float minHeight = 0)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float cardWidth = ImGui.GetContentRegionAvail().X;
        const float padding = 12;
        const float gap = 12;
        bool topLevel = CardDepth == 0;
        using var idScope = ImRaii.PushId(id);
        if (topLevel)
        {
            CardDepth++;
            draw.ChannelsSplit(3);
        }

        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding));
            using (var wrap = ImRaii.TextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * padding))))
            using (var group = ImRaii.Group())
            {
                content();
            }

            float contentBottom = ImGui.GetCursorScreenPos().Y;
            float height = MathF.Max(minHeight, contentBottom - origin.Y + padding);
            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + gap));
        }
        finally
        {
            if (topLevel)
            {
                draw.ChannelsMerge();
                CardDepth--;
            }
            else
            {
                draw.ChannelsSetCurrent(2);
            }
        }
    }

    private static int CardDepth;

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (scale == 1)
        {
            draw.AddText(pos, Theme.Pack(color), text);
        }
        else
        {
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
        }
    }

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length > 0)
        {
            draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
        }
    }

    internal static string FitText(string text, float width, float scale = 1)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (ImGui.CalcTextSize(text).X * scale <= width)
        {
            return text;
        }

        const string ellipsis = "…";
        int length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
        {
            length--;
        }

        return length == 0 ? string.Empty : text[..length].TrimEnd() + ellipsis;
    }
}
