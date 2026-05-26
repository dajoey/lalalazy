using System;
using System.Numerics;
using System.Linq;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;

namespace LazyFATEAutomator;

public class FATEAutomatorWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly string[] _availableCriteria = { "HasTwistOfFate", "Progress", "HasBonus", "Distance" };

    public FATEAutomatorWindow(Plugin plugin) : base("Lazy FATE Automator###LazyFATEAutomatorMain")
    {
        _plugin = plugin;
        Size = new Vector2(480, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("LazyFateAutomatorTabs"))
        {
            if (ImGui.BeginTabItem("Dashboard"))
            {
                DrawDashboard();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Active FATEs"))
            {
                DrawActiveFates();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawDashboard()
    {
        ImGui.Spacing();

        // Core Automator Toggle
        bool isEnabled = _plugin.StateController.IsEnabled;
        if (ImGui.Checkbox("Enable Auto-FATE Farming", ref isEnabled))
        {
            if (isEnabled) _plugin.StateController.Start();
            else _plugin.StateController.Stop();
        }

        ImGui.Separator();
        ImGui.Spacing();

        // State & Status
        ImGui.Text("Automator State: ");
        ImGui.SameLine();
        var state = _plugin.StateController.State;
        Vector4 stateColor = state switch
        {
            GrindState.Idle => ImGuiColors.DalamudGrey,
            GrindState.WaitingForFates => ImGuiColors.DalamudOrange,
            GrindState.WaitingForFollowUp => ImGuiColors.ParsedPurple,
            GrindState.BetweenFates => ImGuiColors.DalamudYellow,
            GrindState.SwapZones => ImGuiColors.ParsedPink,
            GrindState.Engaging => ImGuiColors.HealerGreen,
            GrindState.Unconscious => ImGuiColors.DalamudRed,
            _ => ImGuiColors.DalamudWhite
        };
        ImGui.TextColored(stateColor, state.ToString());

        ImGui.Text("Current Status: ");
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudWhite, _plugin.StateController.Status);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Active Target FATE Details
        ImGui.TextColored(ImGuiColors.DalamudWhite, "Active Target FATE:");
        var activeFate = _plugin.FatesSolver.ActiveTarget;
        if (activeFate != null)
        {
            ImGui.Indent();
            ImGui.Text($"Name: {activeFate.Name}");
            ImGui.Text($"ID: {activeFate.FateId} | Level: {activeFate.Level}");
            ImGui.Text($"Progress: {activeFate.Progress}%");
            int timeRemaining = (int)activeFate.TimeRemaining;
            ImGui.Text($"Time Remaining: {timeRemaining / 60}m {timeRemaining % 60}s");
            ImGui.Unindent();
        }
        else
        {
            ImGui.Indent();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "No active target FATE.");
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Session Performance Metrics
        ImGui.TextColored(ImGuiColors.DalamudWhite, "Session Statistics:");
        ImGui.Indent();
        ImGui.Text($"Completed FATEs: {_plugin.StateController.CompletedFatesCount}");
        
        var duration = DateTime.Now - _plugin.StateController.SessionStartTime;
        string durationStr = _plugin.StateController.IsEnabled 
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : "0h 0m 0s";
        ImGui.Text($"Session Duration: {durationStr}");
        ImGui.Unindent();
    }

    private void DrawActiveFates()
    {
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudWhite, "Sorted List of Available FATEs in Current Zone:");
        ImGui.Spacing();

        var sortedFates = _plugin.FatesSolver.GetSortedAvailableFates().ToList();
        if (sortedFates.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "No FATEs in the zone match the filter configuration.");
            return;
        }

        if (ImGui.BeginTable("ActiveFatesTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Bonus", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();

            foreach (var fate in sortedFates)
            {
                ImGui.TableNextRow();
                
                // Name
                ImGui.TableNextColumn();
                ImGui.Text(fate.Name.ToString());
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"FATE ID: {fate.FateId}\nLevel: {fate.Level}\nDuration: {fate.TimeRemaining}s");
                }

                // Progress
                ImGui.TableNextColumn();
                ImGui.Text($"{fate.Progress}%");

                // Bonus
                ImGui.TableNextColumn();
                bool hasBonus = _plugin.FatesSolver.HasBonus(fate.FateId);
                if (hasBonus) ImGui.TextColored(ImGuiColors.HealerGreen, "YES");
                else ImGui.TextColored(ImGuiColors.DalamudGrey, "No");

                // Distance
                ImGui.TableNextColumn();
                float dist = _plugin.Navigation.GetDistanceTo(fate.Position);
                ImGui.Text($"{dist:F1}y");

                // Actions
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Blacklist###BL_{fate.FateId}"))
                {
                    _plugin.Config.BlacklistedFateIds.Add(fate.FateId);
                    _plugin.SaveConfig();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawSettings()
    {
        ImGui.Spacing();
        bool configChanged = false;

        // FATE Threshold Settings
        ImGui.TextColored(ImGuiColors.DalamudWhite, "FATE Filters & Limits:");
        
        int minTime = _plugin.Config.MinTimeRemaining;
        ImGui.SetNextItemWidth(120);
        if (ImGui.DragInt("Min Time Remaining (s)", ref minTime, 5, 30, 600))
        {
            _plugin.Config.MinTimeRemaining = minTime;
            configChanged = true;
        }

        int maxProg = _plugin.Config.MaxProgress;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("Max Progress (%)", ref maxProg, 10, 100))
        {
            _plugin.Config.MaxProgress = maxProg;
            configChanged = true;
        }

        int maxDur = _plugin.Config.MaxDuration;
        ImGui.SetNextItemWidth(120);
        if (ImGui.DragInt("Max Duration (s)", ref maxDur, 10, 300, 1800))
        {
            _plugin.Config.MaxDuration = maxDur;
            configChanged = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Automation Rule Toggles
        ImGui.TextColored(ImGuiColors.DalamudWhite, "Automation Parameters:");

        bool swapZones = _plugin.Config.SwapZones;
        if (ImGui.Checkbox("Automatically Swap Zones when Dry", ref swapZones))
        {
            _plugin.Config.SwapZones = swapZones;
            configChanged = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Uses lifestream to teleport to random zone in same expansion when no FATEs are active.");
        }

        bool autoSync = _plugin.Config.AutoSyncLevel;
        if (ImGui.Checkbox("Automatically Sync Level", ref autoSync))
        {
            _plugin.Config.AutoSyncLevel = autoSync;
            configChanged = true;
        }

        bool yokai = _plugin.Config.YokaiGrindMode;
        if (ImGui.Checkbox("Yokai Watch Grind Mode", ref yokai))
        {
            _plugin.Config.YokaiGrindMode = yokai;
            configChanged = true;
        }

        bool relic = _plugin.Config.RelicGrindMode;
        if (ImGui.Checkbox("Relic Grind Mode", ref relic))
        {
            _plugin.Config.RelicGrindMode = relic;
            configChanged = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Blacklist Management
        ImGui.TextColored(ImGuiColors.DalamudWhite, $"FATE Blacklist ({_plugin.Config.BlacklistedFateIds.Count} items):");
        if (_plugin.Config.BlacklistedFateIds.Count > 0)
        {
            ImGui.Spacing();
            if (ImGui.Button("Clear Entire Blacklist"))
            {
                _plugin.Config.BlacklistedFateIds.Clear();
                _plugin.SaveConfig();
            }

            ImGui.Spacing();
            if (ImGui.BeginChild("BlacklistChild", new Vector2(0, 120), true))
            {
                uint[] blacklistedIds = _plugin.Config.BlacklistedFateIds.ToArray();
                foreach (uint bid in blacklistedIds)
                {
                    if (ImGui.SmallButton($"Remove###RBL_{bid}"))
                    {
                        _plugin.Config.BlacklistedFateIds.Remove(bid);
                        _plugin.SaveConfig();
                    }
                    ImGui.SameLine();
                    ImGui.Text($"FATE ID: {bid}");
                }
                ImGui.EndChild();
            }
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Blacklist is currently empty.");
        }

        if (configChanged)
        {
            _plugin.SaveConfig();
        }
    }

    public void Dispose()
    {
    }
}
