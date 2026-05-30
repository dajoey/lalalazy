using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace LazyFateAutomation.Helpers.Utils;

/// <summary>
/// A <see cref="Window"/> that can be minimised via a title bar button. When minimised, only minimal content is shown
/// and the window size is reduced to <see cref="MinimisedSize"/>; when restored, the previous size is applied.
/// </summary>
public abstract class MinimisableWindow : Window {
    private readonly TitleBarButton _minimiseBtn;
    private readonly ImGuiWindowFlags _expandedFlags;
    private Vector2? _savedSize;

    protected bool Minimised { get; private set; }

    /// <summary>Width is overridden by <see cref="MinimisedContentWidth"/> when that is set to a positive value</summary>
    protected virtual Vector2 MinimisedSize => new(400, 80);

    /// <summary>Set from <see cref="DrawContent"/> when minimised to the measured content width; the base uses it for the next frame's window width. Leave 0 to use <see cref="MinimisedSize"/> width.</summary>
    protected float MinimisedContentWidth { get; set; }

    protected MinimisableWindow(string title, ImGuiWindowFlags flags = ImGuiWindowFlags.None) : base(title, flags) {
        _expandedFlags = flags;
        _minimiseBtn = new TitleBarButton {
            Icon = FontAwesomeIcon.Minus,
            IconOffset = new Vector2(1.5f, 1),
            Priority = int.MinValue,
            Click = _ => {
                Minimised = !Minimised;
                _minimiseBtn!.Icon = Minimised ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.Minus;
                if (!Minimised && _savedSize is { } saved) {
                    Size = saved;
                    SizeCondition = ImGuiCond.Always;
                }
            },
            ShowTooltip = () => {
                using var _ = ImRaii.Tooltip();
                ImGui.Text(Minimised ? ExpandTooltipText : MinimiseTooltipText);
            },
            AvailableClickthrough = true,
        };
        TitleBarButtons.Add(_minimiseBtn);
    }

    protected virtual string MinimiseTooltipText => "Minimal View";
    protected virtual string ExpandTooltipText => "Expanded View";

    protected abstract void DrawContent(bool minimised);

    public override void Draw() {
        if (Minimised) {
            var w = MinimisedContentWidth > 0 ? MinimisedContentWidth : MinimisedSize.X;
            Size = new Vector2(w, MinimisedSize.Y);
            SizeCondition = ImGuiCond.Always;
            Flags = _expandedFlags | ImGuiWindowFlags.NoResize;
            DrawContent(minimised: true);
            return;
        }

        Flags = _expandedFlags;
        _savedSize = ImGui.GetWindowSize();
        SizeCondition = ImGuiCond.FirstUseEver;
        DrawContent(minimised: false);
    }
}
