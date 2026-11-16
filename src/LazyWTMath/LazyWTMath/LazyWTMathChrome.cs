using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace WondrousTailsSolver;

/// <summary>Plugin-local drawn controls and text measuring for the refreshed probability window.</summary>
internal static class LazyWTMathChrome
{
    internal const float Radius = 8f;

    /// <summary>Side gutter shared by the top bar, page body and footer.</summary>
    internal static float Gutter(float width) => width >= 760 ? 24 : width >= 560 ? 16 : 12;

    // ---- measuring: always at the draw size (the font bakes each size separately) ----

    // Text metrics repeat every frame for the same strings, so they are cached by (text, width, scale, font size).
    private const int CacheLimit = 2048;
    private static readonly Dictionary<(string, float, float), float> WidthCache = new();
    private static readonly Dictionary<(float, float), float> LineCache = new();
    private static readonly Dictionary<(string, float, float, float), List<string>> WrapCache = new();

    private static float WidthAt(string text, float scale)
    {
        if (scale == 1f)
            return ImGui.CalcTextSize(text).X;
        ImGui.SetWindowFontScale(scale);
        var width = ImGui.CalcTextSize(text).X;
        ImGui.SetWindowFontScale(1f);
        return width;
    }

    internal static float TextWidth(string text, float scale = 1f)
    {
        var key = (text, scale, ImGui.GetFontSize());
        if (WidthCache.TryGetValue(key, out var cached))
            return cached;
        if (WidthCache.Count >= CacheLimit)
            WidthCache.Clear();
        return WidthCache[key] = WidthAt(text, scale);
    }

    internal static float LineH(float scale = 1f)
    {
        var key = (scale, ImGui.GetFontSize());
        if (LineCache.TryGetValue(key, out var cached))
            return cached;
        if (scale == 1f)
            return LineCache[key] = ImGui.GetTextLineHeight();
        ImGui.SetWindowFontScale(scale);
        var height = ImGui.GetTextLineHeight();
        ImGui.SetWindowFontScale(1f);
        return LineCache[key] = height;
    }

    internal static string Fit(string text, float width, float scale = 1f)
    {
        if (width <= 0)
            return "";
        if (TextWidth(text, scale) <= width)
            return text;
        return Ellipsize(text, width, scale);
    }

    /// <summary>Longest prefix (binary search) that fits with a trailing ellipsis.</summary>
    private static string Ellipsize(string text, float width, float scale)
    {
        const string ellipsis = "…";
        int lo = 0, hi = text.Length;
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

    /// <summary>Greedy word wrap measured at the draw size. The returned list is cached: do not modify it.</summary>
    internal static List<string> WrapLines(string text, float width, float scale = 1f)
    {
        var key = (text, width, scale, ImGui.GetFontSize());
        if (WrapCache.TryGetValue(key, out var cached))
            return cached;
        if (WrapCache.Count >= CacheLimit)
            WrapCache.Clear();

        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || TextWidth(candidate, scale) <= width)
                current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0 || lines.Count == 0)
            lines.Add(current);
        return WrapCache[key] = lines;
    }

    /// <summary>Wraps to at most <paramref name="maxLines"/> lines; the last one ends in an ellipsis when text was cut.</summary>
    internal static List<string> ClampLines(string text, float width, float scale, int maxLines, out bool elided)
    {
        var lines = WrapLines(text, width, scale);
        elided = false;
        if (lines.Count <= maxLines)
            return lines;
        elided = true;
        var kept = lines.Take(maxLines - 1).ToList();
        kept.Add(Ellipsize(string.Join(" ", lines.Skip(maxLines - 1)), width, scale));
        return kept;
    }

    internal static float ParagraphHeight(string text, float width, float scale = 1f)
        => WrapLines(text, width, scale).Count * LineH(scale);

    // ---- drawing ----

    internal static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    internal static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        Text(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);
    }

    /// <summary>Draws pre-wrapped lines one line height apart; returns the block height.</summary>
    internal static float Lines(ImDrawListPtr draw, Vector2 pos, IReadOnlyList<string> lines, Vector4 color, float scale = 1)
    {
        var lineHeight = LineH(scale);
        for (var i = 0; i < lines.Count; i++)
            Text(draw, pos + new Vector2(0, i * lineHeight), lines[i], color, scale);
        return lines.Count * lineHeight;
    }

    /// <summary>Wrapped paragraph at the given width; returns its height.</summary>
    internal static float Paragraph(ImDrawListPtr draw, Vector2 pos, string text, float width, Vector4 color, float scale = 1)
        => Lines(draw, pos, WrapLines(text, width, scale), color, scale);

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

    internal static void Hairline(ImDrawListPtr draw, float x0, float x1, float y)
        => draw.AddLine(new Vector2(x0, y), new Vector2(x1, y), Theme.Pack(Theme.SurfaceBorder), 1f);

    internal static void Panel(ImDrawListPtr draw, Vector2 pos, Vector2 size, Vector4 fill, Vector4 border)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(border), Radius);
    }

    internal static float ActionWidth(float minWidth, params string[] labels) => MathF.Max(minWidth, labels.Max(l => TextWidth(l)) + 32f);

    /// <summary>Neutral action control with a centred label; the outline and label brighten on hover.</summary>
    internal static bool ActionButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##wtm_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var textSize = new Vector2(TextWidth(label), LineH());
        Text(draw, pos + ((size - textSize) / 2f), label, hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    /// <summary>Square close control drawn as a cross, so it never depends on a font glyph.</summary>
    internal static bool CloseButton(ImDrawListPtr draw, string id, Vector2 pos, float size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##wtm_" + id, new Vector2(size));
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + new Vector2(size), Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + new Vector2(size), Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var c = pos + new Vector2(size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), color, 1.5f);
        draw.AddLine(c + new Vector2(-6, 6), c + new Vector2(6, -6), color, 1.5f);
        return clicked;
    }

    /// <summary>Bordered, opaque tooltip anchored under a control and right-aligned to it.</summary>
    internal static void TooltipBelow(string text, Vector2 anchorRight)
    {
        ImGui.SetNextWindowPos(anchorRight, ImGuiCond.Always, new Vector2(1, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    /// <summary>Status pill of a fixed width: dot and word centred, always neutral fill.</summary>
    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var label = Copy.Word(state);
        var color = Copy.MarkColor(state);
        var neutral = state is UiState.Idle or UiState.Empty or UiState.NeedsSetup;
        var radius = size.Y / 2f;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), radius);
        var textWidth = TextWidth(label);
        var groupWidth = (2 * Theme.DotRadius) + 8 + textWidth;
        var x = pos.X + ((size.X - groupWidth) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        Text(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineH()) / 2f)), label, neutral ? Theme.TextSecondary : color);
    }

    /// <summary>
    /// Rounded notice: alert states tinted, others on the surface. 'Lead' reads inline with its body when it fits,
    /// otherwise the body wraps beneath it.
    /// </summary>
    internal static float NoticeHeight(string lead, string body, float width)
    {
        var inner = width - 34 - 16;
        var inline = TextWidth(lead) + 8 + TextWidth(body, Theme.SecondaryScale) <= inner;
        return 24 + LineH() + (inline ? 0 : 4 + ParagraphHeight(body, inner, Theme.SecondaryScale));
    }

    internal static void Notice(ImDrawListPtr draw, Vector2 pos, float width, string lead, string body, UiState state)
    {
        var height = NoticeHeight(lead, body, width);
        var alert = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
        var mark = Copy.MarkColor(state);
        Panel(draw, pos, new Vector2(width, height),
            alert ? mark with { W = 0.12f } : Theme.Surface,
            alert ? mark with { W = 0.45f } : Theme.SurfaceBorder);

        var inner = width - 34 - 16;
        var lineH = LineH();
        var textX = pos.X + 34;
        draw.AddCircleFilled(new Vector2(pos.X + 16, pos.Y + 12 + (lineH / 2f)), Theme.DotRadius, Theme.Pack(mark));
        Text(draw, new Vector2(textX, pos.Y + 12), lead, Theme.TextPrimary);
        var leadWidth = TextWidth(lead);
        if (leadWidth + 8 + TextWidth(body, Theme.SecondaryScale) <= inner)
            Text(draw, new Vector2(textX + leadWidth + 8, pos.Y + 12 + ((lineH - LineH(Theme.SecondaryScale)) / 2f)), body, Theme.TextSecondary, Theme.SecondaryScale);
        else
            Paragraph(draw, new Vector2(textX, pos.Y + 12 + lineH + 4), body, inner, Theme.TextSecondary, Theme.SecondaryScale);
    }
}
