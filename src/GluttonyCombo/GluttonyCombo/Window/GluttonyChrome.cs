using System;
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

    internal const float Radius = 8f;
    internal const float Control = 36f;
    internal const float Gap = 8f;
    internal const float SecondaryScale = 0.90f;

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
    internal static bool BrandButton(ImDrawListPtr draw, string name, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        var shown = FitText(name, size.X - 48 - 16, Theme.TitleScale);
        DrawText(draw, pos + new Vector2(48, (size.Y - LineHeight(Theme.TitleScale)) / 2f), shown, Theme.TextPrimary, Theme.TitleScale);
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

    internal static bool StopButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label = "Stop")
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_stop", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.24f : 0.12f }), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.StateRed with { W = hovered ? 0.85f : 0.55f }), Radius);
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, Theme.TextPrimary);
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
                Diamond(draw, pos + new Vector2(27, size.Y / 2f), 10, Theme.Pack(Theme.Accent));
            var textSize = ImGui.CalcTextSize(label);
            draw.AddText(pos + new Vector2(54, (size.Y - textSize.Y) / 2f),
                Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), FitText(label, size.X - 64));
            if (hovered && !string.IsNullOrEmpty(disabledHelp))
                ImGui.SetTooltip(disabledHelp);
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

    internal const float TitleBandHeight = 69f;

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

    /// <summary>
    /// Opens the shell's only scroll region. The child's one pixel transparent border makes it honour its window
    /// padding, so every page gets the same side gutter and a bottom margin; the scrollbar sits on the window edge.
    /// Pair with <see cref="EndBody"/>.
    /// </summary>
    internal static void BeginBody(Vector2 size, float gutter, string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.BeginChild(id, size, true);
        // Popups and tooltips opened from page content keep the theme padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.WindowPadding);
    }

    internal static void EndBody()
    {
        ImGui.PopStyleVar();
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
        var width = ImGui.GetContentRegionAvail().X;
        var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
        var labelSize = ImGui.CalcTextSize(label, false, labelWidth);
        var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
        var togglePos = pos + new Vector2(width - ToggleSize.X, 0);

        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##gc_toggle" + literalLabel, ToggleSize);
        if (clicked)
            value = !value;
        DrawTrack(togglePos, value, ImGui.IsItemHovered());

        var labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1
            ? (ToggleSize.Y - labelSize.Y) / 2f
            : 0;
        ImGui.SetCursorScreenPos(pos + new Vector2(0, labelY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
        ImGui.TextUnformatted(label);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight + Theme.ItemSpacing.Y));
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

    /// <summary>Card width used by the auto-sized forecast overlay, which has no content region to measure.</summary>
    internal const float OverlayCardWidth = 220f;

    internal static IDisposable BeginCard(float minHeight = 0, float? fixedWidth = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = fixedWidth ?? ImGui.GetContentRegionAvail().X;
        const float padding = 12;
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        draw.ChannelsSetCurrent(2);
        ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, width - (2 * padding)));
        ImGui.BeginGroup();
        return new CardScope(draw, origin, width, minHeight, padding, topLevel, fixedWidth is not null);
    }

    private sealed class CardScope : IDisposable
    {
        private readonly ImDrawListPtr _draw;
        private readonly Vector2 _origin;
        private readonly float _width;
        private readonly float _minHeight;
        private readonly float _padding;
        private readonly bool _topLevel;
        private readonly bool _reserveWidth;

        internal CardScope(ImDrawListPtr draw, Vector2 origin, float width, float minHeight, float padding, bool topLevel, bool reserveWidth)
        {
            _reserveWidth = reserveWidth;
            _draw = draw;
            _origin = origin;
            _width = width;
            _minHeight = minHeight;
            _padding = padding;
            _topLevel = topLevel;
        }

        public void Dispose()
        {
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(_minHeight, contentBottom - _origin.Y + _padding);
            _draw.ChannelsSetCurrent(_topLevel ? 0 : 1);
            _draw.AddRectFilled(_origin, _origin + new Vector2(_width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            _draw.AddRect(_origin, _origin + new Vector2(_width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            if (_topLevel)
            {
                _draw.ChannelsMerge();
                _cardDepth--;
            }
            else
            {
                _draw.ChannelsSetCurrent(2);
            }
            if (_reserveWidth)
            {
                // An auto-sized window sizes itself from items, so the card's full width is reserved as one.
                ImGui.SetCursorScreenPos(_origin + new Vector2(0, height));
                ImGui.Dummy(new Vector2(_width, _padding));
                return;
            }
            ImGui.SetCursorScreenPos(_origin + new Vector2(0, height + _padding));
        }
    }

    internal static void Card(string id, Action content, float minHeight = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float padding = 12;
        var topLevel = _cardDepth == 0;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, width - (2 * padding)));
            ImGui.BeginGroup();
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + padding);
            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + padding));
        }
        finally
        {
            if (topLevel)
                _cardDepth--;
        }
    }

    internal static void PushControlColors()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.AccentActive);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.AccentActive);
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
