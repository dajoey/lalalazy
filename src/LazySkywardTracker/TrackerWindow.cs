using System;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace LazySkywardTracker;

public sealed class TrackerWindow : Window
{
    private readonly Plugin _plugin;

    public TrackerWindow(Plugin plugin) : base("Lazy Skyward Tracker##lazysky")
    {
        _plugin = plugin;
        Size = new Vector2(550, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        // Overall progress calculations
        uint totalCurrent = 0;
        uint totalMax = 5500000;
        int completedCount = 0;

        foreach (var id in Plugin.AchievementIds)
        {
            if (Plugin.IsAchievementCompleted(id))
            {
                totalCurrent += 500000;
                completedCount++;
            }
            else if (Plugin.ProgressCache.TryGetValue(id, out var progress))
            {
                totalCurrent += Math.Min(progress.Current, 500000);
                if (progress.Current >= 500000)
                {
                    completedCount++;
                }
            }
        }

        // Header / Overall Progress
        ImGui.TextUnformatted("Overall Pteranodon Progress");
        float overallFraction = (float)totalCurrent / totalMax;
        ImGui.ProgressBar(overallFraction, new Vector2(-1, 25), $"{totalCurrent:N0} / {totalMax:N0} ({overallFraction * 100:F2}%)");
        
        ImGui.Spacing();
        ImGui.TextUnformatted($"Jobs Completed: {completedCount} / 11");
        ImGui.SameLine(ImGui.GetWindowWidth() - 150);
        
        if (ImGui.Button("Refresh Points##refresh", new Vector2(130, 25)))
        {
            _plugin.RequestAllProgress();
        }

        ImGui.Separator();
        ImGui.Spacing();

        // Jobs Table
        if (ImGui.BeginTable("skyward_jobs_table", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, -1)))
        {
            ImGui.TableSetupColumn("Job / Class", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Achievement Name", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();

            foreach (var id in Plugin.AchievementIds)
            {
                var info = Plugin.SkywardAchievements[id];
                bool isCompleted = Plugin.IsAchievementCompleted(id);
                uint current = 0;
                uint max = 500000;

                if (isCompleted)
                {
                    current = 500000;
                }
                else if (Plugin.ProgressCache.TryGetValue(id, out var progress))
                {
                    current = progress.Current;
                    max = progress.Max > 0 ? progress.Max : 500000;
                }

                ImGui.TableNextRow();

                // Column 1: Job / Class
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.Job);

                // Column 2: Achievement Name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.AchName);

                // Column 3: Progress Bar
                ImGui.TableNextColumn();
                float fraction = (float)current / max;
                
                // Color progress bar depending on completion status
                if (isCompleted || current >= max)
                {
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.1f, 0.6f, 0.2f, 1.0f)); // Green
                    ImGui.ProgressBar(1.0f, new Vector2(-1, 0), "Completed");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.85f, 0.45f, 0.0f, 1.0f)); // Orange/Yellow
                    ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"{fraction * 100:F1}%");
                    ImGui.PopStyleColor();
                }

                // Column 4: Points
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{current:N0} / {max:N0}");
            }
            ImGui.EndTable();
        }
    }
}
