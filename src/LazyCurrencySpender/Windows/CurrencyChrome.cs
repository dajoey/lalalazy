using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;

namespace CurrencySpender.Windows;

/// <summary>Plugin-local drawn controls for the Currency Spender shell and content surfaces.</summary>
internal static class CurrencyChrome
{
    private const float ControlHeight = 34f;
    private const float CardPadding = 12f;
    private const float CardGap = 12f;
    private static readonly Vector2 ToggleSize = new(44, 24);
    private static int _cardDepth;

    internal static void PushFrame()
    {
        Theme.PushFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 2 * Theme.Unit);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.22f });
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Separator, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Theme.Accent with { W = 0.24f });
    }

    internal static void PopFrame()
    {
        ImGui.PopStyleColor(19);
        ImGui.PopStyleVar(4);
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

    internal static void TopBar(string title, UiState state, string? actionLabel = null, Action? action = null)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var brandWidth = MathF.Min(width, MathF.Max(180, (ImGui.CalcTextSize(title).X * Theme.TitleScale) + 66));
        var stateLabel = Copy.Word(state);
        var statusWidth = MathF.Max(82, ImGui.CalcTextSize(stateLabel).X + 43);
        var actionWidth = actionLabel is null ? 0 : MathF.Max(82, ImGui.CalcTextSize(actionLabel).X + 30);
        var required = brandWidth + statusWidth + (2 * gap) + (actionLabel is null ? 0 : actionWidth + gap);
        var singleRow = width >= required;
        var barHeight = singleRow ? 66f : 112f;
        var brandSize = new Vector2(brandWidth, 42);
        var brandPos = origin + new Vector2(0, 12);

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.AddRectFilled(brandPos, brandPos + brandSize, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(brandPos, brandPos + brandSize, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, brandPos + new Vector2(23, 21), 14, Theme.Pack(Theme.Accent));
        DrawText(draw, brandPos + new Vector2(48, 11), FitText(title, brandWidth - 58, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);

        var rowY = singleRow ? origin.Y + ((barHeight - ControlHeight) / 2f) : origin.Y + 66;
        var statusPos = new Vector2(origin.X + width - statusWidth, rowY);
        if (actionLabel is not null && action is not null)
        {
            var actionPos = new Vector2(statusPos.X - gap - actionWidth, rowY);
            if (OutlineButton(draw, "top_" + actionLabel, actionLabel, actionPos, new Vector2(actionWidth, ControlHeight)))
                action();
        }
        StatusPill(draw, statusPos, new Vector2(statusWidth, ControlHeight), stateLabel, state);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    internal static int FeatureRow(string id, IReadOnlyList<string> labels, int selected)
    {
        var result = selected;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var sizes = labels.Select(label => new Vector2(
            MathF.Min(width, MathF.Max(104, ImGui.CalcTextSize(label).X + 50)), ControlHeight)).ToArray();
        var positions = new Vector2[labels.Count];
        float x = 0;
        float y = 8;
        for (var i = 0; i < labels.Count; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += ControlHeight + gap;
            }
            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }

        var barHeight = y + ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (var i = 0; i < labels.Count; i++)
            if (FeatureButton(draw, id + "_" + i, labels[i], origin + positions[i], sizes[i], selected == i))
                result = i;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
        return result;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##currency_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
            Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
        return clicked;
    }

    internal static bool Button(string id, string label, bool selected = false, float width = 0)
    {
        var textWidth = ImGui.CalcTextSize(label).X;
        var buttonWidth = width > 0 ? width : textWidth + 32;
        return OutlineButton(ImGui.GetWindowDrawList(), id, label, ImGui.GetCursorScreenPos(),
            new Vector2(buttonWidth, ControlHeight), selected);
    }

    private static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##currency_feature_" + id, size);
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

    internal static void PageHeader(string title, string description, string liveCount)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 660)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(liveCount).X;
            DrawText(draw, new Vector2(textX, origin.Y), FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, new Vector2(textX, origin.Y + 29), FitText(description, width - tileSize.X - 12), Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 5, liveCount, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
        }
        else
        {
            DrawText(draw, origin + new Vector2(tileSize.X + 12, 4), FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
            Widgets.SecondaryWrapped(description);
            Widgets.Caption(liveCount);
        }
        ImGui.Separator();
    }

    internal static void Section(string title, string? description = null, string? count = null)
    {
        Widgets.Title(count is null ? title : $"{title} · {count}");
        if (!string.IsNullOrWhiteSpace(description))
            Widgets.SecondaryWrapped(description);
    }

    internal static void TwoColumns(string id, Action work, Action rail)
    {
        if (ImGui.GetContentRegionAvail().X >= 760)
        {
            var available = ImGui.GetContentRegionAvail().X;
            var gap = 3 * Theme.Unit;
            var leftWidth = (available - gap) * 0.68f;
            ImGui.BeginChild("##currency_" + id + "_work", new Vector2(leftWidth, 0), false);
            work();
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + gap);
            ImGui.BeginChild("##currency_" + id + "_rail", new Vector2(0, 0), false);
            rail();
            ImGui.EndChild();
        }
        else
        {
            work();
            ImGui.Separator();
            rail();
        }
    }

    internal static bool Toggle(string literalLabel, ref bool value, string? help = null, string? id = null)
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
        var clicked = ImGui.InvisibleButton("##currency_setting_" + (id ?? literalLabel), ToggleSize);
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
            ImGui.TextUnformatted(help!);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
        }

        var totalHeight = rowHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        ImGui.SetCursorScreenPos(pos + new Vector2(0, totalHeight + Theme.ItemSpacing.Y));
        return clicked;
    }

    internal static void Card(string id, Action content, CurrencyCardTone tone = CurrencyCardTone.Surface, float minHeight = 0, float width = 0)
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
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
            ImGui.BeginGroup();
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var (fill, border) = CardColors(tone);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), fill, 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), border, 2 * Theme.Unit);
            if (tone is CurrencyCardTone.Recommended or CurrencyCardTone.Attention)
                draw.AddRectFilled(origin + new Vector2(0, 8), origin + new Vector2(3, height - 8),
                    Theme.Pack(tone == CurrencyCardTone.Recommended ? Theme.StateGreen : Theme.StateAmber), 2f);
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

    internal static bool BeginTable(string id, int columns, ImGuiTableFlags flags, bool horizontalScroll = false)
    {
        flags = (flags & ~ImGuiTableFlags.Borders) | ImGuiTableFlags.RowBg;
        if (horizontalScroll)
        {
            flags |= ImGuiTableFlags.ScrollX;
            return ImGui.BeginTable(id, columns, flags, new Vector2(ImGui.GetContentRegionAvail().X, 0));
        }
        return ImGui.BeginTable(id, columns, flags);
    }

    internal static void ContextPopup(string id, Action content)
    {
        using var context = ImRaii.ContextPopupItem(id);
        if (context)
            content();
    }

    internal static void EmptyState(string title, string detail, string? actionLabel = null, Action? action = null)
    {
        Card("empty_" + title, () =>
        {
            Widgets.StateText("● " + title, UiState.Empty);
            Widgets.SecondaryWrapped(detail);
            if (actionLabel is not null && action is not null && Button("empty_action_" + title, actionLabel, true))
                action();
        });
    }

    internal readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    internal static void StatCards(params Stat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 650;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
        const float height = 112;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));

            ImGui.SetCursorScreenPos(pos + new Vector2(34, 11));
            Widgets.Caption(stat.Label);
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 34));
            Widgets.Title(stat.Cap is null ? stat.Value : $"{stat.Value} / {stat.Cap.TrimStart('/')}");
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 70);
                var max = pos + new Vector2(width - 12, 74);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0), Theme.Pack(Theme.Accent), 2f);
            }
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 84));
            Widgets.Caption(FitText(stat.Note, width - 24, Theme.CaptionScale));
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
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

    private static (uint Fill, uint Border) CardColors(CurrencyCardTone tone) => tone switch
    {
        CurrencyCardTone.Row => (Theme.Pack(Theme.ShellBar), Theme.Pack(Theme.SurfaceBorder)),
        CurrencyCardTone.Recommended => (Theme.Pack(Theme.StateGreen with { W = 0.13f }), Theme.Pack(Theme.SurfaceBorder)),
        CurrencyCardTone.Attention => (Theme.Pack(Theme.StateAmber with { W = 0.13f }), Theme.Pack(Theme.SurfaceBorder)),
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
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(Theme.TextSecondary), text);
    }
}

internal enum CurrencyCardTone
{
    Surface,
    Row,
    Recommended,
    Attention,
}
