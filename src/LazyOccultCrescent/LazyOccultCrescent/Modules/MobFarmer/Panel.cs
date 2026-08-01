using System.Linq;
using Dalamud.Bindings.ImGui;
using Ocelot;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.MobFarmer;

public class Panel
{
    public void Draw(MobFarmerModule module)
    {
        OcelotUi.Title("Mob Farmer:");
        OcelotUi.Indent(() =>
        {
            if (ImGui.Button(module.Farmer.Running ? I18N.T("generic.label.stop") : I18N.T("generic.label.start")))
            {
                module.Farmer.Toggle(module);
            }

            if (module.Farmer.Running)
            {
                OcelotUi.LabelledValue("Phase", module.Farmer.StateMachine.State);
            }

            OcelotUi.LabelledValue("Not Engaged", module.Scanner.NotInCombat.Count());
            OcelotUi.LabelledValue("Engaged", module.Scanner.InCombat.Count());
        });
    }
}
