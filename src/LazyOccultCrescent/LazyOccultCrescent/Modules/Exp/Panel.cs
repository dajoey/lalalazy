using Dalamud.Interface;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Exp;

public class Panel
{
    public void Draw(ExpModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            if (ImGuiEx.IconButton(FontAwesomeIcon.Redo, $"Reset##Exp"))
            {
                module.tracker.Reset();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(module.T("panel.exp.label"));

            ImGui.SameLine();
            ImGui.TextUnformatted(module.tracker.GetExpPerHour().ToString("F2"));
        });
    }
}
