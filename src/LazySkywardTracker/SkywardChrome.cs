using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazySkywardTracker;

/// <summary>
///     Drawn controls for the Skyward tracker shell and its progress rows. Every measurement derives from the live
///     font (CalcTextSize at the draw size, line height) and the 4 px Theme grid, never from constants tuned for one font.
/// </summary>
internal static class SkywardChrome
{
    public const float Radius = 2 * Theme.Unit;
    public const float Gap = 4 * Theme.Unit;
    public const float ControlHeight = 9 * Theme.Unit;
    public const float PageTitleScale = 1.35f;
    public const float StatValueScale = 1.7f;

    public static float Gutter(float windowWidth) => windowWidth >= 760 ? 6 * Theme.Unit : windowWidth >= 560 ? 4 * Theme.Unit : 3 * Theme.Unit;

    // ---- text ----

    // Measurements run at the same font size the text is drawn at (window font scale): the font bakes each size
    // separately, so scaling a base-size measurement under-reports widths and wraps differently from the draw.
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

    internal static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : MathF.Max(LineHeight(scale), Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(8, width)).Y));

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
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length] + ellipsis, scale) > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
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

    internal static string Sentence(string text)
        => text.Length == 0 || char.IsUpper(text[0]) || !char.IsLetter(text[0]) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // ---- shapes and controls ----

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

    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##skyward_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = Fit(label, size.X - (2 * Theme.Unit));
        var textSize = ImGui.CalcTextSize(shown);
        draw.AddText(pos + ((size - textSize) / 2f), Theme.Pack(hovered ? Theme.Accent : Theme.TextPrimary), shown);
        return clicked;
    }

    /// <summary>One width for every state this window shows, so the pill never changes size between states.</summary>
    internal static float PillWidth()
        => MathF.Max(104, 16 + (Theme.DotRadius * 2) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

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
        DrawText(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f)), word, neutral ? Theme.TextSecondary : mark);
    }

    internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##skyward_close", size);
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

    internal static void Progress(ImDrawListPtr draw, Vector2 pos, float width, float fraction, Vector4 color)
    {
        var size = new Vector2(width, Theme.BarHeight);
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Theme.BarHeight / 2f);
        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0)
            draw.AddRectFilled(pos, pos + new Vector2(width * clamped, Theme.BarHeight), Theme.Pack(color), Theme.BarHeight / 2f);
    }

    // ---- notice ----

    internal readonly record struct NoticeLayout(string Lead, string Body, bool Inline, float TextLeft, float TextWidth, float Height);

    private static bool IsAlert(UiState state) => state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;

    /// <summary>Measure a 'Lead — body' notice at the given width. Alerts are tinted, information stays on the surface.</summary>
    internal static NoticeLayout MeasureNotice(float width, string text)
    {
        var split = text.IndexOf(" — ", StringComparison.Ordinal);
        var lead = split > 0 ? text[..split] : text;
        var body = split > 0 ? text[(split + 3)..] : "";
        const float padX = Gap;
        const float padY = 3 * Theme.Unit;
        var textLeft = padX + (Theme.DotRadius * 2) + (3 * Theme.Unit);
        var textWidth = MathF.Max(40, width - textLeft - padX);
        var inline = body.Length > 0 && TextWidth(lead + " —") + (2 * Theme.Unit) + TextWidth(body) <= textWidth;
        if (!inline && body.Length > 0)
            body = Sentence(body);
        var content = inline || body.Length == 0
            ? WrappedHeight(lead, textWidth)
            : WrappedHeight(lead, textWidth) + Theme.Unit + WrappedHeight(body, textWidth, Theme.SecondaryScale);
        return new NoticeLayout(lead, body, inline, textLeft, textWidth, (2 * padY) + content);
    }

    internal static void DrawNotice(ImDrawListPtr draw, Vector2 pos, float width, NoticeLayout notice, UiState state)
    {
        const float padX = Gap;
        const float padY = 3 * Theme.Unit;
        var color = Copy.MarkColor(state);
        var alert = IsAlert(state);
        var max = pos + new Vector2(width, notice.Height);
        draw.AddRectFilled(pos, max, Theme.Pack(alert ? color with { W = 0.12f } : Theme.ShellBar), Radius);
        draw.AddRect(pos, max, Theme.Pack(alert ? color with { W = 0.45f } : Theme.SurfaceBorder), Radius);
        draw.AddCircleFilled(pos + new Vector2(padX + Theme.DotRadius, padY + (LineHeight() / 2f)), Theme.DotRadius, Theme.Pack(color));
        var textPos = pos + new Vector2(notice.TextLeft, padY);
        if (notice.Inline)
        {
            DrawText(draw, textPos, notice.Lead + " —", Theme.TextPrimary);
            DrawText(draw, textPos + new Vector2(TextWidth(notice.Lead + " —") + (2 * Theme.Unit), 0), notice.Body, Theme.TextSecondary);
        }
        else
        {
            var used = DrawWrapped(draw, textPos, notice.Lead, notice.TextWidth, Theme.TextPrimary);
            if (notice.Body.Length > 0)
                DrawWrapped(draw, textPos + new Vector2(0, used + Theme.Unit), notice.Body, notice.TextWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
    }
}
