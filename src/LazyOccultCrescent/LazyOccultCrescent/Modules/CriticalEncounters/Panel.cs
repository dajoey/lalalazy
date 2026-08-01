using System;
using System.Linq;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.Teleporter;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.CriticalEncounters;

public class Panel
{
    public void Draw(CriticalEncountersModule module)
    {
        OcelotUi.Title($"{module.T("panel.title")}:");
        OcelotUi.Indent(() =>
        {
            var active = module.CriticalEncounters.Values.Count(ev => ev.State != DynamicEventState.Inactive);
            if (active <= 0)
            {
                ImGui.TextUnformatted(module.T("panel.none"));
                return;
            }

            foreach (var ev in module.CriticalEncounters.Values)
            {
                if (!ZoneData.IsInOccultCrescent())
                {
                    module.CriticalEncounters.Clear();
                    return;
                }

                if (ev.EventType >= 4)
                {
                    HandleTower(ev, module);
                    continue;
                }

                if (ev.State == DynamicEventState.Inactive)
                {
                    continue;
                }

                if (!EventData.CriticalEncounters.TryGetValue(ev.DynamicEventId, out var data))
                {
                    continue;
                }

                ImGui.TextUnformatted(ev.Name.ToString());

                switch (ev.State)
                {
                    case DynamicEventState.Register:
                        {
                            var start = DateTimeOffset.FromUnixTimeSeconds(ev.StartTimestamp).DateTime;
                            var timeUntilStart = start - DateTime.UtcNow;
                            var formattedTime = $"{timeUntilStart.Minutes:D2}:{timeUntilStart.Seconds:D2}";

                            ImGui.SameLine();
                            ImGui.TextUnformatted($"({module.T("panel.register")}: {formattedTime})");
                            break;
                        }
                    case DynamicEventState.Warmup:
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"({module.T("panel.warmup")})");
                        break;
                    case DynamicEventState.Battle:
                        {
                            ImGui.SameLine();
                            ImGui.TextUnformatted($"({ev.Progress}%)");

                            if (module.Progress.TryGetValue(ev.DynamicEventId, out var progress))
                            {
                                var estimate = progress.EstimateTimeToCompletion();
                                if (estimate != null)
                                {
                                    ImGui.SameLine();
                                    ImGui.TextUnformatted($"({module.T("panel.estimated")} {estimate.Value:mm\\:ss})");
                                }
                            }

                            break;
                        }
                    case DynamicEventState.Inactive:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (ev.State != DynamicEventState.Register)
                {
                    OcelotUi.Indent(() => EventIconRenderer.Drops(data, module.PluginConfig.EventDropConfig));
                    continue;
                }

                if (module.TryGetModule<TeleporterModule>(out var teleporter) && teleporter!.IsReady())
                {
                    var start = ev.MapMarker.Position;

                    teleporter.teleporter.Button(data.Aethernet, start, ev.Name.ToString(), $"ce_{ev.DynamicEventId}", data);
                }

                OcelotUi.Indent(() => EventIconRenderer.Drops(data, module.PluginConfig.EventDropConfig));
            }
        });
    }


    private void HandleTower(DynamicEvent ev, CriticalEncountersModule module)
    {
        if (!module.Config.TrackForkedTower || ev.State == DynamicEventState.Battle)
        {
            return;
        }

        OcelotUi.Error("This feature is a work in progress.");

        if (ev.State == DynamicEventState.Inactive)
        {
            ImGui.TextUnformatted($"{ev.Name}:");

            var time = module.Tracker.TowerTimer.GetTimeToForkedTowerSpawn(ev.State);
            OcelotUi.Indent(() => { OcelotUi.LabelledValue("Forked Tower Spawn Estimate", $"{time:mm\\:ss}"); });
        }
        else
        {
            ImGui.TextUnformatted($"{ev.Name}:");

            var time = module.Tracker.TowerTimer.GetTimeRemainingToRegister(ev.State);
            OcelotUi.Indent(() => { OcelotUi.LabelledValue("Forked Tower Register", $"{time:mm\\:ss}"); });
        }

        OcelotUi.Indent(32, () =>
        {
            OcelotUi.LabelledValue("Critical Encounters completed", module.Tracker.TowerTimer.CriticalEncountersCompleted);
            OcelotUi.LabelledValue("Fates completed", module.Tracker.TowerTimer.FatesCompleted);
        });


        if (!TowerHelper.IsPlayerNearTower(TowerHelper.TowerType.Blood))
        {
            return;
        }

        OcelotUi.Indent(() =>
        {
            OcelotUi.LabelledValue("Players on Platform", TowerHelper.GetPlayersInTowerZone(TowerHelper.TowerType.Blood));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("This includes your character");
            }

            OcelotUi.LabelledValue("Players near Platform", TowerHelper.GetPlayersNearTowerZone(TowerHelper.TowerType.Blood));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("This includes your character");
            }
        });
    }
}
