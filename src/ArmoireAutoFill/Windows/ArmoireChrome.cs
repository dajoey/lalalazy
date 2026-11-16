using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace ArmoireAutoFill.Windows;

internal readonly record struct ArmoireNavItem(string Id, string Label);

internal readonly record struct ArmoireStat(string Label, string Value, string? Cap, string Note, float? Fraction);

/// <summary>Drawn controls for the Armoire Auto-Fill shell and its content cards.</summary>
internal static class ArmoireChrome
{
    private const float ControlHeight = 34f;
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

    internal static void TopBar(IReadOnlyList<ArmoireNavItem> navigation, int selected, Action<int> navigate, UiState state, Action closeRequested)
    {
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X);
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var brandSize = new Vector2(MathF.Max(196, (ImGui.CalcTextSize("Armoire Auto-Fill").X * Theme.TitleScale) + 68), 42);
        var navSizes = navigation.Select(item => new Vector2(
            MathF.Max(100, ImGui.CalcTextSize(item.Label).X + 52), ControlHeight)).ToArray();
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(82, ImGui.CalcTextSize(stateLabel).X + 43), ControlHeight);
        var closeSize = new Vector2(ControlHeight);
        var navigationWidth = navSizes.Sum(size => size.X) + (Math.Max(0, navSizes.Length - 1) * gap);
        var singleRow = width >= brandSize.X + navigationWidth + statusSize.X + closeSize.X + (5 * gap);
        var positions = new Vector2[navigation.Count];
        var brandPos = new Vector2(0, singleRow ? 12 : 9);
        Vector2 statusPos;
        Vector2 closePos;
        float barHeight;

        if (singleRow)
        {
            barHeight = 66;
            var x = brandSize.X + gap;
            var y = (barHeight - ControlHeight) / 2f;
            for (var i = 0; i < navigation.Count; i++)
            {
                positions[i] = new Vector2(x, y);
                x += navSizes[i].X + gap;
            }
            closePos = new Vector2(width - closeSize.X, y);
            statusPos = new Vector2(closePos.X - gap - statusSize.X, y);
        }
        else
        {
            closePos = new Vector2(width - closeSize.X, 13);
            statusPos = new Vector2(closePos.X - gap - statusSize.X, 13);
            float x = 0;
            float y = 61;
            for (var i = 0; i < navigation.Count; i++)
            {
                var buttonWidth = MathF.Min(navSizes[i].X, width);
                navSizes[i].X = buttonWidth;
                if (x > 0 && x + buttonWidth > width)
                {
                    x = 0;
                    y += ControlHeight + gap;
                }
                positions[i] = new Vector2(x, y);
                x += buttonWidth + gap;
            }
            barHeight = navigation.Count == 0 ? 61 : y + ControlHeight + 12;
        }

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        DrawBrand(draw, origin + brandPos, brandSize);
        for (var i = 0; i < navigation.Count; i++)
            if (OutlineButton(draw, navigation[i].Id, navigation[i].Label, origin + positions[i], navSizes[i], i == selected))
                navigate(i);
        StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        if (CloseButton(draw, origin + closePos, closeSize))
            closeRequested();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##armoire_close", size);
        var hovered = ImGui.IsItemHovered();
        var color = hovered ? Theme.Accent : Theme.TextSecondary;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var center = pos + (size / 2f);
        draw.AddLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), Theme.Pack(color), 1.5f);
        draw.AddLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), Theme.Pack(color), 1.5f);
        if (hovered)
            ImGui.SetTooltip("Close");
        return clicked;
    }

    private static void DrawBrand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 14, Theme.Pack(Theme.Accent));
        const string label = "Armoire Auto-Fill";
        var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * Theme.TitleScale,
            pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1), Theme.Pack(Theme.TextPrimary), label);
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var text = FitText(label, size.X - 42);
        var textSize = ImGui.CalcTextSize(text);
        draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), text);
        return clicked;
    }

    internal static bool Button(string id, string label, bool emphasized = false, bool enabled = true, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var buttonWidth = width > 0 ? MathF.Min(width, available) : MathF.Min(available, MathF.Max(132, ImGui.CalcTextSize(label).X + 38));
        var size = new Vector2(buttonWidth, ControlHeight);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        var border = !enabled ? Theme.SurfaceBorder : emphasized || hovered ? Theme.Accent : Theme.SurfaceBorder;
        var fill = emphasized && enabled ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(border), 2 * Theme.Unit);
        var text = FitText(label, size.X - 18);
        var textSize = ImGui.CalcTextSize(text);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(!enabled ? Theme.TextSecondary : emphasized ? Theme.Accent : Theme.TextPrimary), text);
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

    internal static void PageHeader(string title, string purpose, string count, string? subtext = null)
    {
        subtext ??= string.Empty;
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X);
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
            DrawText(draw, new Vector2(textX, origin.Y),
                FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, new Vector2(textX, origin.Y + 29),
                FitText(purpose, width - tileSize.X - 12 - subtextWidth - 16), Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 4, count, Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 31, subtext, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        DrawText(draw, origin + new Vector2(tileSize.X + 12, 4),
            FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(purpose);
        Widgets.Caption(count);
        if (subtext.Length > 0)
            Widgets.Caption(subtext);
        ImGui.Separator();
    }

    internal static void StatCards(params ArmoireStat[] stats)
    {
        if (stats.Length == 0)
            return;
        var available = MathF.Max(1, ImGui.GetContentRegionAvail().X);
        var columns = stats.Length == 4 && available >= 1000 ? 4
            : stats.Length <= 3 && available >= 760 ? stats.Length
            : available >= 540 ? Math.Min(2, stats.Length)
            : 1;
        var rows = (stats.Length + columns - 1) / columns;
        const float height = 108;
        var width = (available - ((columns - 1) * CardGap)) / columns;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var column = i % columns;
            var row = i / columns;
            var pos = origin + new Vector2(column * (width + CardGap), row * (height + CardGap));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            DrawText(draw, pos + new Vector2(36, 12), FitText(stat.Label, width - 48, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            var value = stat.Cap is null ? stat.Value : $"{stat.Value} / {stat.Cap.TrimStart('/')}";
            DrawText(draw, pos + new Vector2(12, 33), FitText(value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            var barMin = pos + new Vector2(12, 70);
            var barMax = pos + new Vector2(width - 12, 74);
            draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2f);
            if (stat.Fraction is > 0)
                draw.AddRectFilled(barMin, barMin + new Vector2((barMax.X - barMin.X) * Math.Clamp(stat.Fraction.Value, 0, 1), 4), Theme.Pack(Theme.StateGreen), 2f);
            DrawText(draw, pos + new Vector2(12, 86), FitText(stat.Note, width - 24, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, (rows * height) + ((rows - 1) * CardGap) + CardGap));
    }

    internal static void Card(string id, float minHeight, Action content, float width = 0)
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
            ImGui.PushID(id);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            ImGui.PopID();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
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
        var clicked = ImGui.InvisibleButton("##armoire_setting_" + literalLabel, ToggleSize);
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

    internal static void TwoColumns(Action work, Action rail)
    {
        if (ImGui.GetContentRegionAvail().X >= 780)
        {
            using var columns = Layout.TwoColumns.Begin(0.60f);
            work();
            columns.Split();
            rail();
        }
        else
        {
            work();
            ImGui.Separator();
            rail();
        }
    }

    internal static void Requirement(string label, string value, bool ready)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.StateText(ready ? "✓" : "—", ready ? UiState.Ready : UiState.Idle);
        ImGui.SameLine();
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 42 <= width)
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

    internal static float FooterHeight(float width, bool showStop) => width < 620 ? showStop ? 92 : 64 : showStop ? 64 : 50;

    internal static void Footer(UiState state, string action, string detail, bool showStop, out bool stopClicked)
    {
        stopClicked = false;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width, showStop);
        var draw = ImGui.GetWindowDrawList();
        var narrow = width < 620;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var stopPos = origin + new Vector2(width - 104, narrow ? 50 : 14);
        var textPos = origin + new Vector2(26, narrow ? 8 : 9);
        var textRight = showStop && !narrow ? stopPos.X - 16 : origin.X + width - 12;
        draw.AddCircleFilled(origin + new Vector2(12, textPos.Y - origin.Y + 8), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        DrawText(draw, textPos, FitText($"{Copy.Word(state)} · {action}", textRight - textPos.X), Theme.TextPrimary);
        DrawText(draw, textPos + new Vector2(0, 25), FitText(detail, textRight - textPos.X), Theme.TextSecondary);

        if (showStop)
        {
            var size = new Vector2(96, 36);
            ImGui.SetCursorScreenPos(stopPos);
            stopClicked = ImGui.InvisibleButton("Stop", size);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(stopPos, stopPos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.28f : 0.16f }), 2 * Theme.Unit);
            draw.AddRect(stopPos, stopPos + size, Theme.Pack(Theme.StateRed), 2 * Theme.Unit);
            var textSize = ImGui.CalcTextSize("Stop");
            draw.AddText(stopPos + ((size - textSize) / 2f), Theme.Pack(Theme.StateRed), "Stop");
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
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

    private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length == 0)
            return;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
    }

    internal static string FitText(string text, float width, float scale = 1)
    {
        if (width <= 0)
            return string.Empty;
        if (ImGui.CalcTextSize(text).X * scale <= width)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
            length--;
        return length == 0 ? string.Empty : text[..length].TrimEnd() + ellipsis;
    }
}
