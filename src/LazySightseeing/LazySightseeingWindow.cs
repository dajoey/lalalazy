using System;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace LazySightseeing;

public sealed class LazySightseeingWindow : Window
{
    private readonly Plugin _plugin;

    public LazySightseeingWindow(Plugin plugin) : base("Lazy Sightseeing##lazysight")
    {
        _plugin = plugin;
        Size = new Vector2(720, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var activeTarget = _plugin.Automation.CurrentTarget;
        var isRunning = _plugin.Automation.IsRunning;
        var state = _plugin.Automation.State;

        // Get current Eorzea Time details
        var et = WeatherService.GetEorzeaTime();
        int etHour = et.Hour;
        int etMin = et.Minute;
        
        ImGui.Columns(2, "topcols", false);
        ImGui.SetColumnWidth(0, 320);

        // Column 1: Controls
        ImGui.TextUnformatted("Lazy Sightseeing Controls");
        ImGui.Spacing();

        if (isRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.1f, 0.15f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.2f, 0.25f, 1.0f));
            if (ImGui.Button("STOP AUTOMATION##stop", new Vector2(300, 45)))
            {
                _plugin.Automation.Stop();
            }
            ImGui.PopStyleColor(2);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.6f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.7f, 0.3f, 1.0f));
            if (ImGui.Button("START AUTOMATION##start", new Vector2(300, 45)))
            {
                _plugin.Automation.Start();
            }
            ImGui.PopStyleColor(2);
        }

        ImGui.Spacing();
        
        var skip = c.SkipIfWindowNotOpen;
        if (ImGui.Checkbox("Skip if window is closed", ref skip))
        {
            c.SkipIfWindowNotOpen = skip;
            _plugin.SaveConfig();
        }


        ImGui.SetNextItemWidth(150);
        var inn = c.DefaultInn;
        var inns = new[] { "Gridania", "Limsa", "Ul'dah", "Kugane" };
        int innIdx = Array.IndexOf(inns, inn);
        if (innIdx < 0) innIdx = 0;
        if (ImGui.Combo("Default Inn", ref innIdx, inns, inns.Length))
        {
            c.DefaultInn = inns[innIdx];
            _plugin.SaveConfig();
        }

        ImGui.NextColumn();

        // Column 2: Status details
        ImGui.TextUnformatted("Current Status");
        ImGui.Spacing();

        ImGui.TextUnformatted($"Eorzea Time: {etHour:D2}:{etMin:D2}");
        
        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer != null)
        {
            var zoneWeather = WeatherService.GetCurrentWeatherName(Svc.ClientState.TerritoryType);
            ImGui.TextUnformatted($"Current Weather: {zoneWeather}");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted($"State: {state}");
        
        if (activeTarget != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), $"Objective: {activeTarget.Name}");
            ImGui.TextUnformatted($"Zone: {activeTarget.Aetheryte} ({activeTarget.TerritoryType})");
            ImGui.TextUnformatted($"Emote: /{activeTarget.Emote}");
        }
        else if (isRunning)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Objective: Waiting for weather/time windows...");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Objective: Idle");
        }

        ImGui.Columns(1);
        ImGui.Separator();
        ImGui.Spacing();

        // Main List Checklist
        ImGui.TextUnformatted("Sightseeing Checklist");
        ImGui.Spacing();

        if (ImGui.Button("Select All Uncompleted##selall"))
        {
            c.SelectedSightIds.Clear();
            foreach (var s in SightseeingDatabase.Sights)
            {
                if (!AutomationService.IsSightCompleted(s.Id))
                {
                    c.SelectedSightIds.Add(s.Id);
                }
            }
            _plugin.SaveConfig();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear Selection##clear"))
        {
            c.SelectedSightIds.Clear();
            _plugin.SaveConfig();
        }

        ImGui.Spacing();

        if (ImGui.BeginTable("sightseeing_table", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 260)))
        {
            ImGui.TableSetupColumn("Sel", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableSetupColumn("ID & Name", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupColumn("Emote", ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Requirements", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();

            foreach (var sight in SightseeingDatabase.Sights)
            {
                if (AutomationService.IsSightCompleted(sight.Id)) continue;

                bool isSelected = c.SelectedSightIds.Contains(sight.Id);
                bool isWindowOpen = _plugin.Automation.IsWindowOpen(sight);

                ImGui.TableNextRow();

                // Column 1: Checkbox
                ImGui.TableNextColumn();
                var sel = isSelected;
                if (ImGui.Checkbox($"##sel_{sight.Id}", ref sel))
                {
                    if (sel) c.SelectedSightIds.Add(sight.Id);
                    else c.SelectedSightIds.Remove(sight.Id);
                    _plugin.SaveConfig();
                }

                // Column 2: ID & Name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sight.Name);

                // Column 3: Zone
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sight.Aetheryte);

                // Column 4: Emote
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"/{sight.Emote}");

                // Column 5: Requirements
                ImGui.TableNextColumn();
                string req = "";
                if (sight.Weathers != null && sight.Weathers.Count > 0)
                {
                    req += string.Join("/", sight.Weathers);
                }
                else
                {
                    req += "Any Weather";
                }
                if (!string.IsNullOrEmpty(sight.TimeWindow))
                {
                    req += $" @ {sight.TimeWindow}";
                }
                ImGui.TextUnformatted(req);

                // Column 6: Status
                ImGui.TableNextColumn();
                if (isWindowOpen)
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Active Now!");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "Locked");
                }

                // Column 7: Actions (Go)
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Go##go_{sight.Id}"))
                {
                    // Set checklist selection to ONLY this sight for quick single execution
                    c.SelectedSightIds.Clear();
                    c.SelectedSightIds.Add(sight.Id);
                    _plugin.SaveConfig();
                    _plugin.Automation.Start();
                }
            }
            ImGui.EndTable();
        }
    }
}
