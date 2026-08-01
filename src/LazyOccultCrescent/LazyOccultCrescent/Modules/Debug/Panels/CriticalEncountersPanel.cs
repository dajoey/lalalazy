using System;
using System.Linq;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.CriticalEncounters;
using LazyOccultCrescent.Modules.Teleporter;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace LazyOccultCrescent.Modules.Debug.Panels;

public class CriticalEncountersPanel : Panel
{
    public override string GetName()
    {
        return "Critical Encounters";
    }

    public override unsafe void Render(DebugModule module)
    {
        OcelotUi.Title("Critical Encounters:");
        OcelotUi.Indent(() =>
        {
            foreach (var data in EventData.CriticalEncounters.Values)
            {
                var ev = module.GetModule<CriticalEncountersModule>().CriticalEncounters[data.Id];

                ImGui.TextUnformatted(ev.Name.ToString());

                if (ev.State == DynamicEventState.Inactive)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"(Inactive)");
                }

                if (ev.State == DynamicEventState.Register)
                {
                    var start = DateTimeOffset.FromUnixTimeSeconds(ev.StartTimestamp).DateTime;
                    var timeUntilStart = start - DateTime.UtcNow;
                    var formattedTime = $"{timeUntilStart.Minutes:D2}:{timeUntilStart.Seconds:D2}";

                    ImGui.SameLine();
                    ImGui.TextUnformatted($"(Preparing: {formattedTime})");
                }

                if (ev.State == DynamicEventState.Warmup)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"(Starting)");
                }

                if (ev.State == DynamicEventState.Battle)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"({ev.Progress}%)");
                }

                if (module.TryGetModule<TeleporterModule>(out var teleporter) && teleporter!.IsReady())
                {
                    var start = ev.MapMarker.Position;

                    teleporter.teleporter.Button(data.Aethernet, start, ev.Name.ToString(), $"ce_{data.Id}", data);
                }

                OcelotUi.Indent(() => EventIconRenderer.Drops(data, module.PluginConfig.EventDropConfig));

                if (data.Id != EventData.CriticalEncounters.Keys.Max())
                {
                    OcelotUi.VSpace();
                }

                if (ImGui.CollapsingHeader($"Event Data##{data.Id}"))
                {
                    PrintEvent(ev);
                }

                if (ImGui.CollapsingHeader($"Map Marker Data##{data.Id}"))
                {
                    PrintMapMarker(ev.MapMarker);
                }
            }
        });
    }

    private unsafe void PrintEvent(DynamicEvent ev)
    {
        OcelotUi.Title("Name Offset:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.NameOffset.ToString());

        OcelotUi.Title("Description Offset:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.DescriptionOffset.ToString());

        OcelotUi.Title("LGB Event Object:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.LGBEventObject.ToString());

        OcelotUi.Title("LGB Map Range:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.LGBMapRange.ToString());

        OcelotUi.Title("Quest (RowId):");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.Quest.ToString());

        OcelotUi.Title("Announce (RowId):");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.Announce.ToString());

        // OcelotUi.Title("Unknown0:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown0.ToString());
        //
        // OcelotUi.Title("Unknown1:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown1.ToString());
        //
        // OcelotUi.Title("Unknown6:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown6.ToString());
        //
        // OcelotUi.Title("Unknown7:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown7.ToString());
        //
        // OcelotUi.Title("Unknown2:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown2.ToString());

        OcelotUi.Title("Event Type (RowId):");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.EventType.ToString());

        OcelotUi.Title("Enemy Type (RowId):");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.EnemyType.ToString());

        OcelotUi.Title("Max Participants:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.MaxParticipants.ToString());

        // OcelotUi.Title("Radius?:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown4.ToString());

        // OcelotUi.Title("Unknown5:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown5.ToString());

        OcelotUi.Title("Single Battle (RowId):");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.SingleBattle.ToString());

        // OcelotUi.Title("Unknown8:");
        // ImGui.SameLine();
        // ImGui.TextUnformatted(ev.Unknown8.ToString());

        OcelotUi.Title("Start Timestamp:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.StartTimestamp.ToString());

        OcelotUi.Title("Seconds Left:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.SecondsLeft.ToString());

        OcelotUi.Title("Seconds Duration:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.SecondsDuration.ToString());

        OcelotUi.Title("Dynamic Event Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.DynamicEventId.ToString());

        OcelotUi.Title("Dynamic Event Type:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.DynamicEventType.ToString());

        OcelotUi.Title("State:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.State.ToString());

        OcelotUi.Title("Participants:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.Participants.ToString());

        OcelotUi.Title("Progress:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.Progress.ToString());

        OcelotUi.Title("Name:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.Name.ToString());

        OcelotUi.Title("Description:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.Description.ToString());

        OcelotUi.Title("Icon Objective 0:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.IconObjective0.ToString());

        OcelotUi.Title("Max Participants 2:");
        ImGui.SameLine();
        ImGui.TextUnformatted(ev.MaxParticipants2.ToString());

        OcelotUi.Title("Map Marker:");
        ImGui.SameLine();
        ImGui.TextUnformatted(
            $"X: {ev.MapMarker.Position.X}, Y: {ev.MapMarker.Position.Y}, IconId: {ev.MapMarker.IconId}"); // example, adjust fields accordingly

        OcelotUi.Title("Event Container Pointer:");
        ImGui.SameLine();
        ImGui.TextUnformatted(((IntPtr)ev.EventContainer).ToString("X"));
    }


    private unsafe void PrintMapMarker(MapMarkerData marker)
    {
        OcelotUi.Title("Level Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.LevelId.ToString());

        OcelotUi.Title("Objective Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.ObjectiveId.ToString());

        OcelotUi.Title("Tooltip String:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.TooltipString != null ? marker.TooltipString->ToString() : "null");

        OcelotUi.Title("Icon Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.IconId.ToString());

        OcelotUi.Title("Position X:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.Position.X.ToString("F2"));

        OcelotUi.Title("Position Y:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.Position.Y.ToString("F2"));

        OcelotUi.Title("Position Z:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.Position.Z.ToString("F2"));

        OcelotUi.Title("Radius:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.Radius.ToString("F2"));

        OcelotUi.Title("Map Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.MapId.ToString());

        OcelotUi.Title("Place Name Zone Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.PlaceNameZoneId.ToString());

        OcelotUi.Title("Place Name Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.PlaceNameId.ToString());

        OcelotUi.Title("End Timestamp:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.EndTimestamp.ToString());

        OcelotUi.Title("Recommended Level:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.RecommendedLevel.ToString());

        OcelotUi.Title("Territory Type Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.TerritoryTypeId.ToString());

        OcelotUi.Title("Data Id:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.DataId.ToString());

        OcelotUi.Title("Marker Type:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.MarkerType.ToString());

        OcelotUi.Title("Event State:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.EventState.ToString());

        OcelotUi.Title("Flags:");
        ImGui.SameLine();
        ImGui.TextUnformatted(marker.Flags.ToString());
    }
}
