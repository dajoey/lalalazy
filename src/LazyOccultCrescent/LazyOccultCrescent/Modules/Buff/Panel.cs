using LazyOccultCrescent.Data;
using Dalamud.Interface;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Buff;

public class Panel
{
    public void Draw(BuffModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            var isNearKnowledgeCrystal = ZoneData.IsNearKnowledgeCrystal();
            var isQueued = module.BuffManager.IsQueued();

            if (ImGuiEx.IconButton(FontAwesomeIcon.Redo, "Button##ApplyBuffs", enabled: isNearKnowledgeCrystal && !isQueued))
            {
                module.BuffManager.QueueBuffs();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(module.T("panel.button.tooltip"));
            }
        });
    }
}
