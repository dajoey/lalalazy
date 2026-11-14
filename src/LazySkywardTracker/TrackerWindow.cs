using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace LazySkywardTracker;

public sealed class TrackerWindow : Window
{
    private readonly Plugin _plugin;

    // Pre-computed color constants
    private static readonly Vector4 GreenBar = new(0.1f, 0.6f, 0.2f, 1.0f);
    private static readonly Vector4 OrangeBar = new(0.85f, 0.45f, 0.0f, 1.0f);
    private static readonly Vector4 CyanTint = new(0.0f, 0.75f, 0.95f, 0.65f);
    private static readonly Vector4 CyanText = new(0.0f, 0.75f, 0.95f, 1.0f);
    private static readonly Vector4 DimText = new(0.6f, 0.6f, 0.6f, 1.0f);

    public TrackerWindow(Plugin plugin) : base("Lazy Skyward Tracker##lazysky")
    {
        _plugin = plugin;
        Size = new Vector2(550, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        // Get projected inventory points
        var projections = _plugin.Scanner.ScanInventory();

        // ── Overall progress calculations ────────────────────────────────
        uint totalCurrent = 0;
        uint totalMax = 5500000;
        uint totalInventoryPts = 0;
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

            if (projections.TryGetValue(id, out var proj))
                totalInventoryPts += proj.TotalPoints;
        }

        // ── Header / Overall Progress ────────────────────────────────────
        ImGui.TextUnformatted("Overall Pteranodon Progress");

        float overallFraction = (float)totalCurrent / totalMax;
        string overallText = totalInventoryPts > 0
            ? $"{totalCurrent:N0} (+{totalInventoryPts:N0}) / {totalMax:N0}"
            : $"{totalCurrent:N0} / {totalMax:N0}";

        ImGui.ProgressBar(overallFraction, new Vector2(-1, 25), overallText);

        // Draw projected overlay on overall bar
        if (totalInventoryPts > 0)
            DrawBarOverlay(overallFraction, (float)(totalCurrent + totalInventoryPts) / totalMax);

        if (ImGui.IsItemHovered() && totalInventoryPts > 0)
        {
            ImGui.BeginTooltip();
            ImGui.TextColored(CyanText, $"Inventory items add +{totalInventoryPts:N0} Skyward points total");
            ImGui.EndTooltip();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted($"Jobs Completed: {completedCount} / 11");
        ImGui.SameLine(ImGui.GetWindowWidth() - 150);

        if (ImGui.Button("Refresh Points##refresh", new Vector2(130, 25)))
        {
            _plugin.RequestAllProgress();
            _plugin.Scanner.InvalidateCache();
        }

        ImGui.Separator();
        ImGui.Spacing();

        // ── Jobs Table ───────────────────────────────────────────────────
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

                projections.TryGetValue(id, out var projection);
                uint invPts = projection?.TotalPoints ?? 0;

                ImGui.TableNextRow();

                // Column 1: Job / Class
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.Job);

                // Column 2: Achievement Name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.AchName);

                // Column 3: Progress Bar with projected overlay
                ImGui.TableNextColumn();
                float fraction = (float)current / max;

                if (isCompleted || current >= max)
                {
                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, GreenBar);
                    ImGui.ProgressBar(1.0f, new Vector2(-1, 0), "Completed");
                    ImGui.PopStyleColor();
                }
                else
                {
                    string barLabel = invPts > 0
                        ? $"{fraction * 100:F1}% (+{invPts:N0})"
                        : $"{fraction * 100:F1}%";

                    ImGui.PushStyleColor(ImGuiCol.PlotHistogram, OrangeBar);
                    ImGui.ProgressBar(fraction, new Vector2(-1, 0), barLabel);
                    ImGui.PopStyleColor();

                    // Draw cyan projection overlay
                    if (invPts > 0)
                    {
                        float projFrac = Math.Min((float)(current + invPts) / max, 1.0f);
                        DrawBarOverlay(fraction, projFrac);
                    }
                }

                // Tooltip on the progress bar
                if (ImGui.IsItemHovered() && projection is { Items.Count: > 0 })
                {
                    ImGui.BeginTooltip();
                    ImGui.TextColored(CyanText, $"Inventory Turn-ins (+{invPts:N0} points):");
                    ImGui.Separator();
                    foreach (var item in projection.Items)
                    {
                        ImGui.TextUnformatted($"  {item.Quantity}x {item.ItemName}");
                        ImGui.SameLine();
                        ImGui.TextColored(DimText, $"= {item.TotalPoints:N0} pts");
                    }
                    var projected = current + invPts;
                    ImGui.Spacing();
                    if (projected >= max)
                        ImGui.TextColored(GreenBar, $"  → Would complete this job! ({projected:N0}/{max:N0})");
                    else
                        ImGui.TextUnformatted($"  → Projected: {projected:N0} / {max:N0}");
                    ImGui.EndTooltip();
                }

                // Column 4: Points
                ImGui.TableNextColumn();
                if (invPts > 0 && !isCompleted && current < max)
                {
                    ImGui.TextUnformatted($"{current:N0}");
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(CyanText, $"(+{invPts:N0})");
                    ImGui.SameLine(0, 2);
                    ImGui.TextUnformatted($"/ {max:N0}");
                }
                else
                {
                    ImGui.TextUnformatted($"{current:N0} / {max:N0}");
                }
            }
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Draws a semi-transparent cyan rectangle over the last ImGui item (a progress bar)
    /// from <paramref name="fromFrac"/> to <paramref name="toFrac"/> to visualize projected progress.
    /// </summary>
    private static void DrawBarOverlay(float fromFrac, float toFrac)
    {
        toFrac = Math.Min(toFrac, 1.0f);
        if (toFrac <= fromFrac) return;

        var barMin = ImGui.GetItemRectMin();
        var barMax = ImGui.GetItemRectMax();
        float barWidth = barMax.X - barMin.X;

        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(CyanTint);

        drawList.AddRectFilled(
            new Vector2(barMin.X + barWidth * fromFrac, barMin.Y),
            new Vector2(barMin.X + barWidth * toFrac, barMax.Y),
            color);
    }
}
