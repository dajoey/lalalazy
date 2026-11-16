using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyFishSitter;

/// <summary>One stat card: a label, a value, an optional "/ cap" and bar fraction, and a one-line note.</summary>
internal readonly record struct FishSitterStat(string Label, string Value, string? Cap, float? Fraction, string Note, bool Warn = false);

/// <summary>Drawn controls for the plugin's single-area settings shell.</summary>
internal static class FishSitterChrome
{
    internal const float Pad = 16;
    internal const float CardGap = 16;
    internal const float ControlHeight = 36;
    private const float ColumnGap = 16;
    private static readonly Vector2 ToggleSize = new(44, 24);

    private static readonly Stack<float> Rights = new();
    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();
    private static float _fillBottom = float.NaN;
    private static float _lastNaturalBottom;

    // ---- text measuring and drawing (always at the draw size, never base size times scale) ----

    private static T Measure<T>(float scale, Func<T> measure)
    {
        if (scale == 1f)
            return measure();
        ImGui.SetWindowFontScale(scale);
        try { return measure(); }
        finally { ImGui.SetWindowFontScale(1f); }
    }

    internal static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    internal static float LineHeight(float scale = 1f) => Measure(scale, () => ImGui.GetTextLineHeight());

    internal static float WrappedHeight(string text, float width, float scale = 1f) =>
        text.Length == 0 ? 0 : Measure(scale, () => ImGui.CalcTextSize(text, false, width).Y);

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    internal static void DrawWrapped(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, width);
    }

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1) =>
        DrawText(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    /// <summary>Elide one line to a width measured at its draw size.</summary>
    internal static string FitText(string text, float width, float scale = 1)
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

    /// <summary>Limit wrapped text to a number of lines, ending the last with an ellipsis.</summary>
    internal static string ClampLines(string text, float width, float scale, int maxLines)
    {
        var limit = (LineHeight(scale) * maxLines) + 1;
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
        return text[..low].TrimEnd() + ellipsis;
    }

    internal static void Tooltip(string text)
    {
        if (text.Length > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    // ---- inner right edge of the card or column being drawn ----

    internal static void PushRight(float screenX) => Rights.Push(screenX);

    internal static void PopRight() => Rights.Pop();

    internal static float Avail() =>
        Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    /// <summary>Vertical space inside a card, where the item spacing is zero.</summary>
    internal static void Gap(float height)
    {
        if (height > 0)
            ImGui.Dummy(new Vector2(1, height));
    }

    // ---- small drawn pieces ----

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

    /// <summary>The state colour for the status pill, footer dot and notices.</summary>
    internal static Vector4 StateColor(UiState state) => Copy.MarkColor(state);

    internal static float StatusPillWidth()
    {
        var widest = 0f;
        foreach (var state in Enum.GetValues<UiState>())
            widest = MathF.Max(widest, TextWidth(Copy.Word(state)));
        return MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + widest + 16);
    }

    internal static float BrandWidth() => 48 + TextWidth("LazyFishSitter", Theme.TitleScale) + 16;

    internal static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        var height = LineHeight(Theme.TitleScale);
        DrawText(draw, pos + new Vector2(48, (size.Y - height) / 2f), "LazyFishSitter", Theme.TextPrimary, Theme.TitleScale);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var color = StateColor(state);
        var label = Copy.Word(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        var textWidth = TextWidth(label);
        var contentWidth = (2 * Theme.DotRadius) + 8 + textWidth;
        var left = pos.X + ((size.X - contentWidth) / 2f);
        draw.AddCircleFilled(new Vector2(left + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        DrawText(draw, new Vector2(left + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), label,
            state is UiState.Idle or UiState.Empty or UiState.NeedsSetup ? Theme.TextSecondary : color);
    }

    internal static void Hairline(float inset = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        draw.AddLine(origin + new Vector2(inset, 0.5f), origin + new Vector2(width - inset, 0.5f), Theme.Pack(Theme.SurfaceBorder));
        ImGui.Dummy(new Vector2(width, 1));
    }

    /// <summary>A card heading: accent diamond and title at the title scale, then a 12 px gap.</summary>
    internal static void SectionTitle(string title)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var height = LineHeight(Theme.TitleScale);
        Diamond(draw, origin + new Vector2(6, height / 2f), 5, Theme.Pack(Theme.Accent));
        DrawText(draw, origin + new Vector2(18, 0), title, Theme.TextPrimary, Theme.TitleScale);
        ImGui.Dummy(new Vector2(Avail(), height));
        Gap(12);
    }

    /// <summary>A wrapped paragraph drawn at its measured height.</summary>
    internal static void Para(string text, float scale, Vector4 color)
    {
        var width = Avail();
        var height = WrappedHeight(text, width, scale);
        DrawWrapped(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), text, width, color, scale);
        ImGui.Dummy(new Vector2(width, height));
    }

    // ---- cards and columns ----

    /// <summary>
    /// A content-measured rounded card. Nested widgets read their right edge from <see cref="Avail"/>.
    /// With <paramref name="fill"/> the card stretches to the bottom its column shares with the other one.
    /// </summary>
    internal static void Card(Action content, float minHeight = 0, bool fill = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, Avail());
        float height;
        draw.ChannelsSplit(3);
        try
        {
            draw.ChannelsSetCurrent(2);
            PushRight(origin.X + width - Pad);
            ImGui.SetCursorScreenPos(origin + new Vector2(Pad, Pad));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Theme.ItemSpacing.X, 0));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, width - (2 * Pad)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            ImGui.PopStyleVar();
            PopRight();
            var contentBottom = ImGui.GetItemRectMax().Y;
            var natural = MathF.Max(minHeight, contentBottom - origin.Y + Pad);
            _lastNaturalBottom = origin.Y + natural;
            height = natural;
            if (fill && !float.IsNaN(_fillBottom))
                height = MathF.Max(natural, _fillBottom - origin.Y);

            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        }
        finally
        {
            draw.ChannelsMerge();
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, CardGap - Theme.ItemSpacing.Y));
    }

    /// <summary>
    /// Main column and right rail with equal bottoms: the last card of each column is drawn with fill.
    /// Below the breakpoint the two simply stack.
    /// </summary>
    internal static void Columns(string id, Action main, Action rail, float breakpoint)
    {
        var width = Avail();
        if (width < breakpoint)
        {
            main();
            rail();
            return;
        }

        var origin = ImGui.GetCursorScreenPos();
        var railWidth = Math.Clamp(0.26f * width, 300, 360);
        var mainWidth = width - ColumnGap - railWidth;
        ColumnHeights.TryGetValue(id, out var previous);
        var target = MathF.Max(previous.Main, previous.Rail);
        var savedFill = _fillBottom;
        _fillBottom = origin.Y + target;
        try
        {
            ImGui.SetCursorScreenPos(origin);
            ImGui.BeginGroup();
            PushRight(origin.X + mainWidth);
            main();
            PopRight();
            ImGui.EndGroup();
            var mainNatural = NaturalSince(origin.Y);

            ImGui.SetCursorScreenPos(origin + new Vector2(mainWidth + ColumnGap, 0));
            ImGui.BeginGroup();
            PushRight(origin.X + width);
            rail();
            PopRight();
            ImGui.EndGroup();
            var railNatural = NaturalSince(origin.Y);

            ColumnHeights[id] = (mainNatural, railNatural);
            var bottom = origin.Y + MathF.Max(target, MathF.Max(mainNatural, railNatural));
            ImGui.SetCursorScreenPos(new Vector2(origin.X, bottom - Theme.ItemSpacing.Y));
            ImGui.Dummy(new Vector2(width, 0));
        }
        finally
        {
            _fillBottom = savedFill;
        }
    }

    private static float NaturalSince(float originY) => _lastNaturalBottom - originY;

    // ---- stat cards ----

    internal static void StatCards(params FishSitterStat[] stats)
    {
        var available = Avail();
        const float gap = 16;
        var stacked = available < (3 * 200) + (2 * gap);
        var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
        var labelHeight = LineHeight(Theme.SecondaryScale);
        var valueHeight = LineHeight(1.7f);
        var height = Pad + labelHeight + 8 + valueHeight + 8 + Theme.BarHeight + 8 + labelHeight + Pad;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            var size = new Vector2(width, height);
            draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            var inner = width - (2 * Pad);
            var y = pos.Y + Pad;

            Diamond(draw, new Vector2(pos.X + Pad + 5, y + (labelHeight / 2f)), 5, Theme.Pack(Theme.Accent));
            DrawText(draw, new Vector2(pos.X + Pad + 16, y), FitText(stat.Label, inner - 16, Theme.SecondaryScale),
                Theme.TextSecondary, Theme.SecondaryScale);
            y += labelHeight + 8;

            var dash = stat.Value == "—";
            var capText = stat.Cap is null ? "" : (char.IsDigit(stat.Cap[0]) ? "/ " + stat.Cap : stat.Cap);
            var capWidth = capText.Length == 0 ? 0 : 8 + TextWidth(capText, Theme.SecondaryScale);
            var value = FitText(stat.Value, inner - capWidth, 1.7f);
            DrawText(draw, new Vector2(pos.X + Pad, y), value, dash ? Theme.TextSecondary : Theme.TextPrimary, 1.7f);
            if (capText.Length > 0)
            {
                var capHeight = LineHeight(Theme.SecondaryScale);
                var capX = pos.X + Pad + TextWidth(value, 1.7f) + 8;
                DrawText(draw, new Vector2(capX, y + valueHeight - capHeight - (0.08f * valueHeight)), capText,
                    Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + 8;

            var barMin = new Vector2(pos.X + Pad, y);
            var barMax = new Vector2(pos.X + Pad + inner, y + Theme.BarHeight);
            if (stat.Fraction is { } raw)
            {
                draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2);
                var fraction = Math.Clamp(raw, 0f, 1f);
                if (fraction > 0)
                    draw.AddRectFilled(barMin, new Vector2(barMin.X + (inner * fraction), barMax.Y),
                        Theme.Pack(stat.Warn ? Theme.StateAmber : Theme.StateGreen), 2);
            }
            y += Theme.BarHeight + 8;

            var noteColor = stat.Warn ? Theme.StateAmber : stat.Fraction >= 1f ? Theme.StateGreen : Theme.TextSecondary;
            DrawText(draw, new Vector2(pos.X + Pad, y), FitText(stat.Note, inner, Theme.SecondaryScale), noteColor, Theme.SecondaryScale);
            // Hover test only, with no item over the card, so a press on a card still drags the window.
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, pos + size) && TextWidth(stat.Note, Theme.SecondaryScale) > inner)
                ImGui.SetTooltip(stat.Note);
        }
        var totalHeight = stacked ? (stats.Length * height) + ((stats.Length - 1) * gap) : height;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, totalHeight + gap - Theme.ItemSpacing.Y));
    }

    // ---- rows ----

    /// <summary>Settings row: label and help at the left, 44x24 switch at the card's inner right edge.</summary>
    internal static bool Toggle(string literalLabel, ref bool value, string? help, bool separator = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Max(40, width - ToggleSize.X - 16);
        var hasHelp = !string.IsNullOrWhiteSpace(help);
        var labelHeight = WrappedHeight(literalLabel, textWidth);
        var helpHeight = hasHelp ? WrappedHeight(help!, textWidth, Theme.SecondaryScale) : 0;
        var block = labelHeight + (hasHelp ? 4 + helpHeight : 0);
        var rowHeight = MathF.Max(24, block) + 24;
        var draw = ImGui.GetWindowDrawList();

        if (separator)
            draw.AddLine(pos + new Vector2(0, 0.5f), pos + new Vector2(width, 0.5f), Theme.Pack(Theme.SurfaceBorder));

        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##fish_sitter_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textTop = pos.Y + ((rowHeight - block) / 2f);
        DrawWrapped(draw, new Vector2(pos.X, textTop), literalLabel, textWidth, Theme.TextPrimary);
        if (hasHelp)
            DrawWrapped(draw, new Vector2(pos.X, textTop + labelHeight + 4), help!, textWidth, Theme.TextSecondary, Theme.SecondaryScale);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, rowHeight));
        return clicked;
    }

    /// <summary>One key/value row: dim label, value at the secondary size aligned to the card's inner right.</summary>
    internal static void KeyValue(string label, string value, Vector4? valueColor = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var draw = ImGui.GetWindowDrawList();
        var lineHeight = LineHeight();
        var rowHeight = lineHeight + 12;
        var valueWidth = TextWidth(value, Theme.SecondaryScale);
        var shownLabel = FitText(label, width - valueWidth - 16);
        DrawText(draw, origin + new Vector2(0, (rowHeight - lineHeight) / 2f), shownLabel, Theme.TextSecondary);
        DrawTextRight(draw, origin.X + width, origin.Y + ((rowHeight - LineHeight(Theme.SecondaryScale)) / 2f),
            value, valueColor ?? Theme.TextPrimary, Theme.SecondaryScale);
        ImGui.Dummy(new Vector2(width, rowHeight));
        if (shownLabel != label)
            Tooltip(label);
    }

    internal static float ActionWidth(float minWidth, params string[] labels)
    {
        var widest = 0f;
        foreach (var label in labels)
            widest = MathF.Max(widest, TextWidth(label));
        return MathF.Max(minWidth, widest + 32);
    }

    /// <summary>A 36 px neutral action button: surface fill, accent outline and brighter text on hover.</summary>
    internal static bool ActionButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fish_sitter_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label,
            hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    /// <summary>A 36 px stop button: red tint at rest, stronger on hover, white label.</summary>
    internal static bool StopButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fish_sitter_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.24f : 0.12f }), 2 * Theme.Unit);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.85f : 0.55f }), 2 * Theme.Unit);
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, Theme.TextPrimary);
        return clicked;
    }

    /// <summary>The window close control: a neutral 36 px square with a drawn cross, accent outline on hover.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, float size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fish_sitter_close", new Vector2(size));
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + new Vector2(size), Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(pos, pos + new Vector2(size), Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
        var center = pos + new Vector2(size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), color, 1.5f);
        draw.AddLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), color, 1.5f);
        if (hovered)
        {
            ImGui.SetNextWindowPos(new Vector2(pos.X + size, pos.Y + size + 4), ImGuiCond.Always, new Vector2(1f, 0f));
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
}
