using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyGearCollector;

/// <summary>Drawn controls, measuring helpers and content-measured cards for the collector window.</summary>
internal static class CollectorChrome
{
    internal const float Radius = 8f;
    internal const float Control = 36f;

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

    internal static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    internal static float LineHeight(float scale = 1f) => Measure(scale, ImGui.GetTextLineHeight);

    internal static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1, width)).Y);

    internal static void PushRight(float screenX) => Rights.Push(screenX);

    internal static void PopRight() => Rights.Pop();

    internal static void ResetLayout()
    {
        Rights.Clear();
        _fillTop = 0;
        _fillHeight = float.NegativeInfinity;
        _stretch = 0;
    }

    /// <summary>Width left to the nearest card or column edge (a card's inner padding is respected).</summary>
    internal static float Avail()
        => Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    /// <summary>Elides to one line that fits <paramref name="width"/>.</summary>
    internal static string FitText(string text, float width, float scale = 1f)
    {
        if (width <= 0)
            return "";
        if (TextWidth(text, scale) <= width + 1f)
            return text;

        const string ellipsis = "…";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (TextWidth(text[..mid].TrimEnd() + ellipsis, scale) <= width)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo == 0 ? "" : text[..lo].TrimEnd() + ellipsis;
    }

    /// <summary>Wraps to at most <paramref name="maxLines"/> lines, ending in an ellipsis when it had to cut.</summary>
    internal static string ClampLines(string text, float width, float scale, int maxLines)
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

        return lo == 0 ? "" : text[..lo].TrimEnd() + "…";
    }

    // ---- text -------------------------------------------------------------------------------------------------

    internal static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f, float wrapWidth = 0f)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, wrapWidth);
    }

    internal static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => Text(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    /// <summary>Flowing paragraph drawn at measured height; the cursor moves below it plus <paramref name="after"/>.</summary>
    internal static float Para(string text, float scale, Vector4 color, float after = 4f, float? width = null)
    {
        var w = MathF.Max(1, width ?? Avail());
        var h = WrappedHeight(text, w, scale);
        var pos = ImGui.GetCursorScreenPos();
        Text(ImGui.GetWindowDrawList(), pos, text, color, scale, w);
        ImGui.Dummy(new Vector2(w, h));
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + h + after));
        return h;
    }

    internal static void Space(float height) => ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new Vector2(0, height));

    // ---- tooltips ---------------------------------------------------------------------------------------------

    // Tooltips float above cards, so they sit one step lighter than the card surface and carry a visible edge.
    private static readonly Vector4 TipBackground = Theme.Srgb(0x25, 0x28, 0x2D);

    private static void PushTipStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, TipBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.TextSecondary with { W = 0.35f });
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
    }

    private static void PopTipStyle()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);
    }

    /// <summary>A plain tooltip for elided text.</summary>
    internal static void Tip(string text, float wrap = 420f)
    {
        PushTipStyle();
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrap);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        PopTipStyle();
    }

    /// <summary>
    /// A tooltip with a bright first line and muted detail lines, anchored at a fixed point rather than the pointer so
    /// it does not sit on top of what was hovered.
    /// </summary>
    internal static void RichTip(Vector2 anchor, Vector2 pivot, string title, IReadOnlyList<string> details)
    {
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, pivot);
        PushTipStyle();
        ImGui.BeginTooltip();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        if (details.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.SetWindowFontScale(Theme.SecondaryScale);
            foreach (var line in details)
                ImGui.TextUnformatted(line);
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
        }

        ImGui.EndTooltip();
        PopTipStyle();
    }

    internal static void HoverTip(Vector2 min, Vector2 max, string text)
    {
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
            Tip(text);
    }

    // ---- shell controls ---------------------------------------------------------------------------------------

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

    internal const string BrandName = "Lazy Gear Collector";

    internal static float BrandWidth(float scale) => 48 + TextWidth(BrandName, scale) + 16;

    /// <summary>The brand chip; it returns to the Overview page when clicked.</summary>
    internal static bool Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size, float scale)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        var shown = FitText(BrandName, size.X - 48 - 16, scale);
        Text(draw, pos + new Vector2(48, (size.Y - LineHeight(scale)) / 2f), shown, Theme.TextPrimary, scale);
        return clicked;
    }

    /// <summary>Navigation pill: 36 high, label centred, selected in the accent.</summary>
    internal static bool NavButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = FitText(label, size.X - 16);
        Text(draw, pos + new Vector2((size.X - TextWidth(shown)) / 2f, (size.Y - LineHeight()) / 2f), shown,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    internal static float FeatureWidth(string label) => 16 + 12 + 8 + TextWidth(label, Theme.SecondaryScale) + 16 + 1;

    /// <summary>Sub-navigation pill with a radio marker.</summary>
    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var shown = FitText(label, size.X - 36 - 16, Theme.SecondaryScale);
        Text(draw, pos + new Vector2(36, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f), shown,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, Theme.SecondaryScale);
        return clicked;
    }

    internal static void Chevron(ImDrawListPtr draw, Vector2 center, Vector4 color)
    {
        var c = Theme.Pack(color);
        draw.AddLine(center + new Vector2(-5, -2), center + new Vector2(0, 3), c, 1.5f);
        draw.AddLine(center + new Vector2(0, 3), center + new Vector2(5, -2), c, 1.5f);
    }

    /// <summary>The widest state word fixes the pill width so it never shifts between states.</summary>
    internal static float StatusPillWidth()
        => MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var mark = Copy.MarkColor(state);
        var neutral = state is UiState.Idle or UiState.NeedsSetup or UiState.Empty;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);

        var content = (2 * Theme.DotRadius) + 8 + TextWidth(label);
        var x = pos.X + ((size.X - content) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), label,
            neutral ? Theme.TextSecondary : mark);
    }

    /// <summary>Close box with a drawn cross and a tooltip anchored under its right edge.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gear_close", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);

        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        const float extent = 6;
        draw.AddLine(center + new Vector2(-extent, -extent), center + new Vector2(extent, extent), color, 1.5f);
        draw.AddLine(center + new Vector2(-extent, extent), center + new Vector2(extent, -extent), color, 1.5f);
        if (hovered)
        {
            // Below the control and right aligned, so the panel never covers the glyph or the pill.
            ImGui.SetNextWindowPos(new Vector2(pos.X + size.X, pos.Y + size.Y + 4), ImGuiCond.Always, new Vector2(1f, 0f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Close");
            ImGui.EndTooltip();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
        }

        return clicked;
    }

    // ---- cards ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A content-measured rounded card. Background and content use separate draw channels. With
    /// <paramref name="fill"/> the card stretches to the column's shared bottom (see <see cref="Columns"/>).
    /// </summary>
    internal static void Card(Action content, bool fill = false, bool gapAfter = true)
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
        var natural = contentBottom - origin.Y + padY;
        var height = natural;
        if (fill)
        {
            height = MathF.Max(natural, _fillTop + _fillHeight - origin.Y);
            _stretch += height - natural;
        }

        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
        draw.ChannelsMerge();

        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, gapAfter ? 16 - Theme.ItemSpacing.Y : 0));
    }

    /// <summary>Two columns with equal bottoms; below the breakpoint the rail simply follows the main column.</summary>
    internal static void Columns(string id, float bodyHeight, Action main, Action rail, float breakpoint = 760)
    {
        var total = Avail();
        if (total < breakpoint)
        {
            var saved = (_fillTop, _fillHeight);
            _fillTop = 0;
            _fillHeight = float.NegativeInfinity; // stacked: cards keep their natural height
            main();
            Space(8); // last card of the main column has no trailing gap
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
        // A page that scrolls keeps natural card heights: stretching a short column only adds an empty panel at the end.
        var natural = MathF.Max(previous.Main, previous.Rail);
        var target = natural > pageFill + 8 ? 0 : pageFill - natural <= 72 ? MathF.Max(natural, pageFill) : natural;
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

    /// <summary>Card title at the title size, an optional trailing count, and a muted description.</summary>
    internal static void Section(string title, string? description = null, string? count = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var sm = Theme.SecondaryScale;
        var titleH = LineHeight(Theme.TitleScale);
        var countW = count == null ? 0 : TextWidth(count, sm);
        var shown = FitText(title, width - (count == null ? 0 : countW + 12), Theme.TitleScale);
        Text(draw, origin, shown, Theme.TextPrimary, Theme.TitleScale);
        if (count != null)
            TextRight(draw, origin.X + width, origin.Y + ((titleH - LineHeight(sm)) / 2f), count, Theme.TextSecondary, sm);
        ImGui.Dummy(new Vector2(width, titleH));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, titleH + 4));
        if (!string.IsNullOrEmpty(description))
            Para(description, sm, Theme.TextSecondary, 12);
        else
            Space(8);
    }

    // ---- stat card --------------------------------------------------------------------------------------------

    internal const float StatValueScale = 1.7f;

    internal static float StatCardHeight(bool compact, int noteLines = 1)
        => compact
            ? 12 + LineHeight(Theme.SecondaryScale) + 4 + LineHeight(Theme.SecondaryScale) + 8 + Theme.BarHeight + 12
            : 16 + LineHeight(Theme.SecondaryScale) + 8 + LineHeight(StatValueScale) + 8 + Theme.BarHeight + 8 + (Math.Max(1, noteLines) * LineHeight(Theme.SecondaryScale)) + 16;

    /// <summary>Lines (1 or 2) the wide card's caption needs at <paramref name="cardWidth"/>.</summary>
    internal static int StatNoteLines(string note, float cardWidth)
        => WrappedHeight(note, cardWidth - 32, Theme.SecondaryScale) > LineHeight(Theme.SecondaryScale) + 1 ? 2 : 1;

    private static string CapText(string cap)
    {
        var trimmed = cap.TrimStart('/', ' ');
        return int.TryParse(trimmed, out _) ? "/ " + trimmed : trimmed;
    }

    internal static void StatCard(ImDrawListPtr draw, Vector2 pos, float width, bool compact, string label, string value, string? cap, string note, float? fraction, int noteLines = 1)
    {
        var height = StatCardHeight(compact, noteLines);
        var size = new Vector2(width, height);
        const float pad = 16;
        var sm = Theme.SecondaryScale;
        var smH = LineHeight(sm);
        var valueH = LineHeight(StatValueScale);
        var capText = cap == null ? null : CapText(cap);
        var noteColor = fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Radius);

        void ValueWithCap(float x, float y)
        {
            Text(draw, new Vector2(x, y), value, Theme.TextPrimary, StatValueScale);
            if (capText != null)
            {
                var capH = LineHeight(sm);
                Text(draw, new Vector2(x + TextWidth(value, StatValueScale) + 8, y + valueH - capH - (valueH * 0.08f)), capText, Theme.TextSecondary, sm);
            }
        }

        float barY;
        bool elided;
        if (compact)
        {
            // Stacked narrow cards: label and note on the left, the value on the right, the bar along the bottom.
            var top = pos.Y + 12;
            Diamond(draw, new Vector2(pos.X + pad + 5, top + (smH / 2f)), 5, Theme.Pack(Theme.Accent));
            var valueW = TextWidth(value, StatValueScale) + (capText == null ? 0 : 8 + TextWidth(capText, sm));
            var textW = width - pad - 16 - 16 - valueW - 12;
            var shownLabel = FitText(label, textW, sm);
            var shownNote = FitText(note, textW, sm);
            elided = shownLabel != label || shownNote != note;
            Text(draw, new Vector2(pos.X + pad + 16, top), shownLabel, Theme.TextPrimary, sm);
            Text(draw, new Vector2(pos.X + pad + 16, top + smH + 4), shownNote, noteColor, sm);
            ValueWithCap(pos.X + width - pad - valueW, pos.Y + (((height - Theme.BarHeight - 8 - 12) - valueH) / 2f) + 12);
            barY = pos.Y + height - 12 - Theme.BarHeight;
        }
        else
        {
            var y = pos.Y + pad;
            Diamond(draw, new Vector2(pos.X + pad + 5, y + (smH / 2f)), 5, Theme.Pack(Theme.Accent));
            var shownLabel = FitText(label, width - (2 * pad) - 16, sm);
            Text(draw, new Vector2(pos.X + pad + 16, y), shownLabel, Theme.TextPrimary, sm);
            y += smH + 8;
            ValueWithCap(pos.X + pad, y);
            y += valueH + 8;
            barY = y;
            y += Theme.BarHeight + 8;
            var shownNote = ClampLines(note, width - (2 * pad), sm, Math.Max(1, noteLines));
            elided = shownLabel != label || shownNote != note;
            Text(draw, new Vector2(pos.X + pad, y), shownNote, noteColor, sm, width - (2 * pad));
        }

        if (elided)
            HoverTip(pos, pos + size, $"{label}\n{note}");

        // Only a real ratio draws a track; the slot stays reserved either way.
        if (fraction is not { } f)
            return;
        var min = new Vector2(pos.X + pad, barY);
        var max = new Vector2(pos.X + width - pad, barY + Theme.BarHeight);
        draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
        if (f > 0f)
            draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(f, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), 2f);
    }

    // ---- rows -------------------------------------------------------------------------------------------------

    internal static void Hairline()
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        ImGui.GetWindowDrawList().AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        ImGui.Dummy(new Vector2(width, 1));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, 1));
    }

    /// <summary>One requirement row: fixed glyph slot, label, value right aligned in its state colour.</summary>
    internal static void StatusRow(string label, string value, UiState state)
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
            UiState.Missing or UiState.Blocked or UiState.Running => Theme.StateAmber,
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
            var c = Theme.Pack(state is UiState.Failed or UiState.Blocked or UiState.Missing or UiState.Running ? valueColor : Theme.TextSecondary);
            draw.AddLine(center + new Vector2(-4, 0), center + new Vector2(4, 0), c, 1.5f);
        }

        var valueW = TextWidth(value, sm);
        var labelW = width - slot - 4 - valueW - 12;
        var shown = FitText(label, labelW);
        Text(draw, new Vector2(origin.X + slot + 4, origin.Y + ((rowH - lh) / 2f)), shown, Theme.TextSecondary);
        Text(draw, new Vector2(origin.X + width - valueW, origin.Y + ((rowH - LineHeight(sm)) / 2f)), value, valueColor, sm);
        if (shown != label)
            HoverTip(origin, origin + new Vector2(width, rowH), label);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, rowH));
    }

    private static readonly string[] StateSamples = ["Covered", "Short 99,999"];

    /// <summary>
    /// One material row. Wide rows are a table (name, on hand, state) that never wraps; narrow rows put the stock
    /// figure under the name with the state on the right. Dividers sit between rows, inside the card padding.
    /// </summary>
    internal static void CostRow(string name, long required, long have, bool first, bool last)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var sm = Theme.SecondaryScale;
        var lh = LineHeight();
        var smH = LineHeight(sm);
        var shortBy = Math.Max(0, required - have);
        var ready = shortBy == 0;
        var stateText = ready ? "Covered" : $"Short {shortBy:N0}";
        var stateColor = ready ? Theme.StateGreen : Theme.StateAmber;
        var haveText = $"On hand {have:N0}";
        var label = $"{required:N0} × {name}";
        const float pad = 10;
        const float dotSlot = 16;
        var stateW = StateSamples.Max(s => TextWidth(s, sm));
        var wide = width >= 440;
        float rowH;

        if (!first)
            draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        float nameRoom;
        if (wide)
        {
            var haveW = TextWidth("On hand 9,999,999", sm);
            rowH = pad + lh + pad;
            nameRoom = width - dotSlot - 16 - haveW - 16 - stateW;
            var y = origin.Y + pad;
            draw.AddCircleFilled(new Vector2(origin.X + 3, y + (lh / 2f)), Theme.DotRadius, Theme.Pack(stateColor));
            var shown = FitText(label, nameRoom);
            Text(draw, new Vector2(origin.X + dotSlot, y), shown, Theme.TextPrimary);
            TextRight(draw, origin.X + width - stateW - 16, y + ((lh - smH) / 2f), haveText, Theme.TextSecondary, sm);
            TextRight(draw, origin.X + width, y + ((lh - smH) / 2f), stateText, stateColor, sm);
            if (shown != label)
                HoverTip(origin, origin + new Vector2(width, rowH), label);
        }
        else
        {
            // The name takes the full row (two lines at most); stock and state share the line below it.
            nameRoom = width - dotSlot;
            var shown = ClampLines(label, nameRoom, 1f, 2);
            var nameH = WrappedHeight(shown, nameRoom);
            rowH = pad + nameH + 2 + smH + pad;
            var y = origin.Y + pad;
            draw.AddCircleFilled(new Vector2(origin.X + 3, y + (lh / 2f)), Theme.DotRadius, Theme.Pack(stateColor));
            Text(draw, new Vector2(origin.X + dotSlot, y), shown, Theme.TextPrimary, 1f, nameRoom);
            Text(draw, new Vector2(origin.X + dotSlot, y + nameH + 2), haveText, Theme.TextSecondary, sm);
            TextRight(draw, origin.X + width, y + nameH + 2, stateText, stateColor, sm);
            if (shown != label)
                HoverTip(origin, origin + new Vector2(width, rowH), label);
        }

        var used = last ? rowH - pad : rowH;
        ImGui.Dummy(new Vector2(width, used));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, used));
    }

    /// <summary>Name on the left, a figure alone in a right aligned column. A long name wraps to two lines.</summary>
    internal static void ValueRow(string name, string value, bool first, bool last)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lh = LineHeight();
        const float pad = 10;
        var valueW = TextWidth(value);
        var nameW = MathF.Max(40, width - valueW - 16);
        var shown = ClampLines(name, nameW, 1f, 2);
        var nameH = WrappedHeight(shown, nameW);
        var rowH = pad + MathF.Max(lh, nameH) + pad;

        if (!first)
            draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var contentH = rowH - (2 * pad);
        Text(draw, new Vector2(origin.X, origin.Y + pad + ((contentH - nameH) / 2f)), shown, Theme.TextPrimary, 1f, nameW);
        TextRight(draw, origin.X + width, origin.Y + pad + ((contentH - lh) / 2f), value, Theme.TextSecondary);
        if (shown != name)
            HoverTip(origin, origin + new Vector2(width, rowH), name);

        var used = last ? rowH - pad : rowH;
        ImGui.Dummy(new Vector2(width, used));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, used));
    }

    /// <summary>A two line list row: title, then a muted detail line that may wrap.</summary>
    internal static void DetailRow(string title, string detail, bool first, bool last)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var sm = Theme.SecondaryScale;
        var lh = LineHeight();
        const float pad = 10;
        var shown = FitText(title, width);
        var detailH = WrappedHeight(detail, width, sm);
        var rowH = pad + lh + 2 + detailH + pad;

        if (!first)
            draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        Text(draw, origin + new Vector2(0, pad), shown, Theme.TextPrimary);
        Text(draw, origin + new Vector2(0, pad + lh + 2), detail, Theme.TextSecondary, sm, width);
        if (shown != title)
            HoverTip(origin, origin + new Vector2(width, rowH), title);

        var used = last ? rowH - pad : rowH;
        ImGui.Dummy(new Vector2(width, used));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, used));
    }

    /// <summary>Rounded notice: state dot, lead line, then the body below. Alert states are tinted.</summary>
    internal static void Notice(string lead, string body, UiState state)
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
        var tinted = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing or UiState.Ready;
        var fill = tinted ? mark with { W = 0.10f } : Theme.ShellBar;
        var border = tinted ? mark with { W = 0.40f } : Theme.SurfaceBorder;

        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(fill), Radius);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(border), Radius);
        draw.AddCircleFilled(new Vector2(origin.X + 16, origin.Y + 12 + (LineHeight() / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, new Vector2(textX, origin.Y + 12), lead, Theme.TextPrimary, 1f, textW);
        if (bodyH > 0)
            Text(draw, new Vector2(textX, origin.Y + 12 + leadH + 4), body, Theme.TextSecondary, Theme.SecondaryScale, textW);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    // ---- inputs -----------------------------------------------------------------------------------------------

    /// <summary>A switch row with its label and help centred on the switch; the switch ends at the card padding.</summary>
    internal static bool Toggle(string literalLabel, ref bool value, string? help, bool separator = false)
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
        var clicked = ImGui.InvisibleButton("##gear_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;

        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize, Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8, Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textTop = pos.Y + ((rowH - block) / 2f);
        Text(draw, new Vector2(pos.X, textTop), literalLabel, Theme.TextPrimary, 1f, textW);
        if (hasHelp)
            Text(draw, new Vector2(pos.X, textTop + labelH + 4), help!, Theme.TextSecondary, Theme.SecondaryScale, textW);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, rowH));
        ImGui.SetCursorScreenPos(pos + new Vector2(0, rowH));
        return clicked;
    }

    /// <summary>A segmented choice: one outlined bar, equal segments, the selected one filled in the accent.</summary>
    internal static int Segmented(string id, IReadOnlyList<string> labels, int selected, float maxWidth = 360f)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = MathF.Min(Avail(), maxWidth);
        var each = width / labels.Count;
        var size = new Vector2(width, Control);
        var picked = -1;

        draw.AddRectFilled(origin, origin + size, Theme.Pack(Theme.ShellBar), Radius);
        draw.AddRect(origin, origin + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        for (var i = 0; i < labels.Count; i++)
        {
            var pos = origin + new Vector2(i * each, 0);
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton($"##gear_{id}_{i}", new Vector2(each, Control)))
                picked = i;
            var hovered = ImGui.IsItemHovered();
            var isSelected = i == selected;
            if (isSelected)
            {
                draw.AddRectFilled(pos + new Vector2(1, 1), pos + new Vector2(each - 1, Control - 1), Theme.Pack(Theme.Accent with { W = 0.14f }), Radius - 1);
                draw.AddRect(pos + new Vector2(1, 1), pos + new Vector2(each - 1, Control - 1), Theme.Pack(Theme.Accent), Radius - 1);
            }
            else if (i > 0 && i - 1 != selected)
            {
                draw.AddLine(pos + new Vector2(0, 8), pos + new Vector2(0, Control - 8), Theme.Pack(Theme.SurfaceBorder));
            }

            var label = labels[i];
            Text(draw, pos + new Vector2((each - TextWidth(label)) / 2f, (Control - LineHeight()) / 2f), label,
                isSelected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, Control));
        return picked;
    }
}
