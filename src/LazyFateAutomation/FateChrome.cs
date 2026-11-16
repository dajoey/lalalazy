using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyFateAutomation;

/// <summary>Drawn controls and content-measured cards for the FATE window.</summary>
internal static class FateChrome
{
    internal const float Control = 36f;
    internal const float Radius = 8f;
    internal const float CardGap = 16f;
    private const float PadX = 16f;
    private static readonly Vector2 ToggleSize = new(44, 24);

    private static readonly Stack<float> Rights = new();
    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();
    private static int _cardDepth;
    private static float _fillBottom = float.NaN;
    private static float _stretch;

    /// <summary>Clears per-frame layout state; the window calls this when it finishes drawing.</summary>
    internal static void ResetFrame()
    {
        Rights.Clear();
        _cardDepth = 0;
        _fillBottom = float.NaN;
        _stretch = 0;
    }

    internal static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

    // ---- measuring and text ------------------------------------------------------------------------------------

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

    internal static float TextWidth(string text, float scale = 1f) => text.Length == 0 ? 0 : Measure(scale, () => ImGui.CalcTextSize(text).X);

    internal static float LineHeight(float scale = 1f) => Measure(scale, () => ImGui.GetTextLineHeight());

    internal static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1, width)).Y);

    internal static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    /// <summary>Draws text elided to a width; the full text appears on hover when it had to be elided.</summary>
    internal static void TextWithTip(ImDrawListPtr draw, string id, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        var fitted = Fit(text, width, scale);
        Text(draw, pos, fitted, color, scale);
        if (fitted == text)
            return;
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##tip_" + id, new Vector2(MathF.Max(1, TextWidth(fitted, scale)), LineHeight(scale)));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    internal static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => Text(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    internal static void Wrapped(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1f)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, MathF.Max(1, width));
    }

    internal static string Fit(string text, float width, float scale = 1f)
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

    /// <summary>Splits an identifier such as "BetweenFates" into "Between fates" for display.</summary>
    internal static string Humanize(string identifier)
    {
        var text = identifier.Replace('_', ' ');
        var builder = new System.Text.StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (i > 0 && char.IsUpper(ch) && text[i - 1] != ' ' && !char.IsUpper(text[i - 1]))
            {
                builder.Append(' ');
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }

    // ---- layout primitives -------------------------------------------------------------------------------------

    internal static void PushRight(float screenX) => Rights.Push(screenX);

    internal static void PopRight()
    {
        if (Rights.Count > 0)
            Rights.Pop();
    }

    /// <summary>Width from the cursor to the innermost right edge (a card's inner edge, a column edge, or the window).</summary>
    internal static float Avail() => Rights.Count > 0
        ? MathF.Max(1, Rights.Peek() - ImGui.GetCursorScreenPos().X)
        : ImGui.GetContentRegionAvail().X;

    internal static void Space(float height)
    {
        if (height > 0)
            ImGui.Dummy(new Vector2(1, height));
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

    private static Vector4 Alpha(Vector4 color, float alpha) => color with { W = alpha };

    // ---- header and footer controls ----------------------------------------------------------------------------

    internal static float BrandWidth(string name) => 48 + TextWidth(name, Theme.TitleScale) + 16;

    internal static bool Brand(ImDrawListPtr draw, Vector2 pos, float width, string name)
    {
        var size = new Vector2(width, 40);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_brand", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, 20), 12, Theme.Pack(Theme.Accent));
        var textX = pos.X + 48;
        Text(draw, new Vector2(textX, pos.Y + ((40 - LineHeight(Theme.TitleScale)) / 2f)),
            Fit(name, size.X - 48 - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        return clicked;
    }

    /// <summary>Outlined button; <paramref name="selected"/> gives the accent treatment used for navigation and the primary action.</summary>
    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false, bool disabled = false)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_" + id, size);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var dim = disabled ? 0.5f : 1f;
        var fill = selected ? Alpha(Theme.Accent, 0.14f * dim) : Alpha(Theme.Surface, dim);
        var border = selected || hovered ? Alpha(Theme.Accent, dim) : Alpha(Theme.SurfaceBorder, dim);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(border), Radius);
        var fitted = Fit(label, size.X - 16);
        var color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        Text(draw, pos + new Vector2((size.X - TextWidth(fitted)) / 2f, (size.Y - LineHeight()) / 2f), fitted, Alpha(color, dim));
        return clicked;
    }

    internal static float ActionWidth(float minWidth, params string[] labels) => MathF.Max(minWidth, labels.Max(label => TextWidth(label)) + 32f);

    /// <summary>Red-tinted stop button, or an amber-tinted pending variant for a stop that waits for a safe moment.</summary>
    internal static bool StopButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool pending)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var tint = pending ? Theme.StateAmber : Theme.StateRed;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Alpha(tint, hovered ? 0.24f : 0.12f)), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Alpha(tint, hovered ? 0.85f : 0.55f)), Radius);
        Text(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, Theme.TextPrimary);
        return clicked;
    }

    internal static bool NavPill(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_nav_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Alpha(Theme.Accent, 0.14f) : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        var fitted = Fit(label, size.X - 16);
        Text(draw, pos + new Vector2((size.X - TextWidth(fitted)) / 2f, (size.Y - LineHeight()) / 2f), fitted, color);
        return clicked;
    }

    internal static bool FeaturePill(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_feature_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Alpha(Theme.Accent, 0.14f) : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Alpha(Theme.SurfaceBorder, 0.6f)), Radius);
        var marker = pos + new Vector2(16, size.Y / 2f);
        draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
        if (selected)
            draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
        var color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        Text(draw, pos + new Vector2(36, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f),
            Fit(label, size.X - 36 - 12, Theme.SecondaryScale), color, Theme.SecondaryScale);
        return clicked;
    }

    internal static float FeaturePillWidth(string label) => 16 + 12 + 8 + TextWidth(label, Theme.SecondaryScale) + 16 + 1;

    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##fate_close", size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Alpha(Theme.Accent, 0.14f) : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), color, 1.5f);
        draw.AddLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), color, 1.5f);
        if (hovered)
            AnchoredTooltip("Close", new Vector2(pos.X + size.X, pos.Y + size.Y + 4), new Vector2(1, 0));
        return clicked;
    }

    internal static void AnchoredTooltip(string text, Vector2 anchor, Vector2 pivot)
    {
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, pivot);
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

    internal static float PillWidth()
    {
        var widest = Enum.GetValues<UiState>().Max(state => TextWidth(Copy.Word(state)));
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
        Text(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), word,
            neutral ? Theme.TextSecondary : mark);
    }

    // ---- card ---------------------------------------------------------------------------------------------------

    internal static void Card(string id, Action content, float minHeight = 0, float width = 0, bool row = false, bool fill = false, float? gap = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = Avail();
        var cardWidth = width <= 0 ? avail : MathF.Min(width, avail);
        var padY = row ? 12f : 16f;
        var topLevel = _cardDepth == 0;
        _cardDepth++;
        if (topLevel)
            draw.ChannelsSplit(3);
        try
        {
            draw.ChannelsSetCurrent(2);
            PushRight(origin.X + cardWidth - PadX);
            ImGui.SetCursorScreenPos(origin + new Vector2(PadX, padY));
            ImGui.PushID(id);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * PadX)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            ImGui.PopID();
            PopRight();
            var contentBottom = ImGui.GetItemRectMax().Y;
            var natural = MathF.Max(minHeight, contentBottom - origin.Y + padY);
            var height = natural;
            if (fill && !float.IsNaN(_fillBottom) && _fillBottom - origin.Y > natural)
            {
                height = _fillBottom - origin.Y;
                _stretch += height - natural;
            }

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(row ? Theme.ShellBar : Theme.Surface), Radius);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), Radius);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
            ImGui.Dummy(new Vector2(cardWidth, gap ?? (topLevel ? CardGap : 8f)));
        }
        finally
        {
            _cardDepth--;
        }
    }

    /// <summary>Main column and right rail with equal bottoms; below the breakpoint the two stack.</summary>
    internal static void Columns(string id, Action main, Action rail, float breakpoint = 820, float? railFraction = null)
    {
        var avail = Avail();
        if (avail < breakpoint)
        {
            main();
            rail();
            return;
        }

        const float gap = 16;
        var origin = ImGui.GetCursorScreenPos();
        var railWidth = railFraction is { } fraction ? (avail - gap) * fraction : Math.Clamp(0.26f * avail, 300, 360);
        var mainWidth = avail - gap - railWidth;
        var previous = ColumnHeights.TryGetValue(id, out var heights) ? MathF.Max(heights.Main, heights.Rail) : float.NaN;
        var saved = _fillBottom;
        _fillBottom = float.IsNaN(previous) ? float.NaN : origin.Y + previous - CardGap;

        _stretch = 0;
        ImGui.SetCursorScreenPos(origin);
        ImGui.BeginGroup();
        PushRight(origin.X + mainWidth);
        main();
        PopRight();
        ImGui.EndGroup();
        var mainBottom = ImGui.GetCursorScreenPos().Y;
        var mainNatural = mainBottom - origin.Y - _stretch;

        _stretch = 0;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + mainWidth + gap, origin.Y));
        ImGui.BeginGroup();
        PushRight(origin.X + avail);
        rail();
        PopRight();
        ImGui.EndGroup();
        var railBottom = ImGui.GetCursorScreenPos().Y;
        var railNatural = railBottom - origin.Y - _stretch;

        _stretch = 0;
        _fillBottom = saved;
        ColumnHeights[id] = (mainNatural, railNatural);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom)));
        ImGui.Dummy(new Vector2(avail, 0));
    }

    // ---- content blocks -----------------------------------------------------------------------------------------

    internal static void Section(string title, string? detail = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var titleHeight = LineHeight(Theme.TitleScale);
        var detailText = detail is null ? "" : Fit(detail, width * 0.5f, Theme.SecondaryScale);
        var detailWidth = TextWidth(detailText, Theme.SecondaryScale);
        Text(draw, pos, Fit(title, width - detailWidth - (detailWidth > 0 ? 12 : 0), Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        if (detailText.Length > 0)
            Text(draw, new Vector2(pos.X + width - detailWidth, pos.Y + ((titleHeight - LineHeight(Theme.SecondaryScale)) / 2f)),
                detailText, Theme.TextSecondary, Theme.SecondaryScale);
        ImGui.Dummy(new Vector2(width, titleHeight));
        Space(8);
    }

    /// <summary>Wrapped paragraph at the cursor; the caller adds the gap that follows.</summary>
    internal static void Paragraph(string text, Vector4? color = null, float scale = Theme.SecondaryScale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        Wrapped(ImGui.GetWindowDrawList(), pos, text, width, color ?? Theme.TextSecondary, scale);
        ImGui.Dummy(new Vector2(width, WrappedHeight(text, width, scale)));
    }

    /// <summary>Single line of text, elided to the available width.</summary>
    internal static void Line(string text, Vector4? color = null, float scale = 1f)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var fitted = Fit(text, width, scale);
        Text(ImGui.GetWindowDrawList(), pos, fitted, color ?? Theme.TextPrimary, scale);
        ImGui.Dummy(new Vector2(width, LineHeight(scale)));
        if (fitted != text && ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    internal static void Divider()
    {
        Space(12);
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        ImGui.Dummy(new Vector2(width, 1));
        Space(12);
    }

    /// <summary>Rounded notice: a dot, a lead phrase and a body that sits inline when it fits and below otherwise.</summary>
    internal static void Notice(string lead, string body, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textX = 34f;
        var textWidth = MathF.Max(1, width - textX - 16);
        var leadHeight = LineHeight();
        var inline = body.Length == 0 || TextWidth(lead) + 8 + TextWidth(body) <= textWidth;
        var height = 12 + 12 + (inline ? leadHeight : leadHeight + 4 + WrappedHeight(body, textWidth, Theme.SecondaryScale));
        var alert = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
        var mark = Copy.MarkColor(state);
        draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(alert ? Alpha(mark, 0.12f) : Theme.Surface), Radius);
        draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(alert ? Alpha(mark, 0.45f) : Theme.SurfaceBorder), Radius);
        draw.AddCircleFilled(pos + new Vector2(PadX, 12 + (leadHeight / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, pos + new Vector2(textX, 12), lead, Theme.TextPrimary);
        if (inline)
        {
            Text(draw, pos + new Vector2(textX + TextWidth(lead) + 8, 12), body, Theme.TextSecondary);
        }
        else
        {
            Wrapped(draw, pos + new Vector2(textX, 12 + leadHeight + 4), body, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        ImGui.Dummy(new Vector2(width, height));
        Space(12);
    }

    // ---- toggles ------------------------------------------------------------------------------------------------

    internal static float ToggleHeight(string label, string? help, float width, bool compact = false)
    {
        var textWidth = MathF.Max(20, width - ToggleSize.X - 16);
        var labelHeight = WrappedHeight(label, textWidth);
        var helpHeight = string.IsNullOrWhiteSpace(help) ? 0 : WrappedHeight(help, textWidth, Theme.SecondaryScale);
        var block = labelHeight + (helpHeight > 0 ? 4 + helpHeight : 0);
        return MathF.Max(ToggleSize.Y, block) + (compact ? 12 : 24);
    }

    internal static bool Toggle(string id, string label, ref bool value, string? help = null, string? tooltip = null, bool separator = false, bool compact = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Avail();
        var textWidth = MathF.Max(20, width - ToggleSize.X - 16);
        var labelHeight = WrappedHeight(label, textWidth);
        var helpHeight = string.IsNullOrWhiteSpace(help) ? 0 : WrappedHeight(help, textWidth, Theme.SecondaryScale);
        var block = labelHeight + (helpHeight > 0 ? 4 + helpHeight : 0);
        var rowHeight = MathF.Max(ToggleSize.Y, block) + (compact ? 12 : 24);
        var draw = ImGui.GetWindowDrawList();

        if (separator)
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var togglePos = pos + new Vector2(width - ToggleSize.X, (rowHeight - ToggleSize.Y) / 2f);
        ImGui.SetCursorScreenPos(togglePos);
        var clicked = ImGui.InvisibleButton("##fate_setting_" + id, ToggleSize);
        if (clicked)
            value = !value;
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(togglePos, togglePos + ToggleSize, Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), ToggleSize.Y / 2f);
        if (hovered)
            draw.AddRect(togglePos, togglePos + ToggleSize, Theme.Pack(Theme.Accent), ToggleSize.Y / 2f);
        draw.AddCircleFilled(togglePos + new Vector2(value ? 32 : 12, 12), 8, Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));

        var textTop = pos.Y + ((rowHeight - block) / 2f);
        Wrapped(draw, new Vector2(pos.X, textTop), label, textWidth, Theme.TextPrimary);
        if (helpHeight > 0)
            Wrapped(draw, new Vector2(pos.X, textTop + labelHeight + 4), help!, textWidth, Theme.TextSecondary, Theme.SecondaryScale);

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, rowHeight)) && ImGui.IsWindowHovered())
            ImGui.SetTooltip(tooltip);

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, rowHeight));
        return clicked;
    }

    // ---- fields and popups -------------------------------------------------------------------------------------

    private sealed class StyleScope(int colors, int vars) : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopStyleColor(colors);
            ImGui.PopStyleVar(vars);
        }
    }

    /// <summary>Bordered dark field styling for combos and text inputs; keep it alive until the widget ends.</summary>
    internal static IDisposable FieldStyle(float height = Control)
    {
        var padY = MathF.Max(2, (height - LineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(16, padY));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        return new StyleScope(5, 6);
    }

    internal static void Chevron(ImDrawListPtr draw, Vector2 center)
    {
        var color = Theme.Pack(Theme.TextSecondary);
        draw.AddLine(center + new Vector2(-4, -2), center + new Vector2(0, 2), color, 1.5f);
        draw.AddLine(center + new Vector2(0, 2), center + new Vector2(4, -2), color, 1.5f);
    }

    /// <summary>Popup window styling (raised surface, light border, padding); keep it alive across BeginPopup and EndPopup.</summary>
    internal static IDisposable PopupStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Alpha(Theme.TextSecondary, 0.4f));
        return new StyleScope(2, 3);
    }

    /// <summary>One row of a popup list: 32 px high, accent tint and outline when selected.</summary>
    internal static bool PopupRow(string id, string label, bool selected, string? trailing = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = MathF.Max(32, LineHeight() + 12);
        var clicked = ImGui.InvisibleButton("##row_" + id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (selected)
        {
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Alpha(Theme.Accent, 0.14f)), Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Alpha(Theme.Accent, 0.55f)), Radius);
        }
        else if (hovered)
        {
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(new Vector4(1, 1, 1, 0.05f)), Radius);
        }
        var trailingWidth = trailing is null ? 0 : TextWidth(trailing, Theme.SecondaryScale) + 12;
        Text(draw, pos + new Vector2(12, (height - LineHeight()) / 2f), Fit(label, width - 24 - trailingWidth), selected ? Theme.Accent : Theme.TextPrimary);
        Space(2);
        return clicked;
    }

    /// <summary>Heading inside a popup window (uses the popup's own content width).</summary>
    internal static void PopupTitle(string text)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = LineHeight(Theme.TitleScale);
        Text(ImGui.GetWindowDrawList(), pos + new Vector2(12, 0), Fit(text, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.Dummy(new Vector2(width, height));
        Space(8);
    }

    /// <summary>Screen Y of the footer's top edge for the current frame; popups never extend below it.</summary>
    internal static float FooterTopY = float.MaxValue;

    /// <summary>Caps the next popup's height so it stays above the footer (the list scrolls when it does not fit).</summary>
    internal static void LimitPopupHeight(float popupTop)
    {
        var max = MathF.Max(120, FooterTopY - popupTop - 4);
        ImGui.SetNextWindowSizeConstraints(new Vector2(0, 0), new Vector2(float.MaxValue, max));
    }

    internal static float PopupWidth(IEnumerable<string> labels, float minWidth)
        => MathF.Max(minWidth, labels.Max(label => TextWidth(label)) + 24 + 16 + 12);

    // ---- stat cards ---------------------------------------------------------------------------------------------

    internal readonly record struct Stat(string Label, string Value, string Note, float? Fraction = null, string? Cap = null);

    internal static float StatCardHeight()
        => 16 + LineHeight(Theme.SecondaryScale) + 8 + LineHeight(1.7f) + 8 + Theme.BarHeight + 8 + LineHeight(Theme.SecondaryScale) + 16;

    internal static void StatCards(params Stat[] stats)
    {
        var available = Avail();
        const float gap = 16;
        var stacked = available < (3 * 220) + (2 * gap);
        var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
        var height = StatCardHeight();
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var small = LineHeight(Theme.SecondaryScale);
        var valueHeight = LineHeight(1.7f);
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
            var y = pos.Y + 16;
            Diamond(draw, new Vector2(pos.X + 21, y + (small / 2f)), 5, Theme.Pack(Theme.Accent));
            Text(draw, new Vector2(pos.X + 32, y), Fit(stat.Label, width - 32 - 16, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
            y += small + 8;
            var value = Fit(stat.Value, width - 32, 1.7f);
            Text(draw, new Vector2(pos.X + 16, y), value, Theme.TextPrimary, 1.7f);
            if (stat.Cap is { Length: > 0 } cap)
            {
                var capHeight = LineHeight(Theme.SecondaryScale);
                var capX = pos.X + 16 + TextWidth(value, 1.7f) + 8;
                Text(draw, new Vector2(capX, y + valueHeight - capHeight - (0.08f * valueHeight)),
                    Fit(cap, pos.X + width - 16 - capX, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + 8;
            if (stat.Fraction is { } fraction)
            {
                var min = new Vector2(pos.X + 16, y);
                var max = new Vector2(pos.X + width - 16, y + Theme.BarHeight);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), 2f);
            }
            y += Theme.BarHeight + 8;
            Text(draw, new Vector2(pos.X + 16, y), Fit(stat.Note, width - 32, Theme.SecondaryScale),
                stat.Fraction >= 1 ? Theme.StateGreen : Theme.TextSecondary, Theme.SecondaryScale);
        }
        var total = stacked ? (stats.Length * height) + ((stats.Length - 1) * gap) : height;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, total));
        Space(gap);
    }
}
