using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using GluttonyCombo.Core;
using GluttonyCombo.Resources.Localization.UI.Features;
using GluttonyCombo.Window.Tabs;
using LalaUiKit;
using static GluttonyCombo.CustomComboNS.Functions.Jobs;

namespace GluttonyCombo.Window;

// Fork: responsive rounded job destinations used by both preset surfaces.
internal static class GluttonyJobGrid
{
    internal static void Draw(bool pvp)
    {
        var jobs = ConfigWindow.groupedPresets
            .Where(pair => !pvp || pair.Value.Any(preset => preset.IsPvP && !preset.ShouldBeHidden))
            .ToList();
        if (!pvp)
        {
            var regular = jobs.Where(pair => pair.Value[0].JobInfo.Role is not (JobRole.Limited or JobRole.All or JobRole.DoL or JobRole.DoH)).ToList();
            var special = jobs.Where(pair => pair.Value[0].JobInfo.Role is JobRole.Limited or JobRole.All or JobRole.DoL or JobRole.DoH).ToList();
            Widgets.Title("Regular Jobs");
            DrawTable("gc_pve_regular", regular, false);
            Widgets.Title("Limited Jobs and Misc");
            DrawTable("gc_pve_special", special, false);
            return;
        }
        DrawTable("gc_pvp_jobs", jobs, true);
    }

    private static void DrawTable(string id, IReadOnlyList<KeyValuePair<Job, List<PresetStorage.PresetData>>> jobs, bool pvp)
    {
        if (jobs.Count == 0)
        {
            Widgets.InfoBox("Empty — no job presets are available for this surface.", UiState.Empty);
            return;
        }

        if (!GluttonyChrome.BeginTileTable(id, ImGui.GetContentRegionAvail().X, out var columns))
            return;
        foreach (var (job, presets) in jobs)
        {
            ImGui.TableNextColumn();
            var info = presets[0].JobInfo;
            var header = string.IsNullOrEmpty(info.JobShorthand) ? info.JobName : $"{info.JobName} - {info.JobShorthand}";
            var disabled = (pvp ? DisabledJobsPVP : DisabledJobsPVE).Contains(job);
            var selected = pvp ? FeaturesWindow.OpenPvPJob == job : FeaturesWindow.OpenJob == job;
            var size = new Vector2(ImGui.GetContentRegionAvail().X, 52);
            using (ImRaii.Disabled(disabled))
            {
                if (GluttonyChrome.JobButton(ImGui.GetWindowDrawList(), $"{id}_{job}", header,
                        Icons.GetJobIcon(job), ImGui.GetCursorScreenPos(), size, selected, disabled,
                        disabled ? FeaturesUI.Warning_DisabledDueToUpdate : null))
                {
                    if (pvp)
                        FeaturesWindow.OpenPvPJob = job;
                    else
                    {
                        FeaturesWindow.OpenJob = job;
                        PvEFeatures.SetCurrentTab(FeaturesWindow.FeatureTab.Normal);
                    }
                }
                if (!pvp && GluttonyCombo.P.UIHelper.JobControlled(job) is not null)
                    GluttonyCombo.P.UIHelper.ShowIPCControlledIndicatorIfNeeded(job, false, columns > 1);
            }
            ImGui.Dummy(new Vector2(0, Theme.Unit));
        }
        GluttonyChrome.EndTileTable();
    }
}
