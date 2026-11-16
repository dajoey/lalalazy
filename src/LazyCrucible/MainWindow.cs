using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyCrucible;

/// <summary> Main settings window: the shared structural shell and the familiar-selection advisor. </summary>
internal sealed class MainWindow : Window
{
    private readonly Plugin _plugin;
    private readonly CrucibleShell _shell;

    public MainWindow(Plugin plugin, CrucibleShell shell) : base("LazyCrucible###LazyCrucibleMain")
    {
        _plugin = plugin;
        _shell = shell;
        _shell.CloseRequested = () => IsOpen = false;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300), MaximumSize = new Vector2(1400, 1400) };
        Size = new Vector2(1280, 840);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // The shell lays out its own gutters from the window edge, so the window itself carries no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw() => _shell.Draw(_plugin);
}
