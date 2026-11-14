using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13): one section
// row (R1 layer 2) replaces the old tab bar; the Debug section appears only while Debug
// mode is on (the old conditional tab). No background work lives in this window, so it
// ships Idle and omits the footer (Brief R3).
internal class ConfigTabWindow : Window
{
    private enum Section
    {
        Settings,
        Currencies,
        Items,
        Changelog,
        About,
        Debug,
    }

    private Section _section = Section.Settings;

    public ConfigTabWindow() : base($"ConfigTabWindow")
    {
        this.SizeConstraints = new()
        {
            MinimumSize = new(350, 100),
            MaximumSize = new(9999, 9999)
        };
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} Settings {P.Version}###ConfigTabWindow";
    }

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            DrawTopBar();
            DrawSectionNav();

            switch (_section)
            {
                case Section.Settings:
                    Layout.PageHeader(null, FontAwesomeIcon.Cog.ToIconString(), "Settings",
                        "Display options, sellable-item filters and debug mode.",
                        $"{C.SelectedCurrencies.Count + C.SelectedCollectableTypes.Count} selections");
                    ConfigTab.Draw();
                    break;
                case Section.Currencies:
                    Layout.PageHeader(null, FontAwesomeIcon.Coins.ToIconString(), "Currencies",
                        "Pick which currencies the main window lists.",
                        $"{C.SelectedCurrencies.Count} selected");
                    ConfigCurrenciesTab.Draw();
                    break;
                case Section.Items:
                    Layout.PageHeader(null, FontAwesomeIcon.BoxOpen.ToIconString(), "Items",
                        "Items of interest shown in every spending guide.",
                        $"{C.ItemsOfInterest.Count} tracked");
                    ItemsTab.Draw();
                    break;
                case Section.Changelog:
                    Layout.PageHeader(null, FontAwesomeIcon.Book.ToIconString(), "Changelog",
                        "What changed in each version of this plugin.",
                        P.Version);
                    ChangelogTab.Draw();
                    break;
                case Section.About:
                    Layout.PageHeader(null, FontAwesomeIcon.Heart.ToIconString(), "About",
                        "Credits and project links.",
                        P.Version);
                    AboutTab.Draw();
                    break;
                case Section.Debug:
                    Layout.PageHeader(null, FontAwesomeIcon.Bug.ToIconString(), "Debug",
                        "Raw map, rank and generator data for diagnosis.",
                        P.Version);
                    DebugTab.Draw();
                    break;
            }
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private void DrawSectionNav()
    {
        if (Layout.NavSection("cs_cfg_nav_settings", "Settings", null, FontAwesomeIcon.Cog.ToIconString(),
                UiState.Idle, _section == Section.Settings))
            _section = Section.Settings;
        ImGui.SameLine();
        if (Layout.NavSection("cs_cfg_nav_currencies", "Currencies", null, FontAwesomeIcon.Coins.ToIconString(),
                UiState.Ready, _section == Section.Currencies))
            _section = Section.Currencies;
        ImGui.SameLine();
        if (Layout.NavSection("cs_cfg_nav_items", "Items", null, FontAwesomeIcon.BoxOpen.ToIconString(),
                C.ItemsOfInterest.Count > 0 ? UiState.Ready : UiState.Empty, _section == Section.Items))
            _section = Section.Items;
        ImGui.SameLine();
        if (Layout.NavSection("cs_cfg_nav_changelog", "Changelog", null, FontAwesomeIcon.Book.ToIconString(),
                UiState.Idle, _section == Section.Changelog))
            _section = Section.Changelog;
        ImGui.SameLine();
        if (Layout.NavSection("cs_cfg_nav_about", "About", null, FontAwesomeIcon.Heart.ToIconString(),
                UiState.Idle, _section == Section.About))
            _section = Section.About;
        if (C.Debug)
        {
            ImGui.SameLine();
            if (Layout.NavSection("cs_cfg_nav_debug", "Debug", null, FontAwesomeIcon.Bug.ToIconString(),
                    UiState.Idle, _section == Section.Debug))
                _section = Section.Debug;
        }

        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private void DrawTopBar()
    {
        // Top bar: title left, live-state pill right (R3 place 1) - the same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted($"{P.Name} Settings");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(UiState.Idle, "Settings", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }
}
