using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyFoodBuff;

/// <summary>
///     Plugin-local drawn controls for the food automation window. Every measurement derives from the live font
///     (CalcTextSize, line height) and the 4 px Theme grid, never from constants tuned for one font.
/// </summary>
internal static class FoodUiChrome
{
    public const float Radius = 2 * Theme.Unit;
    public const float Gap = 4 * Theme.Unit;
    public const float ControlHeight = 9 * Theme.Unit;
    public const float PageTitleScale = 1.35f;
    public const float StatValueScale = 1.7f;
    private const float RowPad = 3 * Theme.Unit;
    public static readonly Vector2 ToggleSize = new(44, 24);

    private static readonly Stack<float> Rights = new();

    /// <summary>Screen Y limits (top, bottom) that combo popups stay inside; set by the window each frame.</summary>
    public static (float Top, float Bottom) PopupBounds { get; set; } = (0, float.MaxValue);

    // ---- layout: the right edge available to the current card or column ----

    public static void ResetLayout() => Rights.Clear();

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

    public static float Gutter(float windowWidth) => windowWidth >= 760 ? 6 * Theme.Unit : windowWidth >= 560 ? 4 * Theme.Unit : 3 * Theme.Unit;

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
            return "";
        if (TextWidth(text, scale) <= width + 1f)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length] + ellipsis, scale) > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
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

    internal static void Chevron(ImDrawListPtr draw, Vector2 center, Vector4 color)
    {
        var c = Theme.Pack(color);
        draw.AddLine(center + new Vector2(-5, -2), center + new Vector2(0, 3), c, 1.5f);
        draw.AddLine(center + new Vector2(0, 3), center + new Vector2(5, -2), c, 1.5f);
    }

    internal static void CheckMark(ImDrawListPtr draw, Vector2 center, Vector4 color)
    {
        var c = Theme.Pack(color);
        draw.AddLine(center + new Vector2(-4.5f, 0.5f), center + new Vector2(-1.5f, 3.5f), c, 1.75f);
        draw.AddLine(center + new Vector2(-1.5f, 3.5f), center + new Vector2(4.5f, -3.5f), c, 1.75f);
    }

    // ---- shell controls ----

    internal const float BrandHeight = 40;

    internal static float BrandWidth()
        => 12 + 24 + 12 + TextWidth("LazyFoodBuff", Theme.TitleScale) + 16;

    /// <summary>The bordered brand chip: diamond, then the name. The name ellipsises when the chip is shorter than its natural width.</summary>
    internal static void Brand(ImDrawListPtr draw, Vector2 pos, float width)
    {
        var size = new Vector2(width, BrandHeight);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 8f);
        Diamond(draw, pos + new Vector2(24, BrandHeight / 2f), 12, Theme.Pack(Theme.Accent));
        DrawFitted(draw, pos + new Vector2(48, (BrandHeight - LineHeight(Theme.TitleScale)) / 2f),
            "LazyFoodBuff", width - 48 - 16, Theme.TextPrimary, Theme.TitleScale);
    }

    /// <summary>One width for every state so the pill never shifts between states.</summary>
    internal static float PillWidth()
        => MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var mark = Copy.MarkColor(state);
        var neutral = state is UiState.Idle or UiState.NeedsSetup or UiState.Empty;
        var word = Copy.Word(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        var group = (2 * Theme.DotRadius) + 8 + TextWidth(word);
        var x = pos.X + ((size.X - group) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(mark));
        DrawText(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), word,
            neutral ? Theme.TextSecondary : mark);
    }

    /// <summary>The close control: a drawn cross with a tooltip anchored under the box, right aligned to it.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##lfb_close", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
        var c = pos + (size / 2f);
        var col = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), col, 1.5f);
        draw.AddLine(c + new Vector2(-6, 6), c + new Vector2(6, -6), col, 1.5f);
        if (hovered)
        {
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

    internal static bool ChoiceButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##lfb_choice_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.ShellBar;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var marker = pos + new Vector2(Gap, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var textX = Gap + 12 + (2 * Theme.Unit);
        DrawFitted(draw, pos + new Vector2(textX, (size.Y - LineHeight()) / 2f), label, size.X - textX - Gap,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool onCard = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##lfb_button_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(onCard ? Theme.ShellBar : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = Fit(label, size.X - (2 * Theme.Unit));
        var textSize = ImGui.CalcTextSize(shown);
        draw.AddText(pos + ((size - textSize) / 2f), Theme.Pack(hovered ? Theme.TextPrimary : Theme.TextSecondary), shown);
        return clicked;
    }

    /// <summary>The stop control: a tinted outline, red because it stops something, never a solid block.</summary>
    internal static float ActionWidth(float minWidth, string label) => MathF.Max(minWidth, TextWidth(label) + 32f);

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##lfb_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.24f : 0.12f }), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.85f : 0.55f }), Radius);
        var textSize = ImGui.CalcTextSize("Stop");
        draw.AddText(pos + ((size - textSize) / 2f), Theme.Pack(Theme.TextPrimary), "Stop");
        return clicked;
    }

    // ---- fields and combos ----

    /// <summary>Bordered, filled field styling for inputs and combos; pair with <see cref="PopField"/>.</summary>
    internal static void PushField()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Gap, MathF.Max(Theme.Unit, (ControlHeight - ImGui.GetTextLineHeight()) / 2f)));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Theme.Accent with { W = 0.30f });
    }

    internal static void PopField()
    {
        ImGui.PopStyleColor(8);
        ImGui.PopStyleVar(3);
    }

    private static int _comboStyleVars;
    private static int _comboStyleColors;
    private static readonly HashSet<string> PressedWhileOpen = new();

    /// <summary>
    ///     A rounded bordered combo with a small chevron. Its popup is as wide as the field and is placed on the roomier
    ///     side of it, capped so it stays inside the window above the footer. Pair with <see cref="EndCombo"/>.
    /// </summary>
    internal static bool BeginCombo(string id, string preview, float width)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, ControlHeight);
        PushField();
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2 * Theme.Unit, 2 * Theme.Unit));
        _comboStyleVars = 3;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.28f });
        _comboStyleColors = 4;

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##field" + id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (ImGui.IsPopupOpen(id))
                PressedWhileOpen.Add(id);
            else
                PressedWhileOpen.Remove(id);
        }
        if (clicked && !PressedWhileOpen.Remove(id))
            ImGui.OpenPopup(id);
        var isOpen = ImGui.IsPopupOpen(id);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.ShellBar), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(isOpen || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        DrawText(draw, pos + new Vector2(Gap, (size.Y - LineHeight()) / 2f), Fit(preview, width - (4 * Gap)), Theme.TextPrimary);
        Chevron(draw, pos + new Vector2(width - Gap - 4, size.Y / 2f), isOpen || hovered ? Theme.Accent : Theme.TextSecondary);

        // The popup never covers the footer: it takes the larger side of the field and is capped to it.
        var below = PopupBounds.Bottom - (pos.Y + size.Y) - Theme.Unit;
        var above = pos.Y - PopupBounds.Top - Theme.Unit;
        var placeAbove = below < 200 && above > below;
        var room = MathF.Max(120, MathF.Min(320, placeAbove ? above : below));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, room));
        if (placeAbove)
            ImGui.SetNextWindowPos(new Vector2(pos.X, pos.Y - Theme.Unit), ImGuiCond.Always, new Vector2(0, 1));
        else
            ImGui.SetNextWindowPos(new Vector2(pos.X, pos.Y + size.Y + Theme.Unit), ImGuiCond.Always, Vector2.Zero);
        var open = ImGui.BeginPopup(id);
        if (open)
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, Theme.Unit));
        return open;
    }

    internal static void EndCombo(bool open)
    {
        if (open)
        {
            ImGui.PopStyleVar();
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(_comboStyleColors);
        ImGui.PopStyleVar(_comboStyleVars);
        PopField();
    }

    // ---- rows and rules inside cards ----

    /// <summary>Starts a settings row: an optional hairline at its top, then the row padding.</summary>
    internal static void BeginRow(bool separator)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        if (separator)
            ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        Advance(pos, width, RowPad, 0);
    }

    internal static void EndRow()
    {
        var pos = ImGui.GetCursorScreenPos();
        Advance(pos, Avail(), RowPad, 0);
    }

    /// <summary>A wrapped paragraph, measured from the live font.</summary>
    internal static void Paragraph(string text, float scale = Theme.SecondaryScale, Vector4? color = null, float gap = 2 * Theme.Unit)
    {
        if (text.Length == 0)
            return;
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var height = DrawWrapped(ImGui.GetWindowDrawList(), pos, text, width, color ?? Theme.TextSecondary, scale);
        Advance(pos, width, height, gap);
    }

    /// <summary>A card heading: title, optional caption underneath.</summary>
    internal static void CardHeader(string title, string? caption = null, float gap = 3 * Theme.Unit)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var height = LineHeight(Theme.TitleScale);
        DrawFitted(draw, pos, title, width, Theme.TextPrimary, Theme.TitleScale);
        if (!string.IsNullOrEmpty(caption))
        {
            height += Theme.Unit;
            height += DrawWrapped(draw, pos + new Vector2(0, height), caption, width, Theme.TextSecondary, Theme.SecondaryScale);
        }
        Advance(pos, width, height, gap);
    }

    /// <summary>Label and help on the left, the switch on the right, both centred on the row.</summary>
    internal static bool Toggle(string literalLabel, ref bool value, string? help = null, bool separator = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Max(40, width - ToggleSize.X - Gap);
        var labelHeight = WrappedHeight(literalLabel, textWidth);
        var helpHeight = string.IsNullOrWhiteSpace(help) ? 0 : WrappedHeight(help, textWidth, Theme.SecondaryScale);
        var blockHeight = labelHeight + (helpHeight > 0 ? Theme.Unit + helpHeight : 0);
        var rowHeight = MathF.Max(ToggleSize.Y, blockHeight) + (2 * RowPad);

        if (separator)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##lfb_setting_" + literalLabel, ToggleSize);
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

    /// <summary>
    ///     A labelled slider with a visible track: the label on the left, the value on the right, the filled part and
    ///     the grab in the accent. Values snap to whole steps.
    /// </summary>
    internal static bool Slider(string id, string label, ref float value, float min, float max, string valueText, string? help = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lineHeight = LineHeight();
        var valueWidth = TextWidth(valueText);
        DrawFitted(draw, pos, label, width - valueWidth - Gap, Theme.TextPrimary);
        DrawTextRight(draw, pos.X + width, pos.Y, valueText, Theme.TextPrimary);

        const float grabRadius = 8;
        const float hitHeight = 24;
        var trackTop = pos.Y + lineHeight + (2 * Theme.Unit);
        var x0 = pos.X + grabRadius;
        var x1 = pos.X + width - grabRadius;
        ImGui.SetCursorScreenPos(new Vector2(pos.X, trackTop));
        ImGui.InvisibleButton("##lfb_slider_" + id, new Vector2(MathF.Max(1, width), hitHeight));
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        var changed = false;
        if (active && x1 > x0)
        {
            var t = Math.Clamp((ImGui.GetIO().MousePos.X - x0) / (x1 - x0), 0f, 1f);
            var next = MathF.Round(min + (t * (max - min)));
            if (MathF.Abs(next - value) > 0.001f)
            {
                value = next;
                changed = true;
            }
        }

        var fraction = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
        var centerY = trackTop + (hitHeight / 2f);
        var grabX = x0 + ((x1 - x0) * fraction);
        draw.AddRectFilled(new Vector2(pos.X, centerY - 2), new Vector2(pos.X + width, centerY + 2), Theme.Pack(Theme.SurfaceBorder), 2f);
        if (grabX > pos.X)
            draw.AddRectFilled(new Vector2(pos.X, centerY - 2), new Vector2(grabX, centerY + 2), Theme.Pack(Theme.Accent), 2f);
        if (hovered || active)
            draw.AddCircleFilled(new Vector2(grabX, centerY), grabRadius + 4, Theme.Pack(Theme.Accent with { W = 0.20f }));
        draw.AddCircleFilled(new Vector2(grabX, centerY), grabRadius, Theme.Pack(Theme.Accent));

        var height = lineHeight + (2 * Theme.Unit) + hitHeight;
        if (!string.IsNullOrWhiteSpace(help))
        {
            height += Theme.Unit;
            height += DrawWrapped(draw, new Vector2(pos.X, pos.Y + height), help, width, Theme.TextSecondary, Theme.SecondaryScale);
        }
        Advance(pos, width, height, 0);
        return changed;
    }

    /// <summary>A requirement row: state glyph in a fixed slot, label, and a state-coloured value on the right.</summary>
    internal static void StatusRow(string label, string value, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lineHeight = LineHeight();
        var rowHeight = lineHeight + (3 * Theme.Unit);
        const float slot = 20;
        var center = pos + new Vector2(slot / 2f, rowHeight / 2f);
        var valueColor = state switch
        {
            UiState.Ready => Theme.StateGreen,
            UiState.Missing or UiState.Blocked => Theme.StateAmber,
            _ => Theme.TextSecondary,
        };
        switch (state)
        {
            case UiState.Ready:
                CheckMark(draw, center, Theme.StateGreen);
                break;
            case UiState.Missing or UiState.Blocked:
                draw.AddCircleFilled(center, Theme.DotRadius, Theme.Pack(Theme.StateAmber));
                break;
            default:
                draw.AddLine(center + new Vector2(-4, 0), center + new Vector2(4, 0), Theme.Pack(Theme.TextSecondary), 1.5f);
                break;
        }

        var valueScale = Theme.SecondaryScale;
        var valueMax = MathF.Max(40, width * 0.6f);
        var shownValue = Fit(value, valueMax, valueScale);
        var valueWidth = TextWidth(shownValue, valueScale);
        DrawFitted(draw, pos + new Vector2(slot + Theme.Unit, (rowHeight - lineHeight) / 2f), label,
            width - slot - Theme.Unit - valueWidth - Gap, Theme.TextSecondary);
        DrawText(draw, new Vector2(pos.X + width - valueWidth, pos.Y + ((rowHeight - LineHeight(valueScale)) / 2f)), shownValue, valueColor, valueScale);
        if (shownValue != value && ImGui.IsMouseHoveringRect(new Vector2(pos.X + width - valueWidth, pos.Y), pos + new Vector2(width, rowHeight)))
            ImGui.SetTooltip(value);
        Advance(pos, width, rowHeight, 0);
    }

    // ---- notices ----

    private static bool IsAlert(UiState state) => state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing or UiState.NeedsSetup;

    /// <summary>Rounded callout: tinted amber for attention, green for a good result, neutral otherwise.</summary>
    internal static void Notice(string lead, string body, UiState state, float gap = 3 * Theme.Unit)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var color = state == UiState.NeedsSetup ? Theme.StateAmber : Copy.MarkColor(state);
        var tinted = IsAlert(state) || state == UiState.Ready;
        var padX = Gap;
        var padY = 3 * Theme.Unit;
        var textLeft = padX + (Theme.DotRadius * 2) + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textLeft - padX);
        var lineHeight = LineHeight();
        var inline = lead.Length > 0 && body.Length > 0 && TextWidth(lead + " —") + (2 * Theme.Unit) + TextWidth(body) <= textWidth;
        float contentHeight;
        if (lead.Length > 0 && body.Length > 0 && !inline)
            contentHeight = WrappedHeight(lead, textWidth) + (Theme.Unit / 2f) + WrappedHeight(body, textWidth, Theme.SecondaryScale);
        else if (inline)
            contentHeight = lineHeight;
        else
            contentHeight = WrappedHeight(lead.Length > 0 ? lead : body, textWidth);
        var height = (2 * padY) + contentHeight;
        var fillAlpha = state == UiState.Ready ? 0.10f : 0.12f;
        var borderAlpha = state == UiState.Ready ? 0.35f : 0.45f;
        draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(tinted ? color with { W = fillAlpha } : Theme.ShellBar), Radius);
        draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(tinted ? color with { W = borderAlpha } : Theme.SurfaceBorder), Radius);
        draw.AddCircleFilled(pos + new Vector2(padX + Theme.DotRadius, padY + (lineHeight / 2f)), Theme.DotRadius, Theme.Pack(color));
        var textPos = pos + new Vector2(textLeft, padY);
        if (inline)
        {
            DrawText(draw, textPos, lead + " —", Theme.TextPrimary);
            DrawText(draw, textPos + new Vector2(TextWidth(lead + " —") + (2 * Theme.Unit), 0), body, Theme.TextSecondary);
        }
        else if (lead.Length > 0)
        {
            var used = DrawWrapped(draw, textPos, lead, textWidth, Theme.TextPrimary);
            if (body.Length > 0)
                DrawWrapped(draw, textPos + new Vector2(0, used + (Theme.Unit / 2f)), body, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
            DrawWrapped(draw, textPos, body, textWidth, Theme.TextSecondary);
        Advance(pos, width, height, gap);
    }
}

/// <summary>Content-measured rounded cards; their backgrounds never cover their contents.</summary>
internal static class FoodCard
{
    public const float Padding = 4 * Theme.Unit;
    private const float MaxStretch = 12 * Theme.Unit;

    /// <summary>Screen Y that cards marked <c>fill</c> stretch down to, or NaN when columns are stacked.</summary>
    public static float FillBottom { get; set; } = float.NaN;

    /// <summary>The stretch given to the most recent filling card, so column heights can be stored without it.</summary>
    public static float LastStretch { get; private set; }

    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();

    public static void BeginColumn(float bottom)
    {
        FillBottom = bottom;
        LastStretch = 0;
    }

    public static void Draw(string id, Action content, bool fill = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = FoodUiChrome.Avail();
        draw.ChannelsSplit(2);
        ImGui.PushID(id);
        try
        {
            draw.ChannelsSetCurrent(1);
            ImGui.SetCursorScreenPos(origin + new Vector2(Padding, Padding));
            FoodUiChrome.PushRight(origin.X + cardWidth - Padding);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            FoodUiChrome.PopRight();
            var contentBottom = ImGui.GetItemRectMax().Y;
            var natural = MathF.Max(5 * Theme.Unit, contentBottom - origin.Y + Padding);
            var height = natural;
            if (fill && !float.IsNaN(FillBottom))
            {
                // Only close a small difference between column bottoms; a large one stays natural instead of
                // leaving a big empty card.
                var stretch = MathF.Max(0, FillBottom - origin.Y - natural);
                if (stretch <= MaxStretch)
                {
                    height = natural + stretch;
                    LastStretch = stretch;
                }
            }

            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), FoodUiChrome.Radius);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), FoodUiChrome.Radius);
            draw.ChannelsMerge();
            FoodUiChrome.Advance(origin + new Vector2(0, height), cardWidth, 0, FoodUiChrome.Gap);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    ///     Two columns (work and rail) whose last cards share one bottom edge; below the breakpoint they stack.
    ///     Heights come from the previous frame, so the first frame after a layout change settles in one more frame.
    /// </summary>
    public static void Columns(string id, Action main, Action rail, float breakpoint, bool fillPage)
    {
        var width = FoodUiChrome.Avail();
        var saved = FillBottom;
        if (width < breakpoint)
        {
            FillBottom = float.NaN;
            main();
            rail();
            FillBottom = saved;
            return;
        }
        var gap = FoodUiChrome.Gap;
        var origin = ImGui.GetCursorScreenPos();
        var railWidth = Math.Clamp(width * 0.26f, 300, 360);
        var mainWidth = width - gap - railWidth;
        ColumnHeights.TryGetValue(id, out var previous);
        var target = MathF.Max(previous.Main, previous.Rail);
        if (fillPage)
            target = MathF.Max(target, ImGui.GetWindowHeight() - (2 * FoodUiChrome.Gap) - ImGui.GetCursorPos().Y);

        // Each column is a group so that, after every item, the cursor returns to the column's own left edge.
        FoodUiChrome.PushRight(origin.X + mainWidth);
        BeginColumn(origin.Y + target);
        ImGui.SetCursorScreenPos(origin);
        ImGui.BeginGroup();
        main();
        ImGui.EndGroup();
        var mainBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var mainNatural = mainBottom - FoodUiChrome.Gap - origin.Y - LastStretch;
        FoodUiChrome.PopRight();

        FoodUiChrome.PushRight(origin.X + width);
        BeginColumn(origin.Y + target);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + mainWidth + gap, origin.Y));
        ImGui.BeginGroup();
        rail();
        ImGui.EndGroup();
        var railBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var railNatural = railBottom - FoodUiChrome.Gap - origin.Y - LastStretch;
        FoodUiChrome.PopRight();

        ColumnHeights[id] = (mainNatural, railNatural);
        FillBottom = saved;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom) - Theme.ItemSpacing.Y));
        ImGui.Dummy(new Vector2(width, 0));
    }
}
