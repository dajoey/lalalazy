using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyCrafter.UI;

/// <summary>LazyCrafter-local drawn controls for the approved shell, cards and settings tracks.</summary>
internal static class CrafterChrome
{
    internal const float ControlHeight = 34f;
    internal const float WideFooterHeight = 64f;
    internal const float NarrowFooterHeight = 96f;

    private const float CardPadding = 12f;
    private const float CardGap = 12f;
    private static readonly Vector2 ToggleSize = new(44, 24);
    private static int _cardDepth;

    internal static void PushFrame()
    {
        Theme.PushFrame();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.18f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.25f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.32f });
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Theme.Surface with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Theme.Surface with { W = 0.52f });
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 12f);
    }

    internal static void PopFrame()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(15);
        Theme.PopFrame();
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
        draw.AddCircleFilled(center, radius * 0.32f, Theme.Pack(Theme.Surface));
    }

    internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(23, size.Y / 2f), 15, Theme.Pack(Theme.Accent));
        const string label = "LazyCrafter";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), FitText(label, size.X - 12));
        return clicked;
    }

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_feature_" + id, size);
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
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), FitText(label, size.X - 38));
        return clicked;
    }

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_footer_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.85f : 1f }), 2 * Theme.Unit);
        var textSize = ImGui.CalcTextSize("Stop");
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1), Theme.Pack(Theme.TextPrimary), "Stop");
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
        var clicked = ImGui.InvisibleButton("##crafter_setting_" + literalLabel, ToggleSize);
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

    internal static void PageHeader(string title, string description, string count, string subtext)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 720)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            var subtextWidth = ImGui.CalcTextSize(subtext).X;
            DrawText(draw, new Vector2(textX, origin.Y), FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, new Vector2(textX, origin.Y + 29), FitText(description, width - tileSize.X - 12 - subtextWidth - 16), Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 4, count, Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 31, subtext, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        DrawText(draw, origin + new Vector2(tileSize.X + 12, 4), FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(description);
        Widgets.Caption(count);
        Widgets.Caption(subtext);
        ImGui.Separator();
    }

    internal readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    internal static void DrawStatCards(params Stat[] stats)
    {
        if (stats.Length == 0)
            return;
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 650;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
        const float height = 108f;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            DrawText(draw, pos + new Vector2(38, 12), FitText(stat.Label, width - 50, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            var value = stat.Cap is null ? stat.Value : $"{stat.Value} / {stat.Cap.TrimStart('/')}";
            DrawText(draw, pos + new Vector2(12, 34), FitText(value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 68);
                var max = pos + new Vector2(width - 12, 72);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0), Theme.Pack(Theme.StateGreen), 2f);
            }
            DrawText(draw, pos + new Vector2(12, 84), FitText(stat.Note, width - 24, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
    }

    internal enum CardTone
    {
        Surface,
        Row,
        Ready,
        Attention,
    }

    /// <summary>Draws a rounded, content-measured card without covering its child widgets.</summary>
    internal static void Card(string id, Action content, CardTone tone = CardTone.Surface, float minHeight = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = ImGui.GetContentRegionAvail().X;
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        ImGui.PushID(id);
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
            ImGui.BeginGroup();
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var (fill, border) = CardColors(tone);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), fill, 8f);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), border, 8f);
            if (tone is CardTone.Ready or CardTone.Attention)
                draw.AddRectFilled(origin + new Vector2(0, 8), origin + new Vector2(3, height - 8),
                    Theme.Pack(tone == CardTone.Ready ? Theme.StateGreen : Theme.StateAmber), 2f);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + CardGap));
        }
        finally
        {
            ImGui.PopID();
            if (topLevel)
                _cardDepth--;
        }
    }

    internal static void TwoColumns(string id, float leftFraction, Action work, Action rail)
    {
        if (ImGui.GetContentRegionAvail().X < 860)
        {
            work();
            ImGui.Separator();
            rail();
            return;
        }

        var available = ImGui.GetContentRegionAvail().X;
        var gap = 3 * Theme.Unit;
        var leftWidth = (available - gap) * leftFraction;
        ImGui.BeginChild("##" + id + "_work", new Vector2(leftWidth, 0), false);
        work();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##" + id + "_rail", new Vector2(0, 0), false);
        rail();
        ImGui.EndChild();
    }

    internal static void Section(string title, string? description = null)
    {
        Widgets.Title(title);
        if (!string.IsNullOrWhiteSpace(description))
            Widgets.SecondaryWrapped(description);
    }

    internal static void Requirement(string label, string value, UiState state)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.StateText(state == UiState.Ready ? "✓" : "—", state);
        ImGui.SameLine();
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 36 <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Caption(value);
        }
        else
        {
            ImGui.SetCursorPosX(start.X + 24);
            Widgets.Caption(value);
        }
    }

    internal static float ButtonWidth(string label) => ImGui.CalcTextSize(label).X + (2 * Theme.FramePadding.X);

    internal static void SameLineWrapped(float nextWidth)
    {
        var x = ImGui.GetCursorPosX();
        var right = x + ImGui.GetContentRegionAvail().X;
        if (x + Theme.ItemSpacing.X + nextWidth > right)
            ImGui.NewLine();
        else
            ImGui.SameLine();
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

    private static (uint Fill, uint Border) CardColors(CardTone tone) => tone switch
    {
        CardTone.Row => (Theme.Pack(Theme.ShellBar), Theme.Pack(Theme.SurfaceBorder)),
        CardTone.Ready => (Theme.Pack(Theme.StateGreen with { W = 0.13f }), Theme.Pack(Theme.SurfaceBorder)),
        CardTone.Attention => (Theme.Pack(Theme.StateAmber with { W = 0.13f }), Theme.Pack(Theme.SurfaceBorder)),
        _ => (Theme.Pack(Theme.Surface), Theme.Pack(Theme.SurfaceBorder)),
    };

    private static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length == 0)
            return;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
    }
}
