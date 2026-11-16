// LalaUiKit/PopupChrome.cs - the approved window shell for the shared popups ("What's new", "Report a problem").
// Shared SOURCE compiled into each plugin through the LalaUiKit glob; deps are the ImGui bindings and Theme only.
// Geometry follows the suite spec: gutter, 68 px header band with a bordered brand chip, 36 px drawn close box with an
// anchored tooltip, 44 px page-header tile, 60 px footer band, normal action buttons and cards.

namespace LalaUiKit;

using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

/// <summary>Drawn parts of the suite window shell, used by the shared popups. All methods draw on the current window.</summary>
internal static class PopupChrome
{
    public const float Control = 36f;
    public const float HeaderBandHeight = 68f;
    public const float FooterHeight = 60f;
    public const float Radius = 8f;
    public const float Gap = 8f;

    public static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

    // ---- measuring --------------------------------------------------------------------------------------------

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

    public static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

    public static float LineHeight(float scale = 1f) => Measure(scale, ImGui.GetTextLineHeight);

    public static float WrappedHeight(string text, float width, float scale = 1f)
        => text.Length == 0 ? 0 : Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1, width)).Y);

    public static string FitText(string text, float width, float scale = 1f)
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

    // ---- primitives -------------------------------------------------------------------------------------------

    public static void Text(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1f, float wrapWidth = 0f)
    {
        if (text.Length == 0)
            return;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text, wrapWidth);
    }

    public static void TextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1f)
        => Text(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);

    public static void Diamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
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

    /// <summary>The suite's text tooltip panel: Surface fill, SurfaceBorder, radius 8.</summary>
    public static void Tip(string text, float wrap = 420f)
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(wrap);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    // ---- header band ------------------------------------------------------------------------------------------

    public static float BrandWidth(string name) => 48 + TextWidth(name, Theme.TitleScale) + 16;

    private static void Brand(ImDrawListPtr draw, Vector2 pos, Vector2 size, string shown)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), Radius);
        Diamond(draw, pos + new Vector2(24, size.Y / 2f), 12, Theme.Pack(Theme.Accent));
        Text(draw, pos + new Vector2(48, (size.Y - LineHeight(Theme.TitleScale)) / 2f), shown, Theme.TextPrimary, Theme.TitleScale);
    }

    public static bool CloseButton(ImDrawListPtr draw, string id, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);

        var center = pos + (size / 2f);
        var color = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), color, 1.5f);
        draw.AddLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), color, 1.5f);
        if (hovered)
        {
            // Anchored below the box and right aligned to it, so it never covers the cross or the band's other controls.
            ImGui.SetNextWindowPos(new Vector2(pos.X + size.X, pos.Y + size.Y + 4), ImGuiCond.Always, new Vector2(1f, 0f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Close");
            ImGui.EndTooltip();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
        }

        return clicked;
    }

    /// <summary>
    /// Full-bleed 68 px band with the brand chip (plugin name) and the close box, rule at y = 68. Returns the y where the
    /// next band starts (69). <paramref name="closeClicked"/> reports a press on the close box.
    /// </summary>
    public static float HeaderBand(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, string name, string closeId, out bool closeClicked)
    {
        const float pad = 14;
        const float brandHeight = 40;
        var closeSize = new Vector2(Control);
        var bar = pad + brandHeight + pad;

        draw.PushClipRect(winPos, winPos + new Vector2(width, bar + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(width, bar), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, bar), winPos + new Vector2(width, bar), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        // The chip ellipsises its text when the row is short.
        var room = width - (2 * gutter) - closeSize.X - Gap;
        var chipWidth = MathF.Min(BrandWidth(name), room);
        var shown = FitText(name, chipWidth - 48 - 16, Theme.TitleScale);
        Brand(draw, winPos + new Vector2(gutter, pad), new Vector2(chipWidth, brandHeight), shown);

        var closePos = new Vector2(width - gutter - closeSize.X, pad + ((brandHeight - Control) / 2f));
        closeClicked = CloseButton(draw, closeId, winPos + closePos, closeSize);
        return bar + 1;
    }

    // ---- page header ------------------------------------------------------------------------------------------

    /// <summary>
    /// Tile 44 at the gutter, title 1.35x, description 0.9x, one right-aligned meta string (wide windows) or a third
    /// line under the description (narrow). No divider. Returns the header height (20 + block + 8).
    /// </summary>
    public static float PageHeader(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, float top, string title, string description, string meta)
    {
        const float titleScale = 1.35f;
        const float tile = 44;
        var sm = Theme.SecondaryScale;
        var wide = width >= 760;
        var textX = gutter + 60;
        var left = winPos + new Vector2(textX, top + 20);
        var titleH = LineHeight(titleScale);
        var smH = LineHeight(sm);
        float block;

        draw.AddRectFilled(winPos + new Vector2(gutter, top + 20), winPos + new Vector2(gutter + tile, top + 20 + tile), Theme.Pack(Theme.Accent with { W = 0.14f }), Radius);
        Diamond(draw, winPos + new Vector2(gutter + (tile / 2f), top + 20 + (tile / 2f)), 10, Theme.Pack(Theme.Accent));

        if (wide)
        {
            var metaWidth = MathF.Min(TextWidth(meta, sm), width * 0.4f);
            var textWidth = width - gutter - textX - metaWidth - 16;
            var shownTitle = FitText(title, textWidth, titleScale);
            var shownDescription = FitText(description, textWidth, sm);
            Text(draw, left, shownTitle, Theme.TextPrimary, titleScale);
            Text(draw, left + new Vector2(0, titleH + 4), shownDescription, Theme.TextSecondary, sm);
            block = titleH + 4 + smH;

            var shownMeta = FitText(meta, metaWidth, sm);
            TextRight(draw, winPos.X + width - gutter, left.Y + ((titleH - smH) / 2f), shownMeta, Theme.TextSecondary, sm);
            if ((shownTitle != title || shownDescription != description || shownMeta != meta) &&
                ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(left, left + new Vector2(width - gutter - textX, block)))
                Tip($"{title}\n{description}\n{meta}");
        }
        else
        {
            var textWidth = width - gutter - textX;
            Text(draw, left, FitText(title, textWidth, titleScale), Theme.TextPrimary, titleScale);
            var y = left.Y + titleH + 4;
            var descriptionH = WrappedHeight(description, textWidth, sm);
            Text(draw, new Vector2(left.X, y), description, Theme.TextSecondary, sm, textWidth);
            y += descriptionH;
            if (meta.Length > 0)
            {
                y += 4;
                Text(draw, new Vector2(left.X, y), FitText(meta, textWidth, sm), Theme.TextSecondary, sm);
                y += smH;
            }

            block = y - left.Y;
        }

        return 20 + MathF.Max(tile, block) + 8;
    }

    // ---- footer -----------------------------------------------------------------------------------------------

    /// <summary>Full-bleed footer band with a 1 px top rule. <paramref name="top"/> is the band's top, window relative.</summary>
    public static void FooterBand(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float top)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + winSize, false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top), winPos + new Vector2(winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    public static float ActionWidth(float minWidth, params string[] labels) => MathF.Max(minWidth, labels.Max(l => TextWidth(l)) + 32f);

    /// <summary>The normal action button: Surface fill, SurfaceBorder (Accent on hover), secondary text (primary on hover).</summary>
    public static bool ActionButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
        var shown = FitText(label, size.X - 16);
        Text(
            draw,
            pos + new Vector2((size.X - TextWidth(shown)) / 2f, (size.Y - LineHeight()) / 2f),
            shown,
            hovered ? Theme.TextPrimary : Theme.TextSecondary);
        return clicked;
    }

    // ---- cards ------------------------------------------------------------------------------------------------

    public static void CardBackground(ImDrawListPtr draw, Vector2 pos, Vector2 size, bool hovered = false)
    {
        draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
    }
}
