using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace ArmoireAutoFill.Windows;

internal readonly record struct ArmoireNavItem(string Id, string Label);

internal readonly record struct ArmoireStat(string Label, string Value, string? Cap, string Note, float? Fraction);

/// <summary>
///     Drawn controls for the Armoire Auto-Fill shell and its content cards. Every measurement derives from the live
///     font (CalcTextSize at the draw size, line height) and the 4 px Theme grid, never from constants tuned for one font.
/// </summary>
internal static class ArmoireChrome
{
    public const float Radius = 2 * Theme.Unit;
    public const float Gap = 4 * Theme.Unit;
    public const float ControlHeight = 9 * Theme.Unit;
    public const float PageTitleScale = 1.35f;
    public const float StatValueScale = 1.7f;
    public const string BrandName = "Armoire Auto-Fill";

    /// <summary>Bottom space kept under the last card: the body padding plus the gap every card leaves.</summary>
    public const float PageBottomReserve = 2 * Gap;

    /// <summary>Windows shorter than this drop optional chrome so the page body keeps room.</summary>
    public const float ShortWindow = 460;

    private static readonly Vector2 ToggleSize = new(44, 24);
    private static readonly Stack<float> Rights = new();

    // ---- layout: the right edge available to the current card or column ----

    public static void ResetLayout()
    {
        Rights.Clear();
        ArmoireCard.FillBottom = float.NaN;
    }

    public static void PushRight(float screenX) => Rights.Push(screenX);

    public static void PopRight()
    {
        if (Rights.Count > 0)
            Rights.Pop();
    }

    /// <summary>Width from the cursor to the inner right edge of the enclosing card or column.</summary>
    public static float Avail()
        => Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    /// <summary>Move below an element of the given size and leave <paramref name="gap"/> before the next one.</summary>
    public static void Advance(Vector2 origin, float width, float height, float gap = Gap)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(MathF.Max(1, width), MathF.Max(0, height + gap - Theme.ItemSpacing.Y)));
    }

    public static float Gutter(float windowWidth) => windowWidth >= 900 ? 6 * Theme.Unit : windowWidth >= 560 ? 4 * Theme.Unit : 3 * Theme.Unit;

    // ---- text ----

    // Measurements run at the same font size the text is drawn at (window font scale), because the font bakes each
    // size separately; scaling a base-size measurement under-reports widths and wraps differently from the draw.
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

    public static float LineHeight(float scale = 1f) => Measure(scale, ImGui.GetTextLineHeight);

    public static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    public static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        if (scale == 1f)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    public static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => DrawText(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    public static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : MathF.Max(LineHeight(scale), Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(8, width)).Y));

    /// <summary>Draw wrapped text with the draw list; returns the height used.</summary>
    public static float DrawWrapped(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return 0;
        var height = WrappedHeight(text, width, scale);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, MathF.Max(8, width));
        return height;
    }

    public static string Fit(string text, float width, float scale = 1f)
    {
        if (width <= 0)
            return string.Empty;
        if (TextWidth(text, scale) <= width + 1f)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length] + ellipsis, scale) > width)
            length--;
        return length == 0 ? string.Empty : text[..length].TrimEnd() + ellipsis;
    }

    /// <summary>Clamp wrapped text to a number of lines, ending in an ellipsis when it is cut.</summary>
    public static string ClampLines(string text, float width, float scale, int maxLines)
    {
        var limit = (maxLines * LineHeight(scale)) + 1;
        if (WrappedHeight(text, width, scale) <= limit)
            return text;
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (WrappedHeight(text[..mid].TrimEnd() + "…", width, scale) <= limit)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo == 0 ? "…" : text[..lo].TrimEnd() + "…";
    }

    /// <summary>Single line that is elided to fit; the full text shows on hover.</summary>
    public static void DrawFitted(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        var fitted = Fit(text, width, scale);
        DrawText(draw, pos, fitted, color, scale);
        if (fitted != text && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, LineHeight(scale))))
            ImGui.SetTooltip(text);
    }

    public static void DrawFittedRight(ImDrawListPtr draw, float right, float y, string text, float width, Vector4 color, float scale = 1f)
    {
        var fitted = Fit(text, width, scale);
        DrawTextRight(draw, right, y, fitted, color, scale);
        if (fitted != text && ImGui.IsMouseHoveringRect(new Vector2(right - width, y), new Vector2(right, y + LineHeight(scale))))
            ImGui.SetTooltip(text);
    }

    /// <summary>A wrapped paragraph in the secondary colour, measured from the live font.</summary>
    public static void Paragraph(string text, float scale = Theme.SecondaryScale, Vector4? color = null, float gap = 2 * Theme.Unit)
    {
        if (text.Length == 0)
            return;
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var height = DrawWrapped(ImGui.GetWindowDrawList(), pos, text, width, color ?? Theme.TextSecondary, scale);
        Advance(pos, width, height, gap);
    }

    /// <summary>The state's mark colour; neutral states read as ordinary secondary text.</summary>
    private static Vector4 TextColor(UiState state)
        => state is UiState.Idle or UiState.Empty or UiState.NeedsSetup ? Theme.TextSecondary : Copy.MarkColor(state);

    // ---- shapes ----

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

    internal static void Chevron(ImDrawListPtr draw, Vector2 center, Vector4 color, bool right = false)
    {
        var c = Theme.Pack(color);
        if (right)
        {
            draw.AddLine(center + new Vector2(-2, -5), center + new Vector2(3, 0), c, 1.5f);
            draw.AddLine(center + new Vector2(3, 0), center + new Vector2(-2, 5), c, 1.5f);
        }
        else
        {
            draw.AddLine(center + new Vector2(-5, -2), center + new Vector2(0, 3), c, 1.5f);
            draw.AddLine(center + new Vector2(0, 3), center + new Vector2(5, -2), c, 1.5f);
        }
    }

    // ---- top bar ----

    private static float BrandWidth() => (3 * Theme.Unit) + 24 + (3 * Theme.Unit) + TextWidth(BrandName, Theme.TitleScale) + (4 * Theme.Unit);

    /// <summary>One width for every state so the pill never moves between states.</summary>
    private static float PillWidth()
    {
        var words = Enum.GetValues<UiState>().Select(Copy.Word);
        return (3 * Theme.Unit) + (2 * Theme.DotRadius) + (2 * Theme.Unit) + words.Max(w => TextWidth(w)) + (4 * Theme.Unit);
    }

    /// <summary>
    ///     Full-bleed top band: brand, navigation pills, status pill and close button, laid out from the window edge.
    ///     Returns the y (from the window top) where the next band starts.
    /// </summary>
    internal static float TopBar(Vector2 winPos, Vector2 winSize, float gutter, IReadOnlyList<ArmoireNavItem> navigation, int selected,
        Action<int> navigate, UiState state, Action closeRequested)
    {
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var wideGap = 4 * Theme.Unit;
        var inner = winSize.X - (2 * gutter);
        var padY = winSize.Y < ShortWindow ? 2.5f * Theme.Unit : 3.5f * Theme.Unit;
        var rowHeight = 10 * Theme.Unit;
        var showNav = navigation.Count > 1;
        var pillSize = new Vector2(PillWidth(), ControlHeight);
        var closeSize = new Vector2(ControlHeight);
        var rightWidth = pillSize.X + gap + closeSize.X;
        var brandSize = new Vector2(MathF.Min(BrandWidth(), MathF.Max(80, inner - rightWidth - gap)), rowHeight);
        var navSizes = navigation.Select(item => new Vector2(
            MathF.Max(18 * Theme.Unit, TextWidth(item.Label) + (8 * Theme.Unit)), ControlHeight)).ToArray();
        var navWidth = navSizes.Sum(size => size.X) + (Math.Max(0, navSizes.Length - 1) * gap);
        var oneRow = !showNav || inner >= brandSize.X + wideGap + navWidth + wideGap + rightWidth;

        var controlY = padY + ((rowHeight - ControlHeight) / 2f);
        var brandPos = new Vector2(gutter, padY);
        var closePos = new Vector2(winSize.X - gutter - closeSize.X, controlY);
        var pillPos = new Vector2(closePos.X - gap - pillSize.X, controlY);
        var positions = new Vector2[navigation.Count];
        float barHeight;
        if (oneRow)
        {
            var x = gutter + brandSize.X + wideGap;
            for (var i = 0; i < navigation.Count; i++)
            {
                positions[i] = new Vector2(x, controlY);
                x += navSizes[i].X + gap;
            }
            barHeight = padY + rowHeight + padY;
        }
        else
        {
            // Compact: brand, status and close on the first row; the pills share the second row equally.
            var rowY = padY + rowHeight + gap;
            var each = (inner - ((navigation.Count - 1) * gap)) / navigation.Count;
            for (var i = 0; i < navigation.Count; i++)
            {
                navSizes[i] = new Vector2(each, ControlHeight);
                positions[i] = new Vector2(gutter + (i * (each + gap)), rowY);
            }
            barHeight = rowY + ControlHeight + padY;
        }

        DrawBand(draw, winPos, winSize.X, 0, barHeight);
        DrawBrand(draw, winPos + brandPos, brandSize);
        for (var i = 0; i < navigation.Count; i++)
            if (showNav && NavButton(draw, navigation[i].Id, navigation[i].Label, winPos + positions[i], navSizes[i], i == selected))
                navigate(i);
        StatusPill(draw, winPos + pillPos, pillSize, Copy.Word(state), state);
        if (CloseButton(draw, winPos + closePos, closeSize))
            closeRequested();
        return barHeight + 1;
    }

    private static void DrawBand(ImDrawListPtr draw, Vector2 winPos, float winWidth, float top, float height)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + new Vector2(winWidth, top + height + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(winWidth, top + height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + height), winPos + new Vector2(winWidth, top + height), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    private static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##armoire_close", size);
        var hovered = ImGui.IsItemHovered();
        var color = hovered ? Theme.Accent : Theme.TextSecondary;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var center = pos + (size / 2f);
        draw.AddLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), Theme.Pack(color), 1.5f);
        draw.AddLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), Theme.Pack(color), 1.5f);
        if (hovered)
            ImGui.SetTooltip("Close");
        return clicked;
    }

    private static void DrawBrand(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2((3 * Theme.Unit) + 12, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        var textLeft = (3 * Theme.Unit) + 24 + (3 * Theme.Unit);
        DrawFitted(draw, pos + new Vector2(textLeft, (size.Y - LineHeight(Theme.TitleScale)) / 2f), BrandName,
            size.X - textLeft - (2 * Theme.Unit), Theme.TextPrimary, Theme.TitleScale);
    }

    /// <summary>Outlined navigation pill: dim label, Accent label and tint when selected, label centred.</summary>
    internal static bool NavButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = Fit(label, size.X - (2 * Theme.Unit));
        var textSize = ImGui.CalcTextSize(shown);
        draw.AddText(pos + ((size - textSize) / 2f),
            Theme.Pack(selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary), shown);
        return clicked;
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        var textSize = ImGui.CalcTextSize(label);
        var contentWidth = (Theme.DotRadius * 2) + (2 * Theme.Unit) + textSize.X;
        var left = pos.X + ((size.X - contentWidth) / 2f);
        draw.AddCircleFilled(new Vector2(left + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(Copy.MarkColor(state)));
        draw.AddText(new Vector2(left + (Theme.DotRadius * 2) + (2 * Theme.Unit), pos.Y + ((size.Y - textSize.Y) / 2f)),
            Theme.Pack(TextColor(state)), label);
    }

    // ---- buttons ----

    /// <summary>Outlined button at the cursor; the primary action is Accent tinted, the rest neutral.</summary>
    internal static bool Button(string id, string label, bool emphasized = false, bool enabled = true, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var available = Avail();
        var buttonWidth = width > 0 ? MathF.Min(width, available) : MathF.Min(available, MathF.Max(30 * Theme.Unit, TextWidth(label) + (8 * Theme.Unit)));
        var size = new Vector2(buttonWidth, ControlHeight);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        var border = !enabled ? Theme.SurfaceBorder : emphasized || hovered ? Theme.Accent : Theme.SurfaceBorder;
        var fill = emphasized && enabled ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(border), Radius);
        var text = Fit(label, size.X - (2 * Theme.Unit));
        var textSize = ImGui.CalcTextSize(text);
        draw.AddText(pos + ((size - textSize) / 2f),
            Theme.Pack(!enabled ? Theme.TextSecondary with { W = 0.7f } : emphasized ? Theme.Accent : Theme.TextPrimary), text);
        Advance(pos, size.X, size.Y, 3 * Theme.Unit);
        return clicked;
    }

    /// <summary>A collapsible section row with a chevron; the caller keeps the open state.</summary>
    internal static bool Disclosure(string id, string label, bool open)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var size = new Vector2(width, 8 * Theme.Unit);
        var clicked = ImGui.InvisibleButton("##disclosure_" + id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            draw.AddRectFilled(pos, pos + size, Theme.Pack(new Vector4(1f, 1f, 1f, 0.05f)), Radius);
        Chevron(draw, pos + new Vector2(Gap / 2f + 2, size.Y / 2f), open || hovered ? Theme.Accent : Theme.TextSecondary, right: !open);
        DrawFitted(draw, pos + new Vector2(Gap + 8, (size.Y - LineHeight()) / 2f), label, width - Gap - 8 - (2 * Theme.Unit), Theme.TextPrimary);
        Advance(pos, width, size.Y, 2 * Theme.Unit);
        return clicked;
    }

    // ---- page header ----

    /// <summary>
    ///     Icon tile, title and description with an optional two-line meta block: right aligned on wide windows,
    ///     merged into one muted line under the description otherwise.
    /// </summary>
    internal static void PageHeader(string title, string purpose, string count, string? subtext = null)
    {
        subtext ??= string.Empty;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0, Theme.Unit);
        var width = Avail();
        var windowWidth = ImGui.GetWindowWidth();
        var wide = windowWidth >= 1000;
        var shortWindow = ImGui.GetWindowHeight() < ShortWindow - 120;
        const float tile = 11 * Theme.Unit;
        var textX = origin.X + tile + Gap;
        var textWidth = MathF.Max(80, width - tile - Gap);

        float metaWidth = 0;
        if (wide && (count.Length > 0 || subtext.Length > 0))
            metaWidth = MathF.Min(0.4f * windowWidth, MathF.Max(TextWidth(count, Theme.SecondaryScale), TextWidth(subtext, Theme.SecondaryScale)));
        var leftWidth = textWidth - (metaWidth > 0 ? metaWidth + Gap : 0);

        var shownTitle = ClampLines(title, leftWidth, PageTitleScale, wide ? 1 : 2);
        var titleHeight = WrappedHeight(shownTitle, leftWidth, PageTitleScale);
        DrawWrapped(draw, new Vector2(textX, origin.Y), shownTitle, leftWidth, Theme.TextPrimary, PageTitleScale);
        var descTop = origin.Y + titleHeight + Theme.Unit;
        var shownPurpose = ClampLines(purpose, leftWidth, Theme.SecondaryScale, wide || shortWindow ? 1 : 3);
        var descHeight = DrawWrapped(draw, new Vector2(textX, descTop), shownPurpose, leftWidth, Theme.TextSecondary, Theme.SecondaryScale);
        var textBottom = descTop + descHeight;

        if (wide)
        {
            var right = origin.X + width;
            var firstLine = LineHeight(PageTitleScale);
            if (count.Length > 0)
                DrawFittedRight(draw, right, origin.Y + ((firstLine - LineHeight(Theme.SecondaryScale)) / 2f), count, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
            if (subtext.Length > 0)
                DrawFittedRight(draw, right, descTop, subtext, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else if (!shortWindow)
        {
            var meta = count.Length > 0 && subtext.Length > 0 ? $"{count} · {subtext}" : count.Length > 0 ? count : subtext;
            if (meta.Length > 0)
            {
                var y = textBottom + Theme.Unit;
                DrawFitted(draw, new Vector2(textX, y), meta, leftWidth, Theme.TextSecondary, Theme.SecondaryScale);
                textBottom = y + LineHeight(Theme.SecondaryScale);
            }
        }

        var height = MathF.Max(tile, textBottom - origin.Y);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), Radius);
        Diamond(draw, origin + new Vector2(tile / 2f), 11, Theme.Pack(Theme.Accent));
        Advance(origin - new Vector2(0, Theme.Unit), width, height + Theme.Unit, Gap + Theme.Unit);
    }

    // ---- stat cards ----

    /// <summary>Equal-height stat cards: label with diamond, large value with a small cap, bar track, note.</summary>
    internal static void StatCards(params ArmoireStat[] stats)
    {
        if (stats.Length == 0)
            return;
        var draw = ImGui.GetWindowDrawList();
        var available = Avail();
        var gap = Gap;
        var columns = stats.Length == 4 && available >= (4 * 210) + (3 * gap) ? 4
            : stats.Length <= 3 && available >= (3 * 220) + (2 * gap) ? stats.Length
            : available >= (2 * 190) + gap ? Math.Min(2, stats.Length)
            : 1;
        var width = (available - ((columns - 1) * gap)) / columns;
        const float pad = Gap;
        var labelHeight = LineHeight(Theme.SecondaryScale);
        var valueHeight = LineHeight(StatValueScale);
        // The note may wrap; every card takes the tallest so the grid stays level.
        var noteWidths = stats.Select((_, i) => (columns > 1 && stats.Length % columns == 1 && i == stats.Length - 1 ? available : width) - (2 * pad));
        var noteHeight = stats.Zip(noteWidths, (s, w) => WrappedHeight(s.Note, w, Theme.SecondaryScale)).Max();
        var height = pad + labelHeight + (2 * Theme.Unit) + valueHeight + (2 * Theme.Unit) + Theme.BarHeight + (2 * Theme.Unit) + noteHeight + pad;
        var origin = ImGui.GetCursorScreenPos();
        var rows = 0;
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var column = i % columns;
            var row = i / columns;
            rows = row + 1;
            var cardWidth = columns > 1 && i == stats.Length - 1 && column == 0 && stats.Length % columns == 1 ? available : width;
            var pos = origin + new Vector2(column * (width + gap), row * (height + gap));
            draw.AddRectFilled(pos, pos + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), Radius);
            draw.AddRect(pos, pos + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), Radius);

            var y = pos.Y + pad;
            Diamond(draw, new Vector2(pos.X + pad + 5, y + (labelHeight / 2f)), 5, Theme.Pack(Theme.Accent));
            DrawFitted(draw, new Vector2(pos.X + pad + 16, y), stat.Label, cardWidth - (2 * pad) - 16, Theme.TextSecondary, Theme.SecondaryScale);
            y += labelHeight + (2 * Theme.Unit);

            var empty = stat.Value == "—";
            DrawText(draw, new Vector2(pos.X + pad, y), stat.Value, empty ? Theme.TextSecondary : Theme.TextPrimary, StatValueScale);
            if (stat.Cap is { } cap)
            {
                var trimmed = cap.TrimStart('/', ' ');
                var capText = int.TryParse(trimmed, out _) ? "/ " + trimmed : trimmed;
                var capHeight = LineHeight(Theme.SecondaryScale);
                var capX = pos.X + pad + TextWidth(stat.Value, StatValueScale) + (2 * Theme.Unit);
                DrawText(draw, new Vector2(capX, y + valueHeight - capHeight - (valueHeight * 0.08f)), capText, Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + (2 * Theme.Unit);

            var min = new Vector2(pos.X + pad, y);
            var max = new Vector2(pos.X + cardWidth - pad, y + Theme.BarHeight);
            draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), Theme.BarHeight / 2f);
            if (stat.Fraction is { } fraction && fraction > 0)
                draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), Theme.BarHeight / 2f);
            y += Theme.BarHeight + (2 * Theme.Unit);

            DrawWrapped(draw, new Vector2(pos.X + pad, y), stat.Note, cardWidth - (2 * pad),
                stat.Fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary, Theme.SecondaryScale);
        }
        Advance(origin, available, (rows * height) + ((rows - 1) * gap));
    }

    // ---- cards and columns ----

    /// <summary>Rounded card measured from its content; <paramref name="fill"/> stretches it to its column's bottom.</summary>
    internal static void Card(string id, float minHeight, Action content, float width = 0, bool fill = false, float padY = ArmoireCard.Padding)
        => ArmoireCard.Draw(id, minHeight, content, width, fill, padY);

    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();

    /// <summary>
    ///     Two columns (main and rail) whose last cards share one bottom edge; below the breakpoint they stack.
    ///     Heights come from the previous frame, so the first frame after a layout change settles in one more frame.
    /// </summary>
    internal static void Columns(string id, Action main, Action rail, float breakpoint = 820, float? railFraction = null, bool fillPage = false)
    {
        var width = Avail();
        var saved = ArmoireCard.FillBottom;
        if (width < breakpoint)
        {
            ArmoireCard.FillBottom = float.NaN;
            main();
            rail();
            ArmoireCard.FillBottom = saved;
            return;
        }
        var origin = ImGui.GetCursorScreenPos();
        var railWidth = railFraction is { } fraction ? (width - Gap) * fraction : Math.Clamp(width * 0.26f, 300, 360);
        var mainWidth = width - Gap - railWidth;
        ColumnHeights.TryGetValue(id, out var previous);
        var target = MathF.Max(previous.Main, previous.Rail);
        if (fillPage)
            target = MathF.Max(target, ImGui.GetWindowHeight() - PageBottomReserve - ImGui.GetCursorPos().Y);

        // Each column is a group so that, after every item, the cursor returns to the column's own left edge.
        PushRight(origin.X + mainWidth);
        ArmoireCard.BeginColumn(origin.Y + target);
        ImGui.SetCursorScreenPos(origin);
        ImGui.BeginGroup();
        main();
        ImGui.EndGroup();
        var mainBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var mainNatural = mainBottom - Gap - origin.Y - ArmoireCard.LastStretch;
        PopRight();

        PushRight(origin.X + width);
        ArmoireCard.BeginColumn(origin.Y + target);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + mainWidth + Gap, origin.Y));
        ImGui.BeginGroup();
        rail();
        ImGui.EndGroup();
        var railBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var railNatural = railBottom - Gap - origin.Y - ArmoireCard.LastStretch;
        PopRight();

        ColumnHeights[id] = (mainNatural, railNatural);
        ArmoireCard.FillBottom = saved;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom) - Theme.ItemSpacing.Y));
        ImGui.Dummy(new Vector2(width, 0));
    }

    /// <summary>Card heading: title with an optional caption underneath.</summary>
    internal static void CardHeader(string title, string? caption = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var titleHeight = LineHeight(Theme.TitleScale);
        DrawFitted(draw, pos, title, width, Theme.TextPrimary, Theme.TitleScale);
        var height = titleHeight;
        if (!string.IsNullOrEmpty(caption))
        {
            height += Theme.Unit;
            height += DrawWrapped(draw, pos + new Vector2(0, height), caption, width, Theme.TextSecondary, Theme.SecondaryScale);
        }
        Advance(pos, width, height, 3 * Theme.Unit);
    }

    /// <summary>A state dot and word in the state's colour, one line.</summary>
    internal static void StateLine(UiState state, string text)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lineHeight = LineHeight();
        draw.AddCircleFilled(pos + new Vector2(Theme.DotRadius, lineHeight / 2f), Theme.DotRadius, Theme.Pack(Copy.MarkColor(state)));
        DrawFitted(draw, pos + new Vector2((2 * Theme.DotRadius) + (2 * Theme.Unit), 0), text, width - (2 * Theme.DotRadius) - (2 * Theme.Unit), TextColor(state));
        Advance(pos, width, lineHeight, Theme.Unit);
    }

    // ---- notices ----

    private static bool IsAlert(UiState state) => state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;

    /// <summary>Split "Lead — body" into the lead and the secondary body.</summary>
    internal static void Notice(string text, UiState state, string? tooltip = null)
    {
        var split = text.IndexOf(" — ", StringComparison.Ordinal);
        if (split > 0)
            Notice(text[..split], text[(split + 3)..], state, tooltip);
        else
            Notice(string.Empty, text, state, tooltip);
    }

    /// <summary>Rounded banner: tinted for alerts, neutral surface for information, with a state dot.</summary>
    internal static void Notice(string lead, string body, UiState state, string? tooltip = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var color = Copy.MarkColor(state);
        const float padX = Gap;
        const float padY = 3 * Theme.Unit;
        var textLeft = padX + (Theme.DotRadius * 2) + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textLeft - padX);
        var lineHeight = LineHeight();
        var leadText = lead.Length > 0 ? lead + " —" : string.Empty;
        var inline = lead.Length > 0 && body.Length > 0 && TextWidth(leadText) + (2 * Theme.Unit) + TextWidth(body) <= textWidth;
        float contentHeight;
        if (lead.Length > 0 && body.Length > 0 && !inline)
            contentHeight = WrappedHeight(lead, textWidth) + (Theme.Unit / 2f) + WrappedHeight(body, textWidth, Theme.SecondaryScale);
        else if (inline)
            contentHeight = lineHeight;
        else
            contentHeight = WrappedHeight(lead.Length > 0 ? lead : body, textWidth);
        var height = (2 * padY) + contentHeight;
        var alert = IsAlert(state);
        draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(alert ? color with { W = 0.12f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(alert ? color with { W = 0.45f } : Theme.SurfaceBorder), Radius);
        draw.AddCircleFilled(pos + new Vector2(padX + Theme.DotRadius, padY + (lineHeight / 2f)), Theme.DotRadius, Theme.Pack(color));
        var textPos = pos + new Vector2(textLeft, padY);
        if (inline)
        {
            DrawText(draw, textPos, leadText, Theme.TextPrimary);
            DrawText(draw, textPos + new Vector2(TextWidth(leadText) + (2 * Theme.Unit), 0), body, Theme.TextSecondary);
        }
        else if (lead.Length > 0)
        {
            var used = DrawWrapped(draw, textPos, lead, textWidth, Theme.TextPrimary);
            if (body.Length > 0)
                DrawWrapped(draw, textPos + new Vector2(0, used + (Theme.Unit / 2f)), body, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
            DrawWrapped(draw, textPos, body, textWidth, Theme.TextPrimary);
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, height)))
            ImGui.SetTooltip(tooltip);
        Advance(pos, width, height, 3 * Theme.Unit);
    }

    // ---- settings toggle and requirement rows ----

    /// <summary>Label and help on the left, the switch on the right, both centred on the row.</summary>
    internal static bool Toggle(string literalLabel, ref bool value, string? help, bool separator = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Max(40, width - ToggleSize.X - Gap);
        var labelHeight = WrappedHeight(literalLabel, textWidth);
        var helpHeight = string.IsNullOrWhiteSpace(help) ? 0 : WrappedHeight(help, textWidth, Theme.SecondaryScale);
        var blockHeight = labelHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        const float padY = 3 * Theme.Unit;
        var rowHeight = MathF.Max(ToggleSize.Y, blockHeight) + (2 * padY);

        if (separator)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##armoire_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textY = pos.Y + ((rowHeight - blockHeight) / 2f);
        DrawWrapped(draw, new Vector2(pos.X, textY), literalLabel, textWidth, Theme.TextPrimary);
        if (helpHeight > 0)
            DrawWrapped(draw, new Vector2(pos.X, textY + labelHeight + Theme.Unit), help!, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        Advance(pos, width, rowHeight, 0);
        return clicked;
    }

    /// <summary>Requirement row: fixed glyph slot, dim label, small coloured value at the inner right edge.</summary>
    internal static void Requirement(string label, string value, UiState state, bool ok)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lineHeight = LineHeight();
        var rowHeight = lineHeight + (3 * Theme.Unit);
        var textY = pos.Y + ((rowHeight - lineHeight) / 2f);
        const float slot = 5 * Theme.Unit;
        var mark = ok ? "✓" : "—";
        DrawText(draw, new Vector2(pos.X + ((slot - TextWidth(mark)) / 2f), textY), mark, ok ? Theme.StateGreen : Theme.TextSecondary);
        var valueColor = state switch
        {
            UiState.Ready => Theme.StateGreen,
            UiState.Missing or UiState.Blocked => Theme.StateAmber,
            _ => Theme.TextSecondary,
        };
        var valueWidth = TextWidth(value, Theme.SecondaryScale);
        DrawFitted(draw, new Vector2(pos.X + slot + Theme.Unit, textY), label, width - slot - Theme.Unit - valueWidth - Gap, Theme.TextSecondary);
        DrawTextRight(draw, pos.X + width, pos.Y + ((rowHeight - LineHeight(Theme.SecondaryScale)) / 2f), value, valueColor, Theme.SecondaryScale);
        Advance(pos, width, rowHeight, 0);
    }

    // ---- tooltips ----

    /// <summary>Begin a tooltip on an opaque raised surface with a border and padding. Pair with <see cref="EndTooltip"/>.</summary>
    internal static void BeginTooltip()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3 * Theme.Unit, 2 * Theme.Unit));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        // One step lighter than the cards so the tooltip reads as raised, with a visible border.
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(Theme.Surface.X + 0.05f, Theme.Surface.Y + 0.05f, Theme.Surface.Z + 0.05f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.TextSecondary with { W = 0.4f });
        ImGui.BeginTooltip();
    }

    internal static void EndTooltip()
    {
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    // ---- body ----

    /// <summary>
    ///     The one scrolling child under the bands. A transparent 1 px border makes the child honour its padding, which
    ///     gives every page the same gutter, and a soft fade marks content that continues under the footer.
    ///     Pair with <see cref="EndBody"/>.
    /// </summary>
    internal static void BeginBody(string id, Vector2 size, float gutter)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, Gap));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, (2 * Theme.Unit) + 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.BeginChild(id, size, true);
    }

    internal static void EndBody()
    {
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var draw = ImGui.GetWindowDrawList();
            var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
            var min = ImGui.GetWindowPos() + new Vector2(1, ImGui.GetWindowHeight() - (7 * Theme.Unit));
            draw.AddRectFilledMultiColor(min, ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth() - 1, ImGui.GetWindowHeight() - 1),
                bg & 0x00FFFFFFu, bg & 0x00FFFFFFu, bg, bg);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(5);
    }

    // ---- footer ----

    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)", RegexOptions.Compiled);

    internal static float FooterHeight(Vector2 winSize, bool showStop)
    {
        const float pad = 3 * Theme.Unit;
        var compact = winSize.Y < ShortWindow;
        var textBlock = compact ? LineHeight() : LineHeight() + Theme.Unit + LineHeight(Theme.SecondaryScale);
        if (winSize.X < 620 && !compact)
            return pad + textBlock + pad + (showStop ? ControlHeight + pad : 0);
        return pad + MathF.Max(textBlock, showStop ? ControlHeight : 0) + pad;
    }

    /// <summary>
    ///     Full-bleed footer band: state dot, a concise line and a detail line, with the Stop button at the right
    ///     (or on its own row on a narrow window). Windows too short for it keep only the first line.
    /// </summary>
    internal static void Footer(Vector2 winPos, Vector2 winSize, float gutter, UiState state, string action, string detail, bool showStop, out bool stopClicked)
    {
        stopClicked = false;
        var height = FooterHeight(winSize, showStop);
        var top = winPos.Y + winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        const float pad = 3 * Theme.Unit;
        var compact = winSize.Y < ShortWindow;
        var narrow = winSize.X < 620 && !compact;
        var lineHeight = LineHeight();
        var textBlock = compact ? lineHeight : lineHeight + Theme.Unit + LineHeight(Theme.SecondaryScale);
        var stopSize = new Vector2(MathF.Max(24 * Theme.Unit, TextWidth("Stop") + (8 * Theme.Unit)), ControlHeight);
        var textLeft = winPos.X + gutter + Gap;
        float textTop;
        float textRight;
        Vector2 stopPos;
        if (narrow)
        {
            textTop = top + pad;
            stopPos = new Vector2(winPos.X + gutter, textTop + textBlock + pad);
            stopSize.X = winSize.X - (2 * gutter);
            textRight = winPos.X + winSize.X - gutter;
        }
        else
        {
            textTop = top + ((height - textBlock) / 2f);
            stopPos = new Vector2(winPos.X + winSize.X - gutter - stopSize.X, top + ((height - stopSize.Y) / 2f));
            textRight = showStop ? stopPos.X - Gap : winPos.X + winSize.X - gutter;
        }

        draw.AddCircleFilled(new Vector2(winPos.X + gutter + Theme.Unit, textTop + (lineHeight / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        var full = $"{Copy.Word(state)} · {action}";
        var concise = $"{Copy.Word(state)} · {Parenthetical.Replace(action, string.Empty)}";
        var shown = Fit(concise, textRight - textLeft);
        DrawText(draw, new Vector2(textLeft, textTop), shown, Theme.TextPrimary);
        var shownDetail = compact ? detail : Fit(detail, textRight - textLeft, Theme.SecondaryScale);
        if (!compact)
            DrawText(draw, new Vector2(textLeft, textTop + lineHeight + Theme.Unit), shownDetail, Theme.TextSecondary, Theme.SecondaryScale);
        if ((shown != full || shownDetail != detail || compact)
            && ImGui.IsMouseHoveringRect(new Vector2(textLeft, textTop), new Vector2(textRight, textTop + textBlock)))
            ImGui.SetTooltip(full + "\n" + detail);

        if (showStop)
        {
            ImGui.SetCursorScreenPos(stopPos);
            stopClicked = ImGui.InvisibleButton("Stop", stopSize);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(stopPos, stopPos + stopSize, Theme.Pack(Theme.StateRed with { W = hovered ? 0.28f : 0.16f }), Radius);
            draw.AddRect(stopPos, stopPos + stopSize, Theme.Pack(Theme.StateRed), Radius);
            var textSize = ImGui.CalcTextSize("Stop");
            draw.AddText(stopPos + ((stopSize - textSize) / 2f), Theme.Pack(Theme.StateRed), "Stop");
        }
    }
}

/// <summary>Content-measured rounded cards. Backgrounds never cover text and heights follow the live layout.</summary>
internal static class ArmoireCard
{
    public const float Padding = 4 * Theme.Unit;
    private static int _depth;

    /// <summary>Screen Y that cards marked <c>fill</c> stretch down to, or NaN when columns are stacked.</summary>
    public static float FillBottom { get; set; } = float.NaN;

    /// <summary>The stretch given to the most recent filling card, so column heights can be stored without it.</summary>
    public static float LastStretch { get; private set; }

    public static void BeginColumn(float bottom)
    {
        FillBottom = bottom;
        LastStretch = 0;
    }

    public static void Draw(string id, float minHeight, Action content, float width = 0, bool fill = false, float padY = Padding)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = ArmoireChrome.Avail();
        var cardWidth = width <= 0 ? available : MathF.Min(width, available);
        var topLevel = _depth == 0;
        if (topLevel)
        {
            _depth++;
            // Outer surface, nested row surfaces, then glyphs. Never split channels recursively.
            draw.ChannelsSplit(3);
        }
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.PushID(id);
            ImGui.SetCursorScreenPos(origin + new Vector2(Padding, padY));
            ArmoireChrome.PushRight(origin.X + cardWidth - Padding);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            ArmoireChrome.PopRight();
            ImGui.PopID();
            var contentBottom = ImGui.GetItemRectMax().Y;
            var natural = MathF.Max(minHeight, contentBottom - origin.Y + padY);
            var height = natural;
            if (fill && !float.IsNaN(FillBottom) && topLevel)
            {
                height = MathF.Max(natural, FillBottom - origin.Y);
                LastStretch = height - natural;
            }

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), ArmoireChrome.Radius);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), ArmoireChrome.Radius);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ArmoireChrome.Advance(origin + new Vector2(0, height), cardWidth, 0, topLevel ? ArmoireChrome.Gap : 2 * Theme.Unit);
        }
        finally
        {
            if (topLevel)
                _depth--;
        }
    }
}
