using Dalamud.Interface;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Currency;

public class Panel
{
    public void Draw(CurrencyModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            if (ImGui.BeginTable("CurrencyData##OCH", 3, ImGuiTableFlags.SizingFixedFit))
            {
                // Silver
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                if (ImGuiEx.IconButton(FontAwesomeIcon.Redo, "Reset##Silver"))
                {
                    module.Tracker.ResetSilver();
                }

                ImGui.TableNextColumn();
                OcelotUi.Title(module.T("panel.silver.label"));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(module.Tracker.GetSilverPerHour().ToString("F2"));

                // Gold
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                if (ImGuiEx.IconButton(FontAwesomeIcon.Redo, "Reset##Gold"))
                {
                    module.Tracker.ResetGold();
                }

                ImGui.TableNextColumn();
                OcelotUi.Title(module.T("panel.gold.label"));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(module.Tracker.GetGoldPerHour().ToString("F2"));

                ImGui.EndTable();
            }
        });
    }
}
