using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;
using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Two-level responsive navigation and live status for the fork-only PvP settings window.</summary>
internal sealed class PvpApprovedShell
{
    private const float Control = PvpApprovedChrome.ControlHeight;

    private sealed record Page(string Name, string Group, string Purpose);

    private static readonly Page[] Pages =
    [
        new(PvpSettingsCatalog.Match, "Play", "Controls when PvP Solver starts, stops, and hands control back."),
        new(PvpSettingsCatalog.Survival, "Play", "Groups Guard, Purify, movement, and automatic defensive settings."),
        new(PvpSettingsCatalog.Targeting, "Play", "Orders target priorities and controls area attacks, burst, and stickiness."),
        new("My Job", "Setup", "Edits the selected job's enablement, rotation options, and defensive thresholds."),
        new(PvpSettingsCatalog.Display, "Setup", "Controls live overlays, teaching highlights, messages, and tooltips."),
        new(PvpSettingsCatalog.Advanced, "Setup", "Opens the full editor and groups backup, restore, tutorial, and debug tools."),
    ];

    private string _currentPage = PvpSettingsCatalog.Match;

    internal Action? CloseRequested { get; set; }

    private Page Current => Pages.First(page => page.Name == _currentPage);

    internal void Reset() => _currentPage = PvpSettingsCatalog.Match;

    /// <summary>One outer gutter for every band, the page header, the body padding and the footer.</summary>
    internal static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

    internal void Draw(
        (string Name, Action Draw)[] tabs,
        SwitchView view,
        ref string switchMessage,
        Action<string, Action> drawTabBody,
        Func<bool, string?> changeMode)
    {
        PvpApprovedChrome.BeginFrame();
        Theme.PushFrame();
        try
        {
            Vector2 winPos = ImGui.GetWindowPos();
            Vector2 winSize = ImGui.GetWindowSize();
            PvpApprovedChrome.WindowPos = winPos;
            PvpApprovedChrome.WindowSize = winSize;
            float gutter = Gutter(winSize.X);
            bool compact = winSize.Y < 520;
            ImDrawListPtr draw = ImGui.GetWindowDrawList();
            UiState state = StateOf(view, switchMessage);

            float y = DrawTopBar(draw, winPos, winSize.X, gutter, state, compact);
            y += DrawFeatureBand(draw, winPos, winSize.X, gutter, y, compact);
            y += DrawPageHeader(draw, winPos, winSize.X, gutter, y, compact);

            float footerHeight = FooterHeight(winSize.X, compact);
            float bodyHeight = MathF.Max(80, winSize.Y - footerHeight - y);
            DrawBody(tabs, drawTabBody, winSize.X, gutter, y, bodyHeight, switchMessage);
            DrawFooter(draw, winPos, winSize, gutter, compact, view, state, ref switchMessage, changeMode);

            if (PvpApprovedChrome.ModalOpen)
            {
                // A confirmation popup is open: darken the whole window behind it.
                draw.AddRectFilled(winPos, winPos + winSize, Theme.Pack(new Vector4(0f, 0f, 0f, 0.45f)));
            }
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private static UiState StateOf(SwitchView view, string switchMessage)
    {
        if (switchMessage.Length > 0)
        {
            return UiState.Failed;
        }

        if (view.IsOn)
        {
            return UiState.Running;
        }

        return view.ButtonEnabled ? UiState.Ready : UiState.Idle;
    }

    // ---- header band ------------------------------------------------------------------------------------------

    // Band: 14 px top and bottom, 40 px brand, 36 px controls centred on it. Returns the band height plus its rule.
    private float DrawTopBar(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, UiState state, bool compact)
    {
        float pad = compact ? 8 : 14;
        float brandHeight = compact ? 36 : 40;
        const float gap = 8;
        string[] groups = ["Play", "Setup"];
        float inner = width - (2 * gutter);
        Vector2 closeSize = new(Control);
        Vector2 pillSize = new(PvpApprovedChrome.StatusPillWidth(), Control);
        float[] groupWidths = groups.Select(group => MathF.Max(72, PvpApprovedChrome.TextWidth(group) + 32)).ToArray();
        float groupsWidth = groupWidths.Sum() + (gap * (groups.Length - 1));
        float rest = pillSize.X + closeSize.X + (2 * gap);

        float brandScale = Theme.TitleScale;
        float brandWidth = PvpApprovedChrome.BrandWidth(brandScale);
        bool singleRow = brandWidth + 16 + groupsWidth + rest <= inner;
        if (!singleRow && brandWidth + rest > inner)
        {
            brandScale = 1f;
            brandWidth = PvpApprovedChrome.BrandWidth(brandScale);
        }

        float barHeight = singleRow ? pad + brandHeight + pad : pad + brandHeight + gap + Control + pad;
        draw.PushClipRect(winPos, winPos + new Vector2(width, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barHeight), winPos + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        float rowCentre = pad + (brandHeight / 2f);
        Vector2 closePos = new(width - gutter - closeSize.X, rowCentre - (closeSize.Y / 2f));
        Vector2 pillPos = new(closePos.X - gap - pillSize.X, closePos.Y);
        float brandMax = pillPos.X - 12 - gutter;
        brandWidth = MathF.Min(brandWidth, singleRow ? brandWidth : MathF.Max(80, brandMax));

        if (PvpApprovedChrome.Brand(draw, winPos + new Vector2(gutter, pad), new Vector2(brandWidth, brandHeight), brandScale))
        {
            _currentPage = PvpSettingsCatalog.Match;
        }

        float navY = singleRow ? closePos.Y : pad + brandHeight + gap;
        float x = gutter + brandWidth + 16;
        for (int i = 0; i < groups.Length; i++)
        {
            float w = groupWidths[i];
            if (!singleRow)
            {
                w = (inner - (gap * (groups.Length - 1))) / groups.Length;
                x = gutter + (i * (w + gap));
            }

            if (PvpApprovedChrome.NavPill(draw, "group_" + groups[i], groups[i], winPos + new Vector2(x, navY), new Vector2(w, Control), Current.Group == groups[i]))
            {
                _currentPage = Pages.First(page => page.Group == groups[i]).Name;
            }

            x += w + gap;
        }

        PvpApprovedChrome.StatusPill(draw, winPos + pillPos, pillSize, Copy.Word(state), state);
        if (PvpApprovedChrome.CloseButton(draw, winPos + closePos, closeSize))
        {
            CloseRequested?.Invoke();
        }

        return barHeight + 1;
    }

    // Sub-navigation: 12 px padding, 32 px pills, 12 px gaps; wraps onto more rows when the pills do not fit.
    private float DrawFeatureBand(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, float top, bool compact)
    {
        float pad = compact ? 8 : 12;
        const float height = 32;
        Page[] features = Pages.Where(page => page.Group == Current.Group).ToArray();
        float inner = width - (2 * gutter);
        Vector2[] positions = new Vector2[features.Length];
        float[] widths = features.Select(feature => MathF.Min(inner, PvpApprovedChrome.FeatureButtonWidth(feature.Name))).ToArray();
        float x = 0;
        float y = 0;
        for (int i = 0; i < features.Length; i++)
        {
            if (x > 0 && x + widths[i] > inner)
            {
                x = 0;
                y += height + 8;
            }

            positions[i] = new Vector2(x, y);
            x += widths[i] + 12;
        }

        float barHeight = pad + y + height + pad;
        draw.PushClipRect(winPos, winPos + new Vector2(width, top + barHeight + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(width, top + barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + barHeight), winPos + new Vector2(width, top + barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        for (int i = 0; i < features.Length; i++)
        {
            Vector2 pos = winPos + new Vector2(gutter + positions[i].X, top + pad + positions[i].Y);
            if (PvpApprovedChrome.FeatureButton(draw, features[i].Name, features[i].Name, pos, new Vector2(widths[i], height), _currentPage == features[i].Name))
            {
                _currentPage = features[i].Name;
            }
        }

        return barHeight + 1;
    }

    // Tile 44, text column at gutter + 60. Returns the header height; no divider below it.
    private float DrawPageHeader(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, float top, bool compact)
    {
        Page page = Current;
        string count = LiveCount(page.Name);
        const float titleScale = 1.35f;
        float sm = Theme.SecondaryScale;
        bool wide = width >= 760;
        float tile = compact ? 32 : 44;
        float above = compact ? 12 : 20;
        float below = compact ? 4 : 8;
        float textX = gutter + (tile + 16);
        Vector2 left = winPos + new Vector2(textX, top + above);
        float titleH = PvpApprovedChrome.LineHeight(titleScale);
        float smH = PvpApprovedChrome.LineHeight(sm);
        float block;

        draw.AddRectFilled(winPos + new Vector2(gutter, top + above), winPos + new Vector2(gutter + tile, top + above + tile),
            Theme.Pack(Theme.Accent with { W = 0.14f }), 8f);
        PvpApprovedChrome.Diamond(draw, winPos + new Vector2(gutter + (tile / 2f), top + above + (tile / 2f)), compact ? 8 : 10, Theme.Pack(Theme.Accent));

        if (compact)
        {
            // Short window: title and count only, so the page body keeps its height.
            float w = width - gutter - textX;
            string shown = PvpApprovedChrome.FitText(page.Name, w, titleScale);
            PvpApprovedChrome.Text(draw, left + new Vector2(0, (tile - titleH) / 2f), shown, Theme.TextPrimary, titleScale);
            float countW = PvpApprovedChrome.TextWidth(count, sm);
            float titleW = PvpApprovedChrome.TextWidth(shown, titleScale);
            if (titleW + 16 + countW <= w)
            {
                PvpApprovedChrome.TextRight(draw, winPos.X + width - gutter, left.Y + ((tile - smH) / 2f), count, Theme.TextSecondary, sm);
            }

            return above + tile + below;
        }

        if (wide)
        {
            float metaWidth = MathF.Min(PvpApprovedChrome.TextWidth(count, sm), width * 0.4f);
            float textWidth = width - gutter - textX - metaWidth - 16;
            string shownTitle = PvpApprovedChrome.FitText(page.Name, textWidth, titleScale);
            string shownDescription = PvpApprovedChrome.FitText(page.Purpose, textWidth, sm);
            PvpApprovedChrome.Text(draw, left, shownTitle, Theme.TextPrimary, titleScale);
            PvpApprovedChrome.Text(draw, left + new Vector2(0, titleH + 4), shownDescription, Theme.TextSecondary, sm);
            block = titleH + 4 + smH;
            string shownCount = PvpApprovedChrome.FitText(count, metaWidth, sm);
            PvpApprovedChrome.TextRight(draw, winPos.X + width - gutter, left.Y + ((titleH - smH) / 2f), shownCount, Theme.TextSecondary, sm);
            if (shownTitle != page.Name || shownDescription != page.Purpose || shownCount != count)
            {
                HoverIfElided(winPos, top + above, width, block, page.Name + "\n" + page.Purpose + "\n" + count);
            }
        }
        else
        {
            float textWidth = width - gutter - textX;
            List<string> titleLines = PvpApprovedChrome.WrapLines(page.Name, textWidth, titleScale, 2);
            float y = left.Y;
            foreach (string line in titleLines)
            {
                PvpApprovedChrome.Text(draw, new Vector2(left.X, y), line, Theme.TextPrimary, titleScale);
                y += titleH;
            }

            y += 4;
            List<string> lines = PvpApprovedChrome.WrapLines(page.Purpose, textWidth, sm, 3);
            foreach (string line in lines)
            {
                PvpApprovedChrome.Text(draw, new Vector2(left.X, y), line, Theme.TextSecondary, sm);
                y += smH;
            }

            y += 4;
            PvpApprovedChrome.Text(draw, new Vector2(left.X, y), PvpApprovedChrome.FitText(count, textWidth, sm), Theme.TextSecondary, sm);
            y += smH;
            block = y - left.Y;
        }

        return above + MathF.Max(tile, block) + below;
    }

    private static void HoverIfElided(Vector2 winPos, float top, float width, float block, string full)
        => PvpApprovedChrome.HoverTip(winPos + new Vector2(0, top), winPos + new Vector2(width, top + block), full);

    // ---- body -------------------------------------------------------------------------------------------------

    private void DrawBody(
        (string Name, Action Draw)[] tabs,
        Action<string, Action> drawTabBody,
        float width,
        float gutter,
        float top,
        float height,
        string switchMessage)
    {
        ImGui.SetCursorPos(new Vector2(0, top));
        using var vars = ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 1f)
            .Push(ImGuiStyleVar.ChildRounding, 0f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16))
            .Push(ImGuiStyleVar.ScrollbarSize, 10f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 8f);
        using var colors = ImRaii.PushColor(ImGuiCol.Border, 0u)
            .Push(ImGuiCol.ChildBg, 0u)
            .Push(ImGuiCol.ScrollbarBg, 0u)
            .Push(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        using var child = ImRaii.Child("##pvpApprovedBody", new Vector2(width, height), true);
        if (!child)
        {
            return;
        }

        // Cards end one gutter before the window edge (the scrollbar lives in the gutter), like the footer button.
        PvpApprovedChrome.PushRight(ImGui.GetCursorScreenPos().X + width - (2 * gutter));
        try
        {
            if (switchMessage.Length > 0)
            {
                PvpApprovedChrome.Notice("Could not switch PvP Solver", switchMessage, UiState.Failed);
            }

            (string name, Action draw) = tabs.First(tab => tab.Name == _currentPage);
            drawTabBody(name, draw);
            DrawScrollFades(width, height);
        }
        finally
        {
            PvpApprovedChrome.PopRight();
        }
    }

    // A short fade above the footer where more content scrolls in. The pinned header edge is a plain clip, like the other windows.
    private static void DrawScrollFades(float width, float height)
    {
        if (ImGui.GetScrollMaxY() <= 0)
        {
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 min = ImGui.GetWindowPos();
        float right = min.X + width - 11;
        uint window = ImGui.GetColorU32(ImGuiCol.WindowBg);
        uint clear = window & 0x00FFFFFFu;
        uint solid = window | 0xFF000000u;
        if (ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            float bottom = min.Y + height - 1;
            draw.AddRectFilledMultiColor(new Vector2(min.X, bottom - 12), new Vector2(right, bottom), clear, clear, solid, solid);
        }
    }

    private static string LiveCount(string page)
    {
        if (page == "My Job")
        {
            int jobs = JobKey.Groups.Sum(group => group.Jobs.Count);
            return $"{jobs} jobs · {PvpJobTab.PickedKey()}";
        }

        int settings = PvpSettingsCatalog.ForTab(page).Count();
        if (page == PvpSettingsCatalog.Targeting)
        {
            return $"{Service.Config.TargetingTypes.Count} target types · {settings} settings";
        }

        if (page == PvpSettingsCatalog.Display)
        {
            int overlays = 0;
            if (Service.Config.ShowControlWindow) overlays++;
            if (Service.Config.ShowNextActionWindow) overlays++;
            if (Service.Config.ShowCooldownWindow) overlays++;
            if (Service.Config.ShowActionTimelineWindow) overlays++;
            return $"{overlays} overlays on · {settings} settings";
        }

        if (page == PvpSettingsCatalog.Advanced)
        {
            return $"{settings} debug setting · 9 editor links";
        }

        return $"{settings} settings";
    }

    // ---- footer -----------------------------------------------------------------------------------------------

    private static bool Narrow(float width, bool compact) => width < 620 && !compact;

    private static float FooterHeight(float width, bool compact)
    {
        float block = PvpApprovedChrome.LineHeight() + 4 + PvpApprovedChrome.LineHeight(Theme.SecondaryScale);
        return Narrow(width, compact) ? 12 + block + 12 + Control + 12 : 12 + MathF.Max(block, Control) + 12;
    }

    private static void DrawFooter(
        ImDrawListPtr draw,
        Vector2 winPos,
        Vector2 winSize,
        float gutter,
        bool compact,
        SwitchView view,
        UiState state,
        ref string switchMessage,
        Func<bool, string?> changeMode)
    {
        float width = winSize.X;
        bool narrow = Narrow(width, compact);
        float height = FooterHeight(width, compact);
        float top = winSize.Y - height;
        float lh = PvpApprovedChrome.LineHeight();
        float smH = PvpApprovedChrome.LineHeight(Theme.SecondaryScale);
        float textBlock = lh + 4 + smH;

        draw.PushClipRect(winPos + new Vector2(0, top), winPos + winSize, false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top), winPos + new Vector2(width, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        string primary = switchMessage.Length > 0
            ? "Failed · PvP Solver was not switched"
            : view.IsOn
                ? "Running · automatic PvP actions are enabled"
                : view.ButtonEnabled
                    ? "Ready · PvP Solver can be turned on"
                    : "Idle · settings remain available outside PvP";
        string secondary = switchMessage.Length > 0 ? switchMessage : view.Reason;
        string actionLabel = view.IsOn ? "Stop" : view.ButtonLabel;
        Vector2 actionSize = new(PvpApprovedChrome.ButtonWidth(actionLabel, view.IsOn ? 72 : 96), Control);
        if (narrow)
        {
            actionSize.X = width - (2 * gutter);
        }

        Vector2 actionPos = narrow
            ? new Vector2(gutter, top + height - 12 - Control)
            : new Vector2(width - gutter - actionSize.X, top + ((height - Control) / 2f));
        float textRight = narrow ? width - gutter : actionPos.X - 16;
        float textX = gutter + 16;
        float textTop = narrow ? top + 12 : top + ((height - textBlock) / 2f);
        float textWidth = textRight - textX;

        // Concise first line (parentheticals stripped); the tooltip carries the full text when anything is elided.
        string headline = Regex.Replace(primary, @"\s*\([^)]*\)", string.Empty);
        string shownHeadline = PvpApprovedChrome.FitText(headline, textWidth);
        string shownDetail = PvpApprovedChrome.FitText(secondary, textWidth, Theme.SecondaryScale);
        draw.AddCircleFilled(winPos + new Vector2(gutter + 4, textTop + (lh / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        PvpApprovedChrome.Text(draw, winPos + new Vector2(textX, textTop), shownHeadline, Theme.TextPrimary);
        PvpApprovedChrome.Text(draw, winPos + new Vector2(textX, textTop + lh + 4), shownDetail, Theme.TextSecondary, Theme.SecondaryScale);
        if (shownHeadline != headline || shownDetail != secondary)
        {
            PvpApprovedChrome.HoverTip(
                winPos + new Vector2(textX, textTop),
                winPos + new Vector2(textRight, textTop + textBlock),
                headline + "\n" + secondary);
        }

        bool clicked = PvpApprovedChrome.Button(
            "mode",
            actionLabel,
            winPos + actionPos,
            actionSize,
            view.IsOn ? PvpApprovedChrome.ButtonStyle.Danger : PvpApprovedChrome.ButtonStyle.Normal,
            !view.ButtonEnabled,
            Theme.Surface);
        PvpUi.Tip(view.ButtonEnabled ? null : view.ButtonWhy);
        if (clicked)
        {
            switchMessage = changeMode(!view.IsOn) ?? string.Empty;
        }
    }
}
