using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using LalaUiKit;

namespace GluttonyCombo.Window;

// Fork: plugin-local drawn controls for the approved shell; shared kit sources stay unchanged.
internal static class GluttonyChrome
{
    private static readonly Vector2 ToggleSize = new(44, 24);
    private static int _cardDepth;
    // Screen X of the inner right edge of the innermost card, and the screen Y where its content starts.
    private static readonly Stack<float> Rights = new();
    private static readonly Stack<float> Tops = new();
    private static readonly Stack<int> InfoBoxPushes = new();
    // Screen X where the content of the innermost top-level preset card starts: the first toggle label there is the card's title.
    private static readonly Stack<float> CardLefts = new();
    private static bool _titlePending;

    internal const float Radius = 8f;
    internal const float Control = 36f;
    internal const float Gap = 8f;
    internal const float SecondaryScale = 0.90f;
    internal const float CardPad = 16f;
    internal const float ScrollbarWidth = 10f;

    /// <summary>Outer gutter: one step function for the whole suite.</summary>
    internal static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

    // Measurements run at the draw size (window font scale), because each size is baked separately.
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

    internal static float WrappedHeight(string text, float width, float scale = 1f) =>
        Measure(scale, () => ImGui.CalcTextSize(text, false, width).Y);

    /// <summary>The state colour used by the shell: a running rotation is a healthy steady state, so it reads green, not amber.</summary>
    internal static Vector4 StateColor(UiState state) => state == UiState.Running ? Theme.StateGreen : Copy.MarkColor(state);

    // ---- right edge of the current container ----

    internal static void PushRight(float screenX) => Rights.Push(screenX);

    internal static void PopRight()
    {
        if (Rights.Count > 0)
            Rights.Pop();
    }

    /// <summary>Width from the cursor to the inner right edge of the current card, or to the content edge outside a card.</summary>
    internal static float Avail() => Rights.Count > 0
        ? MathF.Max(1, Rights.Peek() - ImGui.GetCursorScreenPos().X)
        : ImGui.GetContentRegionAvail().X;

    /// <summary>Screen X of the inner right edge of the current card, or of the container outside a card.</summary>
    internal static float RightEdge() => Rights.Count > 0
        ? Rights.Peek()
        : ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - ImGui.GetScrollX();

    /// <summary>Screen X wrap limit as the window-local value PushTextWrapPos takes.</summary>
    internal static float LocalWrap(float screenRight) => screenRight - ImGui.GetWindowPos().X + ImGui.GetScrollX();

    /// <summary>Text that wraps at the current card's inner right edge, not at the container edge.</summary>
    internal static void WrappedText(string text)
    {
        var wrap = Rights.Count > 0 ? LocalWrap(Rights.Peek()) : 0f;
        ImGui.PushTextWrapPos(wrap);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>Section heading: title size, primary colour, 24 px above (none at the top of a container) and 8 below.</summary>
    internal static void Heading(string text)
    {
        if (Tops.Count > 0 && ImGui.GetCursorScreenPos().Y - Tops.Peek() > 1f)
            ImGui.Dummy(new Vector2(1, 8));
        ImGui.SetWindowFontScale(Theme.TitleScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
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

    internal static void Chevron(ImDrawListPtr draw, Vector2 center, Vector4 color)
    {
        var c = Theme.Pack(color);
        draw.AddLine(center + new Vector2(-5, -2), center + new Vector2(0, 3), c, 1.5f);
        draw.AddLine(center + new Vector2(0, 3), center + new Vector2(5, -2), c, 1.5f);
    }

    internal static float BrandWidth(string name) => 48 + TextWidth(name, Theme.TitleScale) + 16;

    /// <summary>Bordered brand chip, 40 high: diamond r12 at (24, 20), title at x 48.</summary>
    internal static bool BrandButton(ImDrawListPtr draw, string name, Vector2 pos, Vector2 size, string? tooltip = null)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        var shown = FitText(name, size.X - 48 - 16, Theme.TitleScale);
        DrawText(draw, pos + new Vector2(48, (size.Y - LineHeight(Theme.TitleScale)) / 2f), shown, Theme.TextPrimary, Theme.TitleScale);
        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = FitText(label, size.X - 16);
        var textSize = ImGui.CalcTextSize(shown);
        draw.AddText(pos + ((size - textSize) / 2f),
            Theme.Pack(selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary), shown);
        return clicked;
    }

    internal static float ActionWidth(float minWidth, params string[] labels)
    {
        var widest = 0f;
        foreach (var label in labels)
            widest = MathF.Max(widest, TextWidth(label));
        return MathF.Max(minWidth, widest + 32f);
    }

    internal static bool ActionButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label,
            hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    internal static float ChipWidth(string label) => 12 + 14 + 8 + TextWidth(label) + 12;

    /// <summary>
    /// A setting shown as a neutral button with a small state box, so an option never looks like a selected tab.
    /// Accent appears only inside the box while the option is on.
    /// </summary>
    internal static bool ToggleChip(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool on, string? tooltip = null)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var box = pos + new Vector2(12, (size.Y - 14) / 2f);
        if (on)
        {
            draw.AddRectFilled(box, box + new Vector2(14), Theme.Pack(Theme.Accent), 4f);
            var tick = Theme.Pack(Theme.Surface);
            draw.AddLine(box + new Vector2(3, 7.5f), box + new Vector2(6, 10.5f), tick, 1.8f);
            draw.AddLine(box + new Vector2(6, 10.5f), box + new Vector2(11, 4), tick, 1.8f);
        }
        else
        {
            draw.AddRect(box, box + new Vector2(14), Theme.Pack(Theme.TextSecondary with { W = 0.7f }), 4f);
        }
        DrawText(draw, pos + new Vector2(34, (size.Y - LineHeight()) / 2f), label,
            on || hovered ? Theme.TextPrimary : Theme.TextSecondary);
        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label = "Stop")
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.38f : 0.26f }), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 1f : 0.8f }), Radius);
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, Theme.TextPrimary);
        return clicked;
    }

    private static void PushButtonStyle()
    {
        var padding = Theme.FramePadding;
        padding.X = 16;
        padding.Y = MathF.Max(padding.Y, (Control - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
    }

    private static void PopButtonStyle()
    {
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(3);
    }

    /// <summary>
    /// Draws a callback that renders its own button (the shared problem report) as a 36 high neutral button.
    /// The button's popup inherits the same padding, rounding and colours.
    /// </summary>
    internal static void StyledButtonCallback(Vector2 pos, Action draw)
    {
        ImGui.SetCursorScreenPos(pos);
        PushButtonStyle();
        draw();
        PopButtonStyle();
    }

    /// <summary>A 36 high neutral button for actions inside page content (links and the like).</summary>
    internal static bool StyledButton(string label)
    {
        PushButtonStyle();
        var clicked = ImGui.Button(label);
        PopButtonStyle();
        return clicked;
    }

    /// <summary>36 x 36 close box with a drawn cross and a tooltip anchored under its right edge.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_close", size);
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
            // A border brighter than the band rule, so the tooltip separates from the band it hangs over.
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.TextSecondary with { W = 0.4f });
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
            // Tooltips take their rounding and border from the popup style, so both sets are pushed.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Close");
            ImGui.EndTooltip();
            ImGui.PopStyleVar(5);
            ImGui.PopStyleColor(2);
        }
        return clicked;
    }

    internal static float FeatureWidth(string label) => 16 + 12 + 8 + TextWidth(label, SecondaryScale) + 16 + 1;

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        DrawText(draw, pos + new Vector2(36, (size.Y - LineHeight(SecondaryScale)) / 2f),
            FitText(label, size.X - 36 - 12, SecondaryScale),
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, SecondaryScale);
        return clicked;
    }

    /// <summary>
    /// Neutral tile grid helper: one gap both ways. Use with <see cref="BeginTileTable"/>.
    /// </summary>
    internal const float TileGap = 12f;

    internal static bool BeginTileTable(string id, float avail, out int columns)
    {
        columns = Math.Max(1, (int)((avail + TileGap) / (220f + TileGap)));
        // Cell padding of half the gap on each side leaves one gap between neighbouring tiles; rows add the rest below.
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(TileGap / 2f, 0));
        if (ImGui.BeginTable(id, columns))
            return true;
        ImGui.PopStyleVar();
        return false;
    }

    internal static void EndTileTable()
    {
        ImGui.EndTable();
        ImGui.PopStyleVar();
    }

    internal static bool JobButton(ImDrawListPtr draw, string id, string label, IDalamudTextureWrap? icon,
        Vector2 pos, Vector2 size, bool selected, bool disabled, string? disabledHelp)
    {
        using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(disabled))
        {
            ImGui.SetCursorScreenPos(pos);
            var clicked = ImGui.InvisibleButton("##gc_job_" + id, size);
            var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
            draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
            draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
            if (icon is not null)
                draw.AddImage(icon.Handle, pos + new Vector2(10, 9), pos + new Vector2(44, 43));
            else
                // Accent marks the selected tile only; a missing icon gets a neutral placeholder.
                Diamond(draw, pos + new Vector2(27, size.Y / 2f), 10, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary with { W = 0.35f }));
            var labelWidth = size.X - 64;
            var shown = FitText(label, labelWidth);
            draw.AddText(pos + new Vector2(54, (size.Y - ImGui.CalcTextSize(shown).Y) / 2f),
                Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), shown);
            if (hovered && !string.IsNullOrEmpty(disabledHelp))
                ImGui.SetTooltip(disabledHelp);
            else if (hovered && shown != label)
                ImGui.SetTooltip(label);
            return clicked;
        }
    }

    /// <summary>One width for every state, so nothing shifts when the state changes.</summary>
    internal static float PillWidth()
    {
        var widest = 0f;
        foreach (var state in Enum.GetValues<UiState>())
            widest = MathF.Max(widest, TextWidth(Copy.Word(state)));
        return MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + widest + 16);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var mark = StateColor(state);
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

    internal const float TitleBandHeight = 69f;
    internal const float FooterBandHeight = 60f;

    /// <summary>Header band for a window without navigation: brand chip left, close box right. Returns true on close.</summary>
    internal static bool DrawTitleBand(ImDrawListPtr draw, Vector2 winPos, float winWidth, float gutter, string title)
    {
        const float bar = 68;
        draw.PushClipRect(winPos, winPos + new Vector2(winWidth, bar + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winWidth, bar), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, bar), winPos + new Vector2(winWidth, bar), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        var chipPos = winPos + new Vector2(gutter, 14);
        var chipWidth = MathF.Min(winWidth - (2 * gutter) - Control - Gap, BrandWidth(title));
        var chipSize = new Vector2(chipWidth, 40);
        draw.AddRectFilled(chipPos, chipPos + chipSize, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(chipPos, chipPos + chipSize, Theme.Pack(Theme.SurfaceBorder), Radius);
        Diamond(draw, chipPos + new Vector2(24, 20), 12, Theme.Pack(Theme.Accent));
        DrawText(draw, chipPos + new Vector2(48, (40 - LineHeight(Theme.TitleScale)) / 2f),
            FitText(title, chipWidth - 48 - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        return CloseButton(draw, winPos + new Vector2(winWidth - gutter - Control, 16), new Vector2(Control));
    }

    /// <summary>Full-bleed footer band (60 px) at the bottom of the window.</summary>
    internal static void DrawFooterBand(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float height)
    {
        var top = winPos.Y + winSize.Y - height;
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    /// <summary>
    /// Opens the shell's only scroll region: an outer child that scrolls (its scrollbar sits on the window edge, inside the
    /// gutter) around an inner, non-scrolling child that is exactly the content column. Page renderers therefore measure
    /// the same width whether or not a scrollbar shows, and every widget that spans the content width (headers, rules,
    /// wrapped text, fields) ends at the gutter instead of at the window padding. Pair with <see cref="EndBody"/>.
    /// </summary>
    internal static void BeginBody(Vector2 size, float gutter, string id)
    {
        Rights.Clear();
        Tops.Clear();
        CardLefts.Clear();
        _titlePending = false;
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        // The 1 px transparent border is what makes a child honour its window padding (top and bottom margin).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, ScrollbarWidth);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.BeginChild(id, size, true);
        ImGui.SetCursorPosX(gutter);
        // The column is as tall as the content measured last frame, so the outer region scrolls only when the page
        // really is longer than the view; a page that changes length is off by at most one frame.
        _columnId = id;
        var columnHeight = MathF.Max(1, _columnHeights.TryGetValue(id, out var known) ? known : size.Y - 32);
        ImGui.BeginChild(id + "_column", new Vector2(MathF.Max(40, size.X - (2 * gutter)), columnHeight), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        // What the page content, its popups and its tooltips see: the suite theme, not the child's own overrides.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.WindowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        Tops.Push(ImGui.GetCursorScreenPos().Y);
    }

    private static readonly Dictionary<string, float> _columnHeights = [];
    private static string _columnId = "";

    internal static void EndBody()
    {
        Tops.Clear();
        Rights.Clear();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(8);
        _columnHeights[_columnId] = ImGui.GetCursorPosY();
        ImGui.EndChild();
        var scrollMax = ImGui.GetScrollMaxY();
        if (scrollMax > 0 && ImGui.GetScrollY() < scrollMax - 1)
        {
            // Cards that run on below the footer fade out instead of being cut off.
            var draw = ImGui.GetWindowDrawList();
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
            var clear = bg & 0x00FFFFFFu;
            var solid = bg | 0xFF000000u;
            var bottom = pos.Y + size.Y;
            draw.AddRectFilledMultiColor(new Vector2(pos.X, bottom - 28), new Vector2(pos.X + size.X - ScrollbarWidth, bottom),
                clear, clear, solid, solid);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(5);
    }

    internal static bool Toggle(string literalLabel, ref bool value)
    {
        var hash = literalLabel.IndexOf("##", StringComparison.Ordinal);
        var label = hash < 0 ? literalLabel : literalLabel[..hash];
        if (label.Length == 0)
        {
            var compactPos = ImGui.GetCursorScreenPos();
            var compactClicked = ImGui.InvisibleButton("##gc_toggle" + literalLabel, ToggleSize);
            if (compactClicked)
                value = !value;
            DrawTrack(compactPos, value, ImGui.IsItemHovered());
            return compactClicked;
        }

        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        // A label at the left edge of a preset card is the card's title: one step larger than the rows under it.
        var scale = 1f;
        if (_titlePending && CardLefts.Count > 0 && MathF.Abs(pos.X - CardLefts.Peek()) < 1.5f)
        {
            scale = Theme.TitleScale;
            _titlePending = false;
        }
        var labelSize = Measure(scale, () => ImGui.CalcTextSize(label, false, labelWidth));
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##gc_toggle" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        DrawTrack(togglePos, value, ImGui.IsItemHovered());

        ImGui.SetCursorScreenPos(pos + new Vector2(0, (rowHeight - labelSize.Y) / 2f));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
        ImGui.SetWindowFontScale(scale);
        ImGui.TextUnformatted(label);
        ImGui.SetWindowFontScale(1f);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight));
        // One dummy closes the row: it extends the scroll area and applies the item spacing.
        ImGui.Dummy(new Vector2(1, 0));
        return clicked;
    }

    private static void DrawTrack(Vector2 pos, bool value, bool hovered)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(pos, pos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(pos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));
    }

    /// <summary>
    /// Suite notice: radius 8, a state dot, 16 px padding, tinted for states that need attention. Wraps inside the
    /// current container and leaves 16 px below.
    /// </summary>
    internal static void Notice(UiState state, string headline, string? detail = null)
    {
        var text = string.IsNullOrEmpty(detail) ? headline : $"{headline} — {detail}";
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        const float padX = 16;
        const float padY = 12;
        var textX = padX + 18;
        var wrap = MathF.Max(40, width - textX - padX);
        var textHeight = WrappedHeight(text, wrap);
        var height = MathF.Max(Control, textHeight + (2 * padY));
        var alert = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
        var mark = StateColor(state);
        draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(alert ? mark with { W = 0.12f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(alert ? mark with { W = 0.45f } : Theme.SurfaceBorder), Radius);
        draw.AddCircleFilled(new Vector2(pos.X + padX, pos.Y + (height / 2f) - (textHeight / 2f) + (LineHeight() / 2f)),
            Theme.DotRadius, Theme.Pack(mark));
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(pos.X + textX, pos.Y + ((height - textHeight) / 2f)),
            Theme.Pack(Theme.TextPrimary), text, wrap);
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, height));
        // The dummy leaves the item spacing; one more empty item makes the gap below 16.
        ImGui.Dummy(new Vector2(1, 0));
    }

    /// <summary>Header of the overlay strip: the brand diamond, the title and a count line.</summary>
    internal static void ForecastHeader(string title, string caption)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var titleHeight = LineHeight(Theme.TitleScale);
        Diamond(draw, pos + new Vector2(7, titleHeight / 2f), 7, Theme.Pack(Theme.Accent));
        DrawText(draw, pos + new Vector2(22, 0), title, Theme.TextPrimary, Theme.TitleScale);
        ImGui.Dummy(new Vector2(22 + TextWidth(title, Theme.TitleScale), titleHeight));
        Widgets.Caption(caption);
        ImGui.Dummy(new Vector2(1, 4 - Theme.ItemSpacing.Y));
    }

    /// <summary>One forecast line of the overlay strip: a right-aligned time column, then the label; the soonest stands out.</summary>
    internal static void ForecastRow(double seconds, string label, bool soonest)
    {
        var column = TextWidth("88.8s");
        var pos = ImGui.GetCursorScreenPos();
        var time = $"{seconds:F1}s";
        var draw = ImGui.GetWindowDrawList();
        var color = soonest ? Theme.TextPrimary : Theme.TextSecondary;
        DrawText(draw, pos + new Vector2(column - TextWidth(time), 0), time, color);
        DrawText(draw, pos + new Vector2(column + 12, 0), label, color);
        ImGui.Dummy(new Vector2(column + 12 + TextWidth(label), LineHeight()));
    }

    /// <summary>Card width used by the auto-sized forecast overlay, which has no content region to measure.</summary>
    internal const float OverlayCardWidth = 220f;

    internal static IDisposable BeginCard(float minHeight = 0, float? fixedWidth = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = fixedWidth ?? Avail();
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        draw.ChannelsSetCurrent(2);
        ImGui.SetCursorScreenPos(origin + new Vector2(CardPad, CardPad));
        Rights.Push(origin.X + width - CardPad);
        Tops.Push(origin.Y + CardPad);
        ImGui.PushTextWrapPos(LocalWrap(origin.X + width - CardPad));
        ImGui.BeginGroup();
        return new CardScope(draw, origin, width, minHeight, topLevel, fixedWidth is not null);
    }

    private sealed class CardScope : IDisposable
    {
        private readonly ImDrawListPtr _draw;
        private readonly Vector2 _origin;
        private readonly float _width;
        private readonly float _minHeight;
        private readonly bool _topLevel;
        private readonly bool _reserveWidth;

        internal CardScope(ImDrawListPtr draw, Vector2 origin, float width, float minHeight, bool topLevel, bool reserveWidth)
        {
            _reserveWidth = reserveWidth;
            _draw = draw;
            _origin = origin;
            _width = width;
            _minHeight = minHeight;
            _topLevel = topLevel;
        }

        public void Dispose()
        {
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            PopRight();
            if (Tops.Count > 0)
                Tops.Pop();
            // The group's rectangle ends at the last item, so the trailing item spacing never adds to the padding.
            var contentBottom = MathF.Max(ImGui.GetItemRectMax().Y, _origin.Y + CardPad);
            var height = MathF.Max(_minHeight, contentBottom - _origin.Y + CardPad);
            _draw.ChannelsSetCurrent(_topLevel ? 0 : 1);
            _draw.AddRectFilled(_origin, _origin + new Vector2(_width, height), Theme.Pack(Theme.Surface), Radius);
            _draw.AddRect(_origin, _origin + new Vector2(_width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
            if (_topLevel)
            {
                _draw.ChannelsMerge();
                _cardDepth--;
            }
            else
            {
                _draw.ChannelsSetCurrent(2);
            }
            ImGui.SetCursorScreenPos(_origin + new Vector2(0, height));
            if (_reserveWidth)
            {
                // An auto-sized window sizes itself from items, so the card's full width is reserved as one.
                ImGui.Dummy(new Vector2(_width, 0));
                return;
            }
            // Gap below: 16 between top-level cards, 8 between nested ones (the dummy adds the item spacing).
            ImGui.Dummy(new Vector2(_width, (_topLevel ? 16f : 8f) - Theme.ItemSpacing.Y));
        }
    }

    internal static void Card(string id, Action content, float minHeight = 0)
    {
        using var card = BeginCard(minHeight);
        content();
    }

    // ---- hooks for the preset card box (InfoBox) ----

    /// <summary>
    /// Extra inset that brings an InfoBox's content to the card padding. Returns the offset to add to the content origin and,
    /// for a top-level box, records its inner right edge and content top. Pair with <see cref="EndInfoBox"/>.
    /// </summary>
    internal static Vector2 BeginInfoBox(float curveRadius, float contentsOffset, bool nested, float sizeX)
    {
        if (nested)
        {
            // A nested box ends at the parent's inner edge; its own content sits 12 px inside it.
            if (Rights.Count == 0)
            {
                InfoBoxPushes.Push(0);
                return Vector2.Zero;
            }
            Rights.Push(Rights.Peek() - 12);
            InfoBoxPushes.Push(2);
            return new Vector2(12 - curveRadius, 0);
        }
        var origin = ImGui.GetCursorScreenPos();
        var width = sizeX > 0 ? sizeX : Avail();
        var insetY = InfoBoxTop(curveRadius, contentsOffset) - curveRadius - contentsOffset;
        Rights.Push(origin.X + width - CardPad);
        Tops.Push(origin.Y + InfoBoxTop(curveRadius, contentsOffset));
        CardLefts.Push(origin.X + CardPad);
        _titlePending = true;
        InfoBoxPushes.Push(1);
        return new Vector2(MathF.Max(0, CardPad - curveRadius - contentsOffset), insetY);
    }

    private static bool _boxSplit;

    internal static bool InfoBoxSurfaceDrawn { get; private set; }

    /// <summary>Opens a back channel so a top-level box can paint its surface behind content that is already drawn.</summary>
    internal static void InfoBoxBegin(bool nested)
    {
        InfoBoxSurfaceDrawn = false;
        if (nested || _cardDepth > 0 || _boxSplit)
            return;
        _boxSplit = true;
        var draw = ImGui.GetWindowDrawList();
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
    }

    /// <summary>Paints the filled, rounded surface on the back channel and merges. Nested boxes keep their own outline.</summary>
    internal static void InfoBoxSurface(Vector2 start, Vector2 size, bool nested)
    {
        if (!_boxSplit || nested)
            return;
        var draw = ImGui.GetWindowDrawList();
        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(start, start + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(start, start + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        draw.ChannelsMerge();
        _boxSplit = false;
        InfoBoxSurfaceDrawn = true;
    }

    /// <summary>Wrap limit for text drawn inside the box, set after the box's own wrap push.</summary>
    internal static void InfoBoxWrap()
    {
        if (Rights.Count > 0)
            ImGui.PushTextWrapPos(LocalWrap(Rights.Peek()));
        else
            ImGui.PushTextWrapPos(0f);
    }

    internal static void EndInfoBox()
    {
        ImGui.PopTextWrapPos();
        var kind = InfoBoxPushes.Count > 0 ? InfoBoxPushes.Pop() : 0;
        if (kind == 2)
            PopRight();
        if (kind == 1)
        {
            PopRight();
            if (Tops.Count > 0)
                Tops.Pop();
            if (CardLefts.Count > 0)
                CardLefts.Pop();
            _titlePending = false;
        }
    }

    // Preset rows open with one item spacing of their own, so the card's top inset is that much smaller than the padding.
    private static float InfoBoxTop(float curveRadius, float contentsOffset) =>
        MathF.Max(curveRadius + contentsOffset, CardPad - Theme.ItemSpacing.Y);

    /// <summary>
    /// Final size of an InfoBox: a top-level box keeps the width it measured and gets the padding above and below the
    /// content, counting the spacing the rows already carry; a nested box is clamped to its parent's inner right edge.
    /// </summary>
    internal static Vector2 InfoBoxSize(Vector2 current, Vector2 start, float curveRadius, float contentsOffset, float contentHeight, bool nested)
    {
        if (nested)
            return Rights.Count > 0 ? current with { X = MathF.Min(current.X, Rights.Peek() - start.X) } : current;
        var trailing = contentsOffset + Theme.ItemSpacing.Y;
        return current with { Y = InfoBoxTop(curveRadius, contentsOffset) + contentHeight + MathF.Max(0, CardPad - trailing) };
    }

    /// <summary>Moves the cursor below the card when the card is taller than the content it wraps.</summary>
    internal static void InfoBoxAdvance(float cardBottom)
    {
        var y = ImGui.GetCursorScreenPos().Y;
        if (cardBottom > y)
            ImGui.Dummy(new Vector2(1, cardBottom - y - Theme.ItemSpacing.Y));
    }

    internal static void PushControlColors()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
    }

    internal static void PopControlColors() => ImGui.PopStyleColor(12);

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
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
}
