using System.Numerics;
using CurrencySpender.Classes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class MainTabWindow : Window
{
    private enum Section
    {
        Currencies,
        Instructions,
    }

    private Section _section = Section.Currencies;
    private readonly CurrencyChrome.ShellControls _controls;

    public MainTabWindow() : base($"{P.Name} {P.Version}###MainTabWindow", ImGuiWindowFlags.NoTitleBar)
    {
        _controls = new(this, new Vector2(460, 320), new Vector2(9999, 9999));
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} {P.Version}###MainTabWindow";
    }

    public override void Draw()
    {
        var minimized = _controls.PrepareDraw();
        CurrencyChrome.PushFrame();
        try
        {
            var loggedIn = Service.Objects.LocalPlayer != null;
            var state = !loggedIn ? UiState.NeedsSetup : P.Currencies?.Count > 0 ? UiState.Ready : UiState.Empty;
            CurrencyChrome.TopBar("LazyCurrencySpender", state, "♥ Ko-fi",
                () => GenericHelpers.ShellStart(KofiBanner.DonateLink), _controls,
                "Settings", () => P.configTabWindow.IsOpen = true, "Open settings window");
            if (minimized)
                return;
            _section = (Section)CurrencyChrome.FeatureRow("main_nav", ["Currencies", "Instructions"], (int)_section);

            ImGui.BeginChild("##currency_main_body", Vector2.Zero, false);
            switch (_section)
            {
                case Section.Currencies:
                    CurrencyChrome.PageHeader("Currencies",
                        "Track live balances and open a spending guide for each currency.",
                        $"{P.config.SelectedCurrencies.Count} tracked");
                    MainTab.Draw();
                    break;
                case Section.Instructions:
                    CurrencyChrome.PageHeader("Instructions",
                        "How spending guides, vendor flags, and teleport actions work.",
                        "4 steps");
                    InstructionsTab.Draw();
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
