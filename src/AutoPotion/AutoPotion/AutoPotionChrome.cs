using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace AutoPotion;

/// <summary>Drawn controls for AutoPotion's settings shell and rounded cards.</summary>
internal static class AutoPotionChrome
{
    internal const float CardPad = 16f;
    internal const float CardGap = 16f;
    internal const float Radius = 8f;
    internal const float Row = 36f;
    private static readonly Vector2 ToggleSize = new(44f, 24f);

    // Inner right edges (screen X) of the containers being drawn: cards and columns narrow what a widget may use.
    private static readonly Stack<float> Rights = new();
    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();
    private static float _fillBottom;
    private static float _stretch;
    private static float _lastBottom;

    internal static void Reset()
    {
        Rights.Clear();
        _fillBottom = 0f;
        _stretch = 0f;
        _lastBottom = 0f;
    }

    internal static void PushRight(float x) => Rights.Push(x);

    internal static void PopRight() => Rights.Pop();

    /// <summary>Width from the cursor to the innermost container's inner right edge.</summary>
    internal static float Avail() =>
        Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    // ---- text measured at the size it is drawn ----

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

    internal static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    internal static float LineHeight(float scale = 1f) => Measure(scale, () => ImGui.GetTextLineHeight());

    internal static float WrappedHeight(string text, float width, float scale = 1f) =>
        Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1f, width)).Y);

    internal static Vector4 Alpha(Vector4 color, float alpha) => color with { W = alpha };

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f) =>
        DrawText(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    /// <summary>Draws wrapped text at the given width and returns its measured height.</summary>
    internal static float DrawWrapped(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return 0f;
        var height = WrappedHeight(text, width, scale);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, MathF.Max(1f, width));
        return height;
    }

    /// <summary>Shortens text with an ellipsis until it fits one line at the draw size.</summary>
    internal static string Fit(string text, float width, float scale = 1f)
    {
        if (width <= 0f)
            return "";
        if (TextWidth(text, scale) <= width)
            return text;
        const string ellipsis = "…";
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (TextWidth(text[..mid].TrimEnd() + ellipsis, scale) <= width)
                low = mid;
            else
                high = mid - 1;
        }
        return low == 0 ? "" : text[..low].TrimEnd() + ellipsis;
    }

    /// <summary>Keeps at most maxLines wrapped lines, ending the last one with an ellipsis.</summary>
    internal static string ClampLines(string text, float width, float scale, int maxLines)
    {
        var limit = (LineHeight(scale) * maxLines) + 1f;
        if (WrappedHeight(text, width, scale) <= limit)
            return text;
        const string ellipsis = "…";
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (WrappedHeight(text[..mid].TrimEnd() + ellipsis, width, scale) <= limit)
                low = mid;
            else
                high = mid - 1;
        }
        return low == 0 ? "" : text[..low].TrimEnd() + ellipsis;
    }

    /// <summary>Full text on hover for something that was elided.</summary>
    internal static void HoverTip(Vector2 min, Vector2 max, string full)
    {
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
            ImGui.SetTooltip(full);
    }

    /// <summary>One block of text at the cursor, wrapped to the available width; advances by its measured height plus after.</summary>
    internal static void TextBlock(string text, float scale, Vector4 color, float after = 0f)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var height = DrawWrapped(ImGui.GetWindowDrawList(), origin, text, width, color, scale);
        ImGui.Dummy(new Vector2(width, MathF.Max(1f, height + after - Theme.ItemSpacing.Y)));
    }

    /// <summary>Vertical space of exactly px between the previous item and the next.</summary>
    internal static void Gap(float px) => ImGui.Dummy(new Vector2(1f, MathF.Max(0f, px - Theme.ItemSpacing.Y)));

    // ---- shell pieces ----

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

    internal static float BrandWidth() => 12f + 24f + 8f + TextWidth("AutoPotion", Theme.TitleScale) + 16f;

    internal static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(12f + 12f, size.Y / 2f), 12f, Theme.Pack(Theme.Accent));
        const string label = "AutoPotion";
        var lineHeight = LineHeight(Theme.TitleScale);
        DrawText(draw, pos + new Vector2(12f + 24f + 8f, (size.Y - lineHeight) / 2f), label, Theme.TextPrimary, Theme.TitleScale);
    }

    internal static float FeatureWidth(string label) => 16f + 12f + 8f + TextWidth(label, Theme.SecondaryScale) + 16f + 1f;

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##autopotion_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Alpha(Theme.Accent, 0.14f) : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        var outline = selected || hovered ? Theme.Accent : Alpha(Theme.SurfaceBorder, 0.6f);
        draw.AddRect(pos, pos + size, Theme.Pack(outline), Radius);
        var marker = pos + new Vector2(16f + 6f, size.Y / 2f);
        draw.AddCircle(marker, 6f, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 16, 1f);
        if (selected)
            draw.AddCircleFilled(marker, 3f, Theme.Pack(Theme.Accent));
        var lineHeight = LineHeight(Theme.SecondaryScale);
        var color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        DrawText(draw, pos + new Vector2(16f + 12f + 8f, (size.Y - lineHeight) / 2f), label, color, Theme.SecondaryScale);
        return clicked;
    }

    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, float windowRight)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##autopotion_close", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Alpha(Theme.Accent, 0.20f) : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius, ImDrawFlags.None, hovered ? 1.5f : 1f);
        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextPrimary);
        draw.AddLine(center + new Vector2(-6f, -6f), center + new Vector2(6f, 6f), color, 1.5f);
        draw.AddLine(center + new Vector2(-6f, 6f), center + new Vector2(6f, -6f), color, 1.5f);
        if (hovered)
        {
            // Anchored under the button and kept inside the window, instead of following the pointer off the edge.
            ImGui.SetNextWindowPos(new Vector2(MathF.Min(pos.X + size.X, windowRight), pos.Y + size.Y + 8f), ImGuiCond.Always, new Vector2(1f, 0f));
            ImGui.SetTooltip("Close");
        }
        return clicked;
    }

    /// <summary>Width of the status pill: fixed to the widest state word so nothing shifts when the state changes.</summary>
    internal static float StatusWidth()
    {
        var widest = 0f;
        foreach (var state in Enum.GetValues<UiState>())
            widest = MathF.Max(widest, TextWidth(Copy.Word(state)));
        return (2f * Theme.DotRadius) + 8f + widest + 32f;
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var label = Copy.Word(state);
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        var textWidth = TextWidth(label);
        var content = (2f * Theme.DotRadius) + 8f + textWidth;
        var left = pos.X + ((size.X - content) / 2f);
        draw.AddCircleFilled(new Vector2(left + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        DrawText(draw, new Vector2(left + (2f * Theme.DotRadius) + 8f, pos.Y + ((size.Y - LineHeight()) / 2f)), label, color);
    }

    // ---- cards and columns ----

    /// <summary>
    ///     A content-measured rounded card. Nested widgets read the card's inner right edge through <see cref="Avail"/>.
    ///     With fill the card ends a column: it stretches to the column bottom so both columns finish together.
    /// </summary>
    internal static void Card(string id, Action content, bool fill = false, float padY = CardPad)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        draw.ChannelsSplit(3);
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.PushID(id);
            PushRight(origin.X + width - CardPad);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPad, padY));
            ImGui.BeginGroup();
            content();
            ImGui.Dummy(new Vector2(1f, 0f));
            ImGui.EndGroup();
            PopRight();
            ImGui.PopID();

            var natural = MathF.Max(Theme.CardMinHeight, ImGui.GetItemRectMax().Y - origin.Y + padY);
            var height = natural;
            if (fill && _fillBottom > 0f)
            {
                height = MathF.Max(natural, _fillBottom - origin.Y);
                _stretch += height - natural;
            }

            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
            draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
            _lastBottom = origin.Y + height;
            ImGui.SetCursorScreenPos(origin + new Vector2(0f, height));
            if (fill && _fillBottom > 0f)
                ImGui.Dummy(new Vector2(1f, 0f));
            else
                ImGui.Dummy(new Vector2(width, MathF.Max(0f, CardGap - Theme.ItemSpacing.Y)));
        }
        finally
        {
            draw.ChannelsMerge();
        }
    }

    /// <summary>
    ///     Main column and rail side by side with equal bottoms; below the breakpoint they stack. Each column is a group so the
    ///     cursor's left edge is re-based, and the last card of each column fills to the shared bottom.
    /// </summary>
    internal static void Columns(string id, Action main, Action rail, float breakpoint)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        if (width < breakpoint)
        {
            var saved = _fillBottom;
            _fillBottom = 0f;
            main();
            ImGui.Dummy(new Vector2(1f, 0f));
            rail();
            _fillBottom = saved;
            return;
        }

        var railWidth = Math.Clamp(0.26f * width, 300f, 360f);
        var mainWidth = width - railWidth - CardGap;
        ColumnHeights.TryGetValue(id, out var previous);
        var natural = MathF.Max(previous.Main, previous.Rail);
        var viewport = ImGui.GetWindowHeight() - 17f - ImGui.GetCursorPos().Y;
        var outerFill = _fillBottom;
        _fillBottom = origin.Y + MathF.Max(natural, viewport);

        ImGui.SetCursorScreenPos(origin);
        PushRight(origin.X + mainWidth);
        ImGui.BeginGroup();
        _stretch = 0f;
        _lastBottom = origin.Y;
        main();
        ImGui.EndGroup();
        PopRight();
        var mainBottom = _lastBottom;
        var mainNatural = mainBottom - origin.Y - _stretch;

        ImGui.SetCursorScreenPos(origin + new Vector2(mainWidth + CardGap, 0f));
        PushRight(origin.X + width);
        ImGui.BeginGroup();
        _stretch = 0f;
        _lastBottom = origin.Y;
        rail();
        ImGui.EndGroup();
        PopRight();
        var railBottom = _lastBottom;
        var railNatural = railBottom - origin.Y - _stretch;

        ColumnHeights[id] = (mainNatural, railNatural);
        _fillBottom = outerFill;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom)));
        ImGui.Dummy(new Vector2(width, 0f));
    }

    internal static void Section(string title, string description)
    {
        TextBlock(title, Theme.TitleScale, Theme.TextPrimary, 4f);
        TextBlock(description, Theme.SecondaryScale, Theme.TextSecondary, 12f);
    }

    // ---- rows ----

    /// <summary>A setting row: label and help on the left, the switch on the right, both centred in the row.</summary>
    internal static bool Toggle(string id, string label, ref bool value, string? help = null, bool separator = false, bool more = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Max(20f, width - ToggleSize.X - 16f);
        var labelHeight = WrappedHeight(label, textWidth);
        var hasHelp = !string.IsNullOrWhiteSpace(help);
        var helpHeight = hasHelp ? WrappedHeight(help!, textWidth, Theme.SecondaryScale) : 0f;
        var block = labelHeight + (hasHelp ? 4f + helpHeight : 0f);
        var core = MathF.Max(ToggleSize.Y, block);
        var top = separator ? 12f : 0f;
        var rowHeight = top + core + (more ? 12f : 0f);

        if (separator)
            draw.AddLine(origin, origin + new Vector2(width, 0f), Theme.Pack(Theme.SurfaceBorder));

        var togglePos = origin + new Vector2(width - ToggleSize.X, top + ((core - ToggleSize.Y) / 2f));
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##autopotion_setting_" + id, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize, Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32f : 12f, 12f), 8f, Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textPos = origin + new Vector2(0f, top + ((core - block) / 2f));
        DrawWrapped(draw, textPos, label, textWidth, Theme.TextPrimary);
        if (hasHelp)
            DrawWrapped(draw, textPos + new Vector2(0f, labelHeight + 4f), help!, textWidth, Theme.TextSecondary, Theme.SecondaryScale);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, MathF.Max(1f, rowHeight - Theme.ItemSpacing.Y)));
        return clicked;
    }

    internal static bool ThresholdSlider(string id, string label, ref float value)
    {
        TextBlock(label, Theme.SecondaryScale, Theme.TextSecondary, 4f);
        var width = Avail();
        var padY = MathF.Max(0f, (28f - ImGui.GetFontSize()) / 2f);
        ImGui.SetNextItemWidth(MathF.Max(1f, width));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 16f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.FramePadding.X, padY));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
        var changed = ImGui.SliderFloat("##autopotion_threshold_" + id, ref value, 1f, 99f, "%.0f%%");
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(4);
        return changed;
    }

    /// <summary>A requirement row: state glyph, label, and a state-coloured value on the right, all on one line.</summary>
    internal static void Requirement(string label, string value, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lineHeight = LineHeight();
        var rowHeight = lineHeight + 12f;
        var mid = origin.Y + (rowHeight / 2f);
        var slot = origin.X + 10f;

        if (state == UiState.Ready)
        {
            var color = Theme.Pack(Theme.StateGreen);
            draw.AddLine(new Vector2(slot - 5f, mid), new Vector2(slot - 1.5f, mid + 3.5f), color, 1.5f);
            draw.AddLine(new Vector2(slot - 1.5f, mid + 3.5f), new Vector2(slot + 5f, mid - 4f), color, 1.5f);
        }
        else
        {
            var color = Theme.Pack(state is UiState.Blocked or UiState.Missing ? Theme.StateAmber : Theme.TextSecondary);
            draw.AddLine(new Vector2(slot - 4f, mid), new Vector2(slot + 4f, mid), color, 1.5f);
        }

        var valueColor = state switch
        {
            UiState.Ready => Theme.StateGreen,
            UiState.Missing or UiState.Blocked => Theme.StateAmber,
            _ => Theme.TextSecondary,
        };
        var valueWidth = TextWidth(value, Theme.SecondaryScale);
        var textY = origin.Y + ((rowHeight - lineHeight) / 2f);
        var labelRoom = width - 24f - valueWidth - 12f;
        var shown = Fit(label, labelRoom);
        DrawText(draw, new Vector2(origin.X + 24f, textY), shown, Theme.TextSecondary);
        if (shown != label)
            HoverTip(new Vector2(origin.X + 24f, origin.Y), new Vector2(origin.X + 24f + labelRoom, origin.Y + rowHeight), label);
        DrawText(draw, new Vector2(origin.X + width - valueWidth, origin.Y + ((rowHeight - LineHeight(Theme.SecondaryScale)) / 2f)),
            value, valueColor, Theme.SecondaryScale);
        ImGui.Dummy(new Vector2(width, rowHeight - Theme.ItemSpacing.Y));
    }

    // ---- stat cards ----

    internal static float StatHeight(bool compact) => compact
        ? 12f + MathF.Max(LineHeight(Theme.SecondaryScale), LineHeight(1.35f)) + 8f + Theme.BarHeight + 12f
        : 16f + LineHeight(Theme.SecondaryScale) + 8f + LineHeight(1.7f) + 8f + Theme.BarHeight + 8f + LineHeight(Theme.SecondaryScale) + 16f;

    internal static void StatCard(ImDrawListPtr draw, Vector2 pos, float width, bool compact, string label, string value, string? cap, float fraction, string note)
    {
        var height = StatHeight(compact);
        var pad = compact ? 12f : 16f;
        var inner = width - (2f * pad);
        draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);

        var labelHeight = LineHeight(Theme.SecondaryScale);
        var valueScale = compact ? 1.35f : 1.7f;
        var valueHeight = LineHeight(valueScale);
        var capHeight = LineHeight(Theme.SecondaryScale);
        var y = pos.Y + (compact ? 12f : 16f);
        var x = pos.X + pad;

        if (compact)
        {
            var rowHeight = MathF.Max(labelHeight, valueHeight);
            Diamond(draw, new Vector2(x + 5f, y + (rowHeight / 2f)), 5f, Theme.Pack(Theme.Accent));
            DrawText(draw, new Vector2(x + 16f, y + ((rowHeight - labelHeight) / 2f)), label, Theme.TextSecondary, Theme.SecondaryScale);
            var capWidth = cap is null ? 0f : TextWidth(cap, Theme.SecondaryScale) + 8f;
            var valueWidth = TextWidth(value, valueScale);
            var right = pos.X + width - pad;
            DrawText(draw, new Vector2(right - capWidth - valueWidth, y + ((rowHeight - valueHeight) / 2f)), value, Theme.TextPrimary, valueScale);
            if (cap is not null)
                DrawText(draw, new Vector2(right - capWidth + 8f, y + rowHeight - capHeight - (0.08f * valueHeight)), cap, Theme.TextSecondary, Theme.SecondaryScale);
            y += rowHeight + 8f;
        }
        else
        {
            Diamond(draw, new Vector2(x + 5f, y + (labelHeight / 2f)), 5f, Theme.Pack(Theme.Accent));
            DrawText(draw, new Vector2(x + 16f, y), Fit(label, inner - 16f, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
            y += labelHeight + 8f;
            DrawText(draw, new Vector2(x, y), value, Theme.TextPrimary, 1.7f);
            if (cap is not null)
            {
                var valueWidth = TextWidth(value, 1.7f);
                DrawText(draw, new Vector2(x + valueWidth + 8f, y + valueHeight - capHeight - (0.08f * valueHeight)), cap, Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + 8f;
        }

        var barMin = new Vector2(x, y);
        var barMax = new Vector2(x + inner, y + Theme.BarHeight);
        draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2f);
        var filled = Math.Clamp(fraction, 0f, 1f);
        if (filled > 0f)
            draw.AddRectFilled(barMin, new Vector2(barMin.X + (inner * filled), barMax.Y), Theme.Pack(Theme.StateGreen), 2f);

        if (!compact)
        {
            y += Theme.BarHeight + 8f;
            var shown = Fit(note, inner, Theme.SecondaryScale);
            DrawText(draw, new Vector2(x, y), shown, fraction >= 1f ? Theme.StateGreen : Theme.TextSecondary, Theme.SecondaryScale);
            if (shown != note)
                HoverTip(new Vector2(x, y), new Vector2(x + inner, y + capHeight), note);
        }
    }

    // ---- footer button ----

    internal static Vector2 StopSize() => new(MathF.Max(72f, TextWidth("Stop") + 32f), 36f);

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##autopotion_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Alpha(Theme.StateRed, 0.14f) : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Alpha(Theme.StateRed, hovered ? 0.85f : 0.55f)), Radius);
        const string label = "Stop";
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, Theme.TextPrimary);
        return clicked;
    }
}
