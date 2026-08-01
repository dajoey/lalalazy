using System.Linq;
using ECommons.DalamudServices;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Debug.Panels;

public class StatusPanel : Panel
{
    public override string GetName()
    {
        return "Statuses";
    }

    public override void Render(DebugModule module)
    {
        var data = Svc.Data.GetExcelSheet<Status>();


        OcelotUi.Title("Statuses:");
        OcelotUi.Indent(() =>
        {
            foreach (var s in Svc.Objects.LocalPlayer!.StatusList)
            {
                ImGui.TextUnformatted($"{data.Where(r => r.RowId == s.StatusId).First().Name} ({s.StatusId})");
            }
        });
    }
}
