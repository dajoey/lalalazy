using Dalamud.Bindings.ImGui;
using Ocelot.Ui;
using Ocelot.Chain;

namespace LazyOccultCrescent.Modules.Debug.Panels;

public class ChainManagerPanel : Panel
{
    public override string GetName()
    {
        return "Chain Manager";
    }

    public override void Render(DebugModule module)
    {
        OcelotUi.Title("Chain Manager:");
        OcelotUi.Indent(() =>
        {
            var instances = ChainManager.Queues;
            OcelotUi.Title("# of instances:");
            ImGui.SameLine();
            ImGui.TextUnformatted(instances.Count.ToString());

            foreach (var pair in instances)
            {
                if (pair.Value.CurrentChain == null)
                {
                    continue;
                }

                OcelotUi.Title($"{pair.Key}:");
                OcelotUi.Indent(() =>
                {
                    var current = pair.Value.CurrentChain!;
                    OcelotUi.Title("Current Chain:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(current.Name);

                    OcelotUi.Title("Progress:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"{current.Progress * 100}%");

                    OcelotUi.Title("Queued Chains:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(pair.Value.QueueCount.ToString());
                });
            }
        });
    }
}
