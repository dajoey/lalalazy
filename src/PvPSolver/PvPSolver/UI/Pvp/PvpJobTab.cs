using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using RotationSolver.Basic.Configuration;
using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Tab 4, My Job: a job picker, the "use PvP Solver for this job" switch, the job's own options (for any job in
///     any zone, see <see cref="PvpJobOptions"/>) and the job's rows of the defensive table. The job picked here is also
///     the job the per-job targeting setting on the Targeting tab edits.
/// </summary>
internal static class PvpJobTab
{
    private static string? _picked;

    /// <summary>The window was opened: follow the played job again until a job is picked.</summary>
    internal static void OnOpen() => _picked = null;

    /// <summary>The job key shown: the picked job, else the played job, else Paladin.</summary>
    internal static string PickedKey() => _picked ?? JobKey.PickFor(Player.Available ? Player.Job.ToString() : null, "PLD");

    /// <summary>The job shown, as the game's job enum.</summary>
    internal static Job PickedJob() => Enum.TryParse(PickedKey(), out Job job) ? job : Job.PLD;

    public static void Draw()
    {
        DrawPicker();

        string key = PickedKey();
        Job job = PickedJob();
        string playing = Player.Available ? Player.Job.ToString() : string.Empty;
        ImGui.Spacing();
        ImGui.TextColored(PvpUi.Accent, $"Editing: {key}");
        if (playing.Length > 0 && playing != key)
        {
            ImGui.SameLine();
            ImGui.TextColored(PvpUi.Muted, $"(the character is playing {playing})");
        }

        bool enabled = !Service.Config.DisabledJobs.Contains(job);
        using (var id = ImRaii.PushId("usejob"))
        {
            if (PvpUi.Check("Use PvP Solver for this job", ref enabled))
            {
                _ = enabled ? Service.Config.DisabledJobs.Remove(job) : Service.Config.DisabledJobs.Add(job);
                PvpUi.Save();
            }

            PvpUi.Help("Off: PvP Solver takes no action while this job is played, even when it is turned on.");
        }

        PvpUi.Heading($"Options for {key}");
        PvpJobOptions.Draw(job);

        PvpUi.Heading($"Defensive abilities for {key}");
        DrawDefensives(key);
    }

    private static void DrawPicker()
    {
        string key = PickedKey();
        foreach (JobGroup group in JobKey.Groups)
        {
            ImGui.TextUnformatted(group.Name);
            bool first = true;
            foreach (string job in group.Jobs)
            {
                if (first)
                {
                    ImGui.SameLine(90f * PvpUi.Scale);
                    first = false;
                }
                else
                {
                    ImGui.SameLine();
                }

                if (ImGui.Selectable(job + "##pick", job == key, ImGuiSelectableFlags.None, new Vector2(40f * PvpUi.Scale, 0f)))
                {
                    _picked = job;
                }
            }
        }
    }

    // ---- defensives ------------------------------------------------------------------------------------------------

    private static void DrawDefensives(string key)
    {
        bool master = Service.Config.PvpDefensivesMaster;
        if (master)
        {
            PvpUi.Help("The master switch (Survival tab) is on.", 0f);
        }
        else
        {
            PvpUi.WarningText("The master switch in the Survival tab is off, so none of these is used, including Heart of Corundum, Riddle of Earth, " +
                              "Lady of Crowns, Microcosmos, Meisui and Impalement, which were automatic before.");
        }

        PvpUi.Help("A turned-on ability is used on the character itself when its HP falls below the chosen percentage and the ability is ready.", 0f);
        PvpUi.Help(DefensiveTable.WorstCase, 0f);

        foreach (DefensiveRow row in DefensiveTable.ForJob(key))
        {
            DrawRow(row);
        }
    }

    private static void DrawRow(DefensiveRow row)
    {
        using var id = ImRaii.PushId((int)row.ActionId);
        Configs config = Service.Config;
        bool enabled = config.DefensiveEnabled(row);
        if (PvpUi.Check(row.Label, ref enabled))
        {
            config.SetDefensiveEnabled(row, enabled == row.DefaultEnabled ? null : enabled);
            PvpUi.Save();
        }

        PvpUi.Tip(row.Tooltip);

        int min = OptionText.ToPoints(row.MinPercent);
        int points = Math.Clamp(OptionText.ToPoints(config.DefensivePercent(row)), min, 100);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (24f * PvpUi.Scale));
        ImGui.TextUnformatted("Use when HP is " + OperatorOf(row));
        ImGui.SameLine();
        if (PvpUi.PercentSlider("##percent", ref points, min, 100, width: 160f))
        {
            float ratio = OptionText.FromPoints(points, row.MinPercent, 1f);
            config.SetDefensivePercent(row, OptionText.ToPoints(ratio) == OptionText.ToPoints(row.DefaultPercent) ? null : ratio);
            PvpUi.MarkDirty();
        }

        bool changed = enabled != row.DefaultEnabled || points != OptionText.ToPoints(row.DefaultPercent);
        string defaults = $"(default {(row.DefaultEnabled ? "on" : "off")}, {OptionText.ToPoints(row.DefaultPercent)}%)";
        ImGui.SameLine();
        using (var muted = ImRaii.PushColor(ImGuiCol.Text, PvpUi.Muted))
        {
            ImGui.TextUnformatted(defaults);
        }

        if (changed)
        {
            ImGui.SameLine();
            if (PvpUi.ResetButton("reset"))
            {
                config.SetDefensiveEnabled(row, null);
                config.SetDefensivePercent(row, null);
                PvpUi.Save();
            }
        }

        PvpUi.Help(OptionText.Excerpt(row.Tooltip, 300, "Worst case:"));
    }

    // Heart of Corundum is used at or below its percentage; every other row strictly below.
    private static string OperatorOf(DefensiveRow row) => ReferenceEquals(row, DefensiveTable.HeartOfCorundum) ? "at or below" : "below";
}
