using System.Numerics;
using CurrencySpender.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class DebugTabWindow : Window
{
    public DebugTabWindow() : base($"{P.Name} Debug {P.Version}###DebugTabWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(520, 320),
            MaximumSize = new(9999, 9999),
        };
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} Debug {P.Version}###DebugTabWindow";
    }

    public override void Draw()
    {
        CurrencyChrome.PushFrame();
        try
        {
            CurrencyChrome.TopBar("LazyCurrencySpender", UiState.Idle);
            ImGui.BeginChild("##currency_debug_body", Vector2.Zero, false);
            CurrencyChrome.PageHeader("Debug data",
                "Generated shop data and unlock states for diagnosis.",
                $"{Generator.shops.Count} shops · {Generator.items.Count} items");
            DebugMainTab.Draw();
            ImGui.EndChild();
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }
}
