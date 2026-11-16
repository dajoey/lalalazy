using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyCrafter.UI;

/// <summary>
///     LazyCrafter-local drawn controls for the approved shell, cards and settings tracks. Every measurement derives from
///     the live font (CalcTextSize, line height) and the 4 px Theme grid, never from constants tuned for one font.
/// </summary>
internal static class CrafterChrome
{
    internal const float Radius = 2 * Theme.Unit;
    internal const float Gap = 4 * Theme.Unit;
    internal const float ControlHeight = 9 * Theme.Unit;
    internal const float PageTitleScale = 1.35f;
    internal const float StatValueScale = 1.7f;
    internal const float CardPadding = 4 * Theme.Unit;
    internal static readonly Vector2 ToggleSize = new(44, 24);

    private static readonly Stack<float> Rights = new();
    private static int _cardDepth;

    // ---- frame ----

    internal static void PushFrame()
    {
        Theme.PushFrame();
        var fieldPad = MathF.Max(Theme.Unit, (ControlHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.SurfaceBorder with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.025f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.SurfaceBorder with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3 * Theme.Unit, fieldPad));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 2 * Theme.Unit + 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(2 * Theme.Unit, 1.5f * Theme.Unit));
        ResetLayout();
    }

    internal static void PopFrame()
    {
        ImGui.PopStyleVar(10);
        ImGui.PopStyleColor(21);
        Theme.PopFrame();
    }

    // ---- layout: the right edge available to the current card or column ----

    internal static void ResetLayout()
    {
        Rights.Clear();
        _cardDepth = 0;
        FillBottom = float.NaN;
        MinBottom = float.NaN;
    }

    internal static void PushRight(float screenX) => Rights.Push(screenX);

    internal static void PopRight()
    {
        if (Rights.Count > 0)
            Rights.Pop();
    }

    /// <summary>Width from the cursor to the inner right edge of the enclosing card or column.</summary>
    internal static float Avail()
        => Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    /// <summary>Move below an element of the given size and leave <paramref name="gap"/> before the next one.</summary>
    internal static void Advance(Vector2 origin, float width, float height, float gap = Gap)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(MathF.Max(1, width), MathF.Max(0, height + gap - Theme.ItemSpacing.Y)));
    }

    internal static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

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

    internal static float LineHeight(float scale = 1f) => Measure(scale, ImGui.GetTextLineHeight);

    internal static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        if (scale == 1f)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    internal static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => DrawText(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    internal static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : MathF.Max(LineHeight(scale), Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(8, width)).Y));

    /// <summary>Draw wrapped text with the draw list; returns the height used.</summary>
    internal static float DrawWrapped(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return 0;
        var height = WrappedHeight(text, width, scale);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, MathF.Max(8, width));
        return height;
    }

    internal static string Fit(string text, float width, float scale = 1f)
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
            if (TextWidth(text[..mid] + ellipsis, scale) <= width)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo == 0 ? "" : text[..lo].TrimEnd() + ellipsis;
    }

    /// <summary>Clamp wrapped text to a number of lines, ending in an ellipsis when it is cut.</summary>
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
        return lo == 0 ? "…" : text[..lo].TrimEnd() + "…";
    }

    /// <summary>Single line that is elided to fit; the full text shows on hover.</summary>
    internal static void DrawFitted(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        var fitted = Fit(text, width, scale);
        DrawText(draw, pos, fitted, color, scale);
        if (fitted != text && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, LineHeight(scale))))
            ImGui.SetTooltip(text);
    }

    internal static void DrawFittedRight(ImDrawListPtr draw, float right, float y, string text, float width, Vector4 color, float scale = 1f)
    {
        var fitted = Fit(text, width, scale);
        DrawTextRight(draw, right, y, fitted, color, scale);
        if (fitted != text && ImGui.IsMouseHoveringRect(new Vector2(right - width, y), new Vector2(right, y + LineHeight(scale))))
            ImGui.SetTooltip(text);
    }

    /// <summary>A wrapped paragraph in the flow of the page (secondary colour, optional scale).</summary>
    internal static void Paragraph(string text, float scale = Theme.SecondaryScale, Vector4? color = null, float gap = 2 * Theme.Unit)
    {
        if (text.Length == 0)
            return;
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var height = DrawWrapped(draw, pos, text, width, color ?? Theme.TextSecondary, scale);
        Advance(pos, width, height, gap);
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

    /// <summary>A soft fade over the bottom of a scrolling region, so a cut row reads as "continues below".</summary>
    internal static void FadeBottom(ImDrawListPtr draw, Vector2 min, Vector2 max, uint color, float height = 6 * Theme.Unit)
    {
        var top = MathF.Max(min.Y, max.Y - height);
        var clear = color & 0x00FFFFFFu;
        draw.PushClipRect(min, max, true);
        draw.AddRectFilledMultiColor(new Vector2(min.X, top), max, clear, clear, color, color);
        draw.PopClipRect();
    }

    // ---- table headers ----

    /// <summary>Width a sortable header keeps free for its sort arrow, so the label never shares its cell with the arrow.</summary>
    internal static float SortArrowReserve => 14f;

    /// <summary>
    ///     One table header cell drawn by hand: the label sits at the left or right edge of the cell, so numeric columns
    ///     line up with their right-aligned values, and the sort arrow takes its own slot beside the label. Returns the
    ///     mouse button pressed on a sortable header (0 left, 1 right) or -1.
    /// </summary>
    internal static int HeaderCell(string id, string label, bool right, int sortDirection = 0, bool sortable = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X);
        var height = ImGui.GetTextLineHeight();
        var pressed = -1;
        var hovered = false;
        if (sortable)
        {
            ImGui.InvisibleButton(id, new Vector2(width, height));
            hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                pressed = 0;
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                pressed = 1;
        }
        else
        {
            ImGui.Dummy(new Vector2(width, height));
        }

        var reserve = sortable ? SortArrowReserve : 0f;
        var fitted = Fit(label, MathF.Max(8, width - reserve));
        var textWidth = TextWidth(fitted);
        var color = hovered ? Theme.TextPrimary : sortDirection != 0 ? Theme.TextPrimary : Theme.TextSecondary;
        var textX = right ? pos.X + width - textWidth : pos.X;
        DrawText(draw, new Vector2(textX, pos.Y), fitted, color);
        if (sortDirection != 0)
        {
            var arrowX = right ? textX - 4f - 4f : textX + textWidth + 4f + 4f;
            var centre = new Vector2(arrowX, pos.Y + (height / 2f));
            var up = sortDirection > 0;
            var a = centre + new Vector2(-4f, up ? 2.5f : -2.5f);
            var b = centre + new Vector2(4f, up ? 2.5f : -2.5f);
            var c = centre + new Vector2(0f, up ? -2.5f : 2.5f);
            draw.AddTriangleFilled(a, b, c, Theme.Pack(Theme.Accent));
        }
        return pressed;
    }

    /// <summary>
    ///     A header cell that keeps the table's built-in header behaviour (sort click, resize and reorder drag, the
    ///     right-click menu that shows and hides columns). The built-in label and arrow are made invisible and drawn by
    ///     hand instead, so numeric columns can be right-aligned over their values.
    /// </summary>
    internal static void NativeHeaderCell(int column, string label, bool right, int sortDirection)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X);
        ImGui.PushID(column);
        ImGui.PushStyleColor(ImGuiCol.Text, 0u);
        ImGui.TableHeader("");
        ImGui.PopStyleColor();
        ImGui.PopID();
        var hovered = ImGui.IsItemHovered();
        var fitted = Fit(label, MathF.Max(8, width - SortArrowReserve));
        var textWidth = TextWidth(fitted);
        var color = hovered || sortDirection != 0 ? Theme.TextPrimary : Theme.TextSecondary;
        var textX = right ? pos.X + width - textWidth : pos.X;
        DrawText(draw, new Vector2(textX, pos.Y), fitted, color);
        if (sortDirection == 0)
            return;
        var arrowX = right ? textX - 8f : textX + textWidth + 8f;
        var centre = new Vector2(arrowX, pos.Y + (ImGui.GetTextLineHeight() / 2f));
        var up = sortDirection > 0;
        var a = centre + new Vector2(-4f, up ? 2.5f : -2.5f);
        var b = centre + new Vector2(4f, up ? 2.5f : -2.5f);
        var c = centre + new Vector2(0f, up ? -2.5f : 2.5f);
        draw.AddTriangleFilled(a, b, c, Theme.Pack(Theme.Accent));
    }

    /// <summary>
    ///     A non-sortable header row: every label aligned like its column's values (right for the listed columns). A
    ///     label that starts with "##" is a column without a header text.
    /// </summary>
    internal static void HeadersRow(string[] labels, params int[] rightAligned)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i < labels.Length; i++)
        {
            ImGui.TableSetColumnIndex(i);
            if (labels[i].StartsWith("##", StringComparison.Ordinal))
            {
                ImGui.Dummy(new Vector2(1, ImGui.GetTextLineHeight()));
                continue;
            }
            HeaderCell("##h" + i, labels[i], Array.IndexOf(rightAligned, i) >= 0);
        }
    }

    /// <summary>Right-aligned text in the current table cell (values under a right-aligned header).</summary>
    internal static void CellRight(string text, Vector4 color)
    {
        var width = ImGui.CalcTextSize(text).X;
        var available = ImGui.GetContentRegionAvail().X;
        if (available > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);
        // The cell may wrap its text; a value placed against the right edge must stay on one line.
        ImGui.PushTextWrapPos(-1f);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    // ---- shell controls ----

    internal static float BrandWidth()
        => 48 + TextWidth("LazyCrafter", Theme.TitleScale) + 16;

    internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        DrawText(draw, pos + new Vector2(48, (size.Y - LineHeight(Theme.TitleScale)) / 2f), "LazyCrafter", Theme.TextPrimary, Theme.TitleScale);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = Fit(label, size.X - (2 * Theme.Unit));
        draw.AddText(pos + new Vector2((size.X - TextWidth(shown)) / 2f, (size.Y - LineHeight()) / 2f),
            Theme.Pack(selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary), shown);
        return clicked;
    }

    /// <summary>Close box with a drawn cross and a tooltip anchored under its right edge.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_close", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
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

    internal static float ActionWidth(float minWidth, params string[] labels)
        => MathF.Max(minWidth, labels.Max(l => TextWidth(l)) + 32f);

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label = "Stop")
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_footer_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.24f : 0.12f }), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.85f : 0.55f }), Radius);
        draw.AddText(pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), Theme.Pack(Theme.TextPrimary), label);
        return clicked;
    }

    /// <summary>Radio-style sub-navigation pill (bucket row).</summary>
    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crafter_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        DrawFitted(draw, pos + new Vector2(36, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f), label, size.X - 36 - 12,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, Theme.SecondaryScale);
        return clicked;
    }

    internal static float FeatureWidth(string label) => 16 + 12 + 8 + TextWidth(label, Theme.SecondaryScale) + 16 + 1;

    /// <summary>One width for every state so the search box never moves between states.</summary>
    internal static float PillWidth()
    {
        var words = Enum.GetValues<UiState>().Select(Copy.Word);
        return MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + words.Max(w => TextWidth(w)) + 16);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        var group = (2 * Theme.DotRadius) + 8 + TextWidth(label);
        var left = pos.X + ((size.X - group) / 2f);
        draw.AddCircleFilled(new Vector2(left + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        var textColor = state is UiState.Idle or UiState.Empty or UiState.NeedsSetup ? Theme.TextSecondary : color;
        draw.AddText(new Vector2(left + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), Theme.Pack(textColor), label);
    }

    // ---- inline controls ----

    internal enum ButtonKind
    {
        Normal,
        Primary,
        Danger,
    }

    internal static float ButtonWidth(string label) => TextWidth(label) + 32f;

    /// <summary>Outlined button on the 36 px control height, placed at the cursor. Disabled buttons keep their outline.</summary>
    internal static bool Button(string id, string label, ButtonKind kind = ButtonKind.Normal, bool disabled = false, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width > 0 ? width : ButtonWidth(label), ControlHeight);
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton("##btn_" + id, size);
        var hovered = ImGui.IsItemHovered() && !disabled;
        if (disabled)
            ImGui.EndDisabled();
        var alpha = disabled ? 0.45f : 1f;
        Vector4 fill, border, text;
        switch (kind)
        {
            case ButtonKind.Primary:
                fill = Theme.Accent with { W = hovered ? 0.24f : 0.14f };
                border = Theme.Accent with { W = hovered ? 1f : 0.8f };
                text = Theme.Accent;
                break;
            case ButtonKind.Danger:
                fill = Theme.StateRed with { W = hovered ? 0.24f : 0.12f };
                border = Theme.StateRed with { W = hovered ? 0.85f : 0.55f };
                text = Theme.TextPrimary;
                break;
            default:
                fill = Theme.Surface;
                border = hovered ? Theme.Accent : Theme.SurfaceBorder;
                text = hovered ? Theme.TextPrimary : Theme.TextSecondary;
                break;
        }
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill with { W = fill.W * alpha }), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(border with { W = border.W * alpha }), Radius);
        var shown = Fit(label, size.X - 16);
        draw.AddText(pos + new Vector2((size.X - TextWidth(shown)) / 2f, (size.Y - LineHeight()) / 2f),
            Theme.Pack(text with { W = text.W * alpha }), shown);
        return clicked;
    }

    /// <summary>Compact outlined action for table rows and ledger groups.</summary>
    internal static bool SmallButton(string id, string label, bool disabled = false, bool danger = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var height = LineHeight(Theme.SecondaryScale) + (3 * Theme.Unit);
        var size = new Vector2(TextWidth(label, Theme.SecondaryScale) + (4 * Theme.Unit), height);
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton("##sbtn_" + id, size);
        var hovered = ImGui.IsItemHovered() && !disabled;
        if (disabled)
            ImGui.EndDisabled();
        var alpha = disabled ? 0.45f : 1f;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.ShellBar with { W = alpha }), 6f);
        draw.AddRect(pos, pos + size, Theme.Pack((hovered ? Theme.Accent : Theme.SurfaceBorder) with { W = alpha }), 6f);
        DrawText(draw, pos + new Vector2(2 * Theme.Unit, (height - LineHeight(Theme.SecondaryScale)) / 2f), label,
            (hovered ? Theme.TextPrimary : Theme.TextSecondary) with { W = alpha }, Theme.SecondaryScale);
        return clicked;
    }

    internal static float SmallButtonWidth(string label) => TextWidth(label, Theme.SecondaryScale) + (4 * Theme.Unit);

    /// <summary>Checkbox with a visible outline in both states; the label sits 8 px right of the box.</summary>
    internal static bool Check(string id, string label, ref bool value)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        const float box = 20f;
        var size = new Vector2(box + 8 + TextWidth(label), ControlHeight);
        var clicked = ImGui.InvisibleButton("##chk_" + id, size);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        var min = pos + new Vector2(0, (ControlHeight - box) / 2f);
        draw.AddRectFilled(min, min + new Vector2(box), Theme.Pack(value ? Theme.Accent : Theme.ShellBar), 6f);
        draw.AddRect(min, min + new Vector2(box), Theme.Pack(value ? Theme.Accent : hovered ? Theme.Accent : Theme.TextSecondary with { W = 0.6f }), 6f);
        if (value)
        {
            var tick = Theme.Pack(Theme.ShellBar);
            draw.AddLine(min + new Vector2(5, 10.5f), min + new Vector2(8.5f, 14), tick, 2f);
            draw.AddLine(min + new Vector2(8.5f, 14), min + new Vector2(15, 6.5f), tick, 2f);
        }
        draw.AddText(pos + new Vector2(box + 8, (ControlHeight - LineHeight()) / 2f), Theme.Pack(hovered ? Theme.TextPrimary : Theme.TextPrimary with { W = 0.92f }), label);
        return clicked;
    }

    internal static float CheckWidth(string label) => 20f + 8 + TextWidth(label);

    // ---- combo ----

    private static int _comboStyleVars;
    private static int _comboStyleColors;

    /// <summary>A rounded dark combo with a small chevron. Pair with <see cref="EndCombo"/>.</summary>
    internal static bool BeginCombo(string id, string preview, float width)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2 * Theme.Unit, 2 * Theme.Unit));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, Theme.Unit));
        _comboStyleVars = 3;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        _comboStyleColors = 1;
        ImGui.SetNextItemWidth(width);
        var open = ImGui.BeginCombo(id, Fit(preview, width - (4 * Gap)), ImGuiComboFlags.NoArrowButton);
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, ImGui.GetFrameHeight()));
        Chevron(draw, pos + new Vector2(width - Gap - 4, ImGui.GetFrameHeight() / 2f), hovered ? Theme.Accent : Theme.TextSecondary);
        return open;
    }

    internal static void EndCombo(bool open)
    {
        if (open)
            ImGui.EndCombo();
        ImGui.PopStyleColor(_comboStyleColors);
        ImGui.PopStyleVar(_comboStyleVars);
    }

    /// <summary>A small caption above a field.</summary>
    internal static void FieldLabel(string label)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        DrawFitted(draw, pos, label, Avail(), Theme.TextPrimary);
        Advance(pos, Avail(), LineHeight(), Theme.Unit);
    }

    // ---- stat cards ----

    internal readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    internal static float StatCardHeight()
        => CardPadding + LineHeight(Theme.SecondaryScale) + 8 + LineHeight(StatValueScale) + 8 + Theme.BarHeight + 8 + LineHeight(Theme.SecondaryScale) + CardPadding;

    internal static void DrawStatCards(params Stat[] stats)
    {
        if (stats.Length == 0)
            return;
        var draw = ImGui.GetWindowDrawList();
        var available = Avail();
        var stacked = available < (stats.Length * 220) + ((stats.Length - 1) * Gap);
        var width = stacked ? available : (available - ((stats.Length - 1) * Gap)) / stats.Length;
        var height = StatCardHeight();
        var origin = ImGui.GetCursorScreenPos();
        var labelH = LineHeight(Theme.SecondaryScale);
        var valueH = LineHeight(StatValueScale);
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + Gap)) : new Vector2(i * (width + Gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
            var y = pos.Y + CardPadding;
            Diamond(draw, new Vector2(pos.X + 21, y + (labelH / 2f)), 5, Theme.Pack(Theme.Accent));
            DrawFitted(draw, new Vector2(pos.X + 32, y), stat.Label, width - 32 - CardPadding, Theme.TextSecondary, Theme.SecondaryScale);
            y += labelH + 8;
            var dash = stat.Value == "—";
            var capText = stat.Cap is null ? "" : "/ " + stat.Cap.TrimStart('/', ' ');
            var capWidth = capText.Length == 0 ? 0 : TextWidth(capText, Theme.SecondaryScale) + 8;
            var shownValue = Fit(stat.Value, width - (2 * CardPadding) - capWidth, StatValueScale);
            DrawText(draw, new Vector2(pos.X + CardPadding, y), shownValue, dash ? Theme.TextSecondary : Theme.TextPrimary, StatValueScale);
            if (capText.Length > 0)
            {
                var capX = pos.X + CardPadding + TextWidth(shownValue, StatValueScale) + 8;
                var capH = LineHeight(Theme.SecondaryScale);
                DrawText(draw, new Vector2(capX, y + valueH - capH - (0.08f * valueH)), capText, Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueH + 8;
            if (stat.Fraction is { } fraction)
            {
                var min = new Vector2(pos.X + CardPadding, y);
                var max = new Vector2(pos.X + width - CardPadding, y + Theme.BarHeight);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), 2f);
            }
            y += Theme.BarHeight + 8;
            var noteColor = stat.Fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary;
            DrawFitted(draw, new Vector2(pos.X + CardPadding, y), stat.Note, width - (2 * CardPadding), noteColor, Theme.SecondaryScale);
        }
        var total = stacked ? (stats.Length * height) + ((stats.Length - 1) * Gap) : height;
        Advance(origin, available, total, Gap);
    }

    // ---- cards ----

    internal enum CardTone
    {
        Surface,
        Row,
        Ready,
        Attention,
    }

    /// <summary>Screen Y that cards marked <c>fill</c> stretch down to, or NaN when columns are stacked.</summary>
    internal static float FillBottom { get; set; } = float.NaN;

    /// <summary>Screen Y a card must at least reach (a fixed-height page region), or NaN.</summary>
    internal static float MinBottom { get; set; } = float.NaN;

    internal static float LastStretch { get; private set; }

    /// <summary>Free height below the cursor inside a card that is being stretched (0 when it is not).</summary>
    internal static float RemainingFill()
    {
        var bottom = float.IsNaN(MinBottom) ? FillBottom : MinBottom;
        return float.IsNaN(bottom) ? 0 : MathF.Max(0, bottom - CardPadding - ImGui.GetCursorScreenPos().Y);
    }

    /// <summary>Draws a rounded, content-measured card without covering its child widgets.</summary>
    internal static void Card(string id, Action content, CardTone tone = CardTone.Surface, float minHeight = 0, bool fill = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = Avail();
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        var savedMin = MinBottom;
        if (topLevel && minHeight > 0)
            MinBottom = origin.Y + minHeight;
        else if (!topLevel)
            MinBottom = float.NaN;
        ImGui.PushID(id);
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
            PushRight(origin.X + cardWidth - CardPadding);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            PopRight();
            var contentBottom = ImGui.GetItemRectMax().Y;
            var natural = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);
            var height = natural;
            if (fill && !float.IsNaN(FillBottom) && topLevel)
            {
                height = MathF.Max(natural, FillBottom - origin.Y);
                LastStretch = height - natural;
            }

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var (fillColor, border) = CardColors(tone);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), fillColor, Radius);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), border, Radius);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            Advance(origin + new Vector2(0, height), cardWidth, 0, topLevel ? Gap : 2 * Theme.Unit);
        }
        finally
        {
            ImGui.PopID();
            MinBottom = savedMin;
            if (topLevel)
                _cardDepth--;
        }
    }

    private static (uint Fill, uint Border) CardColors(CardTone tone) => tone switch
    {
        CardTone.Row => (Theme.Pack(Theme.ShellBar), Theme.Pack(Theme.SurfaceBorder)),
        CardTone.Ready => (Theme.Pack(Theme.StateGreen with { W = 0.10f }), Theme.Pack(Theme.StateGreen with { W = 0.35f })),
        CardTone.Attention => (Theme.Pack(Theme.StateAmber with { W = 0.10f }), Theme.Pack(Theme.StateAmber with { W = 0.35f })),
        _ => (Theme.Pack(Theme.Surface), Theme.Pack(Theme.SurfaceBorder)),
    };

    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();

    /// <summary>
    ///     Two columns with equal bottoms. The last card of each column may stretch; stacked below the breakpoint.
    /// </summary>
    internal static void Columns(string id, Action main, Action rail, float breakpoint = 820, float? railFraction = null, bool fillPage = false)
    {
        var width = Avail();
        var saved = FillBottom;
        if (width < breakpoint)
        {
            FillBottom = float.NaN;
            main();
            rail();
            FillBottom = saved;
            return;
        }
        var origin = ImGui.GetCursorScreenPos();
        var railWidth = railFraction is { } fraction ? (width - Gap) * (1 - fraction) : Math.Clamp(width * 0.28f, 300, 380);
        var mainWidth = width - Gap - railWidth;
        ColumnHeights.TryGetValue(id, out var previous);
        // A short column is only stretched when that adds little empty space; a big difference stays ragged.
        var target = MathF.Max(previous.Main, previous.Rail);
        var mainTarget = target - previous.Main <= 12 * Theme.Unit ? target : 0;
        var railTarget = target - previous.Rail <= 12 * Theme.Unit ? target : 0;
        if (fillPage)
        {
            var page = ImGui.GetContentRegionAvail().Y;
            mainTarget = MathF.Max(mainTarget, page);
            railTarget = MathF.Max(railTarget, page);
        }

        PushRight(origin.X + mainWidth);
        FillBottom = mainTarget > 0 ? origin.Y + mainTarget : float.NaN;
        LastStretch = 0;
        ImGui.SetCursorScreenPos(origin);
        ImGui.BeginGroup();
        main();
        ImGui.EndGroup();
        var mainBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var mainNatural = mainBottom - Gap - origin.Y - LastStretch;
        PopRight();

        PushRight(origin.X + width);
        FillBottom = railTarget > 0 ? origin.Y + railTarget : float.NaN;
        LastStretch = 0;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + mainWidth + Gap, origin.Y));
        ImGui.BeginGroup();
        rail();
        ImGui.EndGroup();
        var railBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var railNatural = railBottom - Gap - origin.Y - LastStretch;
        PopRight();

        ColumnHeights[id] = (mainNatural, railNatural);
        FillBottom = saved;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom) - Theme.ItemSpacing.Y));
        ImGui.Dummy(new Vector2(width, 0));
    }

    /// <summary>Centered empty state for a card with free space: diamond, title, short explanation.</summary>
    internal static void EmptyState(string title, string body)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Min(width, MathF.Min(440, TextWidth(body, Theme.SecondaryScale) + 2));
        var titleHeight = LineHeight(Theme.TitleScale);
        var bodyHeight = WrappedHeight(body, textWidth, Theme.SecondaryScale);
        const float icon = 28;
        var block = icon + Gap + titleHeight + Theme.Unit + bodyHeight;
        var top = MathF.Max(2 * Theme.Unit, (RemainingFill() - block) / 2f);
        var centerX = pos.X + (width / 2f);
        Diamond(draw, new Vector2(centerX, pos.Y + top + (icon / 2f)), icon / 2f, Theme.Pack(Theme.Accent with { W = 0.55f }));
        var y = pos.Y + top + icon + Gap;
        var shownTitle = Fit(title, width, Theme.TitleScale);
        DrawText(draw, new Vector2(centerX - (TextWidth(shownTitle, Theme.TitleScale) / 2f), y), shownTitle, Theme.TextPrimary, Theme.TitleScale);
        DrawWrapped(draw, new Vector2(centerX - (textWidth / 2f), y + titleHeight + Theme.Unit), body, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        Advance(pos, width, top + block, 0);
    }

    /// <summary>A card heading: title, optional trailing count, optional caption underneath.</summary>
    internal static void Section(string title, string? description = null, string? count = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var titleHeight = LineHeight(Theme.TitleScale);
        var countWidth = string.IsNullOrEmpty(count) ? 0 : TextWidth(count, Theme.SecondaryScale) + Gap;
        DrawFitted(draw, pos, title, width - countWidth, Theme.TextPrimary, Theme.TitleScale);
        if (!string.IsNullOrEmpty(count))
            DrawTextRight(draw, pos.X + width, pos.Y + titleHeight - LineHeight(Theme.SecondaryScale) - 1, count, Theme.TextSecondary, Theme.SecondaryScale);
        var height = titleHeight;
        if (!string.IsNullOrWhiteSpace(description))
        {
            height += Theme.Unit;
            height += DrawWrapped(draw, pos + new Vector2(0, height), description, width, Theme.TextSecondary, Theme.SecondaryScale);
        }
        Advance(pos, width, height, 3 * Theme.Unit);
    }

    // ---- notices ----

    private static bool IsAlert(UiState state) => state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;

    /// <summary>Rounded banner: tinted for alerts, neutral surface for information, with a state dot.</summary>
    internal static void Notice(string lead, string body, UiState state, bool strong = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var color = Copy.MarkColor(state);
        const float padX = Gap;
        const float padY = 3 * Theme.Unit;
        var textLeft = padX + (Theme.DotRadius * 2) + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textLeft - padX);
        var lh = LineHeight();
        var inline = lead.Length > 0 && body.Length > 0 && TextWidth(lead + " —") + (2 * Theme.Unit) + TextWidth(body) <= textWidth;
        float contentHeight;
        if (lead.Length > 0 && body.Length > 0 && !inline)
            contentHeight = WrappedHeight(lead, textWidth) + (Theme.Unit / 2f) + WrappedHeight(body, textWidth, Theme.SecondaryScale);
        else if (lead.Length > 0 && body.Length == 0)
            contentHeight = WrappedHeight(lead, textWidth);
        else if (inline)
            contentHeight = lh;
        else
            contentHeight = WrappedHeight(body, textWidth);
        var height = (2 * padY) + contentHeight;
        var alert = IsAlert(state);
        draw.AddRectFilled(pos, pos + new Vector2(width, height),
            Theme.Pack(alert ? color with { W = strong ? 0.20f : 0.12f } : Theme.ShellBar), Radius);
        draw.AddRect(pos, pos + new Vector2(width, height),
            Theme.Pack(alert ? color with { W = strong ? 0.75f : 0.45f } : Theme.SurfaceBorder), Radius);
        draw.AddCircleFilled(pos + new Vector2(padX + Theme.DotRadius, padY + (lh / 2f)), Theme.DotRadius, Theme.Pack(color));
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
        Advance(pos, width, height, 3 * Theme.Unit);
    }

    // ---- rows ----

    /// <summary>Requirement row: check or dash slot, label, coloured value on the right, optional sub line.</summary>
    internal static void Requirement(string label, string value, UiState state, string? sub = null, bool separator = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lh = LineHeight();
        var subH = string.IsNullOrEmpty(sub) ? 0 : LineHeight(Theme.SecondaryScale);
        var rowH = lh + 12 + (subH > 0 ? Theme.Unit + subH : 0);
        if (separator)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }));
        var y = pos.Y + 6;
        var ready = state == UiState.Ready;
        var glyph = ready ? "✓" : "—";
        DrawText(draw, new Vector2(pos.X + 10 - (TextWidth(glyph) / 2f), y), glyph, ready ? Theme.StateGreen : Theme.TextSecondary);
        var valueColor = state switch
        {
            UiState.Ready => Theme.StateGreen,
            UiState.Missing or UiState.Blocked => Theme.StateAmber,
            _ => Theme.TextSecondary,
        };
        var valueWidth = TextWidth(value, Theme.SecondaryScale);
        DrawFittedRight(draw, pos.X + width, y + ((lh - LineHeight(Theme.SecondaryScale)) / 2f), value, valueWidth + 1, valueColor, Theme.SecondaryScale);
        DrawFitted(draw, new Vector2(pos.X + 24, y), label, width - 24 - valueWidth - Gap, Theme.TextPrimary);
        if (subH > 0)
            DrawFitted(draw, new Vector2(pos.X + 24, y + lh + Theme.Unit), sub!, width - 24, Theme.TextSecondary, Theme.SecondaryScale);
        Advance(pos, width, rowH, 0);
    }

    /// <summary>Plain label/value row: label left, value right, hairline between rows.</summary>
    internal static void KeyValue(string label, string value, bool separator = true, Vector4? valueColor = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var lh = LineHeight(Theme.SecondaryScale);
        var rowH = lh + (3 * Theme.Unit);
        if (separator)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }));
        var y = pos.Y + (1.5f * Theme.Unit);
        var valueWidth = MathF.Min(width * 0.6f, TextWidth(value, Theme.SecondaryScale));
        DrawFittedRight(draw, pos.X + width, y, value, valueWidth + 1, valueColor ?? Theme.TextPrimary, Theme.SecondaryScale);
        DrawFitted(draw, new Vector2(pos.X, y), label, width - valueWidth - Gap, Theme.TextSecondary, Theme.SecondaryScale);
        Advance(pos, width, rowH, 0);
    }

    // ---- settings toggle row ----

    /// <summary>Label and help on the left, the switch on the right, centred on the text block.</summary>
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
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }));
        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##crafter_setting_" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered || !value)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary with { W = 0.45f }), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textY = pos.Y + ((rowHeight - blockHeight) / 2f);
        DrawWrapped(draw, new Vector2(pos.X, textY), literalLabel, textWidth, Theme.TextPrimary);
        if (helpHeight > 0)
            DrawWrapped(draw, new Vector2(pos.X, textY + labelHeight + Theme.Unit), help!, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        Advance(pos, width, rowHeight, 0);
        return clicked;
    }

    // ---- misc ----

    internal static void WrapNext(float nextWidth)
    {
        var x = ImGui.GetItemRectMax().X;
        var right = Rights.Count > 0 ? Rights.Peek() : ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        // Past the edge the next item simply starts on the line the cursor already moved to.
        if (x + Theme.ItemSpacing.X + nextWidth <= right + 0.5f)
            ImGui.SameLine();
    }

    /// <summary>Like <see cref="WrapNext"/> but inside a table cell or child, where the content region is the limit.</summary>
    internal static void WrapNextLocal(float nextWidth)
    {
        var x = ImGui.GetItemRectMax().X;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        // Past the edge the next item simply starts on the line the cursor already moved to.
        if (x + Theme.ItemSpacing.X + nextWidth <= right + 0.5f)
            ImGui.SameLine();
    }

    /// <summary>Push the styling a bordered input field needs, sized to the 36 px control height.</summary>
    internal static void PushField()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
    }

    internal static void PopField() => ImGui.PopStyleColor(4);
}
