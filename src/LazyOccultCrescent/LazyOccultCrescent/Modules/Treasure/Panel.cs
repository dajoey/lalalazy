using System.Numerics;
using ECommons.GameHelpers;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Treasure;

public class Panel
{
    public void Draw(TreasureModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");

        OcelotUi.Indent(() =>
        {
            DrawActiveChests(module);

            if (module.Treasures.Count <= 0)
            {
                ImGui.TextUnformatted(module.T("panel.none"));
                return;
            }

            foreach (var treasure in module.Treasures)
            {
                if (!treasure.IsValid())
                {
                    continue;
                }

                var pos = treasure.GetPosition();

                ImGui.TextUnformatted($"{treasure.GetName()}");
                OcelotUi.Indent(() =>
                {
                    ImGui.TextUnformatted($"({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
                    ImGui.TextUnformatted($"({Vector3.Distance(Player.Position, pos)})");
                });
            }
        });
    }

    private void DrawActiveChests(TreasureModule module)
    {
        if (!module.Tracker.CountInitialised)
        {
            return;
        }

        OcelotUi.LabelledValue(module.T("panel.active_bronze.label"), $"{module.Tracker.BronzeChests}/30");
        if (module.Config.ShowPercentageActiveTreasureCount)
        {
            var percentage = module.Tracker.BronzeChests / 30f * 100f;
            ImGui.SameLine();
            ImGui.TextUnformatted($"({percentage:f2}%)");
        }

        OcelotUi.LabelledValue(module.T("panel.active_silver.label"), $"{module.Tracker.SilverChests}/8");
        if (module.Config.ShowPercentageActiveTreasureCount)
        {
            var percentage = module.Tracker.SilverChests / 8f * 100f;
            ImGui.SameLine();
            ImGui.TextUnformatted($"({percentage:f2}%)");
        }
    }
}
