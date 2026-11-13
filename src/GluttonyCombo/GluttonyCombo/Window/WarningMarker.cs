using Dalamud.Interface;
using Dalamud.Interface.Components;
using LalaUiKit;

namespace GluttonyCombo.Window;

public static class WarningMarkerComponent
{
    public static void WarningMarker(string warningText) =>
        ImGuiComponents.HelpMarker(
            warningText,
            FontAwesomeIcon.ExclamationTriangle,
            Theme.StateAmber with
            {
                W = Theme.StateAmber.W * 0.85f,
            });
}