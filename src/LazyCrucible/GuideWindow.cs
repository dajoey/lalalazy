using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyCrucible;

/// <summary> Fight guide window: upcoming-fight guidance, board browsing, advisor and recent choices. </summary>
internal sealed class GuideWindow : Window
{
    private readonly Plugin _plugin;
    private readonly CrucibleShell _shell;

    public GuideWindow(Plugin plugin, CrucibleShell shell) : base("Crucible fight guide###LazyCrucibleGuide")
    {
        _plugin = plugin;
        _shell = shell;
        _shell.CloseRequested = () => IsOpen = false;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 440), MaximumSize = new Vector2(1400, 1600) };
        Size = new Vector2(1280, 840);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary> Follow the real focused fight and warned casts without inventing a focus.</summary>
    public void FollowFocus() => _shell.FollowFocus(this);

    // The shell lays out its own gutters from the window edge, so the window itself carries no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw() => _shell.Draw(_plugin);
}
