using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;
using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Two-level responsive navigation and live status for the fork-only PvP settings window.</summary>
internal sealed class PvpApprovedShell
{
    private const float WideFooterHeight = 68f;
    private const float NarrowFooterHeight = 100f;

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

    internal void Draw(
        (string Name, Action Draw)[] tabs,
        SwitchView view,
        ref string switchMessage,
        Action<string, Action> drawTabBody,
        Func<bool, string?> changeMode)
    {
        Theme.PushFrame();
        try
        {
            DrawTopBar(view, switchMessage);
            DrawFeatureRow();
            DrawPageHeader();
            if (switchMessage.Length > 0)
            {
                Widgets.InfoBox(switchMessage, UiState.Failed);
            }

            float footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
            Vector2 bodySize = new(0, MathF.Max(40, ImGui.GetContentRegionAvail().Y - footerHeight));
            using (var body = ImRaii.Child("##pvpApprovedBody", bodySize, false))
            {
                if (body)
                {
                    (string name, Action draw) = tabs.First(tab => tab.Name == _currentPage);
                    drawTabBody(name, draw);
                }
            }

            DrawFooter(view, ref switchMessage, changeMode);
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

    private void DrawTopBar(SwitchView view, string switchMessage)
    {
        float width = ImGui.GetContentRegionAvail().X;
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        const float gap = 2 * Theme.Unit;
        string[] groups = ["Play", "Setup"];
        Vector2 brandSize = new(174, 42);
        Vector2[] groupSizes = groups.Select(group => new Vector2(MathF.Max(78, ImGui.CalcTextSize(group).X + 32), PvpApprovedChrome.ControlHeight)).ToArray();
        UiState state = StateOf(view, switchMessage);
        string stateLabel = Copy.Word(state);
        Vector2 statusSize = new(MathF.Max(86, ImGui.CalcTextSize(stateLabel).X + 43), PvpApprovedChrome.ControlHeight);
        Vector2 closeSize = new(PvpApprovedChrome.ControlHeight);
        float required = brandSize.X + groupSizes.Sum(size => size.X) + statusSize.X + closeSize.X + (5 * gap);
        bool singleRow = width >= required;
        Vector2[] groupPositions = new Vector2[groups.Length];
        Vector2 brandPos = new(0, 12);
        Vector2 statusPos;
        Vector2 closePos;
        float barHeight;

        if (singleRow)
        {
            barHeight = 66;
            float x = brandSize.X + gap;
            float y = (barHeight - PvpApprovedChrome.ControlHeight) / 2f;
            for (int i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, y);
                x += groupSizes[i].X + gap;
            }

            statusPos = new Vector2(width - statusSize.X - closeSize.X - gap, y);
            closePos = new Vector2(width - closeSize.X, y);
        }
        else
        {
            statusPos = new Vector2(width - statusSize.X - closeSize.X - gap, 16);
            closePos = new Vector2(width - closeSize.X, 16);
            float x = 0;
            float y = 62;
            for (int i = 0; i < groups.Length; i++)
            {
                if (x > 0 && x + groupSizes[i].X > width)
                {
                    x = 0;
                    y += PvpApprovedChrome.ControlHeight + gap;
                }

                groupPositions[i] = new Vector2(x, y);
                x += groupSizes[i].X + gap;
            }

            barHeight = y + PvpApprovedChrome.ControlHeight + 12;
        }

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        if (PvpApprovedChrome.BrandButton(draw, origin + brandPos, brandSize))
        {
            _currentPage = PvpSettingsCatalog.Match;
        }

        for (int i = 0; i < groups.Length; i++)
        {
            string group = groups[i];
            if (PvpApprovedChrome.OutlineButton(draw, "group_" + group, group, origin + groupPositions[i], groupSizes[i], Current.Group == group))
            {
                _currentPage = Pages.First(page => page.Group == group).Name;
            }
        }

        PvpApprovedChrome.StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        if (PvpApprovedChrome.OutlineButton(draw, "close_window", "×", origin + closePos, closeSize))
        {
            CloseRequested?.Invoke();
        }

        PvpUi.Tip("Close");
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawFeatureRow()
    {
        float width = ImGui.GetContentRegionAvail().X;
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        const float gap = 3 * Theme.Unit;
        Page[] features = Pages.Where(page => page.Group == Current.Group).ToArray();
        Vector2[] sizes = features.Select(feature => new Vector2(
            MathF.Min(width, MathF.Max(112, ImGui.CalcTextSize(feature.Name).X + 52)), PvpApprovedChrome.ControlHeight)).ToArray();
        Vector2[] positions = new Vector2[features.Length];
        float x = 0;
        float y = 8;
        for (int i = 0; i < features.Length; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += PvpApprovedChrome.ControlHeight + (2 * Theme.Unit);
            }

            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }

        float barHeight = y + PvpApprovedChrome.ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (int i = 0; i < features.Length; i++)
        {
            if (PvpApprovedChrome.FeatureButton(draw, features[i].Name, features[i].Name, origin + positions[i], sizes[i], _currentPage == features[i].Name))
            {
                _currentPage = features[i].Name;
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawPageHeader()
    {
        Page page = Current;
        string count = LiveCount(page.Name);
        float width = ImGui.GetContentRegionAvail().X;
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 tileSize = new(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        PvpApprovedChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 680)
        {
            float textX = origin.X + tileSize.X + 12;
            float countWidth = ImGui.CalcTextSize(count).X;
            PvpApprovedChrome.DrawText(draw, new Vector2(textX, origin.Y),
                PvpApprovedChrome.FitText(page.Name, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            PvpApprovedChrome.DrawText(draw, new Vector2(textX, origin.Y + 29),
                PvpApprovedChrome.FitText(page.Purpose, width - tileSize.X - 12 - countWidth - 16), Theme.TextSecondary);
            PvpApprovedChrome.DrawTextRight(draw, origin.X + width, origin.Y + 12, count, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        PvpApprovedChrome.DrawText(draw, origin + new Vector2(tileSize.X + 12, 4),
            PvpApprovedChrome.FitText(page.Name, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(page.Purpose);
        Widgets.Caption(count);
        ImGui.Separator();
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

    private static float FooterHeight(float width) => width < 560 ? NarrowFooterHeight : WideFooterHeight;

    private static void DrawFooter(SwitchView view, ref string switchMessage, Func<bool, string?> changeMode)
    {
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        float height = FooterHeight(width);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        UiState state = StateOf(view, switchMessage);
        string primary = view.IsOn
            ? "Running · automatic PvP actions are enabled"
            : view.ButtonEnabled
                ? "Ready · PvP Solver can be turned on"
                : "Idle · settings remain available outside PvP";
        string secondary = switchMessage.Length > 0 ? switchMessage : view.Reason;
        string actionLabel = view.IsOn ? "Stop" : view.ButtonLabel;
        float actionWidth = MathF.Min(width, MathF.Max(112, ImGui.CalcTextSize(actionLabel).X + 48));
        bool narrow = width < 560;
        Vector2 actionPos = origin + new Vector2(width - actionWidth, narrow ? 56 : 16);
        float textRight = narrow ? origin.X + width : actionPos.X - 16;
        Vector2 textPos = origin + new Vector2(24, 9);
        draw.AddCircleFilled(origin + new Vector2(10, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        PvpApprovedChrome.DrawText(draw, textPos, PvpApprovedChrome.FitText(primary, textRight - textPos.X), Theme.TextPrimary);
        PvpApprovedChrome.DrawText(draw, textPos + new Vector2(0, 27), PvpApprovedChrome.FitText(secondary, textRight - textPos.X), Theme.TextSecondary);
        ImGui.SetCursorScreenPos(actionPos);
        if (PvpApprovedChrome.Button(actionLabel + "##pvp_approved_mode", !view.ButtonEnabled, danger: view.IsOn,
                size: new Vector2(actionWidth, 36)))
        {
            switchMessage = changeMode(!view.IsOn) ?? string.Empty;
        }

        PvpUi.Tip(view.ButtonEnabled ? null : view.ButtonWhy);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }
}
