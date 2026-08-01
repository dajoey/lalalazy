using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.StateManager;

public class Panel
{
    public bool Draw(StateManagerModule module)
    {
        if (!module.Config.ShowDebug)
        {
            return false;
        }

        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() => ImGui.TextUnformatted($"{module.T("panel.state.label")}: {module.GetStateText()}"));

        return true;
    }
}
