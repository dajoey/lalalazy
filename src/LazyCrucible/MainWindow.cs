using System.Numerics;
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
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300), MaximumSize = new Vector2(1400, 1400) };
    }

    public override void Draw() => _shell.Draw(_plugin);
}
