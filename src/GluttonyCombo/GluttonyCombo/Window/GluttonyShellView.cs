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
    /// <summary>Tooltip of the brand chip (the plugin name with its version).</summary>
    public string? BrandHelp;
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
    /// <summary>Optional shared problem-report button, drawn in the footer so it stays reachable on every page and layout.</summary>
    public Action? ReportButton;
    public string ReportLabel = "Report a problem";
    /// <summary>Tooltip text for the Descriptions switch.</summary>
    public string DescriptionsHelp = "Also match preset descriptions when searching";
    /// <summary>Tooltip of the collapsed destinations control.</summary>
    public string ExpandHelp = "Show all destinations";
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
    private const float MinSearch = 160;
    private const float MaxSearch = 240;
    private const float WideGap = 16;

    /// <summary>Width of the search field drawn last (read by the render harness to check every page agrees).</summary>
    internal static float LastSearchWidth { get; private set; }

    internal static ShellResult Draw(ShellModel model, ref string search, ref bool descriptions,
        Action? bodyPrefix, Action body, bool bodyCard = false)
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
            var y = DrawTopBar(model, result, winPos, winSize.X, gutter, ref search, ref descriptions, out var searchInBand, out var tallBand);
            y = DrawFeatureBar(model, result, winPos, winSize.X, gutter, y, tallBand, searchInBand, ref search, ref descriptions);
            y = DrawPageHeader(model, winPos, winSize.X, gutter, y);
            var footerHeight = FooterHeight(winSize.X, model.Footer.StopLabel is not null || model.ReportButton is not null);
            ImGui.SetCursorPos(new Vector2(0, y));
            DrawBody(new Vector2(winSize.X, MathF.Max(40, winSize.Y - footerHeight - y)), gutter, bodyPrefix, body, bodyCard);
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
        ref string search, ref bool descriptions, out bool searchInBand, out bool tallBand)
    {
        var draw = ImGui.GetWindowDrawList();
        const float gap = GluttonyChrome.Gap;
        var inner = winW - (2 * gutter);
        var brandW = GluttonyChrome.BrandWidth(m.Brand);
        var groupW = m.Groups.Select(g => MathF.Max(88, GluttonyChrome.TextWidth(g) + 32)).ToArray();
        var groupsW = groupW.Sum() + ((groupW.Length - 1) * gap);
        var pillW = GluttonyChrome.PillWidth();
        var rightW = pillW + gap + GluttonyChrome.Control;
        var chipW = GluttonyChrome.ChipWidth("Descriptions");

        var navFits = inner >= brandW + WideGap + groupsW + WideGap + rightW;
        var room = navFits ? inner - (brandW + WideGap + groupsW + WideGap) - rightW - WideGap : 0;
        // The decision uses the widest case (search plus the Descriptions switch), so it depends on the window width only:
        // every page of a window gets the same header and the same search width.
        var searchOnBar = navFits && room >= MinSearch + gap + chipW;
        searchInBand = m.ShowSearch && !searchOnBar;
        tallBand = !searchOnBar;

        var controlY = BandPad + ((BrandHeight - GluttonyChrome.Control) / 2f);
        var closePos = new Vector2(winW - gutter - GluttonyChrome.Control, controlY);
        var pillPos = new Vector2(closePos.X - gap - pillW, controlY);
        var groupPos = new Vector2[groupW.Length];
        var groupSize = groupW.Select(w => new Vector2(w, GluttonyChrome.Control)).ToArray();
        float bottom;
        if (navFits)
        {
            var x = gutter + brandW + WideGap;
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

        var barH = bottom + BandPad;
        draw.PushClipRect(winPos, winPos + new Vector2(winW, barH + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winW, barH), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barH), winPos + new Vector2(winW, barH), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        if (GluttonyChrome.BrandButton(draw, m.Brand, winPos + new Vector2(gutter, BandPad), new Vector2(brandW, BrandHeight), m.BrandHelp))
            r.Brand = true;
        for (var i = 0; i < m.Groups.Count; i++)
            if (GluttonyChrome.OutlineButton(draw, "group_" + m.Groups[i], m.Groups[i], winPos + groupPos[i], groupSize[i],
                    m.Groups[i] == m.CurrentGroup) && m.Groups[i] != m.CurrentGroup)
                r.Group = m.Groups[i];

        if (m.ShowSearch && searchOnBar)
        {
            var searchX = gutter + brandW + WideGap + groupsW + WideGap;
            var searchW = MathF.Min(MaxSearch, room - gap - chipW);
            DrawSearch(winPos + new Vector2(searchX, controlY), searchW, m.SearchHint, ref search);
            if (m.ShowDescriptions && GluttonyChrome.ToggleChip(draw, "search_descriptions", "Descriptions",
                    winPos + new Vector2(searchX + searchW + gap, controlY), new Vector2(chipW, GluttonyChrome.Control), descriptions,
                    m.DescriptionsHelp))
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

    /// <summary>The search field: dim hint, 36 high, with a clear button once there is text.</summary>
    private static void DrawSearch(Vector2 pos, float width, string hint, ref string search)
    {
        var draw = ImGui.GetWindowDrawList();
        LastSearchWidth = width;
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        var padding = Theme.FramePadding;
        padding.X = 16;
        padding.Y = MathF.Max(padding.Y, (GluttonyChrome.Control - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, GluttonyChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        // The hint is drawn in the disabled text colour; keep it clearly dimmer than typed text.
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Theme.TextSecondary with { W = 0.55f });
        ImGui.InputTextWithHint("##gc_shell_search", hint, ref search, 30, ImGuiInputTextFlags.AutoSelectAll);
        ImGui.SetItemAllowOverlap();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
        if (search.Length == 0)
            return;
        var box = new Vector2(24, 24);
        var boxPos = pos + new Vector2(width - 28, (GluttonyChrome.Control - box.Y) / 2f);
        ImGui.SetCursorScreenPos(boxPos);
        var clear = ImGui.InvisibleButton("##gc_search_clear", box);
        var hovered = ImGui.IsItemHovered();
        var c = boxPos + (box / 2f);
        var col = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
        draw.AddLine(c + new Vector2(-4, -4), c + new Vector2(4, 4), col, 1.5f);
        draw.AddLine(c + new Vector2(-4, 4), c + new Vector2(4, -4), col, 1.5f);
        if (hovered)
            ImGui.SetTooltip("Clear the search");
        if (clear)
            search = "";
    }

    // ---- sub-navigation band ----

    private static void DrawBar(ImDrawListPtr draw, Vector2 winPos, float winW, float top, float height)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + new Vector2(winW, top + height + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(winW, top + height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + height), winPos + new Vector2(winW, top + height), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    private static float DrawFeatureBar(ShellModel m, ShellResult r, Vector2 winPos, float winW, float gutter, float top,
        bool tallBand, bool searchInBand, ref string search, ref bool descriptions)
    {
        var draw = ImGui.GetWindowDrawList();
        var inner = winW - (2 * gutter);
        var features = m.Features;
        var hasLeft = m.FeaturesCollapsed || features.Count >= 2;
        if (!hasLeft && !searchInBand)
            return top;

        // One band height for every state of a window: collapsed, pills, field, with or without search.
        var rowH = tallBand ? GluttonyChrome.Control : 32f;
        var height = 12 + rowH + 12;
        DrawBar(draw, winPos, winW, top, height);
        var rowTop = top + 12;
        var leftTop = rowTop + ((rowH - 32f) / 2f);
        const float gap = 8;

        var chipW = searchInBand && m.ShowDescriptions ? GluttonyChrome.ChipWidth("Descriptions") : 0;
        var searchW = searchInBand ? MathF.Min(MaxSearch, MathF.Max(MinSearch, inner * 0.34f)) : 0;
        var rightW = searchInBand ? searchW + (chipW > 0 ? gap + chipW : 0) : 0;
        var leftRoom = searchInBand ? inner - rightW - WideGap : inner;

        if (searchInBand)
        {
            var searchPos = winPos + new Vector2(winW - gutter - rightW, rowTop);
            DrawSearch(searchPos, searchW, m.SearchHint, ref search);
            if (chipW > 0 && GluttonyChrome.ToggleChip(draw, "search_descriptions", "Descriptions",
                    winPos + new Vector2(winW - gutter - chipW, rowTop), new Vector2(chipW, GluttonyChrome.Control), descriptions,
                    m.DescriptionsHelp))
            {
                descriptions = !descriptions;
                r.DescriptionsToggled = true;
            }
        }

        if (!hasLeft)
            return top + height + 1;

        var current = features.FirstOrDefault(f => f.Selected)?.Label ?? (features.Count > 0 ? features[0].Label : "");
        if (m.FeaturesCollapsed)
        {
            // Shows the current destination, so the collapsed state says where the page is; one click brings the list back.
            var label = "Destination: " + current;
            var buttonW = MathF.Min(leftRoom, MathF.Max(160, GluttonyChrome.TextWidth(label) + 16 + 32));
            var buttonPos = winPos + new Vector2(gutter, leftTop);
            if (DestinationField(draw, "expand_features", buttonPos, new Vector2(buttonW, 32), label, false))
                r.ExpandFeatures = true;
            if (ImGui.IsMouseHoveringRect(buttonPos, buttonPos + new Vector2(buttonW, 32)))
                ImGui.SetTooltip(m.ExpandHelp);
            return top + height + 1;
        }

        const float pillGap = 12;
        var sizes = features.Select(f => new Vector2(GluttonyChrome.FeatureWidth(f.Label), 32)).ToArray();
        var total = sizes.Sum(s => s.X) + ((features.Count - 1) * pillGap);
        if (total > leftRoom)
        {
            var fieldW = searchInBand ? MathF.Min(leftRoom, 280) : inner;
            var fieldPos = winPos + new Vector2(gutter, leftTop);
            var fieldSize = new Vector2(fieldW, 32);
            var open = DestinationField(draw, "feature_menu", fieldPos, fieldSize, "Destination: " + current, true);
            if (open)
                ImGui.OpenPopup("##gc_feature_popup");
            ImGui.SetNextWindowPos(fieldPos + new Vector2(0, fieldSize.Y + 4));
            ImGui.SetNextWindowSize(new Vector2(MathF.Max(fieldW, 220), 0));
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

        var px = gutter;
        for (var i = 0; i < features.Count; i++)
        {
            if (GluttonyChrome.FeatureButton(draw, features[i].Id, features[i].Label, winPos + new Vector2(px, leftTop),
                    sizes[i], features[i].Selected))
                r.Feature = i;
            px += sizes[i].X + pillGap;
        }
        return top + height + 1;
    }

    /// <summary>A neutral or accent field with a chevron: the destination list collapsed into one control.</summary>
    private static bool DestinationField(ImDrawListPtr draw, string id, Vector2 pos, Vector2 size, string label, bool accent)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##gc_" + id, size);
        var hovered = ImGui.IsItemHovered();
        var fill = accent ? Theme.Accent with { W = 0.14f } : Theme.Surface;
        draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), GluttonyChrome.Radius);
        draw.AddRect(pos, pos + size, Theme.Pack(hovered || accent ? (accent && !hovered ? Theme.Accent with { W = 0.6f } : Theme.Accent) : Theme.SurfaceBorder),
            GluttonyChrome.Radius);
        var text = accent ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
        GluttonyChrome.DrawText(draw, pos + new Vector2(16, (size.Y - GluttonyChrome.LineHeight(Theme.SecondaryScale)) / 2f),
            GluttonyChrome.FitText(label, size.X - 56, Theme.SecondaryScale), text, Theme.SecondaryScale);
        GluttonyChrome.Chevron(draw, pos + new Vector2(size.X - 20, size.Y / 2f), text);
        return clicked;
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

    internal static float DrawPageHeader(ShellModel m, Vector2 winPos, float winW, float gutter, float top)
    {
        var draw = ImGui.GetWindowDrawList();
        const float tile = 44;
        const float titleScale = 1.35f;
        var wide = winW >= 760;
        // The meta text stays on the title line down to the smallest window, so the header never grows a third line.
        var metaRight = winW >= 560;
        var textX = gutter + 60;
        var origin = winPos + new Vector2(gutter, top + 20);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), 8f);
        GluttonyChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var titleLine = GluttonyChrome.LineHeight(titleScale);
        var smallLine = GluttonyChrome.LineHeight(Theme.SecondaryScale);
        var metaWidth = metaRight && m.Meta.Length > 0
            ? MathF.Min(winW * 0.4f, m.Meta.Max(s => GluttonyChrome.TextWidth(s, Theme.SecondaryScale)))
            : 0;
        var textWidth = metaRight ? winW - gutter - textX - metaWidth - 16 : winW - gutter - textX;
        var textPos = new Vector2(winPos.X + textX, origin.Y);

        var titleLines = metaRight ? [GluttonyChrome.FitText(m.Title, textWidth, titleScale)] : WrapLines(m.Title, textWidth, titleScale, 2);
        var y = origin.Y;
        foreach (var line in titleLines)
        {
            GluttonyChrome.DrawText(draw, new Vector2(textPos.X, y), line, Theme.TextPrimary, titleScale);
            y += titleLine;
        }
        y += 4;
        var descTop = y;
        var descLines = WrapLines(m.Description, textWidth, Theme.SecondaryScale, wide ? 1 : 2);
        foreach (var line in descLines)
        {
            GluttonyChrome.DrawText(draw, new Vector2(textPos.X, y), line, Theme.TextSecondary, Theme.SecondaryScale);
            y += smallLine;
        }
        var blockBottom = y;
        if (metaRight)
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
        var elided = (metaRight && titleLines.Count > 0 && titleLines[^1] != m.Title)
                     || (wide && descLines.Count > 0 && descLines[^1] != m.Description);
        if (elided && ImGui.IsMouseHoveringRect(textPos, new Vector2(textPos.X + textWidth, blockBottom)))
            ImGui.SetTooltip(m.Title + "\n" + m.Description);
        return top + 20 + MathF.Max(tile, blockBottom - origin.Y) + 8;
    }

    // ---- body ----

    private static void DrawBody(Vector2 size, float gutter, Action? prefix, Action body, bool card)
    {
        GluttonyChrome.BeginBody(size, gutter, "##gc_shell_body", card);
        try
        {
            prefix?.Invoke();
            body();
        }
        finally
        {
            // A page that throws must not leave the child windows and style stack open for the rest of the frame.
            GluttonyChrome.EndBody();
        }
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
        GluttonyChrome.DrawFooterBand(draw, winPos, winSize, height);

        var narrow = winSize.X < 620;
        var hasStop = f.StopLabel is not null;
        var hasReport = m.ReportButton is not null;
        var block = GluttonyChrome.LineHeight() + 4 + GluttonyChrome.LineHeight(Theme.SecondaryScale);
        var stopW = hasStop ? GluttonyChrome.ActionWidth(72, f.StopLabel!) : 0;
        var reportW = hasReport ? GluttonyChrome.TextWidth(m.ReportLabel) + 32 : 0;
        var actionsW = stopW + reportW + (hasStop && hasReport ? GluttonyChrome.Gap : 0);
        var textLeft = winPos.X + gutter + 16;
        float actionY, textTop, textRight;
        var actionsX = winPos.X + winSize.X - gutter - actionsW;
        if (narrow && (hasStop || hasReport))
        {
            textTop = top + 12;
            actionY = textTop + block + 12;
            actionsX = winPos.X + gutter;
            textRight = winPos.X + winSize.X - gutter;
        }
        else
        {
            textTop = top + ((height - block) / 2f);
            actionY = top + ((height - GluttonyChrome.Control) / 2f);
            textRight = actionsW > 0 ? actionsX - 16 : winPos.X + winSize.X - gutter;
        }
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + 4, textTop + (GluttonyChrome.LineHeight() / 2f)),
            Theme.DotRadius + 1, Theme.Pack(GluttonyChrome.StateColor(f.State)));
        var width = textRight - textLeft;
        var shown = GluttonyChrome.FitText(f.Primary, width);
        var shownSecondary = GluttonyChrome.FitText(f.Secondary, width, Theme.SecondaryScale);
        GluttonyChrome.DrawText(draw, new Vector2(textLeft, textTop), shown, Theme.TextPrimary);
        GluttonyChrome.DrawText(draw, new Vector2(textLeft, textTop + GluttonyChrome.LineHeight() + 4), shownSecondary,
            Theme.TextSecondary, Theme.SecondaryScale);
        if ((shown != f.Primary || shownSecondary != f.Secondary)
            && ImGui.IsMouseHoveringRect(new Vector2(textLeft, textTop), new Vector2(textRight, textTop + block)))
            ImGui.SetTooltip(f.Primary + "\n" + f.Secondary);
        var x = actionsX;
        if (hasReport)
        {
            GluttonyChrome.StyledButtonCallback(new Vector2(x, actionY), m.ReportButton!);
            x += reportW + GluttonyChrome.Gap;
        }
        if (hasStop && GluttonyChrome.StopButton(draw, new Vector2(x, actionY), new Vector2(stopW, GluttonyChrome.Control), f.StopLabel!))
            r.FooterStop = true;
    }
}
