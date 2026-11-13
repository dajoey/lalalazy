using System.Numerics;
using CurrencySpender.Classes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so the top bar is drawn with the same kit primitives Layout.Shell uses, and the
// suite frame comes from Theme.PushFrame/PopFrame. Navigation is one section row
// (Currencies, Instructions - R1 layer 2, no group tabs).
internal class MainTabWindow : Window
{
    private enum Section
    {
        Currencies,
        Instructions,
    }

    private Section _section = Section.Currencies;

    public MainTabWindow() : base($"")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(250, 100),
            MaximumSize = new(9999, 9999)
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
        Theme.PushFrame();
        try
        {
            // Live state (R3 place 1): the plugin needs a logged-in player to show anything.
            var loggedIn = Service.Objects.LocalPlayer != null;
            var state = loggedIn ? UiState.Ready : UiState.NeedsSetup;
            DrawTopBar(state);
            KofiBanner.DrawRight();

            if (Layout.NavSection("cs_nav_currencies", "Currencies", null, FontAwesomeIcon.Coins.ToIconString(),
                    P.Currencies?.Count > 0 ? UiState.Ready : UiState.Empty, _section == Section.Currencies))
                _section = Section.Currencies;
            ImGui.SameLine();
            if (Layout.NavSection("cs_nav_instructions", "Instructions", null, FontAwesomeIcon.Book.ToIconString(),
                    UiState.Idle, _section == Section.Instructions))
                _section = Section.Instructions;
            ImGui.Dummy(new Vector2(0, Theme.Unit));

            switch (_section)
            {
                case Section.Currencies:
                    Layout.PageHeader(null, FontAwesomeIcon.Coins.ToIconString(), "Currencies",
                        "Track your currencies and open the spending guide for each one.",
                        $"{P.config.SelectedCurrencies.Count} tracked");
                    MainTab.Draw();
                    break;
                case Section.Instructions:
                    Layout.PageHeader(null, FontAwesomeIcon.Book.ToIconString(), "Instructions",
                        "How to use the spending guide and its map buttons.",
                        "4 steps");
                    InstructionsTab.Draw();
                    break;
            }
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private void DrawTopBar(UiState state)
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
        ImGui.TextUnformatted(P.Name);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "Currency Spender", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }
}
