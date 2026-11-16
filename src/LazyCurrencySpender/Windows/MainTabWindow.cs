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

    public MainTabWindow() : base($"{P.Name} {P.Version}###MainTabWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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

            var footerHeight = CurrencyChrome.FooterHeight(); // Fork: suite footer status line
            CurrencyChrome.BeginBody("##currency_main_body", footerHeight); // Fork: shared scrolling body child with the suite gutter
            switch (_section)
            {
                case Section.Currencies:
                    CurrencyChrome.PageHeader("Currencies",
                        "Track live balances and open a spending guide for each currency.",
                        loggedIn ? $"{P.config.SelectedCurrencies.Count} tracked" : ""); // Fork: no stale count while logged out
                    MainTab.Draw();
                    break;
                case Section.Instructions:
                    CurrencyChrome.PageHeader("Instructions",
                        "How spending guides, vendor flags, and teleport actions work.",
                        "4 steps");
                    InstructionsTab.Draw();
                    break;
            }
            CurrencyChrome.EndBody(); // Fork: pairs with BeginBody
            CurrencyChrome.Footer(footerHeight, !loggedIn ? "Not logged in" : $"{P.config.SelectedCurrencies.Count} tracked currencies",
                !loggedIn ? "Log in to a character to read balances" : "Select Spend it to open a spending guide", state);
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }
}
