using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyCrafter.UI;

/// <summary>Empty shell; tabs and tables land in Phase 4.</summary>
public sealed class MainWindow : Window
{
    private readonly Plugin _plugin;

    public MainWindow(Plugin plugin) : base("LazyCrafter##lcraft")
    {
        _plugin = plugin;
        Size = new Vector2(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("LazyCrafter scaffold - nothing to see yet.");
        ImGui.TextDisabled($"core {Core.CoreInfo.Version}, config v{_plugin.Config.Version}");
    }
}
