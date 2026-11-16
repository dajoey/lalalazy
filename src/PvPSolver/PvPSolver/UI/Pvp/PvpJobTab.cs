using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using LalaUiKit;
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
        string key = PickedKey();
        Job job = PickedJob();
        PvpApprovedChrome.Card("job_setup", () =>
        {
            DrawPicker();
            key = PickedKey();
            job = PickedJob();
            PvpApprovedChrome.SeparateNextRow();
            string playing = Player.Available ? Player.Job.ToString() : string.Empty;
            PvpApprovedChrome.Row("editing", $"Editing {key}",
                playing.Length > 0 && playing != key ? $"The character is playing {playing}." : null, 0, 0, null);

            bool enabled = !Service.Config.DisabledJobs.Contains(job);
            if (PvpApprovedChrome.Toggle("usejob", "Use PvP Solver for this job", "Off: PvP Solver takes no action while this job is played, even when it is turned on.", ref enabled))
            {
                _ = enabled ? Service.Config.DisabledJobs.Remove(job) : Service.Config.DisabledJobs.Add(job);
                PvpUi.Save();
            }
        });

        PvpApprovedChrome.Card("job_options", () =>
        {
            PvpUi.Heading($"Options for {key}");
            PvpJobOptions.Draw(job);
        });

        PvpApprovedChrome.Card("job_defensives", () =>
        {
            PvpUi.Heading($"Defensive abilities for {key}");
            DrawDefensives(key);
        });
    }

    // One row per role: its name in a fixed column, the job buttons wrapping to its right. Narrow cards stack the name above.
    private static void DrawPicker()
    {
        string key = PickedKey();
        PvpUi.Heading("Choose a job");
        const float gap = 8;
        float captionWidth = JobKey.Groups.Max(group => PvpApprovedChrome.TextWidth(group.Name, Theme.SecondaryScale)) + 16;
        float available = PvpApprovedChrome.Avail();
        bool stacked = available < 520;
        foreach (JobGroup group in JobKey.Groups)
        {
            Vector2 origin = ImGui.GetCursorScreenPos();
            ImDrawListPtr draw = ImGui.GetWindowDrawList();
            float chipsLeft = stacked ? 0 : captionWidth;
            float y = stacked ? PvpApprovedChrome.LineHeight(Theme.SecondaryScale) + 4 : 0;
            float x = chipsLeft;
            float height = y;
            bool first = true;
            foreach (string job in group.Jobs)
            {
                float w = MathF.Max(56, PvpApprovedChrome.TextWidth(job) + 24);
                if (!first && x + w > available)
                {
                    x = chipsLeft;
                    y += PvpApprovedChrome.BodyControl + gap;
                }

                first = false;
                if (PvpApprovedChrome.Button("job_" + group.Name + "_" + job, job, origin + new Vector2(x, y), new Vector2(w, PvpApprovedChrome.BodyControl),
                        job == key ? PvpApprovedChrome.ButtonStyle.Accent : PvpApprovedChrome.ButtonStyle.Normal))
                {
                    _picked = job;
                }

                x += w + gap;
                height = y + PvpApprovedChrome.BodyControl;
            }

            float captionY = stacked ? 0 : (PvpApprovedChrome.BodyControl - PvpApprovedChrome.LineHeight(Theme.SecondaryScale)) / 2f;
            PvpApprovedChrome.Text(draw, origin + new Vector2(0, captionY), group.Name, Theme.TextSecondary, Theme.SecondaryScale);
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(available, height));
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + gap));
        }

    }

    // ---- defensives ------------------------------------------------------------------------------------------------

    private static void DrawDefensives(string key)
    {
        bool master = Service.Config.PvpDefensivesMaster;
        if (master)
        {
            PvpUi.Help("The master switch (Survival tab) is on.");
        }
        else
        {
            PvpUi.WarningText("The master switch in the Survival tab is off, so none of these is used, including Heart of Corundum, Riddle of Earth, " +
                              "Lady of Crowns, Microcosmos, Meisui and Impalement, which were automatic before.");
        }

        PvpUi.Help("A turned-on ability is used on the character itself when its HP falls below the chosen percentage and the ability is ready.");
        PvpUi.Help(DefensiveTable.WorstCase);

        foreach (DefensiveRow row in DefensiveTable.ForJob(key))
        {
            DrawRow(row);
        }
    }

    private static void DrawRow(DefensiveRow row)
    {
        string rowId = ((int)row.ActionId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Configs config = Service.Config;
        bool enabled = config.DefensiveEnabled(row);
        int min = OptionText.ToPoints(row.MinPercent);
        int points = Math.Clamp(OptionText.ToPoints(config.DefensivePercent(row)), min, 100);
        bool changed = enabled != row.DefaultEnabled || points != OptionText.ToPoints(row.DefaultPercent);
        string defaults = $"(default {(row.DefaultEnabled ? "on" : "off")}, {OptionText.ToPoints(row.DefaultPercent)}%)";
        string help = OptionText.Excerpt(row.Tooltip, 300, "Worst case:");

        if (PvpApprovedChrome.Toggle(rowId, row.Label, help, ref enabled, tip: row.Tooltip, onReset: () =>
            {
                config.SetDefensiveEnabled(row, null);
                config.SetDefensivePercent(row, null);
                PvpUi.Save();
            }, resetVisible: changed))
        {
            config.SetDefensiveEnabled(row, enabled == row.DefaultEnabled ? null : enabled);
            PvpUi.Save();
        }

        if (PvpUi.PercentSlider(rowId + "_percent", "Use when HP is " + OperatorOf(row), defaults, ref points, min, 100, indent: 1, joined: true))
        {
            float ratio = OptionText.FromPoints(points, row.MinPercent, 1f);
            config.SetDefensivePercent(row, OptionText.ToPoints(ratio) == OptionText.ToPoints(row.DefaultPercent) ? null : ratio);
            PvpUi.MarkDirty();
        }
    }

    // Heart of Corundum is used at or below its percentage; every other row strictly below.
    private static string OperatorOf(DefensiveRow row) => ReferenceEquals(row, DefensiveTable.HeartOfCorundum) ? "at or below" : "below";
}
