using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

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
    private readonly CurrencyChrome.ShellControls _controls;

    public ConfigTabWindow() : base("ConfigTabWindow", ImGuiWindowFlags.NoTitleBar)
    {
        _controls = new(this, new Vector2(520, 360), new Vector2(9999, 9999));
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} Settings {P.Version}###ConfigTabWindow";
    }

    public override void Draw()
    {
        var minimized = _controls.PrepareDraw();
        CurrencyChrome.PushFrame();
        try
        {
            if (!C.Debug && _section == Section.Debug)
                _section = Section.Settings;
            CurrencyChrome.TopBar("LazyCurrencySpender", UiState.Idle, controls: _controls);
            if (minimized)
                return;
            string[] sections = C.Debug
                ? ["Settings", "Currencies", "Items", "Changelog", "About", "Debug"]
                : ["Settings", "Currencies", "Items", "Changelog", "About"];
            _section = (Section)CurrencyChrome.FeatureRow("config_nav", sections, (int)_section);

            ImGui.BeginChild("##currency_config_body", Vector2.Zero, false);
            switch (_section)
            {
                case Section.Settings:
                    CurrencyChrome.PageHeader("Settings",
                        "Display options, spending-guide filters, and startup behavior.",
                        $"{C.SelectedCurrencies.Count + C.SelectedCollectableTypes.Count} selections");
                    ConfigTab.Draw();
                    break;
                case Section.Currencies:
                    CurrencyChrome.PageHeader("Currencies",
                        "Choose which currencies appear in the main window.",
                        $"{C.SelectedCurrencies.Count} selected");
                    ConfigCurrenciesTab.Draw();
                    break;
                case Section.Items:
                    CurrencyChrome.PageHeader("Items of interest",
                        "Track specific items in every applicable spending guide.",
                        $"{C.ItemsOfInterest.Count} tracked");
                    ItemsTab.Draw();
                    break;
                case Section.Changelog:
                    CurrencyChrome.PageHeader("Changelog",
                        "Changes recorded for each released version.",
                        P.Version);
                    ChangelogTab.Draw();
                    break;
                case Section.About:
                    CurrencyChrome.PageHeader("About",
                        "Credits, project links, and support information.",
                        P.Version);
                    AboutTab.Draw();
                    break;
                case Section.Debug:
                    CurrencyChrome.PageHeader("Debug",
                        "Raw map, rank, and generator data for diagnosis.",
                        $"{PlayerHelper.GCRanks.Count + PlayerHelper.SharedFateRanks.Count} rank rows");
                    DebugTab.Draw();
                    break;
            }
            ImGui.EndChild();
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }
}
