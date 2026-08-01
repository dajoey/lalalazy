using System.Numerics;
using LazyOccultCrescent.Data;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Automator;

[OcelotWindow]
public class AutomatorWindow(Plugin _plugin, Config _config) : OcelotWindow(_plugin, _config)
{
    public override void PostInitialize()
    {
        base.PostInitialize();

        TitleBarButtons.Add(new TitleBarButton
        {
            Click = (m) =>
            {
                if (m != ImGuiMouseButton.Left)
                {
                    return;
                }

                AutomatorModule.ToggleIllegalMode(Plugin);
            },
            Icon = FontAwesomeIcon.Skull,
            IconOffset = new Vector2(2, 2),
            ShowTooltip = () => ImGui.SetTooltip("Toggle Illegal Mode"),
        });
    }

    protected override void Render(RenderContext context)
    {
        if (!ZoneData.IsInOccultCrescent())
        {
            ImGui.TextUnformatted(I18N.T("generic.label.not_in_zone"));
            return;
        }

        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        if (!automator.IsEnabled)
        {
            ImGui.TextUnformatted("Illegal Mode is not enabled.");
            return;
        }

        automator.panel.Draw(automator);
    }

    protected override string GetWindowName()
    {
        return Plugin.Modules.GetModule<AutomatorModule>().T("panel.lens.title");
    }
}
