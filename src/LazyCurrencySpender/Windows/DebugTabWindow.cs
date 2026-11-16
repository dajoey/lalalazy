using System.Numerics;
using CurrencySpender.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class DebugTabWindow : Window
{
    private readonly CurrencyChrome.ShellControls _controls;

    public DebugTabWindow() : base($"{P.Name} Debug {P.Version}###DebugTabWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _controls = new(this, new Vector2(520, 320), new Vector2(9999, 9999));
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} Debug {P.Version}###DebugTabWindow";
    }

    public override void Draw()
    {
        var minimized = _controls.PrepareDraw();
        CurrencyChrome.PushFrame();
        try
        {
            CurrencyChrome.TopBar("LazyCurrencySpender", UiState.Idle, controls: _controls);
            if (minimized)
                return;
            CurrencyChrome.BeginBody("##currency_debug_body"); // Fork: shared scrolling body child with the suite gutter
            CurrencyChrome.PageHeader("Debug data",
                "Generated shop data and unlock states for diagnosis.",
                $"{Generator.shops.Count} shops · {Generator.items.Count} items");
            DebugMainTab.Draw();
            CurrencyChrome.EndBody(); // Fork: pairs with BeginBody
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }
}
