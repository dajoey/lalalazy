using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using LalaUiKit;

namespace GluttonyCombo.Window;

// Fork: plugin-local drawn controls for the approved shell; shared kit sources stay unchanged.
internal static class GluttonyChrome
{
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
        var clicked = ImGui.InvisibleButton("##gc_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 15, Theme.Pack(Theme.Accent));
        const string label = "GluttonyCombo";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_" + id, size);
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
        var clicked = ImGui.InvisibleButton("##gc_feature_" + id, size);
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

    internal static bool JobButton(ImDrawListPtr draw, string id, string label, IDalamudTextureWrap? icon,
        Vector2 pos, Vector2 size, bool selected, bool disabled, string? disabledHelp)
    {
        using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(disabled))
        {
            ImGui.SetCursorScreenPos(pos);
            var clicked = ImGui.InvisibleButton("##gc_job_" + id, size);
            var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
            draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
            draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
            if (icon is not null)
                draw.AddImage(icon.Handle, pos + new Vector2(10, 9), pos + new Vector2(44, 43));
            else
                Diamond(draw, pos + new Vector2(27, size.Y / 2f), 10, Theme.Pack(Theme.Accent));
            var textSize = ImGui.CalcTextSize(label);
            draw.AddText(pos + new Vector2(54, (size.Y - textSize.Y) / 2f),
                Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), FitText(label, size.X - 64));
            if (hovered && !string.IsNullOrEmpty(disabledHelp))
                ImGui.SetTooltip(disabledHelp);
            return clicked;
        }
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

    internal static bool Toggle(string literalLabel, ref bool value)
    {
        var hash = literalLabel.IndexOf("##", StringComparison.Ordinal);
        var label = hash < 0 ? literalLabel : literalLabel[..hash];
        if (label.Length == 0)
        {
            var compactPos = ImGui.GetCursorScreenPos();
            var compactClicked = ImGui.InvisibleButton("##gc_toggle" + literalLabel, ToggleSize);
            if (compactClicked)
                value = !value;
            DrawTrack(compactPos, value, ImGui.IsItemHovered());
            return compactClicked;
        }

        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        var labelSize = ImGui.CalcTextSize(label, false, labelWidth);
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var togglePos = pos + new Vector2(width - ToggleSize.X, 0);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##gc_toggle" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        DrawTrack(togglePos, value, ImGui.IsItemHovered());

        var labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1
            ? (ToggleSize.Y - labelSize.Y) / 2f
            : 0;
        ImGui.SetCursorScreenPos(pos + new Vector2(0, labelY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
        ImGui.TextUnformatted(label);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    private static void DrawTrack(Vector2 pos, bool value, bool hovered)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(pos, pos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));
    }

    internal static void Card(string id, Action content, float minHeight = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float padding = 12;
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, width - (2 * padding)));
            ImGui.BeginGroup();
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + padding);
            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + padding));
        }
        finally
        {
            if (topLevel)
                _cardDepth--;
        }
    }

    internal static void PushControlColors()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
    }

    internal static void PopControlColors() => ImGui.PopStyleColor(12);

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
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
