using System.Numerics;
using CurrencySpender.Classes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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

    public MainTabWindow() : base($"{P.Name} {P.Version}###MainTabWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(460, 320),
            MaximumSize = new(9999, 9999),
        };
        P.ws.AddWindow(this);
        TitleBarButtons.Add(new()
        {
            Click = (m) => { if (m == ImGuiMouseButton.Left) P.configTabWindow.IsOpen = true; },
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new(2, 2),
            ShowTooltip = () => ImGui.SetTooltip("Open settings window"),
        });
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} {P.Version}###MainTabWindow";
    }

    public override void Draw()
    {
        CurrencyChrome.PushFrame();
        try
        {
            var loggedIn = Service.Objects.LocalPlayer != null;
            var state = !loggedIn ? UiState.NeedsSetup : P.Currencies?.Count > 0 ? UiState.Ready : UiState.Empty;
            CurrencyChrome.TopBar("LazyCurrencySpender", state, "♥ Ko-fi",
                () => GenericHelpers.ShellStart(KofiBanner.DonateLink));
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
