using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyRetainerLive;

/// <summary>Plugin-local drawn controls for the approved LazyRetainerLive shell.</summary>
internal static class RetainerLiveChrome
{
    private static readonly Vector2 ToggleSize = new(44, 24);

    // Screen X of the inner right edge of the innermost card or column; widgets size against it.
    private static readonly Stack<float> Rights = new();

    // Column layout memory: last-frame natural heights per id, plus the stretch state of the frame in progress.
    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();
    private static float _fillTop;
    private static float _fillHeight;
    private static float _stretch;

    // ---- measuring --------------------------------------------------------------------------------------------

    private static T Measure<T>(float scale, Func<T> measure)
    {
        if (scale == 1f)
            return measure();
        ImGui.SetWindowFontScale(scale);
        try
        {
            return measure();
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }

    public static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    public static float LineHeight(float scale = 1f) => Measure(scale, ImGui.GetTextLineHeight);

    public static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1, width)).Y);

    public static void PushRight(float screenX) => Rights.Push(screenX);

    public static void PopRight() => Rights.Pop();

    /// <summary>Width left to the nearest card or column edge (a card's inner padding is respected).</summary>
    public static float Avail()
        => Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    public static string FitText(string text, float width, float scale = 1f)
    {
        if (width <= 0)
            return "";
        if (TextWidth(text, scale) <= width)
            return text;

        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length].TrimEnd() + ellipsis, scale) > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }

    // ---- text -------------------------------------------------------------------------------------------------

    public static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f, float wrapWidth = 0f)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, wrapWidth);
    }

    public static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => Text(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    /// <summary>Flowing paragraph drawn on the draw list at measured height; the cursor moves below it plus <paramref name="after"/>.</summary>
    public static float Para(string text, float scale, Vector4 color, float after = 4f, float? width = null)
    {
        var w = MathF.Max(1, width ?? Avail());
        var h = WrappedHeight(text, w, scale);
        var pos = ImGui.GetCursorScreenPos();
        Text(ImGui.GetWindowDrawList(), pos, text, color, scale, w);
        ImGui.Dummy(new Vector2(w, h));
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + h + after));
        return h;
    }

    public static void Tip(string text, float wrap = 420f)
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrap);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private static void HoverTip(Vector2 min, Vector2 max, string text)
    {
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
            Tip(text);
    }

    // ---- shell bands ------------------------------------------------------------------------------------------

    public static void Diamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
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

    public static float BrandWidth(float scale) => 48 + TextWidth("LazyRetainerLive", scale) + 16;

    public static float ActionWidth(float minWidth, params string[] labels) => MathF.Max(minWidth, labels.Max(l => TextWidth(l)) + 32f);

    public static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size, float scale)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 8f);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        Text(draw, pos + new Vector2(48, (size.Y - LineHeight(scale)) / 2f), "LazyRetainerLive", Theme.TextPrimary, scale);
    }

    /// <summary>The widest state word fixes the pill width so it never shifts between states.</summary>
    public static float StatusPillWidth()
        => MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

    public static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var mark = Copy.MarkColor(state);
        var neutral = state is UiState.Idle or UiState.NeedsSetup or UiState.Empty;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);

        var content = (2 * Theme.DotRadius) + 8 + TextWidth(label);
        var x = pos.X + ((size.X - content) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(
            draw,
            new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)),
            label,
            neutral ? Theme.TextSecondary : mark);
    }

    public static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##retainerlive_close", size);
        var hovered = ImGui.IsItemHovered();
        var fill = hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);

        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        const float extent = 6;
        draw.AddLine(center + new Vector2(-extent, -extent), center + new Vector2(extent, extent), color, 1.5f);
        draw.AddLine(center + new Vector2(-extent, extent), center + new Vector2(extent, -extent), color, 1.5f);
        if (hovered)
        {
            // Below the control and right aligned, so the panel never covers the header meta.
            ImGui.SetNextWindowPos(new Vector2(pos.X + size.X, pos.Y + size.Y + 4), ImGuiCond.Always, new Vector2(1f, 0f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Close");
            ImGui.EndTooltip();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
        }

        return clicked;
    }

    public static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, bool enabled)
    {
        ImGui.SetCursorScreenPos(pos);
        if (!enabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton("##retainerlive_stop", size);
        if (!enabled)
            ImGui.EndDisabled();

        var hovered = enabled && ImGui.IsItemHovered();
        var fill = enabled ? Theme.StateRed with { W = hovered ? 0.24f : 0.12f } : Theme.Surface with { W = 0.5f };
        var border = enabled ? Theme.StateRed with { W = hovered ? 0.85f : 0.55f } : Theme.SurfaceBorder with { W = 0.5f };
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(border), 8f);
        const string label = "Stop listener";
        Text(
            draw,
            pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f),
            label,
            enabled ? Theme.TextPrimary : Theme.TextSecondary with { W = 0.5f });
        return enabled && clicked;
    }

    // ---- cards ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A content-measured rounded card. Background and content use separate draw channels. With
    /// <paramref name="fill"/> the card stretches to the column's shared bottom (see <see cref="Columns"/>).
    /// </summary>
    public static void Card(Action content, bool fill = false, bool gapAfter = true, float minHeight = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        const float padX = 16;
        const float padY = 16;

        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        PushRight(origin.X + width - padX);
        ImGui.SetCursorScreenPos(origin + new Vector2(padX, padY));
        ImGui.BeginGroup();
        content();
        ImGui.EndGroup();
        PopRight();
        var contentBottom = ImGui.GetItemRectMax().Y;
        var natural = MathF.Max(minHeight, contentBottom - origin.Y + padY);
        var height = natural;
        if (fill)
        {
            height = MathF.Max(natural, _fillTop + _fillHeight - origin.Y);
            _stretch += height - natural;
        }

        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
        draw.ChannelsMerge();

        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, gapAfter ? 16 - Theme.ItemSpacing.Y : 0));
    }

    /// <summary>Two columns with equal bottoms; below the breakpoint the rail simply follows the main column.</summary>
    public static void Columns(string id, float bodyHeight, Action main, Action rail, float breakpoint = 760)
    {
        var total = ImGui.GetContentRegionAvail().X;
        if (total < breakpoint)
        {
            var saved = (_fillTop, _fillHeight);
            _fillTop = 0;
            _fillHeight = float.NegativeInfinity; // stacked: cards keep their natural height
            main();
            rail();
            (_fillTop, _fillHeight) = saved;
            return;
        }

        const float gap = 16;
        var railWidth = Math.Clamp(0.26f * total, 300, 360);
        var mainWidth = total - railWidth - gap;
        var origin = ImGui.GetCursorScreenPos();
        ColumnHeights.TryGetValue(id, out var previous);

        // The visible page bottom: body height less the 1 px border and 16 px bottom padding, in cursor space.
        var pageFill = bodyHeight - 17 - 16 - ImGui.GetCursorPos().Y;
        // Equal column bottoms always; reaching down to the page bottom only when that adds little empty card.
        var natural = MathF.Max(previous.Main, previous.Rail);
        var target = pageFill - natural <= 72 ? MathF.Max(natural, pageFill) : natural;
        var savedFill = (_fillTop, _fillHeight);
        _fillTop = origin.Y;
        _fillHeight = target;

        var mainBottom = Column(origin, mainWidth, main, out var mainStretch);
        var railBottom = Column(origin + new Vector2(mainWidth + gap, 0), railWidth, rail, out var railStretch);
        (_fillTop, _fillHeight) = savedFill;

        ColumnHeights[id] = (mainBottom - origin.Y - mainStretch, railBottom - origin.Y - railStretch);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom)));
        ImGui.Dummy(new Vector2(total, 0));
    }

    private static float Column(Vector2 origin, float width, Action content, out float stretch)
    {
        _stretch = 0;
        ImGui.SetCursorScreenPos(origin);
        PushRight(origin.X + width);
        ImGui.BeginGroup();
        content();
        ImGui.EndGroup();
        PopRight();
        stretch = _stretch;
        return ImGui.GetItemRectMax().Y;
    }

    // ---- stat card --------------------------------------------------------------------------------------------

    public static float StatCardHeight(bool compact)
        => compact
            ? 12 + LineHeight(Theme.SecondaryScale) + 4 + LineHeight(Theme.SecondaryScale) + 8 + Theme.BarHeight + 12
            : 16 + LineHeight(Theme.SecondaryScale) + 8 + LineHeight(1.7f) + 8 + Theme.BarHeight + 8 + LineHeight(Theme.SecondaryScale) + 16;

    public static void StatCard(ImDrawListPtr draw, Vector2 pos, float width, bool compact, string label, string value, string note, float? fraction)
    {
        var height = StatCardHeight(compact);
        var size = new Vector2(width, height);
        const float pad = 16;
        var sm = Theme.SecondaryScale;
        var smH = LineHeight(sm);
        var empty = value == "—";
        var valueColor = empty ? Theme.TextSecondary : Theme.TextPrimary;
        var noteColor = fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 8f);

        float barY;
        if (compact)
        {
            // Stacked narrow cards: label and note on the left, the value on the right, the bar along the bottom.
            var top = pos.Y + 12;
            Diamond(draw, new Vector2(pos.X + pad + 5, top + (smH / 2f)), 5, Theme.Pack(Theme.Accent));
            var valueW = TextWidth(value, 1.7f);
            var textW = width - pad - 16 - 16 - valueW - 12;
            Text(draw, new Vector2(pos.X + pad + 16, top), FitText(label, textW, sm), Theme.TextPrimary, sm);
            Text(draw, new Vector2(pos.X + pad + 16, top + smH + 4), FitText(note, textW, sm), noteColor, sm);
            var valueH = LineHeight(1.7f);
            Text(draw, new Vector2(pos.X + width - pad - valueW, pos.Y + (((height - Theme.BarHeight - 8 - 12) - valueH) / 2f) + 12), value, valueColor, 1.7f);
            barY = pos.Y + height - 12 - Theme.BarHeight;
        }
        else
        {
            var y = pos.Y + pad;
            Diamond(draw, new Vector2(pos.X + pad + 5, y + (smH / 2f)), 5, Theme.Pack(Theme.Accent));
            Text(draw, new Vector2(pos.X + pad + 16, y), FitText(label, width - (2 * pad) - 16, sm), Theme.TextPrimary, sm);
            y += smH + 8;
            Text(draw, new Vector2(pos.X + pad, y), value, valueColor, 1.7f);
            y += LineHeight(1.7f) + 8;
            barY = y;
            y += Theme.BarHeight + 8;
            Text(draw, new Vector2(pos.X + pad, y), FitText(note, width - (2 * pad), sm), noteColor, sm);
        }

        // Only a real ratio draws a track; the slot stays reserved either way.
        if (fraction is not { } f)
            return;
        var min = new Vector2(pos.X + pad, barY);
        var max = new Vector2(pos.X + width - pad, barY + Theme.BarHeight);
        draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
        if (f > 0f)
        {
            draw.AddRectFilled(
                min,
                new Vector2(min.X + ((max.X - min.X) * Math.Clamp(f, 0, 1)), max.Y),
                Theme.Pack(Theme.StateGreen),
                2f);
        }
    }

    // ---- rows -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Label column of fixed width, value column filling the rest. Values wrap only at spaces; a value that
    /// cannot fit one line elides when <paramref name="elide"/> is set. Numbers right align to the card edge.
    /// Below <see cref="StackBelow"/> of value width the label sits above its value.
    /// </summary>
    public static void KeyValue(string label, string value, float labelColumn, bool elide = false, bool numeric = false)
    {
        const float StackBelow = 200f;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lh = LineHeight();
        var stacked = width - labelColumn < StackBelow;
        var valueX = stacked ? origin.X : origin.X + labelColumn;
        var valueW = stacked ? width : width - labelColumn;
        var shown = elide || numeric ? FitText(value, valueW) : value;
        var valueH = elide || numeric ? lh : WrappedHeight(value, valueW);
        var rowH = stacked ? lh + 2 + valueH + 12 : MathF.Max(lh, valueH) + 12;
        var y = origin.Y + 6;

        if (stacked)
        {
            Text(draw, new Vector2(origin.X, y), label, Theme.TextSecondary, Theme.SecondaryScale);
            y += lh + 2;
        }
        else
        {
            Text(draw, new Vector2(origin.X, y), label, Theme.TextSecondary);
        }

        var x = numeric ? valueX + valueW - TextWidth(shown) : valueX;
        Text(draw, new Vector2(x, y), shown, Theme.TextPrimary, 1f, elide || numeric ? 0f : valueW);
        if (shown != value)
            HoverTip(origin, origin + new Vector2(width, rowH), value);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, rowH));
    }

    /// <summary>One requirement row: fixed glyph slot, label, value right aligned in its state colour.</summary>
    public static void StatusRow(string label, string value, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var rowH = LineHeight() + 12;
        var lh = LineHeight();
        const float slot = 20;
        var sm = Theme.SecondaryScale;
        var valueColor = state switch
        {
            UiState.Ready => Theme.StateGreen,
            UiState.Missing or UiState.Blocked => Theme.StateAmber,
            UiState.Failed => Theme.StateRed,
            _ => Theme.TextSecondary,
        };

        var center = new Vector2(origin.X + (slot / 2f), origin.Y + (rowH / 2f));
        if (state == UiState.Ready)
        {
            var c = Theme.Pack(Theme.StateGreen);
            draw.AddLine(center + new Vector2(-4, 0), center + new Vector2(-1, 3), c, 1.5f);
            draw.AddLine(center + new Vector2(-1, 3), center + new Vector2(4.5f, -3.5f), c, 1.5f);
        }
        else
        {
            var c = Theme.Pack(state is UiState.Failed or UiState.Blocked or UiState.Missing ? valueColor : Theme.TextSecondary);
            draw.AddLine(center + new Vector2(-4, 0), center + new Vector2(4, 0), c, 1.5f);
        }

        var valueW = TextWidth(value, sm);
        var labelW = width - slot - 4 - valueW - 12;
        var shown = FitText(label, labelW);
        Text(draw, new Vector2(origin.X + slot + 4, origin.Y + ((rowH - lh) / 2f)), shown, Theme.TextSecondary);
        Text(
            draw,
            new Vector2(origin.X + width - valueW, origin.Y + ((rowH - LineHeight(sm)) / 2f)),
            value,
            valueColor,
            sm);
        if (shown != label)
            HoverTip(origin, origin + new Vector2(width, rowH), label);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, rowH));
    }

    /// <summary>Rounded notice: state dot, lead line, then the body below. Alert states are tinted.</summary>
    public static void Notice(string lead, string body, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textX = origin.X + 34;
        var textW = MathF.Max(1, width - 34 - 16);
        var leadH = WrappedHeight(lead, textW);
        var bodyH = body.Length == 0 ? 0 : WrappedHeight(body, textW, Theme.SecondaryScale);
        var height = 12 + leadH + (bodyH > 0 ? 4 + bodyH : 0) + 12;
        var mark = Copy.MarkColor(state);
        var alert = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
        var fill = alert ? mark with { W = 0.12f } : Theme.ShellBar;
        var border = alert ? mark with { W = 0.45f } : Theme.SurfaceBorder;

        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(fill), 8f);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(border), 8f);
        draw.AddCircleFilled(new Vector2(origin.X + 16, origin.Y + 12 + (LineHeight() / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, new Vector2(textX, origin.Y + 12), lead, Theme.TextPrimary, 1f, textW);
        if (bodyH > 0)
            Text(draw, new Vector2(textX, origin.Y + 12 + leadH + 4), body, Theme.TextSecondary, Theme.SecondaryScale, textW);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    public static void Hairline()
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        ImGui.GetWindowDrawList().AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        ImGui.Dummy(new Vector2(width, 1));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, 1));
    }

    public static bool Toggle(string literalLabel, ref bool value, string? help, bool separator = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var draw = ImGui.GetWindowDrawList();
        var textW = MathF.Max(20, width - ToggleSize.X - 16);
        var labelH = WrappedHeight(literalLabel, textW);
        var hasHelp = !string.IsNullOrWhiteSpace(help);
        var helpH = hasHelp ? WrappedHeight(help!, textW, Theme.SecondaryScale) : 0;
        var block = labelH + (hasHelp ? 4 + helpH : 0);
        var rowH = MathF.Max(ToggleSize.Y, block) + 24;

        if (separator)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var togglePos = new Vector2(pos.X + width - ToggleSize.X, pos.Y + ((rowH - ToggleSize.Y) / 2f));
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##retainerlive_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;

        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(
            togglePos,
            togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder),
            ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(
            togglePos + new Vector2(value ? 32 : 12, 12),
            8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textTop = pos.Y + ((rowH - block) / 2f);
        Text(draw, new Vector2(pos.X, textTop), literalLabel, Theme.TextPrimary, 1f, textW);
        if (hasHelp)
            Text(draw, new Vector2(pos.X, textTop + labelH + 4), help!, Theme.TextSecondary, Theme.SecondaryScale, textW);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.SetCursorScreenPos(pos + new Vector2(0, rowH));
        return clicked;
    }
}
