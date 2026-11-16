using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyCrucible;

/// <summary>
///     Drawn controls shared by the Crucible shell and its cards. Every measurement derives from the live font
///     (CalcTextSize, line height) and the 4 px Theme grid, never from constants tuned for one font.
/// </summary>
internal static class CrucibleChrome
{
    public const float Radius = 2 * Theme.Unit;
    public const float Gap = 4 * Theme.Unit;
    public const float ControlHeight = 9 * Theme.Unit;
    public const float PageTitleScale = 1.35f;
    public const float StatValueScale = 1.7f;
    public static readonly Vector2 ToggleSize = new(44, 24);

    private static readonly Stack<float> Rights = new();

    /// <summary>True while the window is short: bands and notices use tighter padding so the body keeps room.</summary>
    public static bool Compact { get; set; }

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

    // ---- shell controls ----

    internal static float BrandWidth()
        => (3 * Theme.Unit) + 24 + (3 * Theme.Unit) + TextWidth("LazyCrucible", Theme.TitleScale) + (4 * Theme.Unit);

    internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crucible_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(3 * Theme.Unit + 12, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        var textSize = ImGui.CalcTextSize("LazyCrucible") * Theme.TitleScale;
        DrawText(draw, pos + new Vector2((3 * Theme.Unit) + 24 + (3 * Theme.Unit), (size.Y - textSize.Y) / 2f),
            "LazyCrucible", Theme.TextPrimary, Theme.TitleScale);
        return clicked;
    }

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crucible_" + id, size);
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

    /// <summary>Close box with a drawn cross and a tooltip anchored under its right edge.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crucible_close", size);
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

    /// <summary>Full-width navigation row: left-aligned label, chevron on the right.</summary>
    internal static bool NavRow(string id, string label, float width)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, ControlHeight);
        var clicked = ImGui.InvisibleButton("##crucible_nav_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.ShellBar), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        DrawFitted(draw, pos + new Vector2(Gap, (size.Y - LineHeight()) / 2f), label, width - (3 * Gap),
            hovered ? Theme.TextPrimary : Theme.TextSecondary);
        Chevron(draw, pos + new Vector2(width - Gap - 2, size.Y / 2f), hovered ? Theme.Accent : Theme.TextSecondary, right: true);
        return clicked;
    }

    /// <summary>Compact outlined button sized to its label, left-aligned at the cursor.</summary>
    internal static bool ActionButton(string id, string label)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Min(Avail(), TextWidth(label) + (2 * Gap)), ControlHeight);
        return OutlineButton(draw, id, label, pos, size);
    }

    internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crucible_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
        var marker = pos + new Vector2(Gap, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var textX = Gap + 12 + (2 * Theme.Unit);
        DrawFitted(draw, pos + new Vector2(textX, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f), label, size.X - textX - Gap,
            selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, Theme.SecondaryScale);
        return clicked;
    }

    internal static float FeatureWidth(string label) => Gap + 12 + (2 * Theme.Unit) + TextWidth(label, Theme.SecondaryScale) + Gap + 1;

    /// <summary>One width for every state so the search box never moves between states.</summary>
    internal static float PillWidth()
    {
        var words = Enum.GetValues<UiState>().Select(Copy.Word).Append("Needs setup");
        return MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + words.Max(w => TextWidth(w)) + 16);
    }

    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
    {
        var color = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
        var textSize = ImGui.CalcTextSize(label);
        var contentWidth = (Theme.DotRadius * 2) + (2 * Theme.Unit) + textSize.X;
        var left = pos.X + ((size.X - contentWidth) / 2f);
        draw.AddCircleFilled(new Vector2(left + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        // The neutral state colour is too dim for text on the pill; the dot keeps it, the word reads as secondary text.
        var textColor = state is UiState.Idle or UiState.Empty or UiState.NeedsSetup ? Theme.TextSecondary : color;
        draw.AddText(new Vector2(left + (Theme.DotRadius * 2) + (2 * Theme.Unit), pos.Y + ((size.Y - textSize.Y) / 2f)), Theme.Pack(textColor), label);
    }

    // ---- combo ----

    private static int _comboStyleVars;
    private static int _comboStyleColors;

    /// <summary>A rounded dark combo with a small chevron. Pair with <see cref="EndCombo"/>.</summary>
    internal static bool BeginCombo(string id, string preview, float width)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var framePadding = new Vector2(Gap, MathF.Max(Theme.Unit, (ControlHeight - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        _comboStyleVars = 5;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.28f });
        _comboStyleColors = 8;
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

    // ---- rows ----

    /// <summary>A selectable list row: title with an optional sub line and trailing text. Returns true when clicked.</summary>
    internal static bool ListRow(string id, string title, string? sub, string? trail, bool selected)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var padX = Gap;
        var padY = string.IsNullOrEmpty(sub) ? 2.5f * Theme.Unit : 3 * Theme.Unit;
        var height = (2 * padY) + LineHeight() + (string.IsNullOrEmpty(sub) ? 0 : Theme.Unit / 2f + LineHeight(Theme.SecondaryScale));
        var clicked = ImGui.InvisibleButton("##row_" + id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (selected)
        {
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Accent with { W = 0.14f }), Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Accent with { W = 0.55f }), Radius);
        }
        else if (hovered)
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(new Vector4(1f, 1f, 1f, 0.05f)), Radius);
        var trailWidth = string.IsNullOrEmpty(trail) ? 0 : TextWidth(trail, Theme.SecondaryScale) + Gap;
        DrawFitted(draw, pos + new Vector2(padX, padY), title, width - (2 * padX) - trailWidth,
            selected ? Theme.Accent : Theme.TextPrimary);
        if (!string.IsNullOrEmpty(trail))
            DrawTextRight(draw, pos.X + width - padX, pos.Y + padY + ((LineHeight() - LineHeight(Theme.SecondaryScale)) / 2f), trail, Theme.TextSecondary, Theme.SecondaryScale);
        if (!string.IsNullOrEmpty(sub))
            DrawFitted(draw, pos + new Vector2(padX, padY + LineHeight() + (Theme.Unit / 2f)), sub, width - (2 * padX), Theme.TextSecondary, Theme.SecondaryScale);
        return clicked;
    }

    /// <summary>Centered pill selector entry (board and page pickers).</summary>
    internal static bool Pill(string id, string label, bool selected, float height = 32)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(TextWidth(label) + (4 * Theme.Unit * 2), height);
        var clicked = OutlineButton(draw, id, label, pos, size, selected);
        ImGui.SetCursorScreenPos(pos + new Vector2(size.X, 0));
        ImGui.Dummy(new Vector2(0, size.Y));
        return clicked;
    }

    /// <summary>A flowing line of small chips that wraps at the available width.</summary>
    internal static void Chips(IEnumerable<string> labels)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Avail();
        var chipHeight = LineHeight(Theme.SecondaryScale) + (2 * Theme.Unit);
        float x = 0, y = 0;
        foreach (var label in labels)
        {
            var chipWidth = MathF.Min(width, TextWidth(label, Theme.SecondaryScale) + (3 * Theme.Unit * 2));
            if (x > 0 && x + chipWidth > width)
            {
                x = 0;
                y += chipHeight + Theme.Unit;
            }
            var min = origin + new Vector2(x, y);
            draw.AddRectFilled(min, min + new Vector2(chipWidth, chipHeight), Theme.Pack(Theme.ShellBar), chipHeight / 2f);
            draw.AddRect(min, min + new Vector2(chipWidth, chipHeight), Theme.Pack(Theme.SurfaceBorder), chipHeight / 2f);
            DrawFitted(draw, min + new Vector2(3 * Theme.Unit, Theme.Unit), label, chipWidth - (6 * Theme.Unit), Theme.TextSecondary, Theme.SecondaryScale);
            x += chipWidth + Theme.Unit;
        }
        Advance(origin, width, y + chipHeight, Theme.Unit * 2);
    }

    // ---- notices ----

    private static bool IsAlert(UiState state) => state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;

    /// <summary>Split "Lead — body" into the bold-ish lead and the secondary body.</summary>
    internal static void Notice(string text, UiState state, bool strong = false)
    {
        var split = text.IndexOf(" — ", StringComparison.Ordinal);
        if (split > 0)
            Notice(text[..split], text[(split + 3)..], state, strong);
        else
            Notice("", text, state, strong);
    }

    /// <summary>Rounded banner: tinted for alerts, neutral surface for information, with a state dot.</summary>
    internal static void Notice(string lead, string body, UiState state, bool strong = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var color = Copy.MarkColor(state);
        var padX = Gap;
        var padY = (Compact ? 2 : 3) * Theme.Unit;
        var textLeft = padX + (Theme.DotRadius * 2) + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textLeft - padX);
        var lh = LineHeight();
        var inline = lead.Length > 0 && body.Length > 0 && TextWidth(lead + " —") + Theme.Unit * 2 + TextWidth(body) <= textWidth;
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
            Theme.Pack(alert ? color with { W = strong ? 0.20f : 0.12f } : Theme.Surface), Radius);
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
        Advance(pos, width, height, (Compact ? 2 : 3) * Theme.Unit);
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
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##crucible_setting_" + literalLabel, ToggleSize);
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

    /// <summary>A two-line card heading: title, optional trailing count, optional caption underneath.</summary>
    internal static void CardHeader(string title, string? caption = null, string? count = null)
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
        if (!string.IsNullOrEmpty(caption))
        {
            height += Theme.Unit;
            height += DrawWrapped(draw, pos + new Vector2(0, height), caption, width, Theme.TextSecondary, Theme.SecondaryScale);
        }
        Advance(pos, width, height, 3 * Theme.Unit);
    }
}
