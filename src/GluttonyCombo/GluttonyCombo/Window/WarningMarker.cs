using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;

namespace GluttonyCombo.Window;

public static class WarningMarkerComponent
{
    public static void WarningMarker(string warningText) =>
        ImGuiComponents.HelpMarker(
            warningText,
            FontAwesomeIcon.ExclamationTriangle,
            ImGuiColors.DalamudYellow with
            {
                W = ImGuiColors.DalamudYellow.W * 0.85f,
            });
}