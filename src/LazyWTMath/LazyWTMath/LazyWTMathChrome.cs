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
    internal static float Gutter(float width) => width >= 900 ? 24 : width >= 560 ? 16 : 12;

    // ---- measuring: always at the draw size (the font bakes each size separately) ----

    internal static T Measure<T>(float scale, Func<T> measure)
    {
        if (scale == 1f)
            return measure();
        ImGui.SetWindowFontScale(scale);
        try { return measure(); }
        finally { ImGui.SetWindowFontScale(1f); }
    }

    internal static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    internal static float LineH(float scale = 1f) => Measure(scale, ImGui.GetTextLineHeight);

    internal static string Fit(string text, float width, float scale = 1f)
    {
        if (width <= 0)
            return "";
        if (TextWidth(text, scale) <= width)
            return text;
        return Ellipsize(text, width, scale);
    }

    private static string Ellipsize(string text, float width, float scale)
    {
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && TextWidth(text[..length].TrimEnd() + ellipsis, scale) > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }

    /// <summary>Greedy word wrap measured at the draw size.</summary>
    internal static List<string> WrapLines(string text, float width, float scale = 1f)
    {
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
        return lines;
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

    /// <summary>Outlined control with a centred label. Secondary controls stay quiet until hovered.</summary>
    internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##wtm_" + id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Surface : Theme.ShellBar), Radius);
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
        draw.AddRectFilled(pos, pos + new Vector2(size), Theme.Pack(hovered ? Theme.Surface : Theme.ShellBar), Radius);
        draw.AddRect(pos, pos + new Vector2(size), Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var c = pos + new Vector2(size / 2f);
        var color = Theme.Pack(hovered ? Theme.TextPrimary : Theme.TextSecondary);
        draw.AddLine(c + new Vector2(-5, -5), c + new Vector2(5, 5), color, 1.5f);
        draw.AddLine(c + new Vector2(-5, 5), c + new Vector2(5, -5), color, 1.5f);
        return clicked;
    }

    /// <summary>Status pill of a fixed width: dot and word centred, alert states tinted.</summary>
    internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, UiState state)
    {
        var label = Copy.Word(state);
        var color = Copy.MarkColor(state);
        var tinted = state is UiState.Ready or UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
        var radius = size.Y / 2f;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(tinted ? color with { W = 0.12f } : Theme.Surface), radius);
        draw.AddRect(pos, pos + size, Theme.Pack(tinted ? color with { W = 0.45f } : Theme.SurfaceBorder), radius);
        var textWidth = TextWidth(label);
        var groupWidth = (2 * Theme.DotRadius) + 8 + textWidth;
        var x = pos.X + ((size.X - groupWidth) / 2f);
        draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, Theme.Pack(color));
        Text(draw, new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineH()) / 2f)), label, tinted ? color : Theme.TextSecondary);
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
