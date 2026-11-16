using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace GluttonyCombo.Window;

// Fork: drawing-only half of the approved shell. It takes a plain model and reports clicks, so it has no
// plugin or game dependencies and the render harness can draw the very same code.
internal sealed record ShellFeature(string Id, string Label, bool Selected);

internal sealed class ShellFooter
{
    public UiState State;
    public string Primary = "";
    public string Secondary = "";
    public string? StopLabel;
}

internal sealed class ShellModel
{
    public string Brand = "GluttonyCombo";
    public IReadOnlyList<string> Groups = [];
    public string CurrentGroup = "";
    public UiState State;
    public bool ShowSearch;
    public bool ShowDescriptions;
    public string SearchHint = "";
    public IReadOnlyList<ShellFeature> Features = [];
    public bool FeaturesCollapsed;
    public string Title = "";
    public string Description = "";
    public string[] Meta = [];
    public ShellFooter Footer = new();
    /// <summary>Optional shared problem-report button, drawn at the right end of the sub-navigation band when it fits.</summary>
    public Action? ReportButton;
}

internal sealed class ShellResult
{
    public bool Brand;
    public bool Close;
    public bool ExpandFeatures;
    public bool DescriptionsToggled;
    public bool FooterStop;
    public string? Group;
    public int Feature = -1;
}

internal static class GluttonyShellView
{
    private const float BandPad = 14;
    private const float BrandHeight = 40;

    internal static ShellResult Draw(ShellModel model, ref string search, ref bool descriptions,
        Action? bodyPrefix, Action body)
    {
        var result = new ShellResult();
        Theme.PushFrame();
        GluttonyChrome.PushControlColors();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = GluttonyChrome.Gutter(winSize.X);
            ImGui.SetCursorPos(Vector2.Zero);
            var y = DrawTopBar(model, result, winPos, winSize.X, gutter, ref search, ref descriptions);
            y = DrawFeatureBar(model, result, winPos, winSize.X, gutter, y);
            y = DrawPageHeader(model, winPos, winSize.X, gutter, y);
            var footerHeight = FooterHeight(winSize.X, model.Footer.StopLabel is not null);
            ImGui.SetCursorPos(new Vector2(0, y));
            DrawBody(new Vector2(winSize.X, MathF.Max(40, winSize.Y - footerHeight - y)), gutter, bodyPrefix, body);
            DrawFooter(model, result, winPos, winSize, gutter, footerHeight);
        }
        finally
        {
            GluttonyChrome.PopControlColors();
            Theme.PopFrame();
        }
        return result;
    }

    // ---- header band ----

    private static float DrawTopBar(ShellModel m, ShellResult r, Vector2 winPos, float winW, float gutter,
        ref string search, ref bool descriptions)
    {
        var draw = ImGui.GetWindowDrawList();
        const float gap = GluttonyChrome.Gap;
        const float wideGap = 16;
        const float minSearch = 180;
        const float maxSearch = 360;
        var inner = winW - (2 * gutter);
        var brandW = GluttonyChrome.BrandWidth(m.Brand);
        var groupW = m.Groups.Select(g => MathF.Max(88, GluttonyChrome.TextWidth(g) + 32)).ToArray();
        var groupsW = groupW.Sum() + ((groupW.Length - 1) * gap);
        var pillW = GluttonyChrome.PillWidth();
        var rightW = pillW + gap + GluttonyChrome.Control;
        var descW = m.ShowDescriptions ? GluttonyChrome.ActionWidth(96, "Descriptions") : 0;
        var searchExtra = descW > 0 ? descW + gap : 0;

        var navFits = inner >= brandW + wideGap + groupsW + wideGap + rightW;
        var room = navFits ? inner - (brandW + wideGap + groupsW + wideGap) - rightW - wideGap : 0;
        var searchOnRow = m.ShowSearch && navFits && room >= minSearch + searchExtra;

        var controlY = BandPad + ((BrandHeight - GluttonyChrome.Control) / 2f);
        var closePos = new Vector2(winW - gutter - GluttonyChrome.Control, controlY);
        var pillPos = new Vector2(closePos.X - gap - pillW, controlY);
        var groupPos = new Vector2[groupW.Length];
        var groupSize = groupW.Select(w => new Vector2(w, GluttonyChrome.Control)).ToArray();
        float bottom;
        if (navFits)
        {
            var x = gutter + brandW + wideGap;
            for (var i = 0; i < groupPos.Length; i++)
            {
                groupPos[i] = new Vector2(x, controlY);
                x += groupW[i] + gap;
            }
            bottom = BandPad + BrandHeight;
        }
        else
        {
            var rowY = BandPad + BrandHeight + gap;
            var natural = groupsW <= inner;
            var each = (inner - ((groupW.Length - 1) * gap)) / MathF.Max(1, groupW.Length);
            var x = gutter;
            for (var i = 0; i < groupPos.Length; i++)
            {
                if (!natural)
                    groupSize[i] = new Vector2(each, GluttonyChrome.Control);
                groupPos[i] = new Vector2(x, rowY);
                x += groupSize[i].X + gap;
            }
            bottom = rowY + GluttonyChrome.Control;
        }

        Vector2 searchPos = default, descPos = default;
        var searchW = 0f;
        if (m.ShowSearch)
        {
            if (searchOnRow)
            {
                descPos = new Vector2(pillPos.X - wideGap - descW, controlY);
                searchW = MathF.Min(maxSearch, room - searchExtra);
                searchPos = new Vector2((descW > 0 ? descPos.X - gap : pillPos.X - wideGap) - searchW, controlY);
            }
            else
            {
                var rowY = bottom + gap;
                searchW = inner - searchExtra;
                searchPos = new Vector2(gutter, rowY);
                descPos = new Vector2(winW - gutter - descW, rowY);
                bottom = rowY + GluttonyChrome.Control;
            }
        }

        var barH = bottom + BandPad;
        draw.PushClipRect(winPos, winPos + new Vector2(winW, barH + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winW, barH), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barH), winPos + new Vector2(winW, barH), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        if (GluttonyChrome.BrandButton(draw, m.Brand, winPos + new Vector2(gutter, BandPad), new Vector2(brandW, BrandHeight)))
            r.Brand = true;
        for (var i = 0; i < m.Groups.Count; i++)
            if (GluttonyChrome.OutlineButton(draw, "group_" + m.Groups[i], m.Groups[i], winPos + groupPos[i], groupSize[i],
                    m.Groups[i] == m.CurrentGroup) && m.Groups[i] != m.CurrentGroup)
                r.Group = m.Groups[i];

        if (m.ShowSearch)
        {
            ImGui.SetCursorScreenPos(winPos + searchPos);
            ImGui.SetNextItemWidth(searchW);
            var padding = Theme.FramePadding;
            padding.Y = MathF.Max(padding.Y, (GluttonyChrome.Control - ImGui.GetTextLineHeight()) / 2f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, GluttonyChrome.Radius);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.InputTextWithHint("##gc_shell_search", m.SearchHint, ref search, 30, ImGuiInputTextFlags.AutoSelectAll);
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);
            if (descW > 0 && GluttonyChrome.OutlineButton(draw, "search_descriptions", "Descriptions", winPos + descPos,
                    new Vector2(descW, GluttonyChrome.Control), descriptions))
            {
                descriptions = !descriptions;
                r.DescriptionsToggled = true;
            }
        }

        GluttonyChrome.StatusPill(draw, winPos + pillPos, new Vector2(pillW, GluttonyChrome.Control), m.State);
        if (GluttonyChrome.CloseButton(draw, winPos + closePos, new Vector2(GluttonyChrome.Control)))
            r.Close = true;
        return barH + 1;
    }

    // ---- sub-navigation band ----

    private static void DrawBar(ImDrawListPtr draw, Vector2 winPos, float winW, float top, float height)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + new Vector2(winW, top + height + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(winW, top + height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + height), winPos + new Vector2(winW, top + height), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    private static float DrawFeatureBar(ShellModel m, ShellResult r, Vector2 winPos, float winW, float gutter, float top)
    {
        var draw = ImGui.GetWindowDrawList();
        var inner = winW - (2 * gutter);
        if (m.FeaturesCollapsed)
        {
            const float height = 12 + GluttonyChrome.Control + 12;
            DrawBar(draw, winPos, winW, top, height);
            var label = "Show destinations";
            var size = new Vector2(MathF.Min(inner, GluttonyChrome.ActionWidth(96, label)), GluttonyChrome.Control);
            if (GluttonyChrome.ActionButton(draw, "expand_features", label, winPos + new Vector2(gutter, top + 12), size))
                r.ExpandFeatures = true;
            return top + height + 1;
        }

        var features = m.Features;
        if (features.Count < 2)
            return top;
        const float gap = 12;
        var sizes = features.Select(f => new Vector2(GluttonyChrome.FeatureWidth(f.Label), 32)).ToArray();
        var total = sizes.Sum(s => s.X) + ((features.Count - 1) * gap);
        if (total > inner)
        {
            const float height = 12 + GluttonyChrome.Control + 12;
            DrawBar(draw, winPos, winW, top, height);
            var fieldPos = winPos + new Vector2(gutter, top + 12);
            var fieldSize = new Vector2(inner, GluttonyChrome.Control);
            ImGui.SetCursorScreenPos(fieldPos);
            var open = ImGui.InvisibleButton("##gc_feature_menu", fieldSize);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(fieldPos, fieldPos + fieldSize, Theme.Pack(Theme.Accent with { W = 0.14f }), GluttonyChrome.Radius);
            draw.AddRect(fieldPos, fieldPos + fieldSize, Theme.Pack(hovered ? Theme.Accent : Theme.Accent with { W = 0.6f }), GluttonyChrome.Radius);
            var current = features.FirstOrDefault(f => f.Selected)?.Label ?? features[0].Label;
            GluttonyChrome.DrawText(draw, fieldPos + new Vector2(16, (fieldSize.Y - GluttonyChrome.LineHeight()) / 2f),
                GluttonyChrome.FitText(current, inner - 56), Theme.Accent);
            GluttonyChrome.Chevron(draw, fieldPos + new Vector2(inner - 20, fieldSize.Y / 2f), Theme.Accent);
            if (open)
                ImGui.OpenPopup("##gc_feature_popup");
            ImGui.SetNextWindowPos(fieldPos + new Vector2(0, fieldSize.Y + 4));
            ImGui.SetNextWindowSize(new Vector2(inner, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, GluttonyChrome.Radius);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
            if (ImGui.BeginPopup("##gc_feature_popup"))
            {
                for (var i = 0; i < features.Count; i++)
                    if (ImGui.Selectable(features[i].Label + "##gc_feature_" + features[i].Id, features[i].Selected,
                            ImGuiSelectableFlags.None, new Vector2(0, GluttonyChrome.Control - 8)))
                        r.Feature = i;
                ImGui.EndPopup();
            }
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(4);
            return top + height + 1;
        }

        const float bandHeight = 12 + 32 + 12;
        DrawBar(draw, winPos, winW, top, bandHeight);
        var px = gutter;
        for (var i = 0; i < features.Count; i++)
        {
            if (GluttonyChrome.FeatureButton(draw, features[i].Id, features[i].Label, winPos + new Vector2(px, top + 12),
                    sizes[i], features[i].Selected))
                r.Feature = i;
            px += sizes[i].X + gap;
        }
        if (m.ReportButton is { } report)
        {
            const string label = "Report a problem";
            var width = GluttonyChrome.TextWidth(label) + (2 * Theme.FramePadding.X);
            if (px - gap + 16 + width <= winW - gutter)
            {
                ImGui.SetCursorScreenPos(winPos + new Vector2(winW - gutter - width, top + 12 + ((32 - (ImGui.GetTextLineHeight() + (2 * Theme.FramePadding.Y))) / 2f)));
                report();
            }
        }
        return top + bandHeight + 1;
    }

    // ---- page header ----

    private static List<string> WrapLines(string text, float width, float scale, int maxLines)
    {
        var lines = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = "";
        for (var i = 0; i < words.Length; i++)
        {
            var next = line.Length == 0 ? words[i] : line + " " + words[i];
            if (line.Length == 0 || GluttonyChrome.TextWidth(next, scale) <= width)
            {
                line = next;
                continue;
            }
            if (lines.Count == maxLines - 1)
            {
                line = string.Join(' ', words.Skip(i - (line.Split(' ').Length - 1)));
                break;
            }
            lines.Add(line);
            line = words[i];
        }
        if (line.Length > 0)
            lines.Add(GluttonyChrome.FitText(line, width, scale));
        return lines;
    }

    private static float DrawPageHeader(ShellModel m, Vector2 winPos, float winW, float gutter, float top)
    {
        var draw = ImGui.GetWindowDrawList();
        const float tile = 44;
        const float titleScale = 1.35f;
        var wide = winW >= 760;
        var textX = gutter + 60;
        var origin = winPos + new Vector2(gutter, top + 20);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), 8f);
        GluttonyChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var titleLine = GluttonyChrome.LineHeight(titleScale);
        var smallLine = GluttonyChrome.LineHeight(Theme.SecondaryScale);
        var metaWidth = wide && m.Meta.Length > 0
            ? MathF.Min(winW * 0.4f, m.Meta.Max(s => GluttonyChrome.TextWidth(s, Theme.SecondaryScale)))
            : 0;
        var textWidth = wide ? winW - gutter - textX - metaWidth - 16 : winW - gutter - textX;
        var textPos = new Vector2(winPos.X + textX, origin.Y);

        var titleLines = wide ? [GluttonyChrome.FitText(m.Title, textWidth, titleScale)] : WrapLines(m.Title, textWidth, titleScale, 2);
        var y = origin.Y;
        foreach (var line in titleLines)
        {
            GluttonyChrome.DrawText(draw, new Vector2(textPos.X, y), line, Theme.TextPrimary, titleScale);
            y += titleLine;
        }
        var titleBottom = y;
        y += 4;
        var descTop = y;
        var descLines = WrapLines(m.Description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
        foreach (var line in descLines)
        {
            GluttonyChrome.DrawText(draw, new Vector2(textPos.X, y), line, Theme.TextSecondary, Theme.SecondaryScale);
            y += smallLine;
        }
        var blockBottom = y;
        if (wide)
        {
            var right = winPos.X + winW - gutter;
            for (var i = 0; i < m.Meta.Length && i < 2; i++)
            {
                var text = GluttonyChrome.FitText(m.Meta[i], metaWidth, Theme.SecondaryScale);
                var my = i == 0 ? origin.Y + ((titleLine - smallLine) / 2f) : descTop;
                GluttonyChrome.DrawText(draw, new Vector2(right - GluttonyChrome.TextWidth(text, Theme.SecondaryScale), my), text,
                    Theme.TextSecondary, Theme.SecondaryScale);
            }
        }
        else
        {
            y += 4;
            foreach (var meta in m.Meta)
            {
                GluttonyChrome.DrawText(draw, new Vector2(textPos.X, y), GluttonyChrome.FitText(meta, textWidth, Theme.SecondaryScale),
                    Theme.TextSecondary, Theme.SecondaryScale);
                y += smallLine + 2;
            }
            blockBottom = y;
        }
        if ((titleLines.Count > 0 && titleLines[^1] != m.Title && wide || descLines.Count > 0 && wide && descLines[0] != m.Description)
            && ImGui.IsMouseHoveringRect(textPos, new Vector2(textPos.X + textWidth, blockBottom)))
            ImGui.SetTooltip(m.Title + "\n" + m.Description);
        _ = titleBottom;
        return top + 20 + MathF.Max(tile, blockBottom - origin.Y) + 8;
    }

    // ---- body ----

    private static void DrawBody(Vector2 size, float gutter, Action? prefix, Action body)
    {
        GluttonyChrome.BeginBody(size, gutter, "##gc_shell_body");
        prefix?.Invoke();
        body();
        GluttonyChrome.EndBody();
    }

    // ---- footer ----

    internal static float FooterHeight(float width, bool hasAction)
    {
        var block = GluttonyChrome.LineHeight() + 4 + GluttonyChrome.LineHeight(Theme.SecondaryScale);
        return width < 620 && hasAction
            ? 12 + block + 12 + GluttonyChrome.Control + 12
            : 12 + MathF.Max(block, GluttonyChrome.Control) + 12;
    }

    private static void DrawFooter(ShellModel m, ShellResult r, Vector2 winPos, Vector2 winSize, float gutter, float height)
    {
        var f = m.Footer;
        var top = winPos.Y + winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var narrow = winSize.X < 620;
        var hasAction = f.StopLabel is not null;
        var block = GluttonyChrome.LineHeight() + 4 + GluttonyChrome.LineHeight(Theme.SecondaryScale);
        var actionSize = new Vector2(
            hasAction ? (narrow ? winSize.X - (2 * gutter) : GluttonyChrome.ActionWidth(72, f.StopLabel!)) : 0,
            GluttonyChrome.Control);
        var textLeft = winPos.X + gutter + 16;
        Vector2 actionPos;
        float textTop, textRight;
        if (narrow)
        {
            textTop = top + 12;
            actionPos = new Vector2(winPos.X + gutter, textTop + block + 12);
            textRight = winPos.X + winSize.X - gutter;
        }
        else
        {
            textTop = top + ((height - block) / 2f);
            actionPos = new Vector2(winPos.X + winSize.X - gutter - actionSize.X, top + ((height - actionSize.Y) / 2f));
            textRight = hasAction ? actionPos.X - 16 : winPos.X + winSize.X - gutter;
        }
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + 4, textTop + (GluttonyChrome.LineHeight() / 2f)),
            Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(f.State)));
        var width = textRight - textLeft;
        var shown = GluttonyChrome.FitText(f.Primary, width);
        var shownSecondary = GluttonyChrome.FitText(f.Secondary, width, Theme.SecondaryScale);
        GluttonyChrome.DrawText(draw, new Vector2(textLeft, textTop), shown, Theme.TextPrimary);
        GluttonyChrome.DrawText(draw, new Vector2(textLeft, textTop + GluttonyChrome.LineHeight() + 4), shownSecondary,
            Theme.TextSecondary, Theme.SecondaryScale);
        if ((shown != f.Primary || shownSecondary != f.Secondary)
            && ImGui.IsMouseHoveringRect(new Vector2(textLeft, textTop), new Vector2(textRight, textTop + block)))
            ImGui.SetTooltip(f.Primary + "\n" + f.Secondary);
        if (hasAction && GluttonyChrome.StopButton(draw, actionPos, actionSize, f.StopLabel!))
            r.FooterStop = true;
    }
}
