using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;

namespace CurrencySpender.Windows;

/// <summary>Plugin-local drawn controls for the Currency Spender shell and content surfaces.</summary>
internal static class CurrencyChrome
{
    internal const float Control = 36f;
    internal const float Radius = 8f;
    internal const float CardPadding = 16f;
    internal const float CardGap = 16f;
    /// <summary>Height of one content line inside a table cell; icons, text and small buttons centre in it.</summary>
    internal const float LineSlot = 28f;
    internal const float SmallButtonHeight = 26f;
    private static readonly Vector2 ToggleSize = new(44, 24);
    private static readonly Vector4 Clear = new(0, 0, 0, 0);
    private static int _cardDepth;
    private static readonly Stack<float> Rights = new();
    private static float _shellWidth;
    private static float _shellHeight;
    private static float _lastToggleBottom = float.NaN;
    private static bool _inTable;
    private static nint _tableDrawList;
    private static int _tableVars;

    /// <summary>Outer gutter shared by every drawn part of a window.</summary>
    internal static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

    // ---- measuring and text ------------------------------------------------------------------------------------

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

    internal static float WrappedHeight(string text, float width, float scale = 1f)
        => Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1, width)).Y);

    internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1)
        => DrawText(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    /// <summary>Greedy word wrap measured at the draw size.</summary>
    internal static List<string> WrapLines(string text, float width, float scale = 1f)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && TextWidth(candidate, scale) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    internal static string FitText(string text, float width, float scale = 1)
    {
        if (width <= 0)
            return "";
        if (TextWidth(text, scale) <= width)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length] + ellipsis, scale) > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }

    /// <summary>Width left to the right edge of the enclosing card or column (else the window content region).</summary>
    internal static float Avail()
        => Rights.Count > 0 ? MathF.Max(1, Rights.Peek() - ImGui.GetCursorScreenPos().X) : ImGui.GetContentRegionAvail().X;

    // ---- frame style -------------------------------------------------------------------------------------------

    internal static void PushFrame()
    {
        _inTable = false; // a table that threw mid-draw must not leave the cell helpers active for the next frame
        Theme.PushFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, MathF.Max(4, MathF.Floor((Control - ImGui.GetTextLineHeight()) / 2f))));

        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Surface);
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
        ImGui.PopStyleVar(6);
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

    // ---- shell: header band ------------------------------------------------------------------------------------

    internal static float BrandWidth(string title) => 48 + TextWidth(title, Theme.TitleScale) + 16;

    private static void Brand(ImDrawListPtr draw, Vector2 pos, float width, string title)
    {
        var size = new Vector2(width, 40);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, 20), 12, Theme.Pack(Theme.Accent));
        DrawText(draw, pos + new Vector2(48, (40 - LineHeight(Theme.TitleScale)) / 2f),
            FitText(title, width - 48 - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
    }

    internal static float PillWidth()
        => MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

    internal static float ActionWidth(float minWidth, params string[] labels)
        => MathF.Max(minWidth, labels.Max(label => TextWidth(label)) + 32f);

    internal static void TopBar(string title, UiState state, string? actionLabel = null, Action? action = null,
        ShellControls? controls = null, string? utilityLabel = null, Action? utilityAction = null, string? utilityTooltip = null)
    {
        var winPos = ImGui.GetWindowPos();
        var winWidth = ImGui.GetWindowWidth();
        _shellWidth = winWidth;
        _shellHeight = ImGui.GetWindowHeight();
        var gutter = Gutter(winWidth);
        var draw = ImGui.GetWindowDrawList();
        const float gap = 8;
        var inner = winWidth - (2 * gutter);
        var stateLabel = Copy.Word(state);
        var pillWidth = PillWidth();
        var brandNatural = BrandWidth(title);
        var actionWidth = actionLabel is null || action is null ? 0 : ActionWidth(96, actionLabel);
        var utilityWidth = utilityLabel is null || utilityAction is null ? 0 : ActionWidth(96, utilityLabel);
        var controlsWidth = controls is null ? 0 : gap + Control + gap + Control;
        var required = brandNatural + gap + pillWidth + controlsWidth
            + (actionWidth > 0 ? actionWidth + gap : 0) + (utilityWidth > 0 ? utilityWidth + gap : 0);
        var singleRow = required <= inner;
        var barHeight = singleRow ? 68f : 14 + 40 + 8 + Control + 12;
        var row1 = winPos.Y + 16;
        var row2 = winPos.Y + 14 + 40 + 8;

        draw.PushClipRect(winPos, winPos + new Vector2(winWidth, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barHeight), winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var right = winPos.X + winWidth - gutter;
        Vector2 closePos = default;
        Vector2 minimizePos = default;
        if (controls is not null)
        {
            closePos = new Vector2(right - Control, row1);
            minimizePos = new Vector2(closePos.X - gap - Control, row1);
            right = minimizePos.X - gap;
        }
        var brandWidth = singleRow ? brandNatural : MathF.Min(brandNatural, MathF.Max(80, right - (winPos.X + gutter)));
        Brand(draw, new Vector2(winPos.X + gutter, winPos.Y + 14), brandWidth, title);

        // Controls to the left of the window buttons: row 1 when everything fits, else on a second row.
        var cluster = right;
        var clusterY = row1;
        var pillPos = new Vector2(cluster - pillWidth, clusterY);
        Vector2 utilityPos = default;
        Vector2 actionPos = default;
        if (singleRow)
        {
            cluster = pillPos.X - gap;
            if (utilityWidth > 0)
            {
                utilityPos = new Vector2(cluster - utilityWidth, clusterY);
                cluster = utilityPos.X - gap;
            }
            if (actionWidth > 0)
                actionPos = new Vector2(cluster - actionWidth, clusterY);
        }
        else
        {
            var x = winPos.X + gutter;
            if (actionWidth > 0)
            {
                actionPos = new Vector2(x, row2);
                x += actionWidth + gap;
            }
            if (utilityWidth > 0)
                utilityPos = new Vector2(x, row2);
            pillPos = new Vector2(winPos.X + winWidth - gutter - pillWidth, row2);
        }

        if (actionWidth > 0 && OutlineButton(draw, "top_" + actionLabel, actionLabel!, actionPos, new Vector2(actionWidth, Control)))
            action!();
        if (utilityWidth > 0)
        {
            if (OutlineButton(draw, "shell_utility_" + utilityLabel, utilityLabel!, utilityPos, new Vector2(utilityWidth, Control)))
                utilityAction!();
            if (utilityTooltip is not null && ImGui.IsItemHovered())
                ImGui.SetTooltip(utilityTooltip);
        }
        StatusPill(draw, pillPos, new Vector2(pillWidth, Control), stateLabel, state);
        if (controls is not null)
        {
            if (WindowButton(draw, "shell_minimize", minimizePos, controls.Minimized ? WindowGlyph.Restore : WindowGlyph.Minimize,
                    controls.Minimized ? "Restore" : "Minimize"))
                controls.ToggleMinimize();
            if (WindowButton(draw, "shell_close", closePos, WindowGlyph.Close, "Close"))
                controls.Close();
            controls.MinimizedHeight = barHeight + 1;
        }
        ImGui.SetCursorScreenPos(winPos + new Vector2(0, barHeight + 1));
        ImGui.Dummy(new Vector2(winWidth, 0));
    }

    internal enum WindowGlyph { Close, Minimize, Restore }

    /// <summary>Square 36 px window button with a drawn glyph and a tooltip anchored under its right edge.</summary>
    internal static bool WindowButton(ImDrawListPtr draw, string id, Vector2 pos, WindowGlyph glyph, string tooltip)
    {
        var size = new Vector2(Control, Control);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##currency_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var c = pos + (size / 2f);
        var col = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        switch (glyph)
        {
            case WindowGlyph.Close:
                draw.AddLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), col, 1.5f);
                draw.AddLine(c + new Vector2(-6, 6), c + new Vector2(6, -6), col, 1.5f);
                break;
            case WindowGlyph.Minimize:
                draw.AddLine(c + new Vector2(-6, 0), c + new Vector2(6, 0), col, 1.5f);
                break;
            default:
                draw.AddRect(c + new Vector2(-6, -3), c + new Vector2(3, 6), col, 1.5f, ImDrawFlags.None, 1.5f);
                draw.AddLine(c + new Vector2(-3, -6), c + new Vector2(6, -6), col, 1.5f);
                draw.AddLine(c + new Vector2(6, -6), c + new Vector2(6, 3), col, 1.5f);
                break;
        }
        if (hovered)
            AnchoredTooltip(new Vector2(pos.X + size.X, pos.Y + size.Y + 4), tooltip);
        return clicked;
    }

    internal static void AnchoredTooltip(Vector2 anchor, string text)
    {
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var color = Copy.MarkColor(state);
        var neutral = state is UiState.Idle or UiState.NeedsSetup or UiState.Empty;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        var group = (2 * Theme.DotRadius) + 8 + TextWidth(label);
        var x = pos.X + ((size.X - group) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        DrawText(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), label,
            neutral ? Theme.TextSecondary : color);
    }

    /// <summary>Owns custom-shell close and minimize state without changing window registration identity.</summary>
    internal sealed class ShellControls
    {
        private readonly Dalamud.Interface.Windowing.Window _window;
        private readonly ImGuiWindowFlags _expandedFlags;
        private readonly Vector2 _minimumSize;
        private readonly Vector2 _maximumSize;
        private Vector2? _savedSize;

        internal ShellControls(Dalamud.Interface.Windowing.Window window, Vector2 minimumSize, Vector2 maximumSize)
        {
            _window = window;
            _expandedFlags = window.Flags | ImGuiWindowFlags.NoTitleBar;
            _minimumSize = minimumSize;
            _maximumSize = maximumSize;
            _window.Flags = _expandedFlags;
            _window.SizeConstraints = new() { MinimumSize = minimumSize, MaximumSize = maximumSize };
        }

        internal bool Minimized { get; private set; }

        /// <summary>Height of the header band, measured each frame, so the minimized window is trimmed to the bar.</summary>
        internal float MinimizedHeight { get; set; } = 70f;

        internal bool PrepareDraw()
        {
            if (Minimized)
            {
                var width = MathF.Max(_minimumSize.X, _savedSize?.X ?? ImGui.GetWindowSize().X);
                _window.SizeConstraints = new()
                {
                    MinimumSize = new Vector2(_minimumSize.X, MinimizedHeight),
                    MaximumSize = new Vector2(_maximumSize.X, MinimizedHeight),
                };
                _window.Size = new Vector2(width, MinimizedHeight);
                _window.SizeCondition = ImGuiCond.Always;
                _window.Flags = _expandedFlags | ImGuiWindowFlags.NoResize;
                return true;
            }

            _window.SizeConstraints = new() { MinimumSize = _minimumSize, MaximumSize = _maximumSize };
            _window.Flags = _expandedFlags;
            _window.SizeCondition = ImGuiCond.FirstUseEver;
            _savedSize = ImGui.GetWindowSize();
            return false;
        }

        internal void ToggleMinimize()
        {
            Minimized = !Minimized;
            if (Minimized)
                return;
            _window.SizeConstraints = new() { MinimumSize = _minimumSize, MaximumSize = _maximumSize };
            _window.Flags = _expandedFlags;
            if (_savedSize is { } saved)
            {
                _window.Size = saved;
                _window.SizeCondition = ImGuiCond.Always;
            }
        }

        internal void Close() => _window.IsOpen = false;
    }

    // ---- shell: sub-navigation band and scrolling body ---------------------------------------------------------

    internal static int FeatureRow(string id, IReadOnlyList<string> labels, int selected)
    {
        var result = selected;
        var winPos = ImGui.GetWindowPos();
        var winWidth = ImGui.GetWindowWidth();
        var origin = ImGui.GetCursorScreenPos();
        origin.Y -= ImGui.GetStyle().ItemSpacing.Y; // the band starts flush under the header rule, not one item spacing below it
        var gutter = Gutter(winWidth);
        var draw = ImGui.GetWindowDrawList();
        const float gap = 12;
        var inner = winWidth - (2 * gutter);
        var widths = labels.Select(label => 16 + 12 + 8 + TextWidth(label, Theme.SecondaryScale) + 16 + 1).ToArray();
        var collapse = widths.Sum() + ((labels.Count - 1) * gap) > inner;
        var bandHeight = collapse ? 12 + Control + 12 : 56f;

        draw.PushClipRect(new Vector2(winPos.X, origin.Y), winPos + new Vector2(winWidth, origin.Y - winPos.Y + bandHeight + 1), false);
        draw.AddRectFilled(new Vector2(winPos.X, origin.Y), new Vector2(winPos.X + winWidth, origin.Y + bandHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, origin.Y + bandHeight), new Vector2(winPos.X + winWidth, origin.Y + bandHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        if (collapse)
        {
            var pos = new Vector2(winPos.X + gutter, origin.Y + 12);
            var size = new Vector2(inner, Control);
            ImGui.SetCursorScreenPos(pos);
            var open = ImGui.InvisibleButton("##currency_nav_field_" + id, size);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Accent with { W = 0.14f }), Radius);
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), Radius);
            DrawText(draw, pos + new Vector2(16, (Control - LineHeight()) / 2f), FitText(labels[selected], inner - 56), Theme.Accent);
            var chevron = pos + new Vector2(inner - 20, Control / 2f);
            draw.AddLine(chevron + new Vector2(-4, -2), chevron + new Vector2(0, 2), Theme.Pack(Theme.Accent), 1.5f);
            draw.AddLine(chevron + new Vector2(0, 2), chevron + new Vector2(4, -2), Theme.Pack(Theme.Accent), 1.5f);
            _ = hovered;
            var popupId = "##currency_nav_menu_" + id;
            if (open)
                ImGui.OpenPopup(popupId);
            ImGui.SetNextWindowPos(pos + new Vector2(0, Control + 4));
            ImGui.SetNextWindowSize(new Vector2(inner, 0));
            if (ImGui.BeginPopup(popupId))
            {
                for (var i = 0; i < labels.Count; i++)
                    if (ImGui.Selectable(labels[i] + "##nav_" + i, i == selected, ImGuiSelectableFlags.None, new Vector2(0, 28)))
                        result = i;
                ImGui.EndPopup();
            }
        }
        else
        {
            var x = winPos.X + gutter;
            for (var i = 0; i < labels.Count; i++)
            {
                if (FeatureButton(draw, id + "_" + i, labels[i], new Vector2(x, origin.Y + 12), new Vector2(widths[i], 32), selected == i))
                    result = i;
                x += widths[i] + gap;
            }
        }
        ImGui.SetCursorScreenPos(new Vector2(winPos.X, origin.Y + bandHeight + 1));
        ImGui.Dummy(new Vector2(winWidth, 0));
        return result;
    }

    private static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##currency_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        DrawText(draw, pos + new Vector2(36, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f), label,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, Theme.SecondaryScale);
        return clicked;
    }

    private const float BodyScrollbar = 10f;
    private static readonly Dictionary<string, bool> _scrollbarShown = new();
    private static string _bodyId = "";
    private static bool _bodyIndented;
    private static int _bodyColors;
    private static int _bodyVars;

    /// <summary>Starts the one scrolling body child below the shell bands (and above an optional footer).</summary>
    internal static void BeginBody(string id, float footerHeight = 0)
    {
        var winPos = ImGui.GetWindowPos();
        var winWidth = ImGui.GetWindowWidth();
        var winHeight = ImGui.GetWindowHeight();
        var top = ImGui.GetCursorScreenPos();
        top.Y -= ImGui.GetStyle().ItemSpacing.Y; // the band above ended with a zero-height Dummy; the body starts flush under its rule
        var gutter = Gutter(winWidth);
        // The scrollbar sits inside the right gutter: while it shows, the right padding shrinks by its width and the left side is indented
        // by the same amount, so content spans gutter to width - gutter in both states.
        _scrollbarShown.TryGetValue(id, out var shown);
        var side = shown ? gutter - BodyScrollbar : gutter;
        var height = MathF.Max(40, winPos.Y + winHeight - top.Y - footerHeight);
        ImGui.SetCursorScreenPos(new Vector2(winPos.X, top.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, BodyScrollbar);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(side, 16));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Clear);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Clear);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.5f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Border, Clear); // the transparent 1 px border makes the child honour its window padding
        _bodyVars = 2;
        _bodyColors = 6;
        ImGui.BeginChild(id, new Vector2(winWidth, height), true);
        _bodyId = id;
        _bodyIndented = shown;
        if (shown)
            ImGui.Indent(BodyScrollbar);
        ImGui.PopStyleVar(3); // the child captured padding and border; popups and tooltips opened in the body keep the default style
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder); // content widgets keep the suite border
        _lastToggleBottom = float.NaN;
    }

    internal static void EndBody()
    {
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var draw = ImGui.GetWindowDrawList();
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var transparent = ImGui.GetColorU32(ImGuiCol.WindowBg) & 0x00FFFFFFu;
            var opaque = ImGui.GetColorU32(ImGuiCol.WindowBg) | 0xFF000000u;
            draw.AddRectFilledMultiColor(pos + new Vector2(0, size.Y - 28), pos + size, transparent, transparent, opaque, opaque);
        }
        if (_bodyIndented)
            ImGui.Unindent(BodyScrollbar);
        _scrollbarShown[_bodyId] = ImGui.GetScrollMaxY() > 0;
        ImGui.EndChild();
        ImGui.PopStyleColor(_bodyColors);
        ImGui.PopStyleVar(_bodyVars);
    }

    /// <summary>Height of the footer band for the current window; 0 when the window is too short to spare it.</summary>
    internal static float FooterHeight()
        => ImGui.GetWindowHeight() < 400 ? 0f : 12 + MathF.Max(LineHeight() + 4 + LineHeight(Theme.SecondaryScale), Control) + 12;

    /// <summary>Footer band with a status dot and a two-line status block at the gutter; call after <see cref="EndBody"/> with <see cref="FooterHeight"/>.</summary>
    internal static void Footer(float height, string status, string detail, UiState state)
    {
        if (height <= 0)
            return;
        var winPos = ImGui.GetWindowPos();
        var width = ImGui.GetWindowWidth();
        var gutter = Gutter(width);
        var top = winPos.Y + ImGui.GetWindowHeight() - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(winPos, winPos + ImGui.GetWindowSize(), false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + ImGui.GetWindowSize(), Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + width, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        var block = LineHeight() + 4 + LineHeight(Theme.SecondaryScale);
        var textY = top + ((height - block) / 2f);
        var textX = winPos.X + gutter + 16;
        var room = winPos.X + width - gutter - textX;
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + 4, textY + (LineHeight() / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        var shownStatus = FitText(status, room);
        var shownDetail = FitText(detail, room, Theme.SecondaryScale);
        DrawText(draw, new Vector2(textX, textY), shownStatus, Theme.TextPrimary);
        DrawText(draw, new Vector2(textX, textY + LineHeight() + 4), shownDetail, Theme.TextSecondary, Theme.SecondaryScale);
        if (shownStatus != status || shownDetail != detail)
        {
            ImGui.SetCursorScreenPos(new Vector2(textX, textY));
            ImGui.InvisibleButton("##currency_footer_text", new Vector2(MathF.Max(1, room), block));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(detail.Length > 0 ? status + "\n" + detail : status);
        }
        ImGui.SetCursorScreenPos(new Vector2(winPos.X, top + height));
        ImGui.Dummy(Vector2.Zero);
    }

    // ---- page header ---------------------------------------------------------------------------------------------

    internal static void PageHeader(string title, string description, string liveCount)
    {
        var windowWidth = _shellWidth > 0 ? _shellWidth : ImGui.GetWindowWidth();
        var compact = _shellHeight > 0 && _shellHeight < 480;
        var wide = windowWidth >= 760;
        var cursor = ImGui.GetCursorScreenPos();
        var origin = cursor + new Vector2(0, compact ? 0 : 4);
        var width = Avail();
        var draw = ImGui.GetWindowDrawList();
        const float tile = 44;
        const float titleScale = 1.35f;
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), Radius);
        Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var textX = origin.X + tile + 16;
        var titleHeight = LineHeight(titleScale);
        var smallHeight = LineHeight(Theme.SecondaryScale);
        float height;
        if (wide)
        {
            var metaWidth = MathF.Min(0.4f * windowWidth, TextWidth(liveCount, Theme.SecondaryScale));
            var room = width - tile - 16 - metaWidth - 16;
            DrawText(draw, new Vector2(textX, origin.Y), FitText(title, room, titleScale), Theme.TextPrimary, titleScale);
            if (!compact && description.Length > 0)
                DrawText(draw, new Vector2(textX, origin.Y + titleHeight + 4), FitText(description, room, Theme.SecondaryScale),
                    Theme.TextSecondary, Theme.SecondaryScale);
            DrawTextRight(draw, origin.X + width, origin.Y + ((titleHeight - smallHeight) / 2f),
                FitText(liveCount, metaWidth, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
            height = MathF.Max(tile, titleHeight + (compact ? 0 : 4 + smallHeight));
        }
        else
        {
            var room = width - tile - 16;
            var y = origin.Y;
            DrawText(draw, new Vector2(textX, y), FitText(title, room, titleScale), Theme.TextPrimary, titleScale);
            y += titleHeight;
            if (!compact && description.Length > 0)
            {
                y += 4;
                foreach (var line in WrapLines(description, room, Theme.SecondaryScale))
                {
                    DrawText(draw, new Vector2(textX, y), line, Theme.TextSecondary, Theme.SecondaryScale);
                    y += smallHeight;
                }
            }
            if (liveCount.Length > 0)
            {
                y += 4;
                DrawText(draw, new Vector2(textX, y), FitText(liveCount, room, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
                y += smallHeight;
            }
            height = MathF.Max(tile, y - origin.Y);
        }
        ImGui.SetCursorScreenPos(cursor);
        ImGui.Dummy(new Vector2(width, (origin.Y - cursor.Y) + height + 16));
    }

    // ---- buttons -------------------------------------------------------------------------------------------------

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##currency_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, color);
        return clicked;
    }

    internal static bool Button(string id, string label, bool selected = false, float width = 0)
    {
        var buttonWidth = width > 0 ? width : TextWidth(label) + 32;
        return OutlineButton(ImGui.GetWindowDrawList(), id, label, ImGui.GetCursorScreenPos(),
            new Vector2(MathF.Min(buttonWidth, MathF.Max(40, Avail())), Control), selected);
    }

    internal static float SmallButtonWidth(string label) => TextWidth(label) + 24;

    /// <summary>Compact action button for table cells, drawn at the cursor.</summary>
    internal static bool SmallButton(string id, string label, bool accent = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(SmallButtonWidth(label), SmallButtonHeight);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##currency_small_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(accent ? Theme.Accent with { W = 0.14f } : Theme.Surface), 6f);
        draw.AddRect(pos, pos + size, Theme.Pack(accent || hovered ? Theme.Accent : Theme.SurfaceBorder), 6f);
        DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label,
            accent ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    // ---- sections, columns, cards -------------------------------------------------------------------------------

    internal static void Section(string title, string? description = null, string? count = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var titleHeight = LineHeight(Theme.TitleScale);
        DrawText(draw, pos, title, Theme.TextPrimary, Theme.TitleScale);
        if (!string.IsNullOrEmpty(count))
        {
            var smallHeight = LineHeight(Theme.SecondaryScale);
            DrawText(draw, new Vector2(pos.X + TextWidth(title, Theme.TitleScale) + 12, pos.Y + titleHeight - smallHeight - 2),
                count, Theme.TextSecondary, Theme.SecondaryScale);
        }
        ImGui.Dummy(new Vector2(Avail(), titleHeight));
        if (!string.IsNullOrWhiteSpace(description))
            Muted(description);
        ImGui.Dummy(new Vector2(1, 4));
    }

    /// <summary>Wrapped secondary text at the secondary size, using the card's wrap edge.</summary>
    internal static void Muted(string text)
    {
        ImGui.SetWindowFontScale(Theme.SecondaryScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
    }

    private sealed class ColumnState
    {
        public float Main;
        public float Rail;
        public int MainCards;
        public int RailCards;
    }

    private static readonly Dictionary<string, ColumnState> Columns = new();
    private static bool _inColumn;
    private static int _columnCardIndex;
    private static int _fillIndex = -1;
    private static float _fillBottom;
    private static float _stretchAdded;

    /// <summary>Main column and a right rail; below the breakpoint both stack. Equal heights get equal bottoms.</summary>
    internal static void TwoColumns(string id, Action work, Action rail, float? railFraction = null)
    {
        var avail = Avail();
        if (avail < 820)
        {
            work();
            rail();
            return;
        }

        const float gap = 16;
        var railWidth = railFraction is { } fraction
            ? MathF.Round((avail - gap) * fraction)
            : Math.Clamp(MathF.Round(avail * 0.26f), 300, 360);
        var mainWidth = avail - gap - railWidth;
        var origin = ImGui.GetCursorScreenPos();
        if (!Columns.TryGetValue(id, out var state))
            Columns[id] = state = new ColumnState();
        var target = state.Main > 0 && state.Rail > 0 && MathF.Abs(state.Main - state.Rail) <= 48 ? MathF.Max(state.Main, state.Rail) : 0;

        var natural = new float[2];
        var stretch = new float[2];
        var cards = new int[2];
        for (var column = 0; column < 2; column++)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(column == 0 ? 0 : mainWidth + gap, 0));
            ImGui.BeginGroup();
            Rights.Push(origin.X + (column == 0 ? mainWidth : avail));
            _inColumn = true;
            _columnCardIndex = 0;
            _fillIndex = (column == 0 ? state.MainCards : state.RailCards) - 1;
            _fillBottom = target > 0 ? origin.Y + target - 8 : 0;
            _stretchAdded = 0;
            if (column == 0)
                work();
            else
                rail();
            _inColumn = false;
            Rights.Pop();
            ImGui.EndGroup();
            stretch[column] = _stretchAdded;
            natural[column] = ImGui.GetItemRectMax().Y - origin.Y - _stretchAdded;
            cards[column] = _columnCardIndex;
        }
        state.Main = natural[0];
        state.Rail = natural[1];
        state.MainCards = cards[0];
        state.RailCards = cards[1];
        _fillIndex = -1;
        _fillBottom = 0;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + MathF.Max(natural[0] + stretch[0], natural[1] + stretch[1])));
        ImGui.Dummy(new Vector2(avail, 0));
    }

    internal static bool Toggle(string literalLabel, ref bool value, string? help = null, string? id = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Max(20, width - ToggleSize.X - 16);
        var hasHelp = !string.IsNullOrWhiteSpace(help);
        var labelHeight = WrappedHeight(literalLabel, textWidth);
        var helpHeight = hasHelp ? WrappedHeight(help!, textWidth, Theme.SecondaryScale) : 0;
        var block = labelHeight + (hasHelp ? 4 + helpHeight : 0);
        var padding = hasHelp ? 12f : 8f;
        var rowHeight = MathF.Max(ToggleSize.Y, block) + (2 * padding);
        var draw = ImGui.GetWindowDrawList();

        if (!float.IsNaN(_lastToggleBottom) && MathF.Abs(_lastToggleBottom - pos.Y) < 1.5f)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }));

        var togglePos = new Vector2(pos.X + width - ToggleSize.X, pos.Y + ((rowHeight - ToggleSize.Y) / 2f));
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##currency_setting_" + (id ?? literalLabel), ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize,
            Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8,
            Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var y = pos.Y + ((rowHeight - block) / 2f);
        var lineHeight = LineHeight();
        foreach (var line in WrapLines(literalLabel, textWidth))
        {
            DrawText(draw, new Vector2(pos.X, y), line, Theme.TextPrimary);
            y += lineHeight;
        }
        if (hasHelp)
        {
            y += 4;
            var smallHeight = LineHeight(Theme.SecondaryScale);
            foreach (var line in WrapLines(help!, textWidth, Theme.SecondaryScale))
            {
                DrawText(draw, new Vector2(pos.X, y), line, Theme.TextSecondary, Theme.SecondaryScale);
                y += smallHeight;
            }
        }

        _lastToggleBottom = pos.Y + rowHeight;
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, rowHeight - ImGui.GetStyle().ItemSpacing.Y));
        return clicked;
    }

    /// <summary>Label on the left, bordered integer field on the right, optional help text below.</summary>
    internal static bool IntField(string label, ref int value, string? help = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var draw = ImGui.GetWindowDrawList();
        if (!float.IsNaN(_lastToggleBottom) && MathF.Abs(_lastToggleBottom - pos.Y) < 1.5f)
        {
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f })); // same hairline as between toggle rows
            pos.Y += 12;
            ImGui.SetCursorScreenPos(pos);
        }
        var fieldWidth = MathF.Min(168, MathF.Max(110, width - TextWidth(label) - 16));
        DrawText(draw, pos + new Vector2(0, (Control - LineHeight()) / 2f), label, Theme.TextPrimary);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + width - fieldWidth, pos.Y));
        ImGui.SetNextItemWidth(fieldWidth);
        var changed = ImGui.InputInt("##currency_int_" + label, ref value);
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + Control + 4));
        if (!string.IsNullOrWhiteSpace(help))
            Muted(help);
        return changed;
    }

    /// <summary>Full-width combo with a drawn chevron; pair with <see cref="EndCombo"/> when it returns true.</summary>
    internal static bool BeginCombo(string id, string preview)
    {
        var width = Avail();
        var pos = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        ImGui.SetNextItemWidth(width);
        var open = ImGui.BeginCombo("##currency_combo_" + id, FitText(preview, width - 64), ImGuiComboFlags.NoArrowButton);
        var chevron = pos + new Vector2(width - 20, Control / 2f);
        var color = Theme.Pack(Theme.TextSecondary);
        draw.AddLine(chevron + new Vector2(-4, -2), chevron + new Vector2(0, 2), color, 1.5f);
        draw.AddLine(chevron + new Vector2(0, 2), chevron + new Vector2(4, -2), color, 1.5f);
        return open;
    }

    internal static void EndCombo() => ImGui.EndCombo();

    internal static bool BeginResultList(string id, int rows)
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        var open = ImGui.BeginListBox(id, new Vector2(Avail(), MathF.Min((rows * 28f) + 16, 176f)));
        if (!open)
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
        }
        return open;
    }

    internal static void EndResultList()
    {
        ImGui.EndListBox();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    internal static void Card(string id, Action content, CurrencyCardTone tone = CurrencyCardTone.Surface, float minHeight = 0, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = Avail();
        var cardWidth = width <= 0 ? available : MathF.Min(width, available);
        var topLevel = _cardDepth == 0;
        var padY = tone == CurrencyCardTone.Row ? 12f : CardPadding;
        var columnIndex = topLevel && _inColumn ? _columnCardIndex++ : -1;
        if (topLevel)
        {
            _cardDepth++;
            draw.ChannelsSplit(3);
        }
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, padY));
            Rights.Push(origin.X + cardWidth - CardPadding);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
            ImGui.BeginGroup();
            _lastToggleBottom = float.NaN;
            _tableEndY = float.NaN;
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            Rights.Pop();
            var contentBottom = ImGui.GetItemRectMax().Y;
            // A table that ends the card carries its own bottom cell padding: trim it so the bottom inset matches the top.
            var trim = !float.IsNaN(_tableEndY) && MathF.Abs(contentBottom - _tableEndY) < 1.5f ? TableCellPaddingY : 0f;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + padY - trim);
            if (columnIndex >= 0 && columnIndex == _fillIndex && _fillBottom > origin.Y + height)
            {
                _stretchAdded += _fillBottom - origin.Y - height;
                height = _fillBottom - origin.Y;
            }

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var (fill, border) = CardColors(tone);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), fill, Radius);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), border, Radius);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
            ImGui.Dummy(new Vector2(cardWidth, (topLevel ? CardGap : 8f) - ImGui.GetStyle().ItemSpacing.Y));
        }
        finally
        {
            if (topLevel)
                _cardDepth--;
        }
    }

    // ---- tables --------------------------------------------------------------------------------------------------

    /// <summary>Hairline-row table filling the card. Fixed and stretch columns share the card width; there is no inner scroll region.</summary>
    internal static bool BeginTable(string id, int columns, ImGuiTableFlags flags, bool horizontalScroll = false)
    {
        _ = horizontalScroll;
        flags = (flags & ~ImGuiTableFlags.Borders) | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 0));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Clear);
        _tableVars = 2;
        var open = ImGui.BeginTable(id, columns, flags, new Vector2(Avail(), 0));
        if (open)
        {
            _inTable = true;
            _tableDrawList = DrawListId();
            return true;
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        return false;
    }

    internal static void EndTable()
    {
        _headerIndentColumn = -1;
        ImGui.EndTable();
        _inTable = false;
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(_tableVars);
        ImGui.Dummy(new Vector2(1, 0));
        _tableEndY = ImGui.GetItemRectMax().Y;
    }

    private static float _tableEndY = float.NaN;
    private const float TableCellPaddingY = 6f;

    /// <summary>Header row: labels at the secondary size, numeric columns right-aligned, sort arrow beside its own label.</summary>
    private static int _headerIndentColumn = -1;
    private static float _headerIndent;

    /// <summary>Indents one header label, for columns whose cells start with a fixed lead slot.</summary>
    internal static void SetHeaderIndent(int column, float indent)
    {
        _headerIndentColumn = column;
        _headerIndent = indent;
    }

    internal static void TableHeaders(params bool[] rightAligned)
    {
        var sortColumn = -1;
        var ascending = true;
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.IsNull && specs.SpecsCount > 0)
        {
            ImGuiTableColumnSortSpecsPtr spec = specs.Specs;
            sortColumn = spec.ColumnIndex;
            ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
        }
        var draw = ImGui.GetWindowDrawList();
        var columnCount = ImGui.TableGetColumnCount();
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i < columnCount; i++)
        {
            ImGui.TableSetColumnIndex(i);
            var name = ImGui.TableGetColumnName(i);
            var start = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.PushStyleColor(ImGuiCol.Text, Clear);
            ImGui.TableHeader(name);
            ImGui.PopStyleColor();
            var right = i < rightAligned.Length && rightAligned[i];
            var arrowRoom = i == sortColumn ? 14f : 0f;
            var label = FitText(name, width - arrowRoom, Theme.SecondaryScale);
            var labelWidth = TextWidth(label, Theme.SecondaryScale);
            var y = start.Y + ((ImGui.GetTextLineHeight() - LineHeight(Theme.SecondaryScale)) / 2f);
            var labelX = right ? start.X + width - labelWidth : start.X + (i == _headerIndentColumn ? _headerIndent : 0);
            DrawText(draw, new Vector2(labelX, y), label, Theme.TextSecondary, Theme.SecondaryScale);
            if (i == sortColumn)
            {
                var arrowX = right ? labelX - 10 : labelX + labelWidth + 10;
                var cy = start.Y + (ImGui.GetTextLineHeight() / 2f);
                var color = Theme.Pack(Theme.Accent);
                if (ascending)
                    draw.AddTriangleFilled(new Vector2(arrowX - 4, cy + 2.5f), new Vector2(arrowX + 4, cy + 2.5f), new Vector2(arrowX, cy - 3.5f), color);
                else
                    draw.AddTriangleFilled(new Vector2(arrowX - 4, cy - 2.5f), new Vector2(arrowX + 4, cy - 2.5f), new Vector2(arrowX, cy + 3.5f), color);
            }
        }
    }

    /// <summary>True while a <see cref="BeginTable"/> table is open: helpers then centre content in a line slot.</summary>
    internal static bool InTable => _inTable && DrawListId() == _tableDrawList;

    /// <summary>Identity of the current window's draw list: a tooltip or popup opened from a table row is another window, so it is not "in the table".</summary>
    private static unsafe nint DrawListId() => (nint)ImGui.GetWindowDrawList().Handle;

    /// <summary>Draws every line of the text as a table cell line when a table is open; false (nothing drawn) otherwise.</summary>
    internal static bool TextCells(string text, bool right)
    {
        if (!InTable)
            return false;
        foreach (var line in text.Split('\n'))
            TextCell(line, right);
        return true;
    }

    /// <summary>Centres content of the given height inside a cell area of the given height; call <see cref="CellEnd"/> afterwards.</summary>
    internal static float CellBegin(float contentHeight, float areaHeight)
    {
        var y = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(y + MathF.Max(0, (areaHeight - contentHeight) / 2f));
        return y;
    }

    internal static void CellEnd(float startY, float areaHeight)
    {
        var missing = startY + areaHeight - ImGui.GetCursorPosY();
        if (missing > 0)
            ImGui.Dummy(new Vector2(1, missing));
    }

    private static int _cellFrame;
    private static int _cellSequence;

    private static string NextCellId()
    {
        var frame = ImGui.GetFrameCount();
        if (frame != _cellFrame)
        {
            _cellFrame = frame;
            _cellSequence = 0;
        }
        return "##currency_cell_" + _cellSequence++;
    }

    /// <summary>One text line centred in a <see cref="LineSlot"/>, elided to the cell with the full text on hover.</summary>
    internal static void TextCell(string text, bool right)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton(NextCellId(), new Vector2(width, LineSlot));
        var shown = FitText(text, width);
        var x = right ? pos.X + width - TextWidth(shown) : pos.X;
        draw.AddText(new Vector2(x, pos.Y + ((LineSlot - ImGui.GetTextLineHeight()) / 2f)), ImGui.GetColorU32(ImGuiCol.Text), shown);
        if (shown != text && ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    /// <summary>Number followed by a currency icon, right aligned in one line slot.</summary>
    internal static bool IconTextCell(string text, ImTextureID icon)
    {
        if (!InTable)
            return false;
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton(NextCellId(), new Vector2(width, LineSlot));
        var right = pos.X + width;
        var iconPos = new Vector2(right - 20, pos.Y + ((LineSlot - 20) / 2f));
        draw.AddImage(icon, iconPos, iconPos + new Vector2(20));
        draw.AddText(new Vector2(iconPos.X - 8 - TextWidth(text), pos.Y + ((LineSlot - ImGui.GetTextLineHeight()) / 2f)),
            ImGui.GetColorU32(ImGuiCol.Text), text);
        return true;
    }

    /// <summary>Marks a sub-row of a combined table row: an elbow connector, an indent and muted text.</summary>
    internal static void ChildRowBegin()
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var color = Theme.Pack(Theme.TextSecondary with { W = 0.55f });
        draw.AddLine(pos + new Vector2(6, 0), pos + new Vector2(6, LineSlot / 2f), color);
        draw.AddLine(pos + new Vector2(6, LineSlot / 2f), pos + new Vector2(16, LineSlot / 2f), color);
        ImGui.SetCursorScreenPos(pos + new Vector2(24, 0));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
    }

    internal static void ChildRowEnd() => ImGui.PopStyleColor();

    /// <summary>Width of the Flag and TP button pair, for the shared Actions column.</summary>
    internal static float MapButtonsWidth() => SmallButtonWidth("Flag") + 8 + SmallButtonWidth("TP");

    internal static Vector2 MapButtonsBegin() => MapButtonsBegin(MapButtonsWidth());

    /// <summary>Starts a right-aligned button pair centred in one line slot; returns the slot's top for <see cref="MapButtonsEnd"/>.</summary>
    internal static Vector2 MapButtonsBegin(float pairWidth)
    {
        var top = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorScreenPos(new Vector2(top.X + MathF.Max(0, width - pairWidth), top.Y + ((LineSlot - SmallButtonHeight) / 2f)));
        return top;
    }

    internal static void MapButtonsEnd(Vector2 slotTop)
    {
        ImGui.SetCursorScreenPos(slotTop + new Vector2(0, LineSlot));
        ImGui.Dummy(Vector2.Zero);
    }

    internal static void ContextPopup(string id, Action content)
    {
        using var context = ImRaii.ContextPopupItem(id);
        if (context)
            content();
    }

    // ---- notices and empty states ------------------------------------------------------------------------------

    /// <summary>Rounded notice: tinted for alert states, with an optional lead sentence split on an em dash.</summary>
    internal static void Notice(string text, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var mark = Copy.MarkColor(state);
        var alert = state is UiState.Blocked or UiState.Failed or UiState.Running or UiState.Missing;
        var split = text.IndexOf(" — ", StringComparison.Ordinal);
        var lead = split > 0 ? text[..split] : null;
        var body = split > 0 ? text[(split + 3)..] : text;
        var textX = origin.X + 34;
        var room = width - 34 - 16;
        var lineHeight = LineHeight();
        var smallHeight = LineHeight(Theme.SecondaryScale);
        var bodyColor = lead is null ? Theme.TextPrimary : Theme.TextSecondary;
        var bodyScale = lead is null ? 1f : Theme.SecondaryScale;
        var bodyLines = WrapLines(body, room, bodyScale);
        var bodyLineHeight = lead is null ? lineHeight : smallHeight;
        var height = 12 + (lead is null ? 0 : lineHeight + 4) + (bodyLines.Count * bodyLineHeight) + 12;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(alert ? mark with { W = 0.12f } : Theme.Surface), Radius);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(alert ? mark with { W = 0.45f } : Theme.SurfaceBorder), Radius);
        var y = origin.Y + 12;
        draw.AddCircleFilled(new Vector2(origin.X + 16, y + (lineHeight / 2f)), Theme.DotRadius, Theme.Pack(mark));
        if (lead is not null)
        {
            DrawText(draw, new Vector2(textX, y), FitText(lead, room), Theme.TextPrimary);
            y += lineHeight + 4;
        }
        foreach (var line in bodyLines)
        {
            DrawText(draw, new Vector2(textX, y), line, bodyColor, bodyScale);
            y += bodyLineHeight;
        }
        ImGui.Dummy(new Vector2(width, height + CardGap - ImGui.GetStyle().ItemSpacing.Y));
    }

    internal static void EmptyState(string title, string detail, string? actionLabel = null, Action? action = null)
        => Card("empty_" + title, () => EmptyBlock(title, detail, actionLabel, action));

    /// <summary>Centred empty state: state dot and title, muted explanation and an optional call to action.</summary>
    internal static void EmptyBlock(string title, string detail, string? actionLabel = null, Action? action = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var width = Avail();
        var origin = ImGui.GetCursorScreenPos();
        var room = MathF.Min(width, 460);
        var lines = WrapLines(detail, room, Theme.SecondaryScale);
        var smallHeight = LineHeight(Theme.SecondaryScale);
        var y = origin.Y + 8;
        var titleWidth = TextWidth(title, Theme.TitleScale);
        var groupWidth = (2 * Theme.DotRadius) + 8 + titleWidth;
        var x = origin.X + ((width - groupWidth) / 2f);
        var titleHeight = LineHeight(Theme.TitleScale);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, y + (titleHeight / 2f)), Theme.DotRadius, Theme.Pack(Copy.MarkColor(UiState.Empty)));
        DrawText(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, y), title, Theme.TextPrimary, Theme.TitleScale);
        y += titleHeight + 8;
        foreach (var line in lines)
        {
            DrawText(draw, new Vector2(origin.X + ((width - TextWidth(line, Theme.SecondaryScale)) / 2f), y), line, Theme.TextSecondary, Theme.SecondaryScale);
            y += smallHeight;
        }
        var bottom = y;
        if (actionLabel is not null && action is not null)
        {
            y += 16;
            var buttonWidth = MathF.Max(144, TextWidth(actionLabel) + 40);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + ((width - buttonWidth) / 2f), y));
            if (Button("empty_action_" + title, actionLabel, true, buttonWidth))
                action();
            bottom = y + Control;
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, bottom - origin.Y + 8));
    }

    /// <summary>Hairline across the card with space above and below, to separate sub-sections.</summary>
    internal static void Divider()
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(1, 8));
        ImGui.GetWindowDrawList().AddLine(pos + new Vector2(0, 4), pos + new Vector2(Avail(), 4), Theme.Pack(Theme.SurfaceBorder));
    }

    /// <summary>Thin progress track under the page header.</summary>
    internal static void ProgressTrack(float fraction)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        if (fraction > 0)
            draw.AddRectFilled(pos, pos + new Vector2(width, Theme.BarHeight), Theme.Pack(Theme.SurfaceBorder), 2f);
        if (fraction > 0)
            draw.AddRectFilled(pos, pos + new Vector2(width * Math.Clamp(fraction, 0, 1), Theme.BarHeight), Theme.Pack(Theme.Accent), 2f);
        ImGui.Dummy(new Vector2(width, Theme.BarHeight + 16 - ImGui.GetStyle().ItemSpacing.Y));
    }

    /// <summary>Release heading of the changelog: version, muted date and an outlined channel badge; a hairline separates releases.</summary>
    internal static void ReleaseHeading(string heading, bool first)
    {
        var draw = ImGui.GetWindowDrawList();
        var width = Avail();
        if (!first)
        {
            var line = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(1, 12));
            draw.AddLine(line + new Vector2(0, 6), line + new Vector2(width, 6), Theme.Pack(Theme.SurfaceBorder));
        }
        var match = System.Text.RegularExpressions.Regex.Match(heading, @"^(?<v>\S+)\s*(\((?<d>[^)]*)\))?\s*(\[(?<t>[^\]]*)\])?");
        var version = match.Success ? match.Groups["v"].Value : heading;
        var date = match.Groups["d"].Value;
        var tag = match.Groups["t"].Value;
        var pos = ImGui.GetCursorScreenPos();
        var titleHeight = LineHeight(Theme.TitleScale);
        var smallHeight = LineHeight(Theme.SecondaryScale);
        DrawText(draw, pos, version, Theme.TextPrimary, Theme.TitleScale);
        var x = pos.X + TextWidth(version, Theme.TitleScale) + 12;
        if (date.Length > 0)
        {
            DrawText(draw, new Vector2(x, pos.Y + ((titleHeight - smallHeight) / 2f)), date, Theme.TextSecondary, Theme.SecondaryScale);
            x += TextWidth(date, Theme.SecondaryScale) + 12;
        }
        if (tag.Length > 0)
        {
            var tagHeight = LineHeight(Theme.CaptionScale) + 6;
            var tagWidth = TextWidth(tag, Theme.CaptionScale) + 16;
            var min = new Vector2(x, pos.Y + ((titleHeight - tagHeight) / 2f));
            draw.AddRectFilled(min, min + new Vector2(tagWidth, tagHeight), Theme.Pack(Theme.Accent with { W = 0.14f }), tagHeight / 2f);
            draw.AddRect(min, min + new Vector2(tagWidth, tagHeight), Theme.Pack(Theme.Accent with { W = 0.55f }), tagHeight / 2f);
            DrawText(draw, min + new Vector2(8, 3), tag, Theme.Accent, Theme.CaptionScale);
        }
        ImGui.Dummy(new Vector2(width, titleHeight + 4));
    }

    /// <summary>Changelog group label such as Added, Changed or Fixed.</summary>
    internal static void Subheading(string text)
    {
        ImGui.SetWindowFontScale(Theme.SecondaryScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
    }

    /// <summary>Bulleted muted paragraph.</summary>
    internal static void Bullet(string text)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        draw.AddCircleFilled(pos + new Vector2(5, LineHeight(Theme.SecondaryScale) / 2f), 2f, Theme.Pack(Theme.TextSecondary));
        ImGui.SetCursorScreenPos(pos + new Vector2(16, 0));
        Muted(text);
    }

    /// <summary>Numbered accent badge for the instruction cards.</summary>
    internal static void StepBadge(int number)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var center = pos + new Vector2(14, 14);
        draw.AddCircleFilled(center, 14, Theme.Pack(Theme.Accent with { W = 0.14f }));
        draw.AddCircle(center, 14, Theme.Pack(Theme.Accent with { W = 0.55f }));
        var text = number.ToString();
        DrawText(draw, center - new Vector2(TextWidth(text, Theme.SecondaryScale) / 2f, LineHeight(Theme.SecondaryScale) / 2f), text,
            Theme.Accent, Theme.SecondaryScale);
    }

    // ---- stat cards ----------------------------------------------------------------------------------------------

    internal readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    internal static void StatCards(params Stat[] stats)
    {
        var available = Avail();
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var count = stats.Length;
        var wide = available >= (count * 220) + ((count - 1) * 16);
        var row = !wide && available >= (count * 128) + ((count - 1) * 8);
        var gap = wide ? 16f : 8f;
        var pad = wide || !row ? 16f : 12f;
        var valueScale = wide || !row ? 1.7f : 1.4f;
        var inner = wide || !row ? 8f : 6f;
        var width = wide || row ? (available - ((count - 1) * gap)) / count : available;
        var height = pad + LineHeight(Theme.SecondaryScale) + inner + LineHeight(valueScale) + inner + Theme.BarHeight + inner
            + LineHeight(Theme.SecondaryScale) + pad;
        for (var i = 0; i < count; i++)
        {
            var stat = stats[i];
            var pos = wide || row ? origin + new Vector2(i * (width + gap), 0) : origin + new Vector2(0, i * (height + 16));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
            var smallHeight = LineHeight(Theme.SecondaryScale);
            var valueHeight = LineHeight(valueScale);
            var y = pos.Y + pad;
            var compactRow = row && !wide;
            var labelLead = compactRow ? 0f : 16f; // the compact one-row variant drops the diamond so the label keeps its width
            var labelRoom = width - (2 * pad) - labelLead;
            var label = stat.Label;
            if (compactRow && TextWidth(label, Theme.SecondaryScale) > labelRoom && label.Contains(' '))
                label = label[..label.IndexOf(' ')]; // short form, first word only
            if (!compactRow)
                Diamond(draw, new Vector2(pos.X + pad + 5, y + (smallHeight / 2f)), 5, Theme.Pack(Theme.Accent));
            DrawText(draw, new Vector2(pos.X + pad + labelLead, y), FitText(label, labelRoom, Theme.SecondaryScale),
                Theme.TextSecondary, Theme.SecondaryScale);
            y += smallHeight + inner;
            var dash = stat.Value == "—";
            DrawText(draw, new Vector2(pos.X + pad, y), stat.Value, dash ? Theme.TextSecondary : Theme.TextPrimary, valueScale);
            if (!string.IsNullOrEmpty(stat.Cap))
            {
                var cap = char.IsDigit(stat.Cap.TrimStart('/', ' ')[0]) ? "/ " + stat.Cap.TrimStart('/', ' ') : stat.Cap;
                var capX = pos.X + pad + TextWidth(stat.Value, valueScale) + 8;
                DrawText(draw, new Vector2(capX, y + valueHeight - smallHeight - (0.08f * valueHeight)),
                    FitText(cap, pos.X + width - pad - capX, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + inner;
            if (stat.Fraction is { } fraction)
            {
                var min = new Vector2(pos.X + pad, y);
                var max = new Vector2(pos.X + width - pad, y + Theme.BarHeight);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), 2f);
            }
            y += Theme.BarHeight + inner;
            DrawText(draw, new Vector2(pos.X + pad, y), FitText(stat.Note, width - (2 * pad), Theme.SecondaryScale),
                stat.Fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary, Theme.SecondaryScale);
        }
        var total = wide || row ? height : (count * height) + ((count - 1) * 16);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, total + CardGap - ImGui.GetStyle().ItemSpacing.Y));
    }

    private static (uint Fill, uint Border) CardColors(CurrencyCardTone tone) => tone switch
    {
        CurrencyCardTone.Row => (Theme.Pack(Theme.ShellBar), Theme.Pack(Theme.SurfaceBorder)),
        CurrencyCardTone.Recommended => (Theme.Pack(Theme.StateGreen with { W = 0.10f }), Theme.Pack(Theme.StateGreen with { W = 0.35f })),
        CurrencyCardTone.Attention => (Theme.Pack(Theme.StateAmber with { W = 0.10f }), Theme.Pack(Theme.StateAmber with { W = 0.35f })),
        _ => (Theme.Pack(Theme.Surface), Theme.Pack(Theme.SurfaceBorder)),
    };
}

internal enum CurrencyCardTone
{
    Surface,
    Row,
    Recommended,
    Attention,
}
