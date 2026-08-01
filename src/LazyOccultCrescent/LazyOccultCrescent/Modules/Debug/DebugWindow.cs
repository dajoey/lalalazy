using LazyOccultCrescent.Data;
using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.Debug;

#if DEBUG_BUILD
[OcelotWindow]
#endif
public class DebugWindow(Plugin priamryPlugin, Config config) : OcelotWindow(priamryPlugin, config)
{
    protected override void Render(RenderContext context)
    {
        if (!ZoneData.IsInOccultCrescent())
        {
            ImGui.TextUnformatted(I18N.T("generic.label.not_in_zone"));
            return;
        }

        if (Plugin.Modules.TryGetModule<DebugModule>(out var module) && module != null)
        {
            module.DrawPanels();
        }
    }

    protected override string GetWindowName()
    {
        return "OCH Debug";
    }
}
