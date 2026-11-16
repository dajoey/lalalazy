using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Fork-local drawn controls for the approved PvP Solver settings shell. Geometry follows the suite chrome spec:
///     36 px header and footer controls, 32 px controls inside cards, 16 px card padding, a 4 px rhythm, and every
///     width derived from text measured at the size it is drawn.
/// </summary>
internal static class PvpApprovedChrome
{
    internal const float ControlHeight = 36f;
    internal const float BodyControl = 32f;
    internal const float IndentStep = 24f;

    private static readonly Vector2 ToggleSize = new(44, 24);

    // Screen X of the inner right edge of the innermost card; widgets size against it.
    private static readonly Stack<float> Rights = new();
    private static int _rowIndex;

    /// <summary>Screen rectangle of the settings window this frame (popups centre on it).</summary>
    internal static Vector2 WindowPos { get; set; }

    internal static Vector2 WindowSize { get; set; }

    /// <summary>True while a confirmation popup is open this frame, so the shell can dim the window behind it.</summary>
    internal static bool ModalOpen { get; set; }

    /// <summary>Clears per-frame layout memory so an exception in an earlier frame cannot leak into this one.</summary>
    internal static void BeginFrame()
    {
        Rights.Clear();
        _rowIndex = 0;
        ModalOpen = false;
    }

    // ---- measuring --------------------------------------------------------------------------------------------

    private static T Measure<T>(float scale, Func<T> measure)
    {
        if (scale == 1f)
        {
            return measure();
        }

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

    internal static void PopRight()
    {
        if (Rights.Count > 0)
        {
            Rights.Pop();
        }
    }

    /// <summary>Width left to the nearest card edge (a card's inner padding is respected).</summary>
    internal static float Avail()
        => Rights.Count > 0 ? Rights.Peek() - ImGui.GetCursorScreenPos().X : ImGui.GetContentRegionAvail().X;

    internal static string FitText(string text, float width, float scale = 1f)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (TextWidth(text, scale) <= width)
        {
            return text;
        }

        const string ellipsis = "…";
        int length = text.Length;
        while (length > 0 && TextWidth(text[..length].TrimEnd() + ellipsis, scale) > width)
        {
            length--;
        }

        return length == 0 ? string.Empty : text[..length].TrimEnd() + ellipsis;
    }

    /// <summary>Greedy word wrap at the draw size, clamped to <paramref name="maxLines"/> with an ellipsis on the last line.</summary>
    internal static List<string> WrapLines(string text, float width, float scale, int maxLines)
    {
        List<string> lines = [];
        string current = string.Empty;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || TextWidth(candidate, scale) <= width)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        if (lines.Count > maxLines)
        {
            string rest = string.Join(" ", lines.Skip(maxLines - 1));
            lines = lines.Take(maxLines - 1).ToList();
            lines.Add(FitText(rest + "…", width, scale));
        }

        return lines;
    }

    // ---- text -------------------------------------------------------------------------------------------------

    internal static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f, float wrapWidth = 0f)
    {
        if (text.Length == 0)
        {
            return;
        }

        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, wrapWidth);
    }

    internal static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => Text(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    private static Vector4 Dim(Vector4 color) => color with { W = color.W * 0.5f };

    /// <summary>
    ///     Flowing paragraph drawn on the draw list at measured height; the cursor moves below it plus
    ///     <paramref name="after"/>. Long notes stop at <paramref name="maxWidth"/> so lines stay readable.
    /// </summary>
    internal static void Para(string text, float scale, Vector4 color, float after = 4f, float indent = 0f, float maxWidth = 760f)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float w = MathF.Max(1, MathF.Min(maxWidth, Avail() - indent));
        float h = WrappedHeight(text, w, scale);
        Vector2 pos = ImGui.GetCursorScreenPos() + new Vector2(indent, 0);
        Text(ImGui.GetWindowDrawList(), pos, text, color, scale, w);
        ImGui.Dummy(new Vector2(w + indent, h));
        ImGui.SetCursorScreenPos(new Vector2(pos.X - indent, pos.Y + h + after));
    }

    /// <summary>A tooltip in the suite style (rounded, bordered, wrapped).</summary>
    internal static void Tip(string text, float wrap = 420f)
    {
        using var colors = ImRaii.PushColor(ImGuiCol.PopupBg, Theme.Surface)
            .Push(ImGuiCol.Border, Theme.SurfaceBorder);
        using var vars = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 8))
            .Push(ImGuiStyleVar.WindowRounding, 8f)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f);
        using var tooltip = ImRaii.Tooltip();
        using var wrapPos = ImRaii.TextWrapPos(wrap);
        ImGui.TextUnformatted(text);
    }

    internal static void HoverTip(Vector2 min, Vector2 max, string text)
    {
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
        {
            Tip(text);
        }
    }

    // ---- shell pieces -----------------------------------------------------------------------------------------

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

    internal const string BrandName = "PvP Solver";

    internal static float BrandWidth(float scale) => 48 + TextWidth(BrandName, scale) + 16;

    internal static bool Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size, float scale)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_brand", size);
        bool hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        string shown = FitText(BrandName, size.X - 48 - 16, scale);
        Text(draw, pos + new Vector2(48, (size.Y - LineHeight(scale)) / 2f), shown, Theme.TextPrimary, scale);
        return clicked;
    }

    /// <summary>A navigation pill of the header band: outlined, label centred, accent when selected.</summary>
    internal static bool NavPill(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_nav_" + id, size);
        bool hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Theme.Accent with { W = 0.14f } : Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
        Vector4 color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        Text(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, color);
        return clicked;
    }

    /// <summary>A sub-navigation pill with a radio marker.</summary>
    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_feature_" + id, size);
        bool hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Theme.Accent with { W = 0.14f } : Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), 8f);
        Vector2 marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
        {
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        }

        Text(draw, pos + new Vector2(36, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f), label,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, Theme.SecondaryScale);
        return clicked;
    }

    internal static float FeatureButtonWidth(string label) => 16 + 12 + 8 + TextWidth(label, Theme.SecondaryScale) + 16 + 1;

    /// <summary>The widest state word fixes the pill width so it never shifts between states.</summary>
    internal static float StatusPillWidth()
        => MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        Vector4 mark = Copy.MarkColor(state);
        bool neutral = state is UiState.Idle or UiState.NeedsSetup or UiState.Empty;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        float content = (2 * Theme.DotRadius) + 8 + TextWidth(label);
        float x = pos.X + ((size.X - content) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), label,
            neutral ? Theme.TextSecondary : mark);
    }

    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_close", size);
        bool hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
        Vector2 center = pos + (size / 2f);
        uint color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), color, 1.5f);
        draw.AddLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), color, 1.5f);
        if (hovered)
        {
            // Below the control and right aligned, so the panel never covers the glyph or the pill.
            ImGui.SetNextWindowPos(new Vector2(pos.X + size.X, pos.Y + size.Y + 4), ImGuiCond.Always, new Vector2(1f, 0f));
            using var colors = ImRaii.PushColor(ImGuiCol.PopupBg, Theme.Surface)
                .Push(ImGuiCol.Border, Theme.SurfaceBorder);
            using var vars = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 4))
                .Push(ImGuiStyleVar.WindowRounding, 8f)
                .Push(ImGuiStyleVar.WindowBorderSize, 1f);
            using var tooltip = ImRaii.Tooltip();
            ImGui.TextUnformatted("Close");
        }

        return clicked;
    }

    // ---- buttons ----------------------------------------------------------------------------------------------

    internal enum ButtonStyle
    {
        Normal,
        Accent,
        Danger,
    }

    internal static float ButtonWidth(string label, float minWidth = 72f, float padding = 32f)
        => MathF.Max(minWidth, TextWidth(label) + padding);

    /// <summary>
    ///     A bordered button drawn at <paramref name="pos"/>. A disabled button keeps its outline and a legible dimmed
    ///     label, never reacts to hover, and still reports hover to a tooltip call that follows it.
    /// </summary>
    internal static bool Button(string id, string label, Vector2 pos, Vector2 size, ButtonStyle style = ButtonStyle.Normal,
        bool disabled = false, Vector4? fill = null)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_btn_" + id, size);
        bool hovered = !disabled && ImGui.IsItemHovered();
        Vector4 rest = fill ?? Theme.ShellBar;
        Vector4 background;
        Vector4 border;
        Vector4 text;
        if (style == ButtonStyle.Danger)
        {
            background = Theme.StateRed with { W = hovered ? 0.24f : 0.12f };
            border = Theme.StateRed with { W = hovered ? 0.85f : 0.55f };
            text = Theme.TextPrimary;
        }
        else if (style == ButtonStyle.Accent)
        {
            background = Theme.Accent with { W = hovered ? 0.22f : 0.14f };
            border = Theme.Accent with { W = disabled ? 0.4f : 1f };
            text = disabled ? Theme.Accent with { W = 0.6f } : Theme.Accent;
        }
        else
        {
            background = disabled ? rest with { W = 0.6f } : rest;
            border = hovered ? Theme.Accent : Theme.SurfaceBorder;
            text = disabled ? Theme.TextSecondary with { W = 0.65f } : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        }

        draw.AddRectFilled(pos, pos + size, Theme.Pack(background), 8f);
        draw.AddRect(pos, pos + size, Theme.Pack(border), 8f);
        Text(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, text);
        return clicked && !disabled;
    }

    /// <summary>A button of the card body at the cursor; the cursor moves past it like any item.</summary>
    internal static bool InlineButton(string id, string label, ButtonStyle style = ButtonStyle.Normal, bool disabled = false, float minWidth = 72f)
        => Button(id, label, ImGui.GetCursorScreenPos(), new Vector2(ButtonWidth(label, minWidth), BodyControl), style, disabled);

    /// <summary>A row of buttons that wraps onto the next line instead of leaving the card.</summary>
    internal static bool FlowButton(string id, string label, bool first, ButtonStyle style = ButtonStyle.Normal, float minWidth = 72f)
    {
        float width = ButtonWidth(label, minWidth);
        if (!first)
        {
            float next = ImGui.GetItemRectMax().X + 8;
            if (Fits(next, width))
            {
                ImGui.SameLine(0, 8);
            }
        }

        return Button(id, label, ImGui.GetCursorScreenPos(), new Vector2(width, BodyControl), style);
    }

    private static bool Fits(float x, float width) => Rights.Count == 0 || x + width <= Rights.Peek() + 0.5f;

    // ---- fields -----------------------------------------------------------------------------------------------

    /// <summary>
    ///     Runs <paramref name="draw"/> with the style of a slider, drag, input or combo: 32 px high, rounded, bordered,
    ///     a dark field on the card with an accent grab. The pushes are ImRaii objects, popped even if the call throws.
    /// </summary>
    internal static bool Field(Func<bool> draw)
    {
        using var vars = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(12, (BodyControl - LineHeight()) / 2f))
            .Push(ImGuiStyleVar.FrameRounding, 8f)
            .Push(ImGuiStyleVar.FrameBorderSize, 1f)
            .Push(ImGuiStyleVar.GrabMinSize, 10f)
            .Push(ImGuiStyleVar.GrabRounding, 8f)
            .Push(ImGuiStyleVar.PopupRounding, 8f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f);
        using var colors = ImRaii.PushColor(ImGuiCol.FrameBg, Theme.ShellBar)
            .Push(ImGuiCol.FrameBgHovered, Theme.ShellBar)
            .Push(ImGuiCol.FrameBgActive, Theme.ShellBar)
            .Push(ImGuiCol.Border, Theme.SurfaceBorder)
            .Push(ImGuiCol.SliderGrab, Theme.Accent)
            .Push(ImGuiCol.SliderGrabActive, Theme.AccentActive)
            .Push(ImGuiCol.PopupBg, Theme.Surface)
            .Push(ImGuiCol.Header, Theme.Accent with { W = 0.20f })
            .Push(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f })
            .Push(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.28f });
        return draw();
    }

    /// <summary>
    ///     A combo field with a drawn chevron. <paramref name="items"/> draws the entries and runs only while open.
    /// </summary>
    internal static void Combo(string id, string preview, float width, Action items)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 pos = ImGui.GetCursorScreenPos();
        _ = Field(() =>
        {
            ImGui.SetNextItemWidth(width);
            using var combo = ImRaii.Combo(id, FitText(preview, width - 24 - 28), ImGuiComboFlags.NoArrowButton);
            Vector2 chevron = new(pos.X + width - 20, pos.Y + (BodyControl / 2f));
            uint color = Theme.Pack(Theme.TextSecondary);
            draw.AddLine(chevron + new Vector2(-4, -2), chevron + new Vector2(0, 2), color, 1.5f);
            draw.AddLine(chevron + new Vector2(0, 2), chevron + new Vector2(4, -2), color, 1.5f);
            if (combo)
            {
                items();
            }

            return false;
        });
    }

    internal static bool Switch(string id, ref bool value, Vector2 pos, bool disabled)
    {
        ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGui.InvisibleButton("##pvp_switch_" + id, ToggleSize) && !disabled;
        if (clicked)
        {
            value = !value;
        }

        bool hovered = !disabled && ImGui.IsItemHovered();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector4 track = value ? Theme.Accent : Theme.SurfaceBorder;
        Vector4 knob = value ? Theme.Surface : Theme.TextSecondary;
        draw.AddRectFilled(pos, pos + ToggleSize, Theme.Pack(disabled ? Dim(track) : track), ToggleSize.Y / 2f);
        if (hovered)
        {
            draw.AddRect(pos, pos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        }

        draw.AddCircleFilled(pos + new Vector2(value ? 32 : 12, 12), 8, Theme.Pack(disabled ? Dim(knob) : knob));
        return clicked;
    }

    // ---- cards and rows ---------------------------------------------------------------------------------------

    /// <summary>A content-measured rounded card with 16 px padding and a 16 px gap after it.</summary>
    internal static void Card(string id, Action content, float minHeight = 0)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = Avail();
        const float pad = 16;
        using var idScope = ImRaii.PushId(id);
        _rowIndex = 0;
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        PushRight(origin.X + width - pad);
        float contentBottom = origin.Y + (2 * pad);
        try
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(pad, pad));
            using (var group = ImRaii.Group())
            {
                content();
            }

            contentBottom = ImGui.GetItemRectMax().Y;
        }
        catch
        {
            // The group is closed by now, so its rectangle still measures what was drawn before the failure.
            contentBottom = ImGui.GetItemRectMax().Y;
            ImGui.SetCursorScreenPos(origin);
            throw;
        }
        finally
        {
            PopRight();
            float height = MathF.Max(minHeight, contentBottom - origin.Y + pad);
            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            draw.ChannelsMerge();
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        }

        ImGui.Dummy(new Vector2(width, 16 - Theme.ItemSpacing.Y));
    }

    /// <summary>Pulls the cursor up so the next block sits <paramref name="gap"/> pixels below the last item instead of the 8 px item spacing.</summary>
    internal static void Tighten(float gap) => ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() - new Vector2(0, Theme.ItemSpacing.Y - gap));

    /// <summary>Makes the next row draw its top hairline, after content that is not a row (a picker, a paragraph).</summary>
    internal static void SeparateNextRow() => _rowIndex = 1;

    /// <summary>The title of a card section; the rows below start their own rhythm after it.</summary>
    internal static void CardTitle(string title, bool blocks = false)
    {
        Para(title, Theme.TitleScale, Theme.TextPrimary, blocks ? 12 : 4);
        _rowIndex = 0;
    }

    /// <summary>A rounded notice: state dot, lead line, then the body below. Alert states are tinted.</summary>
    internal static void Notice(string lead, string body, UiState state)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = Avail();
        float textX = origin.X + 34;
        float textW = MathF.Max(1, width - 34 - 16);
        float leadH = WrappedHeight(lead, textW);
        float bodyH = body.Length == 0 ? 0 : WrappedHeight(body, textW, Theme.SecondaryScale);
        float height = 12 + leadH + (bodyH > 0 ? 4 + bodyH : 0) + 12;
        Vector4 mark = Copy.MarkColor(state);
        bool alert = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(alert ? mark with { W = 0.12f } : Theme.ShellBar), 8f);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(alert ? mark with { W = 0.45f } : Theme.SurfaceBorder), 8f);
        draw.AddCircleFilled(new Vector2(origin.X + 16, origin.Y + 12 + (LineHeight() / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, new Vector2(textX, origin.Y + 12), lead, Theme.TextPrimary, 1f, textW);
        if (bodyH > 0)
        {
            Text(draw, new Vector2(textX, origin.Y + 12 + leadH + 4), body, Theme.TextSecondary, Theme.SecondaryScale, textW);
        }

        ImGui.Dummy(new Vector2(width, height));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 12 - Theme.ItemSpacing.Y));
    }

    private static void Separator(ImDrawListPtr draw, Vector2 origin, float width, bool joined)
    {
        if (_rowIndex++ > 0 && !joined)
        {
            draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        }
    }

    /// <summary>
    ///     One settings row: label and a muted help line on the left, the control on the right edge of the card, an
    ///     optional Reset in a fixed slot to its left. Below 200 px of text width the control moves under the text. All
    ///     heights are measured, and a row is 24 px taller than its content (12 above and below).
    /// </summary>
    /// <param name="control">Draws the control; receives the top left of the control box (size <paramref name="controlWidth"/> by <paramref name="controlHeight"/>).</param>
    /// <param name="onReset">Shows a Reset slot; invoked when it is pressed. The slot is visible only while <paramref name="resetVisible"/>.</param>
    internal static void Row(string id, string label, string? secondary, float controlWidth, float controlHeight,
        Action<Vector2>? control, int indent = 0, bool disabled = false, bool joined = false,
        Action? onReset = null, bool resetVisible = false, string? tag = null)
    {
        using var idScope = ImRaii.PushId(id);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = Avail();
        float resetWidth = onReset is null ? 0 : ButtonWidth("Reset", 56, 24);
        float cluster = resetWidth + (resetWidth > 0 && controlWidth > 0 ? 8 : 0) + controlWidth;
        float clusterHeight = MathF.Max(controlWidth > 0 ? controlHeight : 0, resetWidth > 0 ? BodyControl : 0);
        float left = origin.X + (indent * IndentStep);
        float full = width - (indent * IndentStep);
        bool hasControl = cluster > 0;
        bool stacked = hasControl && full - cluster - 16 < 200f;
        float textW = MathF.Max(40, hasControl && !stacked ? full - cluster - 16 : full);
        bool hasSecondary = !string.IsNullOrWhiteSpace(secondary);
        float labelH = WrappedHeight(label, textW);
        float secondaryH = hasSecondary ? WrappedHeight(secondary!, textW, Theme.SecondaryScale) : 0;
        float tagW = tag is null ? 0 : TextWidth(tag, Theme.SecondaryScale) + 16;
        float tagH = tag is null ? 0 : LineHeight(Theme.SecondaryScale) + 4;
        bool tagInline = tag is not null && labelH <= LineHeight() + 1 && TextWidth(label) + 8 + tagW <= textW;
        float block = labelH + (hasSecondary ? 4 + secondaryH : 0) + (tag is not null && !tagInline ? 4 + tagH : 0);
        float top = joined ? 4 : 12;
        float inner = stacked ? block + 8 + clusterHeight : MathF.Max(clusterHeight, block);
        float rowHeight = top + inner + 12;

        Separator(draw, origin, width, joined);
        float textTop = origin.Y + top + (stacked ? 0 : (inner - block) / 2f);
        Vector4 primary = disabled ? Dim(Theme.TextPrimary) : Theme.TextPrimary;
        Vector4 muted = disabled ? Dim(Theme.TextSecondary) : Theme.TextSecondary;
        Text(draw, new Vector2(left, textTop), label, primary, 1f, textW);
        float y = textTop + labelH;
        if (tag is not null)
        {
            Vector2 tagPos = tagInline
                ? new Vector2(left + TextWidth(label) + 8, textTop + ((LineHeight() - tagH) / 2f))
                : new Vector2(left, y + 4);
            draw.AddRectFilled(tagPos, tagPos + new Vector2(tagW, tagH), Theme.Pack(Theme.Accent with { W = 0.10f }), tagH / 2f);
            draw.AddRect(tagPos, tagPos + new Vector2(tagW, tagH), Theme.Pack(Theme.Accent with { W = 0.6f }), tagH / 2f);
            Text(draw, tagPos + new Vector2(8, (tagH - LineHeight(Theme.SecondaryScale)) / 2f), tag, Theme.Accent, Theme.SecondaryScale);
            if (!tagInline)
            {
                y += 4 + tagH;
            }
        }

        if (hasSecondary)
        {
            Text(draw, new Vector2(left, y + 4), secondary!, muted, Theme.SecondaryScale, textW);
        }

        try
        {
            if (hasControl)
            {
                Vector2 cluster0 = stacked
                    ? new Vector2(left, textTop + block + 8)
                    : new Vector2(origin.X + width - cluster, origin.Y + top + ((inner - clusterHeight) / 2f));
                // Side by side: Reset sits in a fixed slot left of the control. Stacked: the control keeps the left edge
                // (so tracks line up between rows) and Reset follows it.
                float x = cluster0.X;
                float resetX = stacked ? x + controlWidth + (controlWidth > 0 ? 8 : 0) : x;
                if (resetWidth > 0)
                {
                    if (resetVisible && !disabled
                        && Button("reset", "Reset", new Vector2(resetX, cluster0.Y + ((clusterHeight - BodyControl) / 2f)), new Vector2(resetWidth, BodyControl)))
                    {
                        onReset!();
                    }

                    if (!stacked)
                    {
                        x += resetWidth + 8;
                    }
                }

                if (controlWidth > 0 && control is not null)
                {
                    using var off = ImRaii.Disabled(disabled);
                    control(new Vector2(x, cluster0.Y + ((clusterHeight - controlHeight) / 2f)));
                }
            }
        }
        finally
        {
            // Even when a control throws, the row keeps its measured height so the card around it ends below it.
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, rowHeight));
            ImGui.SetCursorScreenPos(origin + new Vector2(0, rowHeight));
        }
    }

    /// <summary>A row that holds a free-form control line (for example a combo and a button) between hairlines.</summary>
    internal static void ControlLine(Action content, float height = BodyControl)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = Avail();
        Separator(draw, origin, width, false);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, 12));
        content();
        float rowHeight = 12 + height + 12;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rowHeight));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, rowHeight));
    }

    internal static bool Toggle(string id, string label, string? help, ref bool value, bool disabled = false, int indent = 0,
        bool joined = false, Action? onReset = null, bool resetVisible = false, string? tip = null)
    {
        bool current = value;
        bool clicked = false;
        Row(id, label, help, ToggleSize.X, ToggleSize.Y, pos =>
        {
            clicked = Switch(id, ref current, pos, disabled);
            if (tip is not null)
            {
                PvpUi.Tip(tip);
            }
        }, indent, disabled, joined, onReset, resetVisible);
        value = current;
        return clicked;
    }

    /// <summary>Segmented choice buttons (one outlined button per choice, the chosen one in the accent).</summary>
    internal static float ChoiceWidth(IEnumerable<string> labels)
        => labels.Sum(l => MathF.Max(64, TextWidth(l) + 28)) + (8 * (labels.Count() - 1));

    internal static string? ChoiceButtons(string id, IReadOnlyList<(string Name, string Label)> choices, string current, Vector2 pos)
    {
        string? picked = null;
        float x = pos.X;
        foreach ((string name, string label) in choices)
        {
            float w = MathF.Max(64, TextWidth(label) + 28);
            bool selected = name == current;
            if (Button(id + "_" + name, label, new Vector2(x, pos.Y), new Vector2(w, BodyControl),
                    selected ? ButtonStyle.Accent : ButtonStyle.Normal) && !selected)
            {
                picked = name;
            }

            x += w + 8;
        }

        return picked;
    }
}
