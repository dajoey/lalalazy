using LazyOccultCrescent.Data;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.ForkedTower;

public class Panel
{
    public void Draw(ForkedTowerModule module)
    {
        if (!ZoneData.IsInForkedTower())
        {
            return;
        }

        OcelotUi.Title("Forked Tower:");
        OcelotUi.Indent(() =>
        {
            var state = OcelotUi.LabelledValue("Tower ID", module.TowerRun.Hash);
            if (state == UiState.Hovered)
            {
                ImGui.SetTooltip("This is unique to you.");
            }
        });
    }
}
