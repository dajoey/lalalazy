using System;
using System.Linq;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.Teleporter;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Fates;

public class Panel
{
    public void Draw(FatesModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            if (module.tracker.Fates.Count <= 0)
            {
                ImGui.TextUnformatted(module.T("panel.none"));
                return;
            }

            // Hoisted: Dictionary.ValueCollection has no IList fast path, so calling
            // Last() inside the loop fully re-enumerated on every iteration.
            var lastFate = module.fates.Values.LastOrDefault();

            foreach (var fate in module.fates.Values)
            {
                if (!ZoneData.IsInOccultCrescent())
                {
                    module.fates.Clear();
                    return;
                }

                try
                {
                    ImGui.TextUnformatted($"{fate.Name} ({fate.CurrentProgress}%)");
                }
                catch (AccessViolationException)
                {
                    continue;
                }


                var estimate = fate.Progress.EstimateTimeToCompletion();
                if (estimate != null)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"({module.T("panel.estimated")} {estimate.Value:mm\\:ss})");
                }


                if (module.TryGetModule<TeleporterModule>(out var teleporter) && teleporter!.IsReady())
                {
                    // GetAethernet(), not Data.Aethernet: the raw field is null for any event
                    // without a curated hint, which is all of North Horn. The accessor
                    // falls back to the nearest shard.
                    teleporter.teleporter.Button(fate.GetAethernet(), fate.StartPosition, fate.Name, $"fate_{fate.Id}", fate.Data);
                }

                OcelotUi.Indent(() => EventIconRenderer.Drops(fate.Data, module.PluginConfig.EventDropConfig));

                if (!fate.Equals(lastFate))
                {
                    OcelotUi.VSpace();
                }
            }
        });
    }
}
