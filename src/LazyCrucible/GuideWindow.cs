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
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 260), MaximumSize = new Vector2(1400, 1600) };
        Size = new Vector2(960, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary> Follow the real focused fight and warned casts without inventing a focus.</summary>
    public void FollowFocus() => _shell.FollowFocus(this);

    public override void Draw() => _shell.Draw(_plugin);
}
